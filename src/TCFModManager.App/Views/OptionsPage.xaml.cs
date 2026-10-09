using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class OptionsPage : Page
{
    public OptionsViewModel ViewModel { get; } = new();

    public OptionsPage()
    {
        DataContext = ViewModel;
        InitializeComponent();
    }

    // Same reason as ServerMapPage: the key file can appear or change while this app is open, and
    // the section that shows it is right here.
    // The held size too, since a removal or Undo elsewhere changes it. And the unsubscribe question's
    // "Don't ask again" can have changed its switch since this page was made.
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AppServices.ServerMap.RefreshLocalKey();
        ViewModel.Reload();
        ViewModel.Profiles.Refresh();
        ViewModel.RefreshRemovedModsSize();
    }

    private void DataFiles_Click(object sender, RoutedEventArgs e) =>
        new DataFilesWindow { Owner = Window.GetWindow(this) }.Show();

    // ------------------------------------------------------------------ fork: the list of sections

    // A click on a section in the list: scrolled to, its heading at the top.
    private void SectionNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string name } item || FindName(name) is not FrameworkElement section) return;

        // Where this will actually land: the last sections are too near the end to reach the top.
        _clicked = item;
        _clickedOffset = Math.Min(TopOf(section), SettingsScroll.ScrollableHeight);

        SettingsScroll.ScrollToVerticalOffset(_clickedOffset);
    }

    // The section last clicked and where the click scrolled to. While the view is still there the
    // mark stays on it, whatever is at the top.
    private RadioButton? _clicked;
    private double _clickedOffset;

    // Scrolling moves the mark in the list to the section now at the top of the view - or, with the
    // page at its end, to the last one, which is too short to ever reach the top.
    private void SettingsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0) return;

        if (_clicked is not null && Math.Abs(SettingsScroll.VerticalOffset - _clickedOffset) < 1.5)
        {
            if (_clicked.IsChecked != true) _clicked.IsChecked = true;
            return;
        }

        _clicked = null;

        RadioButton? current = null;
        var atEnd = SettingsScroll.ScrollableHeight > 0 && SettingsScroll.VerticalOffset >= SettingsScroll.ScrollableHeight - 1;

        foreach (var item in SectionNav.Children.OfType<RadioButton>())
        {
            if (item.Tag is not string name || FindName(name) is not FrameworkElement section) continue;

            // A little before its heading reaches the top, so a click that lands a section there
            // marks that one and not the one above it.
            if (atEnd || TopOf(section) <= SettingsScroll.VerticalOffset + 24) current = item;
        }

        if (current is { IsChecked: not true }) current.IsChecked = true;
    }

    private double TopOf(FrameworkElement section) =>
        section.TransformToAncestor(SettingsStack).Transform(new Point(0, 0)).Y;
}
