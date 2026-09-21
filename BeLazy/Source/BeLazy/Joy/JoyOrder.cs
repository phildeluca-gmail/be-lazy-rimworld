using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Logger = BeLazy.Core.Logger;

namespace BeLazy.Joy
{
    // "Get Rec'd" - bl-architecture.md 5.2, decision 21. One ranked pass,
    // every press, for every selected pawn independently. There is no floor
    // pass and no telling a first press from a later one - decision 21
    // deleted both the second pass and the reissue check the earlier design
    // had.
    public static class JoyOrder
    {
        public static void Execute(IEnumerable<Pawn> pawns)
        {
            // Cached once per call, not re-walked per pawn - same reasoning
            // as Do Not Be Lazy caching its sweep-eligible WorkGiverDef list.
            List<JoyGiverDef> givers = DefDatabase<JoyGiverDef>.AllDefsListForReading;

            List<string> failed = null;

            foreach (Pawn pawn in pawns)
            {
                if (!Qualifies(pawn))
                {
                    continue;
                }

                // ORDERED, bl-architecture.md 5.2a - mirrors SleepOrder's
                // ORDERED line. Fires once per qualifying pawn per press,
                // before TryRank replaces whatever they were already doing.
                Logger.Message(pawn.LabelShortCap + ": Get Rec'd ordered - was "
                    + DescribeCurrentActivity(pawn) + ".");

                if (!TryRank(pawn, givers))
                {
                    if (failed == null)
                    {
                        failed = new List<string>();
                    }

                    failed.Add(pawn.LabelShortCap);
                }
            }

            // Decision 6, read together with decision 21: one message for
            // the whole order, naming every pawn who got nothing - not one
            // per pawn and not one per giver.
            if (failed != null && failed.Count > 0)
            {
                Messages.Message(
                    "Get Rec'd found nothing for: " + string.Join(", ", failed) + ".",
                    MessageTypeDefOf.RejectInput, historical: false);
            }
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
                return false;
            }

            if (pawn.InMentalState)
            {
                return false;
            }

            return pawn.needs?.joy != null;
        }

        // Same helper as SleepOrder's - Verse.AI.JobDriver.GetReport() is
        // public virtual, confirmed 2026-09-19 against
        // lib\Assembly-CSharp.dll.
        private static string DescribeCurrentActivity(Pawn pawn)
        {
            JobDriver driver = pawn.jobs?.curDriver;
            return driver != null ? driver.GetReport() : "nothing in particular";
        }

        // Exactly vanilla's own JobGiver_GetJoy.TryGiveJob ranking (3.14):
        // exclude what the pawn is bored of and what CanBeGivenTo refuses,
        // weigh the rest by GetChance * tolerance bias, and try the
        // highest-weighted giver first instead of vanilla's weighted-random
        // pick - deterministic, because a pawn ordered twice in the same
        // state should get the same answer twice. No logging in this loop -
        // it runs per pawn per press, over up to a few dozen JoyGiverDefs,
        // and CLAUDE.md's standing rule is explicit about that shape.
        private static bool TryRank(Pawn pawn, List<JoyGiverDef> givers)
        {
            JoyToleranceSet tolerances = pawn.needs.joy.tolerances;
            var candidates = new List<(JoyGiverDef def, float weight)>();

            for (int i = 0; i < givers.Count; i++)
            {
                JoyGiverDef def = givers[i];
                if (def?.Worker == null || def.joyKind == null)
                {
                    continue;
                }

                if (tolerances.BoredOf(def.joyKind))
                {
                    continue;
                }

                if (!def.Worker.CanBeGivenTo(pawn))
                {
                    continue;
                }

                float weight = def.Worker.GetChance(pawn)
                    * Mathf.Max(0.001f, Mathf.Pow(1f - tolerances[def.joyKind], 5f));
                candidates.Add((def, weight));
            }

            candidates.Sort((a, b) => b.weight.CompareTo(a.weight));

            for (int i = 0; i < candidates.Count; i++)
            {
                Job job = candidates[i].def.Worker.TryGiveJob(pawn);
                if (job == null)
                {
                    continue;
                }

                // Decision 9, mandatory - 3.10. Without this,
                // JoyUtility.JoyTickCheckEnd ends the job the moment the
                // pawn's timetable says Work, because playerForced is not
                // read anywhere in that path.
                job.ignoreJoyTimeAssignment = true;

                if (pawn.jobs.TryTakeOrderedJob(job))
                {
                    // bl-architecture.md 5.2b - watch this pawn so the job
                    // is ended once Need_Joy.CurLevel is full, which some
                    // JobDrivers (RimWorld.JobDriver_Reading, confirmed by
                    // decompiling) otherwise skip for a playerForced job.
                    JoyWatch.Watch(pawn);
                    return true;
                }
            }

            return false;
        }
    }
}
