using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

//
// One mod the picker offers, and whether it has been ticked.
//
// IsChosen lives on the row rather than on the ListBox because the visible collection is replaced
// whenever the source or the search changes: a tick has to outlive the container that drew it.
//
public sealed partial class ModListAddRow : LocalizedViewModel
{
    [ObservableProperty]
    private bool _isChosen;

    public required ModListAddOption Option { get; init; }

    public required string Detail { get; init; }

    // False for a mod the list already names - shown, so it is clear it is there, but not tickable.
    public required bool CanChoose { get; init; }

    public ModListEntry Entry => Option.Entry;

    public string Name => Option.Entry.Name;

    public bool Matches(string query) =>
        Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (Option.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
}

//
// Picks mods to add to a list, from what this install has and from the cached sp-mod.com catalog.
//
// Nothing here downloads, installs or moves anything: it names mods, and applying the list is what
// acts on them. That is why a mod nobody here has can be added at all - the point of a list is
// often to describe an install someone else should end up with.
//
public partial class ModListAddModWindow : SteamModalWindow
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    //
    // The catalog runs to thousands of listings and nobody scrolls that. Showing the first slice
    // and saying so is honest about it; typing is how you reach the rest.
    //
    private const int MaxRows = 200;

    private readonly List<ModListAddRow> _installed;
    private readonly List<ModListAddRow> _catalog;

    private bool _showingCatalog;

    // Set while selection is being restored after the visible collection changed, so the handler
    // that records a tick doesn't treat that restore as the user ticking things.
    private bool _syncing;

    private ModListAddModWindow(string listName, IReadOnlyList<ModListEntry> current, ModListAddOptions options)
    {
        _installed = [.. options.Installed.Select(o => Row(o, current, fromCatalog: false))];
        _catalog = [.. options.Catalog.Select(o => Row(o, current, fromCatalog: true))];

        InitializeComponent();

        Title = Text(Strings.ModLists_AddTitleFormat, listName);
        InstalledSourceButton.Content = Text(
            Strings.ModLists_AddSourceInstalledFormat, _installed.Count);
        CatalogSourceButton.Content = Text(
            Strings.ModLists_AddSourceCatalogFormat, _catalog.Count);

        // The catalog is the only source worth opening on when there was no install to read.
        _showingCatalog = _installed.Count == 0;
        ShowSource();

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
    }

    //
    // The mods the user picked, or nothing when they cancelled.
    //
    // Deduped on the way out, and the installed row wins where a mod appears in both sources: it
    // carries the version that is actually here, where the catalog row is deliberately unpinned.
    //
    // <param name="current">What the list holds right now, unsaved edits included - those rows are
    // shown as already on it rather than offered again.</param>
    public static IReadOnlyList<ModListEntry> Pick(
        string listName, IReadOnlyList<ModListEntry> current, ModListAddOptions options)
    {
        var window = new ModListAddModWindow(listName, current, options);
        if (window.ShowDialog() != true) return [];

        var chosen = new List<ModListEntry>();

        foreach (var entry in window._installed.Concat(window._catalog).Where(r => r.IsChosen).Select(r => r.Entry))
        {
            if (!ModListEntries.Contains(chosen, entry)) chosen.Add(entry);
        }

        return chosen;
    }

    private static ModListAddRow Row(ModListAddOption option, IReadOnlyList<ModListEntry> current, bool fromCatalog)
    {
        var alreadyOn = ModListEntries.Contains(current, option.Entry);

        return new ModListAddRow
        {
            Option = option,
            CanChoose = !alreadyOn,
            Detail = Detail(option, alreadyOn, fromCatalog),
        };
    }

    private static string Detail(ModListAddOption option, bool alreadyOn, bool fromCatalog)
    {
        var parts = new List<string>();

        if (alreadyOn) parts.Add(Strings.ModLists_AddFactAlreadyOn);
        if (option.Entry.IsAddon) parts.Add(Strings.ModLists_AddFactAddon);

        if (fromCatalog)
        {
            if (!string.IsNullOrWhiteSpace(option.Author))
                parts.Add(Text(Strings.ModLists_AddFactByFormat, option.Author));

            // Said plainly rather than left to be inferred from the version being absent: a list
            // entry with no version means "the newest published", and that is a real choice.
            parts.Add(option.IsInstalled
                ? Strings.ModLists_AddFactInstalledNewest
                : Strings.ModLists_AddFactNewest);
        }
        else
        {
            // The same "installed as" the contents panel shows, left out on the same rule: the
            // title above is the sp-mod.com listing name wherever one matched, and where nothing
            // matched it already is the folder, so printing it again would only repeat it.
            var folders = string.Join(Strings.Common_ListSeparator, option.Entry.Folders);

            if (folders.Length > 0 && !string.Equals(folders, option.Entry.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                parts.Add(Text(Strings.ModLists_AddFactInstalledAsFormat, folders));

            parts.Add(option.Entry.Version is { } version
                ? Text(Strings.ModLists_AddFactVersionFormat, version)
                : Strings.ModLists_AddFactVersionUnknown);
            if (option.IsDisabled) parts.Add(Strings.Footprint_DetailDisabled);
        }

        return string.Join(Strings.Common_FactSeparator, parts);
    }

    private void InstalledSource_Click(object sender, RoutedEventArgs e)
    {
        if (!_showingCatalog) return;

        _showingCatalog = false;
        ShowSource();
    }

    private void CatalogSource_Click(object sender, RoutedEventArgs e)
    {
        if (_showingCatalog) return;

        _showingCatalog = true;
        ShowSource();
    }

    private void ShowSource()
    {
        InstalledSourceButton.Style = (Style)FindResource(_showingCatalog ? "SteamModalGreyButton" : "SteamModalBlueButton");
        CatalogSourceButton.Style = (Style)FindResource(_showingCatalog ? "SteamModalBlueButton" : "SteamModalGreyButton");

        SourceNote.Text = _showingCatalog
            ? Strings.ModLists_AddSourceCatalogNote
            : Strings.ModLists_AddSourceInstalledNote;

        Render();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Render();

    //
    // Rebuilds the visible slice, then puts the ticks back on whatever survived into it. Both
    // halves matter: a tick made under one search has to still be there after another.
    //
    private void Render()
    {
        // The search box raises TextChanged as the XAML is loaded, before the rest of the window
        // exists. AddButton is the last element this method touches, so it is the one to check.
        if (RowsBox is null || AddButton is null) return;

        var query = SearchBox.Text.Trim();
        var source = _showingCatalog ? _catalog : _installed;

        List<ModListAddRow> matched = query.Length == 0 ? source : [.. source.Where(r => r.Matches(query))];
        var shown = matched.Count > MaxRows ? matched.Take(MaxRows).ToList() : matched;

        _syncing = true;
        RowsBox.ItemsSource = shown;
        RowsBox.SelectedItems.Clear();

        foreach (var row in shown.Where(r => r.IsChosen)) RowsBox.SelectedItems.Add(row);

        _syncing = false;

        EmptyNote.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyNote.Text = source.Count == 0
            ? Strings.ModLists_AddNothingHere
            : Strings.ModLists_AddNoMatches;

        ShowCount(matched.Count, shown.Count);
        UpdateChosen();
    }

    private void ShowCount(int matched, int shown)
    {
        var total = _showingCatalog ? _catalog.Count : _installed.Count;

        CountNote.Text = matched != shown
            ? Text(Strings.ModLists_AddTruncatedFormat, shown, matched)
            : _showingCatalog
                ? Strings.ModLists_AddShownCatalog(shown, shown, total)
                : Strings.ModLists_AddShownInstalled(shown, shown, total);
    }

    private void RowsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;

        foreach (ModListAddRow row in e.AddedItems) row.IsChosen = true;
        foreach (ModListAddRow row in e.RemovedItems) row.IsChosen = false;

        UpdateChosen();
    }

    private void UpdateChosen()
    {
        var count = _installed.Concat(_catalog).Count(r => r.IsChosen);

        ChosenNote.Text = count switch
        {
            0 => Strings.ModLists_AddNonePicked,
            _ => Strings.ModLists_AddPicked(count),
        };

        AddButton.Content = count == 0
            ? Strings.ModLists_AddButton
            : Text(Strings.ModLists_AddButtonCountFormat, count);
        AddButton.IsEnabled = count > 0;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
