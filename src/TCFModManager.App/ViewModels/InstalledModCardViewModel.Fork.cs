using CommunityToolkit.Mvvm.ComponentModel;

namespace TCFModManager.App.ViewModels;

// Fork (SSPTMM): what the Subscribed items page works out for a card and draws on it.
public sealed partial class InstalledModCardViewModel
{
    // An update this page can apply right now - the card shows Steam's blue Update for it. Set by
    // InstalledViewModel (MarkUpdatableCards) from the same test Update all uses.
    [ObservableProperty]
    private bool _canUpdateHere;
}

public sealed partial class InstalledModCardViewModel
{
    // Fork: the installed version uploaded again under the same number - set after each scan by
    // InstalledViewModel (ApplyReuploads).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReupload), nameof(ReuploadNote))]
    private TCFModManager.Core.Services.Reupload? _reupload;

    public bool HasReupload => Reupload is not null;

    public string? ReuploadNote => Reupload is not { } found
        ? null
        : Text(
            found.Kind == TCFModManager.Core.Services.ReuploadKind.NewEntry
                ? Localization.Strings.Installed_ReuploadNewEntryFormat
                : Localization.Strings.Installed_ReuploadListingChangedFormat,
            DisplayTitle,
            found.Version.Version,
            found.Now.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            (found.Before ?? 0).ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
}
