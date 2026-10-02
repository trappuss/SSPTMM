using System.Security.Cryptography;
using Xunit;
using TCFModManager.Core.Services;

namespace TCFModManager.Core.Tests;

//
// D24 (CLOSED-10): every archive format a Forge download can be extracts the same tree. Before
// v1.19.0 SharpCompress's forward-only reader threw for non-solid archives other than 7z (tar, a
// non-solid RAR), so those installs failed.
//
// The fixtures were built with 7-Zip, RAR 5 and GNU tar from one tree of six files; each archive must
// give back those six files byte-for-byte.
//
public sealed class ArchiveExtractionTests : IDisposable
{
    private static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["BepInEx/plugins/ArchiveTest/ArchiveTest.dll"] = "cc85e75b5c0693ca380345da13ad9a6e48ba0a014ae4ca2965974b4e853cd300",
        ["BepInEx/plugins/ArchiveTest/file1.txt"] = "f88136787f1ac2f3b929c76674f8faaf5ca0da35308a846af3a86cb516c02b0e",
        ["BepInEx/plugins/ArchiveTest/file2.txt"] = "ede1b06a840095b7491d41641351767257ae7fc6f5dc57bbc565cc05ef895ce4",
        ["BepInEx/plugins/ArchiveTest/file3.txt"] = "9ede336668c893edafb4db42bac3e3b5fd13c8cb180cf0ae3bda8def8e7ea6bb",
        ["SPT/user/mods/ArchiveTest/config/config.json"] = "6b22a6d8e5746c0e3235bcbafa5a3a84d3755953ba960f65020281b9d4a51e14",
        ["SPT/user/mods/ArchiveTest/package.json"] = "318b863696005d558b53daf10e0e86cddb175de8d74d146fc18dc5b45086e2fe",
    };

    // <temp>\<guid>\extract is extracted into; <temp>\<guid>\target is where link.tar's link points.
    private readonly string _base = Path.Combine(Path.GetTempPath(), "tcfmm-extract-" + Guid.NewGuid().ToString("N"));
    private string _root => Path.Combine(_base, "extract");
    private string _target => Path.Combine(_base, "target");

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch (IOException) { }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Archives", name);

    [Theory]
    [InlineData("solid.7z")]
    [InlineData("nonsolid.7z")]
    [InlineData("solid.rar")]
    [InlineData("nonsolid.rar")]
    [InlineData("mod.tar")]
    [InlineData("mod.tar.gz")]
    public async Task Every_format_extracts_the_same_tree(string fixture)
    {
        await ModInstallService.ExtractArchiveAsync(Fixture(fixture), _root, status: null, CancellationToken.None);

        var actual = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => Path.GetRelativePath(_root, f).Replace('\\', '/'),
                f => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))),
                StringComparer.Ordinal);

        Assert.Equal(Expected.OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
    }

    // ../escape.txt - on the random-access path (tar) and the stream path (tar.gz) alike.
    [Theory]
    [InlineData("escape.tar")]
    [InlineData("escape.tar.gz")]
    public async Task An_entry_climbing_out_is_refused_and_nothing_lands_outside(string fixture)
    {
        var ex = await Assert.ThrowsAsync<ModInstallException>(() =>
            ModInstallService.ExtractArchiveAsync(Fixture(fixture), _root, status: null, CancellationToken.None));

        Assert.Equal(ModInstallFailure.UnsafeArchiveEntry, ex.Reason);
        Assert.False(File.Exists(Path.Combine(_base, "escape.txt")));
    }

    //
    // A tar holding a link (X/out -> ../../../../target) and then a file under it (X/out/pwned.txt).
    // Writing that file must never follow the link out of the extraction folder.
    //
    [Fact]
    public async Task A_link_in_an_archive_is_never_written_through()
    {
        Directory.CreateDirectory(_target);

        try
        {
            await ModInstallService.ExtractArchiveAsync(Fixture("link.tar"), _root, status: null, CancellationToken.None);

            // SharpCompress doesn't create links at all - and if a later version did, the install
            // refuses the tree (ArchiveContainsLink) before placing anything.
            Assert.Null(InstallPathGuard.FirstLink(_root));
        }
        catch (ModInstallException)
        {
            // Refusing the archive outright is fine too.
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(_target));
    }
}
