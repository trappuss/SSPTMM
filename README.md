# TCFModManager

A Windows desktop app for finding, installing and keeping track of [SPT](https://sp-tarkov.com)
mods, built against [sp-mod](https://sp-mod.com) catalog. Browse the full mod list, filter it
down to what actually works on your SPT version, install with dependencies resolved for you, and
see at a glance what's out of date.

WPF with Fluent Design, .NET 9, no account or API key needed.

> Standalone for now. Integration with [TCFModSync](https://github.com/TheCrimsonFckr/TCFModSync)
> (pushing a managed mod set out to clients) is the direction of travel manage first, then sync.

## Requirements

- Windows
- An SPT install (the app reads its version from the server exe `SPT.Server.exe`, or the older
 `Aki.Server.exe`, at the install root or under `SPT\` / `SPT_Runtime\`)

The released build is self-contained, so no separate .NET runtime install is needed.

## Install

1. Grab the latest `TCF-ModManager-<version>.zip` from Releases.
2. Extract it into your SPT folder as `<SPT root>\TCFModManager\` a sibling of `BepInEx\` and
  `user\`, **not** a mod folder. Anywhere else works too; it just needs to know where SPT is.
3. Run `TCFModManager.exe`.
4. Go to **Options**, point it at your SPT install folder - the top one, with `EscapeFromTarkov.exe`
  and `BepInEx\` in it, not `SPT\` or `SPT_Runtime\` - and hit Save. The detected server
  version appears underneath everything else keys off that. Picking the server folder by mistake
  is corrected to the folder above it.

## What it does

**Browse** the whole sp-mod.com catalog, fetched once and cached to disk. Search by name or by
author (`@author`), filter by SPT release line, category, Fika compatibility, featured status, and
toggles for hiding ads and AI-generated content. Sort by newest, last updated, most downloaded, most
favourited or most endorsed. Cards show a status dot (installed / update available / disabled / not
installed / nothing compatible published), a badge for mods that pull in dependencies, a badge
counting the mod's addons, and an endorsement count when the mod has any. Clicking a card opens its
details; "Refresh cache" re-pulls the catalog.

**Addons** are sp-mod.com's own concept: extra content published against a *parent mod* rather than
against an SPT release, and versioned against that mod's version. They aren't listed as mods of
their own - a mod's addons appear inside its details dialog, on Browse and on Installed alike, each
with its own version picker and Install button. A version whose parent requirement your install
doesn't meet is shown with the reason rather than hidden ("Needs Raid Review ^1.5.0 - you have
1.4.2"). Installed addons appear on the Installed page as their own cards, labelled with the mod
they attach to, and are captured into and applied by mod lists like anything else.

**Installed** scans your SPT folder for what's actually there, both client mods
(`BepInEx\plugins`, `BepInEx\patchers`) and server mods (`user\mods`), and matches them back to the
catalog. Same search and filters as Browse, plus update-status, enabled/disabled and group filters.
Clicking a mod opens a dialog with the full version history (changelogs rendered from sp-mod's rich
text), a link to the mod page, and an Update button when one applies. Mods can be removed from here
too.

A BepInEx patcher belongs to a mod rather than being one, so a patcher folder is shown as part of
the mod that placed it (`Client + Patcher`) rather than as a second entry of its own. Since a patcher
is conventionally named after its mod plus a word like `Patcher` or `Prepatch`, it's matched to a
listing by that name with the extra word removed - `MoreBotsPrepatch` finds `MoreBotsAPI`. A patcher
that can't be tied to a mod is still listed, labelled `Patcher only`.

Two kinds of file in `BepInEx\patchers` aren't listed at all, the same way core SPT plugins aren't:
SPT's own preloader patcher, and general BepInEx utilities that mods bundle alongside themselves -
currently [FixPluginTypesSerialization](https://github.com/xiaoxiao921/FixPluginTypesSerialization).
Neither is published on sp-mod.com or yours to manage from here; they belong to SPT and to whichever
mod shipped them.

There are three ways to look at the same filtered, sorted list, switched with the buttons at the top
of the page:

- **Cards** - a paginated grid of summary cards, with a Select mode for acting on several at once.
- **Groups** - your own MO2-style separators, drag a mod between them, collapse what you're not
  working on, and enable/disable/invert a whole group at once.
- **List** - one continuous scrollable row per mod, each opening onto everything the app knows about
  it: GUID, installed and published versions, install date, group, content flags, whether it was
  installed by this app or by hand, and the actual folders it occupies.

Sort by name, author, group, or install date; filter to one group or to whatever isn't in one. Every
filter applies to all three views.

**Disabling mods.** A disabled mod is moved into a `.disabled` sibling of the folder SPT loads it
from - `user\mods` to `user\mods.disabled`, `BepInEx\plugins` to `BepInEx\plugins.disabled` - so SPT
ignores it while nothing is deleted. Its own files go with it, server configs included, and client
mod settings in `BepInEx\config` are never touched, so a disable/enable round trip loses no
settings. Disabled mods stay listed, dimmed and marked, and go back exactly where they came from.

Disable one mod from its card, tick several in the card grid's Select mode, or use a group's
enable-all / disable-all / invert buttons. Before a disable that would take something else's
dependency away - or an enable whose own dependencies are still disabled - the app lists what's
affected and offers to carry those along. Dependencies are read from the mods themselves
(`[BepInDependency]` for client mods, `modDependencies` for server mods), so this works offline and
covers hand-installed mods that never matched a catalog listing. Undo puts the last change back.

Whether a mod is disabled is read purely from where it sits on disk, so moving folders by hand works
too. Update, Redownload and Remove are unavailable while a mod is disabled - their install record
points at folders it no longer occupies - so enable it first. Browse shows the same state on its own
cards and refuses a reinstall for the same reason.

If a mod ends up in *both* an enabled and a disabled folder - a move interrupted partway, or one
done by hand - its card says so and offers "Sort out": pick which copy to keep, and the other is
moved into a hidden `.tcfmm-duplicates` folder in your SPT install rather than deleted. Undo puts
that back too.

**Dependencies** resolves the dependency tree of every installed mod that declares one, and
reports each dependency's state against what's on disk, including version conflicts where two mods
want incompatible versions of the same thing. Missing dependencies can be installed straight from
the list.

**Downloads** the install queue. Items process one at a time; each resolves its dependencies and
queues those alongside it. Progress, per-item cancel, and "Clear finished". Cancelling a mod also
cancels the dependencies it dragged in. The page also handles plain archive downloads when you'd
rather install something by hand.

**App update** whether a newer version of this app has been published, what kind of change it is,
the release notes, and the button that installs it. See "Updating the app itself" below.

**Options** SPT install folder, and the detected SPT version.

Before anything is queued, the app asks you to open each mod's page on sp-mod.com first same as
installing manually, and it keeps mod authors' page views and instructions in the loop.

### Updating the app itself

This app has its own mod page on sp-mod.com, the same as everything else you install through it. On
launch it asks that listing whether anything newer has been published, and if so raises a banner and
puts a badge on the **App update** item in the sidebar.

Everything about this goes through sp-mod.com: the check reads the public API, the download is the
file the mod page's own Download button serves, and - exactly as with any other mod - **you're asked
to open the mod page before anything is downloaded**. Nothing here works around sp-mod.com, its
download counts, or anything a mod author has put on a page. It isn't hidden from the site either.
The only thing it changes about Browse is that the app no longer lists *itself* among the mods it
installs into an SPT folder, since it isn't one - installing it from there would drop a second copy
of the manager into `BepInEx\plugins` for SPT to try to load.

**What the version number tells you.** The update page names the change rather than leaving you to
work it out from the numbers:

| Change | Means |
| --- | --- |
| `x.x.`**`1`** | **Bug fix.** Fixes to how the current version already works. Nothing new to learn - safe to skip if nothing is broken for you. |
| `x.`**`1`**`.x` | **Feature update.** Something new, or something works differently. Worth reading the notes. |
| **`1`**`.x.x` | **Major update.** Significant changes. Read the notes and the mod page first. |

Closing the banner skips that release - it won't come back for that version, though anything
published after it will. The **App update** page is always there regardless, with a Check now button.

**How the swap works.** A running program can't overwrite itself, so this is done from outside it.
The release is downloaded and unpacked into a hidden `.tcfmm-update\` folder next to the exe and
checked for a real `TCFModManager.exe` before anything else happens. The app then starts a small
PowerShell script, closes, and the script waits for it to actually exit before copying the new build
into place and starting it again.

That copy is additive, never a mirror: `Data\` and `Staging\` sit in the same folder as the exe, so
your SPT path, install history, kept mod configs and config backups are left exactly as they were. If anything fails - no write access to the folder, not enough disk, a copy that doesn't go
through - **the version you already have is left untouched**, and the unpacked build stays in
`.tcfmm-update\payload\` so you can copy it over by hand. What happened is written into the app's
own log on the next launch.

If the app lives somewhere it can't write to (inside Program Files, say), it says so up front
instead of trying, and points you at the mod page.

### How installs work

The archive is downloaded and extracted into a hidden scratch folder inside your SPT install
(`.tcfmm-work\`, swept of stale runs each time), then moved into place. When you're updating,
the previous version is only removed **after** the new one has downloaded and extracted
successfully a failed or cancelled download can't leave you with neither. Once files start
being placed, the operation runs to completion rather than tearing out a half-installed mod.

Installing and removing both refuse to start while Tarkov or the SPT server is running, since
those hold open the files being replaced; the check runs again once the download finishes, in case
SPT was launched while it was in progress. If a file placement fails anyway, what was placed
before the failure is still recorded and marked incomplete, so those files stay app-managed - a
reinstall completes the mod, a removal clears it out.

A server mod's own config files aren't simply deleted with the rest of it. Removing a mod asks
whether to keep them - moved into a timestamped folder under `Data\LegacyConfigs\` with their
install-relative paths intact, so the folder can be copied back over
an SPT install to restore them - or delete them. Updates always keep them, without asking.

Every install is recorded in `Data\installed-mods.json`, which is what makes a clean uninstall
possible later. That file not folder names, not DLL file versions is the authority on what's
installed and at what version.

### Where it keeps things

Everything lives next to the exe, not in `%LocalAppData%`:

| Path | What |
| --- | --- |
| `Data\settings.json` | SPT install path and app settings |
| `Data\installed-mods.json` | What this app installed, and every file it placed |
| `Data\mod_cache.json` | Cached catalog |
| `Data\addon_cache.json` | Cached addon catalog, with each addon's versions and download links |
| `Data\spt_versions.json` | Cached SPT release list (refetched daily) |
| `Data\dependency_flags.json` | Per-mod "has dependencies" answers, re-checked when a mod publishes |
| `Data\logs\tcfmm-<date>.log` | Daily log |
| `Data\config-backups\` | A copy of each config file taken before the Configs page overwrote it, one timestamped folder per save |
| `Data\LegacyConfigs\` | Config files kept from removed mods, one timestamped folder per removal |
| `Staging\` | Default destination for manually downloaded archives |
| `.tcfmm-update\` | Hidden. Only exists while a self-update is downloading or has failed; swept on the next launch |

### Logging

Info level by default. Drop an empty file named `verbose` (no extension) next to the exe to get
Debug-level output in the same log. Logs rotate daily as `tcfmm-<yyyyMMdd>.log`.

### Translating the app

**Options - Language** picks the language the app's own text is read in; **System default** follows
the display language Windows is set to. Five languages ship: English, German, French, Italian and
Russian.

**Four of those five are machine translations that no native speaker has checked.** They exist
because the alternative was English only until a volunteer translated 1,290 strings from nothing,
which was not going to happen. Each one says so in its own `Meta_TranslationCredit`, shown under the
language picker, and that line is what a human translator replaces with their name.

Mod names, descriptions, changelogs and category names come from sp-mod and stay in whatever
language their author wrote them in, so a translated app still shows an English catalog.

**Reporting a bad line** is the contribution that matters and needs no tooling: open an issue with
the language, what it says now, and what it should say. One line is worth reporting on its own.

**Adding a language.** No account, no build environment - one file out of this repo and one free
editor:

1. Take `src\TCFModManager.App\Localization\Strings.resx`.
2. Open it in [ResXResourceManager](https://github.com/dotnet/ResXResourceManager/releases) - the
   standalone build, not the Visual Studio extension. It shows every language side by side in a
   grid, so the English and the four existing translations are all readable while filling in a new
   column.
3. Add a column for the new language and fill it in. Most entries carry a comment saying what the
   string is for and what each `{0}` is replaced with.
4. Send the `Strings.<tag>.resx` it generates back - a pull request, or the file attached to an
   issue.

Anything in braces stays exactly as it is, including the part after a colon in `{0:N0}`, which is
what formats a number for the reader's region. The words either side can move anywhere the language
needs them.

A translation never has to be complete. An untranslated key falls back to its English value, so a
partial language is a part-English app rather than a blank one, and a release is never held up
waiting for one. Every build prints each shipped language's coverage - `ru: 1220/1220, complete` -
so drift after a release that adds strings is visible rather than silent. Coverage is counted in
translatable units rather than raw keys, because a plural family is one thing to translate and the
number of forms it needs is a property of the language.

Two entries are not translations. `Meta_LanguageName` is the language's name written in itself, the
way the dropdown should list it (`Deutsch`, not `German`). `Meta_TranslationCredit` is the
translator's own line, shown under the language picker in Options and hidden when empty.

Counted sentences are split into one entry per plural form. English and German need two (`_one`,
`_other`), French two but with 0 taking the singular, Russian three (`_one`, `_few`, `_many`); the
key schema reserves all six CLDR categories, so a language needing more is a resx change plus one
rule in `Core\Services\PluralRules.cs` - no call site moves.

## Known limitations

**What it can see**

- Mods nested a level deeper than convention (`BepInEx\plugins\Author\ModName\`) are listed under
  the outer folder's name, with an unknown version.
- Mods installed by hand are matched to a catalog listing by folder name, since there's no record to
  read. An ambiguous folder name is deliberately left unmatched rather than guessed at, so such a
  mod can be listed and removed but not updated from here.
- Most addons install *into their parent mod's folder* rather than one of their own - a SAIN preset
  lands in `BepInEx\plugins\SAIN`. One of those has no card of its own on the Installed page,
  because on disk there is only the parent's folder; it is listed, updated and redownloaded from the
  Addons section of the parent mod's dialog instead. A mod list still records it, and can install or
  update it - but nothing can disable one on its own, since there is no folder to move. Disabling
  the parent takes it with it.
- An addon installed by hand is not recognised as an addon. Addons carry no GUID on sp-mod.com and
  their ids belong to a different sequence from mods', so there is nothing to match one by; only an
  addon this app installed is tracked as one. A hand-installed addon shows as an ordinary unmatched
  card, the same as any hand-installed mod that matches nothing.
- An addon is only offered where its parent mod is installed and its version fits. Installing an
  addon does not pull its parent mod in - that would turn one click into a several-hundred-megabyte
  download without warning. Its own mod dependencies *are* resolved and offered, exactly as a mod's
  are. Applying a list refuses any addon whose parent mod is neither installed nor on that list,
  rather than downloading files nothing would load.
- A mod list containing an addon is exported at share-file format 2, which an app older than addon
  support refuses to import. A list with no addons in it is still written at format 1 and stays
  shareable with those versions.

**Installing**

- Archives must be packaged conventionally - a `BepInEx\`, `user\`, `SPT\` or `SPT_Runtime\` folder
  at the top, optionally inside one wrapper folder. Anything else is refused rather than scattered
  into the install.
- Everything inside the archive is installed, including readmes and optional-variant folders;
  choose-your-variant mods are better installed by hand.
- Files are overwritten with no backup, and uninstall deletes the files the record lists - a file
  shared by two mods goes with whichever is removed first.
- Installing needs roughly twice the archive's size free on the SPT drive, and downloads don't
  resume, so a large mod on a slow connection can time out and need restarting.

**Versions**

- Compatibility is judged from the most recent versions the catalog returns per mod, not the full
  history, so a mod with an older release that would work on this SPT still reads as incompatible.
- Constraint forms the range parser can't read fall back to "SPT version unknown" and are left
  unfiltered rather than hidden.
- Pre-release/beta version numbers aren't compared precisely, so an update may not be flagged
  against one.
- The catalog is filtered to SPT 3.10 and newer.
- Installed dates come from folder creation time, so a mod updated in place keeps its original date.

**Disabling**

- Group membership is keyed on the mod's folder name, so renaming a folder drops it out of its
  group - and out of anything you then disable by group.
- Copies set aside by "Sort out" stay in `.tcfmm-duplicates` in your SPT install until you delete
  them; nothing prunes that folder.
- Disabling doesn't reorder anything: a server mod's `loadBefore`/`loadAfter` ordering relative to
  the mods still enabled is left to SPT.

**Scope**

- The install manifest belongs to the app rather than to the SPT install it points at, so pointing
  Options at a second install carries the first one's records across. Use a separate copy of the app
  per install.
- The catalog refreshes once per session in the background; Refresh cache forces it.

---

# Developer notes

## Layout

- `src/TCFModManager.Core` sp-mod API client, models, and all the non-UI services (install,
 download, extraction, scanning, version matching, caching, logging). Plain `net9.0`, no UI
 dependencies, so it's reusable from tests, a console tool, or a future TCFModSync integration.
 One package reference: SharpCompress.
- `src/TCFModManager.App` the WPF shell (`net9.0-windows10.0.17763.0`, [WPF-UI](https://www.nuget.org/packages/WPF-UI)
 4.3.0 for Fluent Design, MVVM via CommunityToolkit.Mvvm, Microsoft.Toolkit.Uwp.Notifications for update notifications). Six pages: Browse, Installed,
 Dependencies, Downloads, App update, Options.
- `Tests/TCFModManager.Core.Tests` xunit tests over the API client, version matching,
 dependency status, and the installed-mod scanner, using JSON fixtures captured from live
 sp-mod.com responses.

Service wiring is a static `AppServices` holder rather than a DI container.

## sp-mod API

Base URL `https://sp-mod.com`, read-only and unauthenticated. Documented at
https://sp-mod.com/docs/index.html, OpenAPI spec at https://sp-mod.com/docs/openapi.yaml.

`SpModApiClient` (namespace `TCFModManager.Core.SpModApi`) covers the documented surface: mods,
mod versions, update and dependency resolution, version file trees, addons and their
versions/dependencies, categories, and SPT versions. Rate limiting is enforced at Cloudflare's edge
(documented as 40 requests/10s burst, 200/60s sustained); a 429 surfaces as
`SpModApiRateLimitedException` carrying the `Retry-After` value, and other failures as
`SpModApiException` with the status code and error code.

**The docs' generated examples don't always match live responses.** Confirmed by probing the real
endpoints while building this the models and tests are built against the live shape:

- `License` returns only `id`, `hub_id`, `name`, `link`, `created_at`, `updated_at`. The documented
 `short_name` doesn't exist.
- The embedded `Category` on a mod (`include=category`) uses `title`, not `name`, and has no
 `color_class`.
- `/mods` accepts `include=category,versions`, but `include=dependencies` and the nested
 `include=versions.dependencies` both return **HTTP 400** the list endpoint can't carry
 dependency data at all. Dependency info is only reachable per-mod via
 `GET /mod/{id}/versions?include=dependencies`, which is why Browse's dependency badge needs its
 own lookup and disk cache.
- The version objects embedded by `include=versions` on `/mods` are fuller than the docs suggest:
 they also carry `description`, `link`, `content_length` and `fika_compatibility`. The same is true
 of `/addons` - which is what lets an addon be installed straight from the cached catalog with no
 per-addon lookup.
- Addons are their own resource (`/addons`, `/addon/{id}`, `/addon/{id}/versions`,
 `/addons/dependencies`) with their own id sequence, no GUID field, and
 `mod_version_constraint` in place of `spt_version_constraint`. `/addons/dependencies` resolves an
 addon's required **mods** and does not include its parent.

Mod version constraints are resolved against the live SPT release list rather than parsed as
semver ranges, so the UI names releases that actually exist instead of the boundary versions the
constraints are written in terms of.

An SPT constraint and an addon's `mod_version_constraint` share a parser (`SptVersionRange`) but not
a policy. `SptVersionMatcher` deliberately ignores a constraint's patch-level floor, because SPT
does not break mod compatibility within a release line; `ModVersionMatcher` reads the same syntax
literally, because one mod's API against another absolutely does break between patches.

## Building

Needs the .NET 9 SDK, and Windows for `TCFModManager.App` (WPF doesn't build on Linux/macOS).
Open `TCFModManager.sln`, or:

```
dotnet build TCFModManager.sln
dotnet test Tests/TCFModManager.Core.Tests
```

`build/` holds the shared MSBuild config `Directory.Build.props` (the root one is a stub that
imports it, so MSBuild's directory walk-up still finds it) and `NuGet.config` plus two scripts
that aren't tracked in git (`*.ps1` is gitignored, same convention as TCFModSync):

- `build\deploy.ps1` forces a full rebuild and launches the exe, for a fast local test cycle.
- `build\package-release.ps1 -Version 1.4.0-beta` sets the release version (see below), publishes a
 self-contained single-file win-x64 build, zips it (plus a source zip via `git archive`) into
 `dist\`, and deploys that build into `<SptRoot>\TCFModManager\`. The SPT path comes from
 `-SptPath`, otherwise from Options' saved `settings.json`; if neither resolves, deploy is skipped
 and packaging still succeeds. Pass `-SkipDeploy` to always skip it.

### Versioning

**`-Version` on `package-release.ps1` is how a release is versioned.** The script writes it into
`<Version>` in `build\Directory.Build.props` before publishing, so the exe compiles that number in
and the zip is labelled to match. Omit `-Version` and whatever the props file already says is used
unchanged, which is how you repackage without bumping.

```
.\build\package-release.ps1 -Version 1.4.0-beta
```

It expects `major.minor.patch` with an optional pre-release suffix, and refuses anything else -
`1.4`, `1.4.0.5` and `not-a-version` all fail with a message rather than producing a broken release.
A leading `v` is dropped (MSBuild can't derive `AssemblyVersion` from `v1.4.0`). The rewrite is a
targeted text edit, so the props file's comments and formatting survive.

`build\Directory.Build.props` remains the single place the version *lives* - nothing else in the
tree carries one. MSBuild copies `<Version>` into `AssemblyInformationalVersion`, the only one of
the three generated version attributes that keeps a pre-release suffix, which is why `AppVersion`
reads that one rather than `AssemblyVersion` (which drops `-beta` and reports `1.3.0.0`).
`AppVersion.Current` is what the title bar shows, what `SpModApiOptions.UserAgent` is built from,
and what the self-updater compares against sp-mod.com - so a stale value makes every user's copy
either offer an update it already has or never offer one at all.

The bump is left uncommitted for you to review. The script points it out when it warns about
uncommitted changes, since until it's committed the source zip beside the release still carries the
old version.

The published exe is ~135MB, almost entirely the bundled .NET 9 runtime and WPF framework
assemblies rather than app code. `SatelliteResourceLanguages` is already set to trim non-English
resources. `InvariantGlobalization` is **not** usable here WPF's XAML binding pipeline calls
`CultureInfo.GetCultureInfo("en-US")` on startup and invariant mode throws.

### Editor showing false `InitializeComponent` errors

`deploy.ps1` always builds Release. On a fresh clone that means the generated
`obj\Release\...\Views\*.g.cs` partials exist but the Debug equivalents don't, and the C# language
service analyzes Debug by default so `*.xaml.cs` files light up with "`InitializeComponent` does
not exist" / "`x:Name` does not exist" until you build Debug once:

```
dotnet build src\TCFModManager.App\TCFModManager.App.csproj -c Debug
```

May need a language-server restart to pick up. The Release build was never broken; it's purely
tooling looking at the wrong configuration's output.

## Known gaps

- No DI container. `AppServices` is a static holder; worth swapping for
 `Microsoft.Extensions.DependencyInjection` plus WPF-UI's `INavigationService`/`IPageService` if
 the page count grows.
- `build\Directory.Build.props` still declares `<Version>0.1.0</Version>` while releases are cut at
 0.3.x, and the API client's default User-Agent is still `TCFModManager/0.1`.
