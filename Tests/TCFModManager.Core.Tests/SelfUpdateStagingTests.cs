using System.IO.Compression;
using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// 1.3.0: SSPTMM stages its own update from a GitHub release zip - the 1.2.1+ layout (one SSPTMM\
// folder) and the flat one before it. Applying it needs Windows (PowerShell, robocopy) and is not
// run here; staging is everything up to that.
//
[Collection("AppUpdateStaging")]
public class SelfUpdateStagingTests : IDisposable
{
    public void Dispose()
    {
        AppUpdateInstaller.ClearWorkingFiles();
        try
        {
            if (Directory.Exists(AppUpdateInstaller.UpdateDirectory)) Directory.Delete(AppUpdateInstaller.UpdateDirectory, recursive: true);
        }
        catch (IOException) { }
    }

    private static byte[] ReleaseZip(string exePath)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var exe = zip.CreateEntry(exePath).Open())
            {
                exe.Write(new byte[2 * 1024 * 1024]); // past the "far too small to be a build" check
            }

            using var license = new StreamWriter(zip.CreateEntry(Path.GetDirectoryName(exePath)!.Replace('\\', '/') + "/LICENSE.txt").Open());
            license.Write("MIT");
        }

        return buffer.ToArray();
    }

    private sealed class Serve(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private static AppUpdateInfo Update() => new()
    {
        CurrentVersion = "1.2.1",
        LatestVersion = "1.3.0",
        ChangeKind = VersionChangeKind.Minor,
        DownloadUrl = "https://github.com/trappuss/SSPTMM/releases/download/v1.3.0/SSPTMM-1.3.0-win-x64.zip",
        ModPageUrl = "https://github.com/trappuss/SSPTMM/releases/tag/v1.3.0",
    };

    [Theory]
    [InlineData("SSPTMM/SSPTMM.exe")] // 1.2.1 and later
    [InlineData("SSPTMM.exe")]        // 1.2.0 and earlier
    public async Task TheReleaseZip_IsStaged(string exePath)
    {
        var body = ReleaseZip(exePath.Contains('/') ? exePath : "x/" + exePath);
        if (!exePath.Contains('/'))
        {
            // Flat: the exe at the zip's root.
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            using (var exe = zip.CreateEntry("SSPTMM.exe").Open()) exe.Write(new byte[2 * 1024 * 1024]);
            body = buffer.ToArray();
        }

        var installer = new AppUpdateInstaller(new ModDownloadService(new HttpClient(new Serve(body))));

        await installer.PrepareAsync(Update());

        Assert.True(AppUpdateInstaller.HasStagedUpdate);
    }

    [Fact]
    public async Task AZipWithoutSsptmmExe_IsRefused_AndNothingIsStaged()
    {
        var installer = new AppUpdateInstaller(new ModDownloadService(new HttpClient(new Serve(ReleaseZip("TCFModManager/TCFModManager.exe")))));

        var ex = await Assert.ThrowsAsync<AppUpdateException>(() => installer.PrepareAsync(Update()));

        Assert.Equal(AppUpdateFailure.ReleaseMissingExe, ex.Reason);
        Assert.False(AppUpdateInstaller.HasStagedUpdate);
    }
}
