using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Persists each mod's analysed footprint per folder, so the Performance page renders from disk on a
// later launch instead of re-reading every assembly in the install. Same shape and same reasoning
// as DependencyFlagStore, with one difference: what invalidates an entry is a stamp of the folder
// itself rather than a timestamp from a catalog, because nothing here comes from sp-mod.com.
//
public sealed class ModFootprintStore
{
    // Bump when ModFootprint's stored shape changes in a way that makes an old file unsafe to
    // trust - in particular when a count changes meaning, since Level is derived from the counts
    // and an old file would silently produce a new answer.
    private const int SchemaVersion = 4;

    private readonly string _filePath;

    public ModFootprintStore(string? filePath = null)
    {
        // Optional path for testability, matching ModListStore's accepted deviation.
        _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "mod_footprints.json");
    }

    private sealed class CachedFootprints
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, ModFootprint> Footprints { get; set; } = [];
    }

    // Cached footprints by folder key, or an empty dictionary when there's no usable file.
    public Dictionary<string, ModFootprint> Load()
    {
        if (!File.Exists(_filePath)) return [];

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<CachedFootprints>(json);
            if (data is null || data.SchemaVersion != SchemaVersion) return [];
            return data.Footprints;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt, incompatible, or unreadable - re-analysing is cheap and always correct.
            return [];
        }
    }

    public void Save(IReadOnlyDictionary<string, ModFootprint> footprints)
    {
        try
        {
            var data = new CachedFootprints
            {
                SchemaVersion = SchemaVersion,
                Footprints = footprints.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            };
            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(data));
        }
        catch (IOException)
        {
            // A failed write just means the next launch analyses again.
        }
    }

    //
    // Whether a cached footprint still describes what is on disk. Kept here rather than on the
    // model so ModFootprint stays a plain record with no opinion about the filesystem.
    //
    public static bool IsCurrent(ModFootprint? cached, string currentStamp) =>
        cached is not null
        && !string.IsNullOrEmpty(cached.Stamp)
        && cached.Stamp == currentStamp;
}
