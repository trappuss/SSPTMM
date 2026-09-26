using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>What an archive on disk holds, read before it is installed.</summary>
/// <param name="Recognised">False when nothing in it says where its files go (see ArchiveLayout).</param>
/// <param name="Plugins">Each BepInEx plugin DLL: its [BepInPlugin] GUID and file version.</param>
/// <param name="ServerMods">Each server mod folder with a package.json: its name and version.</param>
public sealed record LocalArchiveContents(
    bool Recognised,
    IReadOnlyList<(string Guid, string? Version)> Plugins,
    IReadOnlyList<(string Name, string? Version)> ServerMods);

//
// Installing an archive the user already has - from GitHub, a Discord post, an older version kept
// on disk - through the same install as a download: its layout read the same way, the previous
// version put aside, nothing half-installed, other mods' files kept, a record to remove it by.
//
// The archive is matched to its sp-mod.com listing by what is inside it (a plugin's GUID), so it is
// then the same mod as one installed from the Workshop - updates, pages and all. One that matches
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

            var layout = ArchiveLayout.Read(dir);
            if (layout is null) return new LocalArchiveContents(false, [], []);

            var plugins = new List<(string, string?)>();
            var server = new List<(string, string?)>();

            foreach (var entry in layout)
            {
                var relative = entry.Relative.Replace('\\', '/');
                if (relative.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)
                    && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && InstalledModScanner.ReadPlugin(entry.Source) is { Guid: { Length: > 0 } guid } plugin)
                {
                    plugins.Add((guid, plugin.Version));
                }
                else if (relative.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase)
                         && relative.Contains("user/mods/", StringComparison.OrdinalIgnoreCase)
                         && ReadPackage(entry.Source) is { } package)
                {
                    server.Add(package);
                }
            }

            return new LocalArchiveContents(true, plugins, server);
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
        var guids = contents.Plugins.Select(p => p.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (guids.Count == 0) return null;

        var matches = catalog
            .Where(m => !string.IsNullOrWhiteSpace(m.Guid) && guids.Contains(m.Guid!))
            .DistinctBy(m => m.Id)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Every listing the archive's plugins belong to - more than one for a pack.</summary>
    public static List<Mod> ListingsIn(LocalArchiveContents contents, IEnumerable<Mod> catalog)
    {
        var guids = contents.Plugins.Select(p => p.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. catalog.Where(m => !string.IsNullOrWhiteSpace(m.Guid) && guids.Contains(m.Guid!)).DistinctBy(m => m.Id)];
    }

    /// <summary>The version to record: the published version the matched plugin's DLL version is,
    /// when there is one, else the DLL's own.</summary>
    public static string? VersionOf(LocalArchiveContents contents, Mod? match)
    {
        var dllVersion = match is null
            ? contents.Plugins.Select(p => p.Version).FirstOrDefault(v => v is not null)
                ?? contents.ServerMods.Select(p => p.Version).FirstOrDefault(v => v is not null)
            : contents.Plugins.FirstOrDefault(p => string.Equals(p.Guid, match.Guid, StringComparison.OrdinalIgnoreCase)).Version;

        var published = match?.Versions?.FirstOrDefault(v => ModVersionComparer.IsSameRelease(dllVersion, v.Version));
        return published?.Version ?? dllVersion;
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
