using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// SafeFile is what stands between a crash or a power cut and the user's settings, collections and
// install records. These pin down the promises it makes.
public class SafeFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "safefile-" + Guid.NewGuid().ToString("N"));

    public SafeFileTests()
    {
        Directory.CreateDirectory(_dir);
    }

    // SafeFile's record is shared by every test running at the same time; each looks at its own folder.
    private IReadOnlyList<SafeFileProblem> Problems =>
        [.. SafeFile.Problems.Where(p => p.Path.StartsWith(_dir, StringComparison.OrdinalIgnoreCase))];

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static ModListData? Parse(string json) => JsonSerializer.Deserialize<ModListData>(json);

    [Fact]
    public void Write_KeepsThePreviousVersionAsBackup_AndLeavesNoTempFile()
    {
        var path = PathOf("data.json");
        SafeFile.WriteAllText(path, "first", keepBackup: true);
        SafeFile.WriteAllText(path, "second", keepBackup: true);

        Assert.Equal("second", File.ReadAllText(path));
        Assert.Equal("first", File.ReadAllText(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Write_WithoutBackup_WritesNoBackup()
    {
        var path = PathOf("cache.json");
        SafeFile.WriteAllText(path, "a");
        SafeFile.WriteAllText(path, "b");

        Assert.Equal("b", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Read_ADamagedFile_KeepsItAside_AndPutsTheBackupBack()
    {
        // What a power cut during a save used to leave: half a file, read as "nothing" and saved over.
        var path = PathOf("mod_lists.json");
        SafeFile.WriteAllText(path, """{ "SchemaVersion": 1, "Lists": [] }""", keepBackup: true);
        SafeFile.WriteAllText(path, """{ "SchemaVersion": 1, "Lists": [ { "Name": "Mine" } ] }""", keepBackup: true);
        File.WriteAllText(path, """{ "SchemaVersion": 1, "Lis""");

        var read = SafeFile.ReadJson(path, Parse);

        Assert.NotNull(read);
        var problem = Assert.Single(Problems);
        Assert.True(problem.RestoredFromBackup);
        Assert.NotNull(problem.KeptAs);
        Assert.Equal("""{ "SchemaVersion": 1, "Lis""", File.ReadAllText(problem.KeptAs!));

        // The good copy is back in place, so the next read is clean.
        Assert.NotNull(SafeFile.ReadJson(path, Parse));
        Assert.Single(Problems);
    }

    [Fact]
    public void Read_ADamagedFileWithNoBackup_KeepsItAside_AndReportsIt()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, "{ not json");

        var read = SafeFile.ReadJson(path, json => JsonSerializer.Deserialize<AppSettings>(json));

        Assert.Null(read);
        var problem = Assert.Single(Problems);
        Assert.False(problem.RestoredFromBackup);
        Assert.True(File.Exists(problem.KeptAs));

        // Saving afterwards is allowed: the damaged copy is safe elsewhere.
        Assert.True(SafeFile.WriteAllText(path, "{}", keepBackup: true));
    }

    [Fact]
    public void Read_AFileHeldOpen_RefusesToSaveOverIt()
    {
        if (!OperatingSystem.IsWindows()) return; // FileShare.None is only enforced there.

        var path = PathOf("installed-mods.json");
        File.WriteAllText(path, """{ "Mods": [] }""");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(SafeFile.ReadJson(path, json => JsonSerializer.Deserialize<ModInstallManifest>(json)));
        }

        Assert.True(Assert.Single(Problems).CouldNotRead);
        Assert.False(SafeFile.WriteAllText(path, "{}", keepBackup: true));
        Assert.Equal("""{ "Mods": [] }""", File.ReadAllText(path));
    }

    [Fact]
    public void ModListStore_ATornFile_DoesNotLoseTheLists()
    {
        // The audit's reproduction: two lists stored, the file cut in half, then an unrelated save.
        // Before, that save wrote back an empty file and both lists were gone for good.
        var path = PathOf("lists.json");
        var store = new ModListStore(path);
        var data = store.Load();
        data.Lists.Add(new ModList { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Name = "One" });
        store.Save(data);
        data.Lists.Add(new ModList { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Name = "Two" });
        store.Save(data);

        var text = File.ReadAllText(path);
        File.WriteAllText(path, text[..(text.Length / 2)]);

        var loaded = store.Load();
        store.Save(loaded);

        Assert.Single(new ModListStore(path).Load().Lists); // the last good save: "One"
        Assert.NotEmpty(Directory.GetFiles(_dir, "lists.json.corrupt-*"));
    }
}
