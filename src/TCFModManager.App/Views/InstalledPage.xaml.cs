using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class InstalledPage : Page
{
    public InstalledViewModel ViewModel { get; } = new();

    // Drag/click state for the manual gesture on a group-view mod row (see ModRow_PreviewMouseMove
    // and ModRow_PreviewMouseLeftButtonUp) - WPF has no built-in "drag this ItemsControl row"
    // support, so this hand-rolls the standard press/move-past-a-threshold/release pattern that
    // lets the same row both open on a plain click and drag-to-another-group on a real drag. Only
    // used in group view; the flat grid's ListBox has its own click handling (SelectionChanged)
    // and no drag behavior.
    private Point _dragStart;
    private InstalledModCardViewModel? _dragCandidate;
    private bool _dragStarted;

    // Auto-scroll while a drag is in flight (see GroupsScrollViewer_DragOver and DragScroll_Tick):
    // holding a mod near the top or bottom edge of the group list scrolls it, so a group that's
    // off-screen can be reached without dropping the mod somewhere else first. The zone is how far
    // in from either edge counts as "near"; the step range is per tick, ramping from slow at the
    // inner boundary to fast right at the edge. The wheel does the same job under direct control -
    // see DragWheelHook.
    private const double DragScrollZone = 56;
    private const double DragScrollMinStep = 4;
    private const double DragScrollMaxStep = 26;
    private DispatcherTimer? _dragScrollTimer;
    private double _dragScrollStep;

    // The HwndSource the wheel hook is attached to, held only for as long as a drag is in flight so
    // the hook comes straight back off when it ends. See HookDragWheel.
    private HwndSource? _dragWheelSource;

    private const int WmMouseWheel = 0x020A;

    public InstalledPage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        // Registered directly on the Page (not via a XAML attribute on a specific element) so it's
        // the very first thing to see every wheel event over this page - PreviewMouseWheel tunnels
        // root-to-leaf, so this fires before any descendant's own bubble-phase handling, including
        // the internal ScrollViewer part several controls (ui:TextBox, ComboBox, ...) use for their
        // own content, which otherwise swallows the wheel event and marks it handled even when
        // there's nothing for that control itself to scroll. handledEventsToo:true on top of that
        // means it still runs even if something upstream in the tunnel already marked the event
        // handled. See Page_PreviewMouseWheel below.
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(Page_PreviewMouseWheel), true);

        // handledEventsToo:true is the point of registering these here rather than as XAML
        // attributes: Section_DragOver/Section_Drop below mark their events handled on the group
        // ui:Card, which sits between the dragged pointer and this ScrollViewer, so a normal
        // bubble-phase handler on the ScrollViewer would never run while over a card - i.e. over
        // exactly the part of the list where dragging actually happens.
        GroupsScrollViewer.AddHandler(DragOverEvent, new DragEventHandler(GroupsScrollViewer_DragOver), true);
        GroupsScrollViewer.AddHandler(DragLeaveEvent, new DragEventHandler(GroupsScrollViewer_DragLeave), true);
        GroupsScrollViewer.AddHandler(DropEvent, new DragEventHandler(GroupsScrollViewer_Drop), true);
    }

    // How close to the bottom, in pixels, the infinite list adds its next cards - as Browse does.
    // ScrollChanged also fires when the list grows, so a window tall enough to show every card
    // added keeps asking until the list is longer than the view.
    private const double LoadMoreDistance = 800;

    private void CardsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ViewModel.IsInfinite) return;

        var remaining = CardsScrollViewer.ExtentHeight - CardsScrollViewer.ViewportHeight - CardsScrollViewer.VerticalOffset;
        if (remaining <= LoadMoreDistance) ViewModel.LoadMore();
    }

    private async void InstalledPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.UpdateLayoutForWidth(ResultsItems.ActualWidth);
        await ViewModel.ScanCommand.ExecuteAsync(null);
    }

    //
    // In multi-select mode a click anywhere on a card ticks it, rather than only the small
    // checkbox in its header - the same hit target the card grid had before it became expandable.
    // Marked handled so the expander doesn't also open while you are picking mods.
    //
    // Outside select mode this does nothing and the click falls through to the expander, which is
    // what opens the card. The versions dialog is reached from "Details and versions" inside the
    // card, exactly as it already was in the List view.
    //
    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ViewModel.SelectionMode) return;
        if (sender is not FrameworkElement { DataContext: InstalledModCardViewModel mod }) return;

        // A button or the checkbox itself still does its own job; this claims the rest of the card -
        // the header included, which WPF UI draws inside the expander's own toggle button: that one
        // is not counted, so a click on the header ticks rather than opening the card.
        if (IsInsideButton(e.OriginalSource, sender as DependencyObject)) return;

        mod.IsSelected = !mod.IsSelected;
        e.Handled = true;
    }

    private void ResultsItems_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.UpdateLayoutForWidth(e.NewSize.Width);
    }

    private void CardsView_Click(object sender, RoutedEventArgs e) => ViewModel.ViewMode = InstalledViewMode.Cards;

    private void GroupsView_Click(object sender, RoutedEventArgs e) => ViewModel.ViewMode = InstalledViewMode.Groups;

    private void ListView_Click(object sender, RoutedEventArgs e) => ViewModel.ViewMode = InstalledViewMode.List;

    private void SelectMode_Click(object sender, RoutedEventArgs e) => ViewModel.SelectionMode = !ViewModel.SelectionMode;

    // Lets the wheel scroll whichever of the two scrolling views is showing from anywhere on the
    // page - search box, filter row, group-management bar, directly over the list, all of it - by
    // driving that view's ScrollViewer ourselves unconditionally rather than only stepping in when
    // some other handler didn't already consume the event. Since this runs in the tunnel phase
    // before the ScrollViewer's own native wheel handling would otherwise fire, hovering directly
    // over the list also comes through here (and gets marked handled before the native handling
    // runs) - that's fine, we do the exact same scroll it would have, so there's no visible
    // difference and no double-scroll. Cards view scrolls the same way now that it is an
    // ItemsControl in its own ScrollViewer rather than a ListBox.
    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scroller = ViewModel.ViewMode switch
        {
            InstalledViewMode.Groups => GroupsScrollViewer,
            InstalledViewMode.List => ListScrollViewer,
            InstalledViewMode.Cards => CardsScrollViewer,
            _ => null,
        };

        if (scroller is null) return;

        Behaviors.SmoothScrolling.Glide(scroller, e.Delta);
        e.Handled = true;
    }

    // True when the click landed on (or inside) a button within the row - the row's own gesture
    // steps aside for those, since PreviewMouseLeftButtonDown tunnels through the row Border before
    // reaching the button and would otherwise mark the event handled before the button ever saw it.
    // stopAt: the element handling the click; the walk ends there, and a button that is part of its
    // own template (a CardExpander's header toggle) does not count.
    private static bool IsInsideButton(object? originalSource, DependencyObject? stopAt = null)
    {
        for (var node = originalSource as DependencyObject; node is not null && !ReferenceEquals(node, stopAt);)
        {
            if (node is ButtonBase button && (stopAt is null || !ReferenceEquals(button.TemplatedParent, stopAt))) return true;

            // The visual tree is what the templated parts of a button live in, but a click can
            // report a ContentElement such as a Run as its OriginalSource, and VisualTreeHelper
            // throws on those - the logical tree is the way up from there.
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }

    private void ModRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource))
        {
            _dragCandidate = null;
            _dragStarted = false;
            return;
        }

        // In Multi select a click ticks the row, as on the cards and in List view - no details, no drag.
        if (ViewModel.SelectionMode)
        {
            _dragCandidate = null;
            _dragStarted = false;
            if (sender is FrameworkElement { DataContext: InstalledModCardViewModel ticked }) ticked.IsSelected = !ticked.IsSelected;
            e.Handled = true;
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as InstalledModCardViewModel;
        _dragStarted = false;

        // Without this, the event goes on to bubble as a plain MouseDown, and WPF's default
        // click-to-focus behavior walks up from this (non-focusable) Border to the nearest
        // focusable ancestor - normally the group ScrollViewer or one of its ItemsControls -
        // and focuses it, which was producing a small scroll jump the instant a row was
        // clicked. We're fully hand-rolling this row's click/drag gesture already (see
        // ModRow_PreviewMouseMove/Up below), so nothing downstream needs that default focus
        // assignment. Paired with Focusable="False" on the ScrollViewer/ItemsControls in XAML.
        e.Handled = true;
    }

    private void ModRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null || _dragStarted) return;

        // Sorted by category there are no groups on screen to drag a row into.
        if (ViewModel.GroupsByCategory) return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // Marks this gesture as a drag rather than clearing _dragCandidate outright, so the
        // ButtonUp handler below can tell "this mouse-down turned into a drag" (skip opening
        // details) apart from "this mouse-down never moved" (a plain click).
        _dragStarted = true;
        if (sender is DependencyObject source)
        {
            // Blocks for the whole drag (DoDragDrop runs its own modal message loop), so this is
            // also the one place guaranteed to run once the gesture is over however it ended -
            // dropped on a group, dropped in the gutter, dropped outside the window, or cancelled
            // with Escape. Stopping the auto-scroll timer here means every other stop path below
            // is just a courtesy, not load-bearing.
            HookDragWheel();
            try
            {
                DragDrop.DoDragDrop(source, _dragCandidate, DragDropEffects.Move);
            }
            finally
            {
                UnhookDragWheel();
                StopDragScroll();
            }
        }
    }

    // Runs on every DragOver the group list sees, which is what keeps the scroll speed tracking the
    // pointer. It only records a step; the actual scrolling is the timer's job, because OLE only
    // raises DragOver while the pointer is moving - a pointer parked in the hot zone would scroll
    // once and then stop dead if the scroll happened here.
    private void GroupsScrollViewer_DragOver(object sender, DragEventArgs e)
    {
        // A drag over the gaps between cards reaches this handler unhandled (the ScrollViewer needs
        // AllowDrop in XAML for that to happen at all). Nothing there is a drop target, so say so
        // rather than leaving the Move cursor showing over dead space.
        if (!e.Handled)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        var y = e.GetPosition(GroupsScrollViewer).Y;
        var height = GroupsScrollViewer.ViewportHeight;
        if (height <= DragScrollZone * 2)
        {
            _dragScrollStep = 0;
            return;
        }

        _dragScrollStep = y switch
        {
            _ when y < DragScrollZone => -StepFor(y),
            _ when y > height - DragScrollZone => StepFor(height - y),
            _ => 0,
        };

        if (_dragScrollStep != 0) StartDragScroll();

        // Distance is how far the pointer is from the edge it's near, 0 at the edge itself and
        // DragScrollZone at the inner boundary - so a smaller distance means a faster scroll.
        // Clamped because the pointer can sit slightly past the edge (negative distance) while
        // still inside a child element that extends beyond the viewport.
        static double StepFor(double distance)
        {
            var nearness = Math.Clamp(1 - (distance / DragScrollZone), 0, 1);
            return DragScrollMinStep + (nearness * (DragScrollMaxStep - DragScrollMinStep));
        }
    }

    // Zeroes the step rather than stopping the timer outright: DragLeave also fires on every move
    // from one card to the next (the event bubbles up from whichever child the pointer just left),
    // and the DragOver that immediately follows restores the step well inside a single tick, so a
    // brief zero is invisible. Leaving the list entirely produces a DragLeave with no DragOver
    // after it, which parks the timer at zero until the pointer comes back or the drag ends.
    private void GroupsScrollViewer_DragLeave(object sender, DragEventArgs e) => _dragScrollStep = 0;

    private void GroupsScrollViewer_Drop(object sender, DragEventArgs e) => StopDragScroll();

    private void StartDragScroll()
    {
        // DispatcherPriority.Normal, not Input or below: DoDragDrop's modal loop pumps messages
        // itself, and lower-priority dispatcher work is the first thing to get starved while it
        // does. The timer's own constructor starts it, so the Start() below is only doing anything
        // on subsequent drags.
        _dragScrollTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Normal, DragScroll_Tick, Dispatcher);
        _dragScrollTimer.Start();
    }

    private void StopDragScroll()
    {
        _dragScrollTimer?.Stop();
        _dragScrollStep = 0;
    }

    private void DragScroll_Tick(object? sender, EventArgs e)
    {
        if (_dragScrollStep == 0) return;
        GroupsScrollViewer.ScrollToVerticalOffset(GroupsScrollViewer.VerticalOffset + _dragScrollStep);
    }

    //
    // Lets the wheel scroll the group list while a mod is being dragged, so reaching an off-screen
    // group is a flick of the wheel rather than a wait on the edge auto-scroll above.
    //
    // This has to go in at the raw window-message level: DoDragDrop runs its own modal message loop
    // for the whole drag, and WPF's input manager routes nothing through the element tree while it
    // does - so neither Page_PreviewMouseWheel nor any other MouseWheel handler fires. The loop
    // does still dispatch the messages it doesn't consume itself (it only takes mouse-move, the
    // mouse buttons and the modifier keys), and WM_MOUSEWHEEL goes to the focused window, which is
    // ours - so it arrives at the window procedure, where a plain HwndSource hook can see it. No
    // P/Invoke needed, and nothing is left installed: the hook goes on immediately before
    // DoDragDrop and comes off in its finally.
    //
    private void HookDragWheel()
    {
        _dragWheelSource = PresentationSource.FromVisual(this) as HwndSource;
        _dragWheelSource?.AddHook(DragWheelHook);
    }

    private void UnhookDragWheel()
    {
        _dragWheelSource?.RemoveHook(DragWheelHook);
        _dragWheelSource = null;
    }

    private IntPtr DragWheelHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmMouseWheel) return IntPtr.Zero;

        // wParam's high word is the wheel delta, signed, in multiples of WHEEL_DELTA (120) and
        // positive away from the user - the same units and sign as MouseWheelEventArgs.Delta, so
        // this scrolls by exactly what Page_PreviewMouseWheel would have outside a drag.
        var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        GroupsScrollViewer.ScrollToVerticalOffset(GroupsScrollViewer.VerticalOffset - delta);

        handled = true;
        return IntPtr.Zero;
    }

    // Opens the update dialog for a group-view row that was clicked rather than dragged - the same
    // dialog the "Details and versions" button opens for a card. DoDragDrop below runs its
    // own modal loop and swallows the mouse-up that ends a real drag, so in practice this only ever
    // fires for a genuine click; the _dragStarted check is the belt-and-braces guard against it.
    private async void ModRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var mod = _dragCandidate;
        var wasDrag = _dragStarted;
        _dragCandidate = null;
        _dragStarted = false;

        if (wasDrag || mod is null) return;
        if ((sender as FrameworkElement)?.DataContext as InstalledModCardViewModel != mod) return;

        await ViewModel.ShowDetailsCommand.ExecuteAsync(mod);
    }

    // A category's section is not a group: nothing is dropped on it.
    private void Section_DragOver(object sender, DragEventArgs e)
    {
        var isCategory = sender is FrameworkElement { DataContext: ModGroupSectionViewModel { IsCategory: true } };
        e.Effects = !isCategory && e.Data.GetDataPresent(typeof(InstalledModCardViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Section_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModGroupSectionViewModel { IsCategory: false } section }) return;
        if (e.Data.GetData(typeof(InstalledModCardViewModel)) is not InstalledModCardViewModel mod) return;

        ViewModel.MoveModToGroup(mod, section.GroupId);
        e.Handled = true;
    }

    private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModGroupSectionViewModel section }) return;

        if (e.Key == Key.Enter)
        {
            ViewModel.CommitRenameCommand.Execute(section);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelRenameCommand.Execute(section);
            e.Handled = true;
        }
    }

    // Also commits on losing focus by any other means (clicking elsewhere, tabbing away) - but only
    // while still actually editing, so the extra commit the Enter path's own visibility change can
    // trigger (the TextBox collapses and loses focus as a side effect) is skipped rather than firing
    // a harmless-but-redundant second save.
    private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModGroupSectionViewModel { IsEditing: true } section }) return;
        ViewModel.CommitRenameCommand.Execute(section);
    }
}
