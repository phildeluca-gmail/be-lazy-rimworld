using System.Collections.Generic;
using Verse;

namespace BeLazy.Food
{
    // Throttles the "does this pawn have food it will eat" check used only
    // to grey out the "Go Eat" gizmo - bl-architecture.md 5.x. Pawn.GetGizmos
    // is called far more often than once per game tick (once per frame the
    // command bar is drawn, for every selected pawn), and the underlying
    // check, FoodFinder.HasCandidate, walks every
    // ThingRequestGroup.FoodSourceNotPlantOrTree thing on the map plus the
    // pawn's inventory - the same cost as actually finding a meal. Running
    // that at draw-call frequency would be the "a check that can fire per
    // tick, per frame, per def or per target is a defect until proved
    // otherwise" rule from CLAUDE.md's logging standard, applied to a scan
    // instead of a log line.
    //
    // Cached per pawn, recomputed at most once every CacheIntervalTicks -
    // the same 60-tick interval ForceSleepWatch and JoyWatch already use
    // elsewhere in this mod for their own polls, so the greyed-out reason
    // shown to the player can be at most one second stale. The dictionary is
    // never trimmed of pawns who die or leave the map, the same choice
    // NeedMonitor's own throttle dictionary makes in Do Not Be Lazy - the
    // entry count is bounded by "pawns ever selected while playing," which
    // is not a growth path worth guarding against here.
    public static class FoodAvailabilityCache
    {
        private const int CacheIntervalTicks = 60;

        private struct Entry
        {
            public int computedTick;
            public bool hasFood;
            public string reason;
        }

        private static readonly Dictionary<Pawn, Entry> cache = new Dictionary<Pawn, Entry>();

        public static bool HasFoodItWillEat(Pawn pawn, out string reason)
        {
            int now = Find.TickManager.TicksGame;

            if (cache.TryGetValue(pawn, out Entry entry) && now - entry.computedTick < CacheIntervalTicks)
            {
                reason = entry.reason;
                return entry.hasFood;
            }

            bool hasFood = FoodFinder.HasCandidate(pawn, out string freshReason);
            cache[pawn] = new Entry { computedTick = now, hasFood = hasFood, reason = freshReason };
            reason = freshReason;
            return hasFood;
        }
    }
}
