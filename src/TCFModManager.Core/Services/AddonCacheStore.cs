using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Persists the addon catalog fetched by AddonCacheService to disk, so it doesn't need to be
// re-fetched on every launch. Mirrors ModCacheStore.
// 
public sealed class AddonCacheStore
{
    // Bump if Addon's shape changes in a way that makes an old cache file unsafe to trust.
    // A mismatched version is treated as "no cache".
    private const int SchemaVersion = 1;

    private readonly string _filePath = Path.Combine(AppPaths.DataDirectory, "addon_cache.json");

    public sealed class CachedAddons
    {
        public int SchemaVersion { get; set; }
        public DateTimeOffset FetchedAt { get; set; }
        public List<Addon> Addons { get; set; } = [];
    }

    public CachedAddons? Load()
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            var data = JsonSerializer.Deserialize<CachedAddons>(File.ReadAllText(_filePath));
            if (data is null || data.SchemaVersion != SchemaVersion || data.Addons.Count == 0) return null;
            return data;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt, incompatible or unreadable right now - fall back to a live fetch.
            return null;
        }
    }

    public void Save(IReadOnlyList<Addon> addons)
    {
        try
        {
            var data = new CachedAddons
            {
                SchemaVersion = SchemaVersion,
                FetchedAt = DateTimeOffset.UtcNow,
                Addons = addons.ToList(),
            };

            Directory.CreateDirectory(AppPaths.DataDirectory);
            SafeFile.WriteAllText(_filePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache that can't be written just means the next launch refetches.
            AppLog.Warn("Addons", $"couldn't write the addon cache: {ex.Message}");
        }
    }
}
