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
