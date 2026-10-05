using System.Windows.Threading;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM 1.1.0): Subscribed items opens the way it was left - filters, sort, grouping and view
// - without a Save as default button. Written to the same setting the button wrote
// (AppSettings.InstalledDefaults), so a default saved before 1.1.0 is where the page starts.
//
// Not remembered:
// - the search box: a page that opens filtered to something typed weeks ago looks broken;
// - "Updates available" when a notification click chose it, until you pick a status yourself;
// - a category or group the page fell back from because nothing installed is in it any more - the
//   rebuild that drops it is not a choice you made.
//
// Saved half a second after the last change, so a run of changes (Clear filters sets several at once)
// is one write.
//
public partial class InstalledViewModel
{
    private DispatcherTimer? _rememberTimer;

    // Set while the category list is rebuilt from what is installed (see RebuildCategoryOptions).
    private bool _rebuildingFilterLists;

    // The update filter a notification click set, while it is still the one showing.
    private UpdateFilterItem? _updateFilterFromNavigation;

    // Called after every user-visible change of a remembered control.
    private void RememberFilters()
    {
        // Only the page itself, and only once both rebuilt lists have their saved choice - before
        // that, Category and Group show "All" for want of a list, not because anyone chose it.
        if (!_forPage || _rebuildingFilterLists || !_categoryDefaultApplied || !_groupDefaultApplied) return;

        if (_rememberTimer is null)
        {
            _rememberTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _rememberTimer.Tick += (_, _) =>
            {
                _rememberTimer.Stop();
                SaveFilters();
            };
        }

        _rememberTimer.Stop();
        _rememberTimer.Start();
    }

    private void SaveFilters()
    {
        var remembered = new InstalledPageDefaults
        {
            ViewMode = ViewMode.ToString(),
            UpdateStatus = ReferenceEquals(SelectedUpdateFilter, _updateFilterFromNavigation)
                ? _defaults?.UpdateStatus
                : SelectedUpdateFilter.Value.ToString(),
            Enabled = SelectedEnabledFilter.Value.ToString(),
            Category = SelectedCategory.Title,
            Group = SelectedGroupFilter.AllGroups
                ? "all"
                : SelectedGroupFilter.GroupId?.ToString() ?? "ungrouped",
            Sort = SelectedSortOption.Value.ToString(),
            GroupSort = SelectedGroupSortOption.Value.ToString(),
            Grouping = SelectedGrouping.Value.ToString(),
            PageSize = PageSize,
            Attributes = SavedFilterDefaults.CapturedAttributes(AttributeOptions),
        };

        if (Same(remembered, _defaults)) return;

        var service = new SettingsService();
        var settings = service.Load();
        settings.InstalledDefaults = remembered;
        service.Save(settings);
        _defaults = remembered;
        AppLog.Debug("Installed", "remembered the filters, sort and view");
    }

    private static bool Same(InstalledPageDefaults a, InstalledPageDefaults? b) =>
        b is not null
        && a.ViewMode == b.ViewMode && a.UpdateStatus == b.UpdateStatus && a.Enabled == b.Enabled
        && a.Category == b.Category && a.Group == b.Group && a.Sort == b.Sort && a.GroupSort == b.GroupSort
        && a.Grouping == b.Grouping && a.PageSize == b.PageSize
        && (a.Attributes ?? []).SequenceEqual(b.Attributes ?? []);
}
