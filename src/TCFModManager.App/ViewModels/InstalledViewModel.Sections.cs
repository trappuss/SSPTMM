using System.Collections.ObjectModel;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM): Subscribed items' own orderings - pinned mods at the top whatever the sort, and the
// Cards/List grouping by install state (an Enabled section, then a Disabled one).
//
public partial class InstalledViewModel
{
    // The Enabled / Disabled sections folded this session, by StateKey - kept across rebuilds.
    private readonly HashSet<string> _collapsedStates = new(StringComparer.Ordinal);

    // Pinned first; within pinned and within the rest, the order given. OrderBy is stable.
    private static List<InstalledModCardViewModel> PinnedFirst(List<InstalledModCardViewModel> mods) =>
        mods.Any(m => m.IsPinned) ? [.. mods.OrderBy(m => !m.IsPinned)] : mods;

    //
    // Fork (1.4.6): each addon card that has its mod's card in the list moves to just after it, the
    // addons of one mod in the order they were in. One whose mod isn't shown (filtered out, or not
    // installed) stays where the sort put it. Pinning an addon doesn't lift it away from its mod.
    //
    private static List<InstalledModCardViewModel> AddonsUnderParents(List<InstalledModCardViewModel> mods)
    {
        var parents = mods.Where(m => !m.IsAddon && m.ModId is not null).Select(m => m.ModId!.Value).ToHashSet();
        var tucked = mods.Where(m => m.IsAddon && m.ParentModId is { } p && parents.Contains(p)).ToHashSet();
        if (tucked.Count == 0) return mods;

        var under = tucked.ToLookup(m => m.ParentModId!.Value);
        var result = new List<InstalledModCardViewModel>(mods.Count);
        var placed = new HashSet<int>();
        foreach (var mod in mods)
        {
            if (tucked.Contains(mod)) continue;
            result.Add(mod);
            if (!mod.IsAddon && mod.ModId is { } id && placed.Add(id)) result.AddRange(under[id]);
        }

        return result;
    }

    private static string StateSectionKey(string state) => "s:" + state;

    // Enabled, then Disabled - each in the page's sort order, and only when it has mods in it.
    private void BuildStateSections(ObservableCollection<ModGroupSectionViewModel> sections)
    {
        var existing = new Dictionary<string, ModGroupSectionViewModel>(StringComparer.Ordinal);
        foreach (var section in sections) existing.TryAdd(SectionKey(section), section);

        var wanted = new List<(ModGroupSectionViewModel Section, List<InstalledModCardViewModel> Items)>();
        foreach (var enabled in new[] { true, false })
        {
            var items = _filtered.Where(m => m.IsDisabled != enabled).ToList();
            if (items.Count == 0) continue;

            var key = enabled ? "enabled" : "disabled";
            var collapsed = _collapsedStates.Contains(key);
            var section = existing.TryGetValue(StateSectionKey(key), out var found)
                ? found
                : ModGroupSectionViewModel.ForState(enabled, collapsed);
            section.IsCollapsed = collapsed;
            wanted.Add((section, items));
        }

        ItemsSync.Apply(sections, [.. wanted.Select(w => w.Section)]);
        foreach (var (section, items) in wanted)
        {
            ItemsSync.Apply(section.Items, items);
            section.RefreshCounts();
        }
    }

    private void RememberStateFold(ModGroupSectionViewModel section)
    {
        if (section.IsCollapsed) _collapsedStates.Add(section.StateKey!);
        else _collapsedStates.Remove(section.StateKey!);
    }
}
