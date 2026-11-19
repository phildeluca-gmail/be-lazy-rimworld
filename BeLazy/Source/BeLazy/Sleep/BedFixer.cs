using System.Collections.Generic;
using BeLazy.Alerts;
using RimWorld;
using Verse;
using Verse.AI;

namespace BeLazy.Sleep
{
    // bl-architecture.md section 15.1 (Make arrangements) built on the section 13
    // checks. Plans a bed for every pawn the Bed problems alert flags, then
    // executes a plan. Never walks a pawn, never builds, never bumps anyone:
    // only free slots are taken.
    public static class BedFixer
    {
        public class Row
        {
            public Pawn Pawn;
            public string PawnName;
            public string Problem;      // plain words, as the window shows
            public Building_Bed OldBed; // current claim, may be null
            public string OldBedText;   // "bed, map" for the preview and the log
            public Building_Bed NewBed; // null = not fixed
            public string Why;          // reason for the claim, or why not fixed
        }

        // Plans every flagged pawn. A bed handed to one pawn is counted in
        // 'taken' so two pawns are never given one slot (15.1 step 3).
        public static List<Row> BuildPlan(Alert_BedProblems flagged, Map map)
        {
            var rows = new List<Row>();
            if (map == null)
            {
                return rows;
            }
            // Pawns already planned into each bed, so a later pawn is never
            // given a slot taken by an earlier one, nor a bed shared with a
            // planned pawn it is not willing to share with.
            var taken = new Dictionary<Building_Bed, List<Pawn>>();
            for (int i = 0; i < flagged.EntryCount; i++)
            {
                Pawn pawn = flagged.EntryPawn(i);
                if (pawn == null || pawn.Dead || pawn.Destroyed || !pawn.Spawned || pawn.Map != map)
                {
                    continue;
                }
                Building_Bed old = pawn.ownership?.OwnedBed;
                var row = new Row
                {
                    Pawn = pawn,
                    PawnName = flagged.EntryName(i),
                    Problem = flagged.EntryProblem(i),
                    OldBed = old,
                    OldBedText = old == null ? "none" : (old.LabelShort ?? "bed") + " on " + MapText(old.Map)
                };

                string skip = PawnSkipReason(pawn);
                if (skip != null)
                {
                    row.Why = skip;
                }
                else
                {
                    string reason;
                    Building_Bed bed = PartnerBed(pawn, map, taken, out reason);
                    if (bed == null)
                    {
                        bed = NearestFreeBed(pawn, map, taken);
                        reason = old == null ? "no owned bed: nearest free bed"
                            : (old.Map != map ? "bed was on another map: nearest free bed" : "bed was outdoors: nearest free indoor bed");
                    }
                    if (bed == null)
                    {
                        row.Why = "no free bed";
                    }
                    else
                    {
                        row.NewBed = bed;
                        row.Why = reason;
                        List<Pawn> planned;
                        if (!taken.TryGetValue(bed, out planned))
                        {
                            planned = new List<Pawn>();
                            taken[bed] = planned;
                        }
                        planned.Add(pawn);
                    }
                }
                rows.Add(row);
            }
            return rows;
        }

        public static string MapText(Map m)
        {
            if (m == null)
            {
                return "no map";
            }
            return m.Parent != null ? m.Parent.LabelCap.ToString() : m.ToString();
        }

        // Executes one planned row. Returns null on success, else why it was skipped.
        public static string Execute(Row row, Map map)
        {
            Pawn pawn = row.Pawn;
            Building_Bed bed = row.NewBed;
            if (pawn == null || pawn.Dead || pawn.Destroyed || !pawn.Spawned || pawn.Map != map || pawn.ownership == null)
            {
                return "the pawn is gone or no longer on this map";
            }
            if (bed == null || !bed.Spawned || bed.Map != map)
            {
                return "the bed is gone";
            }
            // The pawn may have been fixed by hand since the plan was drawn.
            Building_Bed cur = pawn.ownership?.OwnedBed;
            if (cur != row.OldBed)
            {
                return "the pawn's bed changed since the preview";
            }
            string why = BedUnfit(pawn, bed, map, null);
            if (why != null)
            {
                return "the bed no longer fits: " + why;
            }
            if (!pawn.ownership.ClaimBedIfNonMedical(bed))
            {
                return "the game refused the claim";
            }
            bed.NotifyRoomAssignedPawnsChanged();
            if (pawn.ownership.OwnedBed != bed)
            {
                return "the claim did not stick (the pawn still does not own the bed)";
            }
            return null;
        }

        private static string PawnSkipReason(Pawn pawn)
        {
            if (pawn.Downed)
            {
                return "the pawn is downed";
            }
            if (pawn.InMentalState)
            {
                return "the pawn is in a mental break";
            }
            return null;
        }

        // Step B of section 13: share a partner's double bed when a slot is free.
        private static Building_Bed PartnerBed(Pawn pawn, Map map, Dictionary<Building_Bed, List<Pawn>> taken, out string reason)
        {
            reason = null;
            if (pawn.relations == null)
            {
                return null;
            }
            var partners = new List<Pawn>();
            foreach (DirectPawnRelation rel in pawn.relations.DirectRelations)
            {
                if (LovePartnerRelationUtility.IsLovePartnerRelation(rel.def)
                    && rel.otherPawn != null && !rel.otherPawn.Dead && rel.otherPawn.Spawned && rel.otherPawn.Map == map)
                {
                    partners.Add(rel.otherPawn);
                }
            }
            // Most liked first.
            partners.Sort((a, b) => pawn.relations.OpinionOf(b).CompareTo(pawn.relations.OpinionOf(a)));
            foreach (Pawn partner in partners)
            {
                Building_Bed bed = partner.ownership?.OwnedBed;
                if (bed == null || bed.Map != map || bed.SleepingSlotsCount < 2 || bed == pawn.ownership?.OwnedBed)
                {
                    continue;
                }
                if (BedUnfit(pawn, bed, map, taken) == null)
                {
                    reason = "shares the double bed of partner " + partner.LabelShort;
                    return bed;
                }
            }
            return null;
        }

        private static Building_Bed NearestFreeBed(Pawn pawn, Map map, Dictionary<Building_Bed, List<Pawn>> taken)
        {
            Building_Bed best = null;
            int bestDist = int.MaxValue;
            foreach (Building_Bed bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == pawn.ownership?.OwnedBed)
                {
                    continue;
                }
                if (BedUnfit(pawn, bed, map, taken) != null)
                {
                    continue;
                }
                int d = (bed.Position - pawn.Position).LengthHorizontalSquared;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = bed;
                }
            }
            return best;
        }

        // Section 13.2 checks. Returns null when the bed fits, else the reason it does not.
        private static string BedUnfit(Pawn pawn, Building_Bed bed, Map map, Dictionary<Building_Bed, List<Pawn>> taken)
        {
            if (!bed.Spawned || bed.Map != map)
            {
                return "not on this map";
            }
            if (bed.Medical)
            {
                return "medical bed";
            }
            // ClaimBedIfNonMedical treats a deathrest casket as a casket
            // assignment, not a bed: OwnedBed is left unchanged.
            if (ModsConfig.BiotechActive && bed.def == ThingDefOf.DeathrestCasket)
            {
                return "deathrest casket";
            }
            if (bed.IsBurning())
            {
                return "burning";
            }
            if (bed.ForPrisoners)
            {
                return "prisoner bed";
            }
            if (bed.ForSlaves != pawn.IsSlave)
            {
                return "slave/colonist mismatch";
            }
            if (bed.Faction != pawn.Faction)
            {
                return "other faction";
            }
            List<Pawn> planned = null;
            if (taken != null)
            {
                taken.TryGetValue(bed, out planned);
            }
            int n = planned == null ? 0 : planned.Count;
            if (bed.OwnersForReading.Count + n >= bed.SleepingSlotsCount)
            {
                return "no free slot";
            }
            if (!RestUtility.CanUseBedEver(pawn, bed.def))
            {
                return "pawn cannot use this bed type";
            }
            CompAssignableToPawn_Bed comp = bed.GetComp<CompAssignableToPawn_Bed>();
            if (comp != null)
            {
                if (!comp.CanAssignTo(pawn).Accepted)
                {
                    return "pawn cannot be assigned";
                }
                if (comp.IdeoligionForbids(pawn))
                {
                    return "ideology forbids";
                }
            }
            // Willing to share with every current owner (13.2 item 4).
            foreach (Pawn owner in bed.OwnersForReading)
            {
                if (!BedUtility.WillingToShareBed(pawn, owner))
                {
                    return "not willing to share";
                }
            }
            if (planned != null)
            {
                foreach (Pawn other in planned)
                {
                    if (!BedUtility.WillingToShareBed(pawn, other))
                    {
                        return "not willing to share";
                    }
                }
            }
            Room room = bed.GetRoom();
            if (room == null || room.PsychologicallyOutdoors)
            {
                return "bed is outdoors";
            }
            if (bed.IsForbidden(pawn))
            {
                return "forbidden";
            }
            if (!pawn.CanReach(bed, PathEndMode.OnCell, Danger.Some))
            {
                return "unreachable";
            }
            return null;
        }
    }
}
