using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (SSPTMM): the check after an install that every archive file landed as the archive has it.
public class ForkInstallVerificationTests
{
    private static FileFingerprint Print(string path, long size, string hash) => new(path, size, hash);

    private static Dictionary<string, FileFingerprint> Archive(params FileFingerprint[] prints) =>
        prints.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Everything_as_in_the_archive_is_nothing_to_say()
    {
        var a = Print("BepInEx/plugins/M/m.dll", 10, "AA");
        Assert.Empty(InstallVerification.Check(Archive(a), [a with { Sha256 = "aa" }], new HashSet<string>(), _ => null));
    }

    [Fact]
    public void Missing_wrong_size_wrong_contents_and_unreadable_are_each_named()
    {
        var archive = Archive(
            Print("a/missing.json", 5, "11"),
            Print("a/size.json", 285, "22"),
            Print("a/contents.json", 7, "33"),
            Print("a/locked.dll", 9, "44"));

        var onDisk = new[]
        {
            Print("a/size.json", 132, "99"),
            Print("a/contents.json", 7, "98"),
        };

        var found = InstallVerification.Check(archive, onDisk, new HashSet<string>(), path => path == "a/locked.dll" ? 9 : null);

        Assert.Equal(
            [
                ("a/contents.json", InstallMismatchKind.DifferentContents),
                ("a/locked.dll", InstallMismatchKind.Unreadable),
                ("a/missing.json", InstallMismatchKind.Missing),
                ("a/size.json", InstallMismatchKind.DifferentSize),
            ],
            found.Select(m => (m.Path, m.Kind)));
        Assert.Equal(132, found.Single(m => m.Kind == InstallMismatchKind.DifferentSize).DiskSize);
    }

    [Fact]
    public void Files_left_as_they_are_on_purpose_are_not_asked_about()
    {
        var archive = Archive(Print("BepInEx/config/x.cfg", 5, "11"));
        var excluded = new HashSet<string>(["bepinex/config/X.cfg"], StringComparer.OrdinalIgnoreCase);

        Assert.Empty(InstallVerification.Check(archive, [Print("BepInEx/config/x.cfg", 6, "22")], excluded, _ => 6));
    }
}

// Fork (SSPTMM): which refused archive files the download card names as a warning.
public class ForkSkippedUserFilesTests
{
    [Theory]
    [InlineData("SPT_Runtime/user/profiles/x.json", true)]
    [InlineData("SPT/user/sptappdata/x", true)]
    [InlineData("user/cache/x", true)]
    [InlineData("SPT_Runtime/user", false)]
    [InlineData("BepInEx/core/BepInEx.dll", false)]
    [InlineData("doorstop_config.ini", false)]
    [InlineData("SPT_Runtime/SPT_Data/x.json", false)]
    [InlineData("BepInEx/plugins/Mod/user/x.json", false)]
    public void Only_files_under_SPTs_user_folder_are_warned_about(string path, bool expected) =>
        Assert.Equal(expected, ProtectedInstallPaths.IsUnderServerUser(path));
}
