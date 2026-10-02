using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-11 step 2: C1 (one plugin GUID twice), C2 (one server GUID twice), C3 (one assembly, different copies).
public sealed class ModConflictFinderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-conflicts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static InstalledMod Client(string name, params string[] guids) => new()
    {
        Name = name,
        Target = InstalledModTarget.Client,
        FolderPath = Path.Combine("plugins", name),
        Guid = guids.FirstOrDefault(),
        Guids = guids,
    };

    private static InstalledMod Server(string name, string? guid) => new()
    {
        Name = name,
        Target = InstalledModTarget.Server,
        FolderPath = Path.Combine("mods", name),
        Guid = guid,
        Guids = guid is null ? [] : [guid],
    };

    private static List<IReadOnlyList<InstalledMod>> Mods(params InstalledMod[][] mods) => [.. mods];

    // ---- C1 --------------------------------------------------------------------------------------

    [Fact]
    public void C1_two_mods_with_one_plugin_guid_is_one_conflict_naming_both()
    {
        var a = Client("SAIN", "me.sol.sain");
        var b = Client("SAIN.4.4.3", "me.sol.sain");

        var conflict = Assert.Single(ModConflictFinder.Find(Mods([a], [b], [Client("Other", "x.other")])));

        Assert.Equal(ModConflictKind.DuplicatePlugin, conflict.Kind);
        Assert.Equal("me.sol.sain", conflict.Identifier);
        Assert.Equal([0, 1], conflict.Members.Select(m => m.ModIndex));
    }

    [Fact]
    public void C1_compares_every_guid_a_folder_registers_ignoring_case()
    {
        var a = Client("Bundle", "a.main", "shared.api");
        var b = Client("Api", "SHARED.API");

        var conflict = Assert.Single(ModConflictFinder.Find(Mods([a], [b])));

        Assert.Equal("shared.api", conflict.Identifier, ignoreCase: true);
    }

    [Fact]
    public void C1_a_disabled_copy_never_conflicts()
    {
        var a = Client("SAIN", "me.sol.sain");
        var b = new InstalledMod
        {
            Name = "SAIN", Target = InstalledModTarget.Client, FolderPath = "x", Guid = "me.sol.sain",
            Guids = ["me.sol.sain"], IsDisabled = true,
        };

        Assert.Empty(ModConflictFinder.Find(Mods([a], [b])));
    }

    [Fact]
    public void C1_one_mods_own_folders_never_conflict_with_each_other()
    {
        var a = Client("Repack", "me.sol.sain");
        var b = Client("RepackExtra", "me.sol.sain");

        Assert.Empty(ModConflictFinder.Find(Mods([a, b])));
    }

    [Fact]
    public void C1_patchers_carry_no_plugin_guid_to_compare()
    {
        var a = Client("P1", "same.guid");
        var patcher = new InstalledMod
        {
            Name = "P2", Target = InstalledModTarget.Client, FolderPath = "x", IsPatcher = true,
            Guid = "same.guid", Guids = ["same.guid"],
        };

        Assert.Empty(ModConflictFinder.Find(Mods([a], [patcher])));
    }

    // ---- C2 --------------------------------------------------------------------------------------

    [Fact]
    public void C2_two_server_folders_with_one_guid_is_one_conflict()
    {
        var conflict = Assert.Single(ModConflictFinder.Find(Mods(
            [Server("LotsOfLoot", "wtf.archangel.lotsoflootredux")],
            [Server("LotsOfLoot - Copy", "wtf.archangel.lotsoflootredux")])));

        Assert.Equal(ModConflictKind.DuplicateServerMod, conflict.Kind);
        Assert.Equal(2, conflict.Members.Count);
    }

    [Fact]
    public void C2_mods_with_no_declared_guid_are_not_compared()
    {
        Assert.Empty(ModConflictFinder.Find(Mods([Server("A", null)], [Server("B", null)])));
    }

    [Fact]
    public void C2_a_client_and_server_half_sharing_a_guid_are_not_a_conflict()
    {
        // Different loaders entirely - and usually the two halves of one mod.
        Assert.Empty(ModConflictFinder.Find(Mods([Client("Fika", "com.fika.core")], [Server("fika-server", "com.fika.core")])));
    }

    // ---- C3 --------------------------------------------------------------------------------------

    private InstalledMod WithDll(string name, string relative, string? assemblyName, string? version, string content, bool patcher = false, string? guid = null)
    {
        var folder = Path.Combine(_root, patcher ? "patchers" : "plugins", name);
        var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return new InstalledMod
        {
            Name = name,
            Target = InstalledModTarget.Client,
            IsPatcher = patcher,
            FolderPath = folder,
            Guid = guid,
            Guids = guid is null ? [] : [guid],
            Assemblies = [new ModAssembly(relative, assemblyName, version, new FileInfo(path).Length)],
        };
    }

    [Fact]
    public void C3_identical_copies_are_never_reported()
    {
        var a = WithDll("ModA", "libs/Newtonsoft.Json.dll", "Newtonsoft.Json", "13.0.0.0", "same bytes");
        var b = WithDll("ModB", "Newtonsoft.Json.dll", "Newtonsoft.Json", "13.0.0.0", "same bytes");

        Assert.Empty(ModConflictFinder.Find(Mods([a], [b])));
    }

    [Fact]
    public void C3_different_versions_are_reported_with_each_copy()
    {
        var a = WithDll("ModA", "Newtonsoft.Json.dll", "Newtonsoft.Json", "13.0.0.0", "v13");
        var b = WithDll("ModB", "Newtonsoft.Json.dll", "Newtonsoft.Json", "12.0.0.0", "v12");

        var conflict = Assert.Single(ModConflictFinder.Find(Mods([a], [b])));

        Assert.Equal(ModConflictKind.DifferentAssemblyCopies, conflict.Kind);
        Assert.Equal("Newtonsoft.Json", conflict.Identifier);
        Assert.Equal(["13.0.0.0", "12.0.0.0"], conflict.Members.Select(m => m.Assembly!.AssemblyVersion));
    }

    [Fact]
    public void C3_same_version_and_size_but_different_bytes_is_reported()
    {
        var a = WithDll("ModA", "Lib.dll", "Lib", "1.0.0.0", "aaaa");
        var b = WithDll("ModB", "Lib.dll", "Lib", "1.0.0.0", "bbbb");

        Assert.Single(ModConflictFinder.Find(Mods([a], [b])));
    }

    [Fact]
    public void C3_is_not_hashed_when_sizes_already_differ()
    {
        var a = WithDll("ModA", "Lib.dll", "Lib", "1.0.0.0", "short");
        var b = WithDll("ModB", "Lib.dll", "Lib", "1.0.0.0", "much longer");
        var hashed = 0;

        Assert.Single(ModConflictFinder.Find(Mods([a], [b]), _ => { hashed++; return "x"; }));
        Assert.Equal(0, hashed);
    }

    [Fact]
    public void C3_a_copy_that_cant_be_read_counts_as_different()
    {
        var a = WithDll("ModA", "Lib.dll", "Lib", "1.0.0.0", "same");
        var b = WithDll("ModB", "Lib.dll", "Lib", "1.0.0.0", "same");

        Assert.Single(ModConflictFinder.Find(Mods([a], [b]), _ => null));
    }

    [Fact]
    public void C3_native_dlls_are_matched_by_file_name()
    {
        var a = WithDll("ModA", "native.dll", null, null, "one");
        var b = WithDll("ModB", "x/native.dll", null, null, "two!");

        Assert.Equal("native.dll", Assert.Single(ModConflictFinder.Find(Mods([a], [b]))).Identifier);
    }

    [Fact]
    public void C3_patchers_are_compared_with_patchers_only()
    {
        var plugin = WithDll("ModA", "Lib.dll", "Lib", "1.0.0.0", "plugin copy");
        var patcherA = WithDll("PatcherA", "Lib.dll", "Lib", "2.0.0.0", "patcher copy", patcher: true);
        var patcherB = WithDll("PatcherB", "Lib.dll", "Lib", "3.0.0.0", "another patcher copy", patcher: true);

        var conflict = Assert.Single(ModConflictFinder.Find(Mods([plugin], [patcherA], [patcherB])));

        Assert.Equal([1, 2], conflict.Members.Select(m => m.ModIndex));
    }

    [Fact]
    public void C3_one_mods_own_copies_never_conflict()
    {
        var a = WithDll("ModA", "Lib.dll", "Lib", "1.0.0.0", "one");
        var b = WithDll("ModA2", "Lib.dll", "Lib", "2.0.0.0", "two");

        Assert.Empty(ModConflictFinder.Find(Mods([a, b])));
    }

    [Fact]
    public void C3_isnt_repeated_for_two_mods_already_reported_as_one_plugin()
    {
        var a = WithDll("SAIN", "SAIN.dll", "SAIN", "4.5.1.0", "new", guid: "me.sol.sain");
        var b = WithDll("SAIN.4.4.3", "SAIN.dll", "SAIN", "4.4.3.0", "old", guid: "me.sol.sain");

        var conflict = Assert.Single(ModConflictFinder.Find(Mods([a], [b])));

        Assert.Equal(ModConflictKind.DuplicatePlugin, conflict.Kind);
    }

    // ---- From a real scan ------------------------------------------------------------------------

    [Fact]
    public void A_real_scan_of_two_versions_of_one_assembly_finds_it()
    {
        var install = Path.Combine(_root, "install");
        string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModMetadata", name);

        void Put(string fixture, string relative)
        {
            var target = Path.Combine(install, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Fixture(fixture), target);
        }

        Put("TCFMM.ServerMap.Stub.Spt40.dll", "BepInEx/plugins/Old/Stub.dll");
        Put("TCFMM.ServerMap.Stub.Spt41.dll", "BepInEx/plugins/New/Stub.dll");
        Put("TCFModSync.Server.dll", "BepInEx/plugins/Same1/Sync.dll");
        Put("TCFModSync.Server.dll", "BepInEx/plugins/Same2/Sync.dll");

        var scanned = InstalledModScanner.Scan(install);
        var conflict = Assert.Single(ModConflictFinder.Find([.. scanned.Select(m => (IReadOnlyList<InstalledMod>)[m])]));

        Assert.Equal(ModConflictKind.DifferentAssemblyCopies, conflict.Kind);
        Assert.Equal("TCFMM.ServerMap.Stub", conflict.Identifier);
    }
}
