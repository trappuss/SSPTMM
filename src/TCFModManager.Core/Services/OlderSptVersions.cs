using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork (1.4.9): the version of a mod for this install's SPT, when it is further back than the list
// carries. sp-mod.com sends each mod's ten newest versions with the list and no more. On an older
// SPT that is often not far enough: measured 2026-11-27, UI Fixes, SVM, Dynamic Maps and Project
// Fika were among the mods whose 3.11 version is not in their ten, and each then read as having
// nothing for this SPT - no update on Subscribed items, hidden by the Workshop's SPT filter, and a
// warning before subscribing to a version that does not run here.
//
// So those mods are asked about one at a time, in the background (one small request each), and the
// answer is added to the mod's versions, where every page already looks. Answers are kept on disk
// and asked again only after RecheckAfter, so this is a few dozen requests once, not on each start.
//
public static class OlderSptVersions
{
    // How many versions sp-mod.com sends with each mod of the list (measured 2026-11-27).
    public const int Carried = 10;

    // How long an answer stands before the mod is asked about again.
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(3);

    //
    // Whether a version for this SPT may exist beyond the ones carried: the mod carries as many as
    // are ever sent, none of them runs here, and at least one is for a newer SPT than this one. A
    // mod whose versions all stop short of this SPT has nothing further back that could fit - which
    // is nearly every mod that qualifies on a current install, and none of those is asked about.
    //
    public static bool WorthAsking(Mod mod, string? sptVersion)
    {
        if (mod.Id <= 0 || string.IsNullOrWhiteSpace(sptVersion)) return false;
        // Of what sp-mod.com sent - not counting a version added here, or a mod with an answer would
        // never be asked about again, and a newer build for this SPT never be found.
        var versions = (mod.Versions ?? []).Where(v => !v.AddedForSpt).ToList();
        if (versions.Count < Carried) return false;
        if (SptVersionMatcher.ParseVersion(sptVersion) is not { } installed) return false;

        if (versions.Any(v => SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, sptVersion) == true)) return false;

        return versions.Any(v => SptVersionRange.TryParse(v.SptVersionConstraint, out var bounds)
            && (bounds.Exact ?? bounds.Min) is { } from && from > installed);
    }

    /// <summary>Adds each known answer to its mod's versions, marked as added here. Returns how many were added.</summary>
    public static int Apply(IEnumerable<Mod> mods, Answers answers, string? sptVersion)
    {
        if (string.IsNullOrWhiteSpace(sptVersion)) return 0;

        var added = 0;
        foreach (var mod in mods)
        {
            if (answers.Find(mod.Id, sptVersion) is not { Version: { } found }) continue;
            if (mod.Versions is not { } versions || versions.Any(v => v.Id == found.Id)) continue;

            // A new list, never the one in hand: a scan on another thread may be reading that one.
            mod.Versions = [.. versions.Where(v => !v.AddedForSpt), new ModVersionSummary
            {
                Id = found.Id,
                Version = found.Version,
                SptVersionConstraint = found.SptVersionConstraint,
                Downloads = found.Downloads,
                PublishedAt = found.PublishedAt,
                CreatedAt = found.CreatedAt,
                ContentLength = found.ContentLength,
                FikaCompatibility = found.FikaCompatibility,
                AddedForSpt = true,
            }];
            added++;
        }

        return added;
    }

    /// <summary>The mods still to ask about: worth asking, and not answered since RecheckAfter.</summary>
    public static List<Mod> ToAsk(IEnumerable<Mod> mods, Answers answers, string? sptVersion, DateTimeOffset now) =>
        [.. mods.Where(m => WorthAsking(m, sptVersion)
            && (answers.Find(m.Id, sptVersion!) is not { } known || now - known.AskedAt > RecheckAfter || known.AskedAt > now))];

    // What sp-mod.com answered for one mod on one SPT version; Version is null when it has none.
    public sealed class Answer
    {
        public DateTimeOffset AskedAt { get; set; }
        public ModVersionSummary? Version { get; set; }
    }

    // The answers kept on disk (Data\older_spt_versions.json), by "spt:modId".
    public sealed class Answers
    {
        public Dictionary<string, Answer> Entries { get; set; } = new(StringComparer.Ordinal);

        public Answer? Find(int modId, string sptVersion) => Entries.GetValueOrDefault(Key(modId, sptVersion));

        public void Set(int modId, string sptVersion, ModVersionSummary? version, DateTimeOffset now) =>
            Entries[Key(modId, sptVersion)] = new Answer { AskedAt = now, Version = version };

        private static string Key(int modId, string sptVersion) =>
            string.Concat(sptVersion.Trim(), ":", modId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "older_spt_versions.json");

    public static Answers Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Answers>(File.ReadAllBytes(FilePath)) ?? new Answers()
                : new Answers();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Asked again, then.
            return new Answers();
        }
    }

    public static void Save(Answers answers)
    {
        try
        {
            SafeFile.WriteBytes(FilePath, JsonSerializer.SerializeToUtf8Bytes(answers));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Debug("Catalog", $"couldn't keep the older SPT versions: {ex.Message}");
        }
    }
}
