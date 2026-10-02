using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-11 step 1: the scan lists every DLL BepInEx would load from an enabled client entry, with its
// assembly identity, for the conflict check (C3). The two Server Map stub fixtures are one assembly
// (TCFMM.ServerMap.Stub) at two versions - exactly the clash C3 is for.
//
public sealed class InstalledModAssembliesTests : IDisposable
{
    private readonly string _install = Path.Combine(Path.GetTempPath(), "tcfmm-assemblies-" + Guid.NewGuid().ToString("N"));

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModMetadata", name);

    public void Dispose()
    {
        try { Directory.Delete(_install, recursive: true); } catch (IOException) { }
    }

    private string Put(string fixture, string relative)
    {
        var target = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Fixture(fixture), target);
        return target;
    }

    private InstalledMod Only(string name) =>
        Assert.Single(InstalledModScanner.Scan(_install), m => m.Name == name);

    [Fact]
    public void Lists_every_dll_at_any_depth_with_its_identity()
    {
        Put("TCFMM.ServerMap.Stub.Spt41.dll", "BepInEx/plugins/ModA/ModA.dll");
        Put("TCFModSync.Server.dll", "BepInEx/plugins/ModA/libs/deep/Helper.dll");

        var mod = Only("ModA");

        Assert.Equal(2, mod.Assemblies.Count);
        var top = Assert.Single(mod.Assemblies, a => a.RelativePath == "ModA.dll");
        Assert.Equal("TCFMM.ServerMap.Stub", top.AssemblyName);
        Assert.Equal("0.2.1.0", top.AssemblyVersion);
        Assert.Equal(new FileInfo(Fixture("TCFMM.ServerMap.Stub.Spt41.dll")).Length, top.Size);

        var deep = Assert.Single(mod.Assemblies, a => a.RelativePath == "libs/deep/Helper.dll");
        Assert.Equal("Helper.dll", deep.FileName);
        Assert.Equal("TCFModSync.Server", deep.AssemblyName);
    }

    [Fact]
    public void A_native_dll_is_listed_by_file_name_with_no_identity()
    {
        var path = Path.Combine(_install, "BepInEx", "plugins", "ModB", "native.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a managed assembly");

        var assembly = Assert.Single(Only("ModB").Assemblies);

        Assert.Equal("native.dll", assembly.FileName);
        Assert.Null(assembly.AssemblyName);
        Assert.Null(assembly.AssemblyVersion);
    }

    [Fact]
    public void A_loose_dll_lists_itself()
    {
        Put("TCFMM.ServerMap.Stub.Spt40.dll", "BepInEx/plugins/Loose.dll");

        var assembly = Assert.Single(Only("Loose").Assemblies);

        Assert.Equal("Loose.dll", assembly.RelativePath);
        Assert.Equal("0.2.0.0", assembly.AssemblyVersion);
    }

    [Fact]
    public void Patchers_are_listed_too()
    {
        Put("TCFModSync.Server.dll", "BepInEx/patchers/SomePatcher/SomePatcher.dll");

        var patcher = Only("SomePatcher");

        Assert.True(patcher.IsPatcher);
        Assert.Single(patcher.Assemblies);
    }

    [Fact]
    public void Disabled_entries_and_server_mods_list_nothing()
    {
        Put("TCFModSync.Server.dll", "BepInEx/plugins.disabled/Off/Off.dll");
        Put("TCFModSync.Server.dll", "SPT/user/mods/ServerMod/ServerMod.dll");
        File.WriteAllText(Path.Combine(_install, "SPT", "SPT.Server.exe"), "server");

        var mods = InstalledModScanner.Scan(_install);

        Assert.Empty(Assert.Single(mods, m => m.Name == "Off").Assemblies);
        Assert.All(mods.Where(m => m.Target == InstalledModTarget.Server), m => Assert.Empty(m.Assemblies));
    }

    [Fact]
    public void A_linked_folder_inside_a_mod_is_not_followed()
    {
        Put("TCFModSync.Server.dll", "BepInEx/plugins/ModC/ModC.dll");
        var elsewhere = Path.Combine(_install, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.Copy(Fixture("TCFModSync.Server.dll"), Path.Combine(elsewhere, "Outside.dll"));
        Directory.CreateSymbolicLink(Path.Combine(_install, "BepInEx", "plugins", "ModC", "link"), elsewhere);

        var assembly = Assert.Single(Only("ModC").Assemblies);

        Assert.Equal("ModC.dll", assembly.RelativePath);
    }
}
