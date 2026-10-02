using System.IO;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// One copy in a conflict, as the Dependencies and Conflicts page lists it. Card and Entry are what
// Keep this one acts on: the mod the copy belongs to, and the folder that clashes.
//
public sealed class ConflictMemberRow
{
    public required string ModName { get; init; }

    public required string Location { get; init; }

    public string? Detail { get; init; }

    public required string FullPath { get; init; }

    public required InstalledModCardViewModel Card { get; init; }

    public required InstalledMod Entry { get; init; }

    // Only for the same mod installed twice (C1, C2). Different copies of a file are information
    // only (D7): neither mod is the wrong one.
    public bool IsKeepable { get; init; }

    //
    // What Keep this one would remove, set by PlanKeeps: each other mod in the conflict, with every
    // folder of it that duplicates this copy's mod - client and server halves at once.
    //
    public IReadOnlyList<(InstalledModCardViewModel Card, IReadOnlyList<InstalledMod> Entries)> Removals { get; internal set; } = [];

    // Set by PlanKeeps when keeping this copy would also take files that aren't duplicated.
    public string? KeepBlockedReason { get; internal set; }

    public bool CanKeep => IsKeepable && KeepBlockedReason is null;

    public string? KeepToolTip => !IsKeepable ? null : KeepBlockedReason ?? Strings.Conflicts_KeepThisToolTip;

    public ConflictItemViewModel Owner { get; internal set; } = null!;
}

//
// One conflict on the Dependencies and Conflicts page (OPEN-11): what kind, why it matters, and
// every mod involved with the folder it sits in. Built once from a ModConflict; nothing here changes
// after that, so it is a plain object rather than an observable one.
//
public sealed class ConflictItemViewModel
{
    public required string Title { get; init; }

    public required string Explanation { get; init; }

    public required IReadOnlyList<ConflictMemberRow> Members { get; init; }

    public static ConflictItemViewModel From(
        ModConflict conflict, IReadOnlyList<InstalledModCardViewModel> cards, string installPath)
    {
        var canKeep = conflict.Kind is ModConflictKind.DuplicatePlugin or ModConflictKind.DuplicateServerMod;

        var item = new ConflictItemViewModel
        {
            Title = ModConflicts.Title(conflict.Kind),
            Explanation = ModConflicts.Explanation(conflict),
            Members =
            [
                .. conflict.Members.Select(m => new ConflictMemberRow
                {
                    ModName = cards[m.ModIndex].DisplayTitle,
                    Location = Relative(installPath, m.Entry.FolderPath),
                    Detail = m.Assembly is { } copy ? Describe(copy) : null,
                    FullPath = m.Assembly is null ? m.Entry.FolderPath : ModConflictFinder.FullPath(m),
                    Card = cards[m.ModIndex],
                    Entry = m.Entry,
                    IsKeepable = canKeep,
                }),
            ],
        };

        foreach (var row in item.Members) row.Owner = item;
        return item;
    }

    //
    // Works out, for every keepable copy on the page, what keeping it would remove - and refuses
    // (KeepBlockedReason) when that would take more than duplicates. For each other mod in the
    // conflict, every enabled folder of it that sits in ANY same-mod conflict with the kept mod goes,
    // so a hand-installed copy of both halves is cleared in one step. A mod this app installed can
    // only be removed whole, by its record; if it has a folder that duplicates nothing of the kept
    // mod, removing it would take that too, so it is refused and left to Remove on Installed.
    //
    public static void PlanKeeps(IReadOnlyList<ConflictItemViewModel> items, ModInstallManifest manifest)
    {
        var sameMod = items.SelectMany(i => i.Members).Where(m => m.IsKeepable).ToList();

        foreach (var keep in sameMod)
        {
            var removals = new List<(InstalledModCardViewModel, IReadOnlyList<InstalledMod>)>();
            string? blocked = null;

            foreach (var other in keep.Owner.Members.Select(m => m.Card).Where(c => !ReferenceEquals(c, keep.Card)).Distinct())
            {
                // Every conflict on the page that has both the kept mod and this one in it.
                var shared = items
                    .Where(i => i.Members.Any(m => m.IsKeepable && ReferenceEquals(m.Card, keep.Card))
                        && i.Members.Any(m => ReferenceEquals(m.Card, other)))
                    .SelectMany(i => i.Members.Where(m => ReferenceEquals(m.Card, other)).Select(m => m.Entry))
                    .Distinct()
                    .ToList();

                var recorded = other is { IsAppManaged: true, ModId: { } id } && manifest.Find(id, other.IsAddon) is not null;
                if (recorded && other.Entries.Any(e => !e.IsDisabled && !shared.Contains(e)))
                {
                    blocked ??= LocalizationService.Text(Strings.Conflicts_KeepBlockedFormat, other.DisplayTitle);
                }

                removals.Add((other, shared));
            }

            keep.Removals = removals;
            keep.KeepBlockedReason = blocked;
        }
    }

    private static string Describe(ModAssembly copy)
    {
        var path = copy.RelativePath.Replace('/', '\\');
        return copy.AssemblyVersion is { } version
            ? LocalizationService.Text(Strings.Conflicts_CopyFormat, path, version)
            : path;
    }

    private static string Relative(string installPath, string path)
    {
        try { return Path.GetRelativePath(installPath, path); }
        catch (ArgumentException) { return path; }
    }
}
