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

    //
    // Download only: the mod's file saved to the download folder and not installed, whatever Monitor
    // mode is set to - the item page's button and the right-click menu. The same queue, gate and
    // checks as Subscribe; a mod already subscribed to is saved again at the version on its card.
    // pageSeen: asked from the mod's own page, so its "read the page first" gate is already met.
    //
    public Task DownloadOnlyAsync(Core.Models.Mod mod, bool pageSeen = false)
    {
        var card = BuildCard(mod);
        var alternate = !AppServices.ModPageGate.IsDownloadOnly; // DownloadOnlyFor(alternate) is then true

        return QueueForDownloadAsync(
            card, card.IsInstalled ? DownloadAction.Redownload : DownloadAction.Install, pinned: null, pageSeen, alternate);
    }
}
