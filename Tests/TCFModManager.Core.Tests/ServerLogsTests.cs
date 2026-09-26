using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ServerLogsTests : IDisposable
{
    private readonly string _install = Path.Combine(Path.GetTempPath(), "tcf-logs-" + Guid.NewGuid().ToString("N"));
    private readonly string _logs;

    public ServerLogsTests()
    {
        Directory.CreateDirectory(Path.Combine(_install, "SPT"));
        File.WriteAllText(Path.Combine(_install, "SPT", "SPT.Server.exe"), "");
        _logs = Path.Combine(_install, "SPT", "user", "logs");
        Directory.CreateDirectory(Path.Combine(_logs, "spt"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_install, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheNewestLogAnywhereUnderLogs_IsTheOneShown()
    {
        var old = Path.Combine(_logs, "old.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));
        var current = Path.Combine(_logs, "spt", "server-2026.log");
        File.WriteAllText(current, "new");

        Assert.Equal(current, ServerLogs.Newest(_install));
    }

    [Fact]
    public void Tail_GivesTheLastLines_WithoutColourCodes()
    {
        var file = Path.Combine(_logs, "spt", "a.log");
        File.WriteAllText(file, string.Join("\r\n", Enumerable.Range(1, 10).Select(i => $"\u001b[32mline {i}\u001b[0m")) + "\r\n");

        Assert.Equal(["line 8", "line 9", "line 10"], ServerLogs.Tail(file, lines: 3));
    }

    [Fact]
    public void Tail_ReadsAFileTheServerHoldsOpen()
    {
        var file = Path.Combine(_logs, "spt", "open.log");
        using var writer = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        writer.Write("held\n"u8);
        writer.Flush();

        Assert.Equal(["held"], ServerLogs.Tail(file));
    }

    [Fact]
    public void Tail_FromPartWayIn_DropsTheCutLine()
    {
        var file = Path.Combine(_logs, "spt", "big.log");
        File.WriteAllText(file, "aaaaaaaaaa\nbbbb\ncccc\n");

        Assert.Equal(["bbbb", "cccc"], ServerLogs.Tail(file, maxBytes: 12));
    }

    [Fact]
    public void NoLogsFolder_MeansNothingToShow()
    {
        Directory.Delete(_logs, recursive: true);

        Assert.Null(ServerLogs.Newest(_install));
    }
}
