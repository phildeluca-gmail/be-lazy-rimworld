using System.Collections.Generic;
using System.Text;
using BeLazy.Core;
using RimWorld;
using Verse;

namespace BeLazy.Alerts
{
    // bl-architecture.md section 12. A vanilla Alert; AlertsReadout finds
    // every Alert subclass by reflection (its allAlertTypesCached field,
    // verified 2026-11-15), so no Harmony patch and no registration.
    // Lists free colonists on the current map with no owned bed, a bed on
    // another map, or a bed in a room Toils_LayDown.ApplyBedThoughts would
    // call outdoors (Room.PsychologicallyOutdoors - the test vanilla uses
    // before giving the SleptOutside thought).
    public class Alert_BedProblems : Alert
    {
        private const int RecomputeIntervalTicks = 250;

        private readonly List<Pawn> culprits = new List<Pawn>();
        private readonly List<string> reasons = new List<string>();
        private int lastComputeTick = -99999;
        private int lastLoggedCount = -1;
        private Map lastMap;

        public Alert_BedProblems()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel()
        {
            return "Bed problems: " + culprits.Count;
        }

        public override TaggedString GetExplanation()
        {
            var sb = new StringBuilder("Colonists on this map with no usable bed:\n");
            for (int i = 0; i < culprits.Count; i++)
            {
                sb.Append("\n  ").Append(culprits[i].LabelShortCap).Append(" - ").Append(reasons[i]);
            }
            return sb.ToString();
        }

        public override AlertReport GetReport()
        {
            if (BeLazyMod.Settings == null || !BeLazyMod.Settings.bedAlertEnabled)
            {
                culprits.Clear();
                return AlertReport.Inactive;
            }

            Map map = Find.CurrentMap;
            int now = Find.TickManager.TicksGame;
            if (map != lastMap || now - lastComputeTick >= RecomputeIntervalTicks || now < lastComputeTick)
            {
                lastMap = map;
                lastComputeTick = now;
                Recompute(map);
            }

            return culprits.Count > 0 ? AlertReport.CulpritsAre(culprits) : AlertReport.Inactive;
        }

        private void Recompute(Map map)
        {
            culprits.Clear();
            reasons.Clear();
            if (map != null)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    // Free colonists and colony slaves; prisoners, guests and quest lodgers out.
                    bool slave = pawn.IsSlaveOfColony;
                    if (!pawn.IsFreeColonist && !slave)
                    {
                        continue;
                    }
                    if ((!slave && pawn.HostFaction != null) || QuestUtility.IsQuestLodger(pawn))
                    {
                        continue;
                    }

                    // Babies sleep in cribs/cots they do not own.
                    if (pawn.DevelopmentalStage == DevelopmentalStage.Baby)
                    {
                        continue;
                    }

                    string reason = Problem(pawn, map);
                    if (reason != null)
                    {
                        culprits.Add(pawn);
                        reasons.Add(reason);
                    }
                }
            }

            // Once per change of count, at most once per recompute (250 ticks).
            if (culprits.Count != lastLoggedCount)
            {
                lastLoggedCount = culprits.Count;
                Logger.Message("Bed problems alert: " + culprits.Count + " on " + (map == null ? "no map" : map.ToString())
                    + (culprits.Count > 0 ? " (" + string.Join(", ", reasons.ConvertAll(r => r).ToArray()) + ")" : ""));
            }
        }

        private static string Problem(Pawn pawn, Map map)
        {
            Building_Bed bed = pawn.ownership?.OwnedBed;
            if (bed == null)
            {
                return "has no owned bed";
            }
            if (bed.Map != map)
            {
                return "owned bed is on another map";
            }
            Room room = bed.GetRoom();
            if (room == null || room.PsychologicallyOutdoors)
            {
                return "owned bed is outdoors";
            }
            return null;
        }
    }
}
