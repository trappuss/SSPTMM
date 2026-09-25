using TCFModManager.Core.Markup;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// A mod's pictures - its own, then every picture in its description - for the Browse hover popup's
// slideshow and for Quick View.
//
// The cached catalog carries no descriptions (sp-mod.com's /mods leaves them out), so the first
// ask for a mod costs one call for its description; the answer is kept for the session. A call
// that fails is not kept, so the next hover asks again.
//
public sealed class ModPictures
{
    // Only touched from the UI thread.
    private readonly Dictionary<int, Task<IReadOnlyList<string>>> _known = new();

    public Task<IReadOnlyList<string>> ForAsync(Mod mod)
    {
        if (_known.TryGetValue(mod.Id, out var known)) return known;

        // Kept while it runs, so a second hover joins it. (Had it finished already, it failed
        // before reaching the network, and is not worth keeping.)
        var loading = LoadAsync(mod);
        if (!loading.IsCompleted) _known[mod.Id] = loading;
        return loading;
    }

    private async Task<IReadOnlyList<string>> LoadAsync(Mod mod)
    {
        var pictures = new List<string>();
        if (!string.IsNullOrWhiteSpace(mod.Thumbnail)) pictures.Add(mod.Thumbnail);

        try
        {
            var details = await AppServices.SpModApi.GetModAsync(mod.Id.ToString(), fields: "id,description");
            if (!string.IsNullOrWhiteSpace(details.Description))
            {
                var document = SpModMarkup.Parse(details.Description);
                pictures.AddRange(SpModMarkup.Media(document)
                    .Where(m => m.Kind == MarkupMediaKind.Image)
                    .Select(m => m.Url));
            }
        }
        catch (Exception ex)
        {
            // The mod's own picture still shows; the description's are asked for again next time.
            AppLog.Debug("Pictures", $"description pictures for {mod.Id} not loaded: {ex.Message}");
            _known.Remove(mod.Id);
        }

        return pictures.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
