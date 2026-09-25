using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// One item on a collection's page (Steam's collectionItem): the mod's picture, name, author and
// short description, and the square subscribe button - or, for an entry sp-mod.com cannot supply,
// the name alone and a note that it is installed by hand.
//
public sealed partial class WorkshopCollectionItem : ObservableObject
{
    public WorkshopCollectionItem(ModListEntry entry, Mod? mod, string? addonName)
    {
        Entry = entry;
        Mod = mod;
        _addonName = addonName;
    }

    private readonly string? _addonName;

    public ModListEntry Entry { get; }

    // The catalog listing, when the entry names a mod sp-mod.com has.
    public Mod? Mod { get; }

    public string Name => Mod?.Name ?? _addonName ?? Entry.Name;

    public string? Author => Mod?.Owner?.Name;

    public string? Teaser => Mod?.Teaser;

    public string? Thumbnail => Mod?.Thumbnail;

    public bool IsAddon => Entry.IsAddon;

    // Subscribe here works for a mod the catalog has; an addon installs from its mod's page, and an
    // entry with no listing at all is installed by hand.
    public bool CanSubscribe => Mod is not null && !IsAddon;

    public string? Note => Mod is not null
        ? null
        : IsAddon ? Strings.Collection_AddonNote : Strings.Collection_ByHandNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeToolTip))]
    private bool _isInstalled;

    public string SubscribeToolTip => IsInstalled ? Strings.Collection_UnsubscribeToolTip : Strings.Collection_SubscribeToolTip;

    public void Refresh() => IsInstalled = Mod is not null && AppServices.Browse.InstalledMatchFor(Mod) is not null;
}

//
// A collection's Workshop page (sharedfiles/filedetails for a collection): the title, the items
// with Subscribe to all / Unsubscribe from all / Save to Collection over them, and who made the
// items, beside them.
//
// Collections are this install's mod lists; applying one is the Collections page's work, and this
// page hands over to it rather than doing it a second way - see SubscribeToAllAsync.
//
public sealed partial class WorkshopCollectionViewModel : LocalizedViewModel, IModActionHost
{
    void IModActionHost.ShowActionMessage(string? message) => Message = message;

    public WorkshopCollectionViewModel(ModList list)
    {
        List = list;

        var catalog = AppServices.ModCache.AllMods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var entry in ModListEntries.Sorted(list.Entries))
        {
            Mod? mod = null;
            string? addonName = null;

            if (entry is { ModId: { } id, IsAddon: false } && catalog.TryGetValue(id, out var found)
                && !BrowseViewModel.IsSelf(found))
            {
                mod = found;
            }
            else if (entry is { ModId: { } addonId, IsAddon: true })
            {
                addonName = AppServices.Addons.ById(addonId)?.Name;
            }

            Items.Add(new WorkshopCollectionItem(entry, mod, addonName));
        }

        Authors = Items
            .Select(i => i.Author)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList()!;

        RefreshInstallState();
        AppServices.Browse.InstalledIndexChanged += OnInstalledIndexChanged;
    }

    public ModList List { get; }

    public string Name => List.Name;

    public ObservableCollection<WorkshopCollectionItem> Items { get; } = [];

    public IReadOnlyList<string> Authors { get; }

    public bool HasAuthors => Authors.Count > 0;

    public string ItemsTitle => Strings.Collection_ItemsHeader;

    public string ItemsCount => LocalizationService.Text(Strings.Collection_ItemsCountFormat, Items.Count);

    public string CreatedByTitle => Strings.Collection_CreatedBy(Items.Count, Items.Count);

    public bool IsEmpty => Items.Count == 0;

    public string? PostedText => SteamDates.Short(List.CreatedAt);

    public string? UpdatedText => SteamDates.Short(List.UpdatedAt);

    // What the last action said.
    [ObservableProperty]
    private string? _message;

    private void OnInstalledIndexChanged(object? sender, EventArgs e) => RefreshInstallState();

    private void RefreshInstallState()
    {
        foreach (var item in Items) item.Refresh();
    }

    /// <summary>Stops listening once the page is closed or replaced.</summary>
    public void Detach() => AppServices.Browse.InstalledIndexChanged -= OnInstalledIndexChanged;

    // ------------------------------------------------------------------ one item

    [RelayCommand]
    private async Task OpenItemAsync(WorkshopCollectionItem? item)
    {
        if (item?.Mod is { } mod && await AppServices.Browse.LoadDetailsAsync(mod) is { } failed) Message = failed;
    }

    // The square button: Subscribe, or - subscribed - Unsubscribe, through the same code the item
    // page's buttons use.
    // Each row on its own: one row's removal (seconds, with a rescan) must not disable the rest.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleSubscribeAsync(WorkshopCollectionItem? item)
    {
        if (item?.Mod is not { } mod || !item.CanSubscribe) return;

        var said = item.IsInstalled ? await ModActions.UnsubscribeAsync(mod) : await ModActions.SubscribeAsync(mod);
        if (said is not null) Message = said;
    }

    [RelayCommand]
    private static void OpenAuthor(string? author)
    {
        if (author is not null) ModActions.ShowAuthor(author);
    }

    // ------------------------------------------------------------------ the whole collection

    //
    // Steam's Subscribe to all: add the items (Add Only), or make the subscriptions exactly this
    // collection (Overwrite My Subscriptions), which Steam asks about twice. Here that is applying
    // the list additively or exclusively - and applying is the Collections page's, which shows what
    // will happen before anything does. So the answer is carried there, as that page's preview.
    //
    [RelayCommand]
    private void SubscribeToAll()
    {
        var answer = SteamDialog.Show(
            Strings.Collection_SubscribeAllTitle,
            Strings.Collection_SubscribeAllBody,
            new SteamDialogChoice(Strings.Collection_AddOnly, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Collection_Overwrite, SteamDialogButton.Blue),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        ModListPolicy policy;
        switch (answer)
        {
            case 0:
                policy = ModListPolicy.Additive;
                break;
            case 1:
                var sure = SteamDialog.Show(
                    Strings.Collection_Overwrite,
                    Strings.Collection_OverwriteBody,
                    new SteamDialogChoice(Strings.Collection_OverwriteYes, SteamDialogButton.Green),
                    new SteamDialogChoice(Strings.Collection_SaveCurrent, SteamDialogButton.Blue),
                    new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

                if (sure == 1)
                {
                    // Capturing what is installed now is the Collections page's first box.
                    ModListsViewModel.Request(new ModListsRequest(null, null));
                    AppNavigation.Navigate(typeof(ModListsPage));
                    return;
                }

                if (sure != 0) return;
                policy = ModListPolicy.Exclusive;
                break;
            default:
                return;
        }

        ModListsViewModel.Request(new ModListsRequest(List.Id, policy));
        AppNavigation.Navigate(typeof(ModListsPage));
    }

    //
    // Steam's Unsubscribe from all takes every item out of the subscriptions. Here that is asked once
    // for the lot: set the installed ones aside (disabled - each comes back with a click on
    // Subscribed items), or remove them (files deleted, config files copied aside first). Both go
    // through the Subscribed items page's own code.
    //
    [RelayCommand]
    private async Task UnsubscribeFromAllAsync()
    {
        var installed = Items.Where(i => i is { IsInstalled: true, Mod: not null }).Select(i => i.Mod!.Id).ToHashSet();
        if (installed.Count == 0)
        {
            Message = Strings.Collection_NothingInstalled;
            return;
        }

        var answer = SteamDialog.Show(
            Strings.Collection_UnsubscribeAll,
            Strings.Collection_UnsubscribeAllBody(installed.Count, installed.Count),
            new SteamDialogChoice(Strings.Collection_SetAside, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Collection_RemoveAll, SteamDialogButton.Blue),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        if (answer is not (0 or 1)) return;

        var page = InstalledViewModel.Current ?? new InstalledViewModel();
        Message = (answer == 0
            ? await page.DisableModsAsync(installed)
            : await page.RemoveModsAsync(installed)) ?? Message;
    }

    // Steam's Save to Collection: a copy of this collection, under a new name.
    [RelayCommand]
    private void SaveToCollection()
    {
        var name = new System.Windows.Controls.TextBox
        {
            Style = (System.Windows.Style)System.Windows.Application.Current.FindResource("SteamSearchField"),
            Height = 34,
            Text = LocalizationService.Text(Strings.Collection_CopyNameFormat, List.Name),
            Margin = new System.Windows.Thickness(0, 10, 0, 0),
        };

        var body = new System.Windows.Controls.StackPanel { MinWidth = 420 };
        body.Children.Add(new System.Windows.Controls.TextBlock { Text = Strings.Collection_NamePrompt });
        body.Children.Add(name);

        var answer = SteamDialog.Show(
            Strings.Collection_SaveToCollection,
            body,
            new SteamDialogChoice(Strings.Collection_Save, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        if (answer != 0 || string.IsNullOrWhiteSpace(name.Text)) return;

        var copy = AppServices.ModLists.Fork(List.Id, name.Text.Trim(), DateTimeOffset.UtcNow);
        Message = LocalizationService.Text(Strings.Collection_SavedFormat, copy.Name);
    }

    // The Collections page, with this list chosen, for everything else - renaming, what it applies
    // as, sharing.
    [RelayCommand]
    private void Manage()
    {
        ModListsViewModel.Request(new ModListsRequest(List.Id, null));
        AppNavigation.Navigate(typeof(ModListsPage));
    }
}
