using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Logger = BeLazy.Core.Logger;

namespace BeLazy.Food
{
    // "Go Eat" - bl-architecture.md 5.x, ordered 2026-09-28. Each selected
    // pawn is found a meal and issued a job independently; one pawn's result
    // never decides another's - the group rule CLAUDE.md states for Do Not
    // Be Lazy, and which SleepOrder and JoyOrder already apply here too.
    //
    // No on-screen message on failure, matching SleepOrder's precedent
    // rather than JoyOrder's decision-6 message: the gizmo is already
    // greyed out with a reason for a pawn with no food (PawnGizmoPatch,
    // FoodAvailabilityCache) before the player can press it for that pawn at
    // all, so a refusal reaching this far is the rarer "conditions changed
    // between draw and click" case, not the expected outcome decision 6 was
    // written for. It is still logged in full, per CLAUDE.md's logging
    // standard.
    public static class FoodOrder
    {
        public static void Execute(IEnumerable<Pawn> pawns)
        {
            List<Pawn> pawnList = pawns as List<Pawn> ?? new List<Pawn>(pawns);

            // One line per press, not per pawn - same pattern SleepOrder and
            // JoyOrder already use, so a merge-related repeat press (5.3's
            // defect, fixed 2026-09-28) is visible without counting
            // "ordered" lines.
            Logger.Message("Go Eat pressed for " + pawnList.Count + " pawns.");

            foreach (Pawn pawn in pawnList)
            {
                if (!Qualifies(pawn))
                {
                    continue;
                }

                string was = DescribeCurrentActivity(pawn);

                Job job = FoodFinder.TryFindBestFoodJob(pawn, out int candidateCount,
                    out float bestMoodEffect, out string declineReason, out string chosenLabel);

                string orderId = Logger.OrderIssued(pawn, "Go Eat", "was " + was);

                if (job == null)
                {
                    Logger.OrderFailedToStart(orderId, pawn, declineReason);
                    // Refused - the event starts and ends in this one line;
                    // nothing about the pawn's job is going to change.
                    Logger.Message(pawn.LabelShortCap + ": Go Eat refused - " + declineReason
                        + " (" + candidateCount + " candidate(s) considered).");
                    continue;
                }

                // ORDERED, following 5.1a's pattern for the other two gizmos.
                Logger.Message(pawn.LabelShortCap + ": Go Eat ordered - was " + was
                    + " - chose " + chosenLabel + ", mood " + bestMoodEffect.ToString("F1")
                    + " (" + candidateCount + " candidate(s) considered).");

                // TryTakeOrderedJob sets playerForced itself (5.4) and
                // enqueues this job ahead of anything the think tree could
                // insert - never EndCurrentJob.
                if (!pawn.jobs.TryTakeOrderedJob(job))
                {
                    Logger.Message(pawn.LabelShortCap + ": Go Eat failed to start - TryTakeOrderedJob refused.");
                    Logger.OrderFailedToStart(orderId, pawn, "TryTakeOrderedJob refused");
                    continue;
                }

                Logger.OrderStarted(orderId, pawn, job);

                FoodWatch.Watch(pawn, job.loadID, pawn.needs.food.CurLevel);
            }
        }

        // Same helper SleepOrder and JoyOrder use -
        // Verse.AI.JobDriver.GetReport() is public virtual, confirmed
        // 2026-09-19 against lib\Assembly-CSharp.dll.
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
                // The gizmo is already withheld for a drafted pawn, same as
                // Go to Bed and Get Rec'd - this is a guard against a queued
                // selection changing between the click and this running.
                return false;
            }

            if (pawn.InMentalState)
            {
                return false;
            }

            return pawn.needs?.food != null;
        }
    }
}
