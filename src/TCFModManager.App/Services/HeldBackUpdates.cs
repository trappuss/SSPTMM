using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// sp-mod.com's own update check (/mods/updates), asked about the whole install at once after each
// scan of Subscribed items. What it adds to the app's own update check is the one thing that check
// cannot see: an update that would break another installed mod. CommonLib 3.0.6, say, while Black
// and Blue 1.0.0 needs CommonLib ~2.0.24. sp-mod.com lists those as blocked updates; here they are
// "held back", said on the mod's card and item page, and left out of Update all.
//
// A failed or offline check leaves the last answer in place; the app's own check still stands.
//
public sealed class HeldBackUpdates
{
    private Dictionary<int, ModBlockedUpdateEntry> _blocked = [];

    // Installed versions that do not run on this install's SPT, with no newer version that does.
    private Dictionary<int, ModIncompatibleEntry> _incompatible = [];

    // Bumped per check, so an older check answering after a newer one is dropped.
    private int _request;

    /// <summary>Raised after every check that got an answer.</summary>
    public event EventHandler? Changed;

    /// <summary>The held-back update for this mod, if sp-mod.com holds one back.</summary>
    public ModBlockedUpdateEntry? For(int? modId) =>
        modId is { } id && _blocked.TryGetValue(id, out var entry) ? entry : null;

    /// <summary>The version sp-mod.com holds back for this mod, if any.</summary>
    public string? HeldVersion(int? modId) => For(modId)?.LatestVersion?.Version;

    // The SPT version the last answer was for.
    private string? _checkedSpt;

    /// <summary>When the installed version of this mod does not run on this install's SPT (and no
    /// newer version does): one sentence saying so, naming the newest version that does when the
    /// catalog has one - an older one, then - or null.</summary>
    public string? NotForSptNote(int? modId, string? installedVersion)
    {
        if (modId is not { } id || !_incompatible.TryGetValue(id, out var entry) || _checkedSpt is not { } spt) return null;

        // sp-mod.com's pick when it gives one; otherwise the newest catalog version that runs here.
        var runs = entry.LatestCompatibleVersion;
        if (runs is null && AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id) is { } mod)
        {
            runs = (mod.Versions ?? [])
                .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault(v => SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, spt) == true)
                ?.Version;
        }

        var version = entry.Version ?? installedVersion ?? "?";

        return runs is not null && !string.Equals(runs, version, StringComparison.OrdinalIgnoreCase)
            ? LocalizationService.Text(Strings.Installed_NotForSptWithFormat, version, spt, runs)
            : LocalizationService.Text(Strings.Installed_NotForSptFormat, version, spt);
    }

    /// <summary>One sentence saying what is held back and why, or null.</summary>
    public string? Note(int? modId) => For(modId) is { } entry ? Describe(entry) : null;

    public static string Describe(ModBlockedUpdateEntry entry)
    {
        var version = entry.LatestVersion?.Version ?? "?";

        var blockers = entry.BlockingMods
            .Where(b => !string.IsNullOrWhiteSpace(b.ModName))
            .Select(b => string.IsNullOrWhiteSpace(b.Constraint)
                ? b.ModName!
                : LocalizationService.Text(Strings.Installed_HeldBackNeedsFormat, b.ModName, b.Constraint))
            .ToList();

        if (blockers.Count > 0)
            return LocalizationService.Text(Strings.Installed_HeldBackFormat, version, TextLists.Join(blockers));

        return entry.BlockReason == "chain_dependency_conflict"
            ? LocalizationService.Text(Strings.Installed_HeldBackChainFormat, version)
            : LocalizationService.Text(Strings.Installed_HeldBackPlainFormat, version);
    }

    /// <summary>Asks sp-mod.com about these installed mods (id and installed version).</summary>
    public async Task RefreshAsync(IReadOnlyCollection<(int ModId, string Version)> installed, string? sptVersion)
    {
        if (installed.Count == 0 || string.IsNullOrWhiteSpace(sptVersion)) return;

        var request = ++_request;
        try
        {
            var mods = string.Join(",", installed.Select(m => $"{m.ModId}:{m.Version}"));
            var result = await AppServices.SpModApi.GetModUpdatesAsync(mods, sptVersion);
            if (request != _request) return;

            _blocked = result.BlockedUpdates
                .Where(b => b.CurrentVersion is not null)
                .GroupBy(b => b.CurrentVersion!.ModId)
                .ToDictionary(g => g.Key, g => g.First());

            _incompatible = result.IncompatibleWithSpt
                .GroupBy(i => i.ModId)
                .ToDictionary(g => g.Key, g => g.First());
            _checkedSpt = sptVersion;

            AppLog.Info("Updates", $"sp-mod.com update check: {result.Updates.Count} updates, {_blocked.Count} held back, {_incompatible.Count} not for SPT {sptVersion}");
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Updates", $"sp-mod.com update check failed: {ex.Message}");
        }
    }
}
