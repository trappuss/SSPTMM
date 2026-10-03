using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Views;

namespace TCFModManager.App.ViewModels;

// Fork (SSPTMM, UI tidy-up 5): a collection of yours, shared with friends - see ShareCollectionDialog.
public sealed partial class WorkshopCollectionViewModel
{
    // The breadcrumb's "Your collections": the grid of them.
    [RelayCommand]
    private static void OpenYourCollections() => AppNavigation.Navigate(typeof(YourCollectionsPage));

    [RelayCommand]
    private void ShareWithFriends()
    {
        if (List is { } list) ShareCollectionDialog.Show(list.Id);
    }
}
