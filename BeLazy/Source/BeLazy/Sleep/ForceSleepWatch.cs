using System.Collections.Generic;
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
    // No logging anywhere in this class. GameComponentTick runs on a
    // 60-tick interval and can walk the whole watch set every time it
    // fires - the exact shape of logging CLAUDE.md's standing rule warns
    // against, so none of it writes to the log at all.
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
                Job job = pawn?.jobs?.curJob;

                if (job == null || !job.forceSleep)
                {
                    // No longer the job Be Lazy gave them - drop silently,
                    // whether it finished, was replaced, or the pawn is
                    // gone.
                    drop.Add(pawn);
                    continue;
                }

                JobDriver driver = pawn.jobs.curDriver;
                if (driver != null && driver.asleep)
                {
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
