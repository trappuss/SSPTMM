using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM): a mod whose installed version was uploaded again under the same number
// (ReuploadCheck) - a RE-UPLOADED chip, a line saying what changed, and Get it again, which installs
// that version over itself. Worked out after each scan from the cached catalog: no network.
//
public partial class InstalledViewModel
{
    private static void ApplyReuploads(IReadOnlyList<InstalledModCardViewModel> cards)
    {
        var records = AppServices.InstallManifest.Load().ModsFor(AppServices.SptEnvironment.InstallPath);
        var catalog = AppServices.ModCache.AllMods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var card in cards)
        {
            card.Reupload = !card.IsAddon
                && card.ModId is { } id
                && records.FirstOrDefault(r => r.ModId == id && !r.IsAddon) is { } record
                && catalog.TryGetValue(id, out var mod)
                    ? ReuploadCheck.Find(record, mod.Versions)
                    : null;
        }

        foreach (var card in cards.Where(c => c.Reupload is not null))
        {
            var found = card.Reupload!;
            AppLog.Info("Reuploads", $"{card.Name} {found.Version.Version}: {found.Kind}, listed {found.Before} then, {found.Now} now (entry {found.Version.Id})");
        }
    }

    [RelayCommand]
    private void GetAgain(InstalledModCardViewModel? card)
    {
        if (card?.Reupload is not { } found || card.ModId is not { } id) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        if (AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id) is not { } mod)
        {
            StatusMessage = Text(Strings.Installed_GetAgainNotInCatalogFormat, card.DisplayTitle);
            return;
        }

        var versionId = found.Version.Id;
        var version = found.Version.Version ?? card.InstalledVersion ?? string.Empty;

        AppServices.DownloadQueue.Enqueue(
            InstallTarget.For(mod),
            version,
            installPath,
            async () =>
            {
                // That entry exactly: two entries can share the version number.
                var versions = await AppServices.SpModApi.GetModVersionsAsync(
                    id.ToString(CultureInfo.InvariantCulture),
                    new ModVersionsQuery { FilterId = versionId.ToString(CultureInfo.InvariantCulture), PerPage = 5 });
                return versions.Data.FirstOrDefault(v => v.Id == versionId);
            },
            totalBytes: found.Now);

        AppLog.Info("Reuploads", $"getting {card.Name} {version} again (entry {versionId})");
        StatusMessage = Text(Strings.Installed_GetAgainQueuedFormat, card.DisplayTitle, version);
    }
}
