# Review of ketjak_be_lazy_complete_bug_report-gem.md

Opus, 2026-11-17. Checked against Be Lazy source at HEAD and `lib\Assembly-CSharp.dll` (ilspycmd decompile, game 1.5.4409). The uncommitted drug-guard diff touches only `JoyOrder.cs` and `SleepOrder.cs` (+7 lines each); line numbers below are HEAD's. No claim depends on that diff.

Tally: 8 confirmed (BUG-04 only in part), 2 wrong, 0 not verified. One extra defect found that the report missed (item 11).

## Verdicts

1. **BUG-01, dispenser nutrition is 0 - WRONG.** `FoodUtility.GetNutrition` (decompile 1375-1386) returns `foodDef`'s stat when `foodSource.def != foodDef`, so a dispenser gives paste nutrition. `WillIngestStackCountOf` clamps to 1 (1240-1243). Vanilla `JobGiver_GetFood:107-116` does the same call.
   1.1 The proposed fix is harmful: passing `null` makes `GetNutrition` return 0 (line 1377).

2. **BUG-02, pawn saved walking to bed keeps forceSleep - CONFIRMED.** `ForceSleepWatch.cs:58` needs `asleep`, but the tick (line 91, 122) clears forceSleep as soon as asleep. `Job.forceSleep` is saved (`Job:534`) and blocks the rest wake (`Toils_LayDown:71`).

3. **BUG-03, merged Go Eat greyed by first pawn - WRONG.** `GizmoGridDrawer` (decompile 87-102) shows the first NOT-disabled gizmo in a group; it falls back to a disabled one only when all are disabled.

4. **BUG-04, JoyWatch keyed by pawn, misses queued jobs - CONFIRMED IN PART.** `JoyWatch.cs:63,112`. Shift-click queues (`KeyBindingDef.IsDownEvent:77`, `Pawn_JobTracker:867-873,902-906`), so the queued job is dropped unwatched. "Ends the wrong job" overstated: it ends the current forced joy job at full need.

5. **BUG-05, queued order logged as failed to start - CONFIRMED, but deliberate.** `Logger.cs:159-163` writes it, naming "order queued behind it". Comment at 149-153 documents this, and the method is `LOCKED (bl H1)` (line 154). Changing it needs the user's order.

6. **BUG-06, bed alert for pawns with no rest need - CONFIRMED.** `Alert_BedProblems.cs:66-98` has no `needs.rest` check. Whether any pawn in this save lacks a rest need is NOT VERIFIED.

7. **BUG-07, SelectedColonists skips Spawned/Drafted - CONFIRMED, cosmetic.** `PawnGizmoPatch.cs:122-127` versus `Offered` at 113-120. Only the "pressed for N pawns" count is inflated; every `Qualifies` drops drafted and unspawned pawns.

8. **BUG-08, verbose logging defaults on - CONFIRMED.** `BeLazySettings.cs:11,17` default true; tooltip at line 32 says "Off unless...". Possibly deliberate - CLAUDE.md wants verbose on to read logs. User's call.

9. **BUG-09, food cache holds pawns across saves - CONFIRMED, low.** `FoodAvailabilityCache.cs:37` is static and never cleared; lines 22-25 accept this on purpose. Memory held only, no wrong result: new pawn objects miss the cache.

10. **BUG-10, About.xml 1.5 only, redundant ConvertAll - CONFIRMED, no impact.** `About.xml` lists 1.5; installed game is 1.5.4409, so no warning today. `Alert_BedProblems.cs:105` `ConvertAll(r => r).ToArray()` is redundant.

11. **Not in the report - Shift-queued Go to Bed can sleep past 100% - CONFIRMED.** `SleepOrder.cs:101,107,115` sets forceSleep and watches; queued job not current, so `ForceSleepWatch.cs:91` drops the pawn. The queued LayDown later runs with forceSleep never cleared.

## Confirmed defects, by severity, one-line fix each

1. Item 11 - skip watching only once the forced job is current, or clear forceSleep on a queued order.
2. BUG-02 - in `LoadedGame`, drop the `asleep` condition and keep `forceSleep` (plus LayDown def).
3. BUG-04 - key JoyWatch by `job.loadID` and keep entries whose job is still queued.
4. BUG-06 - skip pawns with `needs?.rest == null` in `Recompute`.
5. BUG-05 - locked; only on the user's order, log "queued" instead of "failed to start".
6. BUG-09 - clear the cache in a `LoadedGame`/`StartedNewGame` override.
7. BUG-08 - user decides the default.
8. BUG-07 - filter `SelectedColonists` through `Offered`.
9. BUG-10 - use `string.Join(", ", reasons)`; add 1.6 only when supporting it.
