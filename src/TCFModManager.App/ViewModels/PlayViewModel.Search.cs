using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TCFModManager.App.ViewModels;

//
// Fork (1.4.7): Find the broken mod, offered under the server's log once the server was seen to
// stop before it was up - the moment the search is for. Gone again when a server runs.
//
public partial class PlayViewModel
{
    [ObservableProperty]
    private bool _canFindBrokenMod;

    [RelayCommand]
    private static void FindBrokenMod() => AppServices.BrokenModFinder.OpenAndStart();
}
