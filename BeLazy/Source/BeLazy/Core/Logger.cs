using Verse;
using Verse.AI;

namespace BeLazy.Core
{
    // Named Logger, not Log - Verse.Log is in scope in most files here.
    //
    // Deliberately thin. CLAUDE.md's standing rule: a log line that can fire
    // per target, per def or per click is a defect until proved otherwise -
    // broken four times in this project already. bl-architecture.md section
    // 6 says this mod gets the load line and very little else, so there is
    // no log file of its own (compare RimWarOdds/Core/LogFile.cs) - the
    // volume here never gets close to needing one.
    //
    // Loaded() always writes, once, at mod construction. Message() only
    // writes when the verbose setting is on, and every call site is
    // commented at the call with how often it can fire - none of them are
    // inside the per-pawn joy-giver ranking loop (JoyOrder.TryRank). Four
    // call sites added 2026-09-19 for the order-event log lines
    // (bl-architecture.md 5.1a, 5.2a): one in SleepOrder.Execute and one in
    // JoyOrder.Execute, each once per qualifying pawn per press; two in
    // ForceSleepWatch.GameComponentTick, each at most once per watched
    // pawn, at the tick its state changes, not per tick of the 60-tick
    // loop itself. Two more added 2026-09-28: one in SleepOrder.Execute
    // and one in JoyOrder.Execute, each once per press, naming the pawn
    // count - bl-architecture.md 5.3. Five more added 2026-09-28 for "Go
    // Eat": one in FoodOrder.Execute per press (pawn count), one per
    // qualifying pawn (ordered or refused), and two in
    // FoodWatch.GameComponentTick, each at most once per watched pawn, at
    // the tick its state changes.
    public static class Logger
    {
        public static bool VerboseLogging = false;

        private const string Prefix = "[BeLazy] ";

        public static void Loaded(string text)
        {
            Log.Message(Prefix + text);
        }

        public static void Message(string text)
        {
            if (!VerboseLogging)
            {
                return;
            }

            Log.Message(Prefix + text);
        }

        // Order lifecycle lines, ordered 2026-10-02 - every order Be Lazy
        // issues writes exactly three kinds of line, one id per order per
        // pawn, so a script can match them:
        //   ORDER BL-n issued: <pawn> <kind> <detail>
        //   ORDER BL-n started: <pawn> <job def> <target>
        //   ORDER BL-n ended (<reason>): <pawn> <detail>
        // A refusal writes issued then ended (failed to start: why) and no
        // started line. Each fires once per order per pawn, never per tick.
        // Written through Order, which ignores the verbose setting.
        // The exact input behind an order, ordered 2026-10-02: mouse button,
        // every modifier held, the event type the state was read from and
        // the key bound to QueueOrder when it is not plain Shift. Only
        // meaningful while the click's own GUI event is live, which a
        // gizmo's action is.
        public static string Keys()
        {
            UnityEngine.Event e = UnityEngine.Event.current;
            if (e == null)
            {
                return "keys: no GUI event (not read inside a click)";
            }

            // Button and modifiers are read whatever the event type is.
            // By the time a float menu option's or a gizmo's action runs
            // the click event has been consumed (type Used, fixed
            // 2026-10-02 after the first version, which keyed off isMouse,
            // logged "no mouse button + no modifier" for every order).
            // Event.Use only changes the type; the button and modifier
            // fields are left in place.
            string button = e.type == UnityEngine.EventType.MouseDown || e.type == UnityEngine.EventType.MouseUp || e.type == UnityEngine.EventType.Used
                ? (e.button == 0 ? "left-click" : e.button == 1 ? "right-click" : e.button == 2 ? "middle-click" : "mouse button " + e.button)
                : "no mouse button";
            var mods = new System.Collections.Generic.List<string>();
            if (e.shift) mods.Add("Shift");
            if (e.control) mods.Add("Ctrl");
            if (e.alt) mods.Add("Alt");
            if (e.command) mods.Add("Cmd");

            string text = "keys: " + button + (mods.Count == 0 ? " + no modifier" : " + " + string.Join(" + ", mods.ToArray()))
                + " (event " + e.type + ")";

            var queueKey = RimWorld.KeyBindingDefOf.QueueOrder;
            if (KeyPrefs.KeyPrefsData.keyPrefs.TryGetValue(queueKey, out var binding)
                && !(binding.keyBindingA == UnityEngine.KeyCode.LeftShift && binding.keyBindingB == UnityEngine.KeyCode.RightShift))
            {
                text += "; QueueOrder is bound to " + binding.keyBindingA + "/" + binding.keyBindingB;
            }

            return text;
        }

        private static int orderCounter;
        // 2026-10-04: counter restarts each launch; the HHmm stamp keeps ids unique across sessions.
        private static readonly string launchStamp = System.DateTime.Now.ToString("HHmm");

        // Always on, whatever the verbose setting - the order record is
        // required (2026-10-02). Once per event, never per tick.
        private static void Order(string text)
        {
            Log.Message(Prefix + text);
        }

        public static string OrderIssued(Pawn pawn, string kind, string detail)
        {
            string id = "BL-" + launchStamp + "-" + (++orderCounter);
            Order("ORDER " + id + " issued: " + pawn.LabelShortCap + " " + kind + " " + detail + " (" + Keys() + ")");
            return id;
        }

        public static void OrderFailedToStart(string id, Pawn pawn, string why)
        {
            Order("ORDER " + id + " ended (failed to start: " + why + "): " + pawn.LabelShortCap);
        }

        // Call straight after TryTakeOrderedJob returned true. When the job
        // is the pawn's current one, writes the started line and hangs the
        // ended line on the pawn's JobDriver through AddFinishAction
        // (public, takes Action<JobCondition>, verified 2026-10-02 against
        // lib\Assembly-CSharp.dll; JobDriver.Cleanup runs every finish
        // action for any ending, with the condition). That is how the ended
        // line is written without a patch on EndCurrentJob. When the job is
        // not running yet or the pawn was put on something else, there is no
        // driver to hang it on, so the order is ended here as failed to
        // start - the one place an ended line cannot be promised to match a
        // job that later runs anyway.
        public static void OrderStarted(string id, Pawn pawn, Job job)
        {
            Job cur = pawn.jobs?.curJob;
            JobDriver driver = pawn.jobs?.curDriver;
            if (cur == null || !ReferenceEquals(cur, job) || driver == null)
            {
                string curText = cur == null || cur.def == null ? "no job" : cur.def.defName + " on " + cur.targetA;
                OrderFailedToStart(id, pawn, "TryTakeOrderedJob accepted it but it is not running - pawn is on " + curText
                    + (pawn.jobs?.jobQueue != null && pawn.jobs.jobQueue.Contains(job) ? ", order queued behind it" : ""));
                return;
            }

            string name = pawn.LabelShortCap;
            string target = job.targetA.ToString();
            Order("ORDER " + id + " started: " + name + " " + job.def.defName + " " + target);

            driver.AddFinishAction(condition =>
            {
                string reason;
                if (pawn.Dead) reason = "pawn dead";
                else if (pawn.Downed) reason = "pawn downed";
                else if (condition == JobCondition.Succeeded) reason = "finished";
                else if (condition == JobCondition.InterruptForced)
                    reason = job.playerInterruptedForced ? "player gave a different order" : "interrupted by another job";
                else if (condition == JobCondition.Incompletable) reason = "target gone or unreachable";
                else reason = condition.ToString();

                Order("ORDER " + id + " ended (" + reason + "): " + name + " " + job.def?.defName + " " + target
                    + " (" + condition + ")");
            });
        }

        public static void Warning(string text)
        {
            Log.Warning(Prefix + text);
        }
    }
}
