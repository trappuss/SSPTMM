using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The one WebView2 environment every embedded page shares: sp-mod.com's comments, a mod's page read
// before it downloads, and a video playing in the viewer.
//
// Its profile (cookies - so an sp-mod.com sign-in lasts) lives in Data\WebView2 beside the exe,
// with the rest of this app's data. Everything that uses a web view asks IsAvailable first and
// falls back to the browser without one: the WebView2 runtime ships with Windows 11 and current
// Windows 10, but can be missing on an old or stripped-down install.
//
public static class WebViews
{
    private static bool? _available;
    private static Task<CoreWebView2Environment>? _environment;

    public static bool IsAvailable
    {
        get
        {
            if (_available is { } known) return known;

            try
            {
                _available = !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or DllNotFoundException or FileNotFoundException or BadImageFormatException or System.Runtime.InteropServices.COMException)
            {
                AppLog.Info("WebView", $"no WebView2 runtime: {ex.Message}");
                _available = false;
            }

            return _available.Value;
        }
    }

    private static Task<CoreWebView2Environment> EnvironmentAsync() =>
        _environment ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: Path.Combine(AppPaths.DataDirectory, "WebView2"));

    /// <summary>Starts <paramref name="view"/> on the shared environment. False when it could not
    /// start, in which case the caller falls back to the browser.</summary>
    public static async Task<bool> InitializeAsync(WebView2 view)
    {
        if (!IsAvailable) return false;

        try
        {
            if (view.CoreWebView2 is null) await view.EnsureCoreWebView2Async(await EnvironmentAsync());
            return view.CoreWebView2 is not null;
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            AppLog.Warn("WebView", $"could not start a web view: {ex.Message}");
            return false;
        }
    }

    //
    // YouTube videos, played from a page of this app's own rather than by opening the embed address
    // directly.
    //
    // YouTube refuses an embed that arrives with no Referer - "Video player configuration error,
    // Error 153" - and a web view navigated straight to youtube-nocookie.com/embed/<id> sends none:
    // there is no page it was embedded from. So the viewer opens https://player.tcfmodmanager/,
    // which never reaches the network (ServeVideoPlayer answers it from here), and that page embeds
    // the video in an iframe the way a web page does, with this origin as the referrer. Reproduced
    // and checked in Chromium on 2026-09-25: the direct address shows Error 153, the same video
    // embedded from an https page loads its player.
    //
    private const string PlayerHost = "https://player.tcfmodmanager/";

    private static readonly System.Text.RegularExpressions.Regex VideoId =
        new(@"^[A-Za-z0-9_-]{11}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex EmbedId =
        new(@"/embed/(?<id>[A-Za-z0-9_-]{11})(?:[/?#]|$)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Where the viewer's web view plays <paramref name="embedUrl"/> (a YouTube embed
    /// address), or null when it names no video.</summary>
    public static string? PlayerUrl(string? embedUrl)
    {
        var match = EmbedId.Match(embedUrl ?? string.Empty);
        return match.Success ? PlayerHost + "?v=" + match.Groups["id"].Value : null;
    }

    /// <summary>Answers requests for the player page on <paramref name="core"/>. Call once per web
    /// view, before navigating it to a PlayerUrl.</summary>
    public static void ServeVideoPlayer(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter(PlayerHost + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (!e.Request.Uri.StartsWith(PlayerHost, StringComparison.OrdinalIgnoreCase)) return;

            // Only an 11-character video id goes into the page; nothing else from the address does.
            var id = System.Web.HttpUtility.ParseQueryString(new Uri(e.Request.Uri).Query)["v"];
            var html = id is not null && VideoId.IsMatch(id) ? PlayerPage(id) : "<!doctype html><title></title>";

            e.Response = core.Environment.CreateWebResourceResponse(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html)),
                200,
                "OK",
                "Content-Type: text/html; charset=utf-8");
        };
    }

    private static string PlayerPage(string id) => $$"""
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <meta name="referrer" content="strict-origin-when-cross-origin">
        <style>html, body { margin: 0; height: 100%; background: #000; overflow: hidden; } iframe { border: 0; width: 100%; height: 100%; }</style>
        </head>
        <body>
        <iframe src="https://www.youtube-nocookie.com/embed/{{id}}?autoplay=1&rel=0" allow="autoplay; encrypted-media; picture-in-picture; fullscreen" allowfullscreen referrerpolicy="strict-origin-when-cross-origin"></iframe>
        </body>
        </html>
        """;

    /// <summary>True for addresses on sp-mod.com itself, which a web view here may show. Anything
    /// else a page links to opens in the browser.</summary>
    public static bool IsSpModPage(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("sp-mod.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".sp-mod.com", StringComparison.OrdinalIgnoreCase));
}
