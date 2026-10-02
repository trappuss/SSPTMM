using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Services;

// One read of the install, reduced to the shape every Core mod-list piece takes.
public sealed record ModListInstall(
    string InstallPath,
    IReadOnlyList<ModListCandidate> Candidates,
    string? SptVersion);

//
// A list, what applying it would do, and the install both were worked out against.
//
// Held together on purpose: the plan shown to the user and the plan that runs have to be the same
// one, worked out against the same scan, or a rescan between the two silently changes what happens.
//
public sealed record ModListPreview(ModList List, ModListPlan Plan, ModListInstall Install);

//
// Monitor mode's say in a list apply (§9, R6): whether its installs and updates are saved for the
// user to install instead, and the subfolder a list's downloads go into. Enables and disables run
// either way - they move folders inside the install and place nothing new.
//
public sealed record ModListDownloadMode(bool DownloadOnly, string? Subfolder)
{
    public static ModListDownloadMode Install { get; } = new(false, null);

    // Read at the moment it is needed, so a list applied just after the Options setting changed
    // follows the new setting.
    public static ModListDownloadMode For(ModList list)
    {
        var monitor = new SettingsService().Load().Monitor;
        if (monitor.InstallMode != InstallMode.DownloadOnly) return Install;

        return new ModListDownloadMode(true, monitor.DownloadListSubfolders ? list.Name : null);
    }
}

//
// One mod the add-a-mod picker can put on a list: the entry a list would store for it, plus the
// couple of facts the picker labels it with that the entry itself doesn't carry.
//
public sealed record ModListAddOption
{
    public required ModListEntry Entry { get; init; }

    // Installed here, but currently sitting in the disabled folder.
    public bool IsDisabled { get; init; }

    // For a catalog listing, whether this install already has it. Always true for an installed one.
    public bool IsInstalled { get; init; }

    // The catalog listing's author, for telling two mods with similar names apart.
    public string? Author { get; init; }
}

// What the picker can offer, split by where it came from.
public sealed record ModListAddOptions(
    IReadOnlyList<ModListAddOption> Installed,
    IReadOnlyList<ModListAddOption> Catalog);

//
// The App half of mod lists - everything Core deliberately cannot reach.
//
// Core owns the model, the capture, the diff and the ordering of an apply; it can't own the scan
// (which produces App view models) or the downloads (the queue is an App type). This turns the
// Installed cards into ModListCandidates and the plan's fetches into queued downloads, and hands
// both to Core.
//
public sealed class ModListService
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    //
    // Scans the install and builds the candidate list, off the UI thread - the same scan-and-match
    // pass InstalledViewModel runs, for the same reason it runs it in Task.Run.
    //
    // Returns null when no SPT install folder is set.
    //
    public async Task<ModListInstall?> ReadInstallAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return null;

        await AppServices.ModCache.EnsureLoadedAsync();
        await AppServices.Addons.EnsureLoadedAsync();

        var records = AppServices.InstallManifest.Load().ModsFor(installPath);
        var catalog = AppServices.ModCache.AllMods;
        var sptVersion = AppServices.SptEnvironment.InstalledVersion;

        var candidates = await Task.Run(() =>
        {
            var found = InstalledModScanner.Scan(installPath);

            var cards = InstalledModCardViewModel.BuildFrom(
                found, catalog, sptVersion, records, AppServices.Addons.AllAddons);

            return ModListCandidates.From(cards, records);
        });

        return new ModListInstall(installPath, candidates, sptVersion);
    }

    //
    // Re-reads the installed version of every mod on a list.
    //
    // Returns null when there is no SPT install folder to read. The entries come back changed but
    // NOT stored - the caller puts them in the edit buffer, so a refresh is reviewed and saved like
    // any other edit rather than rewriting a list behind the user's back.
    //
    public async Task<ModListRefreshResult?> RefreshVersionsAsync(IEnumerable<ModListEntry> entries)
    {
        var install = await ReadInstallAsync();
        if (install is null) return null;

        return ModListRefresh.Build(entries, install.Candidates, CatalogVersions(), CatalogAddonVersions());
    }

    // Captures what's installed now as a new list and stores it.
    public async Task<ModList?> CaptureAsync(string name, bool includeDisabled = false)
    {
        var install = await ReadInstallAsync();
        if (install is null) return null;

        var list = ModListCapture.Build(
            name,
            install.Candidates,
            DateTimeOffset.UtcNow,
            CatalogVersions(),
            install.SptVersion,
            includeDisabled: includeDisabled,
            addonVersions: CatalogAddonVersions());

        return AppServices.ModLists.Add(list);
    }

    //
    // What this machine is, as the scopes a served list gets filtered to.
    //
    // Read here rather than passed in, because every caller would read the same setting and one that
    // forgot would plan a headless box as an ordinary player - stripping it of the bot and item mods
    // it is hosting the raid with, which is a failure everyone in that raid feels and nobody can see
    // the cause of.
    //
    // Only ever narrows a list a SERVER served. Your own lists describe your own install.
    //
    public static ModListEntryScope MachineScope =>
        InstallRole.ScopeFor(new SettingsService().Load().Roles);

    // Works out what applying this list would do. Nothing moves and nothing downloads.
    public async Task<ModListPreview?> PreviewAsync(ModList list)
    {
        var install = await ReadInstallAsync();
        if (install is null) return null;

        //
        // A personal list is planned knowing what the followed server also requires, so its
        // Exclusive sweep spares those mods. Passed as null when planning the server list itself -
        // it does not need protecting from its own plan, and a served list never disables anyway.
        //
        var serverList = list.Origin == ModListOrigin.Server ? null : AppServices.ModLists.GetActiveServer();

        return new ModListPreview(
            list,
            ModListPlanner.Build(list, install.Candidates, AppServices.ModLists.GetPins(), serverList, MachineScope),
            install);
    }

    //
    // Applies a preview. Core decides the order and when to stop; everything here is the download
    // half plus storing what came back.
    //
    // <param name="prompts">Who answers the two questions a fetch has to ask. Defaults to answering
    // no to both, so an unwired caller downloads nothing rather than everything.</param>
    //
    public async Task<ModListApplyResult> ApplyAsync(
        ModListPreview preview,
        ModListPrompts? prompts = null,
        bool takeSnapshot = true,
        CancellationToken ct = default)
    {
        prompts ??= ModListPrompts.Reject;

        await BackUpProfilesAsync(preview.Install.InstallPath);
        var mode = ModListDownloadMode.For(preview.List);

        var result = await ModListApplier.ApplyAsync(
            preview.Plan,
            preview.Install.Candidates,
            (fetches, token) => FetchAsync(preview.Install, fetches, prompts, mode, token),
            new ModListApplyOptions
            {
                // Named after the list being applied, not "Before X" - the snapshot is only ever
                // shown on the revert button, which words it, and a stored "Before ..." was what
                // grew another "Before " every time one got applied.
                SnapshotName = takeSnapshot ? preview.List.Name : null,
                SnapshotVersions = CatalogVersions(),
                SnapshotAddonVersions = CatalogAddonVersions(),
                SptVersion = preview.Install.SptVersion,
                InstallPath = preview.Install.InstallPath,
            },
            ct: ct);

        if (result.Snapshot is not null) AppServices.ModLists.SetSnapshot(result.Snapshot);

        if (result.Completed)
        {
            //
            // Into whichever slot it belongs to. Following a server must not cost the user the
            // personal list they were already following, and vice versa - see ModListData.
            //
            if (preview.List.Origin == ModListOrigin.Server)
                AppServices.ModLists.SetActiveServer(preview.List.Id);
            else
                AppServices.ModLists.SetActive(preview.List.Id);

            // The revision counts applies, not edits - see ModListStore.BumpRevision. An apply that
            // stopped part way is not one: it is unwound, and the install ends up where it started.
            AppServices.ModLists.BumpRevision(preview.List.Id);
        }

        return result;
    }

    // A copy of the SPT profiles from before the list changes the install, off the UI thread - not
    // while SPT runs (the apply is refused then, and its server may be writing them).
    //
    private static async Task BackUpProfilesAsync(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return;

        if (ModInstallService.RunningBlockers(installPath).Count == 0)
            await Task.Run(() => AppServices.ProfileBackups.BackupIfChanged(installPath, ProfileBackups.BeforeList));
    }

    //
    // Puts the install back the way it was before the last list was applied.
    //
    // Takes no snapshot of its own and clears the one it used: there is a single undo point, so a
    // revert is the end of the chain rather than something else to undo. Without that, applying a
    // snapshot snapshotted the snapshot and every name grew another "Before ".
    //
    // Normally downloads nothing - the snapshot only ever names mods that were installed at the
    // time, so the plan comes out as enables and disables.
    //
    public async Task<ModListApplyResult?> RevertAsync(ModListPrompts? prompts = null, CancellationToken ct = default)
    {
        if (AppServices.ModLists.GetSnapshot() is not { } snapshot) return null;

        var preview = await PreviewAsync(snapshot);
        if (preview is null) return null;

        await BackUpProfilesAsync(preview.Install.InstallPath);

        var result = await ModListApplier.ApplyAsync(
            preview.Plan,
            preview.Install.Candidates,
            (fetches, token) => FetchAsync(
                preview.Install, fetches, prompts ?? ModListPrompts.Reject, ModListDownloadMode.For(snapshot), token),
            new ModListApplyOptions
            {
                SptVersion = preview.Install.SptVersion,
                InstallPath = preview.Install.InstallPath,
            },
            ct: ct);

        if (result.Completed)
        {
            AppServices.ModLists.SetSnapshot(null);
            AppServices.ModLists.SetActive(null);
        }

        return result;
    }

    // How the install stood before the last apply, or null when nothing has been applied yet.
    public ModList? PendingRevert() => AppServices.ModLists.GetSnapshot();

    //
    // Resolves what each fetch would actually download, asks about anything the list can't have as
    // written, then queues the lot.
    //
    // Every version is resolved before anything is enqueued, rather than lazily inside the queue
    // worker the way a single Install does. A list is a batch: the user should be told up front
    // that three of forty mods can't be had at the pinned version, not find out one at a time while
    // downloads are already running.
    //
    private async Task<ModListFetchOutcome> FetchAsync(
        ModListInstall install,
        IReadOnlyList<ModListAction> fetches,
        ModListPrompts prompts,
        ModListDownloadMode mode,
        CancellationToken ct)
    {
        var resolution = await ResolveAsync(fetches, install.Candidates, install.SptVersion, ct);

        var accepted = prompts.ApproveVersionChanges(resolution.Changes);
        var acceptedActions = accepted.Select(c => c.Action).ToHashSet();

        var failed = new List<ModListFetchFailure>(resolution.Unavailable);

        foreach (var change in resolution.Changes.Where(c => !acceptedActions.Contains(c.Action)))
        {
            failed.Add(new ModListFetchFailure(
                change.Action.Name,
                Text(Strings.ModList_ReasonVersionNotTakenFormat, change.Wanted)));
        }

        var downloads = new List<ModListDownload>(resolution.Ready);
        downloads.AddRange(accepted.Select(c => new ModListDownload(c.Action, c.Target, c.Available, IsSubstitute: true)));

        if (downloads.Count == 0) return new ModListFetchOutcome([], failed, false);

        //
        // Versions that don't support the installed SPT are asked about once for the batch, like a
        // single install. No leaves those out, each named as a failure, and fetches the rest.
        //
        var incompatible = downloads.Where(d => SptCompatibility.IsIncompatible(d.Version.SptVersionConstraint)).ToList();
        if (incompatible.Count > 0 && !prompts.InstallIncompatible(incompatible))
        {
            foreach (var d in incompatible)
            {
                failed.Add(new ModListFetchFailure(
                    d.Action.Name,
                    Text(Strings.ModList_ReasonIncompatibleFormat, d.Version.Version, AppServices.SptEnvironment.InstalledVersion)));
            }

            downloads.RemoveAll(incompatible.Contains);
            if (downloads.Count == 0) return new ModListFetchOutcome([], failed, false);
        }

        //
        // The same gate a manual install goes through, asked once for the whole batch rather than
        // once per mod - ConfirmAll is the existing path for exactly this, and it honours the
        // Options switch that turns the gate off.
        //
        if (!prompts.ConfirmModPages(downloads))
            return new ModListFetchOutcome([], failed, Cancelled: true);

        return await QueueAsync(install.InstallPath, downloads, failed, mode, ct);
    }

    //
    // Everything is enqueued first and awaited afterwards, so the queue's own worker decides the
    // order and the user sees the whole batch on the Downloads page at once rather than one card
    // appearing at a time.
    //
    private static async Task<ModListFetchOutcome> QueueAsync(
        string installPath,
        IReadOnlyList<ModListDownload> downloads,
        List<ModListFetchFailure> failed,
        ModListDownloadMode mode,
        CancellationToken ct)
    {
        var waiting = new List<(ModListDownload Download, DownloadQueueItemViewModel Item)>();
        var fetched = new List<ModListAction>();

        foreach (var download in downloads)
        {
            var version = download.Version;

            if (mode.DownloadOnly && AlreadySaved(download.Target, version))
            {
                fetched.Add(download.Action);
                continue;
            }

            var item = await EnqueueAsync(
                download.Target,
                version.Version ?? download.Action.TargetVersion ?? "latest",
                installPath,
                () => Task.FromResult<ModVersion?>(version),
                version.ContentLength,
                mode);

            waiting.Add((download, item));
        }

        using var cancelling = ct.Register(() => CancelAll(waiting.Select(w => w.Item)));

        var cancelled = false;

        foreach (var (download, item) in waiting)
        {
            switch (await WaitForAsync(item))
            {
                case DownloadQueueItemStatus.Completed:
                    fetched.Add(download.Action);
                    break;

                case DownloadQueueItemStatus.Cancelled:
                    cancelled = true;
                    break;

                default:
                    failed.Add(new ModListFetchFailure(download.Action.Name, item.StatusMessage));
                    break;
            }
        }

        return new ModListFetchOutcome(fetched, failed, cancelled);
    }

    //
    // Works out the exact version behind each fetch, and separates the ones the list can't have as
    // written. A pinned version that is gone becomes a question, never a silent substitution - the
    // list named a specific build, and quietly installing a different one is what desyncs a group.
    //
    private static async Task<ModListResolution> ResolveAsync(
        IReadOnlyList<ModListAction> fetches,
        IReadOnlyList<ModListCandidate> installed,
        string? sptVersion,
        CancellationToken ct)
    {
        var catalog = AppServices.ModCache.AllMods
            .GroupBy(m => m.Id)
            .ToDictionary(g => g.Key, g => g.First());

        //
        // Which parent mods an addon in this batch can rely on: everything already installed, plus
        // everything this same apply is about to install. An addon whose parent is in neither is
        // refused here rather than downloaded - it would install files nothing loads, and the list
        // would look applied when it wasn't.
        //
        var parents = installed
            .Where(c => c is { IsAddon: false, ModId: not null })
            .Select(c => c.ModId!.Value)
            .ToHashSet();

        foreach (var action in fetches.Where(a => a is { IsAddon: false, ModId: not null }))
            parents.Add(action.ModId!.Value);

        var ready = new List<ModListDownload>();
        var changes = new List<ModListVersionChange>();
        var unavailable = new List<ModListFetchFailure>();

        foreach (var action in fetches)
        {
            if (action.ModId is not { } modId)
            {
                unavailable.Add(new ModListFetchFailure(action.Name, Strings.ModList_ReasonNoListing));
                continue;
            }

            InstallTarget target;

            if (action.IsAddon)
            {
                if (AppServices.Addons.ById(modId) is not { } addon)
                {
                    unavailable.Add(new ModListFetchFailure(
                        action.Name, Strings.ModList_ReasonNoAddonListing));
                    continue;
                }

                if (addon.ModId is { } parentId && !parents.Contains(parentId))
                {
                    var parentName = catalog.GetValueOrDefault(parentId)?.Name
                        ?? Text(Strings.ModList_UnnamedModFormat, parentId);

                    unavailable.Add(new ModListFetchFailure(
                        action.Name,
                        Text(Strings.ModList_ReasonOrphanedAddonFormat, parentName)));
                    continue;
                }

                target = InstallTarget.For(addon);
            }
            else
            {
                if (!catalog.TryGetValue(modId, out var mod))
                {
                    unavailable.Add(new ModListFetchFailure(action.Name, Strings.ModList_ReasonNoListing));
                    continue;
                }

                // This app's own listing, on a list someone else wrote: never fetched - it would put a
                // second copy of the manager into BepInEx\plugins (see BrowseViewModel.IsSelf).
                if (BrowseViewModel.IsSelf(mod))
                {
                    unavailable.Add(new ModListFetchFailure(action.Name, Strings.ModList_ReasonNoListing));
                    continue;
                }

                target = InstallTarget.For(mod);
            }

            var id = modId.ToString();
            var wanted = action.TargetVersion?.Trim();

            //
            // Per entry, not per apply.
            //
            // Every call below asks sp-mod.com about ONE mod, and a list is as long as the operator
            // made it - 76 entries in the case that found this. Letting one refused or unanswered
            // question escape the loop throws away the other 75 and reports the whole apply as
            // broken, which is both wrong and unactionable: the user cannot tell which entry did it.
            // `unavailable` is the channel that already exists for "this one can't be had, and here
            // is why", so that is where these go.
            //
            // Cancellation is not caught - a user who cancelled meant the whole thing.
            //
            try
            {
                // A list entry with no version string at all is the one case where newest is the
                // only thing it can mean, so it needs no asking.
                if (string.IsNullOrWhiteSpace(wanted))
                {
                    var newest = await NewestAsync(id, action.IsAddon, sptVersion, ct);

                    if (newest is null)
                        unavailable.Add(new ModListFetchFailure(
                            action.Name, Strings.ModList_ReasonNoVersions));
                    else ready.Add(new ModListDownload(action, target, newest, IsSubstitute: false));

                    continue;
                }

                var published = await VersionsAsync(id, action.IsAddon, wanted, ct);

                var exact = published.FirstOrDefault(v => action.VersionId is not null && v.Id == action.VersionId)
                    ?? published.FirstOrDefault(v => string.Equals(v.Version?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));

                if (exact is not null)
                {
                    ready.Add(new ModListDownload(action, target, exact, IsSubstitute: false));
                    continue;
                }

                var replacement = await NewestAsync(id, action.IsAddon, sptVersion, ct);

                if (replacement is null)
                    unavailable.Add(new ModListFetchFailure(
                        action.Name, Text(Strings.ModList_ReasonVersionGoneFormat, wanted)));
                else
                    changes.Add(new ModListVersionChange(action, target, wanted, replacement));
            }
            catch (Exception ex) when (ex is SpModApiException or HttpRequestException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                unavailable.Add(new ModListFetchFailure(
                    action.Name, Text(Strings.ModList_ReasonApiFailedFormat, ex.Message)));
            }
        }

        return new ModListResolution(ready, changes, unavailable);
    }

    //
    // The published versions matching a wanted version string. Addons are asked live like mods
    // rather than read from their cache: the cache only carries each addon's six most recent
    // versions, and a list can pin one older than that.
    //
    // A version string the API's own semver parser refuses. Not an error to report: the question
    // "which published versions match this?" has an answer, and the answer is none.
    //
    // sp-mod.com parses filter[version] as a semver constraint and returns 400 for anything it does
    // not recognise - "2.0.0-BE" and "1.0.0-nope" are refused where "1.0.0-beta" is not. Mod authors
    // do not consult that parser before naming a build, so a list captured from a real install can
    // carry one, and treating it as a failure would strand the entry with a message about the API
    // instead of offering the substitution that is actually available.
    private static bool IsUnmatchableFilter(SpModApiException ex) =>
        ex.StatusCode == HttpStatusCode.BadRequest;

    private static async Task<IReadOnlyList<ModVersion>> VersionsAsync(
        string id, bool isAddon, string wanted, CancellationToken ct)
    {
        try
        {
            return await PublishedAsync(id, isAddon, wanted, ct);
        }
        catch (SpModApiException ex) when (IsUnmatchableFilter(ex))
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<ModVersion>> PublishedAsync(
        string id, bool isAddon, string wanted, CancellationToken ct)
    {
        if (!isAddon)
        {
            var mods = await AppServices.SpModApi.GetModVersionsAsync(
                id, new ModVersionsQuery { FilterVersion = wanted, PerPage = 5 }, ct);
            return mods.Data;
        }

        var addons = await AppServices.SpModApi.GetAddonVersionsAsync(
            id, new AddonVersionsQuery { FilterVersion = wanted, PerPage = 5 }, ct);
        return [.. addons.Data.Select(AsModVersion)];
    }

    //
    // The newest version of a mod for the SPT version installed here, or - when it has none, or the
    // installed version is not known - the newest there is. "Newest" for an entry that names no
    // version, and the replacement offered for one that is gone, both mean a version that runs here
    // before one that does not.
    //
    private static async Task<ModVersion?> NewestAsync(string id, bool isAddon, string? sptVersion, CancellationToken ct)
    {
        if (!isAddon && !string.IsNullOrWhiteSpace(sptVersion))
        {
            try
            {
                var forSpt = await AppServices.SpModApi.GetModVersionsAsync(
                    id, new ModVersionsQuery { FilterSptVersion = sptVersion, Sort = "-published_at", PerPage = 1 }, ct);
                if (forSpt.Data.FirstOrDefault() is { } found) return found;
            }
            catch (SpModApiException ex) when (IsUnmatchableFilter(ex))
            {
                // An SPT version the API does not know: the newest overall, below.
            }
        }

        if (!isAddon)
        {
            // Sorted, as every other caller asks: left to itself the API answers oldest first
            // (measured 2026-09-25 on APBS - 2.0.3 of Nov 2025 came back ahead of 2.2.1), which
            // quietly made "newest" the oldest release it had.
            var mods = await AppServices.SpModApi.GetModVersionsAsync(
                id, new ModVersionsQuery { Sort = "-published_at", PerPage = 5 }, ct);
            return mods.Data.FirstOrDefault();
        }

        var addons = await AppServices.SpModApi.GetAddonVersionsAsync(
            id, new AddonVersionsQuery { Sort = "-published_at", PerPage = 5 }, ct);
        return addons.Data.Select(AsModVersion).FirstOrDefault();
    }

    //
    // An addon version carries everything the download pipeline reads. ModVersionConstraint is
    // dropped deliberately: it decides which version to take, and by here that is already decided.
    //
    private static ModVersion AsModVersion(AddonVersion version) => new()
    {
        Id = version.Id,
        Version = version.Version,
        Description = version.Description,
        Link = version.Link,
        ContentLength = version.ContentLength,
        Downloads = version.Downloads,
        PublishedAt = version.PublishedAt,
    };

    //
    // Completes once the queue has finished with this item, whatever the outcome.
    //
    // The item can finish between the first check and the handler being attached, so the check is
    // repeated afterwards - a queued mod that installs quickly would otherwise wait forever.
    //
    private static Task<DownloadQueueItemStatus> WaitForAsync(DownloadQueueItemViewModel item)
    {
        if (item.IsFinished) return Task.FromResult(item.Status);

        var completion = new TaskCompletionSource<DownloadQueueItemStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(item.Status) || !item.IsFinished) return;

            item.PropertyChanged -= OnChanged;
            completion.TrySetResult(item.Status);
        }

        item.PropertyChanged += OnChanged;

        if (item.IsFinished)
        {
            item.PropertyChanged -= OnChanged;
            completion.TrySetResult(item.Status);
        }

        return completion.Task;
    }

    //
    // A download-only apply of a list the user hasn't finished installing by hand yet: the same
    // version is still pending in the ledger and its archive is still where it was saved. Fetching
    // it again would only leave a "Foo (2).zip" beside the first, every time the list is applied.
    //
    private static bool AlreadySaved(InstallTarget target, ModVersion version)
    {
        if (AppServices.DownloadLedger.Find(target.Id, target.IsAddon) is not
            { State: DownloadState.Pending } saved) return false;

        var same = (version.Id != 0 && saved.VersionId == version.Id)
            || string.Equals(saved.Version, version.Version, StringComparison.Ordinal);

        if (!same || !File.Exists(saved.ArchivePath)) return false;

        AppLog.Info("ModLists", $"{target.Name} {saved.Version} already saved at {saved.ArchivePath}; not fetched again");
        return true;
    }

    // The queue's list is bound to the Downloads page, so it is only ever touched on the UI thread.
    private static Task<DownloadQueueItemViewModel> EnqueueAsync(
        InstallTarget target,
        string versionLabel,
        string installPath,
        Func<Task<ModVersion?>> resolveVersion,
        long? totalBytes,
        ModListDownloadMode mode)
    {
        // The size is passed in rather than left for the worker to discover: every version is
        // already resolved by this point, so the whole apply can be sized before it starts.
        DownloadQueueItemViewModel Enqueue() =>
            AppServices.DownloadQueue.Enqueue(
                target, versionLabel, installPath, resolveVersion, totalBytes: totalBytes,
                downloadOnly: mode.DownloadOnly, downloadSubfolder: mode.Subfolder);

        var dispatcher = Application.Current?.Dispatcher;

        return dispatcher is null || dispatcher.CheckAccess()
            ? Task.FromResult(Enqueue())
            : dispatcher.InvokeAsync(Enqueue).Task;
    }

    private static void CancelAll(IEnumerable<DownloadQueueItemViewModel> items)
    {
        void Cancel()
        {
            foreach (var item in items.Where(i => i.CanCancel)) item.CancelCommand.Execute(null);
        }

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess()) Cancel();
        else dispatcher.InvokeAsync(Cancel);
    }

    //
    // Everything a list could have added to it by hand: the mods this install has, and the ones
    // sp-mod.com publishes.
    //
    // Both halves are answered from data already here - the scan and the catalog cache - so opening
    // the picker never waits on the network. The catalog half is offered even with no SPT folder
    // set: naming mods for a list you are building for someone else doesn't need an install.
    //
    public async Task<ModListAddOptions> AddOptionsAsync()
    {
        var install = await ReadInstallAsync();
        var installed = install is null ? new List<ModListAddOption>() : InstalledOptions(install);

        return new ModListAddOptions(installed, CatalogOptions(installed));
    }

    //
    // One option per installed mod, pinned to the version that is here - the same entry capturing
    // the whole install would have written for it, produced by the same code, so a mod added by
    // hand and a mod captured are indistinguishable in the list afterwards.
    //
    // Disabled mods are included. Capture leaves them out because it records what is running; this
    // is someone naming a mod deliberately, and a mod they have but have set aside is a perfectly
    // reasonable thing to name.
    //
    private static List<ModListAddOption> InstalledOptions(ModListInstall install)
    {
        var versions = CatalogVersions();
        var addonVersions = CatalogAddonVersions();
        var options = new List<ModListAddOption>();

        foreach (var candidate in install.Candidates)
        {
            var entry = ModListCapture
                .BuildEntries([candidate], versions, includeDisabled: true, addonVersions)
                .FirstOrDefault();

            if (entry is null) continue;
            if (options.Any(o => ModListEntries.SameMod(o.Entry, entry))) continue;

            options.Add(new ModListAddOption
            {
                Entry = entry,
                IsDisabled = candidate.IsDisabled,
                IsInstalled = true,
            });
        }

        return [.. options.OrderBy(o => o.Entry.Name, StringComparer.OrdinalIgnoreCase)];
    }

    //
    // One option per catalog listing, unpinned - see ModListEntries.ForCatalogMod for why a mod
    // this install has never had is named without a version.
    //
    // Left in the order the cache holds them, which is most downloaded first, so the picker has
    // something worth showing before anything is typed.
    //
    // This app's own listing is left out for the same reason Browse hides it: it isn't a mod, and a
    // list naming it would try to drop a second copy of the manager into BepInEx\plugins.
    //
    private static List<ModListAddOption> CatalogOptions(IReadOnlyList<ModListAddOption> installed)
    {
        var options = new List<ModListAddOption>();

        foreach (var mod in AppServices.ModCache.AllMods)
        {
            if (string.IsNullOrWhiteSpace(mod.Name)) continue;
            if (string.Equals(mod.Id.ToString(), SelfMod.ModId, StringComparison.Ordinal)) continue;

            var entry = ModListEntries.ForCatalogMod(mod.Id, mod.Name!, mod.Guid);

            options.Add(new ModListAddOption
            {
                Entry = entry,
                IsInstalled = installed.Any(o => ModListEntries.SameMod(o.Entry, entry)),
                Author = mod.Owner?.Name,
            });
        }

        return options;
    }

    // ------------------------------------------------------------------ collections, one mod at a time

    //
    // The Workshop's "Add to Collection": which of this install's own lists a mod is on, and putting
    // it on or taking it off. Only lists made here can be changed - one received from someone else
    // or served by a server is theirs, and is shown (see Collections) but not offered.
    //

    /// <summary>Every list this install holds, newest first - what "your collections" means here.</summary>
    public static IReadOnlyList<ModList> Collections() =>
        [.. AppServices.ModLists.Load().Lists.OrderByDescending(l => l.UpdatedAt)];

    /// <summary>The lists holding <paramref name="mod"/>.</summary>
    public static IReadOnlyList<ModList> CollectionsWith(Mod mod) =>
        [.. Collections().Where(l => ModListEntries.Contains(l.Entries, Probe(mod)))];

    //
    // The entry the mod goes onto a list as: pinned to the version installed here, exactly as a
    // capture of the install would write it, or - for a mod this install does not have - the
    // unpinned catalog entry the Collections page's own picker adds.
    //
    public static ModListEntry EntryFor(Mod mod, InstalledModCardViewModel? installed)
    {
        if (installed is not null)
        {
            var entry = ModListCapture
                .BuildEntries([ModListCandidates.From(installed)], CatalogVersions(), includeDisabled: true, CatalogAddonVersions())
                .FirstOrDefault(e => e.ModId == mod.Id && !e.IsAddon);

            if (entry is not null) return entry;
        }

        return ModListEntries.ForCatalogMod(mod.Id, mod.Name ?? mod.Id.ToString(), mod.Guid);
    }

    /// <summary>Puts <paramref name="mod"/> on exactly the lists in <paramref name="onLists"/> among
    /// the editable ones, and on a new list named <paramref name="newListName"/> when one is given.
    /// Returns how many lists it was added to and taken off.</summary>
    public static (int Added, int Removed) SetCollections(
        Mod mod,
        InstalledModCardViewModel? installed,
        IReadOnlySet<Guid> onLists,
        string? newListName)
    {
        var probe = Probe(mod);
        var added = 0;
        var removed = 0;

        foreach (var list in Collections().Where(l => l.IsEditable))
        {
            var has = ModListEntries.Contains(list.Entries, probe);
            var wants = onLists.Contains(list.Id);
            if (has == wants) continue;

            var entries = wants
                ? list.Entries.Append(EntryFor(mod, installed))
                : list.Entries.Where(e => !ModListEntries.SameMod(e, probe));

            AppServices.ModLists.ReplaceEntries(list.Id, ModListEntries.Sorted(entries));
            if (wants) added++;
            else removed++;
        }

        if (!string.IsNullOrWhiteSpace(newListName))
        {
            //
            // Additive, unlike a capture: a capture describes the whole install, so applying it
            // sets aside what it does not name. A collection started from one mod describes nothing
            // of the kind, and applying it should only add. (It can be changed on the Collections
            // page.)
            //
            var now = DateTimeOffset.UtcNow;
            var list = new ModList
            {
                Id = Guid.NewGuid(),
                Name = newListName.Trim(),
                Origin = ModListOrigin.Local,
                Policy = ModListPolicy.Additive,
                SptVersion = AppServices.SptEnvironment.InstalledVersion,
                CreatedAt = now,
                UpdatedAt = now,
            };
            list.Entries.Add(EntryFor(mod, installed));
            AppServices.ModLists.Add(list);
            added++;
        }

        return (added, removed);
    }

    // What a list's entry for this mod is compared on: its id (see ModListEntries.SameMod).
    private static ModListEntry Probe(Mod mod) =>
        ModListEntries.ForCatalogMod(mod.Id, mod.Name ?? mod.Id.ToString(), mod.Guid);

    //
    // Pins a version id from the catalog's own embedded version list, so capture never touches the
    // network. A catalog Mod carries only its six most recent versions, so anything older resolves
    // to a mod id and a version string without an id - which the planner and applier both handle.
    //
    private static ModListCapture.VersionLookup CatalogVersions()
    {
        var byId = AppServices.ModCache.AllMods
            .Where(m => m.Versions is { Count: > 0 })
            .GroupBy(m => m.Id)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ModVersionSummary>?)g.First().Versions);

        return modId => byId.TryGetValue(modId, out var versions) ? versions : null;
    }

    // The same for addons, off the addon cache. Also offline - the cache carries each addon's
    // versions, so a capture of an installed addon pins its version id without a lookup.
    private static ModListCapture.AddonVersionLookup CatalogAddonVersions()
    {
        var byId = AppServices.Addons.AllAddons
            .Where(a => a.Versions is { Count: > 0 })
            .GroupBy(a => a.Id)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AddonVersionSummary>?)g.First().Versions);

        return addonId => byId.TryGetValue(addonId, out var versions) ? versions : null;
    }
}
