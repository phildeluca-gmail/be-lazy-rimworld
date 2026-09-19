using RimWorld;
using Verse;
using Verse.AI;

namespace BeLazy.Sleep
{
    // Step 1 of "Go to Bed" - bl-architecture.md 5.1, built on 3.1, 3.6 and
    // 3.7. Decision 14: Be Lazy runs no bed search of its own. This one call
    // already carries vanilla's full ordering - deathrest casket, medical
    // bed, the pawn's own assigned bed, the love partner's bed, then every
    // unclaimed bed ranked by bedDefsBestToWorst_RestEffectiveness (never by
    // an individual bed's quality, and never favouring a single over a
    // double - that cost is accepted, decision 22). Because
    // JobGiver_GetRest.TryGiveJob is the exact method Use Bedrolls patches,
    // this same call is also what fires that mod's bedroll conversion for
    // step 3 of the chain when nothing else answers (3.7) - without Be Lazy
    // naming, patching or referencing Use Bedrolls anywhere.
    public static class BedFinder
    {
        // The one piece of the chain that is genuinely Be Lazy's own (3.6,
        // step 4 - "go inside and sleep with no bed"): if vanilla's own
        // answer is a bare ground cell, look for an indoor one nearby
        // instead of leaving the pawn in the open. Matches the outer radius
        // TryFindGroundSleepSpotFor itself uses.
        private const int IndoorSearchRadius = 12;

        public static Job FindTarget(Pawn pawn)
        {
            var giver = new JobGiver_GetRest();
            ThinkResult result = giver.TryIssueJobPackage(pawn, default(JobIssueParams));
            Job job = result.Job;
            if (job == null)
            {
                // Vanilla found neither a bed nor a ground spot (3.1, 3.7) -
                // the order has failed for this pawn.
                return null;
            }

            // Use Bedrolls' own PlaceBedroll job - theirs, not ours (3.7).
            // Never named at compile time; matched by defName only, exactly
            // like Do Not Be Lazy's BedrollLoopPatch does for the same mod.
            if (job.def != null && job.def.defName == "PlaceBedroll")
            {
                return job;
            }

            if (job.def == JobDefOf.LayDown && job.targetA.Thing == null)
            {
                // A bare ground cell, not a bed - try for an indoor one.
                if (TryFindIndoorCellNear(pawn, job.targetA.Cell, out IntVec3 indoorCell))
                {
                    job.targetA = indoorCell;
                }
                // else leave the ground cell exactly as vanilla chose it -
                // step 5, "sleep where you are".
            }

            return job;
        }

        private static bool TryFindIndoorCellNear(Pawn pawn, IntVec3 near, out IntVec3 result)
        {
            Map map = pawn.Map;
            if (map == null)
            {
                result = IntVec3.Invalid;
                return false;
            }

            // Same TraverseParms shape as vanilla's own TryRandomClosewalkCellNear (3.6).
            TraverseParms traverseParms = TraverseParms.For(TraverseMode.NoPassClosedDoors)
                .WithFenceblockedOf(pawn);

            return CellFinder.TryFindRandomReachableNearbyCell(
                near, map, IndoorSearchRadius, traverseParms,
                c => c.Standable(map) && !c.IsForbidden(pawn) && pawn.CanReserve(c) && IsIndoors(c, map),
                null, out result);
        }

        // Same test Use Bedrolls itself uses for "indoors" - 3.6.
        private static bool IsIndoors(IntVec3 cell, Map map)
        {
            Room room = RegionAndRoomQuery.RoomAt(cell, map);
            return room != null && !room.PsychologicallyOutdoors;
        }
    }
}
