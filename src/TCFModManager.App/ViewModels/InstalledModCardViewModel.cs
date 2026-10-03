using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// Display wrapper for the Installed page's card grid, merging a mod's client and server entries and matching it against the sp-mod.com catalog.
public sealed partial class InstalledModCardViewModel : LocalizedViewModel
{
    public required string Name { get; init; }

    // The version to show as "installed". The install manifest's version when this app
    // installed the mod, since that is what was actually fetched; otherwise whatever the files on
    // disk report. See InstalledVersionDetail for the file-reported versions when they differ.
    public string? InstalledVersion { get; init; }

    //
    // The versions the files themselves report, shown only when they disagree with each
    // other or with the version recorded at install time. Null when there is nothing to explain.
    //
    // Held as a key and the values it takes rather than as finished text: everything else on this
    // card is read on every get, so composing the line when the card is built would leave it in
    // whatever language the last scan ran in. A null value stands for a version that could not be
    // read, which is the only thing a null means in any of these lines.
    //
    public (string Key, object?[] Values)? VersionDetail { get; init; }

    // Wraps whatever VersionDetail says rather than replacing it, so it is carried separately.
    public bool IsVersionManuallyConfirmed { get; init; }

    public string? InstalledVersionDetail
    {
        get
        {
            var detail = VersionDetail is { } held
                ? Text(
                    LocalizationService.Get(held.Key),
                    [.. held.Values.Select(v => v ?? Strings.Common_Unknown)])
                : null;

            if (!IsVersionManuallyConfirmed) return detail;

            return detail is null
                ? Strings.Installed_DetailManuallyConfirmed
                : Text(Strings.Installed_DetailManuallyConfirmedFormat, detail);
        }
    }

    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // Earliest InstalledAt across the merged entries.
    public DateTimeOffset? InstalledAt { get; init; }

    // True when any half of this mod is a client one - a BepInEx plugin, a patcher, or both.
    public bool HasClient { get; init; }

    // True specifically for a BepInEx\plugins half, as opposed to a patcher one. HasClient without
    // this is a patcher standing on its own.
    public bool HasPlugin { get; init; }

    // True when any half of this mod sits in BepInEx\patchers. Usually alongside a plugin; on its
    // own for a patcher that couldn't be tied back to a mod.
    public bool HasPatcher { get; init; }

    public bool HasServer { get; init; }

    //
    // Which halves of the install this mod occupies. A patcher is called out rather than folded
    // into "Client", since on its own it's the difference between a card that reads as a mod whose
    // identity couldn't be worked out and one that reads as what it actually is.
    //
    public string TargetSummary
    {
        get
        {
            //
            // Every combination is its own whole phrase rather than the three words joined with a
            // plus. A language can put them in another order or join them another way, and "Client
            // only" is not "Client" with a word appended to it.
            //
            return (HasPlugin, HasPatcher, HasServer) switch
            {
                (true, false, false) => Strings.Installed_TargetClientOnly,
                (false, true, false) => Strings.Installed_TargetPatcherOnly,
                (false, false, true) => Strings.Installed_TargetServerOnly,
                (true, true, false) => Strings.Installed_TargetClientPatcher,
                (true, false, true) => Strings.Installed_TargetClientServer,
                (false, true, true) => Strings.Installed_TargetPatcherServer,
                (true, true, true) => Strings.Installed_TargetClientPatcherServer,
                _ => Strings.Installed_TargetUnknown,
            };
        }
    }

    // The newest version published on sp-mod.com that runs on the installed SPT, else the newest
    // overall when none does. Null when the catalog hasn't loaded yet or nothing matched.
    public string? LatestPublishedVersion { get; init; }

    // The version an update would install - the newest release that runs on the installed SPT,
    // which is not always the newest published. Null when no update is available.
    public string? UpdateVersion { get; init; }

    // The matched sp-mod.com listing's UpdatedAt. Null under the same conditions as LatestPublishedVersion.
    public DateTimeOffset? LatestUpdatedAt { get; init; }

    //
    // What the "Latest published" line shows. A patcher standing on its own gets its own wording:
    // not every patcher in BepInEx\patchers is an sp-mod.com mod at all - some are general BepInEx
    // utilities from elsewhere that a mod bundles alongside itself (FixPluginTypesSerialization is
    // the common one) - so for those "not found on sp-mod.com" reads as a failed lookup when
    // nothing actually went wrong. Everything else keeps the wording it had.
    //
    public string LatestPublishedText => (LatestPublishedVersion, ModId, HasPlugin || HasServer) switch
    {
        ({ } version, _, _) => version,
        (null, _, _) when IsAddon => Strings.Installed_LatestAddonUnlisted,
        (null, not null, _) => Strings.Common_Unknown,
        (null, null, false) when HasPatcher => Strings.Installed_LatestPatcherNotListed,
        _ => Strings.Installed_LatestNotFound,
    };

    // The matched sp-mod.com listing's actual display Name, often different from the installed
    // folder/package Name. Null when nothing matched.
    public string? MatchedModName { get; init; }

    //
    // The name the mod gives itself in its own DLL - an SPT 4.x server mod's declared Name, or its
    // [BepInPlugin] name. Only ever set on a card nothing in the catalog matched: a matched card
    // keeps the catalog's name (R2 of the server mod metadata design).
    //
    public string? DeclaredName { get; init; }

    // The title the card leads with: the real sp-mod.com display name when there is one, then the
    // name the mod declares for itself, otherwise the folder/package Name.
    public string DisplayTitle => MatchedModName ?? DeclaredName ?? Name;

    // The raw installed folder/package name, shown as a secondary line under DisplayTitle only
    // when the title came from somewhere else and differs from the folder name.
    public string? FolderNameIfDifferent =>
        (MatchedModName ?? DeclaredName) is { } title && !string.Equals(title, Name, StringComparison.OrdinalIgnoreCase)
            ? Name
            : null;

    // The matched sp-mod.com listing's GUID. Null when nothing matched.
    public string? Guid { get; init; }

    // The matched sp-mod.com listing's primary author. Null when nothing matched. Backs the
    // "@author" search syntax in InstalledViewModel's search box.
    public string? Author { get; init; }

    // The matched sp-mod.com listing's category title, for the Installed page's Category filter.
    // Null when nothing matched, or for an addon - addons carry no category of their own.
    public string? CategoryTag { get; init; }

    //
    // How many addons this mod has published. Answered from the cached addon catalog during the
    // same pass that builds the card, so the Installed page's "Has addons" filter needs no lookup.
    //
    public int AddonCount { get; init; }

    public bool HasAddons => AddonCount > 0;

    //
    // True when this mod declares a dependency on another installed mod. Filled in after the cards
    // are built, from the dependency graph InstalledViewModel builds off the same scan - the graph
    // reads BepInEx's own metadata, so unlike Browse's badge this answer is complete and offline.
    //
    [ObservableProperty]
    private bool _hasDependencies;

    // True when the matched catalog listing is flagged Fika compatible.
    public bool IsFikaCompatible { get; init; }

    // True when the matched catalog listing is flagged as containing ads.
    public bool ContainsAds { get; init; }

    // True when the matched catalog listing is flagged as containing AI content.
    public bool ContainsAiContent { get; init; }

    // True when a newer, SPT-compatible version than InstalledVersion is published. False when
    // there's no newer or no compatible newer version. Null when it can't be determined (no catalog match,
    // unparsable version, or no installed SPT version detected).
    public bool? UpdateAvailable { get; init; }

    // Client half's folder (or, for a loose top-level DLL, the DLL itself). Null when there's no client half.
    public string? ClientFolderPath { get; init; }

    // Where the "Client folder" button goes - see FolderToOpen.
    public string? ClientFolderLink => FolderToOpen(ClientFolderPath);

    // Server half's folder. Null when there's no server half.
    public string? ServerFolderPath { get; init; }

    // Where the "Server folder" button goes - see FolderToOpen.
    public string? ServerFolderLink => FolderToOpen(ServerFolderPath);

    //
    // The folder a path should actually open. A mod that ships as a single loose DLL has no folder
    // of its own, and the scanner names it after the file - so its recorded path is
    // "...\BepInEx\plugins\AOReplacer.dll", and opening that would ask Windows to run the DLL
    // rather than show where it lives. Anything ending in .dll opens its parent instead.
    //
    // Matched on the .dll suffix specifically, NOT on "does it have an extension": plenty of real
    // mod folders are named with dots in them (Tyfon.AutoDeposit, IcyClawz.MunitionsExpert,
    // RaiRai.ColorConverterAPI), and Path.HasExtension would send every one of those to its parent.
    //
    private static string? FolderToOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return path;

        // Cut at the last separator by hand rather than going through Path.GetDirectoryName, which
        // does not treat "\" as a separator anywhere but Windows - the app only ever runs there,
        // but these view models are exercised headless on Linux and the Path version silently
        // returned an empty string for every Windows path it was given.
        var cut = path.LastIndexOfAny(['\\', '/']);
        return cut > 0 ? path[..cut] : null;
    }

    // The client/server halves' folder (or loose-DLL) *names*, as InstalledModScanner reports them -
    // what a manual version override records as this mod's Folders, so a later rescan still ties the
    // override back to this card. Null when there's no corresponding half.
    public string? ClientFolderName { get; init; }
    public string? ServerFolderName { get; init; }

    // A single path for this card for existing callers/diagnostics; client wins when both exist.
    public required string FolderPath { get; init; }

    // The matched sp-mod.com listing's numeric id. Null when nothing matched. Holds an ADDON id
    // when IsAddon is true - the two are separate sequences, so never compare it to a mod id
    // without checking IsAddon first.
    public int? ModId { get; init; }

    // Fork: ModId when it is a mod's (for the right-click menu); null for an addon.
    public int? CatalogModId => IsAddon ? null : ModId;

    // Fork: the catalog listing's preview image, for the Steam-style card. Looked up when a card
    // first draws rather than carried through the scan: the scan does not need it, and the catalog
    // is already in memory by the time anything is on screen.
    //
    // Mods only: an addon's ModId is an addon id, a separate sequence that would match some
    // unrelated mod's listing here.
    public string? Thumbnail => ModId is { } id && !IsAddon
        ? AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id)?.Thumbnail
        : null;

    //
    // True when this card is an addon rather than a mod. Only ever set from an install record this
    // app wrote: addons carry no GUID and their ids don't overlap mods', so there is nothing for
    // the catalog matcher to recognise a hand-installed addon by - one of those shows up as an
    // ordinary unmatched card, exactly as a hand-installed mod that matches nothing does.
    //
    public bool IsAddon { get; init; }

    // The name of the mod this addon attaches to. Null for a mod, or when the parent isn't in the
    // cached catalog.
    public string? ParentModName { get; init; }

    // The parent mod's installed version - what every one of this addon's version constraints is
    // measured against. Null for a mod, or when the parent isn't installed.
    public string? ParentInstalledVersion { get; init; }

    // The card's "Addon for X" line, or null for an ordinary mod.
    public string? AddonSubtitle => IsAddon
        ? ParentModName is not null ? Text(Strings.Installed_AddonForFormat, ParentModName) : Strings.Installed_Addon
        : null;

    // True when this mod has an install record this app itself wrote, meaning Remove can delete
    // exactly those files. False for anything installed by hand or from outside the app, including a
    // mod with only a manually-confirmed version (see IsManualOverride).
    public bool IsAppManaged { get; init; }

    // True when InstalledVersion comes from the user manually confirming/overriding it (Installed
    // page's "manage version" controls) rather than from an app install or the files on disk.
    public bool IsManualOverride { get; init; }

    //
    // Folders this app's install record placed that the scan can no longer find, and whether the
    // install itself gave up part way through (InstalledModRecord.Incomplete, recorded since
    // partial installs were tracked and never shown anywhere until now).
    //
    // Either one means the mod on disk is not the mod that was installed - and the version it
    // reports is still the recorded one, so nothing else gives it away. Reinstalling is the fix,
    // which is what a mod list now does for it too - see ModListCandidate.IsIncomplete.
    //
    public IReadOnlyList<string> MissingFolders { get; init; } = [];

    public bool WasPartlyInstalled { get; init; }

    //
    // Set after a scan by InstalledViewModel when this folder is one a removal had to leave because
    // it holds files the removed mod didn't install. Null otherwise.
    //
    [ObservableProperty]
    private string? _leftoverSummary;

    //
    // Set after a scan by InstalledViewModel (ModConflicts.Apply): what this mod clashes with at load
    // time and where to see more, or null when it clashes with nothing (OPEN-11).
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    [NotifyPropertyChangedFor(nameof(StatusTooltip))]
    private string? _conflictSummary;

    public bool HasConflicts => ConflictSummary is not null;

    public bool IsIncompleteInstall => MissingFolders.Count > 0 || WasPartlyInstalled;

    // One whole sentence per count: the verb has to agree with how many folders are missing, and
    // a verb spliced into a sentence cannot be translated.
    public string? IncompleteSummary => !IsIncompleteInstall
        ? null
        : MissingFolders.Count == 0
            ? Strings.Installed_IncompletePartial
            : Strings.Installed_IncompleteMissing(
                MissingFolders.Count,
                string.Join(Strings.Common_ListSeparator, MissingFolders));

    //
    // A Monitor mode download of this mod that a scan found on disk but nobody has confirmed yet -
    // installed as far as its files show, or only partly. Set after a scan by InstalledViewModel,
    // the same way Lists is; null when there is none, which is every card outside Monitor mode.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingDownload))]
    [NotifyPropertyChangedFor(nameof(CanConfirmDownload))]
    [NotifyPropertyChangedFor(nameof(DownloadSummary))]
    [NotifyPropertyChangedFor(nameof(StatusTooltip))]
    private PendingDownload? _pendingDownload;

    public bool HasPendingDownload => PendingDownload is not null;

    // Only a download whose every file is on disk at its size. A partial one is never confirmed,
    // by the prompt or by hand - a half-copied install is exactly what the note is there to show.
    public bool CanConfirmDownload => PendingDownload?.Match.Kind == DownloadMatchKind.Installed;

    public string? DownloadSummary => PendingDownload switch
    {
        null => null,
        { Match.Kind: DownloadMatchKind.Installed } p =>
            Text(Strings.Installed_DownloadLooksInstalledFormat, p.Download.Version),
        { } p => Text(Strings.Installed_DownloadPartialFormat, p.Download.Version),
    };

    //
    // Every scan entry merged into this card: both halves of a client+server mod, and both copies
    // of a mod left in a container and in that container's ".disabled" sibling. What the disable
    // commands actually move, and what ModDependencyGraph is keyed on.
    //
    public IReadOnlyList<InstalledMod> Entries { get; init; } = [];

    //
    // The mod lists that name this mod, filled in after a scan by InstalledViewModel (the card
    // itself knows nothing about lists). One badge each, so a glance at the card says which sets
    // it belongs to - and therefore which list switches won't cost a download.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInAnyList))]
    [NotifyPropertyChangedFor(nameof(ShowLists))]
    private IReadOnlyList<string> _lists = [];

    // The page-wide toggle, pushed down per card so one binding answers "draw the chips or not"
    // rather than every template having to combine two conditions itself.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLists))]
    private bool _showBadges = true;

    public bool IsInAnyList => Lists.Count > 0;

    public bool ShowLists => ShowBadges && Lists.Count > 0;

    // Pinned against a list's Exclusive sweep - see ModListStore.SetPinned. Filled in after a scan
    // by InstalledViewModel, like Lists.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinLabel))]
    [NotifyPropertyChangedFor(nameof(PinGlyph))]
    [NotifyPropertyChangedFor(nameof(PinTooltip))]
    private bool _isPinned;

    public string PinLabel => IsPinned ? Strings.Installed_Unpin : Strings.Installed_Pin;

    public string PinGlyph => IsPinned ? "PinOff24" : "Pin24";

    public string PinTooltip => IsPinned ? Strings.Installed_UnpinToolTip : Strings.Installed_PinToolTip;

    // True when every one of this mod's folders sits under a ".disabled" container, so SPT loads none of it.
    public bool IsDisabled => Entries.Count > 0 && Entries.All(e => e.IsDisabled);

    // True when some of this mod's folders are disabled and some aren't - one half of a
    // client+server mod parked on its own, or a half-completed move leaving copies in both places.
    public bool IsMixedState => Entries.Any(e => e.IsDisabled) && Entries.Any(e => !e.IsDisabled);

    // The same folder sitting in both a container and its ".disabled" sibling, which the disable
    // toggle can't settle on its own - one of the two copies has to be set aside first. Worked out
    // once rather than per read: Entries never changes, and HasDuplicateFolders is bound on every
    // card and every list row, so a computed property here is evaluated constantly.
    private IReadOnlyList<ModDuplicatePair>? _duplicateFolders;

    public IReadOnlyList<ModDuplicatePair> DuplicateFolders =>
        _duplicateFolders ??= ModDisableService.DuplicatePairs(Entries);

    public bool HasDuplicateFolders => DuplicateFolders.Count > 0;

    // Dims the whole card (and the group-view row) while disabled.
    public double CardOpacity => IsDisabled ? 0.45 : 1.0;

    // Ticked in the flat grid's multi-select mode. Cards are rebuilt on every scan, so a selection
    // doesn't survive one.
    [ObservableProperty]
    private bool _isSelected;

    //
    // Whether this mod is open in Cards view, and separately in List view.
    //
    // Held here rather than left to the CardExpander because every scan replaces every card:
    // closing the versions dialog triggers a rescan, which would otherwise snap shut the card you
    // opened it from. InstalledViewModel carries the open sets across a rebuild.
    //
    // Two flags rather than one because both views render the SAME card objects - Results and
    // ListItems are both built from _filtered - so a single shared flag meant opening a card in one
    // view silently opened the same mod in the other.
    //
    [ObservableProperty]
    private bool _isCardExpanded;

    [ObservableProperty]
    private bool _isRowExpanded;

    //
    // The user-defined group this mod is assigned to, or null when it's in none. Filled in by
    // InstalledViewModel from the group store after each scan and after every group change, rather
    // than read here - the card knows nothing about where groups are stored.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupLabel))]
    [NotifyPropertyChangedFor(nameof(IsGrouped))]
    private string? _groupName;

    // The same assignment as GroupName, by id - what the Group filter matches on, since two groups
    // can be given the same name.
    [ObservableProperty]
    private Guid? _groupId;

    public string GroupLabel => GroupName ?? Strings.Filter_Ungrouped;

    public bool IsGrouped => GroupName is not null;

    // The catalog listing's content flags as one line, or null when it carries none of them.
    public string? FlagsSummary
    {
        get
        {
            var flags = new List<string>();
            if (IsFikaCompatible) flags.Add(Strings.Common_FlagFikaCompatible);
            if (ContainsAds) flags.Add(Strings.Common_FlagContainsAds);
            if (ContainsAiContent) flags.Add(Strings.Common_FlagContainsAiContent);

            return flags.Count == 0 ? null : string.Join(Strings.Common_FlagSeparator, flags);
        }
    }

    // Where this mod's recorded version comes from, spelled out for the details view.
    public string SourceLabel => IsAppManaged
        ? Strings.Installed_SourceAppManaged
        : IsManualOverride
            ? Strings.Installed_SourceManualConfirmed
            : Strings.Installed_SourceManual;

    public string DisableToggleGlyph => IsDisabled ? "PlugConnected24" : "PlugDisconnected24";

    public string DisableToggleTooltip => IsDisabled
        ? Strings.Installed_CardEnableToolTip
        : Strings.Installed_CardDisableToolTip;

    // This mod's status, using the same vocabulary and icons as the Browse and Dependencies pages.
    // Everything here is installed by definition, so it's only ever disabled, up-to-date or outdated.
    // A conflict outranks the update state: the mod may not load at all.
    public ModStatus Status => IsDisabled
        ? ModStatus.Disabled
        : HasConflicts
            ? ModStatus.Conflict
            : UpdateAvailable switch
        {
            true => ModStatus.UpdateAvailable,
            false => ModStatus.Installed,
            _ => ModStatus.Unknown,
        };

    // A mixed state is neither cleanly enabled nor cleanly disabled, so it gets the conflict icon
    // rather than either side's.
    public string StatusGlyph => IsMixedState || IsIncompleteInstall
        ? "ErrorCircle24"
        : (NotForSptNote is not null || MissingDependencyNote is not null) && !IsDisabled
            ? "Warning24"
            : ModStatusDisplay.Glyph(Status);

    // Fork: sp-mod.com holds this mod's update back: it would break another installed mod. Set
    // after each scan by InstalledViewModel, from AppServices.HeldBack.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTooltip))]
    private string? _heldBackNote;

    // Fork: the installed version does not run on this install's SPT - see HeldBackUpdates.NotForSptNote.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTooltip), nameof(StatusGlyph))]
    private string? _notForSptNote;

    // Fork: what this mod declares it cannot run without, in its own files, that nothing enabled
    // provides - found without the network, so it covers mods installed by hand too. Set after each
    // scan by InstalledViewModel, from ModDependencyGraph.MissingOf.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTooltip), nameof(StatusGlyph))]
    private string? _missingDependencyNote;

    // Missing files first: it is the only one of these the card gives no other sign of.
    // A pending download next: the collapsed card's only sign of one is this tooltip.
    public string StatusTooltip =>
        IncompleteSummary
        ?? (IsDisabled ? null : MissingDependencyNote)
        ?? ConflictSummary
        ?? NotForSptNote
        ?? HeldBackNote
        ?? DownloadSummary
        ?? (HasDuplicateFolders
            ? Strings.Installed_StatusDuplicate
            : IsMixedState
                ? Strings.Installed_StatusMixed
                : UpdateAvailable == true && !IsDisabled && (UpdateVersion ?? LatestPublishedVersion) is { } target
                    ? Text(Strings.Installed_StatusUpdateFormat, target)
                    : ModStatusWording.Tooltip(Status));

    // Groups raw scan results into one card per distinct mod and looks up each against the cached
    // catalog for its latest published version.
    // <param name="installRecords">The current install's manifest. Each record names the folders it
    // placed, which identifies a mod exactly, and the version actually fetched, which beats whatever
    // the files on disk claim. Pass null or an empty list if the manifest doesn't apply.</param>
    public static List<InstalledModCardViewModel> BuildFrom(
        IEnumerable<InstalledMod> scanned,
        IReadOnlyList<Mod> catalog,
        string? installedSptVersion,
        IReadOnlyList<InstalledModRecord>? installRecords = null,
        IReadOnlyList<Addon>? addonCatalog = null)
    {
        // Addon records are held apart from mod records the whole way down: their ids belong to a
        // different sequence, so letting them into recordsByModId or the catalog index would have
        // addon 116 answer to mod 116.
        var addonRecords = (installRecords ?? []).Where(r => r.IsAddon).ToList();
        var modRecords = (installRecords ?? []).Where(r => !r.IsAddon).ToList();
        var addonRecordsByFolder = IndexByFolder(addonRecords);

        var recordsByFolder = IndexByFolder(modRecords);
        var recordsByModId = modRecords
            .GroupBy(r => r.ModId)
            .ToDictionary(g => g.Key, g => g.First());

        // Every per-catalog-mod value the matching tiers below compare against is derived once here
        // rather than recomputed for each installed mod. Without it, matching 120 installed mods
        // against a 3000-entry catalog re-normalizes and re-tokenizes 360,000 catalog entries.
        var index = CatalogIndex.Build(catalog);

        var scannedGroups = scanned
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, Entries: g.ToList()))
            .ToList();

        var resolved = scannedGroups
            .Select(g => (g.Key, g.Entries, Match: ResolveCatalogMatch(g.Key, g.Entries, index, recordsByFolder)))
            .ToList();

        //
        // An addon usually installs INTO its parent mod's folder rather than one of its own - a SAIN
        // preset lands in BepInEx\plugins\SAIN, a spawn notifier in the mod's own plugin folder - so
        // the folders its install record names are very often the parent's. A folder that resolves
        // to a catalog mod therefore stays that mod's, always: claiming it for the addon would make
        // the parent disappear from this page and come back wearing the addon's name.
        //
        // Only a folder nothing resolved to a mod, and that an addon install placed, is the addon's
        // own. An addon with no folder of its own has no card here and is shown inside its parent's
        // dialog instead, which is where it was installed from and where it updates.
        //
        var addonFolders = resolved
            .Where(g => g.Match is null && addonRecordsByFolder.ContainsKey(g.Key))
            .Select(g => (g.Key, g.Entries))
            .ToList();

        var claimed = addonFolders.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var groups = MergeRecordFolders(
                resolved.Where(g => !claimed.Contains(g.Key)).ToList(),
                recordsByFolder,
                index)
            .Select(g => (g.Entries, g.Match))
            .ToList();

        // Patchers first: folding one into the mod it belongs to can turn a client-only group into
        // a client group that still needs its server half found, and never the other way round.
        // One count per parent mod, worked out once rather than per card.
        var addonsByParent = (addonCatalog ?? [])
            .Where(a => a.ModId is not null)
            .GroupBy(a => a.ModId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var merged = MergeSplitClientServerHalves(MergePatcherFolders(groups));

        //
        // A record belongs to the card holding the folders it placed. A second copy of the same mod
        // installed by hand ("SomeMod - Copy") resolves to the same catalog listing, and taking the
        // record by listing alone made that copy app-managed too - so removing the copy (Remove, or
        // Keep this one on the original) went by the record and took the ORIGINAL's files instead.
        // Where some card holds the record's folders, every other card of that listing is the
        // copy it looks like. Where none does, the record still applies by listing, as before.
        //
        var ownedRecords = merged
            .Where(g => g.Match is not null
                && recordsByModId.TryGetValue(g.Match.Id, out var r)
                && HoldsRecordFolder(g.Entries, r))
            .Select(g => g.Match!.Id)
            .ToHashSet();

        var cards = merged
            .Select(g =>
            {
                var record = g.Match is not null && recordsByModId.TryGetValue(g.Match.Id, out var found)
                    && (!ownedRecords.Contains(g.Match.Id) || HoldsRecordFolder(g.Entries, found))
                    ? found
                    : null;

                return BuildCard(g.Entries, g.Match, installedSptVersion, record, addonsByParent);
            })
            .ToList();

        if (addonFolders.Count == 0) return cards;

        // Addon cards come last because an addon's update state is measured against its parent
        // mod's installed version, which the cards built above are what report.
        var addonsById = (addonCatalog ?? []).ToDictionary(a => a.Id, a => a);
        var parentVersionsById = cards
            .Where(c => c is { IsAddon: false, ModId: not null })
            .GroupBy(c => c.ModId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        cards.AddRange(addonFolders
            .GroupBy(g => addonRecordsByFolder[g.Key].ModId)
            .Select(g =>
            {
                var record = addonRecordsByFolder[g.First().Key];
                var addon = addonsById.GetValueOrDefault(record.ModId);
                var parent = addon?.ModId is { } parentId ? parentVersionsById.GetValueOrDefault(parentId) : null;
                var parentName = addon?.ModId is { } id
                    ? catalog.FirstOrDefault(m => m.Id == id)?.Name ?? parent?.DisplayTitle
                    : parent?.DisplayTitle;

                return BuildAddonCard(
                    g.SelectMany(x => x.Entries).ToList(), addon, record, parent, parentName);
            }));

        return cards;
    }

    //
    // One card for an addon this app installed. Everything it knows comes from the install record
    // and the cached addon listing - there is no folder/name/GUID matching involved, because an
    // addon has no GUID and its id shares no space with a mod's.
    //
    private static InstalledModCardViewModel BuildAddonCard(
        List<InstalledMod> entries,
        Addon? addon,
        InstalledModRecord record,
        InstalledModCardViewModel? parent,
        string? parentName)
    {
        var plugin = entries.FirstOrDefault(m => m is { Target: InstalledModTarget.Client, IsPatcher: false });
        var patcher = entries.FirstOrDefault(m => m is { Target: InstalledModTarget.Client, IsPatcher: true });
        var client = plugin ?? patcher;
        var server = entries.FirstOrDefault(m => m.Target == InstalledModTarget.Server);

        var fileVersion = client?.Version ?? server?.Version;
        var installedVersion = record.Version ?? fileVersion;

        // Only versions the installed parent mod actually satisfies count as an update: an addon
        // built for the next release of its parent isn't something this install can use yet.
        var parentVersion = parent?.InstalledVersion;
        var versions = (addon?.Versions ?? [])
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();

        var latest = versions.FirstOrDefault(v =>
            ModVersionMatcher.IsSatisfiedBy(v.ModVersionConstraint, parentVersion) == true);

        (string Key, object?[] Values)? detail = null;
        if (latest is null && versions.Count > 0)
        {
            var newest = versions[0];
            detail = (parentVersion, parentName) switch
            {
                (null, null) => (nameof(Strings.Installed_DetailParentMissingUnnamed), []),
                (null, _) => (nameof(Strings.Installed_DetailParentMissingFormat), [parentName]),
                (_, null) => (
                    nameof(Strings.Installed_DetailParentConstraintUnnamedFormat),
                    [newest.Version, newest.ModVersionConstraint, parentVersion]),
                _ => (
                    nameof(Strings.Installed_DetailParentConstraintFormat),
                    [newest.Version, parentName, newest.ModVersionConstraint, parentVersion]),
            };
        }
        else if (fileVersion is not null && !VersionsAreEquivalent(record.Version, fileVersion))
        {
            detail = (nameof(Strings.Installed_DetailFilesReportFormat), [fileVersion]);
        }

        return new InstalledModCardViewModel
        {
            Name = client?.Name ?? server!.Name,
            InstalledVersion = installedVersion,
            VersionDetail = detail,
            IsVersionManuallyConfirmed = !record.IsAppManaged,
            InstalledAt = record.InstalledAt, // Fork: as above
            HasClient = client is not null,
            HasPlugin = plugin is not null,
            HasPatcher = patcher is not null,
            HasServer = server is not null,
            LatestPublishedVersion = latest?.Version,
            LatestUpdatedAt = addon?.UpdatedAt,
            MatchedModName = addon?.Name ?? record.Name,
            Author = addon?.Owner?.Name,
            ContainsAds = addon?.ContainsAds == true,
            ContainsAiContent = addon?.ContainsAiContent == true,
            UpdateAvailable = ModVersionComparer.IsUpdateAvailable(installedVersion, latest?.Version),
            ClientFolderPath = client?.FolderPath,
            ServerFolderPath = server?.FolderPath,
            ClientFolderName = client?.Name,
            ServerFolderName = server?.Name,
            FolderPath = client?.FolderPath ?? server!.FolderPath,
            ModId = record.ModId,
            IsAddon = true,
            ParentModName = parentName,
            ParentInstalledVersion = parentVersion,
            IsAppManaged = record.IsAppManaged,
            IsManualOverride = !record.IsAppManaged,
            Entries = entries,
        };
    }

    // Maps every folder a record identifies - whether an app-managed install placed it or a manual
    // version override names it - to that record. A folder claimed by two different mods is dropped
    // rather than resolved arbitrarily.
    private static Dictionary<string, InstalledModRecord> IndexByFolder(IReadOnlyList<InstalledModRecord>? records)
    {
        var index = new Dictionary<string, InstalledModRecord>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records ?? [])
        {
            foreach (var folder in InstalledModFolders.Resolve(record))
            {
                if (index.TryGetValue(folder, out var existing) && existing.ModId != record.ModId)
                {
                    ambiguous.Add(folder);
                    continue;
                }

                index[folder] = record;
            }
        }

        foreach (var folder in ambiguous) index.Remove(folder);

        return index;
    }

    //
    // Everything about the catalog the matching tiers need, derived once per BuildFrom call. The
    // tiers match exactly what they matched before - this only lifts the per-catalog-mod work out
    // of the inner loop, and turns the three exact tiers into dictionary lookups.
    //
    private sealed class CatalogIndex
    {
        // Catalog order decides ties in the tiers that take the first match, so each of these keeps
        // whichever mod was seen first.
        public required Dictionary<int, Mod> ById { get; init; }
        public required Dictionary<string, Mod> ByGuid { get; init; }
        public required Dictionary<string, Mod> ByGuidDerivedFolderName { get; init; }

        // The exact name/slug tier has to know when two different mods answer to the same
        // normalized name, so it can decline rather than pick one.
        public required Dictionary<string, List<Mod>> ByNormalizedNameOrSlug { get; init; }

        // The fuzzy tier still walks the whole catalog, but compares against these rather than
        // re-normalizing and re-tokenizing each candidate for every installed mod.
        public required List<CatalogTokens> Tokens { get; init; }

        public static CatalogIndex Build(IReadOnlyList<Mod> catalog)
        {
            var byId = new Dictionary<int, Mod>();
            var byGuid = new Dictionary<string, Mod>(StringComparer.OrdinalIgnoreCase);
            var byGuidFolder = new Dictionary<string, Mod>(StringComparer.Ordinal);
            var byName = new Dictionary<string, List<Mod>>(StringComparer.Ordinal);
            var tokens = new List<CatalogTokens>(catalog.Count);

            foreach (var mod in catalog)
            {
                byId.TryAdd(mod.Id, mod);

                if (!string.IsNullOrWhiteSpace(mod.Guid)) byGuid.TryAdd(mod.Guid, mod);

                foreach (var candidate in GuidDerivedFolderNames(mod.Guid))
                    byGuidFolder.TryAdd(candidate, mod);

                var normalizedName = NormalizedOrNull(mod.Name);
                var normalizedSlug = NormalizedOrNull(mod.Slug);

                foreach (var normalized in new[] { normalizedName, normalizedSlug })
                {
                    if (normalized is null) continue;
                    if (!byName.TryGetValue(normalized, out var list)) byName[normalized] = list = [];
                    if (list.All(m => m.Id != mod.Id)) list.Add(mod);
                }

                tokens.Add(new CatalogTokens(
                    mod,
                    normalizedName,
                    normalizedSlug,
                    SignificantTokens(mod.Name ?? string.Empty),
                    SignificantTokens(mod.Slug ?? string.Empty)));
            }

            return new CatalogIndex
            {
                ById = byId,
                ByGuid = byGuid,
                ByGuidDerivedFolderName = byGuidFolder,
                ByNormalizedNameOrSlug = byName,
                Tokens = tokens,
            };
        }
    }

    // One catalog mod's precomputed name and slug forms, for the fuzzy matching tier.
    private sealed record CatalogTokens(
        Mod Mod,
        string? NormalizedName,
        string? NormalizedSlug,
        HashSet<string> NameTokens,
        HashSet<string> SlugTokens);

    // The catalog lookup used by BuildFrom, run once per name-group.
    private static Mod? ResolveCatalogMatch(
        string folderName,
        List<InstalledMod> entries,
        CatalogIndex index,
        Dictionary<string, InstalledModRecord> recordsByFolder)
    {
        // This app placed the folder, so the catalog listing is already known exactly. Everything
        // below is inference for mods installed by hand, which is where a folder like "EpicsAIO"
        // can't be talked into matching a listing called "Epic's All in One".
        if (recordsByFolder.TryGetValue(folderName, out var record))
        {
            // Fork: installed from a file as a local item (LocalArchive): its record is what it is
            // known by - never a listing its plugin GUID happens to match (a pack, or one installed
            // offline), which would split it into hand installs and leave the record behind.
            if (LocalArchive.IsLocalId(record.ModId))
                return new Mod { Id = record.ModId, Name = record.Name, Guid = record.Guid };

            var fromRecord = index.ById.GetValueOrDefault(record.ModId)
                ?? (string.IsNullOrWhiteSpace(record.Guid) ? null : index.ByGuid.GetValueOrDefault(record.Guid));
            if (fromRecord is not null) return fromRecord;
        }

        // The real GUID the mod declares - a client DLL's [BepInPlugin], or an SPT 4.x server mod's
        // ModGuid - is tried first as an exact identifier before falling back to the folder-name
        // heuristics below. The client's goes first when a card has both halves, as it always has;
        // a server-only hand install, which never had a GUID to offer before, now matches exactly
        // too. Taken from whichever entry actually carries one rather than the first entry, since
        // a patcher never has one and would otherwise skip the whole tier.
        var installedGuid = entries
            .OrderBy(m => m.Target == InstalledModTarget.Client ? 0 : 1)
            .Select(m => m.Guid)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));

        if (!string.IsNullOrWhiteSpace(installedGuid) && index.ByGuid.TryGetValue(installedGuid, out var byGuid))
            return byGuid;

        var fromName = InferFromFolderName(index, folderName);
        if (fromName is not null) return fromName;

        //
        // A patcher folder is conventionally the mod's name plus a packaging word - "MoreBotsAPI"
        // ships "MoreBotsPrepatch", "WTT - Content Backport" ships "WTT-ContentBackportPatcher" -
        // and that extra word is exactly what stops the tiers above from recognising it: the mod's
        // listing is named after the mod, not after its patcher. Trying again without it is what
        // ties a patcher to its own listing, and, once both halves resolve to the same one, is what
        // lets MergePatcherFolders put them on one card.
        //
        // Only ever a fallback, and only for a folder that is nothing but a patcher: the unmodified
        // name is tried first above, so a patcher that really is listed under a name ending in
        // "Patcher" still matches itself rather than being talked into its stem.
        //
        if (!entries.All(m => m.IsPatcher)) return null;

        var stem = PatcherNameSuffix.Replace(folderName, "");
        return stem.Length > 0 && stem.Length != folderName.Length ? InferFromFolderName(index, stem) : null;
    }

    // The folder-name tiers of ResolveCatalogMatch, in order of how much they can be trusted.
    private static Mod? InferFromFolderName(CatalogIndex index, string folderName)
    {
        // Derived from the folder name once, rather than from every catalog entry in turn.
        var normalizedFolder = Normalize(TrailingTargetSuffix.Replace(folderName, ""));

        if (normalizedFolder.Length > 0
            && index.ByGuidDerivedFolderName.TryGetValue(normalizedFolder, out var byGuidFolder))
        {
            return byGuidFolder;
        }

        // Name/slug inference is the only tier that can plausibly hit more than one listing, and
        // being attributed to the wrong one is worse than not being matched at all: the update it
        // then offers would install a different mod over this one. So both name/slug tiers require
        // exactly one candidate, and an exact name/slug match is tried before the fuzzy one rather
        // than letting a loose match on Name beat an exact match on Slug.
        if (normalizedFolder.Length > 0
            && index.ByNormalizedNameOrSlug.TryGetValue(normalizedFolder, out var exact))
        {
            return exact.Count == 1 ? exact[0] : null;
        }

        return OnlyFuzzyMatch(index, folderName, normalizedFolder);
    }

    //
    // The single catalog mod whose name or slug covers the folder name's significant tokens, or
    // null when none or more than one does - the same rule NameOrSlugMatches applied per candidate,
    // with the folder's own forms built once instead of once per catalog entry.
    //
    private static Mod? OnlyFuzzyMatch(CatalogIndex index, string folderName, string normalizedFolder)
    {
        var folderTokens = TokenSet.For(folderName);

        var separator = FirstSeparator.Match(folderName);
        var afterPrefix = separator.Success ? folderName[(separator.Index + separator.Length)..] : null;
        var afterPrefixTokens = string.IsNullOrEmpty(afterPrefix) ? TokenSet.Empty : TokenSet.For(afterPrefix);

        // Neither side can cover anything and there is no exact form to compare, so the whole
        // catalog walk is skipped rather than run to reach the same answer.
        if (!folderTokens.CanCover && !afterPrefixTokens.CanCover && normalizedFolder.Length == 0) return null;

        Mod? only = null;

        foreach (var candidate in index.Tokens)
        {
            if (!Covers(candidate.NormalizedName, candidate.NameTokens)
                && !Covers(candidate.NormalizedSlug, candidate.SlugTokens))
            {
                continue;
            }

            if (only is not null && only.Id != candidate.Mod.Id) return null;

            only ??= candidate.Mod;
        }

        return only;

        bool Covers(string? normalizedCandidate, HashSet<string> candidateTokens) =>
            (normalizedCandidate is not null && normalizedFolder.Length > 0 && normalizedCandidate == normalizedFolder)
            || folderTokens.IsCoveredBy(candidateTokens)
            || afterPrefixTokens.IsCoveredBy(candidateTokens);
    }

    // Normalize, or null when the source is blank or normalizes to nothing - an empty key would
    // otherwise match every folder name that normalizes to nothing too.
    private static string? NormalizedOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }

    //
    // Folds together the name-groups of a mod that installed more than one folder of the same kind
    // - a zip that drops two plugin folders arrives here as two groups, and both resolve to the one
    // catalog listing the install record names, so both would build a full card for the same mod.
    // The install manifest is what makes this exact rather than a guess: only folders one record
    // placed are joined, never two folders that merely resolved alike.
    //
    // Runs before the patcher and client/server passes, which both look for exactly one partner
    // group per mod and would otherwise be looking at two.
    //
    private static List<(string Key, List<InstalledMod> Entries, Mod? Match)> MergeRecordFolders(
        List<(string Key, List<InstalledMod> Entries, Mod? Match)> groups,
        Dictionary<string, InstalledModRecord> recordsByFolder,
        CatalogIndex index)
    {
        var byRecord = new Dictionary<int, List<int>>();

        for (var i = 0; i < groups.Count; i++)
        {
            if (!recordsByFolder.TryGetValue(groups[i].Key, out var record)) continue;
            if (SpeaksForAnotherMod(groups[i], record, index)) continue;

            if (!byRecord.TryGetValue(record.ModId, out var members)) byRecord[record.ModId] = members = [];
            members.Add(i);
        }

        var joins = byRecord.Values.Where(m => m.Count > 1).ToList();
        if (joins.Count == 0) return groups;

        var absorbed = new Dictionary<int, List<int>>();
        var consumed = new HashSet<int>();

        foreach (var members in joins)
        {
            var record = recordsByFolder[groups[members[0]].Key];
            var ordered = members
                .OrderBy(i => CarriesRecordGuid(groups[i].Entries, record) ? 0 : 1)
                .ThenBy(i => PlacementRank(groups[i].Key, record))
                .ToList();

            absorbed[ordered[0]] = ordered.Skip(1).ToList();
            foreach (var member in ordered.Skip(1)) consumed.Add(member);
        }

        var results = new List<(string Key, List<InstalledMod> Entries, Mod? Match)>(groups.Count - consumed.Count);

        for (var i = 0; i < groups.Count; i++)
        {
            if (consumed.Contains(i)) continue;

            var (key, entries, match) = groups[i];
            results.Add(absorbed.TryGetValue(i, out var extra)
                ? (key, entries.Concat(extra.SelectMany(j => groups[j].Entries)).ToList(), match)
                : (key, entries, match));
        }

        return results;
    }

    //
    // Whether what is in the folder now identifies itself as a different mod from the one whose
    // record names it - a folder this app installed into, emptied by hand, and refilled with
    // something else. Enough to leave the group standing on its own, since a wrongly absorbed
    // folder would take a real mod off the page.
    //
    // A foreign GUID is deliberately NOT that signal for a folder whose files are still in place: a
    // mod that repacks another one ships its plugin, GUID and all, in a second folder of its own,
    // and disowning that folder is what put the mod on the page twice.
    //
    private static bool SpeaksForAnotherMod(
        (string Key, List<InstalledMod> Entries, Mod? Match) group,
        InstalledModRecord record,
        CatalogIndex index)
    {
        if (group.Match is { } match && match.Id != record.ModId) return true;

        // What the record placed there is the better evidence, when there is any: the files this
        // app wrote are either still in the folder or they are not.
        var placed = InstalledModFolders.PlacedFilesUnder(record, group.Key);
        if (placed.Count > 0) return !PlacedFilesStillPresent(group.Entries, placed);

        // A manually-confirmed record places no files, so the GUID is all there is to go on.
        var guid = group.Entries
            .OrderBy(m => m.Target == InstalledModTarget.Client ? 0 : 1)
            .SelectMany(m => m.AllGuids)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));

        return guid is not null
            && index.ByGuid.TryGetValue(guid, out var byGuid)
            && byGuid.Id != record.ModId;
    }

    // Whether any file the record placed in this folder is still there. One hit settles it, so this
    // is a handful of File.Exists calls per app-installed folder, on the scan's background thread.
    private static bool PlacedFilesStillPresent(List<InstalledMod> entries, IReadOnlyList<string> placed)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FolderPath)) continue;

            // A loose DLL's "folder" is the file itself.
            if (File.Exists(entry.FolderPath)) return true;

            foreach (var relative in placed)
            {
                var path = Path.Combine(entry.FolderPath, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) return true;
            }
        }

        return false;
    }

    // The folder carrying the plugin the record was installed for, which is the one whose name and
    // version the merged card leads with.
    private static bool CarriesRecordGuid(List<InstalledMod> entries, InstalledModRecord record) =>
        !string.IsNullOrWhiteSpace(record.Guid)
        && entries.Any(m => m.AllGuids.Any(g => string.Equals(g, record.Guid, StringComparison.OrdinalIgnoreCase)));

    // Where a folder sits in the order the install placed it, so the first folder written leads when
    // no GUID settles it. Anything the record does not name sorts last.
    private static int PlacementRank(string folderName, InstalledModRecord record)
    {
        var folders = InstalledModFolders.Resolve(record);

        for (var i = 0; i < folders.Count; i++)
        {
            if (string.Equals(folders[i], folderName, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return int.MaxValue;
    }

    //
    // Folds a patcher-only name-group into the mod it belongs to. A mod that ships a BepInEx
    // preloader patcher puts it in its own folder under BepInEx\patchers, usually named differently
    // from its plugin folder ("SomeMod" and "SomeMod.Preloader"), so the two arrive here as separate
    // name-groups - and the patcher half, having no [BepInPlugin] GUID to identify it, would show as
    // its own card with nothing filled in but a folder name. Two groups that resolved to the same
    // catalog listing are the same mod by definition, so the patcher joins it and the mod is one
    // card again, with the patcher's folder among its Entries where the disable/remove commands can
    // see it.
    //
    // A patcher that neither tier below can place is left standing on its own rather than attached
    // to a guess - it still reads as a patcher rather than an unidentified mod, since TargetSummary
    // says so.
    //
    private static List<(List<InstalledMod> Entries, Mod? Match)> MergePatcherFolders(
        List<(List<InstalledMod> Entries, Mod? Match)> groups)
    {
        // Keyed by the index of the group being joined, so a mod with more than one patcher folder
        // collects all of them.
        var absorbed = new Dictionary<int, List<InstalledMod>>();
        var consumed = new HashSet<int>();

        for (var i = 0; i < groups.Count; i++)
        {
            if (!groups[i].Entries.All(m => m.IsPatcher)) continue;

            var host = FindPatcherHost(groups, i);
            if (host is not { } index) continue;

            if (!absorbed.TryGetValue(index, out var into)) absorbed[index] = into = [];
            into.AddRange(groups[i].Entries);
            consumed.Add(i);
        }

        if (consumed.Count == 0) return groups;

        var results = new List<(List<InstalledMod> Entries, Mod? Match)>(groups.Count - consumed.Count);

        for (var i = 0; i < groups.Count; i++)
        {
            if (consumed.Contains(i)) continue;

            var (entries, match) = groups[i];
            results.Add(absorbed.TryGetValue(i, out var extra)
                ? (entries.Concat(extra).ToList(), match)
                : (entries, match));
        }

        return results;
    }

    //
    // The group a patcher-only group belongs to, or null when it can't be placed with confidence.
    //
    // Two tiers. The catalog listing comes first and is the exact one: two groups that resolved to
    // the same listing are the same mod, whether that came from the install manifest or from a
    // plugin's own GUID. Where a mod has both a plugin and a server half, the patcher goes with the
    // plugin - it's a BepInEx file, and it's the plugin's version and name the card leads with.
    //
    // The second tier is the naming convention, for a mod installed by hand whose patcher folder
    // resolves to nothing on its own (it has no GUID to match on, and "SomeMod.Preloader" isn't
    // going to match a listing called "Some Mod"). Stripping the patcher word off the end has to
    // leave exactly one other mod's folder name, matched in full - a substring or fuzzy rule here
    // would start attaching patchers to whatever mod happened to share a prefix.
    //
    private static int? FindPatcherHost(List<(List<InstalledMod> Entries, Mod? Match)> groups, int patcherIndex)
    {
        var candidates = Enumerable.Range(0, groups.Count)
            .Where(j => j != patcherIndex && groups[j].Entries.Any(m => !m.IsPatcher))
            .ToList();

        if (groups[patcherIndex].Match is { } match)
        {
            var byMatch = candidates.Where(j => groups[j].Match?.Id == match.Id).ToList();
            var withPlugin = byMatch
                .Where(j => groups[j].Entries.Any(m => m is { Target: InstalledModTarget.Client, IsPatcher: false }))
                .ToList();

            var preferred = withPlugin.Count > 0 ? withPlugin : byMatch;
            if (preferred.Count == 1) return preferred[0];
        }

        var patcherName = groups[patcherIndex].Entries[0].Name;
        var stem = PatcherNameSuffix.Replace(patcherName, "");
        if (stem.Length == 0 || stem.Length == patcherName.Length) return null;

        var byName = candidates
            .Where(j => string.Equals(
                groups[j].Entries.First(m => !m.IsPatcher).Name,
                stem,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        return byName.Count == 1 ? byName[0] : null;
    }

    //
    // The word a patcher folder is conventionally named after its mod plus - "SomeMod.Preloader",
    // "SomeMod-Patcher", "SomeModPrepatch". Only stripped from the end, and only used to look for an
    // exact match on what's left.
    //
    private static readonly Regex PatcherNameSuffix = new(
        @"[\s._-]*(preloader|prepatcher|prepatch|patchers|patcher|patch)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Joins a client-only name-group to a server-only name-group when they're the only two that
    // independently resolved to the same catalog Mod. Groups that already have both targets, matched
    // nothing, or have an ambiguous match pass through unmerged.
    private static List<(List<InstalledMod> Entries, Mod? Match)> MergeSplitClientServerHalves(
        List<(List<InstalledMod> Entries, Mod? Match)> groups)
    {
        var results = new List<(List<InstalledMod> Entries, Mod? Match)>();
        var consumed = new HashSet<int>();

        for (var i = 0; i < groups.Count; i++)
        {
            if (consumed.Contains(i)) continue;

            var (entries, match) = groups[i];
            var hasClient = entries.Any(m => m.Target == InstalledModTarget.Client);
            var hasServer = entries.Any(m => m.Target == InstalledModTarget.Server);

            if (match is not null && hasClient != hasServer)
            {
                var partners = Enumerable.Range(i + 1, groups.Count - i - 1)
                    .Where(j => !consumed.Contains(j) && groups[j].Match?.Id == match.Id)
                    .Where(j =>
                    {
                        var otherHasClient = groups[j].Entries.Any(m => m.Target == InstalledModTarget.Client);
                        var otherHasServer = groups[j].Entries.Any(m => m.Target == InstalledModTarget.Server);
                        return otherHasClient != otherHasServer && otherHasClient != hasClient;
                    })
                    .ToList();

                if (partners.Count == 1)
                {
                    consumed.Add(partners[0]);
                    results.Add((entries.Concat(groups[partners[0]].Entries).ToList(), match));
                    continue;
                }
            }

            results.Add((entries, match));
        }

        return results;
    }

    private static InstalledModCardViewModel BuildCard(
        List<InstalledMod> entries,
        Mod? match,
        string? installedSptVersion,
        InstalledModRecord? record,
        IReadOnlyDictionary<int, int> addonsByParent)
    {
        // The plugin half speaks for the client side wherever there's a choice - it's the one with
        // the mod's real name, version and GUID on it, where a patcher is a support file whose
        // assembly version often has nothing to do with the mod's own.
        var plugin = entries.FirstOrDefault(m => m is { Target: InstalledModTarget.Client, IsPatcher: false });
        var patcher = entries.FirstOrDefault(m => m is { Target: InstalledModTarget.Client, IsPatcher: true });
        var client = plugin ?? patcher;
        var server = entries.FirstOrDefault(m => m.Target == InstalledModTarget.Server);

        // Not done for addon cards: an addon's record names its PARENT's folders, so comparing them
        // against the addon's own scan entries would report every one of them as missing.
        IReadOnlyList<string> missingFolders = record is null
            ? []
            : InstalledModFolders.MissingFrom(record, entries.SelectMany(FolderNamesOf));

        // The version recorded at install time beats anything read off disk: plenty of authors never
        // bump the assembly version, so a mod installed as 1.2.1 can still report 1.0.0.0 from its
        // DLL - which then reads as an update being available forever.
        var fileVersion = client?.Version ?? server?.Version;
        var installedVersion = record?.Version ?? fileVersion;

        (string Key, object?[] Values)? detail = null;
        // Compared as versions, not text: a plugin declaring "1.1.5" beside a server half that only
        // has its file version "1.1.5.0" is the same release, not a disagreement.
        if (client is not null && server is not null
            && !VersionsAreEquivalent(client.Version, server.Version))
        {
            detail = (
                nameof(Strings.Installed_DetailFilesReportPairFormat),
                [client.Version, server.Version]);
        }
        else if (record is not null && fileVersion is not null
                 && ModVersionComparer.IsUpdateAvailable(record.Version, fileVersion) is null or false
                 && !VersionsAreEquivalent(record.Version, fileVersion))
        {
            detail = (nameof(Strings.Installed_DetailFilesReportFormat), [fileVersion]);
        }

        var manuallyConfirmed = record is not null && !record.IsAppManaged;

        // Fork: when this app installed it, the record says exactly when (the last install or update);
        // the folder's creation time is only a stand-in, and a file copied with its archive dates can
        // read as years old.
        var installedAt = record?.InstalledAt ?? new[] { client?.InstalledAt, server?.InstalledAt }
            .Where(d => d is not null)
            .OrderBy(d => d)
            .FirstOrDefault();

        //
        // What an update would install: the newest release that runs on this SPT - the same pick
        // Browse's card, its sort and Update selected all use. Comparing against the newest release
        // overall hid real updates: Task Search 1.0.1 on 4.0.13 has 1.9.1 for 4.0 and 2.0.2 for 4.1
        // only, and with the newest being 4.1-only the card said nothing at all.
        //
        var updateTarget = match is null ? null : ModCardViewModel.PickDisplayVersion(match, installedSptVersion);

        // The card's "Latest published" is the same pick, so it names a release this SPT can run whenever one exists.
        var latestPublished = updateTarget?.Version;

        // No installed version could be determined at all (no record, and nothing readable off the
        // files themselves) - plenty of mods never expose a usable version this way. Rather than
        // reading as an update forever, assume the latest published version is what's on disk.
        var versionUndetermined = installedVersion is null && match is not null;

        bool? isNewer;
        if (versionUndetermined)
        {
            isNewer = false;

            // Only when nothing else has been said about the version - a manually confirmed one
            // has already been explained, and these two would contradict it.
            if (detail is null && !manuallyConfirmed)
            {
                detail = latestPublished is not null
                    ? (nameof(Strings.Installed_DetailUndeterminedLatestFormat), [latestPublished])
                    : (nameof(Strings.Installed_DetailUndetermined), []);
            }
        }
        else
        {
            // A newer version alone isn't enough - it also has to target the installed SPT version.
            isNewer = ModVersionComparer.IsUpdateAvailable(installedVersion, updateTarget?.Version);

            // Fork: read from a DLL, which cannot carry a "-hotfix": the same numbers are the same
            // release as far as anything here can tell - not an update for ever.
            if (isNewer == true && record is null && ModVersionComparer.IsSameRelease(installedVersion, updateTarget?.Version))
                isNewer = false;
        }

        var updateAvailable = isNewer == true
            ? SptVersionMatcher.IsSatisfiedBy(updateTarget?.SptVersionConstraint, installedSptVersion)
            : isNewer;

        return new InstalledModCardViewModel
        {
            // Client's folder/package name wins when client and server disagree.
            Name = client?.Name ?? server!.Name,
            InstalledVersion = installedVersion,
            VersionDetail = detail,
            IsVersionManuallyConfirmed = manuallyConfirmed,
            InstalledAt = installedAt,
            HasClient = client is not null,
            HasPlugin = plugin is not null,
            HasPatcher = patcher is not null,
            HasServer = server is not null,
            LatestPublishedVersion = latestPublished,
            UpdateVersion = updateAvailable == true ? updateTarget?.Version : null,
            LatestUpdatedAt = match?.UpdatedAt,
            MatchedModName = match?.Name,
            DeclaredName = match is null ? server?.DeclaredName ?? plugin?.DeclaredName : null,
            Guid = match?.Guid ?? client?.Guid ?? server?.Guid,
            Author = match?.Owner?.Name ?? (match is null ? server?.Author : null),
            CategoryTag = match?.Category?.Title,
            AddonCount = match is null ? 0 : addonsByParent.GetValueOrDefault(match.Id),
            IsFikaCompatible = match?.FikaCompatibility == true,
            ContainsAds = match?.ContainsAds == true,
            ContainsAiContent = match?.ContainsAiContent == true,
            UpdateAvailable = updateAvailable,
            ClientFolderPath = client?.FolderPath,
            ServerFolderPath = server?.FolderPath,
            ClientFolderName = client?.Name,
            ServerFolderName = server?.Name,
            FolderPath = client?.FolderPath ?? server!.FolderPath,
            ModId = match?.Id,
            IsAppManaged = record?.IsAppManaged ?? false,
            IsManualOverride = record is not null && !record.IsAppManaged,
            MissingFolders = missingFolders,
            WasPartlyInstalled = record?.Incomplete ?? false,
            Entries = entries,
        };
    }

    //
    // Every name a scan entry could be known by on disk, for comparing against what an install
    // record says it placed.
    //
    // Name alone is not enough: a server mod takes its name from its package.json when it has one,
    // which is often not what the folder is called, while a record always names the folder. The
    // path gives both spellings - the folder itself, and a loose DLL's file name with the extension
    // dropped, which is how the scanner and the record both name that case.
    //
    private static bool HoldsRecordFolder(List<InstalledMod> entries, InstalledModRecord record)
    {
        var folders = InstalledModFolders.Resolve(record).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entries.SelectMany(FolderNamesOf).Any(folders.Contains);
    }

    private static IEnumerable<string> FolderNamesOf(InstalledMod entry)
    {
        yield return entry.Name;

        if (string.IsNullOrWhiteSpace(entry.FolderPath)) yield break;

        var path = entry.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        yield return Path.GetFileName(path);
        yield return Path.GetFileNameWithoutExtension(path);
    }

    // True when two loosely-formatted version strings mean the same release, so a trailing ".0"
    // difference - or (fork) a "-beta" a DLL cannot carry - isn't reported as a discrepancy.
    private static bool VersionsAreEquivalent(string? a, string? b) =>
        (ModVersionComparer.IsUpdateAvailable(a, b) == false && ModVersionComparer.IsUpdateAvailable(b, a) == false)
        || ModVersionComparer.IsSameRelease(b, a)
        || ModVersionComparer.IsSameRelease(a, b);

    // The folder names a catalog mod's GUID would plausibly have been installed under, normalized
    // ready to compare. Two folder-naming conventions:
    //
    // 1. Drop the leading segment, dash-join the rest - e.g. GUID "com.acidphantasm.bosseshavegpcoins"
    //    -> folder "acidphantasm-bosseshavegpcoins".
    // 2. Reverse the non-modname segments into domain order and concatenate, then dash-join with the
    //    modname - e.g. GUID "wtf.archangel.lotsoflootredux" -> folder "archangelwtf-lotsoflootredux".
    //
    // Tried before the Name/Slug fallback.
    private static IEnumerable<string> GuidDerivedFolderNames(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) yield break;

        var parts = guid.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) yield break;

        if (NormalizedOrNull(string.Join('-', parts.Skip(1))) is { } dropFirst) yield return dropFirst;

        var modName = parts[^1];
        //
        // Enumerable.Reverse spelled out rather than parts[..^1].Reverse().
        //
        // The slice is a string[], which converts implicitly to Span<string>, and C# 14 picks
        // MemoryExtensions.Reverse<T>(this Span<T>) - which reverses in place and returns VOID -
        // over the LINQ one. This project is net9.0-windows with LangVersion=latest, so it compiled
        // for as long as the newest SDK on the machine was 9. The moment a .NET 10 SDK is installed
        // (which building the ServerMap server mod requires - it targets net10.0) this line stops
        // compiling, with an error that names neither Reverse nor Span.
        //
        var reversedDomain = string.Concat(Enumerable.Reverse(parts[..^1]));
        if (NormalizedOrNull($"{reversedDomain}-{modName}") is { } reversed) yield return reversed;
    }

    // Matches a trailing "-client"/"-server"/"_client"/"_server" (or no separator) on a folder name,
    // stripped before matching since it isn't part of the mod's actual identity.
    private static readonly Regex TrailingTargetSuffix =
        new(@"[-_]?(client|server)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string Normalize(string s) =>
        new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // Splits camelCase/PascalCase words apart before the non-alphanumeric split in SignificantTokens
    // below, so a folder name with no separator between words still tokenizes correctly. Two rules:
    // 1. lower/digit -> upper ("ServerMod" -> "Server Mod").
    // 2. upper -> upper+lower ("CSGasServer" -> "CS Gas Server").
    private static readonly Regex CamelCaseBoundary =
        new(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);
    private static readonly Regex NonAlphanumericRun = new(@"[^a-zA-Z0-9]+", RegexOptions.Compiled);

    // Words that describe how a mod is packaged, not what it actually is - stripped from both sides
    // of OnlyFuzzyMatch's token comparison.
    private static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "mod", "mods", "patch", "addon", "plugin", "client", "server", "backend", "frontend",
    };

    // Matches the first "-", "_", or "." in a folder name - used by OnlyFuzzyMatch to retry with a
    // probable author-name prefix dropped off (the "Author-ModName" convention).
    private static readonly Regex FirstSeparator = new(@"[-_.]", RegexOptions.Compiled);

    private static HashSet<string> SignificantTokens(string s)
    {
        var spaced = CamelCaseBoundary.Replace(s, " ");
        return NonAlphanumericRun.Split(spaced)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 2 && !GenericTokens.Contains(t))
            .ToHashSet();
    }

    //
    // A folder name's significant tokens, with the "is this worth comparing at all" test done once
    // up front rather than repeated for every catalog entry it is compared against. The length-4
    // floor guards against a near-empty token set matching everything by vacuous truth.
    //
    private readonly struct TokenSet
    {
        public static TokenSet Empty { get; } = new([]);

        private readonly HashSet<string> _tokens;

        private TokenSet(HashSet<string> tokens)
        {
            _tokens = tokens;
            CanCover = tokens.Count > 0 && tokens.Sum(t => t.Length) >= 4;
        }

        public bool CanCover { get; }

        public static TokenSet For(string source) => new(SignificantTokens(source));

        //
        // True when every one of these tokens appears somewhere in candidateTokens. A token also
        // counts as covered if it is a short abbreviation of some candidate token (see
        // IsAbbreviationOf). Plain loops rather than LINQ: this runs once per catalog entry per
        // unmatched mod, which is where the matching pass spends nearly all of its time.
        //
        public bool IsCoveredBy(HashSet<string> candidateTokens)
        {
            if (!CanCover) return false;

            foreach (var token in _tokens)
            {
                if (candidateTokens.Contains(token)) continue;

                var abbreviated = false;
                foreach (var candidate in candidateTokens)
                {
                    if (!IsAbbreviationOf(token, candidate)) continue;

                    abbreviated = true;
                    break;
                }

                if (!abbreviated) return false;
            }

            return true;
        }
    }

    // True when folderToken reads as a plausible truncation of candidateToken: a prefix match with
    // folderToken between 3 and 6 letters, and candidateToken only a few letters longer.
    private static bool IsAbbreviationOf(string folderToken, string candidateToken) =>
        folderToken.Length is >= 3 and <= 6
        && candidateToken.Length - folderToken.Length is > 0 and <= 5
        && candidateToken.StartsWith(folderToken, StringComparison.Ordinal);
}

// A download from the ledger and what the last scan made of it.
public sealed record PendingDownload(DownloadedModRecord Download, DownloadMatch Match);
