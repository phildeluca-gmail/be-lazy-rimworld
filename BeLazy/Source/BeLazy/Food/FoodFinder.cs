using RimWorld;
using Verse;
using Verse.AI;

namespace BeLazy.Food
{
    // "Go Eat" food search - bl-architecture.md 5.x, ordered 2026-09-28.
    // Best mood buff to lowest, ties go to nearest - the user's own words,
    // verbatim.
    //
    // Copies the approach Do Not Be Lazy's NeedMonitor.TryFindBestFoodJob
    // already verified against lib\Assembly-CSharp.dll
    // (DoNotBeLazy\Source\DoNotBeLazy\Components\NeedMonitor.cs, section 21)
    // rather than re-deriving it from scratch. Be Lazy does not reference Do
    // Not Be Lazy at all (bl-architecture.md section 8), so this is an
    // independent copy of the verified technique, not a shared call.
    //
    // Verified 2026-09-28 against lib\Assembly-CSharp.dll by reflection,
    // loading every DLL into a dictionary first per CLAUDE.md section 4:
    // - RimWorld.FoodUtility.GetFinalIngestibleDef(Thing, bool) - public static.
    // - RimWorld.FoodUtility.WillEat(Pawn, Thing, Pawn, bool, bool) - public
    //   static, [Extension] - callable as pawn.WillEat(candidate, pawn).
    // - RimWorld.ForbidUtility.IsForbidden(Thing, Pawn) - public static,
    //   [Extension] - callable as candidate.IsForbidden(pawn).
    // - RimWorld.CompRottable.Stage - public property, type RotStage.
    // - Verse.AI.ReservationUtility.CanReserveAndReach(Pawn, LocalTargetInfo,
    //   PathEndMode, Danger, int, int, ReservationLayerDef, bool) - public
    //   static, [Extension] - callable as pawn.CanReserveAndReach(candidate,
    //   PathEndMode.ClosestTouch, Danger.Some).
    // - RimWorld.Building_NutrientPasteDispenser.powerComp (public field) and
    //   .HasEnoughFeedstockInHoppers() (public instance method).
    // - Verse.ThingRequestGroup.FoodSourceNotPlantOrTree - confirmed to
    //   exist as an enum member (this group already includes
    //   Building_NutrientPasteDispenser, per NeedMonitor's own comment).
    // - Verse.JobMaker.MakeJob(JobDef, LocalTargetInfo) - public static. Note
    //   the type is Verse.JobMaker, not Verse.AI.JobMaker.
    // - RimWorld.JobDefOf.Ingest - public static field.
    // - RimWorld.FoodUtility.MoodFromIngesting(Pawn, Thing, ThingDef) and
    //   .WillIngestStackCountOf(Pawn, ThingDef, float) and
    //   .GetNutrition(Pawn, Thing, ThingDef) - all public static.
    public static class FoodFinder
    {
        // Cheap existence check for the gizmo's greyed-out state
        // (FoodAvailabilityCache) - runs the exact same scan as
        // TryFindBestFoodJob below and just discards the built Job. Kept as
        // its own entry point so the call site's intent (checking, not
        // ordering) is clear.
        public static bool HasCandidate(Pawn pawn, out string declineReason)
        {
            Job job = TryFindBestFoodJob(pawn, out _, out _, out declineReason, out _);
            return job != null;
        }

        public static Job TryFindBestFoodJob(Pawn pawn, out int candidateCount,
            out float bestMoodEffect, out string declineReason, out string chosenLabel)
        {
            candidateCount = 0;
            bestMoodEffect = 0f;
            declineReason = null;
            chosenLabel = null;

            if (pawn?.needs?.food == null || pawn.Map == null)
            {
                declineReason = "no food need, or not spawned on a map";
                return null;
            }

            Thing bestThing = null;
            ThingDef bestDef = null;
            float bestScore = float.NegativeInfinity;
            int bestDistance = int.MaxValue;
            int candidates = 0;

            void Consider(Thing candidate, bool inInventory)
            {
                ThingDef def = FoodUtility.GetFinalIngestibleDef(candidate);
                if (def == null || !def.IsNutritionGivingIngestible)
                {
                    return;
                }

                if (!pawn.WillEat(candidate, pawn) || candidate.IsForbidden(pawn))
                {
                    return;
                }

                CompRottable rot = candidate.TryGetComp<CompRottable>();
                if (rot != null && rot.Stage != RotStage.Fresh)
                {
                    return;
                }

                if (inInventory)
                {
                    if (!candidate.IngestibleNow)
                    {
                        return;
                    }
                }
                else if (candidate is Building_NutrientPasteDispenser dispenser)
                {
                    if (dispenser.powerComp == null || !dispenser.powerComp.PowerOn
                        || !dispenser.HasEnoughFeedstockInHoppers()
                        || !dispenser.InteractionCell.Standable(dispenser.Map)
                        || !pawn.CanReach(dispenser.InteractionCell, PathEndMode.OnCell, Danger.Some))
                    {
                        return;
                    }
                }
                else
                {
                    if (!candidate.IngestibleNow
                        || !pawn.CanReserveAndReach(candidate, PathEndMode.ClosestTouch, Danger.Some))
                    {
                        return;
                    }
                }

                candidates++;
                float mood = FoodUtility.MoodFromIngesting(pawn, candidate, def);
                int distance = inInventory ? 0 : (pawn.Position - candidate.Position).LengthManhattan;
                if (mood > bestScore || (mood == bestScore && distance < bestDistance))
                {
                    bestScore = mood;
                    bestDistance = distance;
                    bestThing = candidate;
                    bestDef = def;
                }
            }

            if (pawn.inventory?.innerContainer != null)
            {
                foreach (Thing item in pawn.inventory.innerContainer)
                {
                    Consider(item, true);
                }
            }

            foreach (Thing thing in pawn.Map.listerThings.ThingsMatching(
                ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree)))
            {
                Consider(thing, false);
            }

            candidateCount = candidates;

            if (bestThing == null)
            {
                declineReason = candidates == 0
                    ? "no food this pawn will eat, reach or reserve"
                    : "no viable candidate scored";
                return null;
            }

            bestMoodEffect = bestScore;
            chosenLabel = bestThing.LabelShortCap;
            float nutrition = FoodUtility.GetNutrition(pawn, bestThing, bestDef);
            Job job = JobMaker.MakeJob(JobDefOf.Ingest, bestThing);
            job.count = FoodUtility.WillIngestStackCountOf(pawn, bestDef, nutrition);
            return job;
        }
    }
}
