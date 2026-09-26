using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One dependency in a resolved tree, with its status against the current install and
// whatever's needed to queue it. Rendered as an indented row inside its mod's expander.
public sealed partial class DependencyRow : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    public required string Name { get; init; }

    // Nesting level within its tree; 0 is a direct dependency of the mod.
    public int Depth { get; init; }

    public Thickness Indent => new(Depth * 24, 0, 0, 0);

    public required ModStatus Status { get; init; }

    // The version on disk, when there is one.
    public string? InstalledVersion { get; init; }

    // The newest version that satisfies both this dependency and the installed SPT version.
    // Null when the API couldn't resolve one.
    public string? RequiredVersion { get; init; }

    // The catalog listing, when the dependency matched one. Needed to queue an install.
    public Mod? CatalogMod { get; init; }

    // Set once this row has been queued, so the button doesn't invite a second click.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotQueued))]
    [NotifyPropertyChangedFor(nameof(InstallToolTip))]
    private bool _isQueued;

    public bool IsNotQueued => !IsQueued;

    public string Glyph => ModStatusDisplay.Glyph(Status);

    public string StatusText => Status switch
    {
        ModStatus.Installed => InstalledVersion is null
            ? Strings.Dependencies_RowInstalled
            : Text(Strings.Dependencies_RowInstalledVersionFormat, InstalledVersion),
        ModStatus.UpdateAvailable => Text(
            Strings.Dependencies_RowNeedsUpdateFormat,
            RequiredVersion,
            InstalledVersion ?? Strings.Common_Unknown),
        ModStatus.NotInstalled => RequiredVersion is null
            ? Strings.Dependencies_RowNotInstalled
            : Text(Strings.Dependencies_RowNotInstalledNeedsFormat, RequiredVersion),
        ModStatus.NoCompatibleVersion => Strings.Dependencies_RowNoCompatible,
        ModStatus.Disabled => InstalledVersion is null
            ? Strings.Dependencies_RowDisabled
            : Text(Strings.Dependencies_RowDisabledVersionFormat, InstalledVersion),
        ModStatus.TooNew => Text(Strings.Dependencies_RowTooNewFormat, InstalledVersion, RequiredVersion),
        _ => Strings.Dependencies_RowConflict,
    };

    // Whether this row can be queued: something is actually missing or outdated, and there's
    // a resolved version and catalog listing to install.
    public bool CanInstall =>
        Status is ModStatus.NotInstalled or ModStatus.UpdateAvailable
        && CatalogMod is not null
        && !string.IsNullOrWhiteSpace(RequiredVersion);

    public string InstallButtonText => Status == ModStatus.UpdateAvailable
        ? Strings.Dependencies_RowUpdate
        : Strings.Dependencies_RowInstall;

    //
    // The button's tooltip. Its most useful job is explaining the DISABLED state: once a row is
    // queued the button greys out, and a greyed button with no explanation reads as broken rather
    // than as "already done". The page sets ToolTipService.ShowOnDisabled so this is actually
    // reachable then.
    //
    // Otherwise it reuses the shared mod-page gate wording, so this button says the same thing as
    // the install buttons on Browse and in the update dialog - including the warning when the gate
    // has been switched off. Read at bind time rather than bound live to the gate view model: these
    // rows are rebuilt on every refresh of the page, which is the only way to reach them after
    // changing that setting.
    //
    public string InstallToolTip => IsQueued
        ? Text(Strings.Dependencies_AlreadyQueuedFormat, Name)
        : Status == ModStatus.UpdateAvailable
            ? AppServices.ModPageGate.UpdateToolTip
            : AppServices.ModPageGate.InstallToolTip;
}
