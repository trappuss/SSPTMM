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
        // picker is never empty and what the row says about its fit is about a real version.
        SelectedVersion = Versions.FirstOrDefault(v => v.IsCompatible != false) ?? Versions.FirstOrDefault();
    }

    public int AddonId => _addon.Id;

    public string Name => _addon.Name ?? Text(Strings.Addon_NameFormat, _addon.Id);

    public string? Teaser => _addon.Teaser;

    public string? Thumbnail => string.IsNullOrWhiteSpace(_addon.Thumbnail) ? null : _addon.Thumbnail;

    public string? Author => _addon.Owner?.Name;

    public string? AuthorText => string.IsNullOrWhiteSpace(Author) ? null : Text(Strings.Addon_ByFormat, Author);

    public int Downloads => _addon.Downloads ?? 0;

    /// <summary>Some version fits the installed parent mod, or cannot be told not to.</summary>
    public bool FitsParent => IsParentInstalled && Versions.Any(v => v.IsCompatible != false);

    /// <summary>What the search box over the list looks through.</summary>
    public bool Matches(string words) =>
        Name.Contains(words, StringComparison.CurrentCultureIgnoreCase)
        || (Author?.Contains(words, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (Teaser?.Contains(words, StringComparison.CurrentCultureIgnoreCase) ?? false);

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
    [NotifyPropertyChangedFor(nameof(NeedsAnyway))]
    [NotifyPropertyChangedFor(nameof(BlockedReason))]
    [NotifyPropertyChangedFor(nameof(HasBlockedReason))]
    [NotifyPropertyChangedFor(nameof(FitWarning))]
    [NotifyPropertyChangedFor(nameof(HasFitWarning))]
    [NotifyPropertyChangedFor(nameof(CompatibilityNote))]
    [NotifyPropertyChangedFor(nameof(HasCompatibilityNote))]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    [NotifyPropertyChangedFor(nameof(ActionIcon))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallAlternateCommand))]
    private AddonVersionOption? _selectedVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => StatusMessage is not null;

    // Install / Update / Redownload, following the same wording the mod path uses. An addon that
    // lives inside its parent's folder has no card on the Installed page, so this row is where its
    // update is offered - the label has to say so rather than reading as a fresh install.
    public string ActionLabel => (NeedsAnyway, IsInstalled, SelectedVersion) switch
    {
        // Redownloading what is already there is not a new risk, whatever its constraint says.
        (_, true, { IsInstalled: true }) => Strings.Addon_ActionRedownload,
        (true, _, _) => Strings.Addon_ActionInstallAnyway,
        (_, false, _) => Strings.Addon_ActionInstall,
        _ => Strings.Addon_ActionUpdate,
    };

    // Reads the same state the label does rather than comparing against the label's text, which
    // stops being "Install" the moment the app is in another language.
    public string ActionIcon => IsInstalled ? "ArrowSync24" : "ArrowDownload24";

    private bool IsParentInstalled => !string.IsNullOrWhiteSpace(_parentVersion);

    //
    // Fork (round 52): anything with a download can be installed. The button used to be dead when
    // the selected version's constraint did not take the installed parent, or the parent was not
    // found - but the constraint is the author's word, written once and often left as it was while
    // the parent moved on, and "not found" is this app's reading of the folder. A mod that does
    // not fit the installed SPT is asked about and never refused (SptCompatibility); so is this.
    //
    public bool CanInstall => SelectedVersion is { Raw.Link: not null };

    /// <summary>The selected version is not known to fit: installing it asks first, and the
    /// button says "Install Anyway".</summary>
    public bool NeedsAnyway => CanInstall
        && SelectedVersion is { IsInstalled: false }
        && (!IsParentInstalled || SelectedVersion.IsCompatible == false);

    // What stops the button altogether.
    public string? BlockedReason
    {
        get
        {
            if (SelectedVersion is null) return Text(Strings.Addon_NoVersionsFormat, Name);

            if (SelectedVersion.Raw.Link is null)
                return ModInstallProblems.NoDownloadLink(Name, SelectedVersion.VersionText);

            return null;
        }
    }

    public bool HasBlockedReason => BlockedReason is not null;

    // Why the button says "Install Anyway".
    public string? FitWarning
    {
        get
        {
            if (!NeedsAnyway || SelectedVersion is null) return null;

            // Said once over the whole list (AddonsSectionViewModel.ParentNotice), not on every row.
            if (!IsParentInstalled) return null;

            return Text(
                Strings.Addon_NeedsParentFormat,
                _parentName ?? Strings.Addon_ParentFallback,
                SelectedVersion.Raw.ModVersionConstraint,
                _parentVersion);
        }
    }

    public bool HasFitWarning => FitWarning is not null;

    // Shown when the fit couldn't be confirmed - an addon whose constraint this app can't parse,
    // against a parent whose version is known.
    public string? CompatibilityNote => CanInstall && IsParentInstalled && SelectedVersion?.IsCompatible is null
        ? Text(
            Strings.Addon_UncheckedFormat,
            _parentName ?? Strings.Addon_ParentFallback,
            _parentVersion)
        : null;

    public bool HasCompatibilityNote => CompatibilityNote is not null;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void Install() => Queue(alternate: false);

    // The small button beside Install: the opposite of Monitor mode's setting, for this one addon.
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void InstallAlternate() => Queue(alternate: true);

    private void Queue(bool alternate)
    {
        if (SelectedVersion is not { Raw.Link: not null } selected) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        // A download places nothing, so there is nothing to ask about yet.
        var downloadOnly = AppServices.ModPageGate.DownloadOnlyFor(alternate);
        if (NeedsAnyway && !downloadOnly && !ConfirmAnyway(selected))
        {
            StatusMessage = Text(Strings.Addon_AnywayCancelledFormat, Name);
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
            totalBytes: selected.Raw.ContentLength,
            downloadOnly: downloadOnly);

        StatusMessage = Text(Strings.Addon_QueuedFormat, Name, selected.VersionText);
    }

    // Asked, never refused, and No is the answer Enter gives - as for a mod and SPT.
    private bool ConfirmAnyway(AddonVersionOption selected)
    {
        var parent = _parentName ?? Strings.Addon_ParentFallback;

        var body = IsParentInstalled
            ? Text(
                Strings.Addon_AnywayMismatchFormat,
                Name,
                selected.VersionText,
                parent,
                selected.Raw.ModVersionConstraint,
                _parentVersion)
            : Text(Strings.Addon_AnywayNoParentFormat, Name, parent);

        return SteamMessageBox.Show(
            body,
            Strings.Addon_AnywayTitle,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    }

    [RelayCommand]
    private void OpenAddonPage()
    {
        if (string.IsNullOrWhiteSpace(DetailUrl)) return;

        Process.Start(new ProcessStartInfo(DetailUrl) { UseShellExecute = true });
    }
}
