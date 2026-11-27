using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TCFModManager.App.Services;
using TCFModManager.App.Views;

namespace TCFModManager.App;

//
// Fork (1.4.9): the pages that open over the others - an item's, a collection's, an author's, and
// the two Quick Views - are made when first wanted rather than with the window. Built with it they
// took 150-200 ms of the 280-360 ms the window took to build, measured, before its first frame, for
// pages that start out hidden. Each goes into the place the window's XAML keeps for it, so what is
// drawn over what is as it was.
//
// So that the first click on a mod does not pay for it either, they are also made one at a time
// once the window is up and has nothing else to do.
//
public partial class MainWindow
{
    private WorkshopItemView? _itemPage;
    private WorkshopCollectionView? _collectionPage;
    private AuthorView? _authorPage;
    private QuickViewOverlay? _quickView;
    private CollectionQuickViewOverlay? _collectionQuickView;

    private WorkshopItemView ItemPage => _itemPage ??= MakeItemPage();

    private WorkshopCollectionView CollectionPage => _collectionPage ??= MakeCollectionPage();

    private AuthorView AuthorPage => _authorPage ??= MakeAuthorPage();

    private QuickViewOverlay QuickView => _quickView ??= Placed(QuickViewHost, new QuickViewOverlay());

    private CollectionQuickViewOverlay CollectionQuickView =>
        _collectionQuickView ??= Placed(CollectionQuickViewHost, new CollectionQuickViewOverlay());

    private WorkshopItemView MakeItemPage()
    {
        var page = Covering(Placed(ItemPageHost, new WorkshopItemView()));

        // Esc on a required item, back to the item it was opened from: a step back.
        page.WalkedBack += (_, _) => Returned();
        page.Closed += (_, _) =>
        {
            PerfProbe.ItemPage(opened: false);
            Uncover(page);
            Returned();
        };
        return page;
    }

    private WorkshopCollectionView MakeCollectionPage()
    {
        var page = Covering(Placed(CollectionPageHost, new WorkshopCollectionView()));
        page.Closed += (_, _) =>
        {
            Uncover(page);
            Returned();
        };
        return page;
    }

    private AuthorView MakeAuthorPage()
    {
        var page = Covering(Placed(AuthorPageHost, new AuthorView()));
        page.Closed += (_, _) =>
        {
            Uncover(page);
            Returned();
        };
        return page;
    }

    private static T Placed<T>(Border host, T view) where T : FrameworkElement
    {
        view.Visibility = Visibility.Collapsed;
        host.Child = view;
        return view;
    }

    // The item, collection and author pages have no background of their own, so the window's grid
    // (or picture) shows through them as it does on the other Workshop pages. The page they were
    // opened over is hidden meanwhile - Hidden, not Collapsed, so it keeps its layout and scroll
    // for when it comes back.
    private T Covering<T>(T page) where T : FrameworkElement
    {
        page.IsVisibleChanged += (_, _) =>
        {
            RootNavigationView.Visibility = OverlayShowing ? Visibility.Hidden : Visibility.Visible;
            SyncWorkshopStrip();
        };
        return page;
    }

    private void MakeOverlaysWhenIdle(int next = 0)
    {
        if (next > 4) return;

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            _ = next switch
            {
                0 => (FrameworkElement)ItemPage,
                1 => QuickView,
                2 => CollectionPage,
                3 => AuthorPage,
                _ => CollectionQuickView,
            };
            MakeOverlaysWhenIdle(next + 1);
        });
    }
}
