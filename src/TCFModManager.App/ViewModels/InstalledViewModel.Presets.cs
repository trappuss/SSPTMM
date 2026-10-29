using System.IO;
using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM 1.2.0): presets - which mods are on and which are off, saved under a name and put
// back in one go, as Mod Organizer 2's profiles do (Core's ModPresets). Disable all and Enable all go
// the same way, for troubleshooting.
//
// Applying is the page's own disable/enable, run twice (off, then on) and undone as one step, with:
// - a rescan first, so the plan is made from what is on disk now;
// - a confirmation naming every mod it turns off and on, what will be missing something it needs,
//   and the mods sp-mod.com marks as changing the profile;
// - a copy of the SPT profiles first, which must succeed;
// - how the mods were kept as "Put back", which outlives Undo (a restart);
// - refused while SPT runs or a mod is being installed, as every disable is.
//
// Fork (1.4.3): two mods sharing a folder, each with a card of its own, are kept and switched each
// by its own files, as their cards switch them (ModPresetSharedMod).
//
// The Presets menu itself is built in InstalledPage.xaml.cs.
//
public partial class InstalledViewModel
{
    private static readonly ModPresetStore PresetStore = new();

    private static string? PresetInstall => AppServices.SptEnvironment.InstallPath is { Length: > 0 } path ? path : null;

    private List<InstalledMod> Entries() => _all.SelectMany(c => c.Entries).ToList();

    // The cards that share a folder and are told apart by their files, as presets keep them; and
    // which card each came from, for the confirmation.
    private List<ModPresetSharedMod> SharedMods()
    {
        _sharedCards.Clear();
        foreach (var card in _all)
        {
            if (card.SharedFolderParts is not { } parts) continue;
            _sharedCards[new ModPresetSharedMod(card.ModId ?? 0, card.DisplayTitle, parts)] = card;
        }

        return [.. _sharedCards.Keys];
    }

    private readonly Dictionary<ModPresetSharedMod, InstalledModCardViewModel> _sharedCards = [];

    /// <summary>This install's presets, for the menu.</summary>
    public IReadOnlyList<ModPreset> Presets => PresetInstall is { } install ? PresetStore.For(install).Presets : [];

    /// <summary>How the mods were before the last apply, when there is one to put back.</summary>
    public ModPreset? BeforeLastPreset => PresetInstall is { } install ? PresetStore.For(install).BeforeLastApply : null;

    /// <summary>The preset the mods are set to right now, if any - ticked in the menu.</summary>
    public string? CurrentPresetName =>
        PresetInstall is { } install ? ModPresets.Matching(install, Presets, Entries(), SharedMods())?.Name : null;

    public bool HasModsForPresets => _all.Count > 0;

    // ---- saving, renaming, deleting --------------------------------------------------------------

    public async Task SavePresetAsync()
    {
        if (PresetInstall is not { } install || !ReadyForPresets()) return;
        await ScanAsync();

        var name = AskPresetName(Strings.Presets_SaveTitle, Strings.Presets_SaveIntro, Strings.Presets_SaveConfirm, string.Empty);
        if (name is null) return;

        if (ModPresetStore.Find(PresetStore.For(install), name) is { } existing
            && !Ask(Strings.Presets_SaveTitle, Text(Strings.Presets_ReplaceFormat, existing.Name), Strings.Presets_SaveOver))
            return;

        WritePreset(install, name, Text(Strings.Presets_SavedFormat, name));
    }

    public async Task UpdatePresetAsync(string name)
    {
        if (PresetInstall is not { } install || !ReadyForPresets()) return;
        await ScanAsync();

        if (!Ask(Strings.Presets_UpdateTitle, Text(Strings.Presets_UpdateFormat, name), Strings.Presets_SaveOver)) return;
        WritePreset(install, name, Text(Strings.Presets_SavedFormat, name));
    }

    private void WritePreset(string install, string name, string done)
    {
        try
        {
            PresetStore.Save(install, name, ModPresets.Capture(install, Entries(), SharedMods()), DateTimeOffset.Now);
            AppLog.Info("Presets", $"saved \"{name}\"");
            StatusMessage = done;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Text(Strings.Presets_CouldNotSaveFormat, ex.Message);
        }
    }

    public void RenamePreset(string name)
    {
        if (PresetInstall is not { } install) return;

        var newName = AskPresetName(Strings.Presets_RenameTitle, null, Strings.Presets_RenameConfirm, name);
        if (newName is null || newName == name) return;

        try
        {
            StatusMessage = PresetStore.Rename(install, name, newName)
                ? Text(Strings.Presets_RenamedFormat, newName)
                : Text(Strings.Presets_NameTakenFormat, newName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Text(Strings.Presets_CouldNotSaveFormat, ex.Message);
        }
    }

    public void DeletePreset(string name)
    {
        if (PresetInstall is not { } install) return;
        if (!Ask(Strings.Presets_DeleteTitle, Text(Strings.Presets_DeleteFormat, name), Strings.Presets_DeleteConfirm, destructive: true)) return;

        try
        {
            PresetStore.Delete(install, name);
            AppLog.Info("Presets", $"deleted \"{name}\"");
            StatusMessage = Text(Strings.Presets_DeletedFormat, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Text(Strings.Presets_CouldNotSaveFormat, ex.Message);
        }
    }

    // ---- applying ----------------------------------------------------------------------------------

    public Task ApplyPresetAsync(string name) =>
        ApplyEntriesAsync(name, Text(Strings.Presets_ApplyTitleFormat, name), install =>
            ModPresetStore.Find(PresetStore.For(install), name)?.Entries);

    public Task DisableAllAsync() =>
        ApplyEntriesAsync(Strings.Presets_DisableAllName, Strings.Presets_DisableAllTitle, install =>
            ModPresets.All(install, Entries(), enabled: false, SharedMods()));

    public Task EnableAllAsync() =>
        ApplyEntriesAsync(Strings.Presets_EnableAllName, Strings.Presets_EnableAllTitle, install =>
            ModPresets.All(install, Entries(), enabled: true, SharedMods()));

    public Task PutBackBeforeLastPresetAsync() =>
        ApplyEntriesAsync(Strings.Presets_PutBackName, Strings.Presets_PutBackTitle, install =>
            PresetStore.For(install).BeforeLastApply?.Entries);

    // False, with the reason said, while the page is scanning or busy - a preset waits for that.
    private bool ReadyForPresets()
    {
        if (!IsBusy && !ScanCommand.IsRunning) return true;
        StatusMessage = Strings.Presets_WaitForScan;
        return false;
    }

    // False, with the reason said, while folders can't be moved: SPT running, or a mod being placed.
    // Asked before the plan and again after the confirmation, which can stay open for as long as
    // anyone likes while the download queue keeps working.
    private bool InstallFreeToMove(string install)
    {
        if (InstallingNow() is { } busy)
        {
            StatusMessage = Text(Strings.Installed_WaitForInstallFormat, busy);
            return false;
        }

        if (ModInstallService.Blockers(ModInstallAction.Disable, install) is { Count: > 0 } blockers)
        {
            StatusMessage = ModInstallProblems.InstallInUse(blockers, ModInstallAction.Disable);
            return false;
        }

        return true;
    }

    private async Task ApplyEntriesAsync(string name, string title, Func<string, IReadOnlyList<ModPresetEntry>?> entriesFor)
    {
        if (PresetInstall is not { } install || !ReadyForPresets() || !InstallFreeToMove(install)) return;

        // Planned from what is on disk now, not from what the page last saw.
        await ScanAsync();
        if (entriesFor(install) is not { } entries) return;

        var mods = Entries();
        var shared = SharedMods();
        var plan = ModPresets.Plan(install, entries, mods, shared);
        if (plan.ChangesNothing)
        {
            StatusMessage = plan.InTwoPlaces.Count > 0
                ? Strings.Presets_OnlyInTwoPlaces(CardTitles(plan.InTwoPlaces).Count)
                : Text(Strings.Presets_AlreadyFormat, name);
            return;
        }

        if (!ConfirmPreset(title, plan, mods) || !InstallFreeToMove(install)) return;

        IsBusy = true;
        var moved = new List<ModMove>();
        var failed = new List<ModDisableFailure>();
        try
        {
            if (!AppServices.ProfileBackups.EnsureBackupBefore(install, ProfileBackups.BeforePreset))
            {
                StatusMessage = Strings.Presets_BackupFailed;
                return;
            }

            var before = ModPresets.Capture(install, mods, shared);
            try
            {
                PresetStore.KeepBeforeApply(install, name, before, DateTimeOffset.Now);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Undo still covers this session; only the Put back that outlives it is lost.
                AppLog.Warn("Presets", $"couldn't keep how the mods were: {ex.Message}");
            }

            var off = ModDisableService.Apply(plan.ToDisable, disable: true, install);
            moved.AddRange(off.Moved);
            failed.AddRange(off.Failed);

            var filesOff = ModDisableService.ApplyFiles(plan.SharedToDisable.SelectMany(s => s.Parts), disable: true, install);
            moved.AddRange(filesOff.Moved);
            failed.AddRange(filesOff.Failed);

            var on = ModDisableService.Apply(plan.ToEnable, disable: false, install);
            moved.AddRange(on.Moved);
            failed.AddRange(on.Failed);

            var filesOn = ModDisableService.ApplyFiles(plan.SharedToEnable.SelectMany(s => s.Parts), disable: false, install);
            moved.AddRange(filesOn.Moved);
            failed.AddRange(filesOn.Failed);

            AppLog.Info("Presets", $"applied \"{name}\": {off.Moved.Count} off, {on.Moved.Count} on, {failed.Count} failed");
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
            if (moved.Count > 0) SetLastMoves(moved, (nameof(Strings.Presets_UndoFormat), name));
            return;
        }
        finally
        {
            IsBusy = false;
        }

        SetLastMoves(moved, (nameof(Strings.Presets_UndoFormat), name));

        // Counted as the dialog counted them: whole mods, not folders.
        var movedFrom = moved.Select(m => m.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Moved(ModPresetSharedMod mod) => mod.Parts.Any(p => p.Files.Any(f =>
            movedFrom.Contains(Path.Combine(p.Entry.FolderPath, f.Replace('/', Path.DirectorySeparatorChar)))));
        var offCount = CardTitles(plan.ToDisable.Where(m => movedFrom.Contains(m.FolderPath))).Count
            + plan.SharedToDisable.Count(Moved);
        var onCount = CardTitles(plan.ToEnable.Where(m => movedFrom.Contains(m.FolderPath))).Count
            + plan.SharedToEnable.Count(Moved);
        var message = Text(Strings.Presets_AppliedFormat, name, offCount, onCount);
        if (failed.Count > 0) message = Sentences(message, DescribeFailures(failed));

        await ScanAsync();
        StatusMessage = message;
        ModsMoved?.Invoke(this, EventArgs.Empty);
    }

    // ---- the dialogs ---------------------------------------------------------------------------------

    private bool ConfirmPreset(string title, ModPresetPlan plan, IReadOnlyList<InstalledMod> mods)
    {
        var turnsOff = plan.ToDisable.ToHashSet();
        var turnsOn = plan.ToEnable.ToHashSet();
        bool EndsEnabled(InstalledMod m) => turnsOn.Contains(m) || (!m.IsDisabled && !turnsOff.Contains(m));

        var body = new StackPanel { MaxWidth = 640 };

        var turningOff = PlanTitles(plan.ToDisable, plan.SharedToDisable);
        var turningOn = PlanTitles(plan.ToEnable, plan.SharedToEnable);
        AddList(body, turningOff, Strings.Presets_TurnsOff(turningOff.Count));
        AddList(body, turningOn, Strings.Presets_TurnsOn(turningOn.Count));

        // What would be left missing a hard dependency: a mod that ends on, needing one that ends off.
        // (A mod sharing a folder is not looked at here: its folder's declarations are both mods'.)
        var missing = _dependencies.DisableImpact(plan.ToDisable)
            .Concat(_dependencies.EnableRequirements(plan.ToEnable))
            .Where(l => !l.IsSoft && EndsEnabled(l.Dependent) && !EndsEnabled(l.Dependency))
            .Select(l => Text(Strings.Presets_NeedsFormat, TitleOf(l.Dependent), TitleOf(l.Dependency)))
            .Distinct()
            .ToList();
        if (missing.Count > 0)
            body.Children.Add(Caution(Strings.Presets_NeedsHeader + "\n" + string.Join("\n", missing)));

        var goingOff = plan.ToDisable
            .Select(m => _cardByEntry.GetValueOrDefault(m))
            .Concat(plan.SharedToDisable.Select(s => _sharedCards.GetValueOrDefault(s)))
            .OfType<InstalledModCardViewModel>()
            .Distinct()
            .ToList();

        // Fork (1.4.5): what the profiles hold from them, then what sp-mod.com marks beyond that.
        var losses = ProfileItemsWarning.ForCards(goingOff);
        if (losses.Count > 0) body.Children.Add(Caution(ProfileItemsWarning.Text(losses)));

        var profileMods = goingOff
            .Where(c => ChangesProfile(c) && !losses.Any(l => l.ModName == c.DisplayTitle))
            .Select(c => c.DisplayTitle)
            .ToList();
        if (profileMods.Count > 0)
            body.Children.Add(Caution(Text(Strings.Presets_ProfileModsFormat, string.Join("\n", profileMods))));

        if (plan.InTwoPlaces.Count > 0) body.Children.Add(Note(Strings.Presets_InTwoPlaces(CardTitles(plan.InTwoPlaces).Count)));
        if (plan.NotInstalled.Count > 0)
        {
            var missingMods = plan.NotInstalled.Select(e => e.Name ?? e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            body.Children.Add(Note(Strings.Presets_NotInstalled(missingMods)));
        }
        var notInPreset = CardTitles(plan.NotInPreset).Count + plan.SharedNotInPreset.Count;
        if (notInPreset > 0) body.Children.Add(Note(Strings.Presets_NotInPreset(notInPreset)));

        body.Children.Add(Note(Strings.Presets_SafetyNote));

        return SteamDialog.Show(
            title,
            body,
            new SteamDialogChoice(Strings.Presets_ApplyConfirm, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey)) == 0;
    }

    // A card's title for a scan entry, or the folder's own name when it has no card.
    private string TitleOf(InstalledMod mod) => _cardByEntry.TryGetValue(mod, out var card) ? card.DisplayTitle : mod.Name;

    // Whole mods, not folders: a client+server mod is one line.
    private List<string> CardTitles(IEnumerable<InstalledMod> mods) =>
        mods.Select(TitleOf).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();

    // The folders' mods and the shared folders' mods, by title.
    private List<string> PlanTitles(IEnumerable<InstalledMod> mods, IEnumerable<ModPresetSharedMod> shared) =>
        CardTitles(mods)
            .Concat(shared.Select(s => s.Name ?? string.Empty).Where(n => n.Length > 0))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private void AddList(StackPanel body, IReadOnlyList<string> titles, string heading)
    {
        if (titles.Count == 0) return;

        body.Children.Add(new TextBlock
        {
            Text = heading,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 160,
            Margin = new Thickness(0, 0, 0, 12),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock { Text = string.Join("\n", titles), TextWrapping = TextWrapping.Wrap, LineHeight = 20 },
        });
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 20,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static TextBlock Caution(string text) => ProfileWarning(text);

    // A destructive choice is grey with Cancel the default, as the page's removal prompts have it.
    private static bool Ask(string title, string text, string confirm, bool destructive = false) =>
        SteamDialog.Show(
            title,
            Note(text),
            new SteamDialogChoice(confirm, destructive ? SteamDialogButton.Grey : SteamDialogButton.Green, IsDefault: !destructive),
            new SteamDialogChoice(Strings.Common_Cancel, destructive ? SteamDialogButton.Blue : SteamDialogButton.Grey, IsDefault: destructive)) == 0;

    // Null when cancelled or left blank.
    private static string? AskPresetName(string title, string? intro, string confirm, string current)
    {
        var body = new StackPanel { MinWidth = 420, MaxWidth = 560 };
        if (intro is not null) body.Children.Add(Note(intro));

        var box = new TextBox
        {
            Style = (Style)Application.Current.FindResource("SteamSearchField"),
            Tag = Strings.Presets_NamePlaceholder,
            Text = current,
            Height = 34,
            MaxLength = ModPresets.MaxNameLength,
        };
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        body.Children.Add(box);

        var answer = SteamDialog.Show(
            title,
            body,
            new SteamDialogChoice(confirm, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        return answer == 0 ? ModPresets.CleanName(box.Text) : null;
    }
}
