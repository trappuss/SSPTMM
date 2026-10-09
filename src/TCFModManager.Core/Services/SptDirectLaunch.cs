using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

public enum DirectLaunchSupport
{
    // An SPT version whose launcher steps were read and are what this does.
    Supported,

    // A later 4.1 release than the ones read. Allowed, and said so: within 4.1 the steps were the
    // same from 4.1.3 to 4.1.6, but a patch release has added one before (bundles, in 4.1.3).
    NewerUntested,

    // Anything else - 4.0 runs an ownership check this will not stand in for, 4.1.0-4.1.2 have no
    // bundle step, and a later line has not been read at all. The SPT launcher is used.
    Unsupported,
}

public enum DirectLaunchStage
{
    CheckingVersion,
    CleaningUp,
    Wiping,
    ClearingCache,
    Patching,
    Bundles,
    Starting,
}

public sealed record DirectLaunchProgress(DirectLaunchStage Stage, int Done = 0, int Total = 0, long Bytes = 0, long TotalBytes = 0);

public enum DirectLaunchProblem
{
    None,

    // No EscapeFromTarkov.exe in the install folder.
    NoGameExe,

    // The server did not answer one of the launcher routes. Carries Error.
    ServerUnreachable,

    // BepInEx\plugins\spt\spt-core.dll is not there.
    CoreDllMissing,

    // The server and spt-core.dll are different SPT versions. Carries ServerVersion, DllVersion.
    VersionMismatch,

    // The server has no profile by that name (it was removed meanwhile).
    NoSuchProfile,

    // The server refused the wipe (its config forbids wiping).
    WipeRefused,

    // Fork (1.3.1): the server marked the profile invalid (it holds an item, trader or clothing no
    // installed mod adds), so the game would stop on it - and a wipe is never saved for such a
    // profile, so ticking "wipe" can't help either.
    ProfileInvalid,

    // SPT's patch folder is missing, or a patch would not apply. Carries Detail, Error.
    PatchFailed,

    // Some bundles would not download. Carries Detail (how many, and one name).
    BundlesFailed,

    // Starting EscapeFromTarkov.exe threw. Carries Error.
    StartFailed,
}

public sealed record DirectLaunchResult
{
    public DirectLaunchProblem Problem { get; init; }

    public string? Detail { get; init; }

    public string? ServerVersion { get; init; }

    public string? DllVersion { get; init; }

    public Exception? Error { get; init; }

    public bool Started => Problem == DirectLaunchProblem.None;
}

public sealed record DirectLaunchOptions
{
    // Mark the profile for a fresh start first (the launcher's "Wipe profile and start").
    public bool Wipe { get; init; }

    // Delete the server's user\sptappdata (the launcher's "Clear cache").
    public bool ClearCache { get; init; }

    // Names left out of the clean-up - the SPT launcher's own ExcludeFromCleanup setting.
    public IReadOnlyCollection<string> ExcludeFromCleanup { get; init; } = [];

    // Where the last game session's Logs\log_* folder is copied before the clean-up deletes it, so
    // Diagnose logs can still read it. Null to keep nothing.
    public string? KeepGameLogsIn { get; init; }

    public int KeepGameLogsCount { get; init; } = 3;
}

//
// Fork (SSPTMM, experimental): starting the game the way SPT's launcher does, without opening it.
//
// Each step is the 4.1 launcher's (SP-Tushonka/launcher 4.1.6, SPTushonka.Core GameHelper,
// FilePatcher, BundleHelper and Pages/Profile.razor StartGame), in its order:
//
//   1. spt-core.dll's version against the server's (/launcher/v2/version).
//   2. The clean-up: BattlEye, Logs, ConsistencyInfo, EscapeFromTarkov_BE.exe, Uninstall.exe,
//      UnityCrashHandler64.exe and WinPixEventRuntime.dll in the game folder unless the launcher's
//      ExcludeFromCleanup names them, and EscapeFromTarkov_Data\Plugins\x86_64\hwecho.dll always.
//   3. The wipe, when asked (/launcher/v2/wipe), and clearing user\sptappdata, when asked.
//   4. The patches: every *.spt-bak under the game folder put back over the file beside it, then
//      each SPT_Data\Launcher\Patches\<patch>\**\*.delta applied (HDiffPatch) to that backup,
//      making it first if there is none.
//   5. The bundles (/singleplayer/bundles): each one not already in the cache or the mod's own
//      bundles folder (same size, and same write time or CRC32) is downloaded into
//      user\cache\bundles\<CRC>\.
//   6. EscapeFromTarkov.exe -force-gfx-jobs native -token=<profileId>
//      -config={'BackendUrl':'https://<ip:port>','Version':'live','MatchingVersion':'live'}
//
// Where this differs from the launcher, on purpose:
//   - A version mismatch STOPS here. The 4.1.6 launcher logs it and starts anyway (its check
//     returns "no mismatch" on that path), and a game whose core plugin is another SPT version
//     than its server does not work.
//   - The last game session's Logs folder is copied aside before it is deleted (KeepGameLogsIn).
//   - A patch is written to a new file and moved over the target, so a failure leaves the file as
//     it was rather than half written.
//   - The server's address comes from its http.json, where the launcher assumes 127.0.0.1:6969.
//
public sealed class SptDirectLaunch(string gameRoot, string serverRoot, SptLauncherApi api)
{
    public const string TestedUpTo = "4.1.6";

    private const string GameExe = "EscapeFromTarkov.exe";

    private static readonly string[] CleanupNames =
    [
        "BattlEye",
        "Logs",
        "ConsistencyInfo",
        "EscapeFromTarkov_BE.exe",
        "Uninstall.exe",
        "UnityCrashHandler64.exe",
        "WinPixEventRuntime.dll",
    ];

    // Never excluded, as in the launcher.
    private static readonly string HwechoDll = Path.Combine("EscapeFromTarkov_Data", "Plugins", "x86_64", "hwecho.dll");

    private static readonly string CoreDll = Path.Combine("BepInEx", "plugins", "spt", "spt-core.dll");

    private const int BundleDownloads = 8;
    private const int BundleAttempts = 3;

    /// <summary>Applies one HDiffPatch delta: (delta, source, target). Replaceable for tests.</summary>
    public Func<string, string, string, CancellationToken, Task> ApplyPatch { get; init; } = HDiffApply;

    /// <summary>Starts the game process. Replaceable for tests.</summary>
    public Action<ProcessStartInfo> StartProcess { get; init; } = info => Process.Start(info)?.Dispose();

    public string GameRoot { get; } = gameRoot;

    public string ServerRoot { get; } = serverRoot;

    public static DirectLaunchSupport Support(string? sptVersion)
    {
        if (!SemanticVersion.TryParse(sptVersion, out var v)) return DirectLaunchSupport.Unsupported;
        var version = v.Value;

        if (version.Major != 4 || version.Minor != 1 || version.Patch < 3) return DirectLaunchSupport.Unsupported;

        SemanticVersion.TryParse(TestedUpTo, out var tested);
        return version.CompareTo(tested!.Value) <= 0 ? DirectLaunchSupport.Supported : DirectLaunchSupport.NewerUntested;
    }

    //
    // ip:port from the server's http.json: its "ip" when that is a real address (a Fika host often
    // binds a LAN address), 127.0.0.1 for 0.0.0.0, :: or none; its "port", else SPT's 6969.
    //
    public static string ServerAddress(string? serverExePath)
    {
        string? ip = null;
        int? port = null;

        if (Path.GetDirectoryName(serverExePath) is { Length: > 0 } folder)
        {
            foreach (var relative in new[] { Path.Combine("SPT_Data", "configs", "http.json"), Path.Combine("SPT_Data", "Server", "configs", "http.json") })
            {
                var path = Path.Combine(folder, relative);
                if (!File.Exists(path)) continue;

                try
                {
                    var text = File.ReadAllText(path);
                    port = SptServerReadiness.ReadPort(text);
                    ip = ReadIp(text);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Debug("DirectLaunch", $"couldn't read {path}: {ex.Message}");
                }
            }
        }

        if (ip is null or "" or "0.0.0.0" or "::" or "*" or "localhost") ip = "127.0.0.1";
        if (ip.Contains(':') && !ip.StartsWith('[')) ip = $"[{ip}]";

        return $"{ip}:{port ?? SptServerReadiness.DefaultPort}";
    }

    internal static string? ReadIp(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return doc.RootElement.TryGetProperty("ip", out var ip) && ip.ValueKind == JsonValueKind.String ? ip.GetString()?.Trim() : null;
        }
        catch (JsonException)
        {
            var match = Regex.Match(json, "\"ip\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
    }

    /// <summary>The game's arguments, exactly as the launcher writes them.</summary>
    public static string GameArguments(string profileId, string address) =>
        $"-force-gfx-jobs native -token={profileId} -config={{'BackendUrl':'https://{address}','Version':'live','MatchingVersion':'live'}}";

    public async Task<DirectLaunchResult> RunAsync(
        SptMiniProfile profile, DirectLaunchOptions options, IProgress<DirectLaunchProgress>? progress, CancellationToken ct)
    {
        var exe = Path.Combine(GameRoot, GameExe);
        if (!File.Exists(exe)) return new DirectLaunchResult { Problem = DirectLaunchProblem.NoGameExe };

        try
        {
            progress?.Report(new DirectLaunchProgress(DirectLaunchStage.CheckingVersion));
            if (await CheckVersionAsync(ct).ConfigureAwait(false) is { } versionProblem) return versionProblem;

            if (!await api.LoginAsync(profile.Username, ct).ConfigureAwait(false))
                return new DirectLaunchResult { Problem = DirectLaunchProblem.NoSuchProfile };

            // Fork (1.3.1): asked fresh, as the server decides this when it loads the profiles - the
            // list Play shows may be from before a mod was removed and the server restarted.
            var current = (await api.ProfilesAsync(ct).ConfigureAwait(false))
                .FirstOrDefault(p => string.Equals(p.Username, profile.Username, StringComparison.Ordinal));
            if ((current ?? profile).Invalid)
            {
                AppLog.Warn("DirectLaunch", $"{profile.Username} is marked invalid by the server - not started");
                return new DirectLaunchResult { Problem = DirectLaunchProblem.ProfileInvalid };
            }

            progress?.Report(new DirectLaunchProgress(DirectLaunchStage.CleaningUp));
            await Task.Run(() => CleanUp(options), ct).ConfigureAwait(false);

            if (options.Wipe)
            {
                progress?.Report(new DirectLaunchProgress(DirectLaunchStage.Wiping));
                if (!await api.WipeAsync(profile.Username, profile.Edition, ct).ConfigureAwait(false))
                    return new DirectLaunchResult { Problem = DirectLaunchProblem.WipeRefused };
                AppLog.Info("DirectLaunch", $"{profile.Username} marked for a fresh start");
            }

            if (options.ClearCache)
            {
                progress?.Report(new DirectLaunchProgress(DirectLaunchStage.ClearingCache));
                ClearCache(ServerRoot);
            }

            progress?.Report(new DirectLaunchProgress(DirectLaunchStage.Patching));
            if (await Task.Run(() => PatchAsync(ct), ct).ConfigureAwait(false) is { } patchProblem) return patchProblem;

            if (await AcquireBundlesAsync(progress, ct).ConfigureAwait(false) is { } bundleProblem) return bundleProblem;

            progress?.Report(new DirectLaunchProgress(DirectLaunchStage.Starting));
            return Start(exe, profile);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            AppLog.Warn("DirectLaunch", $"server didn't answer: {ex.Message}");
            return new DirectLaunchResult { Problem = DirectLaunchProblem.ServerUnreachable, Error = ex };
        }
        catch (JsonException ex)
        {
            AppLog.Warn("DirectLaunch", $"server answered something unreadable: {ex.Message}");
            return new DirectLaunchResult { Problem = DirectLaunchProblem.ServerUnreachable, Error = ex };
        }
    }

    // ---- 1. versions -------------------------------------------------------------------------

    private async Task<DirectLaunchResult?> CheckVersionAsync(CancellationToken ct)
    {
        var dllPath = Path.Combine(GameRoot, CoreDll);
        if (!File.Exists(dllPath)) return new DirectLaunchResult { Problem = DirectLaunchProblem.CoreDllMissing };

        var tag = await api.VersionAsync(ct).ConfigureAwait(false) ?? "";
        var serverText = tag.Split('-')[0].Trim();

        var info = FileVersionInfo.GetVersionInfo(dllPath);
        var dllText = $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
        AppLog.Info("DirectLaunch", $"server {tag}, spt-core.dll {dllText}");

        // A locally built spt-core.dll is versioned 1.x - the launcher lets that through, and so does this.
        if (info.FileMajorPart == 1) return null;

        if (!SemanticVersion.TryParse(serverText, out var server) || !SemanticVersion.TryParse(dllText, out var dll)
            || server.Value.Major != dll.Value.Major || server.Value.Minor != dll.Value.Minor || server.Value.Patch != dll.Value.Patch)
        {
            return new DirectLaunchResult { Problem = DirectLaunchProblem.VersionMismatch, ServerVersion = serverText, DllVersion = dllText };
        }

        return null;
    }

    // ---- 2. clean-up ---------------------------------------------------------------------------

    private void CleanUp(DirectLaunchOptions options)
    {
        var excluded = new HashSet<string>(options.ExcludeFromCleanup, StringComparer.OrdinalIgnoreCase);

        if (options.KeepGameLogsIn is { } keep && !excluded.Contains("Logs")) KeepLastGameLogs(keep, options.KeepGameLogsCount);

        var paths = CleanupNames.Where(n => !excluded.Contains(n)).Select(n => Path.Combine(GameRoot, n)).Append(Path.Combine(GameRoot, HwechoDll));

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    AppLog.Info("DirectLaunch", $"removing {path}");
                    RemoveTree(new DirectoryInfo(path));
                }
                else if (File.Exists(path))
                {
                    AppLog.Info("DirectLaunch", $"removing {path}");
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // As in the launcher: a file that will not go is logged, and the start goes on.
                AppLog.Warn("DirectLaunch", $"couldn't remove {path}: {ex.Message}");
            }
        }
    }

    private static void RemoveTree(DirectoryInfo dir)
    {
        foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.IsReadOnly = false;
        }

        dir.Delete(recursive: true);
    }

    // The newest Logs\log_* folder (the last game session), copied into keepIn once; the oldest
    // copies beyond keepCount go.
    private void KeepLastGameLogs(string keepIn, int keepCount)
    {
        var logs = Path.Combine(GameRoot, "Logs");
        try
        {
            if (!Directory.Exists(logs)) return;

            var newest = new DirectoryInfo(logs).EnumerateDirectories("log_*").OrderByDescending(d => d.LastWriteTimeUtc).FirstOrDefault();
            if (newest is null) return;

            var target = Path.Combine(keepIn, newest.Name);
            if (!Directory.Exists(target))
            {
                CopyTree(newest.FullName, target);
                Directory.SetLastWriteTimeUtc(target, newest.LastWriteTimeUtc);
                AppLog.Info("DirectLaunch", $"kept the last game session's logs in {target}");
            }

            foreach (var old in new DirectoryInfo(keepIn).EnumerateDirectories("log_*").OrderByDescending(d => d.LastWriteTimeUtc).Skip(Math.Max(1, keepCount)))
            {
                old.Delete(recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("DirectLaunch", $"couldn't keep the game's logs: {ex.Message}");
        }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    // The launcher's Clear cache: the server's user\sptappdata.
    // Whether a path lies in a folder - false for one that can't be resolved (ModInstallService's
    // IsInside says true there, for its own callers; a file written or replaced here needs a yes).
    internal static bool IsSafelyInside(string path, string folder)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static void ClearCache(string serverRoot)
    {
        var folder = Path.Combine(serverRoot, "user", "sptappdata");
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            AppLog.Info("DirectLaunch", $"cleared {folder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("DirectLaunch", $"couldn't clear {folder}: {ex.Message}");
        }
    }

    // ---- 4. patches ----------------------------------------------------------------------------

    private async Task<DirectLaunchResult?> PatchAsync(CancellationToken ct)
    {
        var patches = Path.Combine(ServerRoot, "SPT_Data", "Launcher", "Patches");
        if (!Directory.Exists(patches))
            return new DirectLaunchResult { Problem = DirectLaunchProblem.PatchFailed, Detail = patches };

        // Everything back to the game's own files first, as the launcher does on every start.
        foreach (var backup in Directory.EnumerateFiles(GameRoot, "*.spt-bak", new EnumerationOptions
                 {
                     RecurseSubdirectories = true,
                     IgnoreInaccessible = true,
                     AttributesToSkip = FileAttributes.ReparsePoint,
                 }))
        {
            var target = backup[..^".spt-bak".Length];
            try
            {
                if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                File.Copy(backup, target, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("DirectLaunch", $"couldn't put {target} back: {ex.Message}");
            }
        }

        foreach (var patch in Directory.EnumerateDirectories(patches).Order(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var delta in Directory.EnumerateFiles(patch, "*.delta", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(patch, delta)[..^".delta".Length];
                var target = Path.GetFullPath(Path.Combine(GameRoot, relative));
                if (!IsSafelyInside(target, GameRoot))
                    return new DirectLaunchResult { Problem = DirectLaunchProblem.PatchFailed, Detail = relative };

                var backup = target + ".spt-bak";
                var written = target + ".spt-new";
                try
                {
                    if (!File.Exists(backup)) File.Copy(target, backup);
                    if (File.Exists(written)) File.Delete(written);

                    await ApplyPatch(delta, backup, written, ct).ConfigureAwait(false);

                    File.Move(written, target, overwrite: true);
                    AppLog.Info("DirectLaunch", $"patched {relative}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    try { if (File.Exists(written)) File.Delete(written); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    AppLog.Error("DirectLaunch", $"couldn't patch {relative}", ex);
                    return new DirectLaunchResult { Problem = DirectLaunchProblem.PatchFailed, Detail = relative, Error = ex };
                }
            }
        }

        return null;
    }

    private static Task HDiffApply(string delta, string source, string target, CancellationToken ct)
    {
        SharpHDiffPatch.Core.HDiffPatch.LogVerbosity = SharpHDiffPatch.Core.Verbosity.Quiet;
        var patcher = new SharpHDiffPatch.Core.HDiffPatch();
        patcher.Initialize(delta);
        patcher.Patch(source, target, false, ct);
        return Task.CompletedTask;
    }

    // ---- 5. bundles ----------------------------------------------------------------------------

    private async Task<DirectLaunchResult?> AcquireBundlesAsync(IProgress<DirectLaunchProgress>? progress, CancellationToken ct)
    {
        var manifest = await api.BundlesAsync(ct).ConfigureAwait(false);
        if (manifest.Count == 0) return null;

        var cache = Path.Combine(ServerRoot, "user", "cache", "bundles");
        var missing = await Task.Run(() => manifest.Where(b => !IsAvailable(b, CachePath(cache, b), ModPath(b))).ToList(), ct).ConfigureAwait(false);
        AppLog.Info("DirectLaunch", $"{manifest.Count - missing.Count} bundles already present, {missing.Count} to download");
        if (missing.Count == 0) return null;

        var done = manifest.Count - missing.Count;
        long bytes = 0;
        var totalBytes = missing.Sum(b => b.Size);
        var failed = new System.Collections.Concurrent.ConcurrentBag<string>();
        progress?.Report(new DirectLaunchProgress(DirectLaunchStage.Bundles, done, manifest.Count, 0, totalBytes));

        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = BundleDownloads, CancellationToken = ct }, async (bundle, token) =>
        {
            var destination = CachePath(cache, bundle);
            if (!IsSafelyInside(destination, cache))
            {
                failed.Add(bundle.FileName);
                return;
            }

            for (var attempt = 1; attempt <= BundleAttempts; attempt++)
            {
                long mine = 0;
                try
                {
                    await api.DownloadBundleAsync(bundle.FileName, destination, read =>
                    {
                        mine += read;
                        var now = Interlocked.Add(ref bytes, read);
                        progress?.Report(new DirectLaunchProgress(DirectLaunchStage.Bundles, Volatile.Read(ref done), manifest.Count, now, totalBytes));
                    }, token).ConfigureAwait(false);

                    Interlocked.Increment(ref done);
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Add(ref bytes, -mine);
                    if (attempt == BundleAttempts)
                    {
                        AppLog.Warn("DirectLaunch", $"giving up on bundle {bundle.FileName}: {ex.Message}");
                        failed.Add(bundle.FileName);
                    }
                }
            }
        }).ConfigureAwait(false);

        return failed.IsEmpty
            ? null
            : new DirectLaunchResult { Problem = DirectLaunchProblem.BundlesFailed, Detail = $"{failed.Count}|{failed.First()}" };
    }

    private static string CachePath(string cache, SptBundleEntry bundle) =>
        Path.GetFullPath(Path.Combine(cache, bundle.Crc.ToString("X8"), bundle.FileName));

    private string ModPath(SptBundleEntry bundle) =>
        Path.GetFullPath(Path.Combine(ServerRoot, bundle.ModPath, "bundles", bundle.FileName));

    // The launcher's test: in the cache at the right size; or in the mod's own folder at the right
    // size, with the write time the server hashed it at or, failing that, the same CRC32.
    internal static bool IsAvailable(SptBundleEntry bundle, string cachePath, string modPath)
    {
        try
        {
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length == bundle.Size) return true;
            if (!File.Exists(modPath)) return false;

            var info = new FileInfo(modPath);
            if (info.Length != bundle.Size) return false;
            if (info.LastWriteTimeUtc.Ticks == bundle.ModifiedUtcTicks) return true;

            using var stream = File.OpenRead(modPath);
            return Crc32.Of(stream) == bundle.Crc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---- 6. the game ---------------------------------------------------------------------------

    private DirectLaunchResult Start(string exe, SptMiniProfile profile)
    {
        try
        {
            StartProcess(new ProcessStartInfo(exe)
            {
                Arguments = GameArguments(profile.ProfileId, api.Address),
                UseShellExecute = false,
                WorkingDirectory = GameRoot,
            });

            AppLog.Info("DirectLaunch", $"game started for {profile.Username} against {api.Address}");
            return new DirectLaunchResult();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            AppLog.Error("DirectLaunch", "couldn't start the game", ex);
            return new DirectLaunchResult { Problem = DirectLaunchProblem.StartFailed, Error = ex };
        }
    }
}

/// <summary>CRC-32 (IEEE 802.3, as System.IO.Hashing.Crc32 and the SPT server compute it).</summary>
internal static class Crc32
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }

    public static uint Of(Stream stream)
    {
        var crc = 0xFFFFFFFFu;
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++) crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    public static uint Of(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
