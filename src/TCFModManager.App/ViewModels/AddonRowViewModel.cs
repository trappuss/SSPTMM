using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One selectable version of an addon. Compatibility is measured against the installed PARENT MOD's
// version, not the installed SPT version - that is the whole difference between an addon and a mod.
public sealed class AddonVersionOption
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    public required AddonVersionSummary Raw { get; init; }

    public string VersionText => Raw.Version ?? Strings.Common_Unknown;

    // True when the installed parent version satisfies this version's constraint, false when it
    // doesn't, null when it can't be decided (parent not installed, or an unreadable constraint).
    public bool? IsCompatible { get; init; }

    public bool IsInstalled { get; init; }

    public bool IsLatest { get; init; }

    // What the version picker shows: the number, plus whatever the user needs to know to choose.
    public string Label
    {
        get
        {
            var suffix = (IsInstalled, IsLatest, IsCompatible) switch
            {
                (true, _, _) => Strings.Addon_TagInstalled,
                (_, _, false) => Text(Strings.Addon_TagNeedsFormat, Raw.ModVersionConstraint),
                (_, true, _) => Strings.Addon_TagLatest,
                _ => string.Empty,
            };

            return VersionText + suffix;
        }
    }
}

// 
// One addon in the Addons section of a mod's details dialog: what it is, which of its versions the
// installed parent mod can actually take, and the button that queues it.
// 
public sealed partial class AddonRowViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly Addon _addon;
    private readonly string? _parentName;
    private readonly string? _parentVersion;

    public AddonRowViewModel(
        Addon addon,
        string? parentName,
        string? parentInstalledVersion,
        InstalledModRecord? installRecord)
    {
        _addon = addon;
        _parentName = parentName;
        _parentVersion = parentInstalledVersion;
        InstalledVersion = installRecord?.Version;

        var ordered = (addon.Versions ?? [])
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();

        Versions = ordered
            .Select((v, i) => new AddonVersionOption
            {
                Raw = v,
                IsLatest = i == 0,
                IsInstalled = InstalledVersion is not null
                    && string.Equals(v.Version, InstalledVersion, StringComparison.OrdinalIgnoreCase),
                IsCompatible = ModVersionMatcher.IsSatisfiedBy(v.ModVersionConstraint, parentInstalledVersion),
            })
            .ToList();

        // Newest version the installed parent can take, falling back to the newest overall so the
        // picker is never empty and the reason it can't be installed is visible on the row.
        SelectedVersion = Versions.FirstOrDefault(v => v.IsCompatible != false) ?? Versions.FirstOrDefault();
    }

    public int AddonId => _addon.Id;

    public string Name => _addon.Name ?? Text(Strings.Addon_NameFormat, _addon.Id);

    public string? Teaser => _addon.Teaser;

    public string? Thumbnail => string.IsNullOrWhiteSpace(_addon.Thumbnail) ? null : _addon.Thumbnail;

    public string? Author => _addon.Owner?.Name;

    public string? DetailUrl => _addon.DetailUrl;

    public string DownloadsText => Text(Strings.Addon_DownloadsFormat, _addon.Downloads ?? 0);

    // The content flags as one line, matching how an installed card summarises the same thing.
    public string? FlagsSummary
    {
        get
        {
            var flags = new List<string>();
            if (_addon.ContainsAds == true) flags.Add(Strings.Common_FlagContainsAds);
            if (_addon.ContainsAiContent == true) flags.Add(Strings.Common_FlagContainsAiContent);

            return flags.Count == 0 ? null : string.Join(Strings.Common_FlagSeparator, flags);
        }
    }

    public IReadOnlyList<AddonVersionOption> Versions { get; }

    public string? InstalledVersion { get; }

    public bool IsInstalled => InstalledVersion is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(BlockedReason))]
    [NotifyPropertyChangedFor(nameof(HasBlockedReason))]
    [NotifyPropertyChangedFor(nameof(CompatibilityNote))]
    [NotifyPropertyChangedFor(nameof(HasCompatibilityNote))]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    [NotifyPropertyChangedFor(nameof(ActionIcon))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private AddonVersionOption? _selectedVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => StatusMessage is not null;

    // Install / Update / Redownload, following the same wording the mod path uses. An addon that
    // lives inside its parent's folder has no card on the Installed page, so this row is where its
    // update is offered - the label has to say so rather than reading as a fresh install.
    public string ActionLabel => (IsInstalled, SelectedVersion) switch
    {
        (false, _) => Strings.Addon_ActionInstall,
        (true, { IsInstalled: true }) => Strings.Addon_ActionRedownload,
        _ => Strings.Addon_ActionUpdate,
    };

    // Reads the same state the label does rather than comparing against the label's text, which
    // stops being "Install" the moment the app is in another language.
    public string ActionIcon => IsInstalled ? "ArrowSync24" : "ArrowDownload24";

    // 
    // An addon is only useful next to its parent, so the parent has to be installed and has to
    // satisfy the selected version's constraint. An unreadable or unknowable answer is allowed
    // through with a note rather than blocked - the same way an unparsable SPT constraint is.
    // 
    public bool CanInstall => SelectedVersion is { IsCompatible: not false, Raw.Link: not null }
        && !string.IsNullOrWhiteSpace(_parentVersion);

    public string? BlockedReason
    {
        get
        {
            if (SelectedVersion is null) return Text(Strings.Addon_NoVersionsFormat, Name);

            if (string.IsNullOrWhiteSpace(_parentVersion))
                return Text(
                    Strings.Addon_InstallParentFirstFormat,
                    _parentName ?? Strings.Addon_ParentFallback);

            if (SelectedVersion.IsCompatible == false)
                return Text(
                    Strings.Addon_NeedsParentFormat,
                    _parentName ?? Strings.Addon_ParentFallback,
                    SelectedVersion.Raw.ModVersionConstraint,
                    _parentVersion);

            if (SelectedVersion.Raw.Link is null)
                return ModInstallProblems.NoDownloadLink(Name, SelectedVersion.VersionText);

            return null;
        }
    }

    public bool HasBlockedReason => BlockedReason is not null;

    // Shown when the version can be installed but the fit couldn't be confirmed - an addon whose
    // constraint this app can't parse, against a parent whose version is known.
    public string? CompatibilityNote => CanInstall && SelectedVersion?.IsCompatible is null
        ? Text(
            Strings.Addon_UncheckedFormat,
            _parentName ?? Strings.Addon_ParentFallback,
            _parentVersion)
        : null;

    public bool HasCompatibilityNote => CompatibilityNote is not null;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void Install()
    {
        if (SelectedVersion is not { Raw.Link: not null } selected) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        if (!ReadModPageConfirmationWindow.Confirm(new ModPageLink(Name, DetailUrl) { ModId = _addon.Id, IsAddon = true }))
        {
            StatusMessage = Text(Strings.Addon_CancelledFormat, Name);
            return;
        }

        // Everything the install pipeline needs is already on the cached version, so nothing here
        // waits on a lookup. ModVersionConstraint is deliberately left behind: it decided which
        // version to install, which has just happened, and means nothing downstream of that.
        var version = new ModVersion
        {
            Id = selected.Raw.Id,
            Version = selected.Raw.Version,
            Description = selected.Raw.Description,
            Link = selected.Raw.Link,
            ContentLength = selected.Raw.ContentLength,
        };

        AppServices.DownloadQueue.Enqueue(
            InstallTarget.For(_addon),
            selected.VersionText,
            installPath,
            () => Task.FromResult<ModVersion?>(version),
            totalBytes: selected.Raw.ContentLength);

        StatusMessage = Text(Strings.Addon_QueuedFormat, Name, selected.VersionText);
    }

    [RelayCommand]
    private void OpenAddonPage()
    {
        if (string.IsNullOrWhiteSpace(DetailUrl)) return;

        Process.Start(new ProcessStartInfo(DetailUrl) { UseShellExecute = true });
    }
}
