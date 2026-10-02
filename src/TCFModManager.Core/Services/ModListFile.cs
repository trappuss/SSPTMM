using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// The wrapper a shared mod list travels in.
//
// The list is nested rather than written at the top level so the file can carry things about the
// transfer that aren't part of the list itself, and so a future schema can add fields without the
// list's own shape having to absorb them.
//
public sealed class ModListDocument
{
    // Bumped only when an older app could no longer read a newer file correctly. Written per file
    // by ModListFile.SchemaVersionFor rather than fixed at the current maximum - see that method.
    public int SchemaVersion { get; set; } = ModListFile.BaseSchemaVersion;

    // What wrote it. Informational - never used to decide whether to accept the file.
    public string? App { get; set; }

    // Who shared it, when they said. Becomes the imported list's Source.
    public string? Author { get; set; }

    public DateTimeOffset ExportedAt { get; set; }

    public ModList? List { get; set; }
}

// A parsed file: the list, or why it couldn't be read. Never throws at the caller.
public sealed record ModListImport(ModList? List, string? Error)
{
    public bool Succeeded => List is not null;

    public static ModListImport Failed(string error) => new(null, error);
}

//
// Reads and writes the shareable mod list file.
//
// What travels is a manifest, never mod files - "install mod 2426 at version 5", not somebody's
// archive. That is what keeps sharing free of hosting, bandwidth and redistribution questions, and
// what keeps The Forge's download counts honest.
//
public static class ModListFile
{
    //
    // The version every list has always been written at, and the only one an app that predates
    // addon support can read.
    //
    public const int BaseSchemaVersion = 1;

    //
    // Added when addon entries did. An entry's ModId is an addon id when IsAddon is set, and an app
    // that doesn't know the field reads that id as a mod id - so it would offer to install, update
    // or disable whichever unrelated mod happens to carry the same number. That is exactly the
    // "an older app could no longer read this correctly" case the version exists for.
    //
    public const int AddonSchemaVersion = 2;

    //
    // Added when entry scope did. An app that does not know Scope reads a server-only entry as one
    // it should install - which is exactly the errand scope exists to prevent, and exactly the
    // "an older app could no longer read this correctly" case the version is for.
    //
    public const int ScopeSchemaVersion = 3;

    //
    // Added when the headless became a machine an entry could name. At 3, Scope was one of Both,
    // Client or Server; at 4 it is a set that can also contain Headless, and the two are written
    // differently - "Everyone" where a 3 wrote "Both", and "Client, Headless" where a 3 had no way
    // to say it at all.
    //
    // An app reading 3 would take "Client, Headless" as an unknown value. That is the
    // "an older app could no longer read this correctly" case, so it gets its own number - stamped,
    // as ever, only on a list that actually says something a 3 could not.
    //
    public const int HeadlessSchemaVersion = 4;

    // The highest version this app can read.
    public const int SchemaVersion = HeadlessSchemaVersion;

    //
    // The version a given list has to be written at. Only a list that actually contains an addon is
    // stamped 2, so every addon-free list stays readable by an older app - the alternative, pinning
    // every export to the newest version, would break sharing between versions to describe a
    // feature the file doesn't use.
    //
    public static int SchemaVersionFor(ModList list)
    {
        //
        // Ordered newest first: a list that says something only a 4 can express is a 4 even if it
        // also contains the things 3 and 2 were added for.
        //
        // Asked of what each entry MEANS, not of how it happens to be stored. The first cut tested
        // "does the entry carry a Scope value at all", which made the answer depend on whether
        // Everyone had been written as Everyone or left out - two spellings of one meaning, and a
        // plain list came out stamped 4. Everyone, Client and Server are all sayable at schema 3;
        // only a set naming Headless without being the whole set is new.
        //
        if (list.Entries.Any(e => !IsSayableAtScopeSchema(e.EffectiveScope))) return HeadlessSchemaVersion;

        if (list.Entries.Any(e => e.EffectiveScope != ModListEntryScope.Everyone)) return ScopeSchemaVersion;

        return list.Entries.Any(e => e.IsAddon) ? AddonSchemaVersion : BaseSchemaVersion;
    }

    // The three values schema 3 had: Both, Client and Server. Everything else needs a 4.
    private static bool IsSayableAtScopeSchema(ModListEntryScope scope) =>
        scope is ModListEntryScope.Everyone or ModListEntryScope.Client or ModListEntryScope.Server;

    // Deliberately its own extension rather than .json, so the app can be associated with it later
    // and so a double-click means something.
    public const string Extension = ".tcfmodlist";

    //
    // What a file dialog matches on. The names shown beside these are the App's - see
    // ModListFileDialog - because half of a filter string is a pattern and half is prose.
    //
    public const string FilePattern = "*" + Extension;

    public const string AllFilesPattern = "*.*";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(ModList list, string? author = null, DateTimeOffset? exportedAt = null)
    {
        var document = JsonSerializer.SerializeToNode(
            new ModListDocument
            {
                SchemaVersion = SchemaVersionFor(list),
                App = "TCFModManager",
                Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
                ExportedAt = exportedAt ?? DateTimeOffset.UtcNow,
                List = list,
            },
            Options)!;

        if (list.Origin == ModListOrigin.Local)
        {
            document[nameof(ModListDocument.List)]?.AsObject().Remove(nameof(ModList.Source));
        }

        return document.ToJsonString(Options);
    }

    public static void Save(ModList list, string path, string? author = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        SafeFile.WriteText(path, Write(list, author));
    }

    //
    // Parses a file's contents into a list ready to store.
    //
    // The imported list keeps its Id and Revision, so receiving a newer revision of a list you
    // already have updates it in place rather than leaving two. Everything that describes how a
    // list relates to *this* install is reset: Origin becomes whatever the caller says it is (and
    // neither Imported nor Server is editable - editing forks it), and a snapshot flag never
    // survives the trip, since somebody else's undo point is not one of yours.
    //
    // origin is a parameter because the same bytes arrive two ways: as a file someone sent, and as
    // the body of a server's /list. Nothing in the file itself can tell those apart, and the
    // difference decides what the app is allowed to say about where it came from.
    //
    public static ModListImport Read(string json, string? fallbackSource = null,
        ModListOrigin origin = ModListOrigin.Imported)
    {
        if (string.IsNullOrWhiteSpace(json)) return ModListImport.Failed("the file is empty");

        ModListDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<ModListDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            return ModListImport.Failed($"it isn't a readable mod list file ({ex.Message})");
        }

        if (document?.List is not { } list) return ModListImport.Failed("it doesn't contain a mod list");

        if (document.SchemaVersion > SchemaVersion)
        {
            return ModListImport.Failed(
                $"it was written by a newer version of this app (format {document.SchemaVersion}, this one reads {SchemaVersion})");
        }

        if (list.Id == Guid.Empty) return ModListImport.Failed("the list has no id");
        if (string.IsNullOrWhiteSpace(list.Name)) return ModListImport.Failed("the list has no name");

        var imported = new ModList
        {
            Id = list.Id,
            Name = list.Name.Trim(),
            Description = list.Description,
            Revision = Math.Max(1, list.Revision),
            Origin = origin,
            Policy = list.Policy,
            DerivedFrom = list.DerivedFrom,
            //
            // Who wrote it versus where you got it. For a file someone sent you the author is the
            // useful attribution, so it wins. For a served list it is the opposite: the server
            // address is the thing the user has to recognise and the thing they can go back to, and
            // an author name baked in by whoever exported it would be a stranger's name against a
            // list your own server is handing you.
            //
            Source = origin == ModListOrigin.Server
                ? fallbackSource ?? document.Author ?? list.Source
                : document.Author ?? list.Source ?? fallbackSource,
            SptVersion = list.SptVersion,
            IsSnapshot = false,
            CreatedAt = list.CreatedAt,
            UpdatedAt = list.UpdatedAt,
        };

        // Entries with no name at all can't be shown or matched on, so they are dropped rather than
        // carried through as blanks. Scope travels with them - it is the file's, not the reader's.
        imported.Entries.AddRange(list.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => WidenForHeadless(e, document.SchemaVersion)));

        return new ModListImport(imported, null);
    }

    //
    // A client-scoped entry from before the headless existed becomes Client|Headless.
    //
    // At schema 3 and below "Client" meant "not the server" - the only two machines the format could
    // describe. Read literally now, it would mean "players and not the headless", and a list an
    // operator published last week would quietly stop delivering SAIN and the bot mods to the very
    // machine hosting the raid. The author never said that; the format could not say it.
    //
    // Keyed off the DOCUMENT's version rather than the entry's shape, which is what makes it safe:
    // a schema 4 list saying Client means it, and is left alone. Server-scoped entries are untouched
    // - they meant one machine then and mean the same one now.
    //
    private static ModListEntry WidenForHeadless(ModListEntry entry, int schemaVersion)
    {
        if (schemaVersion >= HeadlessSchemaVersion) return entry;
        if (entry.Scope != ModListEntryScope.Client) return entry;

        return new ModListEntry
        {
            Name = entry.Name,
            ModId = entry.ModId,
            IsAddon = entry.IsAddon,
            VersionId = entry.VersionId,
            Version = entry.Version,
            Guid = entry.Guid,
            Folders = entry.Folders,
            Scope = ModListEntryScope.Client | ModListEntryScope.Headless,
        };
    }

    public static ModListImport Load(string path)
    {
        try
        {
            return Read(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
        }
        catch (IOException ex)
        {
            return ModListImport.Failed($"it couldn't be opened ({ex.Message})");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ModListImport.Failed($"it couldn't be opened ({ex.Message})");
        }
    }

    //
    // A filename for a list, safe on Windows and recognisable in a chat window.
    //
    // The invalid set is written out rather than taken from Path.GetInvalidFileNameChars(), which
    // answers for the platform it is running on: on Linux that is only '/' and NUL, so a name with
    // a colon in it would come back untouched and then be rejected by the Windows machine the file
    // is actually for.
    //
    public static string SuggestedFileName(ModList list)
    {
        const string invalid = "<>:\"/\\|?*";

        var name = new string([.. list.Name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c)]).Trim();

        return (string.IsNullOrWhiteSpace(name) ? "mod list" : name) + Extension;
    }
}
