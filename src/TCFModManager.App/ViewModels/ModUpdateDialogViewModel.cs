using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

// Backs ModUpdateContentDialog, showing mod details and letting the user pick a published version to install. Fetches the full version history for the installed mod.
public partial class ModUpdateDialogViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly SpModApiClient _spModApi = AppServices.SpModApi;
    private readonly InstalledModCardViewModel _mod;

    // Looked up once in LoadAsync from the cached catalog. Exactly one of these is ever set - an
    // installed card is either a mod or an addon, never both.
    private Mod? _catalogMod;
    private Addon? _catalogAddon;

    public ModUpdateDialogViewModel(InstalledModCardViewModel mod)
    {
        _mod = mod;
    }

    // The addons published for this mod, shown under its version list. An addon has none of its
    // own, and its id would otherwise be read as a mod id - so the section stays empty for one.
    public AddonsSectionViewModel Addons { get; } = new();

    public string ModTitle => _mod.DisplayTitle;

    //
    // What an update would do with this mod's own config files, read-only - the moment it matters is
    // the moment somebody is about to press Update. Only for a mod with a server half: a client mod's
    // settings live in BepInEx\config, which no update touches. Set on the Configs page.
    //
    public string? ConfigPolicyNote
    {
        get
        {
            if (!_mod.HasServer) return null;

            // The same folder names the pin and the planner use, so one mod is one answer everywhere.
            var policy = new ModConfigOptionsStore().PolicyFor(ModListCandidates.From(_mod).Folders);

            return policy switch
            {
                ModConfigPolicy.KeepMine => Strings.ModUpdate_ConfigKeepMine,
                ModConfigPolicy.TakeNew => Strings.ModUpdate_ConfigTakeNew,
                _ => Strings.ModUpdate_ConfigMerge,
            };
        }
    }
    public string? InstalledVersionText => _mod.InstalledVersion;

    // The matched catalog listing's sp-mod.com page; null until LoadAsync resolves it, or if no match was found.
    public string? ModPageUrl => _mod.IsAddon ? _catalogAddon?.DetailUrl : _catalogMod?.DetailUrl;

    //
    // A disabled mod's install record points at folders it no longer occupies, so updating or
    // redownloading it would place files where nothing is loading them and leave the old copy
    // behind in the ".disabled" folder. Both buttons are hidden until it's enabled again.
    //
    public bool IsModDisabled => _mod.IsDisabled;

    public string DisabledNotice => Text(Strings.ModUpdate_DisabledNoticeFormat, _mod.DisplayTitle);

    //
    // Which button is shown is decided by the SELECTED version against the installed one, not by
    // the mod's own UpdateAvailable flag.
    //
    // It used to be the flag, which meant the label described the mod rather than the action: with
    // a newer version published, picking an older one from the list and pressing the button still
    // said "Update" while installing a downgrade. The list has always let you choose any version -
    // only the wording was wrong.
    //
    private bool CanAct => SelectedVersion is not null && !_mod.IsDisabled;

    public bool ShowUpdateButton =>
        CanAct && ModVersionComparer.IsUpdateAvailable(InstalledVersionText, SelectedVersion?.VersionText) == true;

    public bool ShowDowngradeButton =>
        CanAct && ModVersionComparer.IsUpdateAvailable(SelectedVersion?.VersionText, InstalledVersionText) == true;

    //
    // The fallback: the selected version is the one installed, or the two can't be compared. Either
    // way "Redownload" is the honest word - it re-fetches whatever is selected and claims no
    // direction, which is what the app actually knows when a version string won't parse.
    //
    public bool ShowRedownloadButton => CanAct && !ShowUpdateButton && !ShowDowngradeButton;

    // Monitor mode's opposite-way button, beside whichever of the three is showing.
    public bool ShowAlternateButton => CanAct;

    // Whether the "manage installed version" controls should be shown - only meaningful once a
    // catalog mod is known, since confirming/overriding a version needs a mod to record it against.
    public bool CanManageVersion => _mod.IsAddon ? _catalogAddon is not null : _catalogMod is not null;

    // Whether this mod's current InstalledVersion came from a manual override, so "Clear override"
    // has something to undo.
    public bool IsManualOverride => _mod.IsManualOverride;

    // Every version fetched. Versions is the filtered view of this - see RepopulateVersions.
    private readonly List<ModVersionRowViewModel> _allVersions = [];

    public ObservableCollection<ModVersionRowViewModel> Versions { get; } = [];

    //
    // Versions built for an SPT release you don't have are hidden by default: with 4.1 out, a 4.0
    // install looking at a mod's history sees a run of newer versions it cannot use, and the
    // obvious reading is "I'm out of date" rather than "these aren't for me".
    //
    // They are hidden rather than dropped. The count and the reason stay on screen and one click
    // brings them back, because silently shortening the list would leave someone believing the
    // newest version is the newest that exists.
    //
    [ObservableProperty]
    private bool _showIncompatibleVersions;

    public int HiddenVersionCount => _allVersions.Count - Versions.Count;

    public bool ShowVersionFilterNotice => HiddenVersionCount > 0 || ShowIncompatibleVersions;

    public string HiddenVersionsNotice => HiddenVersionCount switch
    {
        0 => Strings.ModUpdate_ShowingAllVersions,
        _ => Strings.ModUpdate_Hidden(HiddenVersionCount),
    };

    partial void OnShowIncompatibleVersionsChanged(bool value) => RepopulateVersions();

    //
    // An addon's rows are judged against its PARENT MOD's version, not against SPT, and the rule
    // this app already settled for addons is to show everything with the reason attached rather
    // than filter any of it out - so the hiding applies to mods only.
    //
    // The installed version is never hidden whatever it targets: you have to be able to see what
    // you are running. An unknown constraint is not proof of anything, so it stays too.
    //
    private bool IsShown(ModVersionRowViewModel row) =>
        ShowIncompatibleVersions || _mod.IsAddon || row.IsInstalled || row.IsCompatible != false;

    private void RepopulateVersions()
    {
        Versions.Clear();
        foreach (var row in _allVersions.Where(IsShown)) Versions.Add(row);

        OnPropertyChanged(nameof(HiddenVersionCount));
        OnPropertyChanged(nameof(ShowVersionFilterNotice));
        OnPropertyChanged(nameof(HiddenVersionsNotice));

        SelectedVersion = Versions.FirstOrDefault(v => v.IsCompatible == true) ?? Versions.FirstOrDefault();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(DowngradeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmSelectedAsInstalledCommand))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton))]
    [NotifyPropertyChangedFor(nameof(ShowDowngradeButton))]
    [NotifyPropertyChangedFor(nameof(ShowRedownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowAlternateButton))]
    private ModVersionRowViewModel? _selectedVersion;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetCustomVersionCommand))]
    private string _customVersionText = string.Empty;

    // Loads the mod's version history. Called once by ModUpdateContentDialog's constructor.
    public async Task LoadAsync()
    {
        // Not a listing (none matched, or installed from a file under a local id): nothing to ask.
        if (_mod.ModId is not { } modId || LocalArchive.IsLocalId(modId))
        {
            StatusMessage = Text(Strings.ModUpdate_NotMatchedFormat, _mod.DisplayTitle);
            IsLoading = false;
            return;
        }

        if (_mod.IsAddon)
        {
            await LoadAddonAsync(modId);
            return;
        }

        _catalogMod = AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == modId);
        OnPropertyChanged(nameof(ModPageUrl));
        OnPropertyChanged(nameof(CanManageVersion));
        ConfirmSelectedAsInstalledCommand.NotifyCanExecuteChanged();
        MarkUpToDateCommand.NotifyCanExecuteChanged();
        SetCustomVersionCommand.NotifyCanExecuteChanged();

        IsLoading = true;
        StatusMessage = null;
        try
        {
            var installedSptVersion = AppServices.SptEnvironment.InstalledVersion;
            var result = await _spModApi.GetModVersionsAsync(
                modId.ToString(),
                new ModVersionsQuery { Sort = "-published_at", PerPage = 20 });

            _allVersions.Clear();
            foreach (var v in result.Data)
            {
                _allVersions.Add(new ModVersionRowViewModel
                {
                    Raw = v,
                    IsInstalled = _mod.InstalledVersion is not null
                        && string.Equals(v.Version, _mod.InstalledVersion, StringComparison.OrdinalIgnoreCase),
                    IsCompatible = SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, installedSptVersion),
                });
            }

            // Versions is fetched newest-first, so index 0 is the latest.
            if (Versions.Count > 0) Versions[0].IsLatest = true;

            // Pre-select the newest compatible version, falling back to the newest overall.
            RepopulateVersions();
            MarkUpToDateCommand.NotifyCanExecuteChanged();

            if (Versions.Count == 0)
                StatusMessage = Text(Strings.ModUpdate_NoVersionsFormat, _mod.DisplayTitle);

            await Addons.LoadAsync(modId, _catalogMod?.Name ?? _mod.DisplayTitle, _mod.InstalledVersion);
        }
        catch (SpModApiRateLimitedException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (SpModApiException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    //
    // An addon's version list comes from the cached addon catalog rather than a per-addon fetch:
    // there are under a hundred addons in total, the cache already carries each one's versions with
    // their download links, and every constraint here is measured against the parent mod's
    // installed version rather than the installed SPT version.
    //
    private async Task LoadAddonAsync(int addonId)
    {
        IsLoading = true;
        StatusMessage = null;
        try
        {
            await AppServices.Addons.EnsureLoadedAsync();
            _catalogAddon = AppServices.Addons.ById(addonId);

            OnPropertyChanged(nameof(ModPageUrl));
            OnPropertyChanged(nameof(CanManageVersion));
            ConfirmSelectedAsInstalledCommand.NotifyCanExecuteChanged();
            SetCustomVersionCommand.NotifyCanExecuteChanged();

            if (_catalogAddon is null)
            {
                StatusMessage = Text(Strings.ModUpdate_AddonUnlistedFormat, _mod.DisplayTitle);
                return;
            }

            var parentVersion = _mod.ParentInstalledVersion;
            var parentName = _mod.ParentModName ?? Strings.ModUpdate_ParentFallback;

            var ordered = (_catalogAddon.Versions ?? [])
                .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
                .ToList();

            _allVersions.Clear();
            foreach (var v in ordered)
            {
                _allVersions.Add(new ModVersionRowViewModel
                {
                    Raw = new ModVersion
                    {
                        Id = v.Id,
                        Version = v.Version,
                        Description = v.Description,
                        Link = v.Link,
                        ContentLength = v.ContentLength,
                        Downloads = v.Downloads,
                        PublishedAt = v.PublishedAt,
                    },
                    IsInstalled = _mod.InstalledVersion is not null
                        && string.Equals(v.Version, _mod.InstalledVersion, StringComparison.OrdinalIgnoreCase),
                    IsCompatible = ModVersionMatcher.IsSatisfiedBy(v.ModVersionConstraint, parentVersion),
                    ParentRequirement = Text(
                        Strings.ModUpdate_ParentRequirementFormat,
                        parentName,
                        v.ModVersionConstraint),
                });
            }

            if (Versions.Count > 0) Versions[0].IsLatest = true;

            RepopulateVersions();
            MarkUpToDateCommand.NotifyCanExecuteChanged();

            if (Versions.Count == 0)
                StatusMessage = Text(Strings.ModUpdate_NoVersionsFormat, _mod.DisplayTitle);
            else if (string.IsNullOrWhiteSpace(parentVersion))
                StatusMessage = _mod.ParentModName is { } known
                    ? Text(Strings.ModUpdate_ParentNotInstalledFormat, known)
                    : Strings.ModUpdate_ParentNotInstalledUnnamed;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanUpdate() => SelectedVersion is not null && !_mod.IsDisabled;

    // Queues the currently selected version for download and install.
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void Update() => EnqueueSelectedVersion(ModUpdateAction.Update, alternate: false);

    // Re-queues the currently selected version (defaulting to whatever's already installed, when
    // there's no newer one) for a fresh download and reinstall. Shown in Update's place once the
    // mod is up to date, e.g. to recover from corrupted or hand-edited files.
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void Redownload() => EnqueueSelectedVersion(ModUpdateAction.Redownload, alternate: false);

    //
    // The small button beside whichever of Update / Downgrade / Redownload is showing: the same
    // action, the opposite way round from Monitor mode's setting, for this one mod.
    //
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void Alternate()
    {
        if (ShowDowngradeButton) Downgrade(alternate: true);
        else EnqueueSelectedVersion(
            ShowUpdateButton ? ModUpdateAction.Update : ModUpdateAction.Redownload, alternate: true);
    }

    //
    // Installs an older version over a newer one. Confirmed first, and separately from the
    // hand-installed warning inside EnqueueSelectedVersion, because the risk is a different one:
    // that warning is about leftover FILES, this is about DATA the newer version has already
    // written and the older one may not understand.
    //
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void Downgrade() => Downgrade(alternate: false);

    private void Downgrade(bool alternate)
    {
        if (SelectedVersion is not { } selected) return;

        if (!Confirm(
                Text(Strings.ModUpdate_DowngradeTitleFormat, _mod.DisplayTitle, selected.VersionText),
                Text(
                    Strings.ModUpdate_DowngradeBodyFormat,
                    InstalledVersionText ?? Strings.ModUpdate_ANewerVersion,
                    selected.VersionText)))
        {
            StatusMessage = Strings.ModUpdate_DowngradeCancelled;
            return;
        }

        EnqueueSelectedVersion(ModUpdateAction.Downgrade, alternate);
    }

    //
    // Which of the three buttons asked. The sentences below name the action in the middle of a
    // paragraph, and the app used to build that word from the button's label by English spelling
    // rules - "Update" to "updating", not "updateing". No other language spells its verbs that way,
    // so each action carries its own whole paragraph instead.
    //
    private enum ModUpdateAction
    {
        Update,
        Redownload,
        Downgrade,
    }

    //
    // True once this dialog has actually changed something - a version queued for download, a
    // version recorded as installed, an override set or cleared. Just looking through the version
    // history leaves it false.
    //
    // The Installed page uses it to decide whether closing the dialog needs a rescan. It used to
    // rescan unconditionally, which re-read the whole install and rebuilt every card just because
    // someone opened a mod to read its changelog.
    //
    public bool MadeChanges { get; private set; }

    private void EnqueueSelectedVersion(ModUpdateAction action, bool alternate)
    {
        if (SelectedVersion is null) return;

        var downloadOnly = AppServices.ModPageGate.DownloadOnlyFor(alternate);

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var target = _mod.IsAddon
            ? _catalogAddon is { } addon ? InstallTarget.For(addon) : null
            : _catalogMod is { } catalogMod ? InstallTarget.For(catalogMod) : null;

        if (target is null)
        {
            StatusMessage = Text(Strings.ModUpdate_NotInCatalogFormat, _mod.DisplayTitle);
            return;
        }

        // The hand-installed warning is about this app placing files over ones it has no record of.
        // A download places nothing, so there is nothing to warn about.
        if (!downloadOnly && !_mod.IsAppManaged && !Confirm(
                Text(
                    action switch
                    {
                        ModUpdateAction.Redownload => Strings.ModUpdate_HandInstalledRedownloadTitleFormat,
                        ModUpdateAction.Downgrade => Strings.ModUpdate_HandInstalledDowngradeTitleFormat,
                        _ => Strings.ModUpdate_HandInstalledUpdateTitleFormat,
                    },
                    _mod.DisplayTitle),
                action switch
                {
                    ModUpdateAction.Redownload => Strings.ModUpdate_HandInstalledRedownloadBody,
                    ModUpdateAction.Downgrade => Strings.ModUpdate_HandInstalledDowngradeBody,
                    _ => Strings.ModUpdate_HandInstalledUpdateBody,
                }))
        {
            return;
        }

        //
        // No mod-page dialog here: this dialog is already showing every version's change notes,
        // which is what the page would be opened for before an update, and a link to the page. (It
        // asked for the page to be opened before - the step the item page's Subscribe no longer
        // asks for either.)
        //

        var selectedVersion = SelectedVersion;
        AppServices.DownloadQueue.Enqueue(
            target, selectedVersion.VersionText, installPath, () => Task.FromResult<ModVersion?>(selectedVersion.Raw),
            totalBytes: selectedVersion.Raw.ContentLength,
            downloadOnly: downloadOnly);
        StatusMessage = Text(Strings.ModUpdate_QueuedFormat, _mod.DisplayTitle, selectedVersion.VersionText);
        MadeChanges = true;
    }

    private static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    // Opens ModPageUrl in the OS's default browser.
    [RelayCommand]
    private void OpenModPage()
    {
        if (string.IsNullOrWhiteSpace(ModPageUrl)) return;

        Process.Start(new ProcessStartInfo(ModPageUrl) { UseShellExecute = true });
    }

    private bool CanManageSelectedVersion() => CanManageVersion && SelectedVersion is not null;

    // Records the version already selected below as what's actually installed, without touching any
    // files - for a mod whose auto-detected version is wrong, or that has none at all.
    [RelayCommand(CanExecute = nameof(CanManageSelectedVersion))]
    private void ConfirmSelectedAsInstalled()
    {
        if (SelectedVersion is not { } selected) return;

        ApplyManualVersion(selected.VersionText, selected.Raw.Id);
        StatusMessage = Text(Strings.ModUpdate_RecordedFormat, _mod.DisplayTitle, selected.VersionText);
    }

    private bool CanMarkUpToDate() => CanManageVersion && Versions.Count > 0;

    // Records the newest published version as installed, regardless of what's selected below - a
    // one-click way to clear a false "update available" without picking the version by hand.
    [RelayCommand(CanExecute = nameof(CanMarkUpToDate))]
    private void MarkUpToDate()
    {
        var latest = Versions.FirstOrDefault(v => v.IsLatest) ?? Versions.FirstOrDefault();
        if (latest is null) return;

        ApplyManualVersion(latest.VersionText, latest.Raw.Id);
        StatusMessage = Text(Strings.ModUpdate_MarkedUpToDateFormat, _mod.DisplayTitle, latest.VersionText);
    }

    private bool CanSetCustomVersion() => CanManageVersion && !string.IsNullOrWhiteSpace(CustomVersionText);

    // Records a free-typed version as installed - for a version that isn't in the cached list above
    // (e.g. a beta or dev build the author never published normally).
    [RelayCommand(CanExecute = nameof(CanSetCustomVersion))]
    private void SetCustomVersion()
    {
        var version = CustomVersionText.Trim();
        ApplyManualVersion(version, versionId: null);
        StatusMessage = Text(Strings.ModUpdate_RecordedFormat, _mod.DisplayTitle, version);
        CustomVersionText = string.Empty;
    }

    private void ApplyManualVersion(string version, int? versionId)
    {
        if (!CanManageVersion) return;
        if (string.IsNullOrWhiteSpace(AppServices.SptEnvironment.InstallPath)) return;

        var folders = new[] { _mod.ClientFolderName, _mod.ServerFolderName }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_mod.IsAddon)
        {
            if (_catalogAddon is not { } addon) return;

            AppServices.InstallManifest.SetManualVersion(
                addon.Id, guid: null, addon.Name ?? _mod.DisplayTitle, version, versionId, folders, isAddon: true);
            MadeChanges = true;
            return;
        }

        if (_catalogMod is not { } catalogMod) return;

        AppServices.InstallManifest.SetManualVersion(
            catalogMod.Id, catalogMod.Guid, catalogMod.Name ?? _mod.DisplayTitle, version, versionId, folders);
        MadeChanges = true;
    }

    private bool CanClearOverride() => _mod.IsManualOverride;

    // Undoes a previous manual override, going back to auto-detecting the version from the files on
    // disk.
    [RelayCommand(CanExecute = nameof(CanClearOverride))]
    private void ClearOverride()
    {
        if (_mod.ModId is not { } modId) return;
        AppServices.InstallManifest.ClearManualVersion(modId, _mod.IsAddon);
        MadeChanges = true;
        StatusMessage = Text(Strings.ModUpdate_ClearedOverrideFormat, _mod.DisplayTitle);
    }
}
