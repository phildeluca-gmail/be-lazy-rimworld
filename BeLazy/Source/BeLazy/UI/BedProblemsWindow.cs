using BeLazy.Alerts;
using BeLazy.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace BeLazy.UI
{
    // bl-architecture.md section 15.4. Window opened by a left click on the
    // Bed problems alert. Modelled on Show What's Ratty's TatteredPanel:
    // scroll list, one row per flagged pawn, stays open while the player
    // walks down the list, closes by X / Escape or when nothing is flagged.
    // Reads the alert's own lists directly (rebuilt by the alert at most
    // every 250 ticks), so there is no per-frame allocation.
    // Bottom strip (FooterH) holds the Make arrangements button (section 15.1 step 3),
    // which opens FixAllPreviewWindow.
    public class BedProblemsWindow : Window
    {
        private const float RowH = 28f;
        private const float JumpW = 60f;
        private const float NameW = 130f;
        private const float FooterH = 40f;
        private const int RefreshFrames = 30;

        private readonly Alert_BedProblems alert;
        private Vector2 scroll;
        private string closeReason = "closed by player";
        private int lastRefreshFrame = -9999;

        public BedProblemsWindow(Alert_BedProblems alert)
        {
            this.alert = alert;
            doCloseX = true;
            doCloseButton = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            draggable = true;
            forcePause = false;
        }

        public override Vector2 InitialSize => new Vector2(480f, 360f);

        public override void PostOpen()
        {
            base.PostOpen();
            Core.Logger.Message("Bed problems window opened (" + alert.EntryCount + " pawns)");
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Rescan at most every 30 frames (as Show What's Ratty's panel), so
            // a bed assigned while paused, or a pawn that died, left or
            // despawned, drops out without waiting for the 250-tick clock.
            int frame = Time.frameCount;
            if (frame - lastRefreshFrame >= RefreshFrames || frame < lastRefreshFrame)
            {
                lastRefreshFrame = frame;
                alert.RefreshNow();
            }

            int count = alert.EntryCount;
            if (count == 0)
            {
                closeReason = "no pawn flagged any more";
                Close();
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, 32f), "Bed problems");
            Text.Font = GameFont.Small;

            Rect outRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 36f - FooterH);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, count * RowH);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float y = 0f;
            for (int i = 0; i < count; i++)
            {
                Pawn pawn = alert.EntryPawn(i);
                if (pawn != null)
                {
                    Widgets.Label(new Rect(0f, y, NameW, RowH), alert.EntryName(i));
                    Widgets.Label(new Rect(NameW, y, viewRect.width - NameW - JumpW - 4f, RowH), alert.EntryProblem(i));
                    // A pawn that died or was destroyed since the last rescan
                    // gets no button: CameraJumper would have nothing to land on.
                    if (!pawn.Dead && !pawn.Destroyed
                        && Widgets.ButtonText(new Rect(viewRect.width - JumpW, y, JumpW, RowH - 2f), "Jump"))
                    {
                        CameraJumper.TryJumpAndSelect(pawn);
                        Core.Logger.Message("Bed problems window: jumped to " + pawn.LabelShort);
                    }
                }
                y += RowH;
            }
            Widgets.EndScrollView();

            // Width follows the label so a longer name or a larger font never clips it.
            float fixW = Mathf.Max(140f, Text.CalcSize("Make arrangements").x + 24f);
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - 34f, fixW, 32f), "Make arrangements")
                && !Find.WindowStack.IsOpen<FixAllPreviewWindow>())
            {
                Core.Logger.Message("Make arrangements pressed (" + count + " pawns flagged)");
                Find.WindowStack.Add(new FixAllPreviewWindow(alert));
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            Core.Logger.Message("Bed problems window closed (" + closeReason + ")");
        }
    }
}
