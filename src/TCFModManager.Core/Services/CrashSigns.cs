namespace TCFModManager.Core.Services;

//
// Fork (1.4.7): what tells a crash from an ordinary end, for the "Diagnose logs" suggestion.
//
public static class CrashSigns
{
    //
    // A process Windows ended for a fault leaves the fault's code as its exit code: an access
    // violation is 0xC0000005, a stack overrun 0xC0000409, an unhandled .NET exception 0xE0434352.
    // A process that quit, was closed, or was ended from Task Manager leaves 0, 1 or -1.
    //
    // HUNCH: that the game leaves no code in these two ranges when it quits by itself - not seen
    // to, and not measured over many runs.
    //
    public static bool IsCrashExitCode(int exitCode)
    {
        var code = unchecked((uint)exitCode);
        return code is >= 0xC0000000 and <= 0xCFFFFFFF or >= 0xE0000000 and <= 0xEFFFFFFF;
    }

    /// <summary>The line the server logs when it gives up - SPT logs "The server has unexpectedly
    /// stopped" as Critical - or null when these entries have none.</summary>
    public static LogEntry? ServerCrash(IEnumerable<LogEntry> entries) =>
        entries.FirstOrDefault(e => e.Level is "Critical" or "Fatal");
}
