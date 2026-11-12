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

    //
    // The line the server logs when it gives up, with the error under it - seen on a 4.1.6 server
    // whose mod threw while loading:
    //   [Critical][SPTarkov.Server.Core] The server has unexpectedly stopped, reach out to ...
    // Told by where it comes from as well as what it says, since SPT words it in the PC's language.
    // Not every Critical line: a request that fails is logged Critical too, and the server goes on.
    //
    public static bool IsServerStopped(LogEntry entry) =>
        entry.Level is "Critical" or "Fatal"
        && (entry.Source == "SPTarkov.Server.Core"
            || entry.Message.StartsWith("The server has unexpectedly stopped", StringComparison.OrdinalIgnoreCase));

    /// <summary>The first such line in these entries, or null.</summary>
    public static LogEntry? ServerCrash(IEnumerable<LogEntry> entries) => entries.FirstOrDefault(IsServerStopped);
}
