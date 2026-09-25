namespace TCFModManager.App.ViewModels;

// Shared, app-lifetime signal for showing the update dialog for an installed mod, awaitable so callers know when it closes.
public sealed class ModUpdateOverlayViewModel
{
    // Set by MainWindow. Constructs and shows the update dialog for the given card, returning once
    // it's closed - true when the dialog changed something the Installed page would need to rescan
    // for, false when it was only read.
    public Func<InstalledModCardViewModel, Task<bool>>? ShowAsync { get; set; }

    /// <summary>True while the dialog is open. The item page opens under it (it belongs to the page
    /// area, and the dialog is over everything), so a mod link clicked inside the dialog's
    /// changelog goes to the browser instead - see MarkupActions.OpenLink.</summary>
    public bool IsOpen { get; set; }
}
