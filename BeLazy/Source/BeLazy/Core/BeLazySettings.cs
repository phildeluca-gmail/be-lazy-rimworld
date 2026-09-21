using UnityEngine;
using Verse;

namespace BeLazy.Core
{
    // One setting, per bl-architecture.md section 6 and section 8 ("It does
    // not add a settings screen beyond verbose logging"). Same shape as
    // RimWarOdds/Core/RimWarOddsSettings.cs.
    public class BeLazySettings : ModSettings
    {
        public bool verboseLogging = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", true);

            // Same trap RimWar Odds' settings hit - a saved "on" reads as
            // off until the settings window is opened unless it is synced
            // on load too.
            Logger.VerboseLogging = verboseLogging;
        }

        public void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("Verbose logging", ref verboseLogging,
                "Writes [BeLazy] lines to the log. Off unless you're chasing something.");
            Logger.VerboseLogging = verboseLogging;

            listing.End();
        }
    }
}
