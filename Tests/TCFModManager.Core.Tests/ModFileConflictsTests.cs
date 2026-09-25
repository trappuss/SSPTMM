using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModFileConflictsTests
{
    [Fact]
    public void Archive_paths_land_where_the_install_puts_them()
    {
        // A wrapper folder is looked through; user/ goes under the install's server folder;
        // BepInEx stays at the root.
        var paths = ModFileConflicts.InstallRelativePaths(
        [
            "SAIN-4.5.1/BepInEx/plugins/SAIN/SAIN.dll",
            "SAIN-4.5.1/user/mods/SAIN/package.json",
        ], serverRoot: "SPT");

        Assert.Equal(["BepInEx/plugins/SAIN/SAIN.dll", "SPT/user/mods/SAIN/package.json"], paths);
    }

    // SAIN 4.5.1's file-tree (2026-09-25) has this shape: no wrapper, the server part already under
    // SPT_Runtime.
    [Fact]
    public void Paths_already_under_an_SPT_folder_are_kept_as_they_are()
    {
        var paths = ModFileConflicts.InstallRelativePaths(
            ["SPT_Runtime/user/mods/Solarint-SAIN-ServerMod/SAINServerMod.dll", "BepInEx\\plugins\\SAIN\\SAIN.dll"],
            serverRoot: "SPT_Runtime");

        Assert.Equal(["SPT_Runtime/user/mods/Solarint-SAIN-ServerMod/SAINServerMod.dll", "BepInEx/plugins/SAIN/SAIN.dll"], paths);
    }

    [Fact]
    public void Files_of_another_installed_mod_clash_and_the_mods_own_do_not()
    {
        var root = Path.Combine(Path.GetTempPath(), "tcfmm-clash-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "BepInEx/plugins/Shared/Lib.dll");
            Write(root, "BepInEx/plugins/Mine/Mine.dll");
            Write(root, "BepInEx/plugins/Loose/Hand.dll");

            var records = new List<InstalledModRecord>
            {
                Record(1, "Other Mod", "BepInEx/plugins/Shared/Lib.dll"),
                Record(2, "My Mod", "BepInEx/plugins/Mine/Mine.dll"),
            };
            var target = new InstallTarget(2, false, "My Mod", null, null, null);

            var clashes = ModFileConflicts.Find(
                root,
                ["BepInEx/plugins/Shared/Lib.dll", "BepInEx/plugins/Mine/Mine.dll", "BepInEx/plugins/Loose/Hand.dll", "BepInEx/plugins/New/New.dll"],
                target,
                records);

            Assert.Equal(
                [new FileClash("BepInEx/plugins/Shared/Lib.dll", "Other Mod"), new FileClash("BepInEx/plugins/Loose/Hand.dll", null)],
                clashes);

            // The installing mod's own hand-installed folder is its own.
            var own = ModFileConflicts.Find(
                root, ["BepInEx/plugins/Loose/Hand.dll"], target, records,
                full => ModFileConflicts.IsInside(full, Path.Combine(root, "BepInEx", "plugins", "Loose")));
            Assert.Empty(own);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string root, string relative)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private static InstalledModRecord Record(int id, string name, params string[] files) => new()
    {
        ModId = id,
        Name = name,
        Version = "1.0.0",
        InstalledAt = DateTimeOffset.UnixEpoch,
        Files = [.. files],
    };
}
