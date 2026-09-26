using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
    // Every [BepInPlugin] GUID that BepInEx would load from this install - every DLL under
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
                if (ReadPluginMetadata(dll).Guid is { Length: > 0 } guid) guids.Add(guid);
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
    // Client (BepInEx) mods are versioned via their DLL's embedded file version resource.
    // Handles both a subfolder containing DLLs and a single loose DLL directly in the container.
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
            var metadata = dlls.Select(ReadPluginMetadata).ToList();

            // Every GUID the folder registers, kept alongside the first. A mod shipping an API or
            // config-UI assembly next to its own plugin registers one per DLL, and each of those
            // has its own config file named after it.
            var guids = metadata
                .Select(m => m.Guid)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            results.Add(new InstalledMod
            {
                Name = name,
                Version = dll is null ? null : TryGetFileVersion(dll),
                Guid = guids.FirstOrDefault(),
                Guids = guids,
                Target = InstalledModTarget.Client,
                IsPatcher = isPatcher,
                FolderPath = dir,
                InstalledAt = TryGetCreationTime(dir),
                IsDisabled = disabled,
                Dependencies = MergeDependencies(metadata.SelectMany(m => m.Dependencies)),
            });
        }

        foreach (var dll in Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            if (IsCoreEntry(name, isPatcher)) continue;

            var metadata = ReadPluginMetadata(dll);

            results.Add(new InstalledMod
            {
                Name = name,
                Version = TryGetFileVersion(dll),
                Guid = metadata.Guid,
                Guids = metadata.Guid is null ? [] : [metadata.Guid],
                Target = InstalledModTarget.Client,
                IsPatcher = isPatcher,
                FolderPath = dll,
                InstalledAt = TryGetCreationTime(dll),
                IsDisabled = disabled,
                Dependencies = MergeDependencies(metadata.Dependencies),
            });
        }
    }

    // Server mods are versioned via their package.json manifest's "name"/"version"/"author"
    // fields. Falls back to scanning for a DLL and reading its FileVersionInfo when package.json is
    // missing or has no version.
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

            if (version is null)
            {
                // Fall back to a DLL in the folder, preferring one whose name matches the folder/mod name.
                var dlls = Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).ToList();
                var dll = dlls.FirstOrDefault(d => string.Equals(Path.GetFileNameWithoutExtension(d), name, StringComparison.OrdinalIgnoreCase))
                    ?? dlls.FirstOrDefault();
                if (dll is not null) version = TryGetFileVersion(dll);
            }

            results.Add(new InstalledMod
            {
                Name = name,
                Version = version,
                Author = author,
                Target = InstalledModTarget.Server,
                FolderPath = dir,
                InstalledAt = TryGetCreationTime(dir),
                IsDisabled = disabled,
                Dependencies = MergeDependencies(dependencies),
            });
        }
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

    // Distinct by identifier, keeping the hardest declaration when the same one appears twice.
    private static List<ModDependencyRef> MergeDependencies(IEnumerable<ModDependencyRef> dependencies) =>
        dependencies
            .GroupBy(d => d.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ModDependencyRef(g.Key, g.All(d => d.IsSoft)))
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

    /// <summary>A plugin DLL's [BepInPlugin] GUID (null when it declares none) and file version.</summary>
    public static (string? Guid, string? Version) ReadPlugin(string dllPath) =>
        (ReadPluginMetadata(dllPath).Guid, TryGetFileVersion(dllPath));

    // What a compiled BepInEx plugin DLL declares about itself.
    private readonly record struct PluginMetadata(string? Guid, IReadOnlyList<ModDependencyRef> Dependencies);

    //
    // Reads the GUID from a compiled BepInEx plugin DLL's [BepInPlugin("guid", ...)] attribute and
    // the GUIDs of every [BepInDependency("guid", ...)] on the same types, by walking the raw
    // PE/ECMA-335 metadata without loading or executing the assembly. Returns an empty result
    // (never throws) if the DLL isn't readable managed code.
    //
    private static PluginMetadata ReadPluginMetadata(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return new PluginMetadata(null, []);

            var reader = peReader.GetMetadataReader();
            string? guid = null;
            var dependencies = new List<ModDependencyRef>();

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);

                foreach (var attributeHandle in typeDef.GetCustomAttributes())
                {
                    var attribute = reader.GetCustomAttribute(attributeHandle);

                    if (guid is null && IsBepInExAttribute(reader, attribute, "BepInPlugin"))
                    {
                        var value = TryDecodeFirstStringArgument(reader, attribute);
                        if (!string.IsNullOrWhiteSpace(value)) guid = value;
                        continue;
                    }

                    if (!IsBepInExAttribute(reader, attribute, "BepInDependency")) continue;

                    var dependency = TryDecodeDependency(reader, attribute);
                    if (dependency is not null) dependencies.Add(dependency);
                }
            }

            return new PluginMetadata(guid, dependencies);
        }
        catch (Exception)
        {
            return new PluginMetadata(null, []);
        }
    }

    // True if this custom attribute's constructor resolves to BepInEx.<name>, resolved by
    // namespace/name from the DLL's own TypeReference table without loading BepInEx.dll.
    private static bool IsBepInExAttribute(MetadataReader reader, CustomAttribute attribute, string name)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference) return false;

        var memberRef = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (memberRef.Parent.Kind != HandleKind.TypeReference) return false;

        var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
        return reader.GetString(typeRef.Namespace) == "BepInEx"
            && reader.GetString(typeRef.Name) == name;
    }

    private static string? TryDecodeFirstStringArgument(MetadataReader reader, CustomAttribute attribute)
    {
        var blobReader = reader.GetBlobReader(attribute.Value);

        // Custom attribute value blobs start with a fixed 2-byte prolog (0x0001) per ECMA-335 II.23.3.
        if (blobReader.ReadUInt16() != 1) return null;

        return blobReader.ReadSerializedString();
    }

    //
    // Decodes a [BepInDependency] into its GUID and hardness. BepInEx has two constructors:
    // (string guid, DependencyFlags flags) and (string guid, string minimumVersion) - the second
    // is always a hard dependency. Which one was used is read from the constructor's own signature,
    // since the value blob alone can't tell an enum from a string.
    //
    private static ModDependencyRef? TryDecodeDependency(MetadataReader reader, CustomAttribute attribute)
    {
        var memberRef = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);

        var blobReader = reader.GetBlobReader(attribute.Value);
        if (blobReader.ReadUInt16() != 1) return null;

        var guid = blobReader.ReadSerializedString();
        if (string.IsNullOrWhiteSpace(guid)) return null;

        var isSoft = SecondParameterIsEnum(reader, memberRef)
            && (blobReader.ReadInt32() & SoftDependencyFlag) != 0;

        return new ModDependencyRef(guid, isSoft);
    }

    // BepInEx.BepInDependency.DependencyFlags.SoftDependency.
    private const int SoftDependencyFlag = 2;

    //
    // True when a member reference's second parameter is a value type (the DependencyFlags enum)
    // rather than a string. Walks the raw method signature blob: calling convention, parameter
    // count, return type, then each parameter's element type.
    //
    private static bool SecondParameterIsEnum(MetadataReader reader, MemberReference memberRef)
    {
        try
        {
            var signature = reader.GetBlobReader(memberRef.Signature);

            var callingConvention = signature.ReadByte();
            if ((callingConvention & 0x10) != 0) signature.ReadCompressedInteger();

            var parameterCount = signature.ReadCompressedInteger();
            if (parameterCount < 2) return false;

            // Return type of a constructor is void.
            signature.ReadSignatureTypeCode();

            // First parameter is the GUID string.
            signature.ReadSignatureTypeCode();

            // TypeHandle covers both ELEMENT_TYPE_VALUETYPE and ELEMENT_TYPE_CLASS; the enum
            // overload is the only one of the two BepInEx declares.
            return signature.ReadSignatureTypeCode() == SignatureTypeCode.TypeHandle;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
