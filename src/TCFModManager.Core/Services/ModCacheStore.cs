using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Persists the full mod catalog fetched by ModCacheService to disk, so it doesn't need to be
// re-fetched on every launch.
// 
public sealed class ModCacheStore
{
    // Bump if Mod's shape changes in a way that makes an old cache file unsafe to trust.
    // A mismatched version is treated as "no cache".
    //
    // 2: Mod.EndorsementsCount added. Nothing here expires a cache on age, so a v1 file would keep
    //    being served indefinitely with the new field absent - every mod would read as 0
    //    endorsements and the "Most endorsed" sort would look broken rather than empty.
    private const int SchemaVersion = 2;

    private readonly string _filePath;

    public ModCacheStore()
    {
        // Stored in the Data\ folder next to the exe; not migrated from the legacy
        // %LocalAppData% location since this file is a rebuildable cache.
        _filePath = Path.Combine(AppPaths.DataDirectory, "mod_cache.json");
    }

    public sealed class CachedCatalog
    {
        public int SchemaVersion { get; set; }
        public DateTimeOffset FetchedAt { get; set; }
        public List<Mod> Mods { get; set; } = [];
    }

    public CachedCatalog? Load()
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<CachedCatalog>(json);
            if (data is null || data.SchemaVersion != SchemaVersion || data.Mods.Count == 0) return null;
            return data;
        }
        catch (JsonException)
        {
            // Corrupt or incompatible cache file - fall back to a live fetch.
            return null;
        }
    }

    public void Save(IReadOnlyList<Mod> mods)
    {
        try
        {
            var data = new CachedCatalog
            {
                SchemaVersion = SchemaVersion,
                FetchedAt = DateTimeOffset.UtcNow,
                Mods = mods.ToList(),
            };
            SafeFile.WriteAllText(_filePath, JsonSerializer.Serialize(data));
        }
        catch (IOException)
        {
            // A failed cache write just means the next launch does a full live fetch again.
        }
    }
}
