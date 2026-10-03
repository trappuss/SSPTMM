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

        if (ResolveMod(element) is not { } mod)
        {
            // Nothing to act on (an entry sp-mod.com has no listing for): no menu at all.
            e.Handled = true;
            return;
        }

        menu.Items.Clear();
        Fill(menu, mod, element);
        menu.PlacementTarget = element;
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
        ViewModels.ModCardViewModel card => card.Mod,
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
