using System.Diagnostics;
using System.IO;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The App's side of the conflict check (OPEN-11): runs ModConflictFinder over the Installed page's
// cards - so a mod's own folders, which a card already merges, never count against each other - and
// words what it found. Core reports the conflicts; every sentence about them is here.
//
public static class ModConflicts
{
    // Reads metadata already scanned, and hashes only copies of one assembly that match in size and
    // version - call it off the UI thread anyway.
    public static List<ModConflict> Find(IReadOnlyList<InstalledModCardViewModel> cards) =>
        ModConflictFinder.Find([.. cards.Select(c => (IReadOnlyList<InstalledMod>)c.Entries)]);

    //
    // Scans the install and finds its conflicts, grouping folders into mods the way the Installed page
    // does - with whatever catalog is already loaded, never waiting for one. Runs off the UI thread.
    // Returns the cards too, which the conflicts' member indices point into.
    //
    public static Task<(List<InstalledModCardViewModel> Cards, List<ModConflict> Conflicts)> ScanAsync(string installPath)
    {
        var catalog = AppServices.ModCache.AllMods;
        var addons = AppServices.Addons.AllAddons;
        var sptVersion = AppServices.SptEnvironment.InstalledVersion;
        var records = AppServices.InstallManifest.Load().ModsFor(installPath);

        return Task.Run(() =>
        {
            var cards = InstalledModCardViewModel.BuildFrom(
                InstalledModScanner.Scan(installPath), catalog, sptVersion, records, addons);
            return (cards, Find(cards));
        });
    }

    public static string Title(ModConflictKind kind) => kind switch
    {
        ModConflictKind.DuplicatePlugin => Strings.Conflicts_KindDuplicatePlugin,
        ModConflictKind.DuplicateServerMod => Strings.Conflicts_KindDuplicateServerMod,
        _ => Strings.Conflicts_KindDifferentCopies,
    };

    public static string Explanation(ModConflict conflict) => LocalizationService.Text(
        conflict.Kind switch
        {
            ModConflictKind.DuplicatePlugin => Strings.Conflicts_ExplainDuplicatePluginFormat,
            ModConflictKind.DuplicateServerMod => Strings.Conflicts_ExplainDuplicateServerModFormat,
            _ => Strings.Conflicts_ExplainDifferentCopiesFormat,
        },
        conflict.Identifier);

    //
    // Sets each card's conflict tooltip: one line per conflict it is in, naming the other mods, then
    // where to see the detail. Cards in no conflict are cleared.
    //
    public static void Apply(IReadOnlyList<InstalledModCardViewModel> cards, IReadOnlyList<ModConflict> conflicts)
    {
        var lines = new Dictionary<int, List<string>>();

        foreach (var conflict in conflicts)
        {
            var modIndices = conflict.Members.Select(m => m.ModIndex).Distinct().ToList();

            foreach (var index in modIndices)
            {
                var others = modIndices.Where(i => i != index).Select(i => cards[i].DisplayTitle).ToList();
                if (!lines.TryGetValue(index, out var list)) lines[index] = list = [];
                list.Add(LocalizationService.Text(Strings.Installed_ConflictWithFormat, Title(conflict.Kind), TextLists.Join(others)));
            }
        }

        for (var i = 0; i < cards.Count; i++)
        {
            cards[i].ConflictSummary = lines.TryGetValue(i, out var list)
                ? LocalizationService.Text(Strings.Installed_StatusConflictFormat, string.Join("\n", list))
                : null;
        }
    }

    // Explorer on the folder (or the loose DLL selected), the same way the Configs page opens one.
    public static bool OpenFolder(string path)
    {
        try
        {
            var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Conflicts", $"couldn't open {path}: {ex.Message}");
            return false;
        }
    }
}
