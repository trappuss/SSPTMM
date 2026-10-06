using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Loads/saves AppSettings as JSON under &lt;app folder&gt;\Data\settings.json (see AppPaths).
//
// Fork (SSPTMM 1.3.0): saves merge instead of overwriting. Every part of the app loads its own copy
// of the settings, changes a value or two and saves the whole object - so two parts whose load and
// save overlapped (one waiting on the network or on a dialog in between) used to wipe each other's
// changes: the second save wrote back the first one's values as they were when the second loaded.
//
// Now Load remembers what each copy looked like when it was read, and Save writes only what that
// copy changed since, onto the file as it is now - under one lock, so reading, merging and writing
// are never interleaved. A value changed in two copies at once: the later save wins, as before.
// Values are compared property by property (nested objects too); a list counts as one value.
//
// A copy that was never loaded (new AppSettings()) is written whole, as before.
//
public sealed class SettingsService
{
    private static readonly Lock Gate = new();

    // What each loaded copy looked like when it was read (or last saved), by object identity.
    private static readonly ConditionalWeakTable<AppSettings, JsonObject> Snapshots = [];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _filePath;

    public SettingsService() : this(Path.Combine(AppPaths.DataDirectory, "settings.json"))
    {
    }

    // A file of its own - for the tests.
    public SettingsService(string filePath) => _filePath = filePath;

    public AppSettings Load()
    {
        lock (Gate)
        {
            var (settings, clean) = ReadFile();

            // A file that fell back to defaults is not merged onto later: the next save writes it
            // whole, which is what repairs it.
            if (clean) Snapshots.AddOrUpdate(settings, ToNode(settings));
            return settings;
        }
    }

    public void Save(AppSettings settings)
    {
        lock (Gate)
        {
            var current = ToNode(settings);

            var toWrite = current;
            if (Snapshots.TryGetValue(settings, out var loaded) && File.Exists(_filePath) && ReadNode() is { } onDisk)
            {
                // Only what this copy changed, onto the file as it is now - as long as the result
                // still reads back as settings. A file hand-edited into something that doesn't (a
                // value of the wrong type, a key twice) is written whole instead, as before 1.3.0.
                try
                {
                    Merge(onDisk, loaded, current);
                    _ = onDisk.Deserialize<AppSettings>();
                    toWrite = onDisk;
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    AppLog.Warn("Settings", $"settings.json couldn't be merged onto ({ex.Message}); written whole");
                }
            }

            SafeFile.WriteText(_filePath, toWrite.ToJsonString(Indented), keepBackups: true);

            // The next save of this copy starts from here.
            Snapshots.AddOrUpdate(settings, ToNode(settings));
        }
    }

    // The settings, and whether they are the file's own (false when the defaults stood in).
    private (AppSettings Settings, bool Clean) ReadFile()
    {
        if (!File.Exists(_filePath)) return (new AppSettings(), true);

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) is { } settings
                ? (settings, true)
                : (new AppSettings(), false);
        }
        catch (JsonException)
        {
            // Corrupt or hand-edited settings file - kept aside, then fall back to defaults.
            SafeFile.PreserveDamaged(_filePath);
            return (new AppSettings(), false);
        }
    }

    // The file as a JSON object, or null when it can't be read as one (then the copy is written whole).
    private JsonObject? ReadNode()
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(_filePath)) as JsonObject;

            // Duplicate keys only throw when the object is first looked into - here, not mid-merge.
            _ = node?.Count;
            return node;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonObject ToNode(AppSettings settings) =>
        JsonSerializer.SerializeToNode(settings)?.AsObject() ?? new JsonObject();

    //
    // Applies to target every property that differs between before and after. Objects on both
    // sides are compared property by property - dictionaries in the settings (shared files, page
    // sizes) are objects too, so a key added or removed in one copy merges with the others; anything
    // else (values, lists, null) is one value.
    //
    internal static void Merge(JsonObject target, JsonObject before, JsonObject after)
    {
        foreach (var (name, _) in before)
        {
            if (!after.ContainsKey(name)) target.Remove(name);
        }

        foreach (var (name, now) in after)
        {
            var was = before[name];
            if (JsonNode.DeepEquals(was, now)) continue;

            if (now is JsonObject nowObject && was is JsonObject wasObject && target[name] is JsonObject targetObject)
            {
                Merge(targetObject, wasObject, nowObject);
            }
            else
            {
                target[name] = now?.DeepClone();
            }
        }
    }
}
