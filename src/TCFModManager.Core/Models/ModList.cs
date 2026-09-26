using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

// Where a mod list came from. Only Local lists can be edited in place; the other two are records of
// something someone else authored, and editing one forks it to a new list (see ModListStore.Fork).
public enum ModListOrigin
{
    // Made here, from this install or by hand.
    Local,

    // Received as a file from someone else.
    Imported,

    // Served by a server this install connected to.
    Server,
}

//
// Which machines an entry is for.
//
// A server's published list describes the whole server, and not all of it is a client's business:
// the Server Map mod itself and fika-server live in user\mods, are often not on The Forge at all,
// and a client told to install them is being sent on an errand. Leaving them off the list instead
// is worse - the operator applying their own published list would then disable the very mod that
// serves it. Scope is what breaks that: one list, and the entries say who they concern.
//
// A SET rather than one value, because a Fika headless client is a third kind of machine and not a
// point on a line between the other two. It runs the game, so it needs the plugins that decide how
// a raid plays - bots, items, locations - and it has no use for the ones that draw things at a
// player. "Everyone", "clients and the headless" and "clients only" are all real answers, and none
// of them is expressible by picking one of three.
//
// Inferred at capture from where a mod's files land, so it costs the operator nothing in the common
// case and can be overridden per entry. The inference deliberately INCLUDES the headless in
// everything it is unsure about: a headless carrying a mod it did not need costs nothing anyone can
// see, while a missing bot or item mod on the machine hosting the raid is felt by everybody in it.
//
[Flags]
public enum ModListEntryScope
{
    // A playing client - a BepInEx plugin at a human being.
    Client = 1,

    //
    // The machine running the SPT server. A player applying a SERVED list skips these entirely: not
    // installed, not disabled, not reported missing.
    //
    // A headless box does NOT skip an entry scoped to the server ALONE: it is a full SPT install
    // and such an entry is a whole mod rather than half of one, so taking it is an ordinary install.
    // Set alongside Client, the server flag says nothing about the headless - Client + Server is how
    // an author says the players and the server need this and the headless does not.
    //
    Server = 2,

    //
    // A Fika headless client - the machine that hosts the raid without anyone playing on it.
    //
    // Set on its own, the entry is for the headless and nobody else. Cleared while Client is set,
    // it is the tag that does the pruning: "players need this, the headless does not".
    //
    Headless = 4,

    // Every machine. What an entry means when it says nothing, so a list written before scope
    // existed - and one written before the headless did - means exactly what it meant.
    Everyone = Client | Server | Headless,
}

// What applying a list does to installed mods the list doesn't mention.
public enum ModListPolicy
{
    // Anything not on the list is disabled, so the install ends up as the list describes it.
    Exclusive,

    // Nothing is disabled; the list's mods are installed and enabled alongside whatever is there.
    Additive,
}

// One mod in a list.
//
// Three states, and the difference decides what applying it can do:
//   - pinned      ModId and VersionId both known, so an exact version can be fetched from The Forge.
//   - resolved    ModId known but VersionId isn't, because the pinned version isn't in the cached
//                 version list (only the six most recent are embedded on a catalog Mod). The mod is
//                 still fetchable; the version has to be looked up or fallen back on.
//   - unresolved  no ModId at all - a GitHub-only mod, a hand-installed one, anything the catalog
//                 matcher couldn't place. Carried by name so the receiver is told to fetch it
//                 themselves, never silently dropped.
public sealed class ModListEntry
{
    // The mod's display name as it read when the list was made - the catalog listing name where one
    // matched, the folder name otherwise.
    public required string Name { get; init; }

    public int? ModId { get; init; }

    //
    // True when ModId is an sp-mod.com addon id rather than a mod id. The two are separate
    // sequences, so every comparison of ModId has to carry this with it - otherwise a list naming
    // addon 116 would match, install or disable mod 116 on the receiving side.
    //
    // Defaults false, so a list written before addons were supported keeps meaning what it meant.
    // A list that contains one is exported at share-file schema 2 (see ModListFile), which an
    // older app refuses outright rather than misreading.
    //
    public bool IsAddon { get; init; }

    public int? VersionId { get; init; }

    // The version string as installed. Kept alongside VersionId so a pinned version that has since
    // been taken down can still be named ("pinned 1.4.2 is gone, latest is 1.5.0").
    public string? Version { get; init; }

    // The mod's plugin GUID where it has one, as a second join key on the receiving side. Always
    // null for an addon - sp-mod.com doesn't give addons a GUID.
    public string? Guid { get; init; }

    // The mod folder names on disk this entry covers, lowercased - the same names
    // InstalledModScanner reports. What an unresolved entry is matched on locally.
    public List<string> Folders { get; init; } = [];

    //
    // Which machines this entry is for, or null for "every machine" - which is almost always, so
    // scope still costs nothing in a list that does not use it.
    //
    // Nullable rather than defaulting to Everyone because the default of a [Flags] enum is zero,
    // and zero here reads as "no machine at all". An omitted value has to mean everyone, and the
    // only way to say that without letting a stray default silently exclude every machine is for
    // absent to be its own state. Read it through EffectiveScope, never directly.
    //
    // SETTING Everyone STORES NULL. The two say the same thing, and letting both exist meant the
    // file could carry "Scope": "Everyone" - which stamps the list at a schema an older app refuses,
    // to record a value that means exactly what saying nothing means. Normalised here rather than at
    // each caller because there are four of them and a fifth will be written by someone who has not
    // read this comment.
    //
    // The converter is named on the PROPERTY, not on the enum. Both ModListFile and ModListStore
    // put a plain JsonStringEnumConverter in their options, and an options-level converter beats a
    // type-level attribute - so a type attribute was silently ignored and every legacy "Both" failed
    // to parse. A property attribute outranks both.
    //
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(ModListEntryScopeConverter))]
    public ModListEntryScope? Scope
    {
        get => _scope;
        init => _scope = value == ModListEntryScope.Everyone ? null : value;
    }

    private readonly ModListEntryScope? _scope;

    // What Scope means, with absent resolved. The only thing that should be compared against.
    [JsonIgnore]
    public ModListEntryScope EffectiveScope => Scope ?? ModListEntryScope.Everyone;

    //
    // JsonIgnore on all four computed members here and on ModList below. System.Text.Json writes
    // get-only properties by default, so every .tcfmodlist ever exported carried IsPinned and
    // IsResolved on each entry, and IsEditable plus a SECOND FULL COPY of every unresolved entry
    // under "Unresolved" on the list. Nothing has ever read them back - there are no setters - so
    // this only makes the file smaller and honest.
    //
    // Not a schema change: an older app never set these either, so a file without them means
    // exactly what a file with them meant.
    //
    [JsonIgnore]
    public bool IsPinned => ModId is not null && VersionId is not null;

    [JsonIgnore]
    public bool IsResolved => ModId is not null;
}

// A named set of mods: a playlist, a shared list and a server-served list are all this same object,
// differing only in Origin and in how they arrived.
public sealed class ModList
{
    public required Guid Id { get; init; }

    // Free text and not unique - two people can both call a list "Fika night". Id is the identity.
    public required string Name { get; set; }

    public string? Description { get; set; }

    // Bumped on every edit that changes Entries. Monotonic per Id, so a receiver can tell a newer
    // revision of a list it already has from an older one.
    public int Revision { get; set; } = 1;

    public ModListOrigin Origin { get; init; } = ModListOrigin.Local;

    public ModListPolicy Policy { get; set; } = ModListPolicy.Exclusive;

    // The list this one was forked from, when it was made by editing an imported or served list.
    public Guid? DerivedFrom { get; init; }

    // Who or what it came from - an author name for an imported file, a server address for a served
    // list. Null for a list made here.
    public string? Source { get; init; }

    // The SPT version this install was running when the list was captured, so a receiver on a
    // different version can be warned before anything is fetched.
    public string? SptVersion { get; init; }

    //
    // The page this list was taken from, for one subscribed to from sp-mod.com's public lists
    // (sp-mod.com/list/{id}/{slug}). Its collection page is read from there again each time it is
    // opened, so what the author has changed since shows. Null for every other list - and absent
    // from the file then, so a list without one is written exactly as before.
    //
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Link { get; init; }

    //
    // True for a list written automatically to record the install as it stood before a list was
    // applied - the "put me back" undo.
    //
    // A snapshot never lives in ModListData.Lists; there is one slot for it, overwritten by each
    // apply (see ModListData.Snapshot). It was a normal list to begin with, which meant applying
    // one produced a snapshot of a snapshot, and the names grew a "Before " every time.
    //
    public bool IsSnapshot { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<ModListEntry> Entries { get; init; } = [];

    //
    // IsEditable was the actively misleading one: a served list is written by an app where it was
    // Local, so the file said "IsEditable": true while the receiving app - which sets Origin to
    // Server on the way in - correctly treats it as read-only. A document that contradicts the
    // program reading it is worse than one that says nothing.
    //
    [JsonIgnore]
    public bool IsEditable => Origin == ModListOrigin.Local;

    // Entries that can't be fetched from The Forge, so the receiver has to install them by hand.
    [JsonIgnore]
    public IEnumerable<ModListEntry> Unresolved => Entries.Where(e => !e.IsResolved);

    //
    // The entries that concern a machine of the given kind.
    //
    // A list of your own describes your install, all of it, so all of it applies whatever this
    // machine is - you are the one who wrote it. A list a SERVER handed you describes that server's
    // whole setup, and only the part matching what this machine actually is, is yours to install.
    //
    // Pass what InstallRoles.ScopeFor gives for this install: Client for an ordinary player,
    // Headless for a headless box, both together for a machine that plays and hosts.
    //
    public IEnumerable<ModListEntry> EntriesApplyingTo(ModListEntryScope machine) =>
        Origin == ModListOrigin.Server
            ? Entries.Where(e => ScopeApplies(e.EffectiveScope, machine))
            : Entries;

    //
    // Whether an entry with this scope is a machine of this kind's to install.
    //
    // An overlap is the whole of it, bar one case: an entry scoped to the SERVER ALONE also goes to
    // a headless box. A mod that only ever had a server half is a whole mod, and the headless is a
    // full SPT install running one - so taking it is an ordinary install rather than half of one,
    // and nothing here ever installs half a mod.
    //
    // Server ALONE, and that is the point of the rule living here rather than in the machine's own
    // scope. "Server + Client" is an author saying the server and the players need this and the
    // headless does not, and it can only mean that if naming the server does not by itself reach
    // the headless.
    //
    public static bool ScopeApplies(ModListEntryScope entry, ModListEntryScope machine) =>
        (entry & machine) != 0
        || (entry == ModListEntryScope.Server && machine.HasFlag(ModListEntryScope.Headless));
}

// Every list this install holds, plus which one is currently applied.
public sealed class ModListData
{
    //
    // The shape this file was last written in, so a one-time migration can run once and then stop.
    // Absent - and so zero - on any file written before the headless existed, which is exactly the
    // set of files whose client-scoped entries have to be widened to reach a headless. See
    // ModListStore.Normalise.
    //
    // This is the STORE's version and has nothing to do with ModListFile's share-file schema; the
    // two files are read by different code and move for different reasons.
    //
    public int SchemaVersion { get; set; }

    public List<ModList> Lists { get; init; } = [];

    //
    // The install's OWN list - the one it chose to follow. Null when it isn't following one.
    //
    // One at a time, because two personal lists both claiming to describe this install would be two
    // answers to one question.
    //
    public Guid? ActiveListId { get; set; }

    //
    // The list a SERVER hands this install, followed alongside the one above rather than instead of
    // it.
    //
    // Two slots because they answer different questions and both can be true at once: the server
    // says what its players need, and the player's own list says what else they like running. A
    // single slot forced a choice nobody should have to make - follow the server and lose your own
    // client-side mods from the list that protects them, or keep your list and have an Exclusive
    // apply set aside every mod the server requires.
    //
    // What makes them coexist is in ModListPlanner: a served list never disables, and a personal
    // list's Exclusive sweep spares everything the followed server list names.
    //
    public Guid? ActiveServerListId { get; set; }

    //
    // The list this machine PUBLISHES to its own server. Housekeeping, not behaviour: nothing in the
    // planner reads it, and what an apply does is decided by Origin and Scope.
    //
    // A pointer here rather than a flag on ModList, which is where it started and where it did not
    // work. A share file and this store are serialised by different code but from the SAME objects,
    // so the [JsonIgnore] that correctly kept the mark out of an exported list ALSO kept it out of
    // mod_lists.json - the mark was set, saved into nothing, and gone by the next read. The badge
    // never appeared and the app forgot which list it served the instant it was told.
    //
    // As a pointer it cannot repeat that: it is not on ModList at all, so no share file can carry
    // it and no reader can mistake somebody else's list for the one this machine serves. It also
    // matches the two pointers above, which answer the same shape of question.
    //
    public Guid? PublishedListId { get; set; }

    //
    // Mods this install never lets a list's Exclusive sweep set aside, as lowercased folder names.
    // Kept here rather than on ModListEntry so a pin describes this install and never travels with a
    // shared list.
    //
    public List<string> NeverAutoDisable { get; init; } = [];

    //
    // How the install stood before the last list was applied, and the only one kept - each apply
    // overwrites it, and reverting consumes it. Deliberately outside Lists: it is an undo point,
    // not something to browse, share or apply by hand.
    //
    public ModList? Snapshot { get; set; }
}
