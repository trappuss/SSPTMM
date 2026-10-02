using System.Diagnostics;
using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Scans an SPT install folder for installed client (BepInEx/plugins, BepInEx/patchers) and server
// (user/mods) mods, plus the ".disabled" sibling of each of those containers - a disabled mod is
// still installed and still listed, it just isn't loaded by SPT.
//
public static class InstalledModScanner
{
    // Folder/DLL names that are core SPT client files rather than an installed mod.
    private static readonly HashSet<string> CoreSptEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "spt",
    };

    //
    // The same idea for BepInEx\patchers specifically - names there that aren't mods, so listing
    // them only produces entries nothing can ever match, update or remove. Two kinds:
    //
    // - SPT's own preloader patcher, part of the install rather than something the user added.
    // - General BepInEx utilities that mods bundle alongside themselves. They're published on
    //   GitHub rather than sp-mod.com (FixPluginTypesSerialization is
    //   github.com/xiaoxiao921/FixPluginTypesSerialization), belong to whichever mod shipped them,
    //   and aren't the user's to manage from here.
    //
    // Named exactly rather than by any prefix or keyword rule, so a mod that legitimately names its
    // own patcher along similar lines still shows up. Add to this as more turn up.
    //
    private static readonly HashSet<string> NonModPatcherEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "spt-prepatch",
        "aki-prepatch",
        "FixPluginTypesSerialization",
    };

    private static bool IsCoreEntry(string name, bool isPatcher) =>
        CoreSptEntries.Contains(name) || (isPatcher && NonModPatcherEntries.Contains(name));

    //
    // Whether this scanner would skip a folder of that name wherever it found it.
    //
    // For the one caller that has a name but not the container it came from: an install record
    // naming FixPluginTypesSerialization is not missing a folder when the scan comes back without
    // it - the scan was never going to report it. See InstalledModFolders.MissingFrom.
    //
    public static bool IsNeverReported(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (CoreSptEntries.Contains(name) || NonModPatcherEntries.Contains(name));

    //
    // Fork: every [BepInPlugin] GUID that BepInEx would load from this install - every DLL under
    // BepInEx\plugins, however deep, SPT's own included. The scan above reads each mod folder's top
    // level only and leaves SPT's folder out, so a dependency on SPT's core plugins, or on an API
    // DLL a mod keeps in a subfolder, would otherwise look missing when it is not.
    //
    public static HashSet<string> LoadedPluginGuids(string? installPath)
    {
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(installPath)) return guids;

        var plugins = Path.Combine(installPath, "BepInEx", "plugins");
        if (!Directory.Exists(plugins)) return guids;

        try
        {
            // Past a folder that cannot be read, rather than stopping at it.
            var everywhere = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var dll in Directory.EnumerateFiles(plugins, "*.dll", everywhere))
            {
                if (ModAssemblyMetadata.ReadPlugin(dll).Guid is { Length: > 0 } guid) guids.Add(guid);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Scan", $"couldn't read every plugin under {plugins}: {ex.Message}");
        }

        return guids;
    }

    public static List<InstalledMod> Scan(string? installPath)
    {
        var results = new List<InstalledMod>();
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) return results;

        foreach (var container in DisabledModPaths.ClientContainers(installPath))
        {
            var isPatcher = DisabledModPaths.IsPatcherContainer(container);
            ScanClientFolder(container, results, disabled: false, isPatcher);
            ScanClientFolder(DisabledModPaths.Disabled(container), results, disabled: true, isPatcher);
        }

        // Checks all three known server-content layouts; whichever exists is scanned.
        foreach (var container in DisabledModPaths.ServerContainers(installPath))
        {
            ScanServerFolder(container, results, disabled: false);
            ScanServerFolder(DisabledModPaths.Disabled(container), results, disabled: true);
        }

        return results;
    }

    //
    // Client (BepInEx) mods are versioned by what their [BepInPlugin] declares, falling back to the
    // DLL's embedded file version when it declares nothing. Handles both a subfolder containing
    // DLLs and a single loose DLL directly in the container.
    // <paramref name="isPatcher"/> says which of the two client containers this is - patchers are
    // read exactly the same way, they just get flagged so the card layer can fold one back into
    // the mod it belongs to instead of showing it as a mod in its own right.
    //
    private static void ScanClientFolder(string root, List<InstalledMod> results, bool disabled, bool isPatcher)
    {
        if (!Directory.Exists(root)) return;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(name) || IsCoreEntry(name, isPatcher)) continue;

            // Prefer a DLL whose name matches the folder; otherwise take the first DLL found.
            var dlls = Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).ToList();
            var dll = dlls.FirstOrDefault(d => string.Equals(Path.GetFileNameWithoutExtension(d), name, StringComparison.OrdinalIgnoreCase))
                ?? dlls.FirstOrDefault();

            // Check every DLL in the folder for the [BepInPlugin] attribute; it isn't necessarily
            // on the same DLL used for versioning. Dependencies are collected across all of them.
            var metadata = dlls.Select(d => (Dll: d, Declared: ModAssemblyMetadata.ReadPlugin(d))).ToList();

            // Every GUID the folder registers, kept alongside the first. A mod shipping an API or
            // config-UI assembly next to its own plugin registers one per DLL, and each of those
            // has its own config file named after it.
            var guids = metadata
                .Select(m => m.Declared.Guid)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // The version comes from the DLL chosen for versioning when it is a plugin itself, then
            // from the plugin that stands for the mod's identity.
            var primary = metadata.FirstOrDefault(m => m.Dll == dll && m.Declared.Guid is not null).Declared
                ?? metadata.FirstOrDefault(m => m.Declared.Guid is not null && m.Declared.Guid == guids.FirstOrDefault()).Declared;
            var fileVersion = dll is null ? null : TryGetFileVersion(dll);

            results.Add(new InstalledMod
            {
                Name = name,
                Version = primary?.Version ?? fileVersion,
                FileVersion = fileVersion,
                DeclaredName = primary?.Name,
                Guid = guids.FirstOrDefault(),
                Guids = guids,
                Target = InstalledModTarget.Client,
                IsPatcher = isPatcher,
                FolderPath = dir,
                InstalledAt = TryGetCreationTime(dir),
                IsDisabled = disabled,
                Dependencies = MergeDependencies(metadata.SelectMany(m => m.Declared.Dependencies)),
                Assemblies = disabled ? [] : ListAssemblies(dir),
            });
        }

        foreach (var dll in Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            if (IsCoreEntry(name, isPatcher)) continue;

            var metadata = ModAssemblyMetadata.ReadPlugin(dll);
            var fileVersion = TryGetFileVersion(dll);

            results.Add(new InstalledMod
            {
                Name = name,
                Version = metadata.Version ?? fileVersion,
                FileVersion = fileVersion,
                DeclaredName = metadata.Name,
                Guid = metadata.Guid,
                Guids = metadata.Guid is null ? [] : [metadata.Guid],
                Target = InstalledModTarget.Client,
                IsPatcher = isPatcher,
                FolderPath = dll,
                InstalledAt = TryGetCreationTime(dll),
                IsDisabled = disabled,
                Dependencies = MergeDependencies(metadata.Dependencies),
                Assemblies = disabled ? [] : [Describe(dll, Path.GetFileName(dll))],
            });
        }
    }

    //
    // SPT 3.x server mods describe themselves in package.json ("name"/"version"/"author"/
    // "modDependencies"). SPT 4.x server mods ship no package.json - the same facts are declared in
    // a metadata record inside the mod's DLL (see ModAssemblyMetadata.ReadServer), which is read
    // when package.json is missing or has no version. The DLL's file version is the last resort.
    //
    // Name stays the package name or folder name either way: it is the join key for groups, pins,
    // list entries and SPT 3.x dependencies. The declared name is carried alongside for display.
    //
    private static void ScanServerFolder(string root, List<InstalledMod> results, bool disabled)
    {
        if (!Directory.Exists(root)) return;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var folderName = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(folderName)) continue;

            var name = folderName;
            string? version = null;
            string? author = null;
            var dependencies = new List<ModDependencyRef>();

            var packageJsonPath = Path.Combine(dir, "package.json");
            if (File.Exists(packageJsonPath))
            {
                try
                {
                    using var stream = File.OpenRead(packageJsonPath);
                    using var doc = JsonDocument.Parse(stream);
                    var root2 = doc.RootElement;

                    if (root2.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(n.GetString()))
                    {
                        name = n.GetString()!;
                    }

                    if (root2.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                        version = v.GetString();

                    if (root2.TryGetProperty("author", out var a))
                    {
                        // npm's "author" field is either a plain string or an object with a "name".
                        author = a.ValueKind switch
                        {
                            JsonValueKind.String => a.GetString(),
                            JsonValueKind.Object when a.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String => an.GetString(),
                            _ => null,
                        };
                    }

                    if (root2.TryGetProperty("modDependencies", out var deps))
                        dependencies.AddRange(ReadServerDependencies(deps));
                }
                catch (JsonException)
                {
                    // Malformed package.json - still list the mod by its folder name.
                }
            }

            DeclaredMetadata? declared = null;
            string? fileVersion = null;

            if (version is null)
            {
                // Prefer a DLL whose name matches the folder/mod name, then the rest. The one that
                // carries the metadata record is the mod's own; the others are bundled libraries.
                var dlls = Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(d => string.Equals(Path.GetFileNameWithoutExtension(d), name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                string? declaringDll = null;
                foreach (var candidate in dlls)
                {
                    declared = ModAssemblyMetadata.ReadServer(candidate);
                    if (declared is null) continue;

                    declaringDll = candidate;
                    break;
                }

                var dll = declaringDll ?? dlls.FirstOrDefault();
                if (dll is not null) fileVersion = TryGetFileVersion(dll);

                version = declared?.Version ?? fileVersion;
                author ??= declared?.Author;
                if (declared is not null) dependencies.AddRange(declared.Dependencies);
            }

            results.Add(new InstalledMod
            {
                Name = name,
                Version = version,
                FileVersion = fileVersion,
                DeclaredName = declared?.Name,
                SptVersion = declared?.SptVersion,
                Guid = declared?.Guid,
                Guids = declared?.Guid is { } guid ? [guid] : [],
                Author = author,
                Target = InstalledModTarget.Server,
                FolderPath = dir,
                InstalledAt = TryGetCreationTime(dir),
                IsDisabled = disabled,
                Dependencies = MergeDependencies(dependencies),
            });
        }
    }

    // A folder holding more DLLs than this is listed up to it - no real mod comes close, and the
    // conflict check must not be what makes a scan of a strange folder slow.
    private const int MaxAssembliesPerEntry = 500;

    //
    // Every DLL under a client mod folder, at any depth - BepInEx loads plugins recursively. Folders
    // that are links are not followed, the same as every other walk inside an install.
    //
    private static List<ModAssembly> ListAssemblies(string folder)
    {
        try
        {
            return
            [
                .. Directory.EnumerateFiles(folder, "*.dll", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                        IgnoreInaccessible = true,
                    })
                    .Take(MaxAssembliesPerEntry)
                    .Select(dll => Describe(dll, Path.GetRelativePath(folder, dll).Replace('\\', '/'))),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static ModAssembly Describe(string dll, string relativePath)
    {
        var (name, version) = ModAssemblyMetadata.ReadIdentity(dll);

        long size;
        try { size = new FileInfo(dll).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { size = -1; }

        return new ModAssembly(relativePath, name, version, size);
    }

    //
    // SPT's "modDependencies" is an object of package name -> version range. An array of plain
    // names is accepted too, since some mods write it that way. Server mods have no notion of a
    // soft dependency, so every entry is hard.
    //
    private static IEnumerable<ModDependencyRef> ReadServerDependencies(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (!string.IsNullOrWhiteSpace(property.Name))
                        yield return new ModDependencyRef(property.Name, IsSoft: false);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                        yield return new ModDependencyRef(item.GetString()!, IsSoft: false);
                }

                break;
        }
    }

    //
    // Distinct by identifier, keeping the hardest declaration when the same one appears twice, and
    // the version range of a hard declaration over a soft one's.
    //
    private static List<ModDependencyRef> MergeDependencies(IEnumerable<ModDependencyRef> dependencies) =>
        dependencies
            .GroupBy(d => d.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ModDependencyRef(
                g.Key,
                g.All(d => d.IsSoft),
                g.OrderBy(d => d.IsSoft).Select(d => d.VersionRange).FirstOrDefault(r => r is not null)))
            .ToList();

    private static DateTimeOffset? TryGetCreationTime(string path)
    {
        try
        {
            return new DateTimeOffset(Directory.Exists(path) ? Directory.GetCreationTimeUtc(path) : File.GetCreationTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryGetFileVersion(string dllPath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dllPath);
            var raw = (info.FileVersion ?? info.ProductVersion ?? "").Trim();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
