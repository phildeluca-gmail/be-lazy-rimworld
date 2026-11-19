using System.Collections.Generic;
using BeLazy.Alerts;
using BeLazy.Sleep;
using RimWorld;
using UnityEngine;
using Verse;

namespace BeLazy.UI
{
    // bl-architecture.md section 15.1 step 3. Preview opened by Make arrangements in the
    // Bed problems window: one row per flagged pawn with the bed it would get
    // (or "not fixed") and the claim it loses. OK recomputes the plan, runs
    // every row that still holds and logs the rest; Cancel changes nothing.
    public class FixAllPreviewWindow : Window
    {
        private const float RowH = 44f;
        private const float FooterH = 40f;

        private readonly Alert_BedProblems alert;
        private readonly Map map;
        private readonly List<BedFixer.Row> rows;
        private Vector2 scroll;
        private string closeReason = "closed by player";

        public FixAllPreviewWindow(Alert_BedProblems alert)
        {
            this.alert = alert;
            map = Find.CurrentMap;
            rows = BedFixer.BuildPlan(alert, map);
            doCloseX = true;
            doCloseButton = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            forcePause = false;
        }

        public override Vector2 InitialSize => new Vector2(560f, 420f);

        public override void PostOpen()
        {
            base.PostOpen();
            Core.Logger.Message("Make arrangements preview opened: " + rows.Count + " pawns, "
                + CountFixable() + " with a bed to claim, on " + map);
        }

        private int CountFixable()
        {
            int n = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].NewBed != null)
                {
                    n++;
                }
            }
            return n;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, 32f), "Make arrangements");
            Text.Font = GameFont.Small;

            Rect outRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 36f - FooterH);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, rows.Count * RowH);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float y = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                BedFixer.Row r = rows[i];
                string line1 = r.PawnName + (r.IsKicked ? " (kicked)" : "") + ": " + (r.NewBed != null
                    ? "new bed " + (r.NewBed.LabelShort ?? "bed") + " " + r.NewBed.Position
                    : "not fixed, " + r.Why);
                string line2 = "claim lost: " + (r.OldBed != null && (r.NewBed != null || r.IsKicked) ? r.OldBedText : "none");
                if (r.KickRow != null)
                {
                    line2 += "; kicks " + r.KickRow.PawnName + " to "
                        + (r.KickRow.NewBed != null ? (r.KickRow.NewBed.LabelShort ?? "bed") + " " + r.KickRow.NewBed.Position : "no free bed");
                }
                Widgets.Label(new Rect(0f, y, viewRect.width, RowH / 2f), line1);
                Widgets.Label(new Rect(12f, y + RowH / 2f - 2f, viewRect.width - 12f, RowH / 2f), line2);
                y += RowH;
            }
            Widgets.EndScrollView();

            float bw = 120f;
            float by = inRect.yMax - 34f;
            if (Widgets.ButtonText(new Rect(inRect.x, by, bw, 32f), "OK"))
            {
                Apply();
                closeReason = "OK pressed";
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.xMax - bw, by, bw, 32f), "Cancel"))
            {
                Core.Logger.Message("Make arrangements cancelled by the player; nothing changed");
                closeReason = "cancelled";
                Close();
            }
        }

        private void Apply()
        {
            // The alert lists the current map only; if the player switched maps
            // since the preview opened, the recomputed plan would be for another map.
            if (Find.CurrentMap != map)
            {
                Core.Logger.Message("Make arrangements: the current map changed since the preview opened ("
                    + BedFixer.MapText(map) + " -> " + BedFixer.MapText(Find.CurrentMap) + "); nothing changed");
                return;
            }
            // Recompute now (15.1 step 3): the world may have moved since the preview opened.
            alert.RefreshNow();
            List<BedFixer.Row> fresh = BedFixer.BuildPlan(alert, map);
            int changed = 0;
            int notFixed = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                BedFixer.Row shown = rows[i];
                if (shown.NewBed == null)
                {
                    notFixed++;
                    Core.Logger.Message("Make arrangements: " + shown.PawnName + " not fixed - " + shown.Why
                        + " (was: " + shown.Problem + ", " + shown.OldBedText + ")");
                    continue;
                }
                BedFixer.Row now = null;
                for (int j = 0; j < fresh.Count; j++)
                {
                    if (fresh[j].Pawn == shown.Pawn)
                    {
                        now = fresh[j];
                        break;
                    }
                }
                if (now == null || now.NewBed != shown.NewBed)
                {
                    notFixed++;
                    Core.Logger.Message("Make arrangements: " + shown.PawnName + " skipped - the planned bed "
                        + (shown.NewBed.LabelShort ?? "bed") + " " + shown.NewBed.Position
                        + " no longer holds ("
                        + (now == null ? "the pawn is no longer flagged" : "the plan changed")
                        + "); nothing changed for this pawn");
                    continue;
                }
                string skip = BedFixer.Execute(shown, map);
                if (skip != null)
                {
                    notFixed++;
                    Core.Logger.Message("Make arrangements: " + shown.PawnName + " skipped - " + skip);
                    continue;
                }
                changed++;
                Core.Logger.Message("Make arrangements: " + shown.PawnName + " claimed " + (shown.NewBed.LabelShort ?? "bed")
                    + " " + shown.NewBed.Position + " on " + BedFixer.MapText(map) + " - " + shown.Why
                    + "; old bed: " + shown.OldBedText);
            }
            Core.Logger.Message("Make arrangements finished: " + changed + " changed, " + notFixed + " not fixed");
            alert.RefreshNow();
        }

        public override void PostClose()
        {
            base.PostClose();
            Core.Logger.Message("Make arrangements preview closed (" + closeReason + ")");
        }
    }
}
