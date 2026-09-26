using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// A Comments tab: sp-mod.com's own comments for a mod's or a collection's page, in a web view. The
// site loads them with its own scripts and there is no API for them, so this shows the real thing
// rather than a copy that could not post and would break whenever the site changed. Everything on
// the page but the comments is hidden once it has loaded.
//
// A web view is a window of its own laid over the app, which a scrolling page cannot clip, so on
// this tab the page stops scrolling and the comments fill the space under the tabs.
//
// Used by the item page and the collection page; each gives it the elements it shows - the host for
// the web view, the line for a page with no comments, and the fallback without a web view - and
// asks, through wantedUrl, which comments its page wants right now (null: none).
//
internal sealed class SpModComments
{
    private readonly ScrollViewer _scroll;
    private readonly Border _host;
    private readonly FrameworkElement _off;
    private readonly FrameworkElement _fallback;
    private readonly Func<string?> _wantedUrl;

    // Created the first time the tab is opened and reused after.
    private WebView2? _comments;

    // The comments page last asked for, and those found to have no comments section this session.
    private string? _requested;
    private readonly HashSet<string> _none = new(StringComparer.OrdinalIgnoreCase);

    public SpModComments(ScrollViewer scroll, Border host, FrameworkElement off, FrameworkElement fallback, Func<string?> wantedUrl)
    {
        _scroll = scroll;
        _host = host;
        _off = off;
        _fallback = fallback;
        _wantedUrl = wantedUrl;

        _scroll.SizeChanged += (_, _) => Size();
    }

    public async Task ShowAsync()
    {
        if (_wantedUrl() is not { } url) return;

        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

        if (!WebViews.IsAvailable)
        {
            ShowFallback();
            return;
        }

        if (_comments is null)
        {
            _comments = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1B, 0x28, 0x38) };
            _host.Child = _comments;

            var started = await WebViews.InitializeAsync(_comments);
            if (!started)
            {
                _host.Child = null;
                _comments = null;
                if (StillWants(url)) ShowFallback();
                return;
            }

            var core = _comments.CoreWebView2;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                MarkupActions.OpenInBrowser(e.Uri);
            };
            core.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && !WebViews.IsSpModPage(uri) && uri.Scheme != "about")
                {
                    e.Cancel = true;
                    MarkupActions.OpenInBrowser(e.Uri);
                }
            };
            core.DOMContentLoaded += async (_, _) =>
            {
                try
                {
                    var result = await core.ExecuteScriptAsync(CommentsOnlyScript);

                    // The page with no comments section: say so, rather than showing the whole page
                    // under a Comments tab - now and on every later visit to the tab, when the page
                    // is not loaded again. Only the page asked for counts (matched by its id, so a
                    // redirect to a new slug still does); any other page - the sign-in page, another
                    // mod followed from a link - is left as it is.
                    if (result == "\"none\"" && _requested is { } requested
                        && PageId(core.Source) is { } shown && shown == PageId(requested))
                    {
                        _none.Add(requested);
                        if (StillWants(requested)) ShowOff();
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    AppLog.Debug("Comments", $"could not trim the page: {ex.Message}");
                }
            };
        }

        // The first start of the web view takes a moment, in which another page may have been
        // opened or another tab chosen: these comments are then no longer wanted.
        if (!StillWants(url)) return;

        if (_none.Contains(url))
        {
            ShowOff();
            return;
        }

        _fallback.Visibility = Visibility.Collapsed;
        _off.Visibility = Visibility.Collapsed;
        _host.Visibility = Visibility.Visible;
        Size();

        if (_comments.CoreWebView2 is { } web && !string.Equals(web.Source, url, StringComparison.OrdinalIgnoreCase))
        {
            _requested = url;
            web.Navigate(url);
        }
    }

    public void Hide()
    {
        _host.Visibility = Visibility.Collapsed;
        _fallback.Visibility = Visibility.Collapsed;
        _off.Visibility = Visibility.Collapsed;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    // "mod/791" from https://sp-mod.com/mod/791/sain#comments, "list/12" from a list's page; null
    // for any other page.
    internal static string? PageId(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var match = Regex.Match(uri.AbsolutePath, @"^/(mod|list)/(\d+)(/|$)");
        return match.Success ? match.Groups[1].Value + "/" + match.Groups[2].Value : null;
    }

    private bool StillWants(string url) => string.Equals(_wantedUrl(), url, StringComparison.OrdinalIgnoreCase);

    private void ShowFallback()
    {
        _host.Visibility = Visibility.Collapsed;
        _off.Visibility = Visibility.Collapsed;
        _fallback.Visibility = Visibility.Visible;
    }

    private void ShowOff()
    {
        _host.Visibility = Visibility.Collapsed;
        _fallback.Visibility = Visibility.Collapsed;
        _off.Visibility = Visibility.Visible;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    // Down to the bottom of the page area, from wherever the comments start.
    private void Size()
    {
        if (_host.Visibility != Visibility.Visible || !_host.IsLoaded) return;

        _host.Dispatcher.BeginInvoke(() =>
        {
            if (_host.Visibility != Visibility.Visible) return;
            var top = _host.TranslatePoint(new Point(0, 0), _scroll).Y;
            _host.Height = Math.Max(200, _scroll.ActualHeight - top - 24);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    //
    // Keeps the comments and nothing else. From the comments section up to the page body, every
    // other element beside the path is hidden (hidden, not removed: the site's own scripts keep
    // working on them) and each container on the path is told to use the full width. Colours
    // are moved to the Workshop page's own. A page with no comments section answers "none", and
    // the tab then says so instead of showing it (SAIN's has none; sp-mod.com then shows no
    // Comments tab at all).
    //
    private const string CommentsOnlyScript = """
        (() => {
          const comments = document.getElementById('comments');
          if (!comments) return 'none';
          let node = comments;
          while (node && node !== document.body) {
            const parent = node.parentElement;
            if (!parent) break;
            for (const sibling of parent.children) {
              if (sibling !== node && !['SCRIPT', 'STYLE', 'LINK', 'TEMPLATE'].includes(sibling.tagName)) sibling.style.display = 'none';
            }
            parent.style.maxWidth = 'none';
            parent.style.width = '100%';
            parent.style.margin = '0';
            parent.style.padding = '0';
            parent.style.gridColumn = '1 / -1';
            parent.style.background = 'transparent';
            node = parent;
          }
          comments.style.display = '';
          const style = document.createElement('style');
          style.textContent = 'html, body { background: #1B2838 !important; } body { padding: 0 4px 16px !important; }';
          document.head.appendChild(style);
          window.scrollTo(0, 0);
          return 'shown';
        })();
        """;
}
