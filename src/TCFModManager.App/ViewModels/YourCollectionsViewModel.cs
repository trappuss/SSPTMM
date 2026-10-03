using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM): one of your collections on the Your collections page - drawn as Steam draws a
// collection in a list: its pictures, name, who made it, how many items, and here also how much of
// it your game has.
//
public sealed partial class CollectionTileViewModel : ObservableObject
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public string? ByText { get; init; }

    public required string ItemsText { get; init; }

    public string? SptVersion { get; init; }

    public bool IsForInstalledSpt { get; init; }

    // Up to four of its mods' pictures, Steam's collection mosaic in small.
    public required IReadOnlyList<string> Pictures { get; init; }

    public bool HasPictures => Pictures.Count > 0;

    public bool IsShared { get; init; }

    public bool IsFollowed { get; init; }

    public bool IsFromServer { get; init; }

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private bool _hasUpdate;
}

// A followed collection whose file holds a newer copy: said on the page until synced or put off.
public sealed record CollectionUpdateRow(CollectionSharing.FollowedUpdate Update, string Text);

// A share code on the clipboard that isn't here yet (or is newer than what is).
public sealed record ClipboardOffer(string Code, string Text, bool IsUpdate);

//
// Fork (SSPTMM, UI tidy-up 5): the Collections tab's front page - your collections as Steam's
// "Your Collections" shows them, a grid of them to open, with making, adding and following new ones
// along the top. Opening one shows its Steam collection page (Subscribe to all, Share); the form
// that edits a collection is that page's Manage.
//
public sealed partial class YourCollectionsViewModel : LocalizedViewModel
{
    public ObservableCollection<CollectionTileViewModel> Collections { get; } = [];

    public ObservableCollection<CollectionUpdateRow> Updates { get; } = [];

    // Codes put off this session - not offered again until something else is copied.
    private readonly HashSet<string> _dismissedCodes = [];

    [ObservableProperty]
    private ClipboardOffer? _offer;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsEmpty => Collections.Count == 0;

    public async Task RefreshAsync()
    {
        CollectionSharing.WriteSharedFolders();

        var settings = new SettingsService().Load();
        var installed = AppServices.SptEnvironment.InstalledVersion;
        var catalog = AppServices.ModCache.AllMods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());

        var tiles = ModListService.Collections()
            .Select(list => new CollectionTileViewModel
            {
                Id = list.Id,
                Title = list.Name,
                ByText = list.Origin switch
                {
                    ModListOrigin.Server => Text(Strings.Collections_FromServerFormat, list.Source),
                    ModListOrigin.Imported when list.Source is { } who => Text(Strings.Collections_ByFormat, who),
                    ModListOrigin.Imported => null,
                    _ => Strings.Collections_ByYou,
                },
                ItemsText = Strings.Collections_Contains(list.Entries.Count, list.Entries.Count),
                SptVersion = list.SptVersion,
                IsForInstalledSpt = SameLine(list.SptVersion, installed),
                Pictures = [.. list.Entries
                    .Where(e => e is { ModId: not null, IsAddon: false })
                    .Select(e => catalog.GetValueOrDefault(e.ModId!.Value)?.Thumbnail)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct()
                    .Take(4)
                    .Cast<string>()],
                IsShared = settings.SharedFiles.ContainsKey(list.Id),
                IsFollowed = settings.FollowedFiles.ContainsKey(list.Id),
                IsFromServer = list.Origin == ModListOrigin.Server,
            })
            .ToList();

        Collections.Clear();
        foreach (var tile in tiles) Collections.Add(tile);
        OnPropertyChanged(nameof(IsEmpty));

        await UpdateInstallStateAsync();
        await CheckFollowedAsync();
    }

    // "61 of 68 installed" on each - matched the way the collection page itself matches.
    private async Task UpdateInstallStateAsync()
    {
        await AppServices.ModCache.EnsureLoadedAsync();
        await AppServices.Browse.EnsureInstalledIndexAsync();
        var catalog = AppServices.ModCache.AllMods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var tile in Collections)
        {
            if (AppServices.ModLists.Find(tile.Id) is not { } list) continue;

            var mods = list.Entries.Where(e => !e.IsAddon).ToList();
            var have = mods.Count(e =>
                (e.ModId is { } id && catalog.GetValueOrDefault(id) is { } mod
                    ? AppServices.Browse.InstalledMatchFor(mod)
                    : AppServices.Browse.InstalledMatchFor(new Mod { Id = 0, Guid = e.Guid, Name = e.Name })) is not null);

            tile.IsComplete = mods.Count > 0 && have == mods.Count;
            tile.StatusText = tile.IsComplete
                ? Strings.Collections_AllInstalled
                : Text(Strings.Collections_InstalledFormat, have, mods.Count);
        }
    }

    private async Task CheckFollowedAsync()
    {
        Updates.Clear();
        foreach (var update in await CollectionSharing.CheckFollowedAsync())
        {
            Updates.Add(new CollectionUpdateRow(update, Text(
                Strings.Sharing_FollowedUpdateFormat,
                update.Stored.Name,
                update.Incoming.Source ?? Strings.Sharing_Someone,
                CollectionSharing.Describe(update.Diff))));

            if (Collections.FirstOrDefault(t => t.Id == update.Stored.Id) is { } tile) tile.HasUpdate = true;
        }
    }

    //
    // A share code on the clipboard - copied from Discord a moment ago, most likely - that would add
    // or update a collection here. Read when the page shows and when the window comes back to the
    // front. Never throws: the clipboard can be busy with another program.
    //
    public void CheckClipboard()
    {
        string? text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return;
        }

        if (!ModListShareCode.Contains(text) || _dismissedCodes.Contains(text!))
        {
            Offer = null;
            return;
        }

        var read = ModListShareCode.Decode(text);
        if (!read.Succeeded) return;

        var incoming = read.List!;
        var existing = AppServices.ModLists.Find(incoming.Id);

        // Already here, and this copy is no newer: nothing to offer.
        if (!ModListDiff.IsNews(existing, incoming))
        {
            Offer = null;
            return;
        }

        var from = incoming.Source ?? Strings.Sharing_Someone;
        Offer = existing is null
            ? new ClipboardOffer(text!, Text(Strings.Sharing_ClipboardNewFormat, incoming.Name, from, incoming.Entries.Count), false)
            : new ClipboardOffer(text!, Text(Strings.Sharing_ClipboardUpdateFormat, incoming.Name, from), true);
    }

    [RelayCommand]
    private async Task AcceptClipboardAsync()
    {
        if (Offer is not { } offer) return;
        Offer = null;
        _dismissedCodes.Add(offer.Code);

        if (await CollectionSharing.AddFromCodeAsync(offer.Code) is { } received)
        {
            await RefreshAsync();
            CollectionSharing.Announce(received);
        }
    }

    [RelayCommand]
    private void DismissClipboard()
    {
        if (Offer is { } offer) _dismissedCodes.Add(offer.Code);
        Offer = null;
    }

    [RelayCommand]
    private static void Open(CollectionTileViewModel? tile)
    {
        if (tile is not null) AppServices.CollectionOverlay.Show(tile.Id);
    }

    // Steam's Create Collection: here, from the mods installed now, at their versions.
    [RelayCommand]
    private async Task CreateAsync()
    {
        if (AskName(Strings.Collections_CreateTitle, Strings.Collections_CreatePrompt, "") is not { } name) return;

        IsBusy = true;
        try
        {
            var captured = await AppServices.ModListWorkflow.CaptureAsync(name);
            if (captured is null)
            {
                Message = AppMessages.NoSptInstallFolder;
                return;
            }

            await RefreshAsync();
            AppServices.CollectionOverlay.Show(captured.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddFromCodeAsync()
    {
        var box = new System.Windows.Controls.TextBox
        {
            Style = (Style)Application.Current.FindResource("SteamSearchField"),
            Height = 34,
            Margin = new Thickness(0, 10, 0, 0),
        };

        // Whatever is on the clipboard, if it is a code - it nearly always will be.
        try
        {
            if (Clipboard.ContainsText() && ModListShareCode.Contains(Clipboard.GetText())) box.Text = Clipboard.GetText().Trim();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }

        var body = new System.Windows.Controls.StackPanel { MinWidth = 460 };
        body.Children.Add(new System.Windows.Controls.TextBlock { Text = Strings.Sharing_PasteCode, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(box);

        var answer = SteamDialog.Show(
            Strings.Sharing_AddFromCode,
            body,
            new SteamDialogChoice(Strings.Sharing_Add, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));
        if (answer != 0 || string.IsNullOrWhiteSpace(box.Text)) return;

        if (await CollectionSharing.AddFromCodeAsync(box.Text) is { } received)
        {
            Offer = null;
            await RefreshAsync();
            CollectionSharing.Announce(received);
        }
    }

    [RelayCommand]
    private async Task AddFromFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.ModLists_ImportDialogTitle,
            Filter = ModListFileDialog.Filter,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        var read = ModListFile.Load(dialog.FileName);
        if (!read.Succeeded)
        {
            Message = Text(Strings.ModLists_ImportReadFailedFormat, read.Error);
            return;
        }

        if (CollectionSharing.Receive(await CollectionSharing.WithNamesAsync(read.List!)) is { } received)
        {
            await RefreshAsync();
            CollectionSharing.Announce(received);
        }
    }

    // A friend's collection kept in a folder you both share: added now, and checked each time this
    // page shows for a newer copy.
    [RelayCommand]
    private async Task FollowAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.Sharing_FollowDialogTitle,
            Filter = ModListFileDialog.Filter,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        if (await CollectionSharing.FollowFileAsync(dialog.FileName) is { } received)
        {
            await RefreshAsync();
            CollectionSharing.Announce(received);
        }
    }

    [RelayCommand]
    private async Task SyncUpdateAsync(CollectionUpdateRow? row)
    {
        if (row is null) return;

        var path = CollectionSharing.FollowedFileFor(row.Update.Stored.Id);
        if (CollectionSharing.Receive(row.Update.Incoming, path) is null) return;

        Updates.Remove(row);
        await RefreshAsync();
        CollectionSharing.Sync(row.Update.Stored.Id);
    }

    [RelayCommand]
    private void Later(CollectionUpdateRow? row)
    {
        if (row is not null) Updates.Remove(row);
    }

    //
    // A name, asked the way Save to Collection asks it. Null when cancelled or left empty.
    //
    internal static string? AskName(string title, string prompt, string initial)
    {
        var box = new System.Windows.Controls.TextBox
        {
            Style = (Style)Application.Current.FindResource("SteamSearchField"),
            Height = 34,
            Text = initial,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var body = new System.Windows.Controls.StackPanel { MinWidth = 420 };
        body.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(box);

        var answer = SteamDialog.Show(
            title,
            body,
            new SteamDialogChoice(Strings.Collection_Save, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        return answer == 0 && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }

    // Same SPT release line (4.0.x against 4.0.y).
    private static bool SameLine(string? a, string? b)
    {
        static string Line(string v) => string.Join('.', v.Split('.').Take(2));
        return a is not null && b is not null && string.Equals(Line(a), Line(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);
}
