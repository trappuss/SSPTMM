using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// What happens to a server mod's own config files when a new version of it is installed over the
// old one.
//
// Two steps, either side of the install placing its files:
//
//   Prepare - before anything is removed or placed, copy every config that is there now out to
//             Data\LegacyConfigs. Copied rather than moved, so a file the new version ships is still
//             in place when the placement loop overwrites it, and a file that was never tracked (a
//             mod installed by hand) is rescued too - that one used to be overwritten with nothing
//             kept anywhere.
//
//   Settle  - after the new files are placed, record what each version shipped as a baseline and
//             decide, per file, what the update did to it.
//
// Nothing here fails an install. A file that cannot be copied aside is left exactly as it was and
// reported, which is the one case where the update deliberately does less than it was asked to.
//
public sealed class ConfigCarryOver(
    ConfigBaselineStore? baselines = null,
    string? archiveRoot = null,
    ModConfigOptionsStore? options = null)
{
    private readonly ConfigBaselineStore _baselines = baselines ?? new ConfigBaselineStore();
    private readonly string _archiveRoot = archiveRoot ?? AppPaths.LegacyConfigsDirectory;
    private readonly ModConfigOptionsStore _options = options ?? new ModConfigOptionsStore();

    public ConfigBaselineStore Baselines => _baselines;

    //
    // Copies aside the config files this install already holds for the mod - the ones the old
    // version's record lists, plus the ones the new version is about to place over. Both, because
    // neither list is the whole answer: a record's list misses anything the mod or the user created
    // after the install, and the incoming list misses a file the new version has stopped shipping.
    //
    public PendingConfigs Prepare(
        string installPath,
        InstalledModRecord? existing,
        IEnumerable<string> incomingRelativePaths,
        string modName,
        DateTimeOffset timestamp)
    {
        var options = _options.Effective();
        var incoming = incomingRelativePaths.ToList();

        var candidates = new List<string>();
        if (existing is not null) candidates.AddRange(ModConfigFiles.InRecord(existing, options));
        candidates.AddRange(incoming.Where(p => ModConfigFiles.IsServerModConfig(p, ModConfigFiles.OptionsFor(p, options))));

        //
        // Files the archive is about to place over that are the user's own documents rather than
        // settings - SVM's presets. The install path skips placing these, so what is there stays
        // exactly as it is: a preset is not something to reconcile, it either exists or it doesn't.
        //
        var preserved = incoming
            .Where(p => ModConfigFiles.IsUserData(p, ModConfigFiles.OptionsFor(p, options)))
            .Where(p => File.Exists(Path.Combine(installPath, ToNative(p))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var archived = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var untouchable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var archiveFolder = ModConfigFiles.ArchiveFolder(_archiveRoot, modName, timestamp);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in candidates)
        {
            if (!seen.Add(relative)) continue;

            var source = Path.Combine(installPath, ToNative(relative));
            if (!File.Exists(source)) continue;

            var destination = Path.Combine(archiveFolder, ToNative(relative));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                archived[relative] = destination;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nowhere to put a copy means the file is not safe to replace or delete, so it is
                // left where it is and the update says so.
                AppLog.Warn("Configs", $"couldn't copy {relative} aside; leaving it alone: {ex.Message}");
                untouchable.Add(relative);
            }
        }

        if (archived.Count > 0)
            AppLog.Info("Configs", $"copied {archived.Count} config file(s) from {modName} into {archiveFolder}");

        return new PendingConfigs(archived.Count > 0 ? archiveFolder : null, archived, untouchable, preserved);
    }

    //
    // Records the new version's shipped copies and reports what the update did to each file. Run
    // after the placement loop, so the files on disk are the new version's.
    //
    public ConfigUpdateReport Settle(
        PendingConfigs pending,
        string installPath,
        InstallTarget target,
        InstalledModRecord? existing,
        InstalledModRecord installed,
        DateTimeOffset timestamp)
    {
        var options = _options.Effective();

        var placed = installed.Files
            .Where(p => ModConfigFiles.IsServerModConfig(p, ModConfigFiles.OptionsFor(p, options)))
            .ToList();

        var outcomes = new List<ConfigFileOutcome>();

        //
        // The new version's shipped copies, taken from what was just placed rather than out of the
        // extract folder - these are the exact bytes the install now holds, and they are captured
        // BEFORE a merge writes over any of them.
        //
        _baselines.Capture(installPath, target.Id, target.IsAddon, installed.Version,
            placed.Where(p => !pending.Untouchable.Contains(p)));

        var policy = _options.PolicyFor(InstalledModFolders.Resolve(installed));

        foreach (var relative in placed)
        {
            if (pending.Untouchable.Contains(relative))
            {
                outcomes.Add(new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.NotUpdated });
                continue;
            }

            outcomes.Add(Decide(pending, installPath, target, existing, relative, policy));
        }

        // Archived files the new version no longer ships. Their copy is the only one left.
        foreach (var relative in pending.Archived.Keys)
        {
            if (placed.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;

            outcomes.Add(new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.Removed });
        }

        // The user's own documents, left exactly where they were.
        foreach (var relative in pending.Preserved)
            outcomes.Add(new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.Preserved });

        // One the new version does not ship AND could not be copied: left in the install untouched,
        // which is worth saying since nothing now tracks it.
        foreach (var relative in pending.Untouchable)
        {
            if (placed.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;

            outcomes.Add(new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.NotUpdated });
        }

        _baselines.Prune(target.Id, target.IsAddon, existing?.Version, installed.Version);

        return new ConfigUpdateReport
        {
            ModId = target.Id,
            IsAddon = target.IsAddon,
            ModName = target.Name,
            FromVersion = existing?.Version,
            ToVersion = installed.Version,
            Policy = policy,
            At = timestamp,
            ArchiveFolder = pending.ArchiveFolder,
            Files = [.. outcomes.OrderBy(o => o.Path, StringComparer.OrdinalIgnoreCase)],
        };
    }

    //
    // One file's outcome. The three inputs are the file as it stood (U, now in the archive), the
    // file the old version shipped (B, the baseline) and the file now on disk (N).
    //
    // Without a baseline the only honest answer is that the file was replaced: U differing from N
    // says nothing about who changed it, and claiming the user's edits were carried when nothing
    // knows what their edits were would be worse than saying so.
    //
    private ConfigFileOutcome Decide(
        PendingConfigs pending,
        string installPath,
        InstallTarget target,
        InstalledModRecord? existing,
        string relative,
        ModConfigPolicy policy)
    {
        if (!pending.Archived.TryGetValue(relative, out var userCopy))
            return new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.Added };

        var placed = Path.Combine(installPath, ToNative(relative));

        if (SameBytes(userCopy, placed))
            return new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.Unchanged };

        //
        // Keep mine answers before anything else is asked, including before a baseline is looked for:
        // it means "this file is mine", which is the one honest option for a mod whose config the app
        // cannot reason about.
        //
        if (policy == ModConfigPolicy.KeepMine)
        {
            return Restore(userCopy, placed, relative)
                ? new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.KeptMine }
                : Replaced(relative, ConfigReplaceReason.MergeFailed);
        }

        var baseline = _baselines.Find(target.Id, target.IsAddon, existing?.Version, relative);

        // Nothing recorded what the old version shipped, so a user edit cannot be told from a changed
        // default and there is nothing safe to carry.
        if (baseline is null) return Replaced(relative, ConfigReplaceReason.NoBaseline);

        // The user never touched it, so the new defaults are simply the newer answer and nothing of
        // theirs is lost.
        if (SameBytes(userCopy, baseline))
            return new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.DefaultsUpdated };

        if (policy == ModConfigPolicy.TakeNew) return Replaced(relative, ConfigReplaceReason.TakeNewPolicy);

        return Merge(baseline, userCopy, placed, relative);
    }

    //
    // The three-way merge, written back over the file the install just placed. A merge never fails an
    // install: anything it cannot do falls back to the new version's file, which is on disk already,
    // and says why.
    //
    private static ConfigFileOutcome Merge(string baseline, string userCopy, string placed, string relative)
    {
        try
        {
            var result = JsonConfigMerge.Merge(
                File.ReadAllBytes(baseline), File.ReadAllBytes(userCopy), File.ReadAllBytes(placed));

            if (result.Stop is { } stop)
            {
                return Replaced(relative, stop == JsonConfigMergeStop.TooLarge
                    ? ConfigReplaceReason.TooLarge
                    : ConfigReplaceReason.NotMergeable);
            }

            // Nothing of the user's differed from what the old version shipped, so the new file stands
            // as it is - no write, and nothing lost.
            if (result.Carried.Count == 0 && result.Dropped.Count == 0 && result.UserAdded.Count == 0)
                return new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.DefaultsUpdated };

            if (result.Content is { } merged) SafeFile.WriteBytes(placed, merged);

            return new ConfigFileOutcome
            {
                Path = relative,
                Kind = ConfigOutcomeKind.Merged,
                Carried = result.Carried,
                Dropped = result.Dropped,
                UserAdded = result.UserAdded,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            AppLog.Warn("Configs", $"couldn't merge {relative}: {ex.Message}");
            return Replaced(relative, ConfigReplaceReason.MergeFailed);
        }
    }

    // Puts the user's own file back over the one just placed.
    private static bool Restore(string userCopy, string placed, string relative)
    {
        try
        {
            File.Copy(userCopy, placed, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Configs", $"couldn't put {relative} back: {ex.Message}");
            return false;
        }
    }

    private static ConfigFileOutcome Replaced(string relative, ConfigReplaceReason reason) =>
        new() { Path = relative, Kind = ConfigOutcomeKind.Replaced, Reason = reason };

    private static bool SameBytes(string left, string right)
    {
        try
        {
            var a = new FileInfo(left);
            var b = new FileInfo(right);
            if (!a.Exists || !b.Exists || a.Length != b.Length) return false;

            return File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ToNative(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);
}

//
// The config files copied aside before an update, by install-relative path.
//
// Two sets the install path has to read back out of here, for opposite reasons. Untouchable holds the
// ones that could NOT be copied, so replacing them would risk losing them. Preserved holds the user's
// own documents, which an update is not meant to touch in the first place.
//
public sealed record PendingConfigs(
    string? ArchiveFolder,
    IReadOnlyDictionary<string, string> Archived,
    IReadOnlySet<string> Untouchable,
    IReadOnlySet<string> Preserved)
{
    public static PendingConfigs None { get; } = new(
        null,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    // Everything the install path must not place over or delete.
    public IEnumerable<string> Protected => Untouchable.Concat(Preserved);
}
