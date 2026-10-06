using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

// 
// Resolves the dependency tree of every installed mod that has one, and reports each dependency's
// status against what's actually on disk.
// 
public partial class DependenciesViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly SpModApiClient _spModApi;

    // How many identifier:version pairs go in one request. The endpoint takes many at once,
    // which is what keeps this to a couple of calls instead of one per installed mod.
    private const int BatchSize = 25;

    public DependenciesViewModel() : this(AppServices.SpModApi)
    {
    }

    public DependenciesViewModel(SpModApiClient spModApi)
    {
        _spModApi = spModApi;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasLoaded;

    public ObservableCollection<DependencyTreeViewModel> Trees { get; } = [];

    //
    // What clashes at load time (OPEN-11) - worked out from the install alone, with no network, so it
    // shows even when the dependency lookup can't run. Rechecked each time the page opens.
    //
    public ObservableCollection<ConflictItemViewModel> Conflicts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoConflicts))]
    private bool _conflictsChecked;

    public bool ShowNoConflicts => ConflictsChecked && Conflicts.Count == 0;

    //
    // Scans the install and lists its conflicts. Uses whatever catalog is already loaded for the
    // mods' names and doesn't wait for one - a hand-installed mod is still named by its folder.
    //
    [RelayCommand]
    private async Task RefreshConflictsAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        try
        {
            var (cards, conflicts) = await ModConflicts.ScanAsync(installPath);
            var items = conflicts
                .Select(c => ConflictItemViewModel.From(c, cards, installPath))
                .OrderBy(c => c.Title, StringComparer.CurrentCulture)
                .ToList();
            ConflictItemViewModel.PlanKeeps(items, AppServices.InstallManifest.Load());

            Conflicts.Clear();
            foreach (var item in items) Conflicts.Add(item);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Conflicts", $"couldn't check for conflicts: {ex.Message}");
            Conflicts.Clear();
        }

        ConflictsChecked = true;
        OnPropertyChanged(nameof(ShowNoConflicts));
    }

    //
    // Keep this one (OPEN-11 D6): keeps the chosen copy and removes every other mod holding the same
    // plugin or server mod - through the normal Remove, so each goes into the install's holding folder
    // and Undo on the Installed page can put it back. A mod this app installed is removed by its
    // record; one installed by hand has just the clashing folder moved.
    //
    [RelayCommand]
    private async Task KeepConflictCopyAsync(ConflictMemberRow? keep)
    {
        if (keep is not { CanKeep: true }) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var others = keep.Removals;
        if (others.Count == 0) return;

        var lines = string.Join("\n", others.SelectMany(o =>
            (o.Card.IsAppManaged ? o.Card.Entries.Where(e => !e.IsDisabled) : o.Entries).Select(e =>
                Text(Strings.Conflicts_KeepLineFormat, o.Card.DisplayTitle, Path.GetRelativePath(installPath, e.FolderPath)))));

        var answer = TCFModManager.App.Views.SteamMessageBox.Show(
            Text(Strings.Conflicts_KeepConfirmFormat, keep.ModName, keep.Location, lines, InstalledViewModel.HeldSentence()),
            Strings.Conflicts_KeepConfirmTitle,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        var removed = new List<string>();
        try
        {
            var manifest = AppServices.InstallManifest.Load();

            foreach (var (card, entries) in others)
            {
                try
                {
                    if (card is { IsAppManaged: true, ModId: { } id } && manifest.Find(id, card.IsAddon) is { } record)
                    {
                        // Off the UI thread: it copies the SPT profiles first (fork: ProfileBackups).
                        await Task.Run(() => AppServices.ModInstall.UninstallAsync(installPath, record, ConfigAction.Keep));
                    }
                    else
                    {
                        var paths = entries.Select(e => e.FolderPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        await Task.Run(() => AppServices.ModInstall.RemoveHandInstalled(paths, installPath, card.DisplayTitle));
                    }

                    removed.Add(card.DisplayTitle);
                    AppLog.Info("Conflicts", $"kept {keep.ModName} ({keep.Location}); removed {card.DisplayTitle}");
                }
                catch (ModInstallException ex)
                {
                    StatusMessage = Text(Strings.Conflicts_KeepFailedFormat, card.DisplayTitle, ModInstallProblems.Describe(ex));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    StatusMessage = Text(Strings.Conflicts_KeepFailedFormat, card.DisplayTitle, ex.Message);
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (removed.Count > 0)
            StatusMessage = Text(Strings.Conflicts_KeptFormat, keep.ModName, TextLists.Join(removed));

        await RefreshConflictsAsync();
    }

    [RelayCommand]
    private void OpenConflictFolder(ConflictMemberRow? row)
    {
        if (row is not null && !ModConflicts.OpenFolder(row.FullPath)) StatusMessage = Strings.Common_FolderOpenFailed;
    }

    // True when nothing installed declares a dependency - distinct from "not loaded yet".
    public bool IsEmpty => HasLoaded && Trees.Count == 0;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var sptVersion = AppServices.SptEnvironment.InstalledVersion;
        if (string.IsNullOrWhiteSpace(sptVersion))
        {
            StatusMessage = Strings.Dependencies_NoSptVersion;
            await RefreshConflictsAsync();
            return;
        }

        IsBusy = true;
        StatusMessage = Strings.Dependencies_Scanning;
        try
        {
            await AppServices.ModCache.EnsureLoadedAsync();

            // Before anything that needs the network, so conflicts show even when the lookup fails.
            await RefreshConflictsAsync();

            var scanned = await Task.Run(() => InstalledModScanner.Scan(installPath));
            var installed = InstalledModCardViewModel.BuildFrom(
                scanned, AppServices.ModCache.AllMods, sptVersion, AppServices.InstallManifest.Load().ModsFor(installPath),
                AppServices.Addons.AllAddons);

            // Only mods that matched the catalog can be asked about; a hand-installed mod we
            // couldn't identify has no identifier to query with.
            var queryable = installed
                .Where(m => m is { IsAddon: false, ModId: not null })
                .Select(m => (Card: m, Mod: AppServices.ModCache.AllMods.FirstOrDefault(c => c.Id == m.ModId)))
                .Where(x => x.Mod is not null)
                .Select(x => (x.Card, Mod: x.Mod!, Version: ResolveQueryVersion(x.Card, x.Mod!)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Version))
                .ToList();

            if (queryable.Count == 0)
            {
                Trees.Clear();
                HasLoaded = true;
                OnPropertyChanged(nameof(IsEmpty));
                StatusMessage = Strings.Dependencies_NoneMatched;
                return;
            }

            StatusMessage = Strings.Dependencies_Resolving(queryable.Count);

            // Mods only: an addon's id is from a separate sequence, and would take a dependency's
            // place whenever the two numbers happened to match.
            var installedByModId = installed
                .Where(m => m.ModId is not null && !m.IsAddon)
                .GroupBy(m => m.ModId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var trees = new List<DependencyTreeViewModel>();

            foreach (var batch in Chunk(queryable, BatchSize))
            {
                var pairs = string.Join(",", batch.Select(x => $"{Identifier(x.Mod)}:{x.Version}"));
                AppLog.Debug("Dependencies", $"resolving against SPT {sptVersion}: {pairs}");

                var resolved = await _spModApi.GetModDependenciesAsync(pairs, sptVersion!);

                foreach (var missing in batch.Where(x => !resolved.ContainsKey($"{Identifier(x.Mod)}:{x.Version}")))
                {
                    AppLog.Warn("Dependencies",
                        $"no tree returned for {Identifier(missing.Mod)}:{missing.Version} ({missing.Card.DisplayTitle})");
                }

                foreach (var (card, mod, version) in batch)
                {
                    var key = $"{Identifier(mod)}:{version}";
                    if (!resolved.TryGetValue(key, out var nodes) || nodes.Count == 0) continue;

                    var tree = new DependencyTreeViewModel
                    {
                        ModName = card.DisplayTitle,
                        ModVersion = version!,
                        Mod = mod,
                    };

                    foreach (var row in Flatten(nodes, 0, installedByModId)) tree.Rows.Add(row);

                    tree.Refresh();
                    trees.Add(tree);
                }
            }

            Trees.Clear();
            // Mods needing attention first, so the page opens on the problems.
            foreach (var tree in trees
                         .OrderByDescending(t => t.NeedsAttention)
                         .ThenBy(t => t.ModName, StringComparer.OrdinalIgnoreCase))
            {
                Trees.Add(tree);
            }

            HasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));

            var attention = Trees.Count(t => t.NeedsAttention);
            AppLog.Info("Dependencies",
                $"queried {queryable.Count} mod(s); {Trees.Count} have dependencies, {attention} need attention");
            StatusMessage = Trees.Count == 0
                ? Strings.Dependencies_NoneDeclared
                : attention == 0
                    ? Strings.Dependencies_AllSatisfied(Trees.Count)
                    : Strings.Dependencies_Attention(Trees.Count, Trees.Count, attention);
        }
        catch (SpModApiRateLimitedException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (SpModApiException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (Exception ex)
        {
            AppLog.Error("Dependencies", "Resolve failed", ex);
            StatusMessage = Text(Strings.Dependencies_UnexpectedFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Queues a missing or outdated dependency, behind the same read-the-mod-page gate Browse uses.
    [RelayCommand]
    private void Install(DependencyRow? row) => Queue(row, alternate: false);

    // The small button beside Install: the opposite of Monitor mode's setting, for this one mod.
    [RelayCommand]
    private void InstallAlternate(DependencyRow? row) => Queue(row, alternate: true);

    private void Queue(DependencyRow? row, bool alternate)
    {
        if (row?.CatalogMod is null || string.IsNullOrWhiteSpace(row.RequiredVersion)) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var mod = row.CatalogMod;
        if (!ReadModPageConfirmationWindow.Confirm(ModPageLink.For(mod, mod.Name ?? row.Name)))
        {
            StatusMessage = Text(Strings.Dependencies_CancelledFormat, row.Name);
            return;
        }

        var version = row.RequiredVersion!;

        // checkDependencies stays on: a dependency can have dependencies of its own.
        AppServices.DownloadQueue.Enqueue(
            InstallTarget.For(mod), version, installPath, () => ResolveVersionLinkAsync(mod, version),
            downloadOnly: AppServices.ModPageGate.DownloadOnlyFor(alternate));

        row.IsQueued = true;
        StatusMessage = Text(Strings.Dependencies_QueuedFormat, row.Name, version);
    }

    // Resolves the full ModVersion (with its download link) for exactly one version string,
    // lazily, so queueing never waits on a network call.
    private async Task<ModVersion?> ResolveVersionLinkAsync(Mod mod, string version)
    {
        var versions = await _spModApi.GetModVersionsAsync(
            mod.Id.ToString(), new ModVersionsQuery { FilterVersion = version, PerPage = 5 });

        return versions.Data.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase))
               ?? versions.Data.FirstOrDefault();
    }

    // The endpoint wants a version that matches a published one exactly. The manifest holds
    // that string verbatim for anything this app installed; otherwise the closest published version
    // to what's on disk is used, since a scanned version often carries an extra ".0".
    private static string? ResolveQueryVersion(InstalledModCardViewModel card, Mod mod)
    {
        var published = mod.Versions ?? [];

        var exact = published.FirstOrDefault(v =>
            string.Equals(v.Version, card.InstalledVersion, StringComparison.OrdinalIgnoreCase));
        if (exact?.Version is not null) return exact.Version;

        var best = ModVersionComparer.BestSameRelease(card.InstalledVersion, published.Select(v => v.Version));
        var equivalent = best is null ? null : published.FirstOrDefault(v => v.Version == best);
        if (equivalent?.Version is not null) return equivalent.Version;

        return ModCardViewModel.LatestVersion(mod)?.Version;
    }

    // The endpoint accepts a GUID or a numeric id; GUID is preferred when the mod has one.
    private static string Identifier(Mod mod) =>
        string.IsNullOrWhiteSpace(mod.Guid) ? mod.Id.ToString() : mod.Guid!;

    // Walks a resolved tree depth-first into indented rows, tagging each with its status
    // against what's installed.
    private static IEnumerable<DependencyRow> Flatten(
        IEnumerable<DependencyNode> nodes,
        int depth,
        IReadOnlyDictionary<int, InstalledModCardViewModel> installedByModId)
    {
        foreach (var node in nodes)
        {
            installedByModId.TryGetValue(node.Id, out var installed);

            var required = node.LatestCompatibleVersion?.Version;

            // A disabled dependency is on disk but isn't loaded, so anything needing it is as
            // broken as if it were missing - shown as its own state rather than as "installed".
            var status = DependencyStatusResolver.Resolve(
                node, installed?.InstalledVersion, required, installed?.IsDisabled == true,
                exactVersion: installed is { IsAppManaged: true } or { IsManualOverride: true });

            yield return new DependencyRow
            {
                Name = node.Name ?? node.Guid ?? Strings.Dependencies_UnknownName,
                Depth = depth,
                Status = status,
                InstalledVersion = installed?.InstalledVersion,
                RequiredVersion = required,
                CatalogMod = AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == node.Id),
            };

            foreach (var child in Flatten(node.Dependencies, depth + 1, installedByModId))
                yield return child;
        }
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }
}
