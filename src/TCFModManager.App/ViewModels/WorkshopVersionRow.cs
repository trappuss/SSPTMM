using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

//
// One published version on the item page's Versions tab - sp-mod.com's version card: the number,
// what SPT it is for, its size and downloads, when it came out, Fika, what it needs, and a way to
// install it.
//
public sealed partial class WorkshopVersionRow : ObservableObject
{
    private readonly WorkshopItemViewModel _page;

    public WorkshopVersionRow(
        WorkshopItemViewModel page, ModVersion source, string? installedSpt, string? installedVersion,
        string? dependencies, string? size)
    {
        _page = page;
        Source = source;
        Dependencies = dependencies;
        Size = size;

        SptRange = SptVersionRangeFormatter.Format(source.SptVersionConstraint) ?? source.SptVersionConstraint;
        VirusTotal = WorkshopLink.From(source.VirusTotalLinks, Strings.Item_VirusTotalOpen);
        IsCompatible = SptVersionMatcher.IsSatisfiedBy(source.SptVersionConstraint, installedSpt);
        Refresh(installedVersion);
    }

    /// <summary>Re-reads which version is installed - after an install, an update or a removal.</summary>
    public void Refresh(string? installedVersion)
    {
        IsInstalled = installedVersion is not null && string.Equals(installedVersion, Source.Version, StringComparison.OrdinalIgnoreCase);
        IsModInstalled = installedVersion is not null;
        CanInstall = !IsInstalled && !string.IsNullOrWhiteSpace(Source.Version);
    }

    public ModVersion Source { get; }

    public string? Version => Source.Version;

    public DateTimeOffset? PublishedAt => Source.PublishedAt;

    public int Downloads => Source.Downloads ?? 0;

    public string? Size { get; }

    public string? SptRange { get; }

    public bool HasSptRange => !string.IsNullOrWhiteSpace(SptRange);

    // True when it runs on this install's SPT, false when it does not, null when that cannot be told.
    public bool? IsCompatible { get; }

    public bool IsFikaCompatible => string.Equals(Source.FikaCompatibility, "compatible", StringComparison.OrdinalIgnoreCase);

    public string? Dependencies { get; }

    public bool HasDependencies => Dependencies is not null;

    // This version's VirusTotal scans, as the site's "VirusTotal Results" button on each version.
    public IReadOnlyList<WorkshopLink> VirusTotal { get; }

    public bool HasVirusTotal => VirusTotal.Count > 0;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallLabel))]
    private bool _isModInstalled;

    [ObservableProperty]
    private bool _canInstall;

    // "Install this version", or, with another version of the mod installed, "Switch to this version".
    public string InstallLabel => IsModInstalled ? Strings.Item_VersionSwitch : Strings.Item_VersionInstall;

    [RelayCommand]
    private Task InstallAsync() => _page.InstallVersionAsync(this);

    // ------------------------------------------------------------------ files and verification

    //
    // sp-mod.com checks each version's download and records the files in it ("Passed
    // Verification" beside the version on the site). Its file-tree endpoint lists them, and only
    // for a version that passed; any other answers "not found". Asked once per row, when the
    // Versions tab is shown.
    //
    private bool _checked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVerified), nameof(VerifiedToolTip), nameof(FilesButtonText), nameof(HasFiles))]
    private FileTree? _fileTree;

    public bool IsVerified => FileTree?.VerifiedAt is not null;

    public string? VerifiedToolTip => FileTree?.VerifiedAt is { } when
        ? LocalizationService.Text(Strings.Item_VersionVerifiedToolTipFormat, SteamDates.Full(when))
        : null;

    public bool HasFiles => FileTree is { Files.Count: > 0 };

    public string FilesButtonText => Strings.Item_VersionFiles(FileTree?.FileCount ?? 0, FileTree?.FileCount ?? 0);

    public IReadOnlyList<string> Files => FileTree?.Files ?? [];

    public bool IsTruncated => FileTree?.Truncated == true;

    [ObservableProperty]
    private bool _areFilesShown;

    [RelayCommand]
    private void ToggleFiles() => AreFilesShown = !AreFilesShown;

    public async Task CheckAsync()
    {
        var versionId = Source.Id;
        if (_checked || versionId == 0 || _page.Mod.Id == 0) return;
        _checked = true;

        try
        {
            FileTree = await AppServices.SpModApi.GetModVersionFileTreeAsync(_page.Mod.Id.ToString(), versionId.ToString());
            OnPropertyChanged(nameof(Files));
            OnPropertyChanged(nameof(IsTruncated));
        }
        catch (SpModApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Not verified (or not public): nothing to show, which is what the row then shows.
        }
        catch (Exception ex)
        {
            _checked = false;
            AppLog.Warn("Workshop", $"file list for version {versionId} failed: {ex.Message}");
        }
    }
}
