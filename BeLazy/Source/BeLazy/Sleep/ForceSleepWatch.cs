using System.Collections.Generic;
using BeLazy.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace BeLazy.Sleep
{
    // The GameComponent in bl-architecture.md 5.5. Holds the small set of
    // pawns Be Lazy has put to bed and clears Job.forceSleep once each one
    // is actually asleep, handing the wake decision back to
    // RestUtility.ShouldWakeUp - vanilla's own wake path (3.8, 3.9, 5.6).
    //
    // Decision 18: the guarantee that no pawn sleeps past 100% must survive
    // a save and reload, but the set itself is not saved. Instead it is
    // rebuilt from scratch in LoadedGame() from pawns whose current job
    // already carries forceSleep = true and whose driver is already asleep
    // - both of those already survive a reload correctly on their own
    // (3.13, Job.ExposeData and JobDriver.ExposeData), so there is nothing
    // of Be Lazy's own left to persist.
    //
    // Two of the four order events in bl-architecture.md 5.1a fire here -
    // ENGAGED and, for the one case that can be told apart from it without
    // guessing, INTERRUPTED. GameComponentTick runs on a 60-tick interval
    // and walks the whole watch set every time it fires, but each pawn is
    // logged at most once, at the tick its state actually changes - never
    // per tick, per poll or per pawn per interval, which is what CLAUDE.md's
    // standing rule forbids. See 5.5a for why COMPLETED and the rest of
    // INTERRUPTED are not logged from here.
    public class ForceSleepWatch : GameComponent
    {
        private const int CheckIntervalTicks = 60;

        private readonly HashSet<Pawn> watching = new HashSet<Pawn>();

        // RimWorld constructs this itself - Game.FillComponents walks every
        // loaded assembly for GameComponent subclasses and news them up
        // with the Game. The (Game) constructor has to exist even though it
        // is otherwise unused.
        public ForceSleepWatch(Game game)
        {
        }

        public static void Watch(Pawn pawn)
        {
            Current.Game?.GetComponent<ForceSleepWatch>()?.watching.Add(pawn);
        }

        public override void LoadedGame()
        {
            watching.Clear();

            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                Job job = pawn?.jobs?.curJob;
                JobDriver driver = pawn?.jobs?.curDriver;

                if (job != null && job.forceSleep && driver != null && driver.asleep)
                {
                    watching.Add(pawn);
                }
            }
        }

        public override void GameComponentTick()
        {
            if (watching.Count == 0)
            {
                return;
            }

            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }

            // Snapshot the drop list - the set is mutated after the loop,
            // not during it.
            var drop = new List<Pawn>();

            foreach (Pawn pawn in watching)
            {
                if (pawn == null)
                {
                    drop.Add(pawn);
                    continue;
                }

                Job job = pawn.jobs?.curJob;

                if (job == null || !job.forceSleep)
                {
                    // A pawn stays in this set only until the first poll
                    // that finds it asleep - see the ENGAGED branch below,
                    // which drops it the same tick. So a pawn that reaches
                    // this branch was never seen asleep: the job ended, or
                    // was replaced, before the pawn ever fell asleep.
                    // INTERRUPTED, bl-architecture.md 5.5a. What replaced
                    // it, if anything, is read straight off the pawn's
                    // current job - no guess involved.
                    Logger.Message(pawn.LabelShortCap
                        + ": Go to Bed interrupted before falling asleep - now "
                        + (job?.def?.label ?? "nothing") + ".");
                    drop.Add(pawn);
                    continue;
                }

                JobDriver driver = pawn.jobs.curDriver;
                if (driver != null && driver.asleep)
                {
                    // ENGAGED, bl-architecture.md 5.5a - the pawn has
                    // actually fallen asleep, not merely set off walking.
                    // forceSleep is cleared in the same step as always
                    // (5.5), handing the wake decision to
                    // RestUtility.ShouldWakeUp and dropping the pawn from
                    // this watch. What happens to the job after this point
                    // - a normal wake at 100% rest, or an early one from a
                    // draft, a raid, another order, or a mental break - is
                    // not distinguishable from here; see 5.5a for what that
                    // would need.
                    Logger.Message(pawn.LabelShortCap + ": Go to Bed engaged - asleep.");
                    job.forceSleep = false;
                    drop.Add(pawn);
                }
            }

            foreach (Pawn pawn in drop)
            {
                watching.Remove(pawn);
            }
        }
    }
}
