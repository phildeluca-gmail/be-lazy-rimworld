# Ketjak's Be Lazy — Comprehensive Bug Review Report

**Mod Package:** `phildeluca.belazy` (*Ketjak's Be Lazy*)  
**Files Audited:** 15 (`About.xml` + 14 `.cs` source files)  
**Target Framework:** .NET Framework 4.7.2 / RimWorld 1.5

---

## Summary of Findings

| ID | Severity | File(s) | Summary |
| :--- | :--- | :--- | :--- |
| **BUG-01** | **High** | `Food\FoodFinder.cs` | Nutrient paste dispensers pass a building `Thing` into `FoodUtility.GetNutrition()`, risking `0f` nutrition and an invalid `job.count`. |
| **BUG-02** | **High** | `Sleep\ForceSleepWatch.cs` | `LoadedGame()` requires `driver.asleep`, dropping pawns saved while walking to bed and leaving `forceSleep = true` permanently. |
| **BUG-03** | **High** | `Patches\PawnGizmoPatch.cs` | Multi-selected `"Go Eat"` gizmo inherits `Disabled` from whichever pawn is drawn first, blocking pawns who *do* have food. |
| **BUG-04** | **Medium** | `Joy\JoyOrder.cs`, `Joy\JoyWatch.cs` | `JoyWatch` tracks `Pawn` instead of `job.loadID`, causing Shift-queued joy orders to drop immediately or terminate the wrong job. |
| **BUG-05** | **Medium** | `Core\Logger.cs` | Shift-queued orders trigger `OrderFailedToStart` in `OrderStarted` even though `TryTakeOrderedJob` succeeded and queued the job. |
| **BUG-06** | **Medium** | `Alerts\Alert_BedProblems.cs` | Colonists/races without a rest need (`pawn.needs?.rest == null`) trigger permanent `"has no owned bed"` alerts. |
| **BUG-07** | **Low** | `Patches\PawnGizmoPatch.cs` | `SelectedColonists()` omits `!p.Drafted && p.Spawned`, inflating press log counts when drafted and undrafted pawns are mixed. |
| **BUG-08** | **Low** | `Core\BeLazySettings.cs` | `verboseLogging` defaults to `true` in code and `Scribe_Values`, contradicting its tooltip (`"Off unless you're chasing something."`). |
| **BUG-09** | **Low** | `Food\FoodAvailabilityCache.cs` | Static `Dictionary<Pawn, Entry>` retains strong references to destroyed/previous-save `Pawn` instances across save reloads. |
| **BUG-10** | **Info** | `About\About.xml`, `Alerts\Alert_BedProblems.cs` | `About.xml` only declares `1.5` (will warn on `1.6`), and `Alert_BedProblems` uses a redundant `.ConvertAll(r => r).ToArray()` allocation. |

---

## Detailed Findings & Fixes

### BUG-01 [High]: Nutrient Paste Dispenser `GetNutrition` Calculation
* **Location:** `BeLazy\Source\BeLazy\Food\FoodFinder.cs` (`TryFindBestFoodJob`, lines 148–151)
* **Issue:** When `bestThing` is a `Building_NutrientPasteDispenser`, `bestDef` is `ThingDefOf.MealNutrientPaste`, while `bestThing` is the dispenser building itself. Passing `bestThing` into `FoodUtility.GetNutrition(pawn, bestThing, bestDef)` inspects `bestThing.def.ingestible` (which is `null` on a building) rather than `bestDef.ingestible`, returning `0f`. Passing `0f` into `FoodUtility.WillIngestStackCountOf(pawn, bestDef, nutrition)` divides by zero, producing `int.MinValue` or an invalid stack count on `job.count`.
* **Fix:** Pass `null` for the `Thing` parameter when `bestThing` is a dispenser, and clamp `job.count` to at least `1`:
```csharp
Thing nutritionSource = bestThing is Building_NutrientPasteDispenser ? null : bestThing;
float nutrition = FoodUtility.GetNutrition(pawn, nutritionSource, bestDef);
Job job = JobMaker.MakeJob(JobDefOf.Ingest, bestThing);
job.count = Mathf.Max(1, FoodUtility.WillIngestStackCountOf(pawn, bestDef, nutrition));
```

---

### BUG-02 [High]: Pawns Saved En Route to Bed Never Have `forceSleep` Cleared
* **Location:** `BeLazy\Source\BeLazy\Sleep\ForceSleepWatch.cs` (`LoadedGame`, lines 50–64)
* **Issue:** During normal play, `GameComponentTick()` sets `job.forceSleep = false` on the exact poll that `driver.asleep` becomes `true`, and immediately removes the pawn from `watching`. Consequently, any pawn *already asleep* on save already has `forceSleep == false`, whereas a pawn *walking to bed* when saved has `job.forceSleep == true` and `driver.asleep == false`. Because `LoadedGame()` checks `if (job != null && job.forceSleep && driver != null && driver.asleep)`, walking pawns are never re-added to `watching` on reload and will sleep past 100% rest indefinitely once they arrive.
* **Fix:** Remove the `driver.asleep` requirement in `LoadedGame()`:
```csharp
public override void LoadedGame()
{
    watching.Clear();

    foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
    {
        Job job = pawn?.jobs?.curJob;
        if (job != null && job.forceSleep && job.def == JobDefOf.LayDown)
        {
            watching.Add(pawn);
        }
    }
}
```

---

### BUG-03 [High]: Multi-Select `"Go Eat"` Gizmo Inherits `Disabled` From First Pawn
* **Location:** `BeLazy\Source\BeLazy\Patches\PawnGizmoPatch.cs` (`Postfix`, lines 84–96)
* **Issue:** Every selected pawn emits its own `Command_Action` sharing `groupKey = GoEatGroupKey`. RimWorld's `GizmoGridDrawer` merges grouped gizmos into a single UI button and inherits `Disabled` and `disabledReason` from whichever pawn happens to be drawn first (`interactedGiz`). If Pawn A has no valid food (e.g., restricted food policy or ascetic/raw mismatch) and Pawn B *does* have food, selecting both pawns disables the `"Go Eat"` button for both, preventing Pawn B from eating even though `FoodOrder.Execute` processes pawns independently.
* **Fix:** Evaluate food availability across the selected qualifying colonists so `"Go Eat"` is only disabled when **none** of the selected colonists can eat:
```csharp
List<Pawn> selected = SelectedColonists();
bool anyHasFood = false;
string lastReason = null;

for (int i = 0; i < selected.Count; i++)
{
    if (FoodAvailabilityCache.HasFoodItWillEat(selected[i], out string reason))
    {
        anyHasFood = true;
        break;
    }
    lastReason = reason;
}

string disabledReason = anyHasFood
    ? null
    : (selected.Count > 1
        ? "Nothing to eat for any selected colonist."
        : "Nothing to eat: " + lastReason + ".");
```

---

### BUG-04 [Medium]: `JoyWatch` Tracks `Pawn` Instead of `job.loadID`
* **Location:** `BeLazy\Source\BeLazy\Joy\JoyWatch.cs` & `BeLazy\Source\BeLazy\Joy\JoyOrder.cs`
* **Issue:** Unlike `FoodWatch` (which tracks `job.loadID`), `JoyWatch` uses a `HashSet<Pawn>` and checks `if (job == null || !job.playerForced || job.def?.joyKind == null)`. If a player Shift-queues `"Get Rec'd"` behind a current job (or while the pawn is already doing another forced joy activity), `JoyWatch` either drops the pawn on the next 60-tick check (because `curJob` isn't the queued joy job yet) or terminates the earlier forced joy job instead of the new one.
* **Fix:** Track `Dictionary<Pawn, int> watching` keyed by `job.loadID` (matching `FoodWatch.cs`), pass `job.loadID` from `JoyOrder.TryRank`, and only drop the entry when `curJob?.loadID != targetId` **and** `targetId` is not present in `pawn.jobs.jobQueue`.

---

### BUG-05 [Medium]: Shift-Queued Orders Log `"failed to start"`
* **Location:** `BeLazy\Source\BeLazy\Core\Logger.cs` (`OrderStarted`, lines 142–153)
* **Issue:** When a player holds `Shift` (`QueueOrder`) while clicking `"Go to Bed"`, `"Get Rec'd"`, or `"Go Eat"`, `pawn.jobs.TryTakeOrderedJob(job)` succeeds and appends `job` to `pawn.jobs.jobQueue`. `OrderStarted` immediately checks `!ReferenceEquals(cur, job)` and calls `OrderFailedToStart(..., "order queued behind it")`, falsely marking a valid queued order as failed in the log.
* **Fix:** Detect queued jobs explicitly in `OrderStarted`:
```csharp
bool isQueued = pawn.jobs?.jobQueue != null && pawn.jobs.jobQueue.Any(q => q.job == job);
if (isQueued)
{
    Order("ORDER " + id + " queued: " + pawn.LabelShortCap + " " + job.def.defName + " " + job.targetA);
    return;
}
```

---

### BUG-06 [Medium]: Bedless / Non-Sleeping Pawns Trigger `Alert_BedProblems`
* **Location:** `BeLazy\Source\BeLazy\Alerts\Alert_BedProblems.cs` (`Recompute`, lines 66–101)
* **Issue:** `Recompute` filters out prisoners, guests, quest lodgers, and babies, but does not check `pawn.needs?.rest == null`. Modded non-sleeping colonists (androids, constructs, or genes removing the rest need) never own or use beds and will permanently trigger the `"Bed problems"` alert.
* **Fix:** Add a rest-need guard inside `Recompute`:
```csharp
if (pawn.needs?.rest == null)
{
    continue;
}
```

---

### BUG-07 [Low]: `SelectedColonists()` Includes Drafted and Unspawned Pawns
* **Location:** `BeLazy\Source\BeLazy\Patches\PawnGizmoPatch.cs` (`SelectedColonists`, lines 107–112)
* **Issue:** `Offered()` filters `pawn.Spawned && !pawn.Drafted`, whereas `SelectedColonists()` only checks `p != null && p.Faction == Faction.OfPlayer && p.RaceProps.Humanlike`. When a drafted pawn and an undrafted pawn are selected together, clicking any Be Lazy gizmo passes the drafted pawn into `Execute()`, inflating `"pressed for X pawns"` log lines before `Qualifies()` discards them.
* **Fix:** Match `Offered()` in `SelectedColonists()`:
```csharp
private static List<Pawn> SelectedColonists()
{
    return Find.Selector.SelectedPawns
        .Where(p => Offered(p))
        .ToList();
}
```

---

### BUG-08 [Low]: `verboseLogging` Defaults to `true`
* **Location:** `BeLazy\Source\BeLazy\Core\BeLazySettings.cs` (lines 11–17)
* **Issue:** Both the field initializer (`public bool verboseLogging = true;`) and `Scribe_Values.Look(ref verboseLogging, "verboseLogging", true);` default to `true`, which contradicts the UI tooltip (`"Off unless you're chasing something."`) and writes verbose diagnostic lines for new users out of the box.
* **Fix:** Change both defaults to `false`.

---

### BUG-09 [Low]: `FoodAvailabilityCache` Retains Cross-Save `Pawn` References
* **Location:** `BeLazy\Source\BeLazy\Food\FoodAvailabilityCache.cs` (line 36)
* **Issue:** `private static readonly Dictionary<Pawn, Entry> cache` is never cleared when loading a save or starting a new game, keeping strong references to previous-session `Pawn` and `Map` object graphs in memory.
* **Fix:** Add `public static void Clear() => cache.Clear();` and call it from `ForceSleepWatch.LoadedGame()` (and `StartedNewGame()`).

---

### BUG-10 [Info]: Minor Metadata & Allocation Cleanup
1. **`About\About.xml`:** `<supportedVersions>` only lists `<li>1.5</li>`. If targeting RimWorld 1.6 as well, add `<li>1.6</li>` to avoid a yellow version mismatch warning in the mod manager.
2. **`Alerts\Alert_BedProblems.cs` (line 106):** Replace `string.Join(", ", reasons.ConvertAll(r => r).ToArray())` with `string.Join(", ", reasons)` to avoid redundant list/array allocations on .NET 4.7.2.