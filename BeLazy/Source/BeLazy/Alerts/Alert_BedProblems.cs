using System.Collections.Generic;
using System.Text;
using BeLazy.Core;
using BeLazy.UI;
using RimWorld;
using UnityEngine;
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
        // Plain-words problem per culprit for the window (no bed / bed on map X / bed outdoors).
        private readonly List<string> windowText = new List<string>();
        // Name per culprit, cached at recompute so the window allocates nothing per frame.
        private readonly List<string> windowNames = new List<string>();
        private int lastComputeTick = -99999;
        private Map lastMap;
        // Last logged set per map, keyed by Map.uniqueID, then by pawn
        // thingIDNumber -> (name, reason). Ints and strings only, so a pawn
        // or map that is gone later holds no reference. Per map, so flipping
        // between maps logs nothing unless that map's set really changed.
        private readonly Dictionary<int, Dictionary<int, KeyValuePair<string, string>>> loggedByMap
            = new Dictionary<int, Dictionary<int, KeyValuePair<string, string>>>();

        public Alert_BedProblems()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public int EntryCount { get { return culprits.Count; } }

        public Pawn EntryPawn(int i)
        {
            return i < culprits.Count ? culprits[i] : null;
        }

        public string EntryName(int i)
        {
            return i < windowNames.Count ? windowNames[i] : "";
        }

        // Rebuilds the list now rather than on the 250-tick clock, so the
        // window sees a bed assigned while the game is paused. Called by the
        // window at most every 30 frames, never per frame.
        public void RefreshNow()
        {
            if (BeLazyMod.Settings == null || !BeLazyMod.Settings.bedAlertEnabled)
            {
                ClearLists();
                return;
            }
            Map map = Find.CurrentMap;
            lastMap = map;
            lastComputeTick = Find.TickManager.TicksGame;
            Recompute(map);
        }

        private void ClearLists()
        {
            culprits.Clear();
            reasons.Clear();
            windowText.Clear();
            windowNames.Clear();
        }

        public string EntryProblem(int i)
        {
            return i < windowText.Count ? windowText[i] : "";
        }

        // Left click opens the window (bl-architecture.md 15.4); right click
        // keeps vanilla's backwards cycle. OnClick is protected virtual on
        // Alert (verified 2026-11-18), so no Harmony patch.
        protected override void OnClick()
        {
            Event e = Event.current;
            if (e != null && e.button == 1)
            {
                base.OnClick();
                return;
            }
            if (Find.WindowStack.IsOpen<BedProblemsWindow>())
            {
                return;
            }
            Find.WindowStack.Add(new BedProblemsWindow(this));
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
                ClearLists();
                loggedByMap.Clear();
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
            ClearLists();
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
                        windowText.Add(WindowText(pawn, map));
                        windowNames.Add(pawn.LabelShortCap ?? ("pawn " + pawn.thingIDNumber));
                    }
                }
            }

            LogChanges(map);
        }

        // Logs only when the set of problem pawns on this map changed since
        // that map's last line (never on an unchanged 250-tick recompute):
        // tick, map, new total, and each pawn ADDED, REMOVED or CHANGED (same
        // pawn, different reason) as "name - reason". Runs from Recompute only.
        private void LogChanges(Map map)
        {
            if (map == null)
            {
                return;
            }

            Dictionary<int, KeyValuePair<string, string>> previous;
            if (!loggedByMap.TryGetValue(map.uniqueID, out previous))
            {
                previous = new Dictionary<int, KeyValuePair<string, string>>();
            }

            var current = new Dictionary<int, KeyValuePair<string, string>>();
            var added = new List<string>();
            var changed = new List<string>();
            for (int i = 0; i < culprits.Count; i++)
            {
                Pawn pawn = culprits[i];
                string name = pawn.LabelShortCap ?? ("pawn " + pawn.thingIDNumber);
                string detail = Detail(pawn, reasons[i], map);
                current[pawn.thingIDNumber] = new KeyValuePair<string, string>(name, detail);
                KeyValuePair<string, string> before;
                if (!previous.TryGetValue(pawn.thingIDNumber, out before))
                {
                    added.Add(name + " - " + detail);
                }
                else if (before.Value != detail)
                {
                    changed.Add(name + " - " + before.Value + " -> " + detail);
                }
            }
            var removed = new List<string>();
            foreach (KeyValuePair<int, KeyValuePair<string, string>> kv in previous)
            {
                if (!current.ContainsKey(kv.Key))
                {
                    removed.Add(kv.Value.Key + " - " + kv.Value.Value);
                }
            }

            loggedByMap[map.uniqueID] = current;
            PruneGoneMaps();
            if (added.Count == 0 && removed.Count == 0 && changed.Count == 0)
            {
                return;
            }
            Core.Logger.Message("Bed problems alert: tick " + Find.TickManager.TicksGame + ", " + map
                + ", total " + culprits.Count
                + ", added " + added.Count + " [" + string.Join("; ", added.ToArray()) + "]"
                + ", removed " + removed.Count + " [" + string.Join("; ", removed.ToArray()) + "]"
                + ", changed " + changed.Count + " [" + string.Join("; ", changed.ToArray()) + "]");
        }

        // Drops the memory of maps that no longer exist (abandoned, destroyed).
        private void PruneGoneMaps()
        {
            if (loggedByMap.Count <= 1 || Find.Maps == null)
            {
                return;
            }
            var live = new HashSet<int>();
            foreach (Map m in Find.Maps)
            {
                live.Add(m.uniqueID);
            }
            var gone = new List<int>();
            foreach (int id in loggedByMap.Keys)
            {
                if (!live.Contains(id))
                {
                    gone.Add(id);
                }
            }
            for (int i = 0; i < gone.Count; i++)
            {
                loggedByMap.Remove(gone[i]);
            }
        }

        // Log-only detail; the alert's own reason text is unchanged.
        private static string Detail(Pawn pawn, string reason, Map map)
        {
            Building_Bed bed = pawn.ownership?.OwnedBed;
            if (bed != null && bed.Map != map)
            {
                return reason + " (bed " + (bed.LabelShort ?? "?") + " " + bed.Position + " on " + (bed.Map == null ? "no map" : bed.Map.ToString()) + ")";
            }
            return reason;
        }

        private static string WindowText(Pawn pawn, Map map)
        {
            Building_Bed bed = pawn.ownership?.OwnedBed;
            if (bed == null)
            {
                return "no bed";
            }
            if (bed.Map != map)
            {
                string where = bed.Map == null ? "no map" : (bed.Map.Parent?.LabelCap.ToString() ?? bed.Map.ToString());
                return "bed on map " + where;
            }
            return "bed outdoors";
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
