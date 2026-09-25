using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// Display wrapper around a Mod for the Browse results grid. Precomputes the fields of the version this card represents.
public sealed partial class ModCardViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    public required Mod Mod { get; init; }

    // The installed match's pin keys (ModListPlanner.PinKeys), empty when it isn't installed, so
    // IsPinned can be re-read without matching the card again.
    public IReadOnlyList<string> PinKeys { get; private init; } = [];

    // Pinned against a mod list's disable sweep on this install. Settable so returning to Browse
    // can pick up a pin made on Installed or Mod lists without rebuilding the page.
    [ObservableProperty]
    private bool _isPinned;

    public void RefreshPin(IReadOnlySet<string> pins) =>
        IsPinned = PinKeys.Count > 0 && PinKeys.Any(pins.Contains);

    public string? Name => Mod.Name;
    public string? Guid => Mod.Guid;
    public string? Teaser => Mod.Teaser;
    public string? Thumbnail => Mod.Thumbnail;
    public int? Downloads => Mod.Downloads;

    // Null rather than 0 when the mod has no endorsements, so the card's endorsement line can be
    // collapsed with the same NullToCollapsed converter the author line uses. Almost every mod is
    // on zero while the feature is this new, and a "0 endorsements" row on every card would add
    // height for no information.
    public int? Endorsements => Mod.EndorsementsCount is > 0 ? Mod.EndorsementsCount : null;

    // The mod's primary owner/author.
    public string? Author => Mod.Owner?.Name;

    // The mod's category title, shown as a tag chip.
    public string? CategoryTag => Mod.Category?.Title;

    public bool IsFikaCompatible => Mod.FikaCompatibility == true;
    public bool ContainsAds => Mod.ContainsAds == true;

    // The raw SPT version constraint of the version this card represents, e.g. "^3.9.0". Shown in the tooltip.
    public string? SptVersionConstraint { get; private init; }

    // The card's SPT line, e.g. "✓ SPT 4.0.13 - 4.0.x". Falls back to the raw constraint when it can't be parsed.
    public string SptVersionDisplay { get; private init; } = Strings.Browse_SptVersionUnknown;

    // Tooltip spelling out the requirement, the installed version, and the raw constraint.
    public string SptVersionTooltip { get; private init; } = "";

    // The release number of the version this card represents, e.g. "1.4.2".
    public string? DisplayReleaseVersion { get; private init; }

    // True if the version this card represents runs on the installed SPT, false if not, null if unknown.
    public bool? IsCompatibleWithInstalledSpt { get; private init; }

    // True when the card is showing an older release because the newest one doesn't run on the installed SPT.
    public bool IsOlderCompatibleVersion { get; private init; }

    //
    // Set when the ticked filter line is hiding a version that WOULD run on the installed SPT -
    // e.g. filtering to 4.1 while running SPT 4.0 makes a mod's 4.1 release show red, which is
    // correct, but gives no hint that the same mod has an older 4.0 release that's actually usable.
    // Null when there's nothing to point at (no filter active, or no other version helps either).
    //
    public string? AlternateCompatibleVersionNote { get; private init; }

    // True when this catalog mod matched an installed mod. Drives whether the card shows an install/update status dot.
    public bool IsInstalled { get; private init; }

    // True when the installed match is sitting in a ".disabled" container, so it's on disk but not
    // loaded. Outranks the update state - what's newer hardly matters while nothing loads it.
    public bool IsDisabled { get; private init; }

    // The card's install status, using the same vocabulary and icons as the Installed and
    // Dependencies pages.
    public ModStatus Status => (IsInstalled, IsDisabled, UpdateAvailable) switch
    {
        (false, _, _) => ModStatus.NotInstalled,
        (true, true, _) => ModStatus.Disabled,
        (true, false, true) => ModStatus.UpdateAvailable,
        (true, false, false) => ModStatus.Installed,
        _ => ModStatus.Unknown,
    };

    public string StatusGlyph => ModStatusDisplay.Glyph(Status);

    public string StatusTooltip => ModStatusWording.Tooltip(Status);

    // Only meaningful when IsInstalled is true. True if a compatible newer version is published, false if up to date, null if unknown.
    public bool? UpdateAvailable { get; private init; }

    //
    // How many addons this mod has published. Unlike the dependency badge next to it, this needs no
    // per-card lookup and no debounce: the whole addon catalog is under a hundred entries and is
    // already in memory, so the answer is a dictionary hit.
    //
    public int AddonCount { get; private init; }

    public bool HasAddons => AddonCount > 0;

    public string AddonBadgeText => Strings.Browse_AddonBadge(AddonCount);

    //
    // The Steam card's rating row. sp-mod.com has no star ratings, so the row carries what it does
    // publish - endorsements, favourites and downloads - rather than inventing a score. Each is null
    // at zero so its part of the row collapses, the same way Endorsements above does.
    //
    public int? Favourites => Mod.FavouritesCount is > 0 ? Mod.FavouritesCount : null;

    public bool ContainsAiContent => Mod.ContainsAiContent == true;

    // "Posted" and "Updated" in the hover popup, as Steam's has. Posted falls back to the record's
    // creation date for a mod that was never given a publish date; Updated is The Forge's own
    // record date, the same one Last Updated sorts by when the newest release runs here.
    public DateTimeOffset? PostedAt => Mod.PublishedAt ?? Mod.CreatedAt;

    public DateTimeOffset? UpdatedAt => Mod.UpdatedAt;

    // Steam's popup shows the description's first line; the teaser is this catalog's one-liner.
    public bool HasTeaser => !string.IsNullOrWhiteSpace(Mod.Teaser);

    // 
    // Builds a card. <paramref name="selectedLines"/> is the SPT release lines currently ticked in
    // Browse's filter; the card's SPT text describes what the mod supports on exactly those lines,
    // so filtering by 4.0 and 4.1 shows both. With none ticked it falls back to describing the
    // version that would be installed.
    // 
    public static ModCardViewModel From(
        Mod mod,
        string? installedSptVersion,
        InstalledModCardViewModel? installedMatch = null,
        IReadOnlyList<(int Major, int Minor)>? selectedLines = null,
        IReadOnlyList<SptRelease>? releases = null,
        int addonCount = 0,
        IReadOnlyList<string>? pinKeys = null)
    {
        var newest = LatestVersion(mod);
        var shown = PickDisplayVersion(mod, installedSptVersion) ?? newest;
        var constraints = (mod.Versions ?? []).Select(v => v.SptVersionConstraint).ToList();

        // The glyph answers "does what's shown here actually run on my SPT". When a filter is
        // ticked, that has to look only at the constraints relevant to the ticked lines - not the
        // mod's whole history - or filtering to 4.0 while running SPT 4.1.1 could show a green
        // check next to "SPT 4.0.13" just because the mod separately publishes an unrelated
        // 4.1-compatible version elsewhere. Chris: "if you're running 4.1 and you have 4.0 filter
        // you should be seeing red because this are incompatible... this applies to them all".
        // With no filter ticked the text describes every line the mod supports, so the glyph stays
        // mod-wide too ("can I install ANY version of this on my SPT").
        var relevantConstraints = selectedLines is { Count: > 0 }
            ? constraints.Where(c => selectedLines.Any(line => SptVersionRange.IntersectsReleaseLine(c, line.Major, line.Minor))).ToList()
            : constraints;

        var compatible = relevantConstraints.Count == 0
            ? null
            : relevantConstraints.Any(c => SptVersionMatcher.IsSatisfiedBy(c, installedSptVersion) == true)
                ? true
                : relevantConstraints.Any(c => SptVersionMatcher.IsSatisfiedBy(c, installedSptVersion) == false)
                    ? false
                    : (bool?)null;

        var isOlder = shown is not null && newest is not null && shown.Id != newest.Id;

        // "shown" is already PickDisplayVersion's pick - the version that genuinely runs on the
        // installed SPT, independent of whatever line is ticked. When the ticked filter's glyph is
        // red/unknown but "shown" itself IS compatible with the real installed SPT, the mod isn't
        // actually incompatible for this user - the filter is just describing a line they aren't
        // running, and "shown"/DisplayReleaseVersion is already quietly pointing at the version
        // that would really get installed. Say so, rather than leaving a red card with no
        // explanation. Chris: "maybe it should be red but with a note saying 'older version
        // available for your installed SPT version'".
        var alternateNote = compatible != true && shown is not null
            && SptVersionMatcher.IsSatisfiedBy(shown.SptVersionConstraint, installedSptVersion) == true
            ? BuildAlternateCompatibleVersionNote(shown, newest)
            : null;

        return new ModCardViewModel
        {
            Mod = mod,
            SptVersionConstraint = shown?.SptVersionConstraint,
            SptVersionDisplay = BuildDisplay(constraints, shown?.SptVersionConstraint, selectedLines, releases, compatible),
            SptVersionTooltip = BuildTooltip(shown, newest, installedSptVersion, isOlder, constraints, releases, alternateNote),
            DisplayReleaseVersion = shown?.Version,
            IsCompatibleWithInstalledSpt = compatible,
            IsOlderCompatibleVersion = isOlder,
            AlternateCompatibleVersionNote = alternateNote,
            IsInstalled = installedMatch is not null,
            IsDisabled = installedMatch?.IsDisabled == true,
            UpdateAvailable = installedMatch?.UpdateAvailable,
            AddonCount = addonCount,
            PinKeys = pinKeys ?? [],
        };
    }

    // Phrases the note pointing at "shown" - "older" when it isn't the mod's newest release
    // (the common case: filtering to a newer line than what's installed surfaces an older, still-
    // compatible release), otherwise a neutral "compatible version" phrasing.
    private static string BuildAlternateCompatibleVersionNote(ModVersionSummary shown, ModVersionSummary? newest)
    {
        var isOlder = newest is not null && shown.Id != newest.Id
            && shown.PublishedAt is { } shownDate && newest.PublishedAt is { } newestDate && shownDate < newestDate;

        // "version (vX)" rather than "vX" was eleven characters that said nothing the number
        // doesn't - and at three or four Browse columns it was what pushed this note past the card
        // and into an ellipsis. The card already writes its release as "v1.6.0" a line above.
        return Text(
            isOlder
                ? Strings.Browse_OlderVersionNoteFormat
                : Strings.Browse_CompatibleVersionNoteFormat,
            shown.Version);
    }

    // 
    // The newest cached version that runs on <paramref name="installedSptVersion"/>, or the newest
    // version overall when none of them do. Shared with Browse's Install command so the card always
    // describes the version that installing would actually fetch.
    // 
    // Only sees the versions the catalog carries (the API embeds the latest few), so a
    // compatible release older than that window won't be found.
    public static ModVersionSummary? PickDisplayVersion(Mod mod, string? installedSptVersion)
    {
        var candidates = (mod.Versions ?? [])
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();

        return candidates.FirstOrDefault(v => SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, installedSptVersion) == true)
            ?? candidates.FirstOrDefault();
    }

    // Returns the mod's most recently published version.
    public static ModVersionSummary? LatestVersion(Mod mod) => mod.Versions?
        .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
        .FirstOrDefault();

    private static string BuildDisplay(
        List<string?> constraints,
        string? pickedConstraint,
        IReadOnlyList<(int Major, int Minor)>? selectedLines,
        IReadOnlyList<SptRelease>? releases,
        bool? compatible)
    {
        // The tick and the cross live inside their own whole key rather than being pasted onto a
        // shared one, so a language can drop them or use something else.
        var format = compatible switch
        {
            true => Strings.Browse_SptCompatibleFormat,
            false => Strings.Browse_SptIncompatibleFormat,
            _ => Strings.Browse_SptPlainFormat,
        };

        // Name the newest release that actually shipped on each ticked line, rather than the
        // boundary the constraint is written against - "~4.0.4" is 4.0.13 in practice.
        if (releases is { Count: > 0 })
        {
            var lines = selectedLines is { Count: > 0 }
                ? selectedLines
                : SptReleases.Lines(SptReleases.Supported(constraints, releases));

            var named = lines
                .Select(line => SptReleases.NewestSupportedOnLine(constraints, releases, line.Major, line.Minor))
                .Where(release => release is not null)
                .Select(release => release!.Value.Label)
                .ToList();

            if (named.Count > 0)
                return Text(format, string.Join(Strings.Common_ListSeparator, named));
        }

        // No release list yet (first run, offline) - fall back to describing the constraint itself.
        if (selectedLines is { Count: > 0 })
        {
            var perLine = selectedLines
                .Select(line => SptVersionRange.UnionForLine(constraints, line.Major, line.Minor))
                .Where(bounds => bounds is not null)
                .Select(bounds => SptVersionRangeFormatter.Format(bounds!.Value))
                .Distinct()
                .ToList();

            if (perLine.Count > 0)
                return Text(format, string.Join(Strings.Common_ListSeparator, perLine));
        }

        if (string.IsNullOrWhiteSpace(pickedConstraint)) return Strings.Browse_SptVersionUnknown;

        return Text(format, SptVersionRangeFormatter.Format(pickedConstraint) ?? pickedConstraint);
    }

    private static string BuildTooltip(
        ModVersionSummary? shown,
        ModVersionSummary? newest,
        string? installedSptVersion,
        bool isOlder,
        List<string?> constraints,
        IReadOnlyList<SptRelease>? releases,
        string? alternateNote)
    {
        if (shown is null) return Strings.Browse_TooltipNoVersion;

        var lines = new List<string>();

        var supported = releases is { Count: > 0 } ? SptReleases.Supported([shown.SptVersionConstraint], releases) : [];
        if (supported.Count > 0)
        {
            lines.Add(supported.Count == 1
                ? Text(Strings.Browse_TooltipRunsOnSingleFormat, shown.Version, supported[0].Label)
                : Text(
                    Strings.Browse_TooltipRunsOnRangeFormat,
                    shown.Version,
                    supported[^1].Label,
                    supported[0].Label));
        }
        else
        {
            var range = SptVersionRangeFormatter.Format(shown.SptVersionConstraint);
            lines.Add(range is null
                ? Text(Strings.Browse_TooltipNoRequirementFormat, shown.Version)
                : Text(Strings.Browse_TooltipNeedsFormat, shown.Version, range));
        }

        lines.Add(string.IsNullOrWhiteSpace(installedSptVersion)
            ? Strings.Browse_TooltipNoInstall
            : Text(Strings.Browse_TooltipYouHaveFormat, installedSptVersion));

        if (isOlder && newest is not null)
        {
            var newestRange = SptVersionRangeFormatter.Format(newest.SptVersionConstraint) ?? newest.SptVersionConstraint;
            lines.Add(Text(
                Strings.Browse_TooltipShowingOlderFormat, newest.Version, newestRange));
        }

        if (!string.IsNullOrWhiteSpace(shown.SptVersionConstraint))
            lines.Add(Text(Strings.Browse_TooltipConstraintFormat, shown.SptVersionConstraint));

        if (!string.IsNullOrWhiteSpace(alternateNote))
            lines.Add(alternateNote);

        return string.Join(Environment.NewLine, lines);
    }
}
