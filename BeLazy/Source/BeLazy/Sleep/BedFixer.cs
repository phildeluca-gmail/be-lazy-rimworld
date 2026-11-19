using System.Collections.Generic;
using BeLazy.Alerts;
using RimWorld;
using Verse;
using Verse.AI;

namespace BeLazy.Sleep
{
    // bl-architecture.md section 15.1 (Make arrangements) built on the section 13
    // checks, with the section 15.5 kick rules. Plans a bed for every pawn the
    // Bed problems alert flags, then executes a plan. Never walks a pawn, never
    // builds. Free slots are taken; the only claim ever removed from another
    // pawn is a non-partner's, when a partner needs the slot (15.5).
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
            public Pawn Kick;           // non-partner this row's pawn kicks out of NewBed
            public Row KickRow;         // the kicked pawn's own row (where it goes)
            public bool IsKicked;       // this row is a pawn kicked out of OldBed
            public Row KickedBy;        // for a kicked row: the row that kicks it
            public Row MovesWith;       // for a partner moving in: the row it moves in with
            public bool Done;           // set by Execute on success
        }

        // Plans every flagged pawn (or only 'only' when given). A bed handed to
        // one pawn is counted in 'taken' so two pawns are never given one slot
        // (15.1 step 3). A kicked pawn is added as its own row, with the
        // nearest free bed (15.5).
        public static List<Row> BuildPlan(Alert_BedProblems flagged, Map map, Pawn only = null)
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
            var kicked = new HashSet<Pawn>();
            var plannedBed = new Dictionary<Pawn, Building_Bed>();
            var done = new HashSet<Pawn>();
            var flaggedSet = new HashSet<Pawn>();
            var flaggedInfo = new Dictionary<Pawn, string[]>();
            for (int i = 0; i < flagged.EntryCount; i++)
            {
                Pawn fp = flagged.EntryPawn(i);
                if (fp != null)
                {
                    flaggedSet.Add(fp);
                    flaggedInfo[fp] = new string[] { flagged.EntryName(i), flagged.EntryProblem(i) };
                }
            }
            for (int i = 0; i < flagged.EntryCount; i++)
            {
                Pawn pawn = flagged.EntryPawn(i);
                if (pawn == null || pawn.Dead || pawn.Destroyed || !pawn.Spawned || pawn.Map != map)
                {
                    continue;
                }
                if ((only != null && pawn != only) || done.Contains(pawn))
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
                    OldBedText = BedText(old)
                };
                rows.Add(row);
                done.Add(pawn);

                string skip = PawnSkipReason(pawn);
                if (skip != null)
                {
                    row.Why = skip;
                    continue;
                }
                string reason;
                Pawn kick = null;
                Pawn mover = null;
                Building_Bed bed = PartnerBed(pawn, map, taken, kicked, plannedBed, out reason);
                if (bed == null)
                {
                    bed = PartnerKickBed(pawn, map, taken, kicked, plannedBed, flaggedSet, out kick, out reason);
                }
                if (bed == null)
                {
                    bed = CoupleBed(pawn, map, taken, kicked, done, flaggedSet, out kick, out mover, out reason);
                }
                if (bed == null)
                {
                    bed = NearestFreeBed(pawn, map, taken, kicked);
                    reason = old == null ? "no owned bed: nearest free bed"
                        : (old.Map != map ? "bed was on another map: nearest free bed" : "bed was outdoors: nearest free indoor bed");
                }
                if (bed == null)
                {
                    row.Why = "no free bed";
                    continue;
                }
                row.NewBed = bed;
                row.Why = reason;
                AddTaken(taken, bed, pawn);
                plannedBed[pawn] = bed;

                if (mover != null)
                {
                    string[] info;
                    flaggedInfo.TryGetValue(mover, out info);
                    Building_Bed moverOld = mover.ownership?.OwnedBed;
                    rows.Add(new Row
                    {
                        Pawn = mover,
                        PawnName = info != null ? info[0] : mover.LabelShort,
                        Problem = info != null ? info[1] : "moves in with partner",
                        OldBed = moverOld,
                        OldBedText = BedText(moverOld),
                        NewBed = bed,
                        Why = "moves in with partner " + pawn.LabelShort,
                        MovesWith = row
                    });
                    AddTaken(taken, bed, mover);
                    plannedBed[mover] = bed;
                    done.Add(mover);
                    // The mover's old claim is released, so that bed counts as
                    // free for a pawn kicked below (15.5 item 4). The row
                    // order (pawn, mover, kicked) makes it free by then.
                    kicked.Add(mover);
                }
                if (kick != null)
                {
                    kicked.Add(kick);
                    row.Kick = kick;
                    var kr = new Row
                    {
                        Pawn = kick,
                        PawnName = kick.LabelShort,
                        Problem = "kicked out by " + pawn.LabelShort,
                        OldBed = bed,
                        OldBedText = BedText(bed),
                        IsKicked = true,
                        KickedBy = row
                    };
                    Building_Bed kb = NearestFreeBed(kick, map, taken, kicked);
                    if (kb == null)
                    {
                        kr.Why = "no free bed";
                    }
                    else
                    {
                        kr.NewBed = kb;
                        kr.Why = "kicked out by " + pawn.LabelShort + ": nearest free bed";
                        AddTaken(taken, kb, kick);
                        plannedBed[kick] = kb;
                    }
                    row.KickRow = kr;
                    rows.Add(kr);
                    done.Add(kick);
                }
            }
            return rows;
        }

        private static void AddTaken(Dictionary<Building_Bed, List<Pawn>> taken, Building_Bed bed, Pawn pawn)
        {
            List<Pawn> planned;
            if (!taken.TryGetValue(bed, out planned))
            {
                planned = new List<Pawn>();
                taken[bed] = planned;
            }
            planned.Add(pawn);
        }

        private static string BedText(Building_Bed bed)
        {
            return bed == null ? "none" : (bed.LabelShort ?? "bed") + " on " + MapText(bed.Map);
        }

        public static string MapText(Map m)
        {
            if (m == null)
            {
                return "no map";
            }
            return m.Parent != null ? m.Parent.LabelCap.ToString() : m.ToString();
        }

        private static bool IsLovePartner(Pawn a, Pawn b)
        {
            if (a == null || b == null || a.relations == null)
            {
                return false;
            }
            foreach (DirectPawnRelation rel in a.relations.DirectRelations)
            {
                if (rel.otherPawn == b && LovePartnerRelationUtility.IsLovePartnerRelation(rel.def))
                {
                    return true;
                }
            }
            return false;
        }

        private static List<Pawn> PartnersOf(Pawn pawn, Map map)
        {
            var partners = new List<Pawn>();
            if (pawn.relations == null)
            {
                return partners;
            }
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
            return partners;
        }

        // A pawn who may be kicked out of a bed for 'pawn' and 'partner': on
        // this map, not already moved, not flagged itself, partner of neither.
        private static bool KickableOwner(Pawn owner, Pawn pawn, Pawn partner, Map map, HashSet<Pawn> kicked, HashSet<Pawn> flaggedSet)
        {
            return owner != pawn && owner != partner && !owner.Dead && !owner.Destroyed && owner.Spawned && owner.Map == map
                && !kicked.Contains(owner) && !flaggedSet.Contains(owner)
                && !IsLovePartner(owner, pawn) && !IsLovePartner(owner, partner)
                && !IsLovePartner(pawn, owner) && !IsLovePartner(partner, owner);
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
            if (row.IsKicked)
            {
                if (row.KickedBy == null || !row.KickedBy.Done)
                {
                    return "the kick did not happen";
                }
                if (cur != null)
                {
                    return "the pawn's bed changed since the preview";
                }
            }
            else if (cur != row.OldBed)
            {
                return "the pawn's bed changed since the preview";
            }
            // A partner moves in only together with the pawn it moves in with.
            if (row.MovesWith != null && !row.MovesWith.Done)
            {
                return "the partner it moves in with was not assigned";
            }
            Pawn victim = row.Kick;
            bool kicking = victim != null && victim.ownership != null && victim.ownership.OwnedBed == bed;
            HashSet<Pawn> kickSet = kicking ? new HashSet<Pawn> { victim } : null;
            string why = BedUnfit(pawn, bed, map, null, kickSet);
            if (why != null)
            {
                return "the bed no longer fits: " + why;
            }
            if (kicking)
            {
                victim.ownership.UnclaimBed();
                bed.NotifyRoomAssignedPawnsChanged();
            }
            if (!pawn.ownership.ClaimBedIfNonMedical(bed) || pawn.ownership.OwnedBed != bed)
            {
                if (kicking)
                {
                    // Put the kicked pawn back so nothing is lost on a failed claim.
                    victim.ownership.ClaimBedIfNonMedical(bed);
                    bed.NotifyRoomAssignedPawnsChanged();
                    return "the game refused the claim (kicked pawn restored)";
                }
                return "the game refused the claim or it did not stick";
            }
            bed.NotifyRoomAssignedPawnsChanged();
            row.Done = true;
            if (kicking)
            {
                Core.Logger.Message("Make arrangements: KICK " + victim.LabelShort + " out of " + (bed.LabelShort ?? "bed")
                    + " " + bed.Position + " on " + MapText(map) + " for " + pawn.LabelShort + " (" + row.Why + "); "
                    + victim.LabelShort + " goes to "
                    + (row.KickRow != null && row.KickRow.NewBed != null
                        ? (row.KickRow.NewBed.LabelShort ?? "bed") + " " + row.KickRow.NewBed.Position
                        : "no free bed"));
            }
            return null;
        }

        // Fix for one pawn only (per-person Fix button in the Bed problems
        // window). No preview; kicks apply and are logged like the full run.
        public static string RunFor(Alert_BedProblems alert, Map map, Pawn pawn)
        {
            alert.RefreshNow();
            List<Row> rows = BuildPlan(alert, map, pawn);
            Core.Logger.Message("Make arrangements (one pawn) started for " + pawn.LabelShort + ": " + rows.Count + " rows planned");
            int changed = 0;
            int notFixed = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                if (r.NewBed == null)
                {
                    notFixed++;
                    Core.Logger.Message("Make arrangements: " + r.PawnName + " not fixed - " + r.Why
                        + " (was: " + r.Problem + ", " + r.OldBedText + ")");
                    continue;
                }
                string skip = Execute(r, map);
                if (skip != null)
                {
                    notFixed++;
                    Core.Logger.Message("Make arrangements: " + r.PawnName + " skipped - " + skip);
                    continue;
                }
                changed++;
                Core.Logger.Message("Make arrangements: " + r.PawnName + " claimed " + (r.NewBed.LabelShort ?? "bed")
                    + " " + r.NewBed.Position + " on " + MapText(map) + " - " + r.Why + "; old bed: " + r.OldBedText);
            }
            Core.Logger.Message("Make arrangements (one pawn) finished for " + pawn.LabelShort + ": " + changed + " changed, " + notFixed + " not fixed");
            alert.RefreshNow();
            return changed + " changed, " + notFixed + " not fixed";
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
        // The partner's bed may be one it is only planned into (couple move-in).
        private static Building_Bed PartnerBed(Pawn pawn, Map map, Dictionary<Building_Bed, List<Pawn>> taken, HashSet<Pawn> kicked,
            Dictionary<Pawn, Building_Bed> plannedBed, out string reason)
        {
            reason = null;
            foreach (Pawn partner in PartnersOf(pawn, map))
            {
                Building_Bed bed;
                if (!plannedBed.TryGetValue(partner, out bed))
                {
                    bed = partner.ownership?.OwnedBed;
                }
                if (bed == null || bed.Map != map || bed.SleepingSlotsCount < 2 || bed == pawn.ownership?.OwnedBed)
                {
                    continue;
                }
                if (BedUnfit(pawn, bed, map, taken, kicked) == null)
                {
                    reason = "shares the double bed of partner " + partner.LabelShort;
                    return bed;
                }
            }
            return null;
        }

        // 15.5 (a): the partner's double bed is full and another slot is held
        // by a non-partner. Kick that non-partner and take the slot.
        private static Building_Bed PartnerKickBed(Pawn pawn, Map map, Dictionary<Building_Bed, List<Pawn>> taken, HashSet<Pawn> kicked,
            Dictionary<Pawn, Building_Bed> plannedBed, HashSet<Pawn> flaggedSet, out Pawn kick, out string reason)
        {
            kick = null;
            reason = null;
            foreach (Pawn partner in PartnersOf(pawn, map))
            {
                Building_Bed bed = partner.ownership?.OwnedBed;
                if (bed == null || bed.Map != map || bed.SleepingSlotsCount < 2 || bed == pawn.ownership?.OwnedBed
                    || plannedBed.ContainsKey(partner) || taken.ContainsKey(bed))
                {
                    continue;
                }
                foreach (Pawn owner in bed.OwnersForReading)
                {
                    if (!KickableOwner(owner, pawn, partner, map, kicked, flaggedSet))
                    {
                        continue;
                    }
                    var tmp = new HashSet<Pawn>(kicked) { owner };
                    if (BedUnfit(pawn, bed, map, taken, tmp) == null)
                    {
                        kick = owner;
                        reason = "kicks non-partner " + owner.LabelShort + " out of the double bed of partner " + partner.LabelShort;
                        return bed;
                    }
                }
            }
            return null;
        }

        // 15.5 (b): a couple moves into a 2+ slot bed. An unowned one is
        // preferred; otherwise a bed held by one lone non-partner, who is
        // kicked. The partner must fit too; a partner who is not flagged moves
        // only out of a single bed (or none).
        private static Building_Bed CoupleBed(Pawn pawn, Map map, Dictionary<Building_Bed, List<Pawn>> taken, HashSet<Pawn> kicked,
            HashSet<Pawn> done, HashSet<Pawn> flaggedSet, out Pawn kick, out Pawn mover, out string reason)
        {
            kick = null;
            mover = null;
            reason = null;
            foreach (Pawn partner in PartnersOf(pawn, map))
            {
                if (done.Contains(partner) || partner.Downed || partner.InMentalState || partner.ownership == null)
                {
                    continue;
                }
                Building_Bed partnerOwn = partner.ownership.OwnedBed;
                if (!flaggedSet.Contains(partner) && partnerOwn != null && partnerOwn.SleepingSlotsCount >= 2)
                {
                    continue;
                }
                Building_Bed best = null;
                Pawn bestVictim = null;
                long bestScore = long.MaxValue;
                foreach (Building_Bed bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
                {
                    if (bed.SleepingSlotsCount < 2 || bed == pawn.ownership?.OwnedBed || bed == partnerOwn || taken.ContainsKey(bed))
                    {
                        continue;
                    }
                    Pawn victim = null;
                    int owners = 0;
                    foreach (Pawn o in bed.OwnersForReading)
                    {
                        if (kicked.Contains(o))
                        {
                            continue;
                        }
                        owners++;
                        victim = o;
                    }
                    if (owners > 1)
                    {
                        continue;
                    }
                    var tmp = new HashSet<Pawn>(kicked);
                    if (owners == 1)
                    {
                        if (!KickableOwner(victim, pawn, partner, map, kicked, flaggedSet))
                        {
                            continue;
                        }
                        tmp.Add(victim);
                    }
                    if (BedUnfit(pawn, bed, map, taken, tmp) != null)
                    {
                        continue;
                    }
                    // Would the partner fit beside the pawn? Count the pawn in 'taken' for the check.
                    AddTaken(taken, bed, pawn);
                    string partnerWhy = BedUnfit(partner, bed, map, taken, tmp);
                    taken.Remove(bed);
                    if (partnerWhy != null)
                    {
                        continue;
                    }
                    long score = (long)(bed.Position - pawn.Position).LengthHorizontalSquared + (owners * 1000000000L);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = bed;
                        bestVictim = owners == 1 ? victim : null;
                    }
                }
                if (best != null)
                {
                    kick = bestVictim;
                    mover = partner;
                    reason = kick != null
                        ? "moves in with partner " + partner.LabelShort + " in a double bed, kicking non-partner " + kick.LabelShort
                        : "moves in with partner " + partner.LabelShort + " in a free double bed";
                    return best;
                }
            }
            return null;
        }

        private static Building_Bed NearestFreeBed(Pawn pawn, Map map, Dictionary<Building_Bed, List<Pawn>> taken, HashSet<Pawn> kicked)
        {
            Building_Bed best = null;
            int bestDist = int.MaxValue;
            foreach (Building_Bed bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == pawn.ownership?.OwnedBed)
                {
                    continue;
                }
                if (BedUnfit(pawn, bed, map, taken, kicked) != null)
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

        // Section 13.2 checks. Returns null when the bed fits, else the reason it
        // does not. 'kicked' owners are treated as already gone.
        private static string BedUnfit(Pawn pawn, Building_Bed bed, Map map, Dictionary<Building_Bed, List<Pawn>> taken, HashSet<Pawn> kicked)
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
            int ownerCount = 0;
            foreach (Pawn o in bed.OwnersForReading)
            {
                if (kicked == null || !kicked.Contains(o))
                {
                    ownerCount++;
                }
            }
            if (ownerCount + n >= bed.SleepingSlotsCount)
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
                if (kicked != null && kicked.Contains(owner))
                {
                    continue;
                }
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
