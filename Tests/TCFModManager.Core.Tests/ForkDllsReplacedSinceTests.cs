using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.8): a mod counts as updated by hand only when a DLL its install placed has changed size.
public sealed class ForkDllsReplacedSinceTests : IDisposable
{
    private const string Plugin = "BepInEx/plugins/SAIN/SAIN.dll";

    private readonly string _install = Path.Combine(Path.GetTempPath(), "ssptmm-replaced-" + Guid.NewGuid().ToString("N"));

    public ForkDllsReplacedSinceTests() => Directory.CreateDirectory(_install);

    public void Dispose() => Directory.Delete(_install, recursive: true);

    private void Write(string relative, int bytes)
    {
        var path = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private static InstalledModRecord Record(params FileFingerprint[] prints) => new()
    {
        ModId = 791,
        Name = "SAIN",
        Version = "1.0.0",
        InstalledAt = DateTimeOffset.UnixEpoch,
        Files = [.. prints.Select(p => p.Path)],
        Fingerprints = [.. prints],
    };

    [Fact]
    public void A_dll_still_the_size_it_was_placed_at_was_not_replaced()
    {
        Write(Plugin, 100);

        Assert.False(InstalledModFolders.DllsReplacedSince(Record(new FileFingerprint(Plugin, 100, "x")), _install));
    }

    [Fact]
    public void A_dll_of_another_size_was_replaced()
    {
        Write(Plugin, 140);

        Assert.True(InstalledModFolders.DllsReplacedSince(Record(new FileFingerprint(Plugin, 100, "x")), _install));
    }

    [Fact]
    public void One_replaced_dll_among_several_is_enough_and_other_files_do_not_count()
    {
        Write(Plugin, 100);
        Write("BepInEx/plugins/SAIN/SAIN.Extra.dll", 55);
        Write("BepInEx/plugins/SAIN/config.json", 999);

        var record = Record(
            new FileFingerprint(Plugin, 100, "x"),
            new FileFingerprint("BepInEx/plugins/SAIN/SAIN.Extra.dll", 50, "y"),
            new FileFingerprint("BepInEx/plugins/SAIN/config.json", 10, "z"));
        Assert.True(InstalledModFolders.DllsReplacedSince(record, _install));

        // Only the config differs: a mod writes to that itself.
        Write("BepInEx/plugins/SAIN/SAIN.Extra.dll", 50);
        Assert.False(InstalledModFolders.DllsReplacedSince(record, _install));
    }

    [Fact]
    public void With_nothing_to_go_by_it_is_not_ruled_out()
    {
        // No sizes kept, no DLL where the record put it (switched off), no install.
        Write(Plugin, 100);
        Assert.True(InstalledModFolders.DllsReplacedSince(Record(), _install));
        Assert.True(InstalledModFolders.DllsReplacedSince(Record(new FileFingerprint("BepInEx/plugins/Gone/Gone.dll", 100, "x")), _install));
        Assert.True(InstalledModFolders.DllsReplacedSince(Record(new FileFingerprint(Plugin, 100, "x")), null));
    }
}
