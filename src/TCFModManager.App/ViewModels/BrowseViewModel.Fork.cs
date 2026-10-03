namespace TCFModManager.App.ViewModels;

// Fork (SSPTMM): what other pages need from Browse's index of what is installed.
public partial class BrowseViewModel
{
    //
    // Builds the installed index if nothing has yet - Your collections asks before Browse has ever
    // been opened, when every collection would otherwise read "0 installed". Once built it is kept
    // current by Browse's own refreshes (after installs, removals and moves).
    //
    public Task EnsureInstalledIndexAsync() =>
        _installedByGuid.Count == 0 && _installedByName.Count == 0 ? RefreshInstalledIndexAsync() : Task.CompletedTask;
}
