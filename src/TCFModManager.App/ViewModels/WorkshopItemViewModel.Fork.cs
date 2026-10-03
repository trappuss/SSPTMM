using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM): Download only on the item page - a labelled button beside Subscribe, for a mod
// whether or not it is subscribed to, that saves its file to the download folder and installs
// nothing (BrowseViewModel.DownloadOnlyAsync). Replaces 1.19's unlabelled icon there, which stays
// only in Monitor mode's download-only setting, where it does the opposite: installs.
//
public sealed partial class WorkshopItemViewModel
{
    /// <summary>The labelled Download only button: not while this mod is in the queue, and not in
    /// Monitor mode's download-only setting, where Subscribe already only downloads.</summary>
    public bool ShowDownloadOnly => !IsInQueue && !AppServices.ModPageGate.IsDownloadOnly;

    [RelayCommand]
    private async Task DownloadOnlyAsync()
    {
        if (IsInQueue) return;

        JustSubscribed = false;
        await AppServices.Browse.DownloadOnlyAsync(Mod, pageSeen: true);
        Message = AppServices.Browse.StatusMessage;
    }
}
