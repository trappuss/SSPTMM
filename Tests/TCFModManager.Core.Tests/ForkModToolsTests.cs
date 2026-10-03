using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (SSPTMM): the Play page's mod tools - every .exe a mod installed (ModTools).
public class ForkModToolsTests : IDisposable
{
    private readonly string _install = Path.Combine(Path.GetTempPath(), "tcfmm-tools-" + Guid.NewGuid().ToString("N"));

    private const string Greed = "Greed.exe";
    private const string SvmFolder = "SPT_Runtime/user/mods/[SVM] Server Value Modifier";
    private const string GiveUi = "SPT_Runtime/user/mods/give-ui/give-ui_x64-portable.exe";

    public ForkModToolsTests()
    {
        Write("EscapeFromTarkov.exe");
        Write("SPT_Runtime/SPT.Server.exe");
    }

    public void Dispose()
    {
        try { Directory.Delete(_install, recursive: true); }
        catch (IOException) { }
    }

    private string Full(string relative) => Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relative)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(relative))!);
        File.WriteAllText(Full(relative), "x");
    }

    private InstalledModRecord Record(string name, string? stamp, params string[] files)
    {
        foreach (var f in files) Write(f);
        return new InstalledModRecord
        {
            ModId = name.Length,
            Name = name,
            Version = "1",
            InstalledAt = DateTimeOffset.UtcNow,
            InstallPath = stamp,
            Files = [.. files],
        };
    }

    private InstalledModRecord Svm() =>
        Record("Server Value Modifier [SVM]", InstallStamp.Of(_install), Greed, $"{SvmFolder}/ServerValueModifier.dll", $"{SvmFolder}/Loader/loader.json");

    [Fact]
    public void Each_exe_a_mod_installed_is_a_tool_with_what_it_needs_and_its_mods_folder()
    {
        var giveUi = Record("give-ui", null, GiveUi, "SPT_Runtime/user/mods/give-ui/give-ui.dll");

        var tools = ModTools.Find(_install, [Svm(), giveUi], [], []);

        Assert.Equal(["give-ui_x64-portable", "Greed"], tools.Select(t => t.Name));

        var greed = tools.Single(t => t.Name == "Greed");
        Assert.Equal(ModToolNeed.ServerStopped, greed.Need);
        Assert.Equal(Full(Greed), greed.FullPath);
        Assert.Equal(Full(SvmFolder), greed.ModFolder);
        Assert.False(greed.IsModDisabled);

        Assert.Equal(ModToolNeed.ServerRunning, tools.Single(t => t.Name == "give-ui_x64-portable").Need);
    }

    [Fact]
    public void A_disabled_mod_keeps_its_tool_greyed_including_one_outside_its_folders()
    {
        var svm = Svm();
        Directory.CreateDirectory(Full("SPT_Runtime/user/mods.disabled"));
        Directory.Move(Full(SvmFolder), Full("SPT_Runtime/user/mods.disabled/[SVM] Server Value Modifier"));

        var greed = ModTools.Find(_install, [svm], [], []).Single();

        Assert.True(greed.IsModDisabled);
        Assert.Equal(Full("SPT_Runtime/user/mods.disabled/[SVM] Server Value Modifier"), greed.ModFolder);
    }

    [Fact]
    public void A_tool_inside_a_disabled_folder_is_found_where_it_was_moved_and_keeps_its_key()
    {
        var giveUi = Record("give-ui", null, GiveUi);
        var enabledKey = ModTools.Find(_install, [giveUi], [], []).Single().Key;

        Directory.CreateDirectory(Full("SPT_Runtime/user/mods.disabled"));
        Directory.Move(Full("SPT_Runtime/user/mods/give-ui"), Full("SPT_Runtime/user/mods.disabled/give-ui"));

        var tool = ModTools.Find(_install, [giveUi], [], []).Single();
        Assert.True(tool.IsModDisabled);
        Assert.Equal(Full("SPT_Runtime/user/mods.disabled/give-ui/give-ui_x64-portable.exe"), tool.FullPath);
        Assert.Equal(enabledKey, tool.Key);
    }

    [Fact]
    public void Hidden_tools_are_marked_not_dropped_and_removed_or_foreign_ones_are_left_out()
    {
        var svm = Svm();
        var removed = Record("Gone", null, "SPT_Runtime/user/mods/Gone/tool.exe");
        File.Delete(Full("SPT_Runtime/user/mods/Gone/tool.exe"));
        var foreign = Record("Elsewhere", @"Z:\another\SPT", "SPT_Runtime/user/mods/Other/other.exe");

        var tools = ModTools.Find(_install, [svm, removed, foreign], [], ["GREED.EXE"]);

        Assert.True(tools.Single().IsHidden);
        Assert.Equal("Greed", tools.Single().Name);
    }

    [Fact]
    public void A_hand_installed_mods_exe_is_found_in_its_own_folder_once()
    {
        Write("BepInEx/plugins/HandTool/HandTool.dll");
        Write("BepInEx/plugins/HandTool/bin/Configurator.exe");
        var scanned = new InstalledMod
        {
            Name = "Hand Tool",
            Target = InstalledModTarget.Client,
            FolderPath = Full("BepInEx/plugins/HandTool"),
        };

        var tools = ModTools.Find(_install, [], [scanned, scanned], []);

        var tool = Assert.Single(tools);
        Assert.Equal("Configurator", tool.Name);
        Assert.Equal("Hand Tool", tool.ModName);
        Assert.Equal(ModToolNeed.None, tool.Need);
        Assert.Equal(Full("BepInEx/plugins/HandTool"), tool.ModFolder);
    }

    [Theory]
    [InlineData("give-ui_x64-portable.exe", ModToolNeed.ServerRunning)]
    [InlineData("GIVE-UI.exe", ModToolNeed.ServerRunning)]
    [InlineData("Greed.exe", ModToolNeed.ServerStopped)]
    [InlineData("Greedy.exe", ModToolNeed.None)]
    [InlineData("tool.exe", ModToolNeed.None)]
    public void Known_tools_say_what_they_need(string exe, ModToolNeed expected) =>
        Assert.Equal(expected, ModTools.NeedFor(exe));
}
