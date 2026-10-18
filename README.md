![SSPTMM - Steamified SPT Mod Manager](docs/images/ssptmm-banner.png)

# SSPTMM - Steamified SPT Mod Manager

A Windows app for finding, installing and keeping track of [SPT](https://sp-tarkov.com) (Single
Player Tarkov) mods, laid out and styled like Steam's Workshop. Mods and collections come from
[sp-mod.com](https://sp-mod.com); nothing needs an account or an API key.

![Browse](docs/images/03c-browse-hover.png)

**[Download the latest release](https://github.com/trappuss/SSPTMM/releases/latest)** ·
**[Wiki](https://github.com/trappuss/SSPTMM/wiki)** ·
**[Changelog](CHANGELOG.md)** ·
**[Report a problem](https://github.com/trappuss/SSPTMM/issues)**

## What it does

- **Workshop** - Home, Browse with Steam's filters and sorts, hover popups and Quick View, authors'
  pages, and an item page with the description exactly as its author wrote it, change notes,
  versions and comments.
- **Subscribe** - installs a mod and what it needs, up to three downloads at once. Unsubscribe
  removes it, with Undo for a while afterwards. Download only, and Install from file for an archive
  you already have.
- **Subscribed items** - everything in your SPT install as cards, a list or your own groups: update
  all (libraries first), enable and disable, presets of which mods are on and off (and Disable all
  for troubleshooting), pin, spot re-uploads, and a warning before removing a mod that changes your
  profile. Right-click a mod to rename it, add a note, skip an update, or go back to the version an
  update replaced; with several picked, the menu acts on them all.
- **Collections** - public collections from sp-mod.com, your own collections, and sharing them with
  friends by code or shared folder.
- **Play** - start the server and the game, read the server log, close the game, and open the tools
  your mods installed (SVM's Greed, give-ui...). Before you launch it warns about conflicts and about
  mods that need something missing or switched off. Pick a profile and press PLAY - server and game
  start with no SPT launcher window (SPT 4.1.3 and later in 4.1; it can be switched off in Options).
- **Tools** - Configs, Dependencies and Conflicts, and **Diagnose logs**, which reads SPT's logs and
  says what went wrong and which mod it came from.
- **Safety** - a copy of your SPT profiles before anything changes the install, your BepInEx
  settings kept, SPT's own files protected, every install checked file by file, and an install cut
  off part-way (a crash, the power going) put on record so it can be finished or removed.
- **Updates itself** - Help > About says when a new SSPTMM is out, and **Install update** puts it
  in place for you.

| | |
|---|---|
| ![Workshop Home](docs/images/02b-workshop-home.png) | ![Item page](docs/images/04-item.png) |
| ![Subscribed items](docs/images/07-subscribed-cards.png) | ![Collection](docs/images/06b-collection-items.png) |
| ![Your collections](docs/images/08-your-collections.png) | ![Diagnose logs](docs/images/11-diagnose.png) |

More screenshots of every page are in the [wiki](https://github.com/trappuss/SSPTMM/wiki).

## Should you use a mod manager?

SPT mods are folders and files: you can install every one of them by hand, and plenty of people
do. A manager is worth it when you run more than a handful of mods, update them often, or play with
friends who need the same set. Here is the honest version of both sides.

### Why use one

- **Removing a mod cleanly.** One mod can put files in `BepInEx\plugins`, `BepInEx\patchers`,
  `BepInEx\config`, `user\mods`, `user\patchers` and the game folder itself. By hand you have to
  remember all of them; SSPTMM records every file it places and removes exactly those.
- **Updating without losing your settings.** Extracting a new version over the old one replaces the
  config files you tuned. SSPTMM keeps your changes (see below).
- **Dependencies and versions.** Many mods need a library or another mod at a certain version, and
  not every version runs on your SPT. SSPTMM installs what a mod needs, updates libraries before the
  mods that use them, and warns before you launch when something is missing or switched off.
- **Troubleshooting.** Switching a mod off (or all of them, with Disable all) and back on is one
  click, instead of moving folders around and remembering where they came from.
- **Playing together.** With Fika, everyone needs the same mods. A shared collection keeps a group
  in step, and says exactly what changed when someone adds or removes one.
- **Your profile.** Some mods write into your profile and can leave it unloadable when removed.
  SSPTMM backs your profiles up before every change and warns before removing one of those mods.

### Why not, or what to keep in mind

- **It is one more program between you and your files.** When something breaks, whoever helps you
  will still want your logs and what is in `BepInEx\plugins` and `user\mods`. It pays to know where
  your mods actually are; Diagnose logs and Copy a report for help make that part easier, not
  unnecessary.
- **It only knows what the archive and sp-mod.com tell it.** Authors pack mods in different ways,
  the SPT versions on sp-mod.com are set by hand and are sometimes wrong, and some mods need a step
  no manager can take for you: a config edit to make two mods agree, a file the read-me says to
  delete. Read a mod's page before you subscribe.
- **There is no virtual file system.** Unlike Mod Organizer 2, SSPTMM installs into your real SPT
  folder. That is why everything keeps working without it, but it also means two mods that ship the
  same file really do collide: SSPTMM stops and asks (Install Anyway) rather than picking one.
- **Browsing, subscribing and update checks need sp-mod.com.** Offline, your installed mods still
  list and switch on and off, but there is nothing new to fetch. Options > Advanced can point
  SSPTMM at a mirror when the site is down.
- **For three mods updated twice a year, you may not need any of this.**
- Windows 10 and 11 only.

### How SSPTMM handles the awkward cases

- **Mods you installed by hand carry over.** There is no import step. Subscribed items reads every
  mod in the folder - client mods from the plugin their DLL declares, SPT 4 server mods from the
  details in their DLL (`package.json` on SPT 3.x) - and matches them to sp-mod.com. They get update
  checks, dependency warnings, switching on and off, groups and presets like anything SSPTMM
  installed. The first time one is updated, the config files it already has are copied aside
  before the new version goes in, even though SSPTMM never recorded them.
- **Nothing locks you in.** Mods go into the same folders SPT always loads from, and SSPTMM's own
  records stay in its `Data` folder. Stop using it and your game runs as it did. A switched-off mod
  is only moved to the folder's `.disabled` twin (`user\mods.disabled`, `BepInEx\plugins.disabled`),
  so it can be moved back by hand too.
- **SVM (Server Value Modifier) works out of the box.** SVM puts its editor, Greed, in the SPT
  folder, keeps the presets Greed writes in its `Presets` folder, and remembers the one in use in
  `Loader\loader.json`. SSPTMM knows all three:
  - the Play page lists **Greed** with an Open button, and says to use it with the server stopped;
  - updates never touch your presets, and removing SVM keeps them;
  - the preset you picked survives an update, instead of quietly going back to the default as it
    does when you extract a new version over the old one;
  - the empty `Presets` folder in SVM's archive is created, so Greed finds it the first time.

  Any other mod that keeps its files somewhere unusual can be set up the same way in Tools >
  Configs (**Files I authored...**, **Settings kept elsewhere...**).
- **Config changes carry into updates.** A server mod's JSON is merged by default - your changes
  stay, settings the new version adds come in - or, per mod, kept as it is or replaced. A BepInEx
  `.cfg` you have changed is never overwritten, and every config is backed up before the editor in
  Tools > Configs saves it.
- **Mods that change your profile** (SVM, Skills Extended and others sp-mod.com marks): a profile
  backup before every change, the warning before removal asked even with Don't ask again ticked, and
  Skills Extended's server prepatch left in place when the mod is switched off, so the profile still
  loads. If one won't load anyway, Diagnose logs says so and can let SPT clean it.
- **Archives with an unusual layout** - a DLL and a read-me, a server mod's folder on its own - are
  shown to you with the question "client mod or server mod?", instead of being refused or guessed.
- **Two mods in one folder** (Realism's Stance Overhaul and Common Lib share one) get a card each.
- **Changes while SPT is running** are refused by default, since the game holds files open; an
  option asks instead, as Mod Organizer 2 does.
- **Second thoughts.** Unsubscribed mods are kept for a while for **Undo**, and an update can be
  gone back on from the mod's right-click menu.

## What SSPTMM adds over TCF Mod Manager

SSPTMM began as a fork of TCF Mod Manager, and everything it does is still here. On top of it:

### Your mods' tools, one click away

Some mods ship a program of their own, like **SVM's Greed** (the Server Value Modifier's editor) or
**give-ui**. After you subscribe, the **Play** page lists every program your mods put in the SPT
folder. **Open** starts it in the right folder, and the folder button opens the mod's own folder
(for SVM, its presets). SSPTMM also knows when a tool wants the server stopped (Greed) or running
(give-ui), and says so. If you disable the mod, its tool is greyed out.

![Mod tools on the Play page, with SVM's Greed](docs/images/17-play-mod-tools.png)

### Collections you can share

- **Browse sp-mod.com's public collections** inside the app.
- **Subscribe to all** of a collection, at the versions for your SPT or the collection's.
- **Make your own** from the mods you have installed.
- **Share with friends in two ways:**
  - a **share code**: one line of text that fits in a Discord message;
  - a **shared folder** (Dropbox, OneDrive, Google Drive), which keeps everyone's copy up to date.
- **When a friend's collection changes,** SSPTMM says exactly what was added or removed and offers
  **Sync**. Only the list travels; every mod still downloads from sp-mod.com.
- **Friends can suggest mods for yours.** They save a copy of your collection, add mods and send
  you its code. SSPTMM lists the mods their copy adds, and you tick the ones that join yours.

![Sharing a collection by code or shared folder](docs/images/18-share-collection.png)

### Diagnose logs

Reads SPT's server log, BepInEx's log and the game's error log, and says in plain words what went
wrong. When the log shows it, it also says which mod caused it. Examples:

- a mod that didn't load, and what it was missing;
- two mods that don't work together;
- raid results that weren't saved;
- a profile that won't load.

**Copy a report for help** takes out your user name, user folder, profile ids and players' IP
addresses.

### A Steam Workshop to browse

- **Browsing:** hover previews with each mod's pictures, Quick View, authors' pages, and following
  authors.
- **Item pages:** sp-mod.com's comments, change notes for every version, and YouTube videos, all
  inside the app.
- **Badges:** every card shows whether you have the mod, it needs an update, or it's disabled.

### Safer installs

- **Profile backups:** a copy of your SPT profiles before anything changes your install.
- **Held-back updates:** an update that would break another installed mod is held back - left out
  of Update all and never announced.
- **Interrupted installs:** an install cut off part-way is put on record at the next start, so
  it can be finished or cleanly removed.
- **Re-upload detection:** spots a mod re-uploaded under the same version number.
- **SPT upgrade check:** shows which of your mods are ready for a newer SPT.
- **Install from file:** installs an archive you already have.

### More control from the Play page

- **Warnings before you launch:** conflicts, and mods that need something missing or switched off.
- **Server:** stop it, run it without its window, and read its log live.
- **Game:** close it, optionally whenever the server stops.
- **Play:** start the game straight from SSPTMM with no SPT launcher window (on by default).

Everything that differs, item by item, is in the wiki:
[Changes from TCF Mod Manager](https://github.com/trappuss/SSPTMM/wiki/Changes-from-TCF-Mod-Manager).

## Install

1. Download `SSPTMM-<version>-win-x64.zip` from
   [Releases](https://github.com/trappuss/SSPTMM/releases/latest).
2. Unzip it into your SPT folder (the one with `EscapeFromTarkov.exe`). The zip holds one folder,
   so you get `SPT\SSPTMM\`. Anywhere else works too - just never inside `BepInEx\plugins` or
   `user\mods`.
3. Run `SSPTMM\SSPTMM.exe`.
4. Unzipped into your SPT folder, SSPTMM finds that install by itself - the window title shows your
   SPT version. Anywhere else, open **Options** (the gear, top right), set **SPT Install Folder** to
   the folder with `EscapeFromTarkov.exe` in it, and press **Save**.

### Updating

From 1.3.0, **Help > About** shows when a newer release is out and has an **Install update**
button: it asks first, downloads the release from this repository, closes SSPTMM, puts the new
version over its folder and starts it again. Your settings in `Data` stay. Or update by hand: close
SSPTMM and unzip the new release to the same place. Coming from 1.2.x, update by hand once.

Windows 10 or 11. The download is self-contained: no .NET install needed. Settings, caches and logs
live in a `Data` folder beside the exe, so SSPTMM never touches another mod manager or another copy of itself.
Made and tested for SPT 4.0 and 4.1.

## Credits

SSPTMM began as a fork of **[TCF Mod Manager](https://sp-mod.com/mod/2945/tcf-mod-manager) by
TheCrimsonFckr** (MIT), whose mod handling, installing and removing it was built on. It is now
developed on its own, with its own look, names, settings and version numbers. Yes I reached out and got permission before uploading.

Code goes both ways: TCF Mod Manager has since taken several SSPTMM features (profile backups, the
download queue, kept BepInEx settings and more), and SSPTMM takes fixes back from TCF Mod Manager's
later releases, credited in [CHANGELOG.md](CHANGELOG.md) and in the code.

Everything it adds, changes or leaves out compared with TCF Mod Manager 1.19.0-beta, the release it
was based on, is in
[Changes from TCF Mod Manager](https://github.com/trappuss/SSPTMM/wiki/Changes-from-TCF-Mod-Manager).

Problems with SSPTMM belong in this repository's
[Issues](https://github.com/trappuss/SSPTMM/issues). Diagnose logs' **Copy a report for help** gives
you a report to paste there, with your user name, user folder, profile ids and IP addresses taken
out.

## Building from source

Two files do everything:

- **`SSPTMM-build-and-run.bat`** - installs the .NET 9 SDK into the folder if the PC has none,
  builds, runs the tests, publishes a self-contained `SSPTMM.exe` into `dist\SSPTMM\` and starts it.
  When there is a `steam-workshop-ui.bundle` in `Claude outputs\`, it brings that update in first.
- **`SSPTMM-upload-to-github.bat`** - puts the code on GitHub. When the version in
  `build\Directory.Build.props` is new, it also makes the release: builds and tests it, zips it and
  publishes it with its CHANGELOG section. It asks before sending anything.

`tools\SSPTMM-test-checklist.bat` walks through testing a release by hand on a fresh SPT install and
writes a report to `test-reports\`. See
[Building from source](https://github.com/trappuss/SSPTMM/wiki/Building-from-Source) in the wiki.

## More

- [CHANGELOG.md](CHANGELOG.md) - what each release changed.
- [docs/steam-workshop-ui.md](docs/steam-workshop-ui.md) - how each Steam page was matched, what was
  measured, and every change round by round.
- [docs/server-map-guide.md](docs/server-map-guide.md) - setting up the Server Map mod on a server.
- [Files and network](https://github.com/trappuss/SSPTMM/wiki/Files-and-Network) - every file SSPTMM
  keeps and every server it talks to.
- [docs/branding](docs/branding) - the icon, banners, mascot and sp-mod.com thumbnail, in every size.

## License

MIT - see [LICENSE](LICENSE), which also keeps TCF Mod Manager's MIT copyright notice. The libraries
and runtime in the release build are listed with their licences in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Steam and the Steam Workshop are trademarks of Valve Corporation. SSPTMM is an independent fan
project, not affiliated with or endorsed by Valve, Battlestate Games, the SPT project or
sp-mod.com.
