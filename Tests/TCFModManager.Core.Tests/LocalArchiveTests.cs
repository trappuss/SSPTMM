using System.IO.Compression;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class LocalArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcf-local-" + Guid.NewGuid().ToString("N"));

    public LocalArchiveTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Zip(params (string Path, string Content)[] files)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(content);
        }

        return path;
    }

    [Fact]
    public void ALocalId_IsNegative_AndTheSameForTheSameName()
    {
        var id = LocalArchive.IdFor("My Mod");

        Assert.True(id < 0);
        Assert.True(LocalArchive.IsLocalId(id));
        Assert.Equal(id, LocalArchive.IdFor(" my mod "));
        Assert.NotEqual(id, LocalArchive.IdFor("Other Mod"));
    }

    [Fact]
    public void ALink_CarriesThePathThere_AndOnlyALocalLinkIsReadBack()
    {
        var file = Path.Combine(_root, "a b.zip");

        Assert.True(LocalArchive.TryGetPath(LocalArchive.LinkFor(file), out var path));
        Assert.Equal(Path.GetFullPath(file), path);
        Assert.False(LocalArchive.TryGetPath("https://sp-mod.com/files/x.zip", out _));
        Assert.False(LocalArchive.TryGetPath(null, out _));
    }

    [Fact]
    public async Task AServerModArchive_IsRead_ThroughItsWrapperFolder()
    {
        var zip = Zip(
            ("readme.txt", "hi"),
            ("MyMod-1.2/user/mods/MyMod/package.json", """{ "name": "MyMod", "version": "1.2.0" }"""),
            ("MyMod-1.2/user/mods/MyMod/src/mod.js", "x"));

        var contents = await LocalArchive.InspectAsync(zip);

        Assert.True(contents.Recognised);
        Assert.Equal([("MyMod", "1.2.0")], contents.ServerMods);
        Assert.Empty(contents.Plugins);
    }

    [Fact]
    public async Task AnArchiveThatSaysNothingAboutWhereItGoes_IsNotRecognised()
    {
        var contents = await LocalArchive.InspectAsync(Zip(("Loose.dll", "x")));

        Assert.False(contents.Recognised);
    }

    private static Mod Listing(int id, string guid, params string[] versions) => new()
    {
        Id = id,
        Name = "Mod " + id,
        Guid = guid,
        Versions = [.. versions.Select((v, i) => new ModVersionSummary { Id = id * 100 + i, Version = v })],
    };

    [Fact]
    public void AnArchive_IsMatchedToTheOneListingItsPluginsBelongTo()
    {
        var catalog = new[] { Listing(1, "com.a.one", "1.0.0", "1.1.0-beta"), Listing(2, "com.b.two", "2.0.0") };
        var contents = new LocalArchiveContents(true, [("com.a.one", "1.1.0.0"), ("com.a.one.api", null)], []);

        var match = LocalArchive.MatchIn(contents, catalog);

        Assert.Equal(1, match?.Id);

        // The published version the DLL's own version is.
        Assert.Equal("1.1.0-beta", LocalArchive.VersionOf(contents, match));
    }

    [Fact]
    public void APackOfSeveralMods_OrNoneKnown_MatchesNothing()
    {
        var catalog = new[] { Listing(1, "com.a.one"), Listing(2, "com.b.two") };

        Assert.Null(LocalArchive.MatchIn(new LocalArchiveContents(true, [("com.a.one", "1.0"), ("com.b.two", "2.0")], []), catalog));
        Assert.Null(LocalArchive.MatchIn(new LocalArchiveContents(true, [("com.someone.else", "1.0")], []), catalog));

        // Its own version then, as the files report it.
        Assert.Equal("1.0", LocalArchive.VersionOf(new LocalArchiveContents(true, [("com.someone.else", "1.0")], []), null));
    }
}
