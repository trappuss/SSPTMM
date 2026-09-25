using System.Windows;
using Microsoft.Web.WebView2.Core;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

//
// Shows one or more sp-mod.com mod pages in the app, in order. Each page shown counts as opened
// for the gate that asked - the same thing a click on "Open page" meant when it opened a browser.
//
public partial class ModPageWindow : FluentWindow
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly IReadOnlyList<ModPageLink> _links;
    private int _index;

    private ModPageWindow(IReadOnlyList<ModPageLink> links, int start)
    {
        _links = links;
        _index = start;
        InitializeComponent();

        Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1B, 0x28, 0x38);

        // Until the web view has started there is no page to step from: the steps appear with
        // the first one (ShowPage), and the way out is Close.
        PreviousButton.Visibility = NextButton.Visibility = Visibility.Collapsed;
        DoneButton.Content = Strings.ModPage_Close;

        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => _closed = true;
    }

    /// <summary>Shows <paramref name="links"/> (those with a page) over <paramref name="owner"/>
    /// until closed. False when the app has no web view to show them with, in which case nothing
    /// was shown and the caller opens the browser instead. True once the window has been shown,
    /// even if it was closed before a page appeared: a page not shown stays unread for the gate,
    /// and opening them all in the browser as well would be a second answer to one click.</summary>
    public static bool Show(Window? owner, IReadOnlyList<ModPageLink> links)
    {
        var withPages = links.Where(l => l.HasUrl).ToList();
        if (withPages.Count == 0 || !WebViews.IsAvailable) return false;

        var window = new ModPageWindow(withPages, 0) { Owner = owner };
        window.ShowDialog();
        return true;
    }

    private bool _closed;

    private async Task StartAsync()
    {
        var started = await WebViews.InitializeAsync(Browser);

        // Closed while the web view was starting: nothing more to show, and nowhere to show it.
        if (_closed) return;

        if (!started)
        {
            // The runtime was there but would not start: hand the pages to the browser instead.
            AppLog.Warn("ModPages", "web view would not start; opening the page(s) in the browser");
            foreach (var link in _links) MarkupActions.OpenInBrowser(link.Url!);
            foreach (var link in _links) link.IsOpened = true;
            Close();
            return;
        }

        var core = Browser.CoreWebView2;
        core.NewWindowRequested += OnNewWindowRequested;
        core.NavigationStarting += OnNavigationStarting;

        ShowPage();
    }

    private void ShowPage()
    {
        var link = _links[_index];
        link.IsOpened = true;

        Title = WindowTitleBar.Title = link.Name;
        Heading.Text = _links.Count == 1
            ? link.Name
            : Text(Strings.ModPage_StepFormat, _index + 1, _links.Count, link.Name);

        PreviousButton.Visibility = _links.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = PreviousButton.Visibility;
        PreviousButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index < _links.Count - 1;
        DoneButton.Content = _index < _links.Count - 1 && _links.Any(l => !l.IsOpened)
            ? Strings.ModPage_Close
            : Strings.ModPage_Done;

        Browser.CoreWebView2?.Navigate(link.Url!);
    }

    // The site's own pages (a linked mod, the sign-in page) stay here; anywhere else goes to the browser.
    private static void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || WebViews.IsSpModPage(uri) || uri.Scheme == "about") return;

        e.Cancel = true;
        MarkupActions.OpenInBrowser(e.Uri);
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        MarkupActions.OpenInBrowser(e.Uri);
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_index == 0) return;
        _index--;
        ShowPage();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_index >= _links.Count - 1) return;
        _index++;
        ShowPage();
    }

    private void BrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.Source is { } source) MarkupActions.OpenInBrowser(source);
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e) => Close();
}
