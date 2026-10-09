using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

//
// A mod's addons: the Addons tab of its item page, and the same list inside the update dialog.
// Served entirely from the cached addon catalog - there are under a hundred addons in total, so
// opening it never waits on a lookup after the first load of the session.
//
public sealed partial class AddonsSectionViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // Fork (round 52): a search box once there are more than a screenful. HUNCH: the number.
    private const int FilterFrom = 7;

    // Every addon of the mod, in the order shown; Addons is what the search leaves of it.
    private readonly List<AddonRowViewModel> _all = [];

    private int _parentModId;
    private string? _parentModName;

    // What the rows were last built from: the parent's installed version and each installed
    // addon's own. Refreshing with the same leaves the rows - and a version picked in one - alone.
    private string? _builtFrom;

    public ObservableCollection<AddonRowViewModel> Addons { get; } = [];

    public int Count => _all.Count;

    public bool HasAddons => _all.Count > 0;

    public string Heading => Text(Strings.Addon_HeadingFormat, _all.Count);

    public bool ShowFilter => _all.Count >= FilterFrom;

    /// <summary>The search box: an addon's name, its author or its one line of description.</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    partial void OnFilterChanged(string value) => ApplyFilter();

    public bool HasNoMatches => _all.Count > 0 && Addons.Count == 0;

    // Shown above the list when the parent isn't installed, so every "Install Anyway" on it has one
    // explanation rather than the same sentence repeated on each row.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParentNotice))]
    private string? _parentNotice;

    public bool HasParentNotice => ParentNotice is not null;

    //
    // Fills the section for one mod. <paramref name="parentInstalledVersion"/> is what every
    // addon version's constraint is measured against; null means the parent isn't installed, which
    // is shown once at the top rather than per row.
    //
    public async Task LoadAsync(int parentModId, string? parentModName, string? parentInstalledVersion)
    {
        await AppServices.Addons.EnsureLoadedAsync();

        _parentModId = parentModId;
        _parentModName = parentModName;
        Build(parentInstalledVersion, force: true);
    }

    //
    // Fork (round 52): the rows again, after something was installed or removed. The page used to
    // keep the rows it opened with: Subscribe to the mod, and its addons went on saying "install
    // the mod first" until the page was closed and opened again; install an addon, and its row
    // still offered Install.
    //
    public void Refresh(string? parentInstalledVersion)
    {
        if (_builtFrom is null) return; // not loaded yet; LoadAsync is on its way

        Build(parentInstalledVersion, force: false);
    }

    private void Build(string? parentInstalledVersion, bool force)
    {
        var records = AppServices.InstallManifest.Load().ModsFor(AppServices.SptEnvironment.InstallPath)
            .Where(r => r.IsAddon)
            .ToList();

        var addons = AppServices.Addons.ForMod(_parentModId).ToList();

        var from = string.Join(
            '|',
            addons.Select(a => a.Id + "=" + records.FirstOrDefault(r => r.ModId == a.Id)?.Version)
                .Prepend(parentInstalledVersion ?? string.Empty));
        if (!force && from == _builtFrom) return;
        _builtFrom = from;

        _all.Clear();
        _all.AddRange(addons
            .Select(a => new AddonRowViewModel(a, _parentModName, parentInstalledVersion, records.FirstOrDefault(r => r.ModId == a.Id)))

            // What is installed first, then what fits the installed mod, each most downloaded first.
            .OrderByDescending(r => r.IsInstalled)
            .ThenByDescending(r => r.FitsParent)
            .ThenByDescending(r => r.Downloads)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase));

        ParentNotice = _all.Count > 0 && string.IsNullOrWhiteSpace(parentInstalledVersion)
            ? Text(
                Strings.Addon_InstallParentNoticeFormat,
                _parentModName ?? Strings.Addon_ThisMod)
            : null;

        ApplyFilter();

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasAddons));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(ShowFilter));
    }

    private void ApplyFilter()
    {
        var words = Filter.Trim();

        Addons.Clear();
        foreach (var row in _all)
        {
            if (words.Length == 0 || row.Matches(words)) Addons.Add(row);
        }

        OnPropertyChanged(nameof(HasNoMatches));
    }
}
