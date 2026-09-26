using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// The Configs page: every config file the installed mods actually have, and an editor for the
// selected one.
//
// The list is grouped by where a file lives, because that is the difference that matters to whoever
// is editing it - a client mod's settings sit in the shared BepInEx\config folder and outlive the
// mod, a server mod's sit inside its own folder and travel with it. See ModConfigDiscovery for how
// each file is found and attributed.
//
// This is the raw-text stage: both formats are edited as text, with JSON checked before it is
// written. The generated form for .cfg files comes next, on top of BepInExConfigFile, which already
// parses everything it needs.
//
public sealed partial class ConfigsViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private List<ConfigEntryViewModel> _all = [];

    // The file as it was last read from disk. Carries the write time a save is checked against and
    // the byte order mark to reproduce, and is what Revert and the dirty check compare with.
    private ModConfigDocument? _loaded;

    // The entry _loaded belongs to, which is not always SelectedEntry - the selection moves first
    // and the load follows, and a discarded prompt puts the selection back.
    private ConfigEntryViewModel? _loadedEntry;

    // Guards the selection being put back after the user declines to discard their edits, so the
    // change that restores it doesn't run the prompt a second time.
    private bool _restoringSelection;

    // Per-mod update policy, and what recent updates did to each file.
    private readonly ModConfigOptionsStore _options = new();
    private readonly ConfigUpdateLog _updates = new();

    // Guards the policy dropdown being set to match the newly selected file, so showing a stored
    // choice doesn't read as making one.
    private bool _showingPolicy;

    public ConfigsViewModel()
    {
        SourceFilterOptions =
        [
            new ConfigSourceFilterItem(nameof(Strings.Filter_ConfigAll), ConfigSourceFilter.All),
            new ConfigSourceFilterItem(nameof(Strings.Filter_ConfigClient), ConfigSourceFilter.Client),
            new ConfigSourceFilterItem(nameof(Strings.Filter_ConfigServer), ConfigSourceFilter.Server),
            new ConfigSourceFilterItem(nameof(Strings.Filter_ConfigOther), ConfigSourceFilter.Other),
        ];

        _selectedSourceFilter = SourceFilterOptions[0];

        PolicyOptions =
        [
            new ConfigPolicyOption(nameof(Strings.ConfigPolicy_Merge), ModConfigPolicy.Merge),
            new ConfigPolicyOption(nameof(Strings.ConfigPolicy_KeepMine), ModConfigPolicy.KeepMine),
            new ConfigPolicyOption(nameof(Strings.ConfigPolicy_TakeNew), ModConfigPolicy.TakeNew),
        ];

        _selectedPolicyOption = PolicyOptions[0];
    }

    //
    // What an update does with this mod's config when the user has changed it. Per mod rather than per
    // file: a mod's configs are one decision, and the store is keyed by its folder.
    //
    public IReadOnlyList<ConfigPolicyOption> PolicyOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PolicyNote))]
    private ConfigPolicyOption _selectedPolicyOption;

    public string PolicyNote => ConfigUpdateWording.PolicyNote(SelectedPolicyOption.Value);

    public bool ShowPolicy => SelectedEntry?.CanSetPolicy == true;

    //
    // The places this mod keeps things that no convention would find, and the ones that only look like
    // config. Shown as chips under the policy, because they are per mod exactly as the policy is.
    //
    public ObservableCollection<ConfigLocationChip> Locations { get; } = [];

    public bool HasLocations => Locations.Count > 0;

    //
    // A folder inside the mod holding documents the user wrote - SVM's Presets. An update leaves it
    // alone; a removal rescues it.
    //
    [RelayCommand]
    private async Task AddUserDataFolderAsync()
    {
        if (ModFolder() is not ({ } modName, { } modFolder)) return;

        var dialog = new OpenFolderDialog
        {
            Title = Text(Strings.Configs_AddUserDataTitleFormat, modName),
            InitialDirectory = modFolder,
        };

        if (dialog.ShowDialog() != true) return;

        if (Inside(modFolder, dialog.FolderName) is not { } relative)
        {
            StatusMessage = Strings.Configs_FolderNotInsideMod;
            return;
        }

        // The guard rail D7 exists for: an opted-in folder is somebody's presets, not a database.
        var count = CountFiles(dialog.FolderName);
        if (count > ModConfigFiles.MaxUserDataFiles)
        {
            StatusMessage = Text(Strings.Configs_TooManyFilesFormat, relative, count);
            return;
        }

        var stored = _options.For(modName);
        _options.SetUserData(modName, [.. stored.UserData, relative]);
        await AfterLocationsChangedAsync(
            modName,
            Text(Strings.Configs_UserDataAddedFormat, relative, modName));
    }

    // A settings file the mod keeps somewhere nothing would look - SVM's Loader\loader.json.
    [RelayCommand]
    private async Task AddSettingsFileAsync()
    {
        if (ModFolder() is not ({ } modName, { } modFolder)) return;

        var dialog = new OpenFileDialog
        {
            Title = Text(Strings.Configs_AddSettingsFileTitleFormat, modName),
            InitialDirectory = modFolder,
            Filter = Strings.Configs_FileFilter,
        };

        if (dialog.ShowDialog() != true) return;

        if (Inside(modFolder, dialog.FileName) is not { } relative)
        {
            StatusMessage = Strings.Configs_FileNotInsideMod;
            return;
        }

        var stored = _options.For(modName);
        _options.SetSettings(modName, [.. stored.Settings, relative]);
        await AfterLocationsChangedAsync(modName, Text(Strings.Configs_SettingsAddedFormat, relative));
    }

    // The opposite case: a file that sits in a folder called "config" and is really data.
    [RelayCommand(CanExecute = nameof(ShowPolicy))]
    private async Task IgnoreSelectedFileAsync()
    {
        if (SelectedEntry is not { Entry.ModName: { } modName } entry) return;
        if (ModFolder() is not (_, { } modFolder)) return;
        if (Inside(modFolder, entry.FullPath) is not { } relative) return;

        var stored = _options.For(modName);
        _options.SetExclude(modName, [.. stored.Exclude, relative]);
        await AfterLocationsChangedAsync(modName, Text(Strings.Configs_ExcludedFormat, relative, modName));
    }

    [RelayCommand]
    private async Task RemoveLocationAsync(ConfigLocationChip? chip)
    {
        if (chip is null || SelectedEntry?.Entry.ModName is not { } modName) return;

        var stored = _options.For(modName);

        switch (chip.Kind)
        {
            case ConfigLocationKind.UserData:
                _options.SetUserData(modName, stored.UserData.Where(p => p != chip.Path));
                break;
            case ConfigLocationKind.Settings:
                _options.SetSettings(modName, stored.Settings.Where(p => p != chip.Path));
                break;
            default:
                _options.SetExclude(modName, stored.Exclude.Where(p => p != chip.Path));
                break;
        }

        await AfterLocationsChangedAsync(modName, Text(Strings.Configs_LocationRemovedFormat, chip.Path));
    }

    //
    // Every one of these changes what counts as a config, so the list is rebuilt from disk rather than
    // patched - which is also the only honest way to show a file that has just started counting.
    //
    private async Task AfterLocationsChangedAsync(string modName, string message)
    {
        ShowLocations(SelectedEntry);
        AppLog.Info("Configs", $"{modName} config locations changed");

        await ScanAsync();
        StatusMessage = message;
    }

    private void ShowLocations(ConfigEntryViewModel? entry)
    {
        Locations.Clear();

        if (entry is { CanSetPolicy: true, Entry.ModName: { } modName })
        {
            var stored = _options.For(modName);

            foreach (var path in stored.UserData) Locations.Add(new ConfigLocationChip(ConfigLocationKind.UserData, path));
            foreach (var path in stored.Settings) Locations.Add(new ConfigLocationChip(ConfigLocationKind.Settings, path));
            foreach (var path in stored.Exclude) Locations.Add(new ConfigLocationChip(ConfigLocationKind.Exclude, path));
        }

        OnPropertyChanged(nameof(HasLocations));
    }

    //
    // The selected file's mod: its folder name, and the full path of the folder itself, found by
    // climbing out of the file rather than rebuilt from the install path - the server root is nested
    // differently on different installs and the file already knows where it is.
    //
    private (string ModName, string Folder)? ModFolder()
    {
        if (SelectedEntry is not { CanSetPolicy: true, Entry.ModName: { } modName }) return null;

        for (var dir = Path.GetDirectoryName(SelectedEntry.FullPath); dir is not null; dir = Path.GetDirectoryName(dir))
            if (string.Equals(Path.GetFileName(dir), modName, StringComparison.OrdinalIgnoreCase))
                return (modName, dir);

        return null;
    }

    // The path of something inside the mod, relative to the mod's folder - or null when it isn't.
    private static string? Inside(string modFolder, string path)
    {
        var relative = Path.GetRelativePath(modFolder, path).Replace('\\', '/');

        return relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : ModConfigPaths.Normalise(relative);
    }

    private static int CountFiles(string folder)
    {
        try
        {
            return Directory
                .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Take(ModConfigFiles.MaxUserDataFiles + 1)
                .Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    partial void OnSelectedPolicyOptionChanged(ConfigPolicyOption value)
    {
        if (_showingPolicy) return;
        if (SelectedEntry is not { CanSetPolicy: true, Entry.ModName: { } modName }) return;

        _options.SetPolicy(modName, value.Value);

        // A whole sentence per policy rather than the dropdown's own label lower-cased and dropped
        // into one: a label is a noun phrase in English and need not be one in another language,
        // and lower-casing a word is not a translation.
        StatusMessage = Text(
            value.Value switch
            {
                ModConfigPolicy.KeepMine => Strings.Configs_PolicyKeepMineFormat,
                ModConfigPolicy.TakeNew => Strings.Configs_PolicyTakeNewFormat,
                _ => Strings.Configs_PolicyMergeFormat,
            },
            modName);
        AppLog.Info("Configs", $"{modName} update policy set to {value.Value}");
    }

    //
    // The filtered list, flat and pre-sorted by section then title. The view groups it with a
    // CollectionViewSource rather than it being handed over already nested, so the whole list stays
    // one ListBox with one selection - see ConfigSectionHeader.
    //
    public ObservableCollection<ConfigEntryViewModel> Results { get; } = [];

    public IReadOnlyList<ConfigSourceFilterItem> SourceFilterOptions { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = Strings.Configs_StatusScanning;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ConfigSourceFilterItem _selectedSourceFilter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowPolicy))]
    [NotifyCanExecuteChangedFor(nameof(IgnoreSelectedFileCommand))]
    private ConfigEntryViewModel? _selectedEntry;

    public bool HasSelection => SelectedEntry is not null;

    // The editor's contents. Bound two-way, so every keystroke re-checks whether anything differs
    // from what was read off disk.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private string _editorText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private bool _isDirty;

    // Why the last save was refused, or why the file couldn't be read. Cleared on every edit.
    [ObservableProperty]
    private string? _editorError;

    //
    // Shown while SPT is running. A warning rather than a block: the file is perfectly writable, but
    // BepInEx writes its whole .cfg back out when the game closes, so an edit made now is likely to
    // be overwritten - and a server mod reads its config at startup, so an edit won't take effect
    // until the server restarts either way.
    //
    [ObservableProperty]
    private string? _runningWarning;

    [ObservableProperty]
    private bool _hasResults;

    //
    // The warning at the top of the page. Closable, and deliberately not remembered anywhere: the
    // page is built once per launch (its nav item caches it), so dismissing it lasts for the session
    // and it is back the next time the app opens. A disclaimer nobody ever sees again after the
    // first click isn't one.
    //
    [ObservableProperty]
    private bool _showDisclaimer = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedSourceFilterChanged(ConfigSourceFilterItem value) => ApplyFilter();

    partial void OnEditorTextChanged(string value)
    {
        EditorError = null;
        IsDirty = _loaded is not null && !string.Equals(value, _loaded.Text, StringComparison.Ordinal);
    }

    partial void OnSelectedEntryChanged(ConfigEntryViewModel? value)
    {
        if (_restoringSelection) return;

        // Moving away from an edited file would drop the edit silently, so it is offered back first.
        if (IsDirty && _loadedEntry is not null && !ReferenceEquals(_loadedEntry, value))
        {
            var keep = MessageBox.Show(
                Text(Strings.Configs_DiscardFormat, _loadedEntry.FileName),
                Strings.Configs_DiscardTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.No;

            if (keep)
            {
                _restoringSelection = true;
                SelectedEntry = _loadedEntry;
                _restoringSelection = false;
                return;
            }
        }

        Load(value);
        ShowStoredPolicy(value);
        ShowLocations(value);
    }

    // Puts the dropdown on this mod's stored choice without that counting as a change.
    private void ShowStoredPolicy(ConfigEntryViewModel? entry)
    {
        if (entry is not { CanSetPolicy: true, Entry.ModName: { } modName }) return;

        var stored = _options.For(modName).Policy;

        _showingPolicy = true;
        SelectedPolicyOption = PolicyOptions.FirstOrDefault(o => o.Value == stored) ?? PolicyOptions[0];
        _showingPolicy = false;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _all = [];
            ApplyFilter();
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        IsBusy = true;
        try
        {
            // Walking BepInEx\config and every server mod's folder is disk work, and the scan it
            // starts from is the same one the Installed page runs - neither belongs on the UI thread.
            var entries = await Task.Run(() =>
            {
                var installed = InstalledModScanner.Scan(installPath);
                return ModConfigDiscovery.Find(installPath, installed, _options.Effective());
            });

            // Read once for the whole list rather than per row - it is one small file.
            var history = _updates.Load().Reports;

            _all = entries.Select(e => Row(e, history)).ToList();

            ApplyFilter();
            RefreshRunningWarning();

            var mods = _all
                .Where(e => e.Entry.ModName is not null)
                .Select(e => e.Entry.ModName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            StatusMessage = _all.Count == 0
                ? Strings.Configs_NoneFound
                : Text(Strings.Configs_CountFormat, _all.Count, mods);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Text(Strings.Configs_ReadFailedFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    //
    // One row, carrying what the most recent recorded update did to that exact file so the page can
    // say it days later - which is the other half of the report the Downloads queue shows once.
    //
    private static ConfigEntryViewModel Row(ModConfigEntry entry, List<ConfigUpdateReport> history)
    {
        foreach (var report in history)
        {
            var outcome = report.Files.FirstOrDefault(f =>
                string.Equals(f.Path, entry.DisplayPath, StringComparison.OrdinalIgnoreCase));

            if (outcome is not null)
                return new ConfigEntryViewModel { Entry = entry, LastUpdate = report, LastUpdateOutcome = outcome };
        }

        return new ConfigEntryViewModel { Entry = entry };
    }

    // Rebuilds the grouped list from the search box and the source dropdown.
    private void ApplyFilter()
    {
        var term = SearchText.Trim();

        var filtered = _all
            .Where(e => SelectedSourceFilter.Matches(e.Source))
            .Where(e => term.Length == 0 || e.MatchesSearch(term))
            .OrderBy(e => e.Section.Rank)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Subtitle, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Results.Clear();
        foreach (var entry in filtered) Results.Add(entry);

        HasResults = filtered.Count > 0;

        // A filter that hides the open file leaves the editor showing something the list no longer
        // offers, so the selection goes with it.
        if (SelectedEntry is not null && !filtered.Contains(SelectedEntry) && !IsDirty) SelectedEntry = null;
    }

    //
    // The copies earlier saves kept of the open file (ModConfigStore.Backup), to bring one back: it
    // is put in the editor, not written - Save puts it back (keeping a copy of what is there now),
    // Revert leaves the file as it is.
    //
    public ObservableCollection<ConfigBackupRow> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    [ObservableProperty]
    private ConfigBackupRow? _selectedBackup;

    partial void OnSelectedBackupChanged(ConfigBackupRow? value)
    {
        if (value is null || _loaded is null || _restoringSelection) return;

        // Edits not saved yet would be replaced: asked first, as switching files does.
        if (IsDirty && _loadedEntry is not null
            && MessageBox.Show(
                Text(Strings.Configs_DiscardFormat, _loadedEntry.FileName),
                Strings.Configs_DiscardTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            _restoringSelection = true;
            try
            {
                SelectedBackup = null;
            }
            finally
            {
                _restoringSelection = false;
            }

            return;
        }

        try
        {
            EditorText = ModConfigStore.Load(value.Backup.Path).Text;
            StatusMessage = Text(Strings.Configs_BackupLoadedFormat, value.Label);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EditorError = Text(Strings.Configs_FileReadFailedFormat, ex.Message);
        }
    }

    private void RefreshBackups(ConfigEntryViewModel? entry)
    {
        Backups.Clear();
        _selectedBackup = null;
        OnPropertyChanged(nameof(SelectedBackup));

        if (entry is not null && AppServices.SptEnvironment.InstallPath is { Length: > 0 } installPath)
        {
            foreach (var backup in ModConfigStore.BackupsOf(installPath, entry.FullPath)) Backups.Add(new ConfigBackupRow(backup));
        }

        OnPropertyChanged(nameof(HasBackups));
    }

    private void Load(ConfigEntryViewModel? entry)
    {
        EditorError = null;
        _loadedEntry = entry;
        RefreshBackups(entry);

        if (entry is null)
        {
            _loaded = null;
            EditorText = string.Empty;
            IsDirty = false;
            return;
        }

        try
        {
            _loaded = ModConfigStore.Load(entry.FullPath);
            EditorText = _loaded.Text;
            IsDirty = false;
            RefreshRunningWarning();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _loaded = null;
            EditorText = string.Empty;
            IsDirty = false;
            EditorError = Text(Strings.Configs_FileReadFailedFormat, ex.Message);
        }
    }

    private bool CanEdit => SelectedEntry is not null && _loaded is not null;

    private bool CanSave => CanEdit && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => SaveInternal(overwriteChangesOnDisk: false);

    private void SaveInternal(bool overwriteChangesOnDisk)
    {
        var entry = SelectedEntry;
        if (entry is null || _loaded is null) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        EditorError = null;

        var result = ModConfigStore.Save(
            installPath,
            entry.FullPath,
            EditorText,
            _loaded,
            DateTimeOffset.Now,
            overwriteChangesOnDisk);

        switch (result.Outcome)
        {
            case ModConfigSaveOutcome.Saved:
                _loaded = result.Saved;
                IsDirty = false;
                RefreshBackups(entry);
                StatusMessage = result.BackupPath is null
                    ? Text(Strings.Configs_SavedFormat, entry.FileName)
                    : Text(
                        Strings.Configs_SavedBackupFormat,
                        entry.FileName,
                        ModConfigStore.BackupDisplayPath);
                break;

            case ModConfigSaveOutcome.Invalid:
                EditorError = result.Error;
                break;

            case ModConfigSaveOutcome.ChangedOnDisk:
                HandleChangedOnDisk(entry);
                break;

            default:
                EditorError = result.Error;
                break;
        }
    }

    //
    // The file changed underneath the editor - the game wrote it, or it was edited elsewhere. Three
    // ways out, and the default is the safe one: nothing has been written yet at this point.
    //
    private void HandleChangedOnDisk(ConfigEntryViewModel entry)
    {
        var answer = MessageBox.Show(
            Text(
                Strings.Configs_ChangedOnDiskFormat,
                entry.FileName,
                ModConfigStore.BackupDisplayPath),
            Strings.Configs_ChangedOnDiskTitle,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        switch (answer)
        {
            case MessageBoxResult.Yes:
                SaveInternal(overwriteChangesOnDisk: true);
                break;

            case MessageBoxResult.No:
                // Copied aside before it is thrown away, so a reload can't lose anything either.
                var installPath = AppServices.SptEnvironment.InstallPath;
                if (!string.IsNullOrWhiteSpace(installPath))
                    ModConfigStore.Backup(installPath, entry.FullPath, DateTimeOffset.Now);

                Load(entry);
                StatusMessage = Text(Strings.Configs_ReloadedFormat, entry.FileName);
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Revert()
    {
        if (_loaded is null) return;

        EditorText = _loaded.Text;
        IsDirty = false;
        EditorError = null;

        // Nothing picked any more: the file is as it was.
        _restoringSelection = true;
        try
        {
            SelectedBackup = null;
        }
        finally
        {
            _restoringSelection = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFolder()
    {
        if (SelectedEntry is null) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{SelectedEntry.FullPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Configs", $"couldn't open the folder for {SelectedEntry.FullPath}: {ex.Message}");
            StatusMessage = Strings.Common_FolderOpenFailed;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyPath()
    {
        if (SelectedEntry is null) return;

        try
        {
            Clipboard.SetText(SelectedEntry.FullPath);
            StatusMessage = Strings.Configs_PathCopied;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The clipboard is held by another process often enough to be worth not crashing over.
            StatusMessage = Strings.Configs_PathCopyFailed;
        }
    }

    //
    // Reuses the same running-process check the install and disable paths use, rather than adding
    // another one. The wording differs from theirs on purpose: there it is a blocker, here it is a
    // note about the edit likely being undone.
    //
    private void RefreshRunningWarning()
    {
        if (ModInstallService.RunningBlockers() is not { Count: > 0 } blockers)
        {
            RunningWarning = null;
            return;
        }

        // One whole sentence per count: "SPT.Server.exe and EscapeFromTarkov.exe is running" never
        // agreed in English either.
        var bepInEx = SelectedEntry?.Entry.Format == ModConfigFormat.BepInExCfg;

        RunningWarning = bepInEx
            ? Strings.Configs_RunningBepInEx(blockers.Count, TextLists.Join(blockers))
            : Strings.Configs_RunningServer(blockers.Count, TextLists.Join(blockers));
    }
}

/// <summary>A kept copy of a config in the earlier-versions picker, with its time as the user's
/// culture writes it.</summary>
public sealed record ConfigBackupRow(ModConfigBackup Backup)
{
    public string Label { get; } = Backup.SavedAt.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
}
