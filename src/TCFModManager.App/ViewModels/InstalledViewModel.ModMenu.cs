using System.IO;
using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM 1.4.3): what a mod's right-click menu on Subscribed items adds for that one mod -
// skipping an update, going back to the version an update replaced, and a note of your own.
//
public partial class InstalledViewModel
{
    /// <summary>The version an update of this mod would install, when it can be skipped.</summary>
    public static string? SkippableUpdate(InstalledModCardViewModel card) =>
        card is { UpdateAvailable: true, ModId: not null }
            ? card.UpdateVersion ?? card.LatestPublishedVersion
            : null;

    /// <summary>Skips this update, or (null) stops skipping, then scans so every count agrees.</summary>
    public async Task SkipUpdateAsync(InstalledModCardViewModel card, string? version)
    {
        if (card.ModId is not { } id) return;

        var title = card.DisplayTitle;
        SkippedUpdates.Set(id, card.IsAddon, version);

        await ScanAsync();
        ModsMoved?.Invoke(this, EventArgs.Empty); // Browse and the item pages offer Update by the same rule

        StatusMessage = version is null
            ? Text(Strings.Installed_StoppedSkippingFormat, title)
            : Text(Strings.Installed_SkippingFormat, title, version);
    }

    /// <summary>The version an update replaced that is still held, so it can be gone back to.</summary>
    public static string? GoBackVersion(InstalledModCardViewModel card)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)
            || card is not { IsAppManaged: true, ModId: { } id, InstalledVersion: { } current }
            || card.Entries.Any(e => e.IsDisabled)) // its files aren't where the record says; switch it on first
        {
            return null;
        }

        return RemovedMods.LatestReplaced(installPath, id, card.IsAddon, current)?.Log.Record?.Version;
    }

    //
    // Puts back the version the last update of this mod replaced, from the copy held when it was
    // updated (ModInstallService.RevertUpdate) - offline, and after the author has taken it down.
    // The version it replaces is held the same way, so the menu then offers that one back.
    //
    public async Task GoBackAsync(InstalledModCardViewModel card)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath) || card.ModId is not { } id || card.InstalledVersion is not { } current) return;

        var held = RemovedMods.LatestReplaced(installPath, id, card.IsAddon, current);
        var record = AppServices.InstallManifest.Load().Find(id, card.IsAddon);
        if (held is not { } found || found.Log.Record is not { } old || record is null)
        {
            StatusMessage = Strings.Installed_UndoNothing;
            return;
        }

        var title = card.DisplayTitle;
        var answer = SteamDialog.Show(
            Text(Strings.Installed_GoBackTitleFormat, old.Version),
            Paragraph(Text(Strings.Installed_GoBackBodyFormat, title, old.Version, current)),
            new SteamDialogChoice(Strings.Installed_GoBack, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));
        if (answer != 0) return;

        // The queue moves the same files and writes the same records.
        if (InstallingNow() is { } busy)
        {
            StatusMessage = Text(Strings.Installed_WaitForInstallFormat, busy);
            return;
        }

        string message;
        IsBusy = true;
        try
        {
            // Off the UI thread: a large mod is a lot of files to move.
            var result = await ModInstallService.AloneAsync(() => Task.Run(() => AppServices.ModInstall.RevertUpdate(installPath, record, found.Folder)));

            message = !result.Ran
                ? Strings.Installed_UndoNothing
                : result.Blocked.Count == 0
                    ? Text(Strings.Installed_WentBackFormat, title, old.Version)
                    : Text(Strings.Installed_WentBackBlockedFormat, title, old.Version, found.Folder);

            ModRemoved?.Invoke(this, EventArgs.Empty);
        }
        catch (ModInstallException ex)
        {
            message = ModInstallProblems.Describe(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = Text(Strings.Installed_RemoveFailedFormat, title, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await ScanAsync();
        StatusMessage = message;
    }

    /// <summary>Fork (1.4.8): whether this card can be told which sp-mod.com page it is - nothing
    /// matched it, so it has no page, no updates and, for an addon, no mod it belongs to.</summary>
    public static bool CanLinkToPage(InstalledModCardViewModel card) =>
        card is { ModId: null, IsFolderCompanion: false } && LinkFolders(card).Count > 0;

    private static List<string> LinkFolders(InstalledModCardViewModel card) =>
        new[] { card.ClientFolderName, card.ServerFolderName }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    //
    // Fork (1.4.8): asks for the page's link and records the card's folders as that mod or addon,
    // the way a version confirmed by hand is recorded (SetManualVersion) - the record's folders are
    // what the next scan matches the card by, so nothing new is stored and nothing on disk changes.
    // "Clear override" in the mod's update dialog removes the record, and with it the link.
    //
    // An addon installed by hand has nothing else to be known by: it has no GUID, and no file of it
    // says which addon it is.
    //
    // The version recorded is the release its files report when they report one, else the newest
    // that fits - what an unmatched mod with no readable version is assumed to be anyway.
    //
    public async Task LinkToPageAsync(InstalledModCardViewModel card)
    {
        var folders = LinkFolders(card);
        if (card.ModId is not null || folders.Count == 0) return;

        var box = new TextBox
        {
            Style = (Style)Application.Current.FindResource("SteamSearchField"),
            Tag = "https://sp-mod.com/mod/...",
            Height = 34,
            MinWidth = 480,
            MaxLength = 400, // HUNCH: far longer than any page link
        };
        box.Loaded += (_, _) => box.Focus();
        System.Windows.Automation.AutomationProperties.SetName(box, Strings.Menu_LinkToPage);

        var answer = SteamDialog.Show(
            Text(Strings.Menu_LinkTitleFormat, card.DisplayTitle),
            new StackPanel { Children = { Paragraph(Strings.Menu_LinkBody), box } },
            new SteamDialogChoice(Strings.Menu_LinkSave, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));
        if (answer != 0) return;

        if (!SpModPageLink.TryParse(box.Text, out var id, out var isAddon))
        {
            StatusMessage = Strings.Installed_LinkNotALink;
            return;
        }

        var fromFiles = card.InstalledVersion;
        string? name;
        string? guid = null;
        string? version;
        int? versionId;

        if (isAddon)
        {
            if (AppServices.Addons.ById(id) is not { } addon)
            {
                StatusMessage = Strings.Installed_LinkNotInList;
                return;
            }

            var parentVersion = _all.FirstOrDefault(c => !c.IsAddon && c.ModId == addon.ModId)?.InstalledVersion;
            var published = (addon.Versions ?? []).Where(v => !string.IsNullOrWhiteSpace(v.Version)).ToList();
            var held = ModVersionComparer.BestSameRelease(fromFiles, published.Select(v => v.Version));
            var pick = published.FirstOrDefault(v => v.Version == held)
                ?? (fromFiles is not null ? null : published
                    .NewestFirst()
                    .FirstOrDefault(v => parentVersion is null || ModVersionMatcher.IsSatisfiedBy(v.ModVersionConstraint, parentVersion) != false));

            name = addon.Name;
            version = pick?.Version ?? fromFiles;
            versionId = pick?.Id;
        }
        else
        {
            if (AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id) is not { } mod)
            {
                StatusMessage = Strings.Installed_LinkNotInList;
                return;
            }

            var published = (mod.Versions ?? []).Where(v => !string.IsNullOrWhiteSpace(v.Version)).ToList();
            var held = ModVersionComparer.BestSameRelease(fromFiles, published.Select(v => v.Version));
            var pick = published.FirstOrDefault(v => v.Version == held)
                ?? (fromFiles is not null ? null : ModCardViewModel.PickDisplayVersion(mod, AppServices.SptEnvironment.InstalledVersion) ?? published.FirstOrDefault());

            name = mod.Name;
            guid = mod.Guid;
            version = pick?.Version ?? fromFiles;
            versionId = pick?.Id;
        }

        name ??= card.DisplayTitle;

        // One record a mod: linking a second folder to it would rewrite the record of the first -
        // and, were that one installed by this app, the version it was installed as.
        if (AppServices.InstallManifest.Load().Find(id, isAddon) is not null || _all.Any(c => c.ModId == id && c.IsAddon == isAddon))
        {
            StatusMessage = Text(Strings.Installed_LinkAlreadyFormat, name);
            return;
        }

        if (version is null)
        {
            StatusMessage = Text(Strings.Installed_LinkNoVersionFormat, name);
            return;
        }

        // The queue writes the same records.
        if (InstallingNow() is { } busy)
        {
            StatusMessage = Text(Strings.Installed_WaitForInstallFormat, busy);
            return;
        }

        var folder = card.Name;
        AppServices.InstallManifest.SetManualVersion(id, guid, name, version, versionId, folders, isAddon);
        AppLog.Info("Installed", $"linked by hand: {string.Join(", ", folders)} -> {(isAddon ? "addon" : "mod")} {id} as {version}");

        await ScanAsync();
        ModsMoved?.Invoke(this, EventArgs.Empty); // the page it now has shows as installed in the Workshop

        StatusMessage = Text(Strings.Installed_LinkedFormat, folder, name, version);
    }

    /// <summary>A line of your own on the mod's card; empty removes it.</summary>
    public void EditNote(InstalledModCardViewModel card)
    {
        var box = new TextBox
        {
            Style = (Style)Application.Current.FindResource("SteamSearchField"),
            Tag = Strings.Menu_NoteHint,
            Text = card.Note ?? string.Empty,
            Height = 34,
            MinWidth = 480,
            MaxLength = 300, // HUNCH: a line or two on a card, not a document
        };
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        var answer = SteamDialog.Show(
            Text(Strings.Menu_NoteTitleFormat, card.DisplayTitle),
            box,
            new SteamDialogChoice(Strings.Common_Save, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));
        if (answer != 0) return;

        var note = box.Text.Trim();
        ModNotes.Set(card, note.Length == 0 ? null : note);
        ApplyFilter();
    }
}
