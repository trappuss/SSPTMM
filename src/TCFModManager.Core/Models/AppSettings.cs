using System.Text.Json.Serialization;
using TCFModManager.Core.ServerMap;

namespace TCFModManager.Core.Models;

// Persisted application settings.
public sealed class AppSettings
{
    public string? SptInstallPath { get; set; }

    //
    // What this machine does with that install: whether anyone plays on it, and whether it runs a
    // Fika headless client. Together they decide which entries of a SERVED mod list are this
    // machine's to install - see InstallRole.ScopeFor.
    //
    // Two questions rather than one mode, because "dedicated headless" is just the second without
    // the first, and the machine that does both needs no value of its own.
    //
    // NULL MEANS NOT YET ANSWERED, which is why these are nullable and not plain bools. The app has
    // to tell "nobody has been asked" from "asked, and the answer was no" - the first is what makes
    // the setup prompt appear exactly once, and a plain false would make it either appear forever or
    // never. Absent from the file until answered, so an install that never meets the question keeps
    // a settings.json that says nothing about it.
    //
    // Nothing on disk can answer the first: a headless install has SPT.Server.exe like any other.
    // The headless launcher answers the second, and only the person setting the app up knows the
    // first, which is why it is asked at all rather than detected.
    //
    public bool? PlaysHere { get; set; }

    public bool? RunsHeadlessClient { get; set; }

    //
    // The Fika headless launcher, named outright, for a setup where it is not in the install folder.
    //
    // Detection looks at the top of the install folder and nowhere else, deliberately: the exe is
    // the whole signal that a machine is a headless, and every widening of that search so far has
    // either missed the real launcher or matched a player's one - see SptLaunchService. A path
    // somebody typed is not a guess, so it is the answer for any layout the search cannot reach,
    // including a manager sitting beside the SPT folder rather than inside it.
    //
    // Wins over detection when it points at a file that exists, and is ignored when it does not -
    // a moved or renamed exe falls back to the search rather than leaving the card dead.
    //
    public string? HeadlessLauncherPath { get; set; }

    //
    // Whether Start server on the Play page also opens the SPT launcher once the server is up -
    // up meaning listening on its port, which is when the launcher can reach it. Off by default:
    // a machine that hosts for others often has nobody playing on it.
    //
    public bool StartLauncherAfterServer { get; set; }

    //
    // Whether a turn of the mouse wheel glides the page to where it takes it, as a browser does,
    // or jumps there at once. On by default; the distance per turn is the same either way.
    //
    public bool SmoothScrolling { get; set; } = true;

    //
    // The sp-mod.com authors followed from the Workshop pages, by their sp-mod.com user id (names
    // can change); the name is kept for showing. Their newest items head the Workshop's front page
    // and Browse can show only theirs, as Steam does for the authors you follow.
    //
    public List<FollowedAuthor> FollowedAuthors { get; set; } = [];

    //
    // The two answers as the roles the rest of the app reasons about.
    //
    // An unanswered PlaysHere reads as yes. An install nobody has been asked about is overwhelmingly
    // somebody's own game, and the wrong guess in the other direction would filter a served list
    // down to the headless share on a machine with a player sitting at it.
    //
    [JsonIgnore]
    public InstallRoles Roles =>
        (PlaysHere is not false ? InstallRoles.Player : InstallRoles.None)
        | (RunsHeadlessClient is true ? InstallRoles.Headless : InstallRoles.None);

    // Whether the setup question has been put to anyone yet. Both halves, because a file written by
    // a build that only knew one of them is still a file that has not been answered.
    [JsonIgnore]
    public bool InstallRolesAnswered => PlaysHere is not null && RunsHeadlessClient is not null;

    // The app version whose update banner the user dismissed, so a release they've decided to skip
    // (a bug-fix one, most likely) stops raising the banner on every launch. It's compared as an
    // exact string, so anything published after it raises a fresh one.
    public string? DismissedAppUpdateVersion { get; set; }

    //
    // Which theme to use. Written as a name rather than a number, because settings.json is offered
    // for hand-editing on the Options page and "Theme": 2 would mean nothing to whoever opened it.
    //
    // Defaults to following Windows, so the app matches the rest of the desktop without anyone
    // having to find this setting - which is the point of supporting themes at all.
    //
    // This does mean an install upgrading from a build with no Theme key changes appearance on
    // first launch if Windows is set to light. That is deliberate rather than overlooked: the
    // alternative is a feature almost nobody discovers, and putting it back is one dropdown.
    //
    [JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
    public ThemePreference Theme { get; set; } = ThemePreference.FollowSystem;

    //
    // Which language the app's own text is read in, as a BCP-47 tag - "ru", "de", "pt-BR".
    //
    // NULL OR ABSENT MEANS FOLLOW WINDOWS, the same way Theme's default follows it: the app takes
    // the user's Windows display languages in their own order and uses the first one it has
    // resources for, and English when it has none of them. A tag is stored only once someone picks
    // a language on the Options page.
    //
    // A tag rather than an enum because languages are not a closed set - a translation arriving as
    // one more resource file should need no code. A tag with nothing behind it (a language dropped
    // from a later build, or a typo in a hand-edited file) is ignored and the default is used,
    // which is the rule this file already follows for a page default that no longer parses.
    //
    // Only the text follows this. Dates and numbers keep following the Windows regional setting,
    // which is a separate choice the user already made.
    //
    public string? Language { get; set; }

    //
    // Skips the "read the mod's page first" gate before anything is downloaded.
    //
    // Off by default, and turning it on is confirmed on the Options page, because the gate is not
    // busywork: a mod's page is where its author puts install steps, requirements, known conflicts
    // and warnings, and this app has no way to tell you which mods need reading before they will
    // work. Someone who knows their setup can reasonably turn it off; someone who doesn't should be
    // told what they are giving up first.
    //
    // Does not apply to this app's own update, which always asks - that page carries its release
    // notes.
    //
    public bool SkipModPageConfirmation { get; set; }

    //
    // Whether the Mod footprint page appears in the sidebar at all.
    //
    // OFF by default, deliberately. The page reads what each mod ships and describes how much of
    // the game it is positioned to touch - it times nothing and measures nothing, and what a mod
    // actually costs depends on hardware, settings and mod interactions it cannot see. Someone who
    // has read what it is can turn it on and take it for what it is; someone who meets a "Heavy"
    // label with no context is being handed a conclusion the app never made. Opt-in until the
    // measurement side of this exists to back it up.
    //
    public bool ShowModFootprintPage { get; set; }

    //
    // Whether the Installed page tags each mod with the mod lists it belongs to. On by default -
    // the badges are the point of having lists visible at all - but an install with several lists
    // puts a row of chips on every card, so it can be turned off to quieten the page down.
    //
    public bool ShowModListBadges { get; set; } = true;

    //
    // Where the main window opens and how big. Always present, the same as ServerMap below, and
    // never null for the same reason: this file is offered for hand-editing, so a "Window": null
    // written into it is a thing that happens rather than a thing to assume away.
    //
    public WindowSettings Window
    {
        get => _window;
        set => _window = value ?? new WindowSettings();
    }

    //
    // What the Installed and Browse pages open filtered and sorted to, saved from those pages
    // rather than set here - see PageDefaults.
    //
    // Null means the page has never had a default saved and uses the app's own, which is why these
    // two are nullable when everything else on this class has a value. Clearing a saved default
    // sets it back to null rather than writing out an object full of nothing.
    //
    public InstalledPageDefaults? InstalledDefaults { get; set; }

    public BrowsePageDefaults? BrowseDefaults { get; set; }

    //
    // Server Map. Always present in settings.json so the shape is obvious to anyone hand-editing
    // it, even on an install that never turns the page on.
    //
    // Never null, including when a hand-edited file says "ServerMap": null - this file is offered
    // for editing, so a null written into it is a thing that happens rather than a thing to assume
    // away.
    //
    public ServerMapSettings ServerMap
    {
        get => _serverMap;
        set => _serverMap = value ?? new ServerMapSettings();
    }

    private ServerMapSettings _serverMap = new();

    private WindowSettings _window = new();
}

//
// Where the Server Map server is and what it is trusted to be.
//
// Deliberately not derived from SptInstallPath: the address is the one the user already types into
// the SPT launcher, and the install's own http.json is stale on any Fika setup. See
// ServerMapEndpoint for why.
//
public sealed class ServerMapSettings
{
    //
    // Whether the Server map page appears in the sidebar at all.
    //
    // OFF by default and switched on deliberately, the same as the Mod footprint page: the map only
    // does anything if someone you play with runs an SPT server with the Server Map mod installed,
    // which most installs will not. A page that is empty for everyone except the people who set one
    // up is worth opting into rather than shipping to everyone.
    //
    // Kept in here rather than beside ShowModFootprintPage on AppSettings so the whole feature is
    // one object in settings.json - it is switched off and forgotten far more often than it is used.
    //
    public bool ShowPage { get; set; }

    // Host or IP as the user typed it. Empty means the feature is unconfigured, not off.
    public string? Host { get; set; }

    public int Port { get; set; } = ServerMapEndpoint.DefaultPort;

    //
    // SHA-256 thumbprint of the certificate this host presented the first time it was reached.
    //
    // Recorded on first connect and compared on every later one. SPT's certificate is self-signed
    // for localhost, so it fails the chain and hostname checks at any remote address - this is the
    // only check that means anything, and clearing it re-arms trust-on-first-use.
    //
    public string? PinnedThumbprint { get; set; }

    //
    // The server's shared key, as the operator sent it. Everything except the handshake needs it.
    //
    // Stored in the clear, which is honest about what it is: not a password and not tied to an
    // identity, just the string that says you were told about this server. It reaches settings.json,
    // which the Options page already invites people to open - anyone who can read that file can
    // already read the address it goes with.
    //
    public string? SharedKey { get; set; }

    //
    // JsonIgnore because System.Text.Json serialises get-only properties by default, so these were
    // being written into settings.json - a file the Options page invites people to hand-edit, where
    // "IsConfigured": true reads as a switch you can flip and is in fact ignored on load.
    //
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    [JsonIgnore]
    public bool HasKey => !string.IsNullOrWhiteSpace(SharedKey);

    public ServerMapEndpoint ToEndpoint() => new(Host ?? string.Empty, Port, PinnedThumbprint, SharedKey);
}

// An author followed from the Workshop pages - see AppSettings.FollowedAuthors.
public sealed class FollowedAuthor
{
    public int Id { get; set; }

    public string? Name { get; set; }
}
