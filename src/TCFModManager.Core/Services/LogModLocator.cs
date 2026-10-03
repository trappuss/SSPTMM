using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>An installed mod a log line was traced to. <paramref name="Name"/> is its folder name - the
/// key Subscribed items uses - and <paramref name="DisplayName"/> what it calls itself.</summary>
public sealed record DiagnosedMod(string Name, string DisplayName, string FolderPath, InstalledModTarget Target, string? Guid)
{
    /// <summary>When its folder was made - the scan's stand-in for when it was installed.</summary>
    public DateTimeOffset? InstalledAt { get; init; }
}

//
// Fork (SSPTMM): which installed mod a log line is about. Logs name a mod in several ways, all read
// from the mods' own files - nothing is guessed from a name looking similar:
//
//   - BepInEx names a plugin "[Name Version]" - the [BepInPlugin] name, read from every plugin DLL;
//   - plugin GUIDs and server mod GUIDs, as dependencies and SPT's mod list name them;
//   - a server log's logger and a stack trace's frames are type names - matched to the namespaces each
//     mod's DLLs define types in (longest match wins; a namespace two mods both define names neither);
//   - a bundle path, matched to the server mod folders that hold that file under bundles\.
//
// Framework namespaces (System, Microsoft, SPT's own, BepInEx, Harmony, Unity, the game's) are never
// taken as a mod's, even when a mod's DLL defines a type in one (compiler-generated attributes do).
//
public sealed class LogModLocator
{
    private static readonly string[] FrameworkPrefixes =
    [
        "System", "Microsoft", "SPTarkov", "SPTushonka", "SPT", "BepInEx", "HarmonyLib", "MonoMod", "Mono",
        "UnityEngine", "Unity", "EFT", "Comfort", "Newtonsoft", "JetBrains", "Internal", "Aki", "Diz", "TMPro",
    ];

    // Client plugin names and GUIDs, and apart from them server mods' declared names and GUIDs: a mod
    // with both halves often gives both one name (SAIN), and a BepInEx line means the plugin.
    private readonly Dictionary<string, DiagnosedMod?> _byPlugin = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiagnosedMod?> _byServerName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiagnosedMod?> _byNamespace = new(StringComparer.Ordinal);
    private readonly List<DiagnosedMod> _serverMods = [];
    private readonly List<DiagnosedMod> _all = [];

    public IReadOnlyList<DiagnosedMod> ServerMods => _serverMods;

    public IReadOnlyList<DiagnosedMod> All => _all;

    /// <summary>From the scan of the install: enabled mods only (a disabled one isn't loaded).</summary>
    public static LogModLocator Build(IEnumerable<InstalledMod> scanned)
    {
        var locator = new LogModLocator();

        foreach (var mod in scanned.Where(m => !m.IsDisabled))
        {
            var found = new DiagnosedMod(mod.Name, mod.DeclaredName ?? mod.Name, mod.FolderPath, mod.Target, mod.Guid)
            {
                InstalledAt = mod.InstalledAt,
            };
            locator._all.Add(found);
            if (mod.Target == InstalledModTarget.Server) locator._serverMods.Add(found);

            var names = mod.Target == InstalledModTarget.Client ? locator._byPlugin : locator._byServerName;
            foreach (var guid in mod.AllGuids) AddName(names, guid, found);
            if (mod.DeclaredName is { Length: > 0 } declared) AddName(names, declared, found);

            foreach (var dll in DllsOf(mod))
            {
                if (mod.Target == InstalledModTarget.Client)
                {
                    var plugin = ModAssemblyMetadata.ReadPlugin(dll);
                    if (plugin.Guid is { } guid) AddName(locator._byPlugin, guid, found);
                    if (plugin.Name is { Length: > 0 } name) AddName(locator._byPlugin, name, found);
                }

                foreach (var ns in ModAssemblyMetadata.ReadNamespaces(dll))
                {
                    if (IsFramework(ns)) continue;
                    locator._byNamespace[ns] = locator._byNamespace.TryGetValue(ns, out var other) && !SameMod(other, found) ? null : found;
                }
            }
        }

        return locator;
    }

    // For tests and for logs read without a scan: the mods as given.
    public static LogModLocator From(IEnumerable<(DiagnosedMod Mod, IEnumerable<string> Names, IEnumerable<string> Namespaces)> mods)
    {
        var locator = new LogModLocator();
        foreach (var (mod, names, namespaces) in mods)
        {
            locator._all.Add(mod);
            if (mod.Target == InstalledModTarget.Server) locator._serverMods.Add(mod);
            var into = mod.Target == InstalledModTarget.Client ? locator._byPlugin : locator._byServerName;
            if (mod.Guid is { } guid) AddName(into, guid, mod);
            AddName(into, mod.DisplayName, mod);
            foreach (var name in names) AddName(into, name, mod);
            foreach (var ns in namespaces.Where(n => !IsFramework(n)))
                locator._byNamespace[ns] = locator._byNamespace.TryGetValue(ns, out var other) && !SameMod(other, mod) ? null : mod;
        }

        return locator;
    }

    /// <summary>The mod with this plugin name or GUID - or, when no plugin has it, the server mod with
    /// that declared name or GUID; null when none, or when two mods share it.</summary>
    public DiagnosedMod? ByName(string name) =>
        _byPlugin.TryGetValue(name.Trim(), out var plugin) ? plugin
        : _byServerName.TryGetValue(name.Trim(), out var server) ? server
        : null;

    /// <summary>The mod whose code a type or member name ("Roulette.Server.RouletteCallbacks.State") is in.</summary>
    public DiagnosedMod? ByCode(string qualifiedName)
    {
        var name = qualifiedName;
        while (true)
        {
            var dot = name.LastIndexOf('.');
            if (dot <= 0) return null;
            name = name[..dot];
            if (_byNamespace.TryGetValue(name, out var found)) return found;
        }
    }

    /// <summary>The server mods holding this bundle (a path as SPT logs it, "assets/content/.../x.bundle").</summary>
    public IReadOnlyList<DiagnosedMod> ByBundle(string bundle)
    {
        var relative = bundle.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return _serverMods.Where(m => File.Exists(Path.Combine(m.FolderPath, "bundles", relative))).ToList();
    }

    private static void AddName(Dictionary<string, DiagnosedMod?> names, string name, DiagnosedMod mod)
    {
        var key = name.Trim();
        if (key.Length == 0) return;
        names[key] = names.TryGetValue(key, out var other) && !SameMod(other, mod) ? null : mod;
    }

    private static bool SameMod(DiagnosedMod? a, DiagnosedMod b) =>
        a is not null && string.Equals(a.FolderPath, b.FolderPath, StringComparison.OrdinalIgnoreCase);

    private static bool IsFramework(string ns) =>
        FrameworkPrefixes.Any(p => ns == p || ns.StartsWith(p + ".", StringComparison.Ordinal));

    private static IEnumerable<string> DllsOf(InstalledMod mod)
    {
        if (File.Exists(mod.FolderPath)) return mod.FolderPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? [mod.FolderPath] : [];

        try
        {
            return Directory.Exists(mod.FolderPath)
                ? Directory.EnumerateFiles(mod.FolderPath, "*.dll", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
