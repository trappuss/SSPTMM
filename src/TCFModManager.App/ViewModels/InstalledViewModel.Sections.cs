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
