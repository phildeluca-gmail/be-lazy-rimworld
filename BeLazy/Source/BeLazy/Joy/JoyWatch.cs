using System.Collections.Generic;
using BeLazy.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace BeLazy.Joy
{
    // Ends a "Get Rec'd" job when the pawn's recreation need is full -
    // bl-architecture.md 5.2b, built 2026-09-19. Same shape as
    // Sleep/ForceSleepWatch.cs: a small unsaved set, held by a
    // GameComponent, checked on a 60-tick interval, one action at the tick
    // a pawn's state actually changes - never every tick, never logged on
    // a poll.
    //
    // WHY THIS EXISTS: RimWorld.JoyUtility.JoyTickCheckEnd (decompiled
    // 2026-09-19) is what normally ends a joy job once
    // Need_Joy.CurLevel > 0.9999f - it calls
    // pawn.jobs.curDriver.EndJobWith(JobCondition.Succeeded) in its
    // JoyTickFullJoyAction.EndJob branch, which is the default. But
    // RimWorld.JobDriver_Reading (decompiled in full 2026-09-19) special-
    // cases job.playerForced: its reading toil passes
    // JoyTickFullJoyAction.None instead, and JoyTickCheckEnd's switch has
    // no case for None, so the full-joy branch is silently skipped. Get
    // Rec'd issues its job through pawn.jobs.TryTakeOrderedJob (5.4),
    // which is what sets playerForced - and forcing is required, because
    // it is what makes the order beat the timetable (decision 8) - so the
    // mod inherits this vanilla behaviour.
    //
    // Reading is the only driver actually decompiled and confirmed to do
    // this, but the fix does not depend on which JobDriver Get Rec'd
    // handed out. There is no single method every joy JobDriver overrides
    // the same way to signal "I am special-casing playerForced" or "I am
    // now performing, not travelling" - auditing each one in turn was
    // already investigated and rejected as too large an undertaking for
    // one session (5.2a). Instead this watches Need_Joy.CurLevel directly -
    // the exact number JoyUtility itself checks, and a property of the
    // need, not of any particular driver - and ends the job through the
    // same public call JoyUtility's own EndJob branch makes,
    // JobDriver.EndJobWith(JobCondition.Succeeded) (decompiled in full
    // 2026-09-19: it is public, and guards pawn.CurJob == job before
    // calling Pawn_JobTracker.EndCurrentJob, so a stale watch entry is
    // harmless). Because the check is on the need rather than the driver,
    // it stops ANY forced joy activity at full need, not only reading.
    //
    // No Harmony patch of any kind, and in particular not the one the
    // user held on Pawn_JobTracker.EndCurrentJob ("Wait for the order").
    // That held patch is about observing every route a job can end
    // through, for every pawn, so INTERRUPTED could be told apart from
    // COMPLETED after the fact. This is different: EndJobWith is a plain
    // public method call, made once, on a job Be Lazy itself issued and is
    // already watching - the same relationship ForceSleepWatch already has
    // to Job.forceSleep.
    public class JoyWatch : GameComponent
    {
        private const int CheckIntervalTicks = 60;

        // JoyUtility.JoyTickCheckEnd's own threshold for "full" - matched
        // here rather than 1f so this fires at exactly the level vanilla
        // itself would have ended the job at, for a driver that let it.
        private const float FullJoyThreshold = 0.9999f;

        private readonly HashSet<Pawn> watching = new HashSet<Pawn>();

        // RimWorld constructs this itself - Game.FillComponents walks
        // every loaded assembly for GameComponent subclasses and news them
        // up with the Game. The (Game) constructor has to exist even
        // though it is otherwise unused.
        public JoyWatch(Game game)
        {
        }

        public static void Watch(Pawn pawn)
        {
            Current.Game?.GetComponent<JoyWatch>()?.watching.Add(pawn);
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

                // The job we issued is gone or replaced - by an
                // interruption, a draft, another order, or (for whichever
                // drivers do NOT special-case playerForced) vanilla's own
                // JoyTickCheckEnd already ending it correctly on its own.
                // Telling those apart needs the held EndCurrentJob patch
                // (5.5a's reasoning applies here too), so this is a silent
                // drop and not an INTERRUPTED line - bl-architecture.md
                // 5.2a.
                if (job == null || !job.playerForced || job.def?.joyKind == null)
                {
                    drop.Add(pawn);
                    continue;
                }

                Need_Joy joy = pawn.needs?.joy;
                if (joy == null)
                {
                    drop.Add(pawn);
                    continue;
                }

                if (joy.CurLevel > FullJoyThreshold)
                {
                    // COMPLETED, bl-architecture.md 5.2a/5.2b - the
                    // recreation need is full. End the job the same way
                    // vanilla's own JoyUtility.JoyTickCheckEnd would have,
                    // for a driver that let it reach that branch.
                    Logger.Message(pawn.LabelShortCap + ": Get Rec'd completed - need full.");
                    pawn.jobs.curDriver?.EndJobWith(JobCondition.Succeeded);
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
