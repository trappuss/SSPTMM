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
        // A damaged file is kept aside and its backup put back - see SafeFile.
        return SafeFile.ReadJson(_filePath, json => JsonSerializer.Deserialize<AppSettings>(json)) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(_filePath, json, keepBackup: true);
    }
}
