using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Backs DataFilesWindow - a raw view/edit window over the JSON files this app keeps under Data\,
// for when the built-in pages don't cover what someone needs to fix by hand (e.g. installed-mods.json
// after the Installed page's own "manage version" controls aren't enough). Every save is validated as
// JSON first and backed up to "<file>.bak", so a bad edit can't silently corrupt what the app reads
// on next launch.
//
public partial class DataFilesViewModel : LocalizedViewModel
{
    // Known files this app writes, shown first and in this order even when some don't exist yet.
    // Anything else found under Data\*.json (e.g. from a future file, or a hand-added one) is listed
    // after these, alphabetically.
    private static readonly string[] KnownFiles =
    [
        "installed-mods.json",
        "settings.json",
        "dependency_flags.json",
        "mod_cache.json",
    ];

    public List<string> Files { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private string? _selectedFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _text = string.Empty;

    // The text as last loaded from (or saved to) disk, used to tell whether Text has unsaved edits.
    private string _savedText = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    // True when StatusMessage describes a validation/save failure rather than a plain confirmation.
    [ObservableProperty]
    private bool _hasError;

    public bool IsDirty => Text != _savedText;

    public DataFilesViewModel()
    {
        var found = Directory.Exists(AppPaths.DataDirectory)
            ? Directory.EnumerateFiles(AppPaths.DataDirectory, "*.json").Select(Path.GetFileName)
            : [];

        Files = KnownFiles
            .Concat((found ?? []).Where(f => f is not null && !KnownFiles.Contains(f))!)
            .Select(f => f!)
            .OrderBy(f => Array.IndexOf(KnownFiles, f) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SelectedFile = Files.FirstOrDefault();
    }

    partial void OnSelectedFileChanged(string? value) => Load();

    private static string? PathFor(string? file) =>
        file is null ? null : Path.Combine(AppPaths.DataDirectory, file);

    // The file Text was loaded from: Save writes back to it, not to whatever PathFor would say now -
    // installed-mods.json follows the install setting, which can change while this window is open.
    private string? _loadedPath;

    private void Load()
    {
        HasError = false;
        var path = _loadedPath = PathFor(SelectedFile);
        if (path is null)
        {
            Text = string.Empty;
            _savedText = string.Empty;
            StatusMessage = null;
            return;
        }

        if (!File.Exists(path))
        {
            Text = string.Empty;
            _savedText = string.Empty;
            StatusMessage = LocalizationService.Text(Strings.DataFiles_MissingFormat, SelectedFile);
            return;
        }

        try
        {
            Text = File.ReadAllText(path);
            _savedText = Text;
            StatusMessage = null;
        }
        catch (IOException ex)
        {
            Text = string.Empty;
            _savedText = string.Empty;
            HasError = true;
            StatusMessage = LocalizationService.Text(Strings.DataFiles_ReadFailedFormat, SelectedFile, ex.Message);
        }
    }

    private bool CanReload() => SelectedFile is not null;

    [RelayCommand(CanExecute = nameof(CanReload))]
    private void Reload() => Load();

    private bool CanSave() => SelectedFile is not null && Text.Length > 0;

    // Validates Text as JSON (and, for installed-mods.json, as a well-formed manifest) before
    // writing anything. A file that already exists is copied to "<file>.bak" first, overwriting any
    // previous backup, so one bad hand-edit is always recoverable.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var path = _loadedPath;
        if (path is null) return;

        if (!TryValidate(SelectedFile!, Text, out var error))
        {
            HasError = true;
            StatusMessage = LocalizationService.Text(Strings.DataFiles_NotSavedFormat, error);
            return;
        }

        try
        {
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            SafeFile.WriteText(path, Text, keepBackups: true);
            _savedText = Text;
            HasError = false;
            StatusMessage = LocalizationService.Text(
                Strings.DataFiles_SavedFormat, SelectedFile, Path.GetFileName(path));
        }
        catch (IOException ex)
        {
            HasError = true;
            StatusMessage = LocalizationService.Text(Strings.DataFiles_NotSavedFormat, ex.Message);
        }
    }

    // Plain JSON validity for any file; installed-mods.json additionally has to deserialize as a
    // ModInstallManifest, since that's the shape the rest of the app actually reads.
    private static bool TryValidate(string file, string text, out string error)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            if (string.Equals(file, "installed-mods.json", StringComparison.OrdinalIgnoreCase))
            {
                var manifest = JsonSerializer.Deserialize<ModInstallManifest>(text);
                if (manifest is null)
                {
                    error = Strings.DataFiles_ErrorNotManifest;
                    return false;
                }
            }

            error = "";
            return true;
        }
        catch (JsonException ex)
        {
            error = LocalizationService.Text(Strings.DataFiles_ErrorInvalidJsonFormat, ex.Message);
            return false;
        }
    }
}
