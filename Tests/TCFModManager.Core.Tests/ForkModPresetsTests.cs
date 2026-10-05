using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (SSPTMM 1.1.0): presets of which mods are on and off - see ModPresets.
public sealed class ForkModPresetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ssptmm-presets-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;

    public ForkModPresetsTests()
    {
        _install = Path.Combine(_root, "SPT");
        Directory.CreateDirectory(_install);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private InstalledMod Mod(string relative, string name, bool disabled = false) => new()
    {
        Name = name,
        Target = relative.StartsWith("BepInEx", StringComparison.Ordinal) ? InstalledModTarget.Client : InstalledModTarget.Server,
        FolderPath = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar)),
        IsDisabled = disabled,
    };

    [Fact]
    public void AModsKey_IsItsPathAsItReadsWhenEnabled()
    {
        Assert.Equal("BepInEx/plugins/SAIN", ModPresets.KeyFor(_install, Mod("BepInEx/plugins/SAIN", "SAIN")));
        Assert.Equal("BepInEx/plugins/SAIN", ModPresets.KeyFor(_install, Mod("BepInEx/plugins.disabled/SAIN", "SAIN", disabled: true)));
        Assert.Equal("SPT_Runtime/user/mods/SVM", ModPresets.KeyFor(_install, Mod("SPT_Runtime/user/mods.disabled/SVM", "SVM", disabled: true)));
    }

    [Fact]
    public void Capture_KeepsEachFolder_SoAHalfDisabledModStaysThatWay()
    {
        var mods = new[]
        {
            Mod("BepInEx/plugins/Fika.Core.dll", "Fika"),
            Mod("SPT_Runtime/user/mods.disabled/fika-server", "Fika server", disabled: true),
        };

        var entries = ModPresets.Capture(_install, mods);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e is { Path: "BepInEx/plugins/Fika.Core.dll", Enabled: true });
        Assert.Contains(entries, e => e is { Path: "SPT_Runtime/user/mods/fika-server", Enabled: false });
    }

    [Fact]
    public void Plan_MovesOnlyWhatDiffers_AndLeavesModsThePresetDoesntName()
    {
        var a = Mod("BepInEx/plugins/A", "A");
        var b = Mod("BepInEx/plugins.disabled/B", "B", disabled: true);
        var c = Mod("BepInEx/plugins/C", "C");
        var newer = Mod("BepInEx/plugins/New", "New");

        var preset = new List<ModPresetEntry>
        {
            new() { Path = "BepInEx/plugins/A", Enabled = false },
            new() { Path = "BepInEx/plugins/B", Enabled = true },
            new() { Path = "BepInEx/plugins/C", Enabled = true },
            new() { Path = "BepInEx/plugins/Gone", Enabled = true, Name = "Gone" },
        };

        var plan = ModPresets.Plan(_install, preset, [a, b, c, newer]);

        Assert.Equal([a], plan.ToDisable);
        Assert.Equal([b], plan.ToEnable);
        Assert.Equal("Gone", Assert.Single(plan.NotInstalled).Name);
        Assert.Equal([newer], plan.NotInPreset);
        Assert.False(plan.ChangesNothing);
    }

    [Fact]
    public void Plan_LeavesAModThatIsInTheInstallTwice()
    {
        var live = Mod("BepInEx/plugins/A", "A");
        var copy = Mod("BepInEx/plugins.disabled/A", "A", disabled: true);

        var plan = ModPresets.Plan(_install, [new ModPresetEntry { Path = "BepInEx/plugins/A", Enabled = false }], [live, copy]);

        Assert.True(plan.ChangesNothing);
    }

    [Fact]
    public void DisableAll_AndEnableAll_PlanEveryMod()
    {
        var mods = new[] { Mod("BepInEx/plugins/A", "A"), Mod("SPT_Runtime/user/mods.disabled/B", "B", disabled: true) };

        Assert.Single(ModPresets.Plan(_install, ModPresets.All(_install, mods, enabled: false), mods).ToDisable);
        Assert.Single(ModPresets.Plan(_install, ModPresets.All(_install, mods, enabled: true), mods).ToEnable);
    }

    [Fact]
    public void Matching_FindsThePresetTheModsAreSetTo()
    {
        var mods = new[] { Mod("BepInEx/plugins/A", "A"), Mod("BepInEx/plugins.disabled/B", "B", disabled: true) };
        var presets = new[]
        {
            new ModPreset { Name = "All on", Entries = ModPresets.All(_install, mods, enabled: true) },
            new ModPreset { Name = "Now", Entries = ModPresets.Capture(_install, mods) },
        };

        Assert.Equal("Now", ModPresets.Matching(_install, presets, mods)?.Name);
    }

    [Theory]
    [InlineData("  Fika  ", "Fika")]
    [InlineData("two\nlines", "two lines")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Names_AreKeptOnOneTrimmedLine(string? typed, string? kept)
    {
        Assert.Equal(kept, ModPresets.CleanName(typed));
    }

    [Fact]
    public void Names_AreCutToTheLimit()
    {
        Assert.Equal(ModPresets.MaxNameLength, ModPresets.CleanName(new string('x', 200))!.Length);
    }

    [Fact]
    public void Store_SavesRenamesAndDeletes_PerInstall()
    {
        var store = new ModPresetStore(Path.Combine(_root, "mod_presets.json"));
        var other = Path.Combine(_root, "Other SPT");
        var entries = new List<ModPresetEntry> { new() { Path = "BepInEx/plugins/A", Enabled = true } };

        store.Save(_install, "Fika", entries, DateTimeOffset.UnixEpoch);
        store.Save(_install, "all off", [], DateTimeOffset.UnixEpoch);
        store.Save(_install, "FIKA", [], DateTimeOffset.UnixEpoch.AddDays(1)); // same name, other case: replaced

        var mine = store.For(_install);
        Assert.Equal(["all off", "Fika"], mine.Presets.Select(p => p.Name));
        Assert.Empty(ModPresetStore.Find(mine, "fika")!.Entries);
        Assert.Empty(store.For(other).Presets);

        Assert.False(store.Rename(_install, "Fika", "All Off")); // taken
        Assert.True(store.Rename(_install, "Fika", "Fika co-op"));
        store.Delete(_install, "ALL OFF");

        Assert.Equal(["Fika co-op"], store.For(_install).Presets.Select(p => p.Name));
    }

    [Fact]
    public void Store_KeepsHowModsWereBeforeTheLastApply()
    {
        var store = new ModPresetStore(Path.Combine(_root, "mod_presets.json"));
        store.KeepBeforeApply(_install, "Fika", [new ModPresetEntry { Path = "BepInEx/plugins/A", Enabled = false }], DateTimeOffset.UnixEpoch);

        var before = store.For(_install).BeforeLastApply;
        Assert.Equal("Fika", before?.Name);
        Assert.False(Assert.Single(before!.Entries).Enabled);
    }

    [Fact]
    public void Store_ADamagedFile_IsSetAsideAndReadsAsNoPresets()
    {
        var file = Path.Combine(_root, "mod_presets.json");
        File.WriteAllText(file, "{ not json");

        var store = new ModPresetStore(file);
        Assert.Empty(store.For(_install).Presets);

        store.Save(_install, "Fika", [], DateTimeOffset.UnixEpoch);
        Assert.Equal("Fika", Assert.Single(store.For(_install).Presets).Name);
    }
}
