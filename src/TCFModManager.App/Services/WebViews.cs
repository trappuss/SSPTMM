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

    /// <summary>True for addresses on sp-mod.com itself, which a web view here may show. Anything
    /// else a page links to opens in the browser.</summary>
    public static bool IsSpModPage(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("sp-mod.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".sp-mod.com", StringComparison.OrdinalIgnoreCase));
}
