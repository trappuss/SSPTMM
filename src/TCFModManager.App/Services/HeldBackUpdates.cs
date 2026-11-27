using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// sp-mod.com's own update check (/mods/updates), asked about the whole install at once after each
// scan of Subscribed items - and, until that page has been opened, after each rebuild of the
// Workshop's installed index (BrowseViewModel), so a collection or item page opened first knows
// what is held back too. What it adds to the app's own update check is the one thing that check
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

    /// <summary>True when sp-mod.com holds back exactly this version of this mod - the update the
    /// app would install. A different, newer version being held back leaves this one alone.</summary>
    public bool Holds(int? modId, string? version) =>
        HeldVersion(modId) is { } held && string.Equals(held, version, StringComparison.OrdinalIgnoreCase)
        && !_together.ContainsKey(modId!.Value);

    //
    // Fork (1.4.9), as TCF 71708aa: held-back updates that only work together. Two mods that each
    // need an exact release of the other are both held back by sp-mod.com, so neither was ever
    // offered. One sp-mod.com confirms is fine once the others have theirs is an update again, and
    // is installed with them (UpdateCheckService.HeldBackTogetherAsync).
    //
    private IReadOnlyDictionary<int, IReadOnlyList<int>> _together = new Dictionary<int, IReadOnlyList<int>>();

    private readonly UpdateCheckService _checker = new(AppServices.SpModApi);

    /// <summary>The mods this one's held-back update is installed together with; none for most.</summary>
    public IReadOnlyList<int> Partners(int? modId) =>
        modId is { } id && _together.TryGetValue(id, out var partners) ? partners : [];

    // The SPT version the last answer was for.
    private string? _checkedSpt;

    /// <summary>When the installed version of this mod does not run on this install's SPT (and no
    /// newer version does): one sentence saying so, naming the newest version that does when the
    /// catalog has one - an older one, then - or null.</summary>
    public string? NotForSptNote(int? modId, string? installedVersion)
    {
        if (modId is not { } id || !_incompatible.TryGetValue(id, out var entry) || _checkedSpt is not { } spt) return null;

        // An answer about a version no longer installed (switched since the check) says nothing.
        if (entry.Version is not null && installedVersion is not null
            && !string.Equals(entry.Version, installedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // sp-mod.com's pick when it gives one; otherwise the newest catalog version that runs here.
        var runs = entry.LatestCompatibleVersion;
        if (runs is null && AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id) is { } mod)
        {
            runs = (mod.Versions ?? [])
                .NewestFirst()
                .FirstOrDefault(v => SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, spt) == true)
                ?.Version;
        }

        var version = entry.Version ?? installedVersion ?? "?";

        return runs is not null && !string.Equals(runs, version, StringComparison.OrdinalIgnoreCase)
            ? LocalizationService.Text(Strings.Installed_NotForSptWithFormat, version, spt, runs)
            : LocalizationService.Text(Strings.Installed_NotForSptFormat, version, spt);
    }

    /// <summary>One sentence saying what is held back and why, or null.</summary>
    public string? Note(int? modId) => For(modId) is not { } entry ? null
        : Partners(modId) is { Count: > 0 } partners ? LocalizationService.Text(Strings.Installed_HeldBackTogetherFormat, TextLists.Join(
            [.. partners.Select(id => For(id) is { } p ? string.Join(' ', p.CurrentVersion?.Name ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture), p.LatestVersion?.Version) : null).OfType<string>()]))
        : Describe(entry);

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

    /// <summary>Taken just before asking sp-mod.com, and handed to <see cref="Apply"/> with the
    /// answer: an answer to an older question than the last one asked is dropped (Fork, 1.3.0).</summary>
    public int Begin() => ++_request;

    /// <summary>Takes an answer sp-mod.com already gave - the background update check's (Fork, 1.3.0).
    /// Dropped when a newer question was asked since <paramref name="ticket"/> was taken.</summary>
    public bool Apply(int ticket, IEnumerable<ModBlockedUpdateEntry> blockedUpdates, IEnumerable<ModIncompatibleEntry> incompatibleWithSpt, string sptVersion,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? together = null)
    {
        if (ticket != _request) return false;

        // Both worked out before either is replaced, so a surprise in one leaves both as they were.
        var blocked = blockedUpdates
            .Where(b => b.CurrentVersion is not null)
            .GroupBy(b => b.CurrentVersion!.ModId)
            .ToDictionary(g => g.Key, g => g.First());

        var incompatible = incompatibleWithSpt
            .GroupBy(i => i.ModId)
            .ToDictionary(g => g.Key, g => g.First());

        _blocked = blocked;
        _together = together ?? new Dictionary<int, IReadOnlyList<int>>();
        _incompatible = incompatible;
        _checkedSpt = sptVersion;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The same, after asking sp-mod.com which of the held-back updates go together (1.4.9).</summary>
    public async Task<bool> ApplyAsync(int ticket, IReadOnlyList<(int ModId, string Version)> installed,
        IReadOnlyList<ModBlockedUpdateEntry> blockedUpdates, IEnumerable<ModIncompatibleEntry> incompatibleWithSpt, string sptVersion,
        CancellationToken ct = default)
    {
        if (ticket != _request) return false;

        var together = await _checker.HeldBackTogetherAsync(installed, blockedUpdates, sptVersion, ct);
        return Apply(ticket, blockedUpdates, incompatibleWithSpt, sptVersion, together);
    }

    /// <summary>Asks sp-mod.com about the mods of a scan of the install (addons left out).</summary>
    public Task RefreshAsync(IEnumerable<InstalledModCardViewModel> cards, string? sptVersion) =>
        RefreshAsync(cards
            .Where(c => c is { IsAddon: false, ModId: > 0 } && !string.IsNullOrWhiteSpace(c.InstalledVersion))
            .Select(c => (c.ModId!.Value, c.InstalledVersion!))
            .DistinctBy(c => c.Item1)
            .ToList(), sptVersion);

    /// <summary>Asks sp-mod.com about these installed mods (id and installed version).</summary>
    public async Task RefreshAsync(IReadOnlyList<(int ModId, string Version)> installed, string? sptVersion)
    {
        if (installed.Count == 0 || string.IsNullOrWhiteSpace(sptVersion)) return;

        var request = Begin();
        try
        {
            var mods = string.Join(",", installed.Select(m => $"{m.ModId}:{m.Version}"));
            var result = await AppServices.SpModApi.GetModUpdatesAsync(mods, sptVersion);
            if (!await ApplyAsync(request, installed, result.BlockedUpdates ?? [], result.IncompatibleWithSpt ?? [], sptVersion)) return;
            AppLog.Info("Updates", $"sp-mod.com update check: {result.Updates?.Count ?? 0} updates, {_blocked.Count} held back, {_incompatible.Count} not for SPT {sptVersion}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Updates", $"sp-mod.com update check failed: {ex.Message}");
        }
    }
}
