using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TCFModManager.App.Localization;
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
        // Cards and List take the same drags when they are in sections of your groups.
        foreach (var scroller in new[] { GroupsScrollViewer, CardsScrollViewer, ListScrollViewer })
        {
            scroller.AddHandler(DragOverEvent, new DragEventHandler(GroupsScrollViewer_DragOver), true);
            scroller.AddHandler(DragLeaveEvent, new DragEventHandler(GroupsScrollViewer_DragLeave), true);
            scroller.AddHandler(DropEvent, new DragEventHandler(GroupsScrollViewer_Drop), true);
        }

        // Archives dropped from Explorer anywhere on the page install like Install from file. Seen
        // first (tunnelling) and settled here, so a file dragged over a group never reaches the
        // group's own handlers, which are for moving a mod between groups.
        AddHandler(PreviewDragEnterEvent, new DragEventHandler(Page_PreviewDragOver), true);
        AddHandler(PreviewDragOverEvent, new DragEventHandler(Page_PreviewDragOver), true);
        AddHandler(PreviewDropEvent, new DragEventHandler(Page_PreviewDrop), true);
    }

    // AllowDrop is on for the whole page (for files), which would otherwise also let a mod being
    // moved between groups show a Move cursor over parts of the page that take nothing - so outside
    // the group list that drag is told no here, as it was before.
    private void Page_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(InstalledViewModel.IsModArchive)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!OverGroupList(e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private async void Page_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (!OverGroupList(e)) e.Handled = true;
            return;
        }

        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        // A copy, never a move: the dropped file stays where it is.
        e.Effects = DragDropEffects.Copy;

        // An archive dragged out of 7-Zip or WinRAR is a file they unpacked to the temp folder for
        // the drag, and delete as soon as the drop returns - so it is copied now, before that.
        var kept = KeepDroppedTempFiles(files);

        // Not while OLE is still waiting for the drop to finish: Explorer's window stays stuck
        // until this returns, and the install asks a question first.
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        await ViewModel.InstallArchivesAsync(kept);
    }

    private static string[] KeepDroppedTempFiles(string[] files)
    {
        var temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        var kept = System.IO.Path.Combine(temp, "SSPTMM-dropped");

        // Copies from earlier drops, installed (or not) long since.
        try
        {
            if (System.IO.Directory.Exists(kept))
            {
                foreach (var old in System.IO.Directory.EnumerateDirectories(kept)
                             .Where(d => System.IO.Directory.GetCreationTimeUtc(d) < DateTime.UtcNow.AddDays(-1)))
                {
                    System.IO.Directory.Delete(old, recursive: true);
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            TCFModManager.Core.Services.AppLog.Debug("Install", $"couldn't clear old dropped copies: {ex.Message}");
        }

        return [.. files.Select(file =>
        {
            try
            {
                if (!InstalledViewModel.IsModArchive(file) || !System.IO.File.Exists(file)
                    || !System.IO.Path.GetFullPath(file).StartsWith(temp, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }

                var folder = System.IO.Path.Combine(kept, Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(folder);
                var copy = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(file));
                System.IO.File.Copy(file, copy);
                return copy;
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                TCFModManager.Core.Services.AppLog.Warn("Install", $"couldn't keep a copy of the dropped {System.IO.Path.GetFileName(file)}: {ex.Message}");
                return file;
            }
        })];
    }

    // Over the view on screen, while its sections are your groups - the only place a mod is dropped.
    private bool OverGroupList(DragEventArgs e)
    {
        if (!ViewModel.SectionsTakeDrops || ActiveScroller is not { IsVisible: true } scroller) return false;

        var point = e.GetPosition(scroller);
        return point.X >= 0 && point.Y >= 0 && point.X <= scroller.ActualWidth && point.Y <= scroller.ActualHeight;
    }

    // The scrolling list of whichever view is on screen.
    private ScrollViewer? ActiveScroller => ViewModel.ViewMode switch
    {
        InstalledViewMode.Groups => GroupsScrollViewer,
        InstalledViewMode.List => ListScrollViewer,
        InstalledViewMode.Cards => CardsScrollViewer,
        _ => null,
    };

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
    // Fork (SSPTMM, UI tidy-up 4): picking as Steam's library does. Ctrl+click adds or drops this
    // card, Shift+click takes the run from the last one picked to this one; either is marked
    // handled so the card doesn't also open. A plain click falls through to the expander, which is
    // what opens the card - and, in sections of your groups, may start a drag (Card_PreviewMouseMove).
    // A button inside the card (the switch, Update, the tick box) still does its own job.
    //
    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        _dragStarted = false;

        if (sender is not FrameworkElement { DataContext: InstalledModCardViewModel mod }) return;
        if (IsInsideButton(e.OriginalSource, sender as DependencyObject)) return;

        if (TryPick(mod))
        {
            e.Handled = true;
            return;
        }

        // In sections of your groups a card or row can be dragged to another group, as a Groups
        // view row can. Only noted here: the click still goes on to open or close it, and only a
        // move past the drag distance (Card_PreviewMouseMove) makes it a drag.
        if (ViewModel.SectionsTakeDrops)
        {
            _dragStart = e.GetPosition(null);
            _dragCandidate = mod;
        }
    }

    // Ctrl+click or Shift+click: picked, and true. Any other click: false, and nothing done.
    private bool TryPick(InstalledModCardViewModel mod)
    {
        var keys = Keyboard.Modifiers;
        if ((keys & ModifierKeys.Shift) != 0)
        {
            ViewModel.SelectRange(mod);
            return true;
        }

        if ((keys & ModifierKeys.Control) != 0)
        {
            ViewModel.ToggleSelected(mod);
            return true;
        }

        return false;
    }

    // The tick box ticks through its own binding; this makes it the mod Shift+click counts from.
    private void PickTick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InstalledModCardViewModel mod }) ViewModel.NoteAnchor(mod);
    }

    //
    // Esc drops every pick; Ctrl+A picks every mod the filters show. Neither while typing - in the
    // search box or a group's name, Ctrl+A selects the text and Esc is the box's own.
    //
    private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase) return;

        if (e.Key == Key.Escape && ViewModel.HasAnySelection)
        {
            ViewModel.ClearSelectionCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ViewModel.SelectAllCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null || _dragStarted) return;
        if (!ViewModel.SectionsTakeDrops || !PastDragDistance(e)) return;

        // Taken before letting go of the mouse: that raises a mouse move of its own, straight back
        // into this handler, which must find nothing left to drag.
        var mod = _dragCandidate;
        _dragCandidate = null;
        _dragStarted = true;

        // The expander's header button took the mouse when it was pressed; let it go without a click,
        // so the card is not opened or closed as well when the drag ends.
        Mouse.Captured?.ReleaseMouseCapture();

        if (sender is DependencyObject source) DragMod(source, mod);
    }

    private void Card_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        _dragStarted = false;
    }

    private void ResultsItems_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.UpdateLayoutForWidth(e.NewSize.Width);
    }

    private void CardsView_Click(object sender, RoutedEventArgs e) => ViewModel.ViewMode = InstalledViewMode.Cards;

    private void GroupsView_Click(object sender, RoutedEventArgs e) => ViewModel.ViewMode = InstalledViewMode.Groups;

    private void ListView_Click(object sender, RoutedEventArgs e) => ViewModel.ViewMode = InstalledViewMode.List;

    //
    // Undo a removal: with one held it is undone straight away; with several, a menu of them (newest
    // first) lets the user pick which one comes back.
    //
    private void UndoRemoval_Click(object sender, RoutedEventArgs e)
    {
        var command = ViewModel.UndoRemovalCommand;
        if (command.IsRunning) return;

        var held = ViewModel.HeldRemovals;
        if (held.Count <= 1)
        {
            command.Execute(null);
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = PlacementMode.Bottom,
        };

        foreach (var item in held)
            menu.Items.Add(new MenuItem { Header = item.Label, Command = command, CommandParameter = item.Folder });

        menu.IsOpen = true;
    }

    //
    // Fork (SSPTMM, UI tidy-up 2): the toolbar's "..." menu - the page actions used now and then
    // rather than every visit, which each had a button of their own on the toolbar before. Built on
    // each click, so every entry reads the page as it is right now (Multi select and List badges
    // show a tick when on; open/close all only in the two views they work in).
    //
    private void More_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = PlacementMode.Bottom,
            DataContext = vm,
        };

        menu.Items.Add(new MenuItem
        {
            Header = Strings.Installed_InstallFromFile,
            ToolTip = Strings.Installed_InstallFromFileToolTip,
            Command = vm.InstallFromFileCommand,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Upgrade_Button,
            ToolTip = Strings.Upgrade_ButtonToolTip,
            Command = vm.CheckSptUpgradeCommand,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Common_Rescan,
            ToolTip = Strings.Installed_RescanToolTip,
            Command = vm.ScanCommand,
        });

        menu.Items.Add(new Separator());

        if (vm.ShowExpanders)
        {
            menu.Items.Add(new MenuItem { Header = Strings.Installed_ExpandAllToolTip, Command = vm.ExpandAllCommand });
            menu.Items.Add(new MenuItem { Header = Strings.Installed_CollapseAllToolTip, Command = vm.CollapseAllCommand });
        }

        menu.Items.Add(new Separator());

        var badges = new MenuItem
        {
            Header = Strings.Installed_ListBadges,
            ToolTip = Strings.Installed_ListBadgesToolTip,
            IsCheckable = true,
        };
        badges.SetBinding(MenuItem.IsCheckedProperty, new Binding(nameof(InstalledViewModel.ShowListBadges)) { Mode = BindingMode.TwoWay });
        menu.Items.Add(badges);

        // Each entry's explanation opens to the left of the menu, never over it: seen under Wine,
        // the first entry's tooltip came up as the menu opened and stayed, covering the entries
        // under it.
        foreach (var entry in menu.Items.OfType<MenuItem>())
            ToolTipService.SetPlacement(entry, PlacementMode.Left);

        menu.IsOpen = true;
    }

    //
    // Fork (1.2.0): the Presets menu - each saved preset (ticked when the mods are set as it has them)
    // with Apply, Save over, Rename and Delete under it; then Save the current setup, Put back, and
    // Disable all / Enable all. Built on each click, so it always reads the presets as they are.
    //
    private void Presets_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = PlacementMode.Bottom,
            DataContext = vm,
        };

        var presets = vm.Presets;
        var current = vm.CurrentPresetName;

        if (presets.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = Strings.Presets_None, IsEnabled = false });
        }

        foreach (var preset in presets)
        {
            var name = preset.Name;
            var isCurrent = string.Equals(name, current, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem
            {
                Header = name,
                // A tick for the preset the mods are set to: a menu item that has a submenu draws no
                // check mark of its own, so it is the item's icon.
                Icon = isCurrent ? new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Checkmark16 } : null,
            };

            // What a tooltip would say, as the submenu's first, greyed line: the submenu opens on the
            // side a tooltip would, and covered it.
            item.Items.Add(new MenuItem
            {
                Header = isCurrent
                    ? Strings.Presets_CurrentToolTip
                    : LocalizationService.Text(Strings.Presets_SavedOnFormat, preset.SavedAt.ToLocalTime().ToString("g")),
                IsEnabled = false,
            });
            item.Items.Add(new Separator());
            item.Items.Add(Entry(Strings.Presets_Apply, () => vm.ApplyPresetAsync(name)));
            item.Items.Add(Entry(Strings.Presets_UpdateToCurrent, () => vm.UpdatePresetAsync(name)));
            item.Items.Add(new Separator());
            item.Items.Add(Entry(Strings.Presets_Rename, () => { vm.RenamePreset(name); return Task.CompletedTask; }));
            item.Items.Add(Entry(Strings.Presets_Delete, () => { vm.DeletePreset(name); return Task.CompletedTask; }));
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Strings.Presets_SaveCurrent, vm.SavePresetAsync, enabled: vm.HasModsForPresets));

        if (vm.BeforeLastPreset is { } before)
        {
            menu.Items.Add(Entry(
                LocalizationService.Text(Strings.Presets_PutBackFormat, before.Name),
                vm.PutBackBeforeLastPresetAsync,
                LocalizationService.Text(Strings.Presets_PutBackToolTipFormat, before.Name, before.SavedAt.ToLocalTime().ToString("g"))));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Strings.Presets_DisableAll, vm.DisableAllAsync, Strings.Presets_DisableAllToolTip, vm.HasModsForPresets));
        menu.Items.Add(Entry(Strings.Presets_EnableAll, vm.EnableAllAsync, Strings.Presets_EnableAllToolTip, vm.HasModsForPresets));

        foreach (var entry in menu.Items.OfType<MenuItem>().Where(m => m.ToolTip is not null))
            ToolTipService.SetPlacement(entry, PlacementMode.Left);

        menu.IsOpen = true;

        // Each action runs once the menu has closed, so its dialog never opens over a menu that is still
        // on its way out.
        MenuItem Entry(string header, Func<Task> run, string? toolTip = null, bool enabled = true)
        {
            var item = new MenuItem { Header = header, ToolTip = toolTip, IsEnabled = enabled };
            item.Click += (_, args) =>
            {
                args.Handled = true;
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () => await run()));
            };
            return item;
        }
    }

    // Fork: the empty state's way on - the Workshop, where mods are subscribed to.
    private void BrowseWorkshop_Click(object sender, RoutedEventArgs e) => AppNavigation.Navigate(typeof(WorkshopHomePage));

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

        // Fork: Ctrl+click and Shift+click pick, as on the cards - no details, no drag.
        if (sender is FrameworkElement { DataContext: InstalledModCardViewModel picked } && TryPick(picked))
        {
            _dragCandidate = null;
            _dragStarted = false;
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

        if (!PastDragDistance(e)) return;

        // Marks this gesture as a drag rather than clearing _dragCandidate outright, so the
        // ButtonUp handler below can tell "this mouse-down turned into a drag" (skip opening
        // details) apart from "this mouse-down never moved" (a plain click).
        _dragStarted = true;
        if (sender is DependencyObject source) DragMod(source, _dragCandidate);
    }

    private bool PastDragDistance(MouseEventArgs e)
    {
        var current = e.GetPosition(null);
        return Math.Abs(current.X - _dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(current.Y - _dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    // A mod dragged from a Groups view row, or from a card or List row in sections of your groups.
    private void DragMod(DependencyObject source, InstalledModCardViewModel mod)
    {
        // Blocks for the whole drag (DoDragDrop runs its own modal message loop), so this is also
        // the one place guaranteed to run once the gesture is over however it ended - dropped on a
        // group, dropped in the gutter, dropped outside the window, or cancelled with Escape.
        // Stopping the auto-scroll timer here means every other stop path below is just a courtesy,
        // not load-bearing.
        HookDragWheel();
        try
        {
            DragDrop.DoDragDrop(source, mod, DragDropEffects.Move);
        }
        finally
        {
            UnhookDragWheel();
            StopDragScroll();
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

        // Only a mod being moved scrolls the list; a file dragged in from Explorer installs wherever
        // it is dropped and has no reason to.
        if (sender is not ScrollViewer scroller || !e.Data.GetDataPresent(typeof(InstalledModCardViewModel))) return;

        var y = e.GetPosition(scroller).Y;
        var height = scroller.ViewportHeight;
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
        if (_dragScrollStep == 0 || ActiveScroller is not { } scroller) return;
        scroller.ScrollToVerticalOffset(scroller.VerticalOffset + _dragScrollStep);
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
        if (ActiveScroller is { } scroller) scroller.ScrollToVerticalOffset(scroller.VerticalOffset - delta);

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

    // Rename puts the name in a box ready to type over, rather than leaving the box to be clicked
    // first - typing straight after the pencil otherwise went nowhere, and Escape did nothing.
    private void RenameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;

        // After layout: a box that has only just become visible cannot take focus yet.
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Input);
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
