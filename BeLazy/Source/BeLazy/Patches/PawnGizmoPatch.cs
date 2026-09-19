using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using BeLazy.Joy;
using BeLazy.Sleep;

namespace BeLazy.Patches
{
    // Patches Verse.Pawn.GetGizmos to add "Go to Bed" and "Get Rec'd" for
    // any selected, player-controlled, undrafted humanlike pawn. Postfix,
    // per CLAUDE.md section 4.
    //
    // bl-architecture.md 3.11: reflection over Assembly-CSharp found zero
    // subclasses of Pawn in vanilla, so for colonists a postfix on
    // Pawn.GetGizmos itself is enough - there is no derived override to
    // miss, unlike Standard Cargo's VehiclePawn. If Be Lazy ever needs a
    // gizmo on something that is not a plain Pawn, that is a decision of
    // its own and needs the DeclaredOnly treatment CLAUDE.md describes.
    //
    // Every selected pawn produces one copy of each gizmo; groupKey merges
    // them into one row in the UI (5.3), and each action reads the live
    // selection rather than __instance, so it doesn't matter which merged
    // copy was clicked, and dropping one pawn from an order never removes
    // the gizmo for the rest of the selection.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class PawnGizmoPatch
    {
        private const int GoToBedGroupKey = 0x424C5A31; // "BLZ1", just needs to be ours
        private const int GetRecdGroupKey = 0x424C5A32; // "BLZ2"

        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
        {
            foreach (var gizmo in gizmos)
            {
                yield return gizmo;
            }

            if (!Offered(__instance))
            {
                yield break;
            }

            // Icons are two existing, already-verified-safe generic button
            // textures carried over from Standard Cargo for Vehicles'
            // VehicleGizmoPatch.cs, loaded with reportFailure: false. No
            // themed icon exists in TexCommand for sleep or recreation, and
            // bl-architecture.md gives no icon instruction of its own.
            yield return new Command_Action
            {
                defaultLabel = "Go to Bed",
                defaultDesc = "Sends the selected pawns to bed now, beating the timetable. Each pawn finds the best bed it can on its own, the same way vanilla would, and sleeps until 100% rest or until something would normally wake it.",
                icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Buttons/OpenStatsReport", false),
                groupKey = GoToBedGroupKey,
                action = () => SleepOrder.Execute(SelectedColonists())
            };

            yield return new Command_Action
            {
                defaultLabel = "Get Rec'd",
                defaultDesc = "Sends the selected pawns to do the best recreation each can find right now, ranked the same way vanilla ranks a pawn's own choice. A pawn for whom nothing answers is named on screen.",
                icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Buttons/Rename", false),
                groupKey = GetRecdGroupKey,
                action = () => JoyOrder.Execute(SelectedColonists())
            };
        }

        // decision 17: neither gizmo appears for a drafted pawn at all -
        // absent, not greyed.
        private static bool Offered(Pawn pawn)
        {
            return pawn != null
                && pawn.Spawned
                && pawn.Faction == Faction.OfPlayer
                && pawn.RaceProps.Humanlike
                && !pawn.Drafted;
        }

        private static List<Pawn> SelectedColonists()
        {
            return Find.Selector.SelectedPawns
                .Where(p => p != null && p.Faction == Faction.OfPlayer && p.RaceProps.Humanlike)
                .ToList();
        }
    }
}
