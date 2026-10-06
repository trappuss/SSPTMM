using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// SSPTMM unzipped into an SPT folder finds that install by itself on first start (1.3.0).
public class InstallAroundAppTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ssptmm-around-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string Dir(string relative)
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    [Fact]
    public void InsideAnSptFolder_FindsIt()
    {
        File.WriteAllText(Path.Combine(Dir("SPT"), "EscapeFromTarkov.exe"), "");
        File.WriteAllText(Path.Combine(Dir(Path.Combine("SPT", "SPT")), "SPT.Server.exe"), "");
        var app = Dir(Path.Combine("SPT", "SSPTMM"));

        Assert.Equal(Path.Combine(_root, "SPT"), SptRootResolver.InstallAroundApp(app));
        Assert.Equal(Path.Combine(_root, "SPT"), SptRootResolver.InstallAroundApp(app + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void ABepInExFolderAlone_IsNotEnough()
    {
        Dir(Path.Combine("SPT", "BepInEx"));
        var app = Dir(Path.Combine("SPT", "SSPTMM"));

        Assert.Null(SptRootResolver.InstallAroundApp(app));
    }

    // The live game has the same exe but no SPT server: never picked by itself.
    [Fact]
    public void AGameFolderWithoutSpt_IsNotPicked()
    {
        File.WriteAllText(Path.Combine(Dir("EFT"), "EscapeFromTarkov.exe"), "");
        var app = Dir(Path.Combine("EFT", "SSPTMM"));

        Assert.Null(SptRootResolver.InstallAroundApp(app));
    }

    [Fact]
    public void OnlyTheFolderDirectlyAbove_IsChecked()
    {
        File.WriteAllText(Path.Combine(Dir("SPT"), "EscapeFromTarkov.exe"), "");
        var app = Dir(Path.Combine("SPT", "Tools", "SSPTMM"));

        Assert.Null(SptRootResolver.InstallAroundApp(app));
    }

    [Fact]
    public void NoFolder_IsNull()
    {
        Assert.Null(SptRootResolver.InstallAroundApp(null));
        Assert.Null(SptRootResolver.InstallAroundApp(""));
    }
}
