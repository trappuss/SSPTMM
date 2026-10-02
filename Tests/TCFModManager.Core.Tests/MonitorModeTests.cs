using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ArchiveLayoutTests
{
    private static List<ArchiveFileEntry> Entries(params string[] paths) =>
        [.. paths.Select(p => new ArchiveFileEntry(p, 10))];

    [Fact]
    public void Prefix_IsEmptyWhenContentSitsAtTheTop()
    {
        Assert.Equal("", ArchiveLayout.FindContentPrefix(["BepInEx/plugins/Foo/Foo.dll", "user/mods/Foo/package.json"]));
    }

    [Fact]
    public void Prefix_DescendsThroughWrapperFolders()
    {
        Assert.Equal("Outer/Inner/", ArchiveLayout.FindContentPrefix(["Outer/Inner/BepInEx/plugins/Foo.dll"]));
    }

    [Fact]
    public void Prefix_StopsAtALoneKnownRoot()
    {
        Assert.Equal("", ArchiveLayout.FindContentPrefix(["BepInEx/plugins/Foo/Foo.dll"]));
    }

    [Fact]
    public void Prefix_StopsWhenAFileSitsBesideTheWrapper()
    {
        Assert.Equal("", ArchiveLayout.FindContentPrefix(["Extra.dll", "Wrapper/BepInEx/plugins/Foo.dll"]));
    }

    // Fork: a read-me or a picture beside the wrapper is the archive's, not content - see ForkLayoutTests.
    [Fact]
    public void Prefix_LooksThroughAWrapperWithAReadMeBesideIt()
    {
        Assert.Equal("Wrapper/", ArchiveLayout.FindContentPrefix(["readme.txt", "Wrapper/BepInEx/plugins/Foo.dll"]));
    }

    [Fact]
    public void Plan_StripsTheWrapperAndNamesTheFolders()
    {
        var plan = ArchiveLayout.Plan(
            Entries("Foo-1.2.1/BepInEx/plugins/Foo/Foo.dll", "Foo-1.2.1/user/mods/FooServer/package.json"), "");

        Assert.True(plan.Recognised);
        Assert.Equal(["BepInEx/plugins/Foo/Foo.dll", "user/mods/FooServer/package.json"], plan.Files.Select(f => f.Path));
        Assert.Equal(["Foo", "FooServer"], plan.Folders);
    }

    [Theory]
    [InlineData("SPT")]
    [InlineData("SPT_Runtime")]
    public void Plan_RemapsUserUnderTheServerRootAndLeavesBepInExAlone(string serverRoot)
    {
        var plan = ArchiveLayout.Plan(Entries("BepInEx/plugins/Foo/Foo.dll", "user/mods/Foo/package.json"), serverRoot);

        Assert.Equal(
            ["BepInEx/plugins/Foo/Foo.dll", $"{serverRoot}/user/mods/Foo/package.json"],
            plan.Files.Select(f => f.Path));
    }

    [Fact]
    public void Plan_NormalisesBackslashesAndDotPrefixes()
    {
        var plan = ArchiveLayout.Plan(Entries(@".\BepInEx\plugins\Foo\Foo.dll"), "");

        Assert.Equal("BepInEx/plugins/Foo/Foo.dll", Assert.Single(plan.Files).Path);
    }

    [Fact]
    public void Plan_IsUnrecognisedWithNoKnownRoot()
    {
        var plan = ArchiveLayout.Plan(Entries("Foo/Foo.dll", "Foo/readme.txt"), "");

        Assert.False(plan.Recognised);
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void Plan_IsUnrecognisedWhenAnEntryEscapesTheRoot()
    {
        Assert.False(ArchiveLayout.Plan(Entries("BepInEx/plugins/Foo.dll", "../evil.dll"), "").Recognised);
        Assert.False(ArchiveLayout.Plan(Entries("/abs/BepInEx/plugins/Foo.dll"), "").Recognised);
    }

    [Fact]
    public void Plan_KeepsEachFilesSize()
    {
        var plan = ArchiveLayout.Plan([new ArchiveFileEntry("BepInEx/plugins/Foo.dll", 1234)], "");

        Assert.Equal(1234, Assert.Single(plan.Files).Size);
    }
}

public class ArchiveFileTests : IDisposable
{
    // A 7z holding Wrapper/BepInEx/plugins/Foo/Foo.dll (10 bytes) and Wrapper/user/mods/Foo/package.json.
    internal const string SevenZipBase64 =
        "N3q8ryccAAR05jgjwQAAAAAAAAAWAAAAAAAAAFHZo5UBABcwMTIzNDU2Nzg5eyJuYW1lIjoiZm9vIn0A4ADnAJ1dAACBMweuD8/Hr2gP1GpefeXX3Qg9eTikvyId0jYG7Wx3v/9WfX61aVBQSi1GqanZmQ+V6fuzwL3UgYXjSikIPL6YN0pyc7tgowDoo/AKPtr+ari5+tB/E7CekgRBJHhjGeUx1gu4bsTF5v/HZ4VzEjCKq1VnAAh8BQs04/JFYJnHliUmIzADqBLewBgwsP8mM9qFDDlUy6gKC+wAAAAAFwYcAQmApQAHCwEAASEhARgMgOgAAA==";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TCFModManagerArchiveFileTests_" + Guid.NewGuid());

    public ArchiveFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    internal static byte[] Zip(params (string Path, string Content)[] files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ListsAZip()
    {
        var path = Write("a.bin", Zip(("Foo-1.0/BepInEx/plugins/Foo/Foo.dll", "0123456789")));

        var plan = ArchiveLayout.Plan(path, "");

        Assert.Equal(ArchiveKind.Zip, ArchiveLayout.Detect(path));
        var file = Assert.Single(plan.Files);
        Assert.Equal("BepInEx/plugins/Foo/Foo.dll", file.Path);
        Assert.Equal(10, file.Size);
    }

    [Fact]
    public void ListsASevenZipWithANestedRoot()
    {
        var path = Write("b.bin", Convert.FromBase64String(SevenZipBase64));

        var plan = ArchiveLayout.Plan(path, "SPT");

        Assert.Equal(ArchiveKind.SevenZip, ArchiveLayout.Detect(path));
        Assert.True(plan.Recognised);
        Assert.Equal(
            ["BepInEx/plugins/Foo/Foo.dll", "SPT/user/mods/Foo/package.json"],
            plan.Files.Select(f => f.Path).Order());
        Assert.Equal(10, plan.Files.Single(f => f.Path.EndsWith("Foo.dll")).Size);
    }

    [Fact]
    public void AFileThatIsNoArchiveIsUnrecognisedRatherThanThrown()
    {
        var path = Write("c.bin", "not an archive at all"u8.ToArray());

        Assert.False(ArchiveLayout.Plan(path, "").Recognised);
    }

    [Fact]
    public void DetectsRarByItsSignature()
    {
        var path = Write("d.bin", [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00]);

        Assert.Equal(ArchiveKind.Rar, ArchiveLayout.Detect(path));
    }
}

public class DownloadFoldersTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TCFModManagerDownloadFoldersTests_" + Guid.NewGuid());

    public DownloadFoldersTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void UsesTheOfferedName()
    {
        Assert.Equal("Foo_1.2.1.7z", DownloadFolders.FileNameFor("Foo_1.2.1.7z", "Foo", "1.2.1", ArchiveKind.Zip));
    }

    [Fact]
    public void GivesAnOfferedNameWithoutAnExtensionTheSniffedOne()
    {
        Assert.Equal("Foo.7z", DownloadFolders.FileNameFor("Foo", "Foo", "1.2.1", ArchiveKind.SevenZip));
    }

    [Fact]
    public void FallsBackToModAndVersion()
    {
        Assert.Equal("Epic's AIO-1.2.1.zip", DownloadFolders.FileNameFor(null, "Epic's AIO", "1.2.1", ArchiveKind.Zip));
    }

    [Theory]
    [InlineData("A/B:C*D?.zip", "A_B_C_D_.zip")]
    [InlineData("name. . ", "name")]
    [InlineData("CON.zip", "_CON.zip")]
    [InlineData("   ", "download")]
    public void MakesNamesSafeForWindows(string name, string expected)
    {
        Assert.Equal(expected, DownloadFolders.SafeName(name));
    }

    [Fact]
    public void NeverOverwritesAnExistingFile()
    {
        File.WriteAllText(Path.Combine(_directory, "Foo.zip"), "");
        File.WriteAllText(Path.Combine(_directory, "Foo (2).zip"), "");

        Assert.Equal(Path.Combine(_directory, "Foo (3).zip"), DownloadFolders.UniquePath(_directory, "Foo.zip"));
    }

    [Fact]
    public void ResolvesANullSettingToADownloadsFolder()
    {
        Assert.False(string.IsNullOrWhiteSpace(DownloadFolders.Resolve(null)));
        Assert.Equal(@"D:\Mods", DownloadFolders.Resolve(@" D:\Mods "));
    }
}

public class DownloadLedgerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TCFModManagerDownloadLedgerTests_" + Guid.NewGuid());

    private readonly DownloadLedgerService _ledger;

    public DownloadLedgerTests()
    {
        Directory.CreateDirectory(_directory);
        _ledger = new DownloadLedgerService(Path.Combine(_directory, "downloads.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    internal static DownloadedModRecord Download(int modId = 1, string version = "1.0.0", bool isAddon = false) => new()
    {
        ModId = modId,
        IsAddon = isAddon,
        Name = "Foo",
        Version = version,
        DownloadedAt = DateTimeOffset.UtcNow,
        ArchivePath = @"C:\Downloads\Foo.zip",
        ExpectedFolders = ["Foo"],
        ExpectedFiles = [new ExpectedFile("BepInEx/plugins/Foo/Foo.dll", 10)],
    };

    [Fact]
    public void ANewerDownloadReplacesTheOlderWhateverItsState()
    {
        _ledger.Record(Download(version: "1.0.0"));
        _ledger.SetState(1, false, "1.0.0", DownloadState.Confirmed);
        _ledger.Record(Download(version: "1.1.0"));

        var only = Assert.Single(_ledger.Load().Downloads);
        Assert.Equal("1.1.0", only.Version);
        Assert.Equal(DownloadState.Pending, only.State);
    }

    [Fact]
    public void AModAndAnAddonWithTheSameIdAreSeparate()
    {
        _ledger.Record(Download(modId: 116));
        _ledger.Record(Download(modId: 116, isAddon: true));

        Assert.Equal(2, _ledger.Load().Downloads.Count);
    }

    [Fact]
    public void SettlingAnOlderVersionLeavesTheNewerPending()
    {
        _ledger.Record(Download(version: "1.1.0"));

        Assert.False(_ledger.SetState(1, false, "1.0.0", DownloadState.Dismissed));
        Assert.Single(_ledger.Pending());
    }

    [Fact]
    public void RoundTripsTheExpectedFilesAndStateAsAName()
    {
        _ledger.Record(Download());
        _ledger.SetState(1, false, "1.0.0", DownloadState.Dismissed);

        var json = File.ReadAllText(Path.Combine(_directory, "downloads.json"));
        var loaded = _ledger.Find(1, false)!;

        Assert.Contains("\"Dismissed\"", json);
        Assert.Equal(10, Assert.Single(loaded.ExpectedFiles).Size);
    }

    [Fact]
    public void ACorruptFileReadsAsEmpty()
    {
        File.WriteAllText(Path.Combine(_directory, "downloads.json"), "{ not json");

        Assert.Empty(_ledger.Load().Downloads);
    }
}

public class DownloadMatcherTests : IDisposable
{
    private readonly string _install =
        Path.Combine(Path.GetTempPath(), "TCFModManagerDownloadMatcherTests_" + Guid.NewGuid());

    public DownloadMatcherTests() => Directory.CreateDirectory(_install);

    public void Dispose()
    {
        if (Directory.Exists(_install)) Directory.Delete(_install, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void Place(string relative, int bytes)
    {
        var path = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    [Fact]
    public void NothingInTheScanIsAbsent()
    {
        Assert.Equal(DownloadMatch.Absent, DownloadMatcher.Check(DownloadLedgerTests.Download(), _install, ["Other"]));
    }

    [Fact]
    public void EveryFileAtItsSizeIsInstalled()
    {
        Place("BepInEx/plugins/Foo/Foo.dll", 10);

        Assert.Equal(DownloadMatchKind.Installed,
            DownloadMatcher.Check(DownloadLedgerTests.Download(), _install, ["foo"]).Kind);
    }

    [Fact]
    public void TheOldVersionStillThereIsPartial()
    {
        Place("BepInEx/plugins/Foo/Foo.dll", 8);

        var match = DownloadMatcher.Check(DownloadLedgerTests.Download(), _install, ["Foo"]);

        Assert.Equal(DownloadMatchKind.Partial, match.Kind);
        Assert.Equal(1, match.DifferingFiles);
    }

    [Fact]
    public void AFolderWithFilesMissingIsPartial()
    {
        var download = DownloadLedgerTests.Download();
        download.ExpectedFiles.Add(new ExpectedFile("BepInEx/plugins/Foo/Extra.dll", 4));
        Place("BepInEx/plugins/Foo/Foo.dll", 10);

        Assert.Equal(1, DownloadMatcher.Check(download, _install, ["Foo"]).MissingFiles);
    }

    private static InstalledMod Scanned(string name, string folderPath, bool disabled = false) => new()
    {
        Name = name,
        FolderPath = folderPath,
        Target = InstalledModTarget.Client,
        IsDisabled = disabled,
    };

    [Fact]
    public void FolderNamesCoverTheFolderAndALooseDllWithoutItsExtension()
    {
        var names = DownloadMatcher.FolderNames(
        [
            Scanned("Epic's All in One", Path.Combine("BepInEx", "plugins", "EpicsAIO")),
            Scanned("Loose", Path.Combine("BepInEx", "plugins", "LooseMod.dll")),
        ]);

        Assert.Contains("EpicsAIO", names);
        Assert.Contains("LooseMod", names);
        Assert.Contains("Epic's All in One", names);
    }

    [Fact]
    public void ADisabledCopyIsNotAHandInstall()
    {
        var names = DownloadMatcher.FolderNames([Scanned("Foo", Path.Combine("BepInEx", "plugins.disabled", "Foo"), disabled: true)]);

        Assert.Equal(DownloadMatch.Absent, DownloadMatcher.Check(DownloadLedgerTests.Download(), _install, names));
    }

    [Fact]
    public void AnUnrecognisedDownloadNeverMatches()
    {
        Place("BepInEx/plugins/Foo/Foo.dll", 10);
        var download = new DownloadedModRecord
        {
            ModId = 1, Name = "Foo", Version = "1", DownloadedAt = DateTimeOffset.UtcNow,
            ArchivePath = "x", Unrecognised = true,
        };

        Assert.Equal(DownloadMatch.Absent, DownloadMatcher.Check(download, _install, ["Foo"]));
    }
}

public class ConfirmDownloadTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TCFModManagerConfirmDownloadTests_" + Guid.NewGuid());

    private readonly ModInstallManifestService _manifest;

    public ConfirmDownloadTests()
    {
        Directory.CreateDirectory(_directory);
        _manifest = new ModInstallManifestService(Path.Combine(_directory, "installed-mods.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AHandInstallWithNoRecordIsNotAppManaged()
    {
        var record = _manifest.ConfirmDownload(DownloadLedgerTests.Download(version: "1.2.1"));

        Assert.False(record.IsAppManaged);
        Assert.Empty(record.Files);
        Assert.Equal(["Foo"], record.Folders);
        Assert.Equal("1.2.1", record.Version);
    }

    [Fact]
    public void OverAnAppManagedRecordItStaysManagedAndTakesTheNewFileList()
    {
        _manifest.Save(new ModInstallManifest
        {
            Mods =
            [
                new InstalledModRecord
                {
                    ModId = 1, Name = "Foo", Version = "1.0.0", InstalledAt = DateTimeOffset.UtcNow,
                    Files = ["BepInEx/plugins/Foo/Old.dll"], Folders = ["Foo"], Incomplete = true,
                },
            ],
        });

        var record = _manifest.ConfirmDownload(DownloadLedgerTests.Download(version: "1.2.1"));

        Assert.True(record.IsAppManaged);
        Assert.False(record.Incomplete);
        Assert.Equal(["BepInEx/plugins/Foo/Foo.dll"], record.Files);
        Assert.Equal("1.2.1", Assert.Single(_manifest.Load().Mods).Version);
    }
}

public class ModArchiveServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TCFModManagerModArchiveServiceTests_" + Guid.NewGuid());

    private readonly string _folder;
    private readonly DownloadLedgerService _ledger;

    public ModArchiveServiceTests()
    {
        _folder = Path.Combine(_directory, "Downloads");
        Directory.CreateDirectory(_folder);
        _ledger = new DownloadLedgerService(Path.Combine(_directory, "downloads.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class ArchiveHandler(byte[] body, string? fileName, long? claimedLength = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new ByteArrayContent(body);
            if (fileName is not null)
                content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
            if (claimedLength is not null) content.Headers.ContentLength = claimedLength;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private ModArchiveService Service(byte[] body, string? fileName = null, long? claimedLength = null) =>
        new(new ModDownloadService(new HttpClient(new ArchiveHandler(body, fileName, claimedLength))), _ledger);

    private static readonly InstallTarget Target = new(7, false, "Foo", "com.foo", null, null);

    private static ModVersion Version(string? link = "https://example.test/f") =>
        new() { Id = 70, Version = "1.2.1", Link = link };

    private static byte[] FooZip() => ArchiveFileTests.Zip(("BepInEx/plugins/Foo/Foo.dll", "0123456789"));

    [Fact]
    public async Task SavesUnderTheOfferedNameAndRecordsWhatItWouldPlace()
    {
        var result = await Service(FooZip(), "Foo_v1.2.1.zip").SaveArchiveAsync(Target, Version(), _folder, null);

        Assert.Equal(Path.Combine(_folder, "Foo_v1.2.1.zip"), result.Record.ArchivePath);
        Assert.True(File.Exists(result.Record.ArchivePath));
        Assert.Empty(Directory.GetFiles(_folder, "*.part"));

        var stored = _ledger.Find(7, false)!;
        Assert.Equal(DownloadState.Pending, stored.State);
        Assert.Equal(["Foo"], stored.ExpectedFolders);
        Assert.Equal(new ExpectedFile("BepInEx/plugins/Foo/Foo.dll", 10), Assert.Single(stored.ExpectedFiles));
    }

    [Fact]
    public async Task FallsBackToModAndVersionAndNeverOverwrites()
    {
        File.WriteAllText(Path.Combine(_folder, "Foo-1.2.1.zip"), "the user's own file");

        var result = await Service(FooZip()).SaveArchiveAsync(Target, Version(), _folder, null);

        Assert.Equal(Path.Combine(_folder, "Foo-1.2.1 (2).zip"), result.Record.ArchivePath);
        Assert.Equal("the user's own file", File.ReadAllText(Path.Combine(_folder, "Foo-1.2.1.zip")));
    }

    [Fact]
    public async Task PutsAListsDownloadsInItsOwnSubfolder()
    {
        var result = await Service(FooZip()).SaveArchiveAsync(Target, Version(), _folder, null, subfolder: "My: list");

        Assert.Equal(Path.Combine(_folder, "My_ list"), Path.GetDirectoryName(result.Record.ArchivePath));
    }

    [Fact]
    public async Task AnUnrecognisedArchiveIsStillSaved()
    {
        var result = await Service(ArchiveFileTests.Zip(("Foo/Foo.dll", "x"))).SaveArchiveAsync(Target, Version(), _folder, null);

        Assert.True(File.Exists(result.Record.ArchivePath));
        Assert.True(result.Record.Unrecognised);
    }

    [Fact]
    public async Task AMissingFolderIsRefusedBeforeAnythingIsFetched()
    {
        var missing = Path.Combine(_directory, "Gone");

        var ex = await Assert.ThrowsAsync<ModInstallException>(
            () => Service(FooZip()).SaveArchiveAsync(Target, Version(), missing, null));

        Assert.Equal(ModInstallFailure.DownloadFolderMissing, ex.Reason);
        Assert.Equal(missing, ex.Folder);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task NoLinkIsRefused()
    {
        var ex = await Assert.ThrowsAsync<ModInstallException>(
            () => Service(FooZip()).SaveArchiveAsync(Target, Version(link: null), _folder, null));

        Assert.Equal(ModInstallFailure.NoDownloadLink, ex.Reason);
    }

    [Fact]
    public async Task AShortDownloadLeavesNothingBehind()
    {
        var body = FooZip();

        var ex = await Assert.ThrowsAsync<ModInstallException>(
            () => Service(body, "Foo.zip", claimedLength: body.Length + 100).SaveArchiveAsync(Target, Version(), _folder, null));

        Assert.Equal(ModInstallFailure.DownloadIncomplete, ex.Reason);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Null(_ledger.Find(7, false));
    }
}

public class MonitorSettingsTests
{
    [Fact]
    public void DefaultsToInstallingIntoTheOsDownloadsFolder()
    {
        var upgraded = JsonSerializer.Deserialize<AppSettings>("""{ "SptInstallPath": "C:\\SPT" }""")!;

        Assert.Equal(InstallMode.Install, upgraded.Monitor.InstallMode);
        Assert.Null(upgraded.Monitor.DownloadFolder);
        Assert.Equal(DownloadConfirmation.Ask, upgraded.Monitor.DownloadConfirmation);
        Assert.True(upgraded.Monitor.DownloadListSubfolders);
    }

    [Fact]
    public void WritesTheModesAsNames()
    {
        var settings = new AppSettings();
        settings.Monitor.InstallMode = InstallMode.DownloadOnly;
        settings.Monitor.DownloadConfirmation = DownloadConfirmation.MarkQuietly;

        var json = JsonSerializer.Serialize(settings);
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Contains("\"DownloadOnly\"", json);
        Assert.Contains("\"MarkQuietly\"", json);
        Assert.Equal(InstallMode.DownloadOnly, back.Monitor.InstallMode);
    }

    [Fact]
    public void AHandWrittenNullIsNotANullObject()
    {
        Assert.NotNull(JsonSerializer.Deserialize<AppSettings>("""{ "Monitor": null }""")!.Monitor);
    }
}
