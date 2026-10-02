using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>What an archive on disk holds, read before it is installed.</summary>
/// <param name="Recognised">False when nothing in it says where its files go (see ArchiveLayout).</param>
/// <param name="Plugins">Each BepInEx plugin DLL: its [BepInPlugin] GUID and version - the one it
/// declares, as the scan reads an installed plugin (ModAssemblyMetadata).</param>
/// <param name="ServerMods">Each server mod folder with a package.json: its name and version.</param>
/// <param name="ServerFolders">The folder names under user/mods - what an SPT 4 server mod (a DLL, no
/// package.json) is known by for its local id.</param>
/// <param name="ServerGuids">Each SPT 4 server mod's ModGuid and version, from its DLL's metadata
/// (see ModAssemblyMetadata) - for matching to a listing only; never part of the local id, so an
/// archive installed before these were read keeps the id it was recorded under.</param>
public sealed record LocalArchiveContents(
    bool Recognised,
    IReadOnlyList<(string Guid, string? Version)> Plugins,
    IReadOnlyList<(string Name, string? Version)> ServerMods,
    IReadOnlyList<string>? ServerFolders = null,
    IReadOnlyList<(string Guid, string? Version)>? ServerGuids = null);

//
// Installing an archive the user already has - from GitHub, a Discord post, an older version kept
// on disk - through the same install as a download: its layout read the same way, the previous
// version put aside, nothing half-installed, other mods' files kept, a record to remove it by.
//
// The archive is matched to its sp-mod.com listing by what is inside it (a plugin's GUID, or an
// SPT 4 server mod's ModGuid), so it is then the same mod as one installed from the Workshop -
// updates, pages and all. One that matches
// nothing, or more than one mod (a pack), is recorded under a local identity of its own instead: a
// negative id, which no sp-mod.com listing has, derived from the file's name so installing the same
// file again replaces that record rather than adding another.
//
public static class LocalArchive
{
    private const string Scheme = "local-file:";

    /// <summary>What goes in a ModVersion's Link for an archive on disk.</summary>
    public static string LinkFor(string path) => Scheme + Path.GetFullPath(path);

    public static bool TryGetPath(string? link, out string path)
    {
        path = link is not null && link.StartsWith(Scheme, StringComparison.Ordinal) ? link[Scheme.Length..] : string.Empty;
        return path.Length > 0;
    }

    /// <summary>The id an archive that matches no listing is recorded under: negative, and the same
    /// for the same mod whatever its file is called - worked out from what it holds (its plugin
    /// GUIDs and server mod names); from the name only when it holds neither.</summary>
    public static int IdFor(LocalArchiveContents contents, string fallbackName)
    {
        var identity = contents.Plugins.Select(p => p.Guid)
            .Concat(contents.ServerMods.Select(m => "server:" + m.Name))
            .Concat((contents.ServerFolders ?? []).Select(f => "folder:" + f))
            .Select(i => i.ToLowerInvariant())
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        return IdFor(identity.Count > 0 ? string.Join("|", identity) : fallbackName);
    }

    /// <summary>The id for a name (see the overload above).</summary>
    public static int IdFor(string name)
    {
        // FNV-1a over the lowered name: stable across runs, unlike string.GetHashCode.
        var hash = 2166136261u;
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return -(int)(hash & 0x3FFFFFFF) - 1;
    }

    /// <summary>True for an id from <see cref="IdFor"/>.</summary>
    public static bool IsLocalId(int id) => id < 0;

    public static async Task<LocalArchiveContents> InspectAsync(string archivePath, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "TCFModManager", "inspect-" + Guid.NewGuid().ToString("N"));
        try
        {
            await ModInstallService.ExtractAsync(archivePath, dir, ct).ConfigureAwait(false);

            // Where each file would go, read the way an install reads it (ArchiveLayout).
            var root = ArchiveLayout.FindContentRoot(dir);
            var bare = ArchiveLayout.IsBareBepInEx(root);
            if (!bare && !Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Any(ArchiveLayout.IsKnownRoot))
                return new LocalArchiveContents(false, [], []);

            var plugins = new List<(string, string?)>();
            var server = new List<(string, string?)>();
            var serverGuids = new List<(string, string?)>();
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var placed = Path.GetRelativePath(root, source);
                if (bare)
                {
                    if (ArchiveLayout.MapBareBepInEx(placed) is not { } mapped) continue;
                    placed = mapped;
                }

                var relative = placed.Replace('\\', '/');

                // What the install would skip says nothing about what this archive is: SPT's own
                // plugins shipped inside a pack, say (1.19's protected paths).
                if (ProtectedInstallPaths.IsProtected(relative) && !ProtectedInstallPaths.IsNewFileOnly(relative)) continue;

                var mods = relative.IndexOf("user/mods/", StringComparison.OrdinalIgnoreCase);
                if (mods >= 0 && relative[(mods + 10)..].Split('/') is { Length: > 1 } below) folders.Add(below[0]);

                if (relative.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)
                    && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && ModAssemblyMetadata.ReadPlugin(source) is { Guid: { Length: > 0 } guid } plugin)
                {
                    plugins.Add((guid, plugin.Version));
                }
                else if (relative.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase)
                         && relative.Contains("user/mods/", StringComparison.OrdinalIgnoreCase)
                         && ReadPackage(source) is { } package)
                {
                    server.Add(package);
                }
                else if (mods >= 0
                         && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                         && ModAssemblyMetadata.ReadServer(source) is { Guid: { } serverGuid } metadata)
                {
                    serverGuids.Add((serverGuid, metadata.Version));
                }
            }

            return new LocalArchiveContents(true, plugins, server, [.. folders], serverGuids);
        }
        finally
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug("Install", $"couldn't clear {dir}: {ex.Message}");
            }
        }
    }

    /// <summary>The one listing the archive's plugins belong to - a mod's own helper plugins, with no
    /// listing of their own, come along with it - or null when there is no such single listing
    /// (none, or a pack of several).</summary>
    public static Mod? MatchIn(LocalArchiveContents contents, IEnumerable<Mod> catalog)
    {
        var matches = ListingsIn(contents, catalog);
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Every listing the archive's plugins belong to - more than one for a pack. Its SPT 4
    /// server mods' ModGuids only when no plugin belongs to one: a mod's listing is keyed on its
    /// plugin, and a server library it bundles may have a listing of its own.</summary>
    public static List<Mod> ListingsIn(LocalArchiveContents contents, IEnumerable<Mod> catalog)
    {
        var all = catalog as IReadOnlyCollection<Mod> ?? [.. catalog];

        var byPlugin = Listed(contents.Plugins.Select(p => p.Guid), all);
        return byPlugin.Count > 0 ? byPlugin : Listed((contents.ServerGuids ?? []).Select(g => g.Guid), all);
    }

    private static List<Mod> Listed(IEnumerable<string> guids, IEnumerable<Mod> catalog)
    {
        var set = guids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return set.Count == 0 ? [] : [.. catalog.Where(m => !string.IsNullOrWhiteSpace(m.Guid) && set.Contains(m.Guid!)).DistinctBy(m => m.Id)];
    }

    /// <summary>The version to record: the published version the matched plugin's DLL is, when there
    /// is one, else the DLL's own.</summary>
    public static string? VersionOf(LocalArchiveContents contents, Mod? match)
    {
        var serverGuids = contents.ServerGuids ?? [];
        var published = match?.Versions?.Select(v => v.Version).ToList() ?? [];

        string? dllVersion;
        if (match is null)
        {
            dllVersion = contents.Plugins.Select(p => p.Version).FirstOrDefault(v => v is not null)
                ?? contents.ServerMods.Select(p => p.Version).FirstOrDefault(v => v is not null)
                ?? serverGuids.Select(p => p.Version).FirstOrDefault(v => v is not null);
        }
        else
        {
            var plugin = contents.Plugins.FirstOrDefault(p => string.Equals(p.Guid, match.Guid, StringComparison.OrdinalIgnoreCase));
            dllVersion = plugin.Guid is not null
                ? plugin.Version
                : serverGuids.FirstOrDefault(p => string.Equals(p.Guid, match.Guid, StringComparison.OrdinalIgnoreCase)).Version;
        }

        var best = match?.Versions is not null ? ModVersionComparer.BestSameRelease(dllVersion, published) : null;
        return best ?? dllVersion;
    }

    private static (string Name, string? Version)? ReadPackage(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (!root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) return null;

            var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return (name.GetString()!, version);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
