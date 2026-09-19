using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace BeLazy.Core
{
    // Entry point. RimWorld builds one of these per load and hands it the
    // ModContentPack. Applies the one Harmony patch - Pawn.GetGizmos,
    // postfix, bl-architecture.md section 6 and 3.11 - and hosts the
    // settings.
    public class BeLazyMod : Mod
    {
        public const string HarmonyId = "phildeluca.belazy";

        public static BeLazySettings Settings;

        public BeLazyMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BeLazySettings>();
            Logger.VerboseLogging = Settings.verboseLogging;

            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll();

            // CLAUDE.md's Standard Cargo lesson: log one line on successful
            // patching so "the button never appeared" is answerable from
            // the log, rather than assuming PatchAll silently worked.
            // Colonists are a plain Pawn with zero vanilla subclasses
            // (bl-architecture.md 3.11), so patching Pawn.GetGizmos
            // directly - no by-hand DeclaredOnly resolution - is the right
            // target here.
            MethodInfo getGizmos = AccessTools.Method(typeof(Pawn), nameof(Pawn.GetGizmos));
            bool patched = harmony.GetPatchedMethods().Contains(getGizmos);

            // The one unconditional log line this mod ever writes. Fires
            // once per game process launch, when the mod is constructed -
            // not per save load and not per tick, so its worst case inside
            // any single in-game hour is 1.
            Logger.Loaded(patched
                ? "Loaded. Harmony patch applied to Pawn.GetGizmos - Go to Bed and Get Rec'd are live."
                : "Loaded, but Harmony reports Pawn.GetGizmos was NOT patched - the gizmos will not appear.");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }

        // Must match About.xml's <name> exactly. The Mod options list is
        // drawn from this string, NOT from About.xml - CLAUDE.md records
        // the 2026-09-12 mistake of missing this half of the rename.
        public override string SettingsCategory()
        {
            return "Ketjak's Be Lazy";
        }
    }
}
