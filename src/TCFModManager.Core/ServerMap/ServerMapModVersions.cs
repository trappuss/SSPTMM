using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.ServerMap;

//
// The SPT line a Server Map mod build is for. Each line has its own stub and, on sp-mod.com, its
// own addon listing.
//
public enum ServerMapSptLine
{
    Spt40,
    Spt41,
}

//
// The Server Map mod's listings on sp-mod.com: addons of this app's mod page, one per SPT line.
// One set of constants so the Options link, the update check and the file probe can never drift
// apart.
//
public static class ServerMapAddon
{
    public const string Spt40AddonId = "126";

    public const string Spt40PageUrl = "https://sp-mod.com/addon/126/tcf-server-mapper";

    public const string Spt41AddonId = "142";

    public const string Spt41PageUrl = "https://sp-mod.com/addon/142/tcf-server-mapper-41";

    public static string AddonId(ServerMapSptLine line) =>
        line == ServerMapSptLine.Spt40 ? Spt40AddonId : Spt41AddonId;

    public static string PageUrl(ServerMapSptLine line) =>
        line == ServerMapSptLine.Spt40 ? Spt40PageUrl : Spt41PageUrl;

    public const string PayloadFileName = "TCFMM.ServerMap.Payload.dll";

    public const string StubFolderName = "TCFMM.ServerMap";

    public const string StubFileName = "TCFMM.ServerMap.Stub.dll";
}

//
// The Server Map mod as installed on THIS machine: the payload under TCFModManager\ServerMap\ and,
// when it can be found, the stub in the server's user\mods. Versions are the ones each assembly
// was built with, so they agree with what the mod's own /hello reports.
//
public sealed record InstalledServerMapMod
{
    public required string PayloadPath { get; init; }

    public string? PayloadVersion { get; init; }

    public string? StubPath { get; init; }

    public string? StubVersion { get; init; }

    // Which line the stub is for, from the server folder it sits in. Null when no stub was found or
    // it sits in neither SPT_Runtime\ nor SPT\.
    public ServerMapSptLine? StubLine { get; init; }

    //
    // The stub is older than the payload beside it. 0.2.0 is where this starts to matter: an older
    // stub never passes on the caller's address, so a LAN-only server refuses everyone.
    //
    public bool StubBehindPayload => ServerMapModVersions.IsBehind(StubVersion, PayloadVersion);
}

// The newest Server Map mod published on sp-mod.com.
public sealed record ServerMapModRelease
{
    public required string LatestVersion { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public string? Changelog { get; init; }
}

public static class ServerMapModVersions
{
    private static readonly string[] PayloadSegments = ["TCFModManager", "ServerMap", "payload"];

    // Where a server keeps user\mods, probed the same way DisabledModPaths finds mod containers.
    private static readonly string[][] ModsContainers =
    [
        ["SPT_Runtime", "user", "mods"],
        ["SPT", "user", "mods"],
        ["user", "mods"],
    ];

    // The line whose addon to check and link. A stub on disk decides it; otherwise the configured
    // install's SPT version does, which on a player's machine is also the line of the server they
    // join. With neither, the current line.
    public static ServerMapSptLine LineFor(InstalledServerMapMod? installed, string? sptInstallPath)
    {
        if (installed?.StubLine is { } stubLine) return stubLine;

        var version = SptInstallationService.GetInstalledVersion(sptInstallPath).Version;
        if (version is not null)
        {
            var parts = version.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor))
                return major < 4 || (major == 4 && minor < 1) ? ServerMapSptLine.Spt40 : ServerMapSptLine.Spt41;
        }

        return ServerMapSptLine.Spt41;
    }

    // True when candidate is a newer release than installed. Unparsable either side is not behind:
    // the check never claims an update it cannot show.
    public static bool IsBehind(string? installed, string? candidate) =>
        SemanticVersion.Classify(installed, candidate) is VersionChangeKind.Patch
            or VersionChangeKind.Minor
            or VersionChangeKind.Major;

    //
    // Finds the payload by walking up from the app's own folder and the configured install, the way
    // the stub's loader finds it on the server - so the answer is the copy the server actually loads.
    // Null when this machine runs no Server Map mod.
    //
    public static InstalledServerMapMod? FindInstalled(string? sptInstallPath, string? appDirectory = null)
    {
        foreach (var start in new[] { appDirectory ?? AppContext.BaseDirectory, sptInstallPath })
        {
            if (FindPayload(start) is not { } payload) continue;

            // <SPT root>\TCFModManager\ServerMap\payload\x.dll -> <SPT root>
            var sptRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(payload))));

            var stub = FindStub(sptInstallPath) ?? FindStub(sptRoot);

            return new InstalledServerMapMod
            {
                PayloadPath = payload,
                PayloadVersion = VersionOf(payload),
                StubPath = stub,
                StubVersion = stub is null ? null : VersionOf(stub),
                StubLine = stub is null ? null : LineOfStub(stub),
            };
        }

        return null;
    }

    private static string? FindPayload(string? start)
    {
        if (string.IsNullOrWhiteSpace(start)) return null;

        try
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine([directory.FullName, .. PayloadSegments, ServerMapAddon.PayloadFileName]);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return null;
    }

    private static string? FindStub(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;

        foreach (var container in ModsContainers)
        {
            var candidate = Path.Combine([root, .. container, ServerMapAddon.StubFolderName, ServerMapAddon.StubFileName]);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    // <server>\user\mods\TCFMM.ServerMap\x.dll -> the name of <server>.
    private static ServerMapSptLine? LineOfStub(string stubPath)
    {
        var server = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(stubPath)))));

        if (string.Equals(server, "SPT_Runtime", StringComparison.OrdinalIgnoreCase)) return ServerMapSptLine.Spt41;
        if (string.Equals(server, "SPT", StringComparison.OrdinalIgnoreCase)) return ServerMapSptLine.Spt40;
        return null;
    }

    //
    // The version an assembly was built as, read from its metadata without loading it: the
    // informational version (what the payload's /hello reports) with any "+commit" dropped, or the
    // assembly version when there is none. Null when the file is not a readable .NET assembly.
    //
    public static string? VersionOf(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return null;

            var reader = pe.GetMetadataReader();

            foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (AttributeName(reader, attribute) != nameof(AssemblyInformationalVersionAttribute)) continue;

                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1) continue;

                var value = blob.ReadSerializedString();
                if (string.IsNullOrWhiteSpace(value)) continue;

                var plus = value.IndexOf('+');
                return (plus >= 0 ? value[..plus] : value).Trim();
            }

            var version = reader.GetAssemblyDefinition().Version;
            return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException
                                       or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? AttributeName(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind == HandleKind.MemberReference)
        {
            var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
            if (parent.Kind == HandleKind.TypeReference)
                return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name);
        }
        else if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
        {
            var type = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
            return reader.GetString(reader.GetTypeDefinition(type).Name);
        }

        return null;
    }
}

//
// Asks sp-mod.com for the newest Server Map mod for one SPT line, through the same public API the app's own update
// check uses. The mod is installed by hand, so this only ever says what is out - it never downloads.
//
public sealed class ServerMapModUpdateService(SpModApiClient spModApi)
{
    private const int VersionsToInspect = 25;

    // Null when the listing carries no parsable version. Failures surface as exceptions, as the
    // app's own check does, so a caller can tell "nothing published" from "could not ask".
    public async Task<ServerMapModRelease?> LatestAsync(ServerMapSptLine line, CancellationToken ct = default)
    {
        var addonId = ServerMapAddon.AddonId(line);

        var versions = await spModApi
            .GetAddonVersionsAsync(
                addonId,
                new AddonVersionsQuery { Sort = "-published_at", PerPage = VersionsToInspect },
                ct)
            .ConfigureAwait(false);

        // By version number, not publish date, so a re-published older release cannot pose as newest.
        var newest = versions.Data
            .Select(v => (Version: v, Parsed: SemanticVersion.TryParse(v.Version, out var p) ? p : null))
            .Where(x => x.Parsed is not null)
            .OrderByDescending(x => x.Parsed!.Value)
            .Select(x => x.Version)
            .FirstOrDefault();

        if (newest?.Version is null)
        {
            AppLog.Info("ServerMapMod", $"addon {addonId} returned no parsable version");
            return null;
        }

        AppLog.Info("ServerMapMod", $"newest Server Map mod published for {line}: {newest.Version}");

        return new ServerMapModRelease
        {
            LatestVersion = newest.Version,
            PublishedAt = newest.PublishedAt,
            Changelog = newest.Description,
        };
    }
}
