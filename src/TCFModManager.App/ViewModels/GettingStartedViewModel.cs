using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM, UI tidy-up 4): the "Getting started" list at the top of Workshop Home - the three
// things between installing this app and playing SPT with mods, for whoever opens it the first time.
// Each step is worked out from the install itself, not from what was clicked, so a step done some
// other way still counts:
//
//  1. Set your SPT folder - an SPT install is found in the folder Options names.
//  2. Subscribe to a mod  - this app has installed at least one mod into that install.
//  3. Start SPT           - the install has a profile, which SPT only makes once its server has run
//                           and the launcher has been used.
//
// Read again whenever Workshop Home is shown. Once all three are done the list goes for good, and
// Hide takes it away for good sooner - an existing user, with all three done long ago, never sees it.
//
public sealed partial class GettingStartedViewModel : LocalizedViewModel
{
    [ObservableProperty]
    private bool _isShown;

    [ObservableProperty]
    private bool _folderDone;

    [ObservableProperty]
    private bool _subscribedDone;

    [ObservableProperty]
    private bool _playedDone;

    public void Refresh()
    {
        var service = new SettingsService();
        var settings = service.Load();
        if (settings.GettingStartedDone)
        {
            IsShown = false;
            return;
        }

        var installPath = AppServices.SptEnvironment.InstallPath;
        FolderDone = !string.IsNullOrWhiteSpace(AppServices.SptEnvironment.InstalledVersion);
        SubscribedDone = FolderDone && HasInstalledAMod(installPath);
        PlayedDone = FolderDone && HasAProfile(installPath);

        if (FolderDone && SubscribedDone && PlayedDone)
        {
            settings.GettingStartedDone = true;
            service.Save(settings);
            AppLog.Info("GettingStarted", "all three steps done - the list is gone for good");
            IsShown = false;
            return;
        }

        IsShown = true;
    }

    private static bool HasInstalledAMod(string? installPath)
    {
        try
        {
            return AppServices.InstallManifest.Load().ModsFor(installPath).Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasAProfile(string? installPath)
    {
        try
        {
            return ProfileBackups.ProfilesFolder(installPath) is { } folder
                && Directory.EnumerateFiles(folder, "*.json").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [RelayCommand]
    private static void OpenOptions() => AppNavigation.Navigate(typeof(OptionsPage));

    [RelayCommand]
    private static void Browse() => AppNavigation.Navigate(typeof(BrowsePage));

    [RelayCommand]
    private static void Play() => AppNavigation.Navigate(typeof(PlayPage));

    [RelayCommand]
    private void Hide()
    {
        var service = new SettingsService();
        var settings = service.Load();
        settings.GettingStartedDone = true;
        service.Save(settings);
        AppLog.Info("GettingStarted", "hidden by hand");
        IsShown = false;
    }
}
