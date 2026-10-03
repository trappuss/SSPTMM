using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM): the Per page each page opens at.
//
// Every page with a Per page opens on Infinite until a size is picked there. The size picked is
// kept straight away (AppSettings.PageSizes), with no Save as default needed, and the page opens
// on it from then on. Clear filters leaves it alone: it is how the page is shown, not what it
// shows.
//
// A size saved with the upstream app's Save as default is still honoured on a page where nothing
// has been picked since, because that was also a size someone chose.
//
public static class PageSizeMemory
{
    /// <summary>0, the "Infinite" entry on every Per page.</summary>
    public const int Infinite = 0;

    // The page names the sizes are kept under. Fixed strings, not type names, so renaming a class
    // never loses anyone's choice.
    public const string Browse = "Browse";
    public const string SubscribedItems = "SubscribedItems";
    public const string CollectionsBrowse = "CollectionsBrowse";
    public const string AuthorItems = "AuthorItems";

    //
    // The size to open at: the one last picked on this page, else one saved as this page's default,
    // else Infinite. A size the page no longer offers (an older build's list, a hand-edited
    // settings.json) is passed over, so the dropdown never shows blank.
    //
    public static int Resolve(AppSettings settings, string page, int? savedDefault, IReadOnlyCollection<int> options)
    {
        if (settings.PageSizes.TryGetValue(page, out var picked) && options.Contains(picked)) return picked;
        if (savedDefault is { } saved && options.Contains(saved)) return saved;
        return Infinite;
    }

    // Keeps the size picked. Returns false when there was nothing to change, so nothing is written.
    public static bool Remember(AppSettings settings, string page, int size)
    {
        if (settings.PageSizes.TryGetValue(page, out var current) && current == size) return false;
        settings.PageSizes[page] = size;
        return true;
    }

    // Reads, changes and writes settings.json in one go, as the app's other display choices are kept.
    public static void Save(string page, int size)
    {
        var service = new SettingsService();
        var settings = service.Load();
        if (Remember(settings, page, size)) service.Save(settings);
    }

    public static int Load(string page, int? savedDefault, IReadOnlyCollection<int> options) =>
        Resolve(new SettingsService().Load(), page, savedDefault, options);
}
