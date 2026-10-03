using System.Collections.Generic;
using BeLazy.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace BeLazy.Sleep
{
    // "Go to Bed" - bl-architecture.md 5.1. Each selected pawn is found a
    // target and issued a job independently; one pawn's result never
    // decides another's - the group rule CLAUDE.md states for Do Not Be
    // Lazy, which applies here too.
    public static class SleepOrder
    {
        public static void Execute(IEnumerable<Pawn> pawns)
        {
            List<Pawn> pawnList = pawns as List<Pawn> ?? new List<Pawn>(pawns);

            // One line per press, not per pawn - added 2026-09-28 alongside
            // the alsoClickIfOtherInGroupClicked fix, so a press that still
            // runs more than once (the fix failing, or a future regression)
            // is visible from the log without counting "ordered" lines.
            Logger.Message("Go to Bed pressed for " + pawnList.Count + " pawns.");

            foreach (Pawn pawn in pawnList)
            {
                if (!Qualifies(pawn))
                {
                    continue;
                }

                // ORDERED, bl-architecture.md 5.1a. Fires once per
                // qualifying pawn per press, before anything about their
                // current job changes - TryTakeOrderedJob (5.4) is what
                // ends whatever this line describes.
                Logger.Message(pawn.LabelShortCap + ": Go to Bed ordered - was "
                    + DescribeCurrentActivity(pawn) + ".");

                // One outcome line per pawn per press, added 2026-10-02.
                TrySendToBed(pawn, Logger.OrderIssued(pawn, "Go to Bed", "was " + DescribeCurrentActivity(pawn)));
            }
        }

        // Verse.AI.JobDriver.GetReport() is public virtual - confirmed
        // 2026-09-19 against lib\Assembly-CSharp.dll. It falls back to the
        // job def's own reportString when nothing overrides it. A pawn
        // with no current job (idle) has no driver at all.
        private static string DescribeCurrentActivity(Pawn pawn)
        {
            JobDriver driver = pawn.jobs?.curDriver;
            return driver != null ? driver.GetReport() : "nothing in particular";
        }

        private static bool Qualifies(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                return false;
            }

            if (!pawn.RaceProps.Humanlike || pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (pawn.Drafted)
            {
                // The gizmo is already withheld for a drafted pawn (decision
                // 17) - this is a guard against a queued selection changing
                // between the click and this running.
                return false;
            }

            if (pawn.InMentalState)
            {
                return false;
            }

            // Decision 19: nothing for a pawn already asleep. Whether the
            // order is issued or not, they sleep until they would normally
            // wake or be woken - the drop is deliberate and silent.
            if (pawn.jobs?.curDriver != null && pawn.jobs.curDriver.asleep)
            {
                return false;
            }

            return true;
        }

        private static void TrySendToBed(Pawn pawn, string orderId)
        {
            Job job = BedFinder.FindTarget(pawn);
            if (job == null)
            {
                // The order failed for this pawn. bl-architecture.md 5.1
                // records no on-screen message for a sleep failure - only
                // "Get Rec'd" (decision 6) does.
                Logger.OrderFailedToStart(orderId, pawn, "vanilla found neither a bed nor a ground spot");
                return;
            }

            bool isPlaceBedrollJob = job.def != null && job.def.defName == "PlaceBedroll";

            // A PlaceBedroll job must not be modified - its driver starts
            // its own LayDown job afterwards, and that is the job that
            // would need the flag, which Be Lazy has no handle to (5.1).
            if (!isPlaceBedrollJob)
            {
                job.forceSleep = true;
            }

            // TryTakeOrderedJob sets playerForced itself (decision 8 is
            // free) and enqueues this job ahead of anything the think tree
            // could insert (5.4) - never EndCurrentJob.
            if (!pawn.jobs.TryTakeOrderedJob(job))
            {
                Logger.OrderFailedToStart(orderId, pawn, "TryTakeOrderedJob refused " + job.def?.defName);
                return;
            }

            if (!isPlaceBedrollJob)
            {
                ForceSleepWatch.Watch(pawn);
            }

            Logger.OrderStarted(orderId, pawn, job);
        }
    }
}
