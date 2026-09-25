using System.Diagnostics;
using System.Text.RegularExpressions;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Markup;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What a click inside a rendered description does: a link to another mod on sp-mod.com opens that
// mod's item page here, a picture opens full size in the viewer, a video plays in the viewer, and
// any other link goes to the browser as before.
//
public static class MarkupActions
{
    // sp-mod.com/mod/<id>/<slug>, with or without the slug and the www.
    private static readonly Regex ModLink = new(
        @"^https?://(www\.)?sp-mod\.com/mod/(?<id>\d+)(/|$|\?|#)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Hosts whose links are the picture itself rather than a page about it.
    private static readonly Regex PictureLink = new(
        @"\.(png|jpe?g|gif|webp|bmp)(\?|#|$)|^https?://i\.imgur\.com/", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void OpenLink(string url)
    {
        var match = ModLink.Match(url);

        // Browse's catalog rather than the raw cache, so a link to this app's own listing goes to
        // the browser instead of an item page with a Subscribe button - see BrowseViewModel.IsSelf.
        // Not while the update dialog is open, either: the item page would open underneath it.
        if (match.Success && int.TryParse(match.Groups["id"].Value, out var id)
            && !AppServices.ModUpdateOverlay.IsOpen
            && AppServices.Browse.FindInCatalog(id) is { } mod)
        {
            _ = OpenItemPageAsync(mod, url);
            return;
        }

        OpenInBrowser(url);
    }

    // The item page, or the link in the browser when the page could not be loaded (offline, rate
    // limited): a click inside a description has no status line of its own to explain a failure in.
    private static async Task OpenItemPageAsync(Core.Models.Mod mod, string url)
    {
        if (await AppServices.Browse.LoadDetailsAsync(mod) is not null) OpenInBrowser(url);
    }

    public static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("Links", $"could not open {url}: {ex.Message}");
        }
    }

    /// <summary>True when a link around a picture points at a picture too, so a click should show
    /// it full size rather than leave the app.</summary>
    public static bool IsPictureLink(string? href, string imageSource) =>
        href is null
        || string.Equals(StripQuery(href), StripQuery(imageSource), StringComparison.OrdinalIgnoreCase)
        || PictureLink.IsMatch(href);

    public static void ShowPicture(IReadOnlyList<MarkupMedia> gallery, string url) =>
        AppServices.MediaViewer.Show(gallery, url);

    public static void PlayVideo(IReadOnlyList<MarkupMedia> gallery, MarkupVideo video) =>
        AppServices.MediaViewer.Show(gallery, video.WatchUrl);

    private static string StripQuery(string url)
    {
        var cut = url.IndexOfAny(['?', '#']);
        return cut < 0 ? url : url[..cut];
    }
}
