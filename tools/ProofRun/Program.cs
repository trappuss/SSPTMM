using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

//
// Usage: dotnet run --project tools/ProofRun -- <listing.txt> <installed-mods.json> <work folder>
//
// The listing is one line per entry of a live install, "d <path>" or "f <path>", made on the
// install with:  find . -mindepth 1 \( -type f -o -type d \) -printf "%y %P\n"
// Nothing here reads or writes the live install: the copy is built under <work folder>.
//
// OPEN-10 stage 6 proof run. Rebuilds a copy of a live install's tree from its file listing (every
// file holds a unique placeholder, its own path), loads that install's real installed-mods.json, and
// removes every mod in turn, checking the disk - not what the code reports - after each step.
//
var listing = args[0];
var manifestSource = args[1];
var work = args[2];
var label = Path.GetFileName(work);

var failures = new List<string>();
var notes = new List<string>();
var rescued = new HashSet<string>(StringComparer.Ordinal);
void Fail(string m) { failures.Add(m); Console.WriteLine("  FAIL " + m); }

// The work folder is wiped on every run, so only one this tool made (it holds the marker) or an empty
// one is accepted - pointing it at a real install by mistake must fail here, not delete it.
var marker = Path.Combine(work, ".proofrun");
if (Directory.Exists(work) && Directory.EnumerateFileSystemEntries(work).Any() && !File.Exists(marker))
{
    Console.Error.WriteLine($"{work} isn't empty and wasn't made by this tool - give an empty or new folder.");
    return 2;
}
if (Directory.Exists(work)) Directory.Delete(work, true);
var install = Path.Combine(work, "install");
Directory.CreateDirectory(install);
File.WriteAllText(marker, "made by tools/ProofRun");

var lines = File.ReadAllLines(listing);
foreach (var line in lines.Where(l => l.StartsWith("d ")))
    Directory.CreateDirectory(Path.Combine(install, line[2..]));
foreach (var line in lines.Where(l => l.StartsWith("f ")))
{
    var rel = line[2..];
    var full = Path.Combine(install, rel);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    File.WriteAllText(full, "placeholder:" + rel);
}

var serverRoot = Directory.Exists(Path.Combine(install, "SPT_Runtime")) ? "SPT_Runtime" : "SPT";
var manifestPath = Path.Combine(work, "installed-mods.json");
File.Copy(manifestSource, manifestPath);
var manifest = new ModInstallManifestService(manifestPath);
var service = new ModInstallService(
    new ModDownloadService(new HttpClient()), manifest,
    removedMods: new RemovedMods(() => RemovedModsRetention.UntilCleared));

if (Directory.Exists(AppPaths.DataDirectory)) Directory.Delete(AppPaths.DataDirectory, true);
Directory.CreateDirectory(AppPaths.DataDirectory);

var holdingRoot = RemovedMods.Root(install);
var baseline = Snap();
Console.WriteLine($"[{label}] {baseline.Files.Count} files, {baseline.Dirs.Count} folders, server root {serverRoot}");

var records = manifest.Load().Mods;
var managed = records.Where(r => r.IsAppManaged).ToList();
var handInstalled = records.Where(r => !r.IsAppManaged).ToList();
Console.WriteLine($"[{label}] {managed.Count} app-installed records ({managed.Sum(r => r.Files.Count)} files), {handInstalled.Count} hand-installed");

// ---- 1. Each app-installed mod on its own: remove, check, undo, check back to the baseline. ----
Console.WriteLine($"[{label}] pass 1 - remove and undo each app-installed mod");
var totals = new Dictionary<string, int>();
foreach (var record in managed)
{
    var result = await RemoveAndCheck(record);
    Tally(result);
    Undo(record.Name);
    Same(baseline, Snap(), $"after undoing {record.Name}");
    if (manifest.Load().Find(record.ModId, record.IsAddon) is not { } back || !back.Files.SequenceEqual(record.Files))
        Fail($"{record.Name}: record not restored by Undo");
}
Console.WriteLine($"  totals: {string.Join(", ", totals.Select(t => $"{t.Key} {t.Value}"))}");

// ---- 2. Every app-installed mod removed one after another, then undone newest first. ----
Console.WriteLine($"[{label}] pass 2 - remove all, then undo all");
foreach (var record in managed) await RemoveAndCheck(record);
var afterAll = Snap();
var everyRecorded = new HashSet<string>(managed.SelectMany(r => r.Files), StringComparer.OrdinalIgnoreCase);
foreach (var gone in baseline.Files.Keys.Except(afterAll.Files.Keys, StringComparer.OrdinalIgnoreCase))
    if (!everyRecorded.Contains(gone) && !rescued.Contains(gone)) Fail($"remove all: {gone} went but no record lists it");
var left = everyRecorded.Where(f => afterAll.Files.ContainsKey(f)).ToList();
Console.WriteLine($"  after removing all: {baseline.Files.Count - afterAll.Files.Count} files gone, {left.Count} recorded files still there");
foreach (var l in left.Take(10)) Console.WriteLine("    still there: " + l);
for (var i = 0; i < managed.Count; i++) Undo("(newest)");
Same(baseline, Snap(), "after undoing every removal");
if (RemovedMods.List(install).Count != 0) Fail("holding folders left after every removal was undone");

// ---- 3. Records as v1.19.0 writes them: stamped and fingerprinted, one file in each edited. ----
Console.WriteLine($"[{label}] pass 3 - fingerprinted records, one edited file each");
var stamped = manifest.Load();
var edited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < stamped.Mods.Count; i++)
{
    var r = stamped.Mods[i];
    if (!r.IsAppManaged) continue;
    var prints = r.Files
        .Select(f => FileFingerprint.Compute(Path.Combine(install, f), f))
        .OfType<FileFingerprint>().ToList();
    stamped.Mods[i] = Copy(r, prints, install);
    var editable = r.Files.FirstOrDefault(f => File.Exists(Path.Combine(install, f)));
    if (r.Files.Count >= 2 && editable is not null)
    {
        File.AppendAllText(Path.Combine(install, editable), " - edited by the user");
        edited[r.Name + "|" + r.ModId] = editable;
    }
}
manifest.Save(stamped);
var editedBaseline = Snap();
var editOutcomes = new Dictionary<string, int>();
var zoneMoved = new List<string>();
foreach (var record in manifest.Load().Mods.Where(r => r.IsAppManaged))
{
    var zoneBefore = record.Files.Where(f => ProtectedInstallPaths.IsNewFileOnly(f) && File.Exists(Path.Combine(install, f))).ToList();
    var result = await RemoveAndCheck(record);
    zoneMoved.AddRange(zoneBefore.Where(f => !File.Exists(Path.Combine(install, f))));
    if (edited.TryGetValue(record.Name + "|" + record.ModId, out var e))
    {
        // Left in place (changed, or a protected path), or - a config, with Keep - moved to the kept
        // configs with the user's edit intact. Never into holding as if it were the mod's own copy.
        var editedContent = editedBaseline.Files[e];
        var inPlace = File.Exists(Path.Combine(install, e));
        var asConfig = !inPlace && Directory.EnumerateFiles(AppPaths.DataDirectory, "*", SearchOption.AllDirectories)
            .Any(f => File.ReadAllText(f) == editedContent);
        if (!inPlace && !asConfig) Fail($"{record.Name}: edited {e} was taken");
        if (inPlace && !result.KeptChanged.Concat(result.RefusedFiles ?? []).Contains(e, StringComparer.OrdinalIgnoreCase))
            Fail($"{record.Name}: edited {e} left without a reason");
        editOutcomes[inPlace ? "left in place" : "kept as a config"] = editOutcomes.GetValueOrDefault(inPlace ? "left in place" : "kept as a config") + 1;
    }
    Undo(record.Name);
    Same(editedBaseline, Snap(), $"after undoing {record.Name} (fingerprinted)");
}
Console.WriteLine($"  install root / Managed files proven by fingerprint and taken: {string.Join(", ", zoneMoved)}");
Console.WriteLine($"  {edited.Count} edited files: {string.Join(", ", editOutcomes.Select(o => $"{o.Value} {o.Key}"))}");

// ---- 4. A record from another install is refused with nothing touched. ----
Console.WriteLine($"[{label}] pass 4 - a record stamped with another install");
var foreign = Copy(managed.First(r => r.Files.Count > 0), [], Path.Combine(work, "some-other-install"));
try
{
    await service.UninstallAsync(install, foreign, ConfigAction.Keep);
    Fail("foreign record was not refused");
}
catch (ModInstallException ex) when (ex.Reason == ModInstallFailure.RecordFromAnotherInstall) { }
Same(editedBaseline, Snap(), "after the foreign record");
if (Directory.Exists(holdingRoot) && Directory.EnumerateFileSystemEntries(holdingRoot).Any()) Fail("foreign record made a holding folder");

// ---- 5. Two records listing one file: removing one leaves it. ----
Console.WriteLine($"[{label}] pass 5 - a file two records list");
var owner = manifest.Load().Mods.First(r => r.IsAppManaged && r.Files.Count(f => File.Exists(Path.Combine(install, f))) >= 2);
var sharedFile = owner.Files.Last(f => File.Exists(Path.Combine(install, f)));
var withShared = manifest.Load();
withShared.Mods.Add(new InstalledModRecord
{
    ModId = 999_999, Name = "Proof sharer", Version = "1.0.0", InstalledAt = DateTimeOffset.Now, Files = [sharedFile],
});
manifest.Save(withShared);
var shareResult = await RemoveAndCheck(owner, sharedFile);
if (!File.Exists(Path.Combine(install, sharedFile))) Fail($"shared {sharedFile} was taken");
if (!shareResult.KeptOwned.Contains(sharedFile, StringComparer.OrdinalIgnoreCase)) Fail("shared file not reported as kept");
Undo(owner.Name);

// ---- 6. Hand-installed Remove of every hand-installed mod's folders. ----
Console.WriteLine($"[{label}] pass 6 - hand-installed Remove");
var beforeHand = Snap();
var handCount = 0;
foreach (var record in handInstalled)
{
    var paths = record.Folders
        .SelectMany(f => new[] { Path.Combine(install, "BepInEx", "plugins", f), Path.Combine(install, serverRoot, "user", "mods", f) })
        .Where(Directory.Exists).ToList();
    if (paths.Count == 0) { notes.Add($"hand-installed {record.Name}: no folder found for {string.Join(", ", record.Folders)}"); continue; }
    var before = Snap();
    var held = service.RemoveHandInstalled(paths, install, record.Name);
    var after = Snap();
    foreach (var gone in before.Files.Keys.Except(after.Files.Keys))
        if (!paths.Any(p => Path.Combine(install, gone).StartsWith(p + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            Fail($"hand-installed {record.Name}: {gone} went but is outside its folders");
    if (after.Files.Keys.Except(before.Files.Keys).Any()) Fail($"hand-installed {record.Name}: files appeared");
    foreach (var p in paths) if (Directory.Exists(p)) Fail($"hand-installed {record.Name}: {p} still there");
    if (held is null) Fail($"hand-installed {record.Name}: not held");
    else
    {
        var r = service.UndoRemoval(install, held);
        if (r.Blocked.Count > 0) Fail($"hand-installed {record.Name}: undo blocked {r.Blocked.Count}");
    }
    Same(before, Snap(), $"after undoing hand-installed {record.Name}");
    handCount++;
}
Console.WriteLine($"  {handCount} hand-installed mods removed and put back");

// ---- 7. Protected and unsafe paths in a record are never touched. ----
Console.WriteLine($"[{label}] pass 7 - a record listing SPT's own files and unsafe paths");
var hostile = new InstalledModRecord
{
    ModId = 999_998, Name = "Proof hostile", Version = "1.0.0", InstalledAt = DateTimeOffset.Now,
    Files =
    [
        .. baseline.Files.Keys.Where(f =>
            f.StartsWith("BepInEx/core/", StringComparison.Ordinal)
            || f.StartsWith("BepInEx/plugins/spt/", StringComparison.Ordinal)
            || f.StartsWith(serverRoot + "/SPT_Data/", StringComparison.Ordinal)
            || f.StartsWith(serverRoot + "/user/profiles/", StringComparison.Ordinal)
            || f.StartsWith("EscapeFromTarkov_Data/Managed/", StringComparison.Ordinal)
            || !f.Contains('/')).Take(400),
        "../outside.txt", "/etc/passwd", "C:/Windows/notepad.exe", "BepInEx/../../outside.txt",
    ],
};
var protectedBefore = Snap();
var hostileResult = await service.UninstallAsync(install, hostile, ConfigAction.Delete);
Same(protectedBefore, Snap(), "after the hostile record");
Console.WriteLine($"  {hostile.Files.Count} paths listed, {hostileResult.RefusedFiles?.Count ?? 0} refused, {hostileResult.FilesDeleted} moved");
if (hostileResult.FilesDeleted != 0) Fail("hostile record moved files");

Console.WriteLine();
foreach (var r in rescued) Console.WriteLine("  user document kept with configs (not in any record): " + r);
foreach (var n in notes) Console.WriteLine("  note: " + n);
Console.WriteLine(failures.Count == 0 ? $"[{label}] PROOF PASSED" : $"[{label}] PROOF FAILED - {failures.Count} failures");
return failures.Count == 0 ? 0 : 1;

// --------------------------------------------------------------------------------------------------

async Task<UninstallResult> RemoveAndCheck(InstalledModRecord record, string? expectedKept = null)
{
    var before = Snap();
    var heldBefore = RemovedMods.List(install).Select(h => h.Folder).ToHashSet();
    var result = await service.UninstallAsync(install, record, ConfigAction.Keep);
    var after = Snap();
    var recorded = new HashSet<string>(record.Files, StringComparer.OrdinalIgnoreCase);

    var held = RemovedMods.List(install).Where(h => !heldBefore.Contains(h.Folder)).ToList();
    if (held.Count != 1) { Fail($"{record.Name}: {held.Count} new holding folders"); return result; }
    var holding = held[0].Folder;
    if (!File.Exists(Path.Combine(holding, RemovedMods.LogName))) Fail($"{record.Name}: no removal.json");

    var gone = before.Files.Keys.Except(after.Files.Keys).ToList();
    var dataFiles = Directory.EnumerateFiles(AppPaths.DataDirectory, "*", SearchOption.AllDirectories)
        .Select(File.ReadAllText).ToHashSet();
    foreach (var g in gone)
    {
        // The one unrecorded file a removal may take: a user document in the mod's folder (SVM's
        // presets), moved to the kept configs - never into holding, never deleted.
        if (!recorded.Contains(g))
        {
            if (dataFiles.Contains(before.Files[g])) rescued.Add(g);
            else Fail($"{record.Name}: {g} went but isn't in its record");
            continue;
        }
        var inHolding = Path.Combine(holding, RemovedMods.FilesFolder, g);
        var safe = (File.Exists(inHolding) && File.ReadAllText(inHolding) == before.Files[g]) || dataFiles.Contains(before.Files[g]);
        if (!safe) Fail($"{record.Name}: {g} went and isn't in holding or kept configs");
    }
    foreach (var added in after.Files.Keys.Except(before.Files.Keys)) Fail($"{record.Name}: {added} appeared");
    foreach (var (path, content) in after.Files)
        if (before.Files.TryGetValue(path, out var was) && was != content) Fail($"{record.Name}: {path} changed");
    foreach (var d in before.Dirs.Except(after.Dirs))
    {
        if (IsContainer(d)) Fail($"{record.Name}: container folder {d} removed");
        if (before.Files.Keys.Any(f => f.StartsWith(d + "/", StringComparison.Ordinal) && !recorded.Contains(f)))
            Fail($"{record.Name}: folder {d} removed though it held files not in the record");
    }
    foreach (var d in after.Dirs.Except(before.Dirs)) Fail($"{record.Name}: folder {d} appeared");

    var stillThere = record.Files.Where(f => after.Files.ContainsKey(f)).ToList();
    var explained = result.KeptChanged.Concat(result.KeptOwned).Concat(result.RefusedFiles ?? []).Concat(result.FailedFiles)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var s in stillThere)
        if (!explained.Contains(s) && !string.Equals(s, expectedKept, StringComparison.OrdinalIgnoreCase))
            Fail($"{record.Name}: {s} left without a reason");
    if (manifest.Load().Find(record.ModId, record.IsAddon) is not null) Fail($"{record.Name}: record still in the manifest");
    return result;
}

void Undo(string name)
{
    if (RemovedMods.LatestUndoable(install) is not { } latest) { Fail($"{name}: nothing to undo"); return; }
    var r = service.UndoRemoval(install, latest.Folder);
    if (!r.Ran || r.Blocked.Count > 0) Fail($"{name}: undo ran={r.Ran} blocked={r.Blocked.Count}");
    if (Directory.Exists(latest.Folder)) Fail($"{name}: holding folder kept after a clean undo");
}

void Tally(UninstallResult r)
{
    void Add(string k, int n) => totals[k] = totals.GetValueOrDefault(k) + n;
    Add("moved", r.FilesDeleted);
    Add("kept changed", r.KeptChanged.Count);
    Add("kept owned", r.KeptOwned.Count);
    Add("refused", r.RefusedFiles?.Count ?? 0);
    Add("failed", r.FailedFiles.Count);
    Add("configs kept", r.ConfigsKept);
}

void Same(Snapshot expected, Snapshot actual, string when)
{
    foreach (var f in expected.Files.Keys.Except(actual.Files.Keys)) Fail($"{when}: {f} missing");
    foreach (var f in actual.Files.Keys.Except(expected.Files.Keys)) Fail($"{when}: {f} extra");
    foreach (var (f, c) in expected.Files)
        if (actual.Files.TryGetValue(f, out var a) && a != c) Fail($"{when}: {f} differs");
    foreach (var d in expected.Dirs.Except(actual.Dirs))
    {
        if (expected.Files.Keys.Any(f => f.StartsWith(d + "/", StringComparison.Ordinal))) Fail($"{when}: folder {d} missing");
        else notes.Add($"{when}: empty folder {d} not recreated");
    }
    foreach (var d in actual.Dirs.Except(expected.Dirs)) Fail($"{when}: folder {d} extra");
}

bool IsContainer(string d) =>
    d.Split('/').Length <= 2
    || d is "BepInEx/plugins" or "BepInEx/patchers" or "BepInEx/config"
    || string.Equals(d, serverRoot + "/user", StringComparison.Ordinal)
    || string.Equals(d, serverRoot + "/user/mods", StringComparison.Ordinal);

Snapshot Snap()
{
    var files = new Dictionary<string, string>(StringComparer.Ordinal);
    var dirs = new HashSet<string>(StringComparer.Ordinal);
    foreach (var e in Directory.EnumerateFileSystemEntries(install, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(install, e).Replace('\\', '/');
        if (rel == RemovedMods.FolderName || rel.StartsWith(RemovedMods.FolderName + "/", StringComparison.Ordinal)) continue;
        if (Directory.Exists(e)) dirs.Add(rel);
        else files[rel] = File.ReadAllText(e);
    }
    return new Snapshot(files, dirs);
}

static InstalledModRecord Copy(InstalledModRecord r, List<FileFingerprint> prints, string installPath) => new()
{
    ModId = r.ModId, IsAddon = r.IsAddon, Guid = r.Guid, Name = r.Name, VersionId = r.VersionId, Version = r.Version,
    InstalledAt = r.InstalledAt, Files = [.. r.Files], Folders = [.. r.Folders], Incomplete = r.Incomplete,
    IsAppManaged = r.IsAppManaged, Fingerprints = prints, InstallPath = InstallStamp.Of(installPath), Overwrote = [.. r.Overwrote],
};

record Snapshot(Dictionary<string, string> Files, HashSet<string> Dirs);
