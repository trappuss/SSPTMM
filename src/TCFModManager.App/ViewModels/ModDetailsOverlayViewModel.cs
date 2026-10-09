using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// What the details dialog is being asked to show: the mod, and the version of it currently
// installed. The installed version is what the mod's addons measure their own constraints against,
// so it travels with the request rather than being looked up again inside the dialog.
public sealed record ModDetailsRequest(Mod Mod, string? InstalledVersion)
{
    /// <summary>Opens the page at the mod's Addons tab - it was reached from one of its addons.</summary>
    public bool OpenAddons { get; init; }
}

// Shared, app-lifetime signal for showing mod details. MainWindow subscribes to Requested and displays the details dialog.
public sealed class ModDetailsOverlayViewModel
{
    public event EventHandler<ModDetailsRequest>? Requested;

    public void Show(Mod mod, string? installedVersion = null, bool openAddons = false) =>
        Requested?.Invoke(this, new ModDetailsRequest(mod, installedVersion) { OpenAddons = openAddons });
}
