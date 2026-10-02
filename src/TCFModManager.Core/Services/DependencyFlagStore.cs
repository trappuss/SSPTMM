using System.Text.Json;

namespace TCFModManager.Core.Services;

// 
// Persists "does this mod's latest version have dependencies" per mod id, so Browse's dependency
// badges can render from disk on a later launch instead of costing one sp-mod.com call per card.
// 
public sealed class DependencyFlagStore
{
    // Bump if Entry's shape changes in a way that makes an old file unsafe to trust.
    private const int SchemaVersion = 1;

    private readonly string _filePath;

    public DependencyFlagStore()
    {
        // Stored in the Data\ folder next to the exe, alongside mod_cache.json; not migrated from
        // the legacy %LocalAppData% location since this file is a rebuildable cache.
        _filePath = Path.Combine(AppPaths.DataDirectory, "dependency_flags.json");
    }

    // One mod's cached answer. CheckedAt is compared against the mod's own UpdatedAt to
    // decide whether the answer still holds.
    public sealed class Entry
    {
        public bool HasDependencies { get; set; }
        public DateTimeOffset CheckedAt { get; set; }
    }

    private sealed class CachedFlags
    {
        public int SchemaVersion { get; set; }
        public Dictionary<int, Entry> Flags { get; set; } = [];
    }

    // Reads the cached flags, or an empty dictionary when there's no usable file.
    public Dictionary<int, Entry> Load()
    {
        if (!File.Exists(_filePath)) return [];

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<CachedFlags>(json);
            if (data is null || data.SchemaVersion != SchemaVersion) return [];
            return data.Flags;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt, incompatible, or unreadable - start over rather than fail the page.
            return [];
        }
    }

    public void Save(IReadOnlyDictionary<int, Entry> flags)
    {
        try
        {
            var data = new CachedFlags
            {
                SchemaVersion = SchemaVersion,
                Flags = flags.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            };
            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(data));
        }
        catch (IOException)
        {
            // A failed write just means these flags get looked up again next launch.
        }
    }
}
