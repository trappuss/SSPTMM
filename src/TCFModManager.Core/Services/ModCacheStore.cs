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

    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

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
            //
            // Read as the UTF-8 it is stored in, not through a string: the file is a few megabytes,
            // and a string of it is twice that in UTF-16 which the parser then turns back into
            // UTF-8. Measured on a 2.7 MB catalog (1,424 mods): 29 ms and 14 MB before, 8 ms and 6 MB
            // after. A byte order mark is stepped over by hand because the parser, given bytes,
            // reads one as a syntax error; Save never writes one.
            //
            ReadOnlySpan<byte> utf8 = File.ReadAllBytes(_filePath);
            if (utf8.StartsWith(Utf8ByteOrderMark)) utf8 = utf8[Utf8ByteOrderMark.Length..];

            CachedCatalog? data;
            try
            {
                data = JsonSerializer.Deserialize<CachedCatalog>(utf8);
            }
            catch (JsonException)
            {
                // Not UTF-8 after all - a copy saved from an editor as UTF-16, say. Read the way
                // it always was, which works the encoding out; a file that is simply damaged
                // fails again here and is no cache, as before.
                data = JsonSerializer.Deserialize<CachedCatalog>(File.ReadAllText(_filePath));
            }

            if (data is null || data.SchemaVersion != SchemaVersion || data.Mods.Count == 0) return null;
            return data;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt, incompatible or unreadable right now - fall back to a live fetch.
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
            // The same bytes WriteText made of the string, without the string in between.
            SafeFile.WriteBytes(_filePath, JsonSerializer.SerializeToUtf8Bytes(data));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed cache write just means the next launch does a full live fetch again.
        }
    }
}
