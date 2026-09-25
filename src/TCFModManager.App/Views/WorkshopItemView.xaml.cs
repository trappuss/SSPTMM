using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TCFModManager.App.Services;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Views;

//
// The Workshop item page. MainWindow shows it for every mod opened anywhere in the app; a required
// item opened from it replaces it, and Esc (or the mouse's back button, or Alt+Left) walks back
// through those one at a time before closing.
//
public partial class WorkshopItemView : UserControl
{
    // The item pages this one was opened over, most recent last.
    private readonly Stack<ModDetailsRequest> _history = new();

    private ModDetailsRequest? _current;

    private WorkshopItemViewModel? _viewModel;

    // sp-mod.com's comments, created the first time the Comments tab is opened and reused after.
    private WebView2? _comments;

    public WorkshopItemView()
    {
        InitializeComponent();

        Scroll.SizeChanged += (_, _) => SizeComments();

        // The mouse's back button, anywhere on the page.
        MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.XButton1) return;
            e.Handled = true;
            GoBack();
        };
    }

    public void Show(ModDetailsRequest request)
    {
        if (IsVisible && _current is not null) _history.Push(_current);

        Open(request);
    }

    public void Close()
    {
        _history.Clear();
        _current = null;

        Detach();
        DataContext = null;
        Visibility = Visibility.Collapsed;
        HideComments();
    }

    private void Detach()
    {
        if (_viewModel is null) return;

        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.Detach();
        _viewModel = null;
    }

    private void Open(ModDetailsRequest request)
    {
        Detach();

        _current = request;
        _viewModel = new WorkshopItemViewModel(request);
        _viewModel.PropertyChanged += OnViewModelChanged;
        DataContext = _viewModel;

        HideComments();
        Visibility = Visibility.Visible;
        Scroll.ScrollToTop();
        Strip.ScrollToLeftEnd();
        SyncPreviewStretch();

        // Focus so Esc reaches this rather than the page underneath.
        Focus();

        _ = _viewModel.LoadAsync();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // A tab switch is a new page on Steam; start it at the top.
            case nameof(WorkshopItemViewModel.Tab):
                Scroll.ScrollToTop();
                if (_viewModel?.IsCommentsShown == true) _ = ShowCommentsAsync();
                else HideComments();
                break;

            case nameof(WorkshopItemViewModel.SelectedMedia):
                SyncPreviewStretch();
                break;
        }
    }

    private void GoBack()
    {
        if (_history.Count > 0) Open(_history.Pop());
        else Close();
    }

    private void View_KeyDown(object sender, KeyEventArgs e)
    {
        var back = e.Key == Key.Escape || (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) ||
                   (e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt);
        if (!back) return;

        e.Handled = true;
        GoBack();
    }

    // The first two breadcrumbs lead back to the Workshop page this was opened from.
    private void Workshop_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- preview and strip

    // The mod's own picture and a video's thumbnail fill the preview, as before; a picture from the
    // description is never blown up past its own size (a badge would turn to mush).
    private void SyncPreviewStretch()
    {
        var media = _viewModel?.SelectedMedia;
        var first = _viewModel is not null && media is not null && ReferenceEquals(_viewModel.Gallery.FirstOrDefault(), media);
        PreviewPicture.StretchDirection = first || _viewModel?.SelectedIsVideo == true
            ? StretchDirection.Both
            : StretchDirection.DownOnly;
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel?.OpenSelectedMediaCommand.Execute(null);
    }

    // The slider under the strip mirrors the strip's own horizontal scroll, and drives it.
    private void Strip_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var overflow = Math.Max(0, Strip.ExtentWidth - Strip.ViewportWidth);
        StripSlider.Visibility = overflow > 0 ? Visibility.Visible : Visibility.Collapsed;
        StripSlider.Minimum = 0;
        StripSlider.Maximum = overflow;
        StripSlider.ViewportSize = Strip.ViewportWidth;
        StripSlider.Value = Strip.HorizontalOffset;
    }

    private void StripSlider_Scroll(object sender, ScrollEventArgs e) => Strip.ScrollToHorizontalOffset(e.NewValue);

    // ---------------------------------------------------------------- comments

    //
    // The comments are sp-mod.com's own, in a web view: the site loads them with its own scripts
    // and there is no API for them, so this shows the real thing rather than a copy that could not
    // post and would break whenever the site changed. Everything on the page but the comments is
    // hidden once it has loaded.
    //
    // A web view is a window of its own laid over the app, which a scrolling page cannot clip, so
    // on this tab the page stops scrolling and the comments fill the space under the tabs.
    //
    private async Task ShowCommentsAsync()
    {
        if (_viewModel?.CommentsUrl is not { } url) return;

        Scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

        if (!WebViews.IsAvailable)
        {
            ShowCommentsFallback();
            return;
        }

        if (_comments is null)
        {
            _comments = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1B, 0x28, 0x38) };
            CommentsHost.Child = _comments;

            var started = await WebViews.InitializeAsync(_comments);
            if (!started)
            {
                CommentsHost.Child = null;
                _comments = null;
                if (StillWants(url)) ShowCommentsFallback();
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

                    // The mod's own page with no comments section: say so, rather than showing the
                    // whole page under a Comments tab. (Any other page - the sign-in page, say -
                    // is left as it is.)
                    if (result == "\"none\"" && StillWants(core.Source)) ShowCommentsOff();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    AppLog.Debug("Comments", $"could not trim the page: {ex.Message}");
                }
            };
        }

        // The first start of the web view takes a moment, in which another item may have been
        // opened or another tab chosen: these comments are then no longer wanted.
        if (!StillWants(url)) return;

        CommentsFallback.Visibility = Visibility.Collapsed;
        CommentsOff.Visibility = Visibility.Collapsed;
        CommentsHost.Visibility = Visibility.Visible;
        SizeComments();

        if (_comments.CoreWebView2 is { } web && !string.Equals(web.Source, url, StringComparison.OrdinalIgnoreCase))
            web.Navigate(url);
    }

    private bool StillWants(string url) =>
        _viewModel is { IsCommentsShown: true } current
        && string.Equals(current.CommentsUrl, url, StringComparison.OrdinalIgnoreCase);

    private void ShowCommentsFallback()
    {
        CommentsHost.Visibility = Visibility.Collapsed;
        CommentsOff.Visibility = Visibility.Collapsed;
        CommentsFallback.Visibility = Visibility.Visible;
    }

    private void ShowCommentsOff()
    {
        CommentsHost.Visibility = Visibility.Collapsed;
        CommentsFallback.Visibility = Visibility.Collapsed;
        CommentsOff.Visibility = Visibility.Visible;
        Scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    private void HideComments()
    {
        CommentsHost.Visibility = Visibility.Collapsed;
        CommentsFallback.Visibility = Visibility.Collapsed;
        CommentsOff.Visibility = Visibility.Collapsed;
        Scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    // Down to the bottom of the page area, from wherever the comments start.
    private void SizeComments()
    {
        if (CommentsHost.Visibility != Visibility.Visible || !IsLoaded) return;

        Dispatcher.BeginInvoke(() =>
        {
            if (CommentsHost.Visibility != Visibility.Visible) return;
            var top = CommentsHost.TranslatePoint(new Point(0, 0), Scroll).Y;
            CommentsHost.Height = Math.Max(200, Scroll.ActualHeight - top - 24);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OpenComments_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.CommentsUrl is { } url) MarkupActions.OpenInBrowser(url);
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
