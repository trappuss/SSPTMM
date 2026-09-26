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

    private string Tar(bool gzip, params (string Path, string Content)[] files)
    {
        var source = Path.Combine(_root, "src-" + Guid.NewGuid().ToString("N"));
        foreach (var (entry, content) in files)
        {
            var full = Path.Combine(source, entry.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + (gzip ? ".tar.gz" : ".tar"));
        using (var output = File.Create(path))
        {
            if (gzip)
            {
                using var zipped = new GZipStream(output, CompressionLevel.Fastest);
                System.Formats.Tar.TarFile.CreateFromDirectory(source, zipped, includeBaseDirectory: false);
            }
            else
            {
                System.Formats.Tar.TarFile.CreateFromDirectory(source, output, includeBaseDirectory: false);
            }
        }

        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TarAndTarGz_AreRead(bool gzip)
    {
        var archive = Tar(gzip, ("user/mods/TarMod/package.json", """{ "name": "TarMod", "version": "3.0.0" }"""));

        var contents = await LocalArchive.InspectAsync(archive);

        Assert.True(contents.Recognised);
        Assert.Equal([("TarMod", "3.0.0")], contents.ServerMods);
    }

    [Fact]
    public void TheLocalId_FollowsWhatIsInside_NotTheFileName()
    {
        var mod = new LocalArchiveContents(true, [("com.x.mod", "1.0"), ("com.x.mod.api", "1.0")], []);
        var sameModNewer = new LocalArchiveContents(true, [("com.x.mod.api", "1.1"), ("COM.X.MOD", "1.1")], []);
        var other = new LocalArchiveContents(true, [("com.y.other", "1.0")], []);
        var nothing = new LocalArchiveContents(true, [], []);

        Assert.Equal(LocalArchive.IdFor(mod, "MyMod-1.0"), LocalArchive.IdFor(sameModNewer, "MyMod-1.1"));
        Assert.NotEqual(LocalArchive.IdFor(mod, "release"), LocalArchive.IdFor(other, "release"));
        Assert.Equal(LocalArchive.IdFor("release"), LocalArchive.IdFor(nothing, "release"));
    }

    [Fact]
    public async Task AnSpt4ServerMod_IsKnownByItsFolder_NotTheFileName()
    {
        var a = await LocalArchive.InspectAsync(Zip(("user/mods/ModA/ModA.dll", "a")));
        var b = await LocalArchive.InspectAsync(Zip(("user/mods/ModB/ModB.dll", "b")));

        Assert.Equal(["ModA"], a.ServerFolders);
        Assert.NotEqual(LocalArchive.IdFor(a, "release"), LocalArchive.IdFor(b, "release"));
    }
}
