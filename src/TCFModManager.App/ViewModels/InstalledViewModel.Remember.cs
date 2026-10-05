using System.IO;
using System.Windows;
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
// - a category the page fell back from because nothing installed is in it (or, offline, no catalog
//   says so) - the remembered one stays until a category is picked.
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

    // The remembered category isn't installed, so the page shows All in its place.
    private bool _categoryFellBack;

    // A change made before the first scan had rebuilt the lists - saved once it has.
    private bool _rememberPending;

    // Called after every user-visible change of a remembered control.
    private void RememberFilters()
    {
        if (!_forPage || _rebuildingFilterLists) return;

        // Before both rebuilt lists have their saved choice, Category and Group show "All" for want of
        // a list, not because anyone chose it - so the save waits for them.
        if (!_categoryDefaultApplied || !_groupDefaultApplied)
        {
            _rememberPending = true;
            return;
        }

        if (_rememberTimer is null)
        {
            _rememberTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _rememberTimer.Tick += (_, _) =>
            {
                _rememberTimer.Stop();
                SaveFilters();
            };

            // A change in the last half second before the app closes is still kept.
            if (Application.Current is { } app)
            {
                app.Exit += (_, _) =>
                {
                    if (!_rememberTimer.IsEnabled) return;
                    _rememberTimer.Stop();
                    SaveFilters();
                };
            }
        }

        _rememberTimer.Stop();
        _rememberTimer.Start();
    }

    // From the two list rebuilds: a change that had to wait for them is saved now.
    private void RememberIfPending()
    {
        if (!_rememberPending || !_categoryDefaultApplied || !_groupDefaultApplied) return;
        _rememberPending = false;
        RememberFilters();
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
            Category = _categoryFellBack ? _defaults?.Category : SelectedCategory.Title,
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

        try
        {
            var service = new SettingsService();
            var settings = service.Load();
            settings.InstalledDefaults = remembered;
            service.Save(settings);
            _defaults = remembered;
            AppLog.Debug("Installed", "remembered the filters, sort and view");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a convenience: the page works as it is, and the next change tries again.
            AppLog.Warn("Installed", $"couldn't remember the filters: {ex.Message}");
        }
    }

    private static bool Same(InstalledPageDefaults a, InstalledPageDefaults? b) =>
        b is not null
        && a.ViewMode == b.ViewMode && a.UpdateStatus == b.UpdateStatus && a.Enabled == b.Enabled
        && a.Category == b.Category && a.Group == b.Group && a.Sort == b.Sort && a.GroupSort == b.GroupSort
        && a.Grouping == b.Grouping && a.PageSize == b.PageSize
        && (a.Attributes ?? []).SequenceEqual(b.Attributes ?? []);
}
