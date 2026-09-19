using Verse;

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
    // inside the per-pawn joy-giver ranking loop (JoyOrder.TryRank) or the
    // per-pawn sleep watch loop (ForceSleepWatch.GameComponentTick).
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

        public static void Warning(string text)
        {
            Log.Warning(Prefix + text);
        }
    }
}
