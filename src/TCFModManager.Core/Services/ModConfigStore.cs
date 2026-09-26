using System.Text;
using System.Text.Json;

namespace TCFModManager.Core.Services;

// What happened to a save. Anything other than Saved left the file on disk exactly as it was.
public enum ModConfigSaveOutcome
{
    Saved,

    // The file changed on disk after it was loaded here. Refused rather than written, so an edit
    // made in a text editor (or by the game itself) isn't silently thrown away.
    ChangedOnDisk,

    // The text isn't valid for the file's format, so writing it would leave the mod unable to read
    // its own settings.
    Invalid,

    Failed,
}

// A config file as it was read: its text, the write time it had at the time, and whether it carried
// a byte order mark, so a save can put back exactly the same shape of file.
public sealed record ModConfigDocument(string Text, DateTime LastWriteUtc, bool HasByteOrderMark);

public sealed record ModConfigSaveResult(
    ModConfigSaveOutcome Outcome,
    ModConfigDocument? Saved,
    string? BackupPath,
    string? Error)
{
    public bool Succeeded => Outcome == ModConfigSaveOutcome.Saved;
}

//
// Reads and writes mod config files.
//
// Three things stand between an edit and the file, in this order: the text has to be valid for its
// format, the file has to still be the one that was loaded, and the old contents have to be safely
// copied aside first. Only then is anything written. Nothing here ever deletes a file.
//
public static class ModConfigStore
{
    //
    // Folder holding a copy of every config file taken before it was overwritten.
    //
    // Kept in the app's own Data folder rather than in the SPT install: this is the app's working
    // data, not the game's, and scattering folders through someone's install is how a mod manager
    // makes a mess of it. Being outside the install is also why it is neither dot-prefixed nor
    // hidden the way ".tcfmm-work" and ".tcfmm-duplicates" are - those exist to stay out of SPT's
    // way, whereas the whole point of a backup is being able to find it.
    //
    public const string BackupFolderName = "config-backups";

    // Where the backups actually go: "<app>\Data\config-backups".
    public static string BackupDirectory => Path.Combine(AppPaths.DataDirectory, BackupFolderName);

    // How that folder is named to the user - short enough for a status line.
    public static string BackupDisplayPath => $"Data\\{BackupFolderName}";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    //
    // Reads a config file. Read as bytes rather than through File.ReadAllText so a byte order mark
    // can be noticed and put back on save - BepInEx writes its .cfg files without one, and rewriting
    // a file with one added is a change to a file the user didn't ask to change.
    //
    public static ModConfigDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Utf8NoBom.GetString(hasBom ? bytes.AsSpan(3) : bytes);

        return new ModConfigDocument(text, File.GetLastWriteTimeUtc(path), hasBom);
    }

    //
    // Writes <paramref name="text"/> over the file <paramref name="loaded"/> came from, backing up
    // what was there first.
    //
    // <param name="loaded">What Load returned. Carries the write time the save is checked against
    // and the byte order mark to reproduce.</param>
    // <param name="overwriteChangesOnDisk">Set once the user has been told the file changed
    // underneath them and has chosen to overwrite it anyway.</param>
    //
    public static ModConfigSaveResult Save(
        string installPath,
        string path,
        string text,
        ModConfigDocument loaded,
        DateTimeOffset timestamp,
        bool overwriteChangesOnDisk = false)
    {
        if (ValidateFor(path, text) is { } invalid)
            return new ModConfigSaveResult(ModConfigSaveOutcome.Invalid, null, null, invalid);

        try
        {
            if (!overwriteChangesOnDisk && File.Exists(path) && File.GetLastWriteTimeUtc(path) != loaded.LastWriteUtc)
            {
                return new ModConfigSaveResult(
                    ModConfigSaveOutcome.ChangedOnDisk,
                    null,
                    null,
                    "This file has changed on disk since it was opened here.");
            }

            var backup = Backup(installPath, path, timestamp);

            SafeFile.WriteAllText(path, text, loaded.HasByteOrderMark ? Utf8WithBom : Utf8NoBom);

            AppLog.Info("Configs", $"saved {Path.GetFileName(path)}{(backup is null ? "" : $" (backup: {backup})")}");

            return new ModConfigSaveResult(
                ModConfigSaveOutcome.Saved,
                loaded with { Text = text, LastWriteUtc = File.GetLastWriteTimeUtc(path) },
                backup,
                null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Warn("Configs", $"couldn't save {path}: {ex.Message}");
            return new ModConfigSaveResult(ModConfigSaveOutcome.Failed, null, null, ex.Message);
        }
    }

    //
    // Copies the file as it currently stands into the backup folder, keeping its path relative to
    // the install so a whole timestamped folder can be copied back over an SPT install to undo a
    // round of edits. Returns null when there was nothing to copy.
    //
    // A failure here is not allowed to stop the save: the backup is a convenience, and refusing to
    // write a config because a spare copy couldn't be made would be the more annoying failure.
    //
    public static string? Backup(string installPath, string path, DateTimeOffset timestamp)
    {
        if (!File.Exists(path)) return null;

        try
        {
            // The path inside the backup stays relative to the SPT install even though the backup
            // itself lives with the app, so a timestamped folder can still be copied straight back
            // over an install to undo a round of edits.
            var relative = RelativeForBackup(installPath, path);
            var stamp = $"{timestamp.ToLocalTime():yyyyMMdd-HHmmss}";
            var folder = Path.Combine(BackupDirectory, stamp);
            var destination = Path.Combine(folder, relative);

            // A second save of the same file within the same second must not replace the backup of
            // what was there before the first: that one is the copy worth having.
            for (var n = 2; File.Exists(destination); n++)
            {
                folder = Path.Combine(BackupDirectory, $"{stamp}-{n}");
                destination = Path.Combine(folder, relative);
            }

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.Copy(path, destination, overwrite: false);

            // Which SPT install these copies came from, so another install's are not offered back.
            var marker = Path.Combine(folder, InstallMarker);
            if (!File.Exists(marker)) File.WriteAllText(marker, Path.GetFullPath(installPath));

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            AppLog.Warn("Configs", $"couldn't back up {path}: {ex.Message}");
            return null;
        }
    }

    //
    // Null when the text is fine to write, otherwise why it isn't. A .json5 file is not checked:
    // JSON5 allows unquoted keys and single-quoted strings, which no JSON reader here accepts, so a
    // check would refuse every file the mod itself reads happily.
    //
    public static string? ValidateFor(string path, string text) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cfg" => null,
            ".json5" => string.IsNullOrWhiteSpace(text) ? "The file is empty." : null,
            _ => ValidateJson(text),
        };

    //
    // Null when the text parses as JSON, otherwise a message naming where it stopped making sense.
    //
    // Comments and trailing commas are accepted, because a large share of SPT server mods ship
    // JSON5/JSONC configs full of both - rejecting a file the mod itself reads happily would be this
    // editor being wrong about the file rather than the file being wrong.
    //
    public static string? ValidateJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "The file is empty.";

        try
        {
            using var _ = JsonDocument.Parse(
                text,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            return null;
        }
        catch (JsonException ex)
        {
            var message = Shorten(ex.Message);

            return ex.LineNumber is { } line
                ? $"Line {line + 1}, position {(ex.BytePositionInLine ?? 0) + 1}: {message}"
                : message;
        }
    }

    // System.Text.Json appends its own position record to the message, which is already being said
    // more readably by the caller above.
    private static string Shorten(string message)
    {
        var index = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        return index < 0 ? message : message[..index].Trim();
    }

    // In each backup folder: the SPT install its copies came from.
    private const string InstallMarker = ".install";

    /// <summary>The copies of a config kept by earlier saves (Backup), newest first.</summary>
    public static IReadOnlyList<ModConfigBackup> BackupsOf(string installPath, string path)
    {
        if (!Directory.Exists(BackupDirectory)) return [];

        var relative = RelativeForBackup(installPath, path);
        var found = new List<ModConfigBackup>();

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(BackupDirectory))
            {
                // "yyyyMMdd-HHmmss", or "yyyyMMdd-HHmmss-2" for a second save in the same second.
                var name = Path.GetFileName(dir);
                if (name.Length < 15 || !DateTime.TryParseExact(name[..15], "yyyyMMdd-HHmmss",
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var savedAt))
                    continue;

                // Kept from another SPT install (copies from before this was written down are shown).
                var marker = Path.Combine(dir, InstallMarker);
                if (File.Exists(marker) && !string.Equals(File.ReadAllText(marker).Trim(), Path.GetFullPath(installPath),
                        StringComparison.OrdinalIgnoreCase)) continue;

                var copy = Path.Combine(dir, relative);
                if (File.Exists(copy)) found.Add(new ModConfigBackup(copy, savedAt, name));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Configs", $"couldn't list the backups of {Path.GetFileName(path)}: {ex.Message}");
        }

        return [.. found.OrderByDescending(b => b.SavedAt).ThenByDescending(b => b.Folder, StringComparer.Ordinal)];
    }

    // The config file's path relative to the install, or just its name when it sits outside one.
    private static string RelativeForBackup(string installPath, string path)
    {
        var relative = Path.GetRelativePath(installPath, path);

        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? Path.GetFileName(path)
            : relative;
    }

}

/// <summary>One kept copy of a config: where it is, and when the save that kept it was made
/// (local time).</summary>
public sealed record ModConfigBackup(string Path, DateTime SavedAt, string Folder);
