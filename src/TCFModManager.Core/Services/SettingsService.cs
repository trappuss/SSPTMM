using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Loads/saves AppSettings as JSON under &lt;app folder&gt;\Data\settings.json (see AppPaths).
// 
public sealed class SettingsService
{
    private readonly string _filePath = Path.Combine(AppPaths.DataDirectory, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(_filePath)) return new AppSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupt or hand-edited settings file - kept aside, then fall back to defaults.
            SafeFile.PreserveDamaged(_filePath);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteText(_filePath, json, keepBackups: true);
    }
}
