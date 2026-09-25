using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

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
}
