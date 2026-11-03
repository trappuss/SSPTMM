using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;

namespace TCFModManager.App.Behaviors;

//
// The right-click menu on a mod, wherever one is shown: ModContextMenu.Mod="{Binding Mod}" (or
// ModId, for a place that only knows the id) on the element that stands for it.
//
// Steam's web pages have no menu on a Workshop item, so what is on it is this app's: open it,
// Subscribe or Unsubscribe, Update when there is one, Quick View, Add to Collection, the link, the
// author's other items and following them - each through ModActions, the same code the item page's buttons use.
// It looks like Steam's own popup menus (SteamPopupMenu). The menu is built as it opens, so it
// always matches the mod's state at that moment.
//
// What an action says goes to the nearest page that can show it (IModActionHost), found by walking
// up from the element; Browse's status line when none is found.
//
public static class ModContextMenu
{
    public static readonly DependencyProperty ModProperty = DependencyProperty.RegisterAttached(
        "Mod", typeof(Mod), typeof(ModContextMenu), new PropertyMetadata(null, OnTargetChanged));

    public static Mod? GetMod(DependencyObject element) => (Mod?)element.GetValue(ModProperty);

    public static void SetMod(DependencyObject element, Mod? value) => element.SetValue(ModProperty, value);

    public static readonly DependencyProperty ModIdProperty = DependencyProperty.RegisterAttached(
        "ModId", typeof(int?), typeof(ModContextMenu), new PropertyMetadata(null, OnTargetChanged));

    public static int? GetModId(DependencyObject element) => (int?)element.GetValue(ModIdProperty);

    public static void SetModId(DependencyObject element, int? value) => element.SetValue(ModIdProperty, value);

    // Fork (1.4.2): on a mod on Subscribed items - adds "Add to group", and gives a mod sp-mod.com has
    // no listing for a menu at all, holding only that.
    public static readonly DependencyProperty GroupsProperty = DependencyProperty.RegisterAttached(
        "Groups", typeof(bool), typeof(ModContextMenu), new PropertyMetadata(false, OnTargetChanged));

    public static bool GetGroups(DependencyObject element) => (bool)element.GetValue(GroupsProperty);

    public static void SetGroups(DependencyObject element, bool value) => element.SetValue(GroupsProperty, value);

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        element.ContextMenuOpening -= Element_ContextMenuOpening;
        element.ContextMenuOpening += Element_ContextMenuOpening;

        // ContextMenuOpening is only raised for an element that has a menu; this one is filled in
        // as it opens.
        element.ContextMenu ??= new ContextMenu { Style = MenuStyle() };
    }

    private static Mod? ResolveMod(FrameworkElement element) =>
        GetMod(element) ?? (GetModId(element) is { } id ? AppServices.Browse.FindInCatalog(id) : null);

    private static void Element_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is not { } menu) return;

        var mod = ResolveMod(element);
        var card = GetGroups(element) ? element.DataContext as ViewModels.InstalledModCardViewModel : null;
        var page = card is null ? null : HostOf(element) as ViewModels.InstalledViewModel;

        if (mod is null && page is null)
        {
            // Nothing to act on (an entry sp-mod.com has no listing for): no menu at all.
            e.Handled = true;
            return;
        }

        menu.Items.Clear();

        // Fork (1.4.3): one of several picked mods - what can be done to all of them, as Steam's
        // library offers for a selection, and nothing that is about one mod only.
        if (page is not null && card is not null && page.ModsFor(card).Count > 1)
        {
            FillPicked(menu, page, card);
            menu.PlacementTarget = element;
            return;
        }

        if (mod is not null) Fill(menu, mod, element);

        if (page is not null && card is not null)
        {
            if (mod is not null) menu.Items.Add(Rule());
            menu.Items.Add(GroupMenu(page, card));
            menu.Items.Add(ModColorMenu(page, card)); // Fork (1.4.3)
            menu.Items.Add(Item(Strings.Menu_Rename, () => page.RenameMod(card))); // Fork (1.4.3)
            menu.Items.Add(Item(Strings.Menu_Note, () => page.EditNote(card))); // Fork (1.4.3)

            // Fork (1.4.3): skip this update, or stop skipping one; go back to what an update replaced.
            var skippable = ViewModels.InstalledViewModel.SkippableUpdate(card);
            var goBack = ViewModels.InstalledViewModel.GoBackVersion(card);
            if (skippable is not null || card.SkippedUpdateVersion is not null || goBack is not null) menu.Items.Add(Rule());
            if (skippable is not null)
                menu.Items.Add(Item(LocalizationService.Text(Strings.Menu_SkipUpdateFormat, skippable), () => page.SkipUpdateAsync(card, skippable)));
            if (card.SkippedUpdateVersion is { } skipped)
                menu.Items.Add(Item(LocalizationService.Text(Strings.Menu_StopSkippingFormat, skipped), () => page.SkipUpdateAsync(card, null)));
            if (goBack is not null)
                menu.Items.Add(Item(LocalizationService.Text(Strings.Menu_GoBackFormat, goBack), () => page.GoBackAsync(card)));

            // Fork (1.4.3): the card's own folder buttons, on the menu too.
            if (card.ClientFolderLink is not null || card.ServerFolderLink is not null) menu.Items.Add(Rule());
            if (card.ClientFolderLink is { } client)
                menu.Items.Add(Item(Strings.Menu_OpenClientFolder, () => page.OpenModFolderCommand.Execute(client)));
            if (card.ServerFolderLink is { } server)
                menu.Items.Add(Item(Strings.Menu_OpenServerFolder, () => page.OpenModFolderCommand.Execute(server)));
        }

        menu.PlacementTarget = element;
    }

    // Fork (1.4.3): the toolbar's own commands for the picked mods, then Add to group.
    private static void FillPicked(ContextMenu menu, ViewModels.InstalledViewModel page, ViewModels.InstalledModCardViewModel card)
    {
        menu.Items.Add(Command(Strings.Installed_EnableSelected, page.EnableSelectedCommand));
        menu.Items.Add(Command(Strings.Installed_DisableSelected, page.DisableSelectedCommand));
        menu.Items.Add(Command(Strings.Installed_UpdateSelected, page.UpdateSelectedCommand));
        menu.Items.Add(Rule());
        menu.Items.Add(GroupMenu(page, card));
        menu.Items.Add(ModColorMenu(page, card)); // Fork (1.4.3)
        menu.Items.Add(Rule());
        menu.Items.Add(Command(Strings.Installed_UnsubscribeSelected, page.UnsubscribeSelectedCommand));
    }

    private static MenuItem Command(string header, System.Windows.Input.ICommand command)
    {
        var item = new MenuItem { Header = header, Style = ItemStyle(), IsEnabled = command.CanExecute(null) };
        item.Click += (_, _) =>
        {
            if (command.CanExecute(null)) command.Execute(null);
        };
        return item;
    }

    //
    // "Add to group" and its list: New group... first, then your groups by name, then Remove from
    // group when any of them is in one. On every picked mod when this one is among two or more picked.
    // A group they are all in already is shown but can't be chosen.
    //
    private static MenuItem GroupMenu(ViewModels.InstalledViewModel page, ViewModels.InstalledModCardViewModel card)
    {
        var mods = page.ModsFor(card);
        var header = mods.Count > 1
            ? LocalizationService.Text(Strings.Menu_AddPickedToGroupFormat, mods.Count)
            : Strings.Menu_AddToGroup;

        var menu = new MenuItem { Header = header, Style = ItemStyle() };
        menu.Items.Add(Item(Strings.Menu_NewGroup, () => page.MoveModsToNewGroup(mods)));

        var groups = page.GroupChoices();
        if (groups.Count > 0) menu.Items.Add(Rule());
        foreach (var (id, name) in groups)
        {
            var item = Item(name, () => page.MoveModsToGroup(mods, id));
            item.IsEnabled = mods.Any(m => m.GroupId != id);
            menu.Items.Add(item);
        }

        if (mods.Any(m => m.GroupId is not null))
        {
            menu.Items.Add(Rule());
            menu.Items.Add(Item(Strings.Menu_RemoveFromGroup, () => page.MoveModsToGroup(mods, null)));
        }

        return menu;
    }

    // Fork (1.4.3): Colour for this mod, or for every picked one when it is among two or more.
    private static MenuItem ModColorMenu(ViewModels.InstalledViewModel page, ViewModels.InstalledModCardViewModel card)
    {
        var mods = page.ModsFor(card);
        var header = mods.Count > 1 ? LocalizationService.Text(Strings.Menu_ColorPickedFormat, mods.Count) : Strings.Menu_Color;
        var shared = mods.Select(m => m.OwnColor).Distinct().Count() == 1 ? mods[0].OwnColor : null;
        return ColorMenu(header, shared, mods.Any(m => m.OwnColor is not null), color => page.ColorMods(mods, color));
    }

    //
    // Fork (1.4.3): a colour menu - the palette's ten, each with its swatch, then Custom colour...
    // and, when there is one to take away, No colour. The colour already given is ticked.
    //
    public static MenuItem ColorMenu(string header, string? current, bool canClear, Action<string?> set)
    {
        var menu = new MenuItem { Header = header, Style = ItemStyle() };
        FillColors(menu.Items, current, canClear, set);
        return menu;
    }

    public static void FillColors(ItemCollection items, string? current, bool canClear, Action<string?> set)
    {
        foreach (var swatch in Core.Services.ModColors.Palette)
        {
            var item = Item(string.Empty, () => set(swatch.Hex));
            item.Header = Swatched(ModOwnColors.NameOf(swatch), ModOwnColors.BrushOf(swatch.Hex), swatch.Hex == current);
            items.Add(item);
        }

        items.Add(Rule());
        var custom = current is not null && Core.Services.ModColors.SwatchOf(current) is null ? current : null;
        var customItem = Item(string.Empty, () =>
        {
            if (ViewModels.InstalledViewModel.AskCustomColor(custom) is { } color) set(color);
        });
        customItem.Header = Swatched(Strings.Menu_ColorCustom, ModOwnColors.BrushOf(custom), custom is not null);
        items.Add(customItem);

        if (canClear) items.Add(Item(Strings.Menu_ColorNone, () => set(null)));
    }

    // A swatch before the name; ringed when it is the colour given now.
    private static StackPanel Swatched(string name, Brush? fill, bool current)
    {
        var dot = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = fill ?? Brushes.Transparent,
            BorderBrush = current ? Brushes.White : (Brush)Application.Current.FindResource("TextFillColorTertiaryBrush"),
            BorderThickness = new Thickness(current ? 2 : fill is null ? 1 : 0),
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { dot, new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center } },
        };
    }

    private static void Fill(ContextMenu menu, Mod mod, FrameworkElement source)
    {
        void Report(string? message)
        {
            if (message is null) return;
            if (HostOf(source) is { } host) host.ShowActionMessage(message);
            else AppServices.Browse.StatusMessage = message;
        }

        menu.Items.Add(Item(Strings.Menu_Open, async () => Report(await ModActions.OpenAsync(mod))));

        menu.Items.Add(Item(Strings.Menu_QuickView, () => AppServices.QuickView.Show(mod, SequenceAround(source))));

        menu.Items.Add(Rule());

        if (ModActions.IsSubscribed(mod))
        {
            if (ModActions.CanUpdate(mod))
                menu.Items.Add(Item(Strings.ModUpdate_Update, async () => await ModActions.UpdateAsync(mod)));

            menu.Items.Add(Item(Strings.Item_Unsubscribe, async () => Report(await ModActions.UnsubscribeAsync(mod))));
        }
        else
        {
            menu.Items.Add(Item(Strings.Item_Subscribe, async () => Report(await ModActions.SubscribeAsync(mod))));
        }

        // Fork: save the file without installing it - unless Monitor mode already only downloads,
        // when Subscribe above does exactly that.
        if (!AppServices.ModPageGate.IsDownloadOnly)
            menu.Items.Add(Item(Strings.Item_DownloadOnly, async () => Report(await ModActions.DownloadOnlyAsync(mod))));

        menu.Items.Add(Item(Strings.Menu_AddToCollection, () => Report(ModActions.AddToCollection(mod))));

        if (ModActions.HasPage(mod))
        {
            menu.Items.Add(Rule());
            menu.Items.Add(Item(Strings.Item_CopyLink, () => Report(ModActions.CopyLink(mod))));
            menu.Items.Add(Item(Strings.Common_ViewModPage, () => ModActions.OpenPage(mod)));
        }

        if (mod.Owner?.Name is { Length: > 0 } author)
        {
            menu.Items.Add(Rule());
            menu.Items.Add(Item(LocalizationService.Text(Strings.Item_AuthorsWorkshopFormat, author), () => ModActions.ShowAuthor(author, mod.Owner?.Id)));

            if (mod.Owner is { Id: not 0 } owner)
            {
                var following = AppServices.Followed.IsFollowing(owner.Id);
                menu.Items.Add(Item(
                    LocalizationService.Text(following ? Strings.Workshop_UnfollowFormat : Strings.Workshop_FollowFormat, author),
                    () => AppServices.Followed.Set(owner.Id, author, !following)));
            }
        }
    }

    public static MenuItem Item(string header, Action run)
    {
        var item = new MenuItem { Header = header, Style = ItemStyle() };
        item.Click += (_, _) => run();
        return item;
    }

    private static MenuItem Item(string header, Func<Task> run)
    {
        var item = new MenuItem { Header = header, Style = ItemStyle() };
        item.Click += async (_, _) =>
        {
            try
            {
                await run();
            }
            catch (Exception ex)
            {
                // Each action reports its own failures; anything left over is logged, not thrown
                // into the dispatcher from a menu click.
                Core.Services.AppLog.Warn("Workshop", $"menu action failed: {ex.Message}");
            }
        };
        return item;
    }

    private static Separator Rule() => new() { Style = (Style)Application.Current.FindResource("SteamPopupMenuSeparator") };

    private static Style MenuStyle() => (Style)Application.Current.FindResource("SteamPopupMenu");

    private static Style ItemStyle() => (Style)Application.Current.FindResource("SteamPopupMenuItem");

    /// <summary>The mods listed beside this element - every item of the list it is in - for Quick
    /// View's arrows. Only the mod itself when it is not in a list.</summary>
    public static IReadOnlyList<Mod> SequenceAround(DependencyObject element)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not ItemsControl list) continue;

            var mods = list.Items.OfType<object>().Select(ModOf).OfType<Mod>().ToList();
            if (mods.Count > 0) return mods;
        }

        return ResolveMod((FrameworkElement)element) is { } mod ? [mod] : [];
    }

    // The mod a list item stands for, whatever kind of row it is.
    private static Mod? ModOf(object item) => item switch
    {
        Mod mod => mod,
        ViewModels.ModCardViewModel card => card.MenuMod, // an addon's card has none (1.4.6)
        ViewModels.WorkshopCollectionItem row => row.Mod,
        ViewModels.WorkshopRelatedItem related => related.Mod,
        ViewModels.WorkshopRequiredItem required => AppServices.Browse.FindInCatalog(required.ModId),
        ViewModels.InstalledModCardViewModel { CatalogModId: { } id } => AppServices.Browse.FindInCatalog(id),
        _ => null,
    };

    private static IModActionHost? HostOf(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is FrameworkElement { DataContext: IModActionHost host }) return host;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }
}
