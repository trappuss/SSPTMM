using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class InstalledModFoldersTests
{
    [Fact]
    public void FromPlacedFiles_FindsServerModFolders()
    {
        var folders = InstalledModFolders.FromPlacedFiles(
        [
            "user/mods/EpicsAIO/package.json",
            "user/mods/EpicsAIO/src/mod.js",
        ]);

        Assert.Equal(["EpicsAIO"], folders);
    }

    [Fact]
    public void FromPlacedFiles_FindsServerModFoldersUnderARemappedServerRoot()
    {
        // Server content is remapped under whatever the install calls its server root.
        var folders = InstalledModFolders.FromPlacedFiles(["SPT_Runtime/user/mods/EpicsAIO/package.json"]);

        Assert.Equal(["EpicsAIO"], folders);
    }

    [Fact]
    public void FromPlacedFiles_FindsClientPluginAndPatcherFolders()
    {
        var folders = InstalledModFolders.FromPlacedFiles(
        [
            "BepInEx/plugins/WTT-ClientCommonLib/WTT-ClientCommonLib.dll",
            "BepInEx/patchers/SomePatcher/SomePatcher.dll",
        ]);

        Assert.Equal(["WTT-ClientCommonLib", "SomePatcher"], folders);
    }

    [Fact]
    public void FromPlacedFiles_NamesALooseDllAfterTheFileItself()
    {
        // The scanner reports a loose top-level DLL by its file name, so this has to agree.
        var folders = InstalledModFolders.FromPlacedFiles(["BepInEx/plugins/SomeMod.dll"]);

        Assert.Equal(["SomeMod"], folders);
    }

    [Fact]
    public void FromPlacedFiles_ReturnsBothHalvesOfASplitModOnce()
    {
        var folders = InstalledModFolders.FromPlacedFiles(
        [
            "BepInEx/plugins/WTT-ClientCommonLib/WTT-ClientCommonLib.dll",
            "BepInEx/plugins/WTT-ClientCommonLib/extra.dll",
            "user/mods/WTT-ServerCommonLib/package.json",
        ]);

        Assert.Equal(["WTT-ClientCommonLib", "WTT-ServerCommonLib"], folders);
    }

    [Fact]
    public void FromPlacedFiles_IgnoresFilesOutsideAKnownContainer()
    {
        Assert.Empty(InstalledModFolders.FromPlacedFiles(["SPT_Data/Server/database/x.json", "readme.txt"]));
    }

    [Fact]
    public void Resolve_PrefersTheStoredFolders()
    {
        var record = NewRecord(files: ["user/mods/Derived/package.json"], folders: ["Stored"]);

        Assert.Equal(["Stored"], InstalledModFolders.Resolve(record));
    }

    [Fact]
    public void Resolve_FallsBackToTheFileListForRecordsWrittenBeforeFoldersWereStored()
    {
        var record = NewRecord(files: ["user/mods/EpicsAIO/package.json"], folders: []);

        Assert.Equal(["EpicsAIO"], InstalledModFolders.Resolve(record));
    }

    [Fact]
    public void PlacedFilesUnder_ReturnsOnlyThatFolderSFilesRelativeToIt()
    {
        var record = NewRecord(
            files:
            [
                "BepInEx/plugins/HollywoodFX/HollywoodFX.dll",
                "BepInEx/plugins/HollywoodGraphics/HollywoodGraphics.dll",
                "BepInEx/plugins/HollywoodGraphics/bloom/LensDust1.png",
            ],
            folders: ["HollywoodFX", "HollywoodGraphics"]);

        Assert.Equal(["HollywoodGraphics.dll", "bloom/LensDust1.png"],
            InstalledModFolders.PlacedFilesUnder(record, "HollywoodGraphics"));
    }

    [Fact]
    public void PlacedFilesUnder_ReportsALooseDllAsTheOnlyFileInItsOwnFolder()
    {
        var record = NewRecord(files: ["BepInEx/plugins/SomeMod.dll"], folders: ["SomeMod"]);

        Assert.Equal(["SomeMod.dll"], InstalledModFolders.PlacedFilesUnder(record, "SomeMod"));
    }

    [Fact]
    public void PlacedFilesUnder_IsEmptyForAFolderTheRecordNeverPlacedFilesIn()
    {
        var record = NewRecord(files: ["user/mods/EpicsAIO/package.json"], folders: ["EpicsAIO"]);

        Assert.Empty(InstalledModFolders.PlacedFilesUnder(record, "SomethingElse"));
    }

    private static InstalledModRecord NewRecord(List<string> files, List<string> folders) => new()
    {
        ModId = 1263,
        Guid = "com.epicrangetime.aio",
        Name = "Epic's All in One",
        VersionId = 13812,
        Version = "4.0.8",
        InstalledAt = DateTimeOffset.UnixEpoch,
        Files = files,
        Folders = folders,
    };

    [Fact]
    public void MissingFrom_ReportsTheFoldersTheScanDidNotFind()
    {
        // Half-installed: the server folder is where this install looks, the client one never
        // arrived - what an install under the wrong root leaves behind.
        var record = new InstalledModRecord
        {
            ModId = 2426,
            Name = "MoreBotsAPI",
            Version = "2.0.1",
            InstalledAt = DateTimeOffset.UtcNow,
            Folders = ["MoreBotsPrepatch", "MoreBotsAPI", "MoreBotsServer"],
        };

        var missing = InstalledModFolders.MissingFrom(record, ["MoreBotsServer"]);

        Assert.Equal(["MoreBotsPrepatch", "MoreBotsAPI"], missing);
    }

    [Fact]
    public void MissingFrom_IsEmptyWhenEveryFolderIsThere()
    {
        var record = new InstalledModRecord
        {
            ModId = 2426,
            Name = "MoreBotsAPI",
            Version = "2.0.1",
            InstalledAt = DateTimeOffset.UtcNow,
            Folders = ["MoreBotsAPI", "MoreBotsServer"],
        };

        // Case-insensitively, and an extra folder on disk is not this record's business.
        Assert.Empty(InstalledModFolders.MissingFrom(record, ["morebotsserver", "MOREBOTSAPI", "SAIN"]));
    }

    [Fact]
    public void MissingFrom_FallsBackToTheFileListForARecordWithNoFolders()
    {
        var record = new InstalledModRecord
        {
            ModId = 1,
            Name = "WTT - CommonLib",
            Version = "2.0.24",
            InstalledAt = DateTimeOffset.UtcNow,
            Files =
            [
                "BepInEx/plugins/WTT-ClientCommonLib/WTT-ClientCommonLib.dll",
                "user/mods/WTT-ServerCommonLib/package.json",
            ],
        };

        Assert.Equal(["WTT-ClientCommonLib"], InstalledModFolders.MissingFrom(record, ["WTT-ServerCommonLib"]));
    }

    [Fact]
    public void MissingFrom_SaysNothingAboutAManuallyConfirmedRecord()
    {
        // Its folders are whatever was on disk when the version was confirmed, not a record of what
        // this app placed - so their absence is not evidence of a broken install.
        var record = new InstalledModRecord
        {
            ModId = 2512,
            Name = "WTT - Content Backport",
            Version = "1.1.5",
            InstalledAt = DateTimeOffset.UtcNow,
            IsAppManaged = false,
            Folders = ["WTT-ContentBackportClient", "WTT-ContentBackport"],
        };

        Assert.Empty(InstalledModFolders.MissingFrom(record, []));
    }

    private static InstalledModRecord OpenSesame() => new()
    {
        ModId = 1184,
        Name = "Open Sesame",
        Version = "2.5.0",
        InstalledAt = DateTimeOffset.UnixEpoch,
        Files = ["BepInEx/plugins/SPTOpenSesame.dll"],
    };

    [Fact]
    public void Placed_IsTrueForTheFolderTheRecordPlaced()
    {
        Assert.True(InstalledModFolders.Placed(OpenSesame(), ["SPTOpenSesame", "SPTOpenSesame.dll"]));
    }

    [Fact]
    public void Placed_IsFalseForACopyUnderAnotherName()
    {
        // A renamed copy of the same DLL matches the same listing by GUID, but this app did not put
        // it there: removing it must not delete the files the record placed.
        Assert.False(InstalledModFolders.Placed(OpenSesame(), ["Dummy01", "Dummy01.dll"]));
    }
}
