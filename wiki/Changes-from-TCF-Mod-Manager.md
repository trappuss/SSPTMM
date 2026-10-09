# Changes from TCF Mod Manager

SSPTMM began as a fork of [TCF Mod Manager](https://sp-mod.com/mod/2945/tcf-mod-manager) by
TheCrimsonFckr (MIT), version **1.19.0-beta**. This page lists everything SSPTMM adds, changes or
leaves out compared with that release, up to SSPTMM 1.2.0. Release by release, the same changes are
in the [changelog](https://github.com/trappuss/SSPTMM/blob/main/CHANGELOG.md).

Apart from the few things under [Different or left out](#different-or-left-out), everything TCF Mod
Manager 1.19.0-beta does is still here, under Steam's names:

| TCF Mod Manager | SSPTMM |
|---|---|
| Install | **Subscribe** |
| Remove | **Unsubscribe** |
| Installed | **Subscribed items** |
| Mod lists | **Collections** |
| App update | **About** |

![Workshop Home](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/02b-workshop-home.png)

## Look and navigation

- Steam Workshop look throughout: Steam's colours, layout, grid background, buttons, menus, dialogs
  and tooltips.
- Six tabs across the top, as Steam lays them out: Play, Workshop, Subscribed items, Collections,
  **Tools ▾** and **Help ▾**. Options is a gear, and an sp-mod.com button sits where Steam's Store
  Page button is.
- A downloads bar along the bottom shows what is downloading or installing; click it for Downloads.
- Right-click any mod, anywhere: Open, Quick View, Subscribe/Unsubscribe, Update, Download only, Add
  to Collection, Copy link, View mod page, the author's page and Follow.
- Item, author and collection pages open over the page you came from. Esc, the mouse back button or
  Alt+Left steps back through them.
- Smooth scrolling (Options can switch it off). Options can also hide the Subscribed items and
  Collections tabs, or put your own picture behind the window, with a slider to darken it.
- Pages show one line of explanation; a small "?" at its end holds the rest.

## Workshop

- **Workshop Home**: a banner, the past week's most downloaded new mods, From Followed Authors, list
  tabs (Top Rated, Most Subscribed, Last Updated, New) and content types with counts.
- **Browse** with Steam's sidebar: SPT version rows, Content Type, Special Filters (Featured, Created
  by Followed) and tag boxes (Fika compatible, Has dependencies, Has addons, hide Contains ads, hide
  Subscribed).
- Steam's sort orders, mapped to what sp-mod.com counts, search options (Title & Description, Title
  Only, Description Only) and **Filter by Date**.
- Each active filter shows as a chip; click it to remove that filter.
- **Per page** of 10, 15, 30, 50 or **Infinite** (more loads as you scroll), remembered per page.
- **Hover popups** with a slideshow of the mod's pictures, and **Quick View** with Subscribe and Add
  to Collection; the arrow keys step through the list.
- Badges on every card for installed, update available and disabled.
- **Item page** with Description, Comments, Change Notes and Versions tabs, a strip of pictures and
  videos, a full-size picture viewer and YouTube videos in the app. Descriptions show exactly as
  written on sp-mod.com, including tables, code, warnings and animated GIFs.
- Required Items (ticked when installed), Required By and More By.
- sp-mod.com's own details on the item page: the multiplayer-cheat warning, the "may change your
  profile" notice, VirusTotal scans, licence, plugin GUID, source links, Passed Verification and the
  files in a download.
- A **Versions** tab on the item page: each version's SPT range, size, downloads, Fika status and
  requirements, with **Install this version** or **Switch to this version**. (TCF Mod Manager lists
  versions and installs older ones from its details view.)
- **Comments**: sp-mod.com's comments, inside the app. Sign in once to reply or react.
- **Authors' pages** (Workshop Items, Collections, Addons, followers) and **Follow authors**.

![Item page](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/04-item.png)

## Subscribing and downloads

- Subscribe finds what a mod needs first, then asks once in Steam's **Additional Required Items**
  dialog: Just This Item, Subscribe to All, or Cancel.
- Subscribing from a mod's page doesn't ask you to open its page again. Elsewhere the "read the mod
  page first" window shows the page inside the app, and an update's change notes.
- **Up to three downloads at once**; installs still run one at a time, in order. Passing failures are
  retried and stalled downloads resume where the server allows it.
- Downloads are kept (up to 4 GB), so reinstalling the same version doesn't download it again.
- **Download only** as a button beside Subscribe and in the right-click menu (TCF Mod Manager has it
  as a mode and a small button beside Install).
- **Install from file** for .zip, .7z, .rar, .tar and .tar.gz, or drop archives onto Subscribed items.
  Your file is copied, never moved.
- **File clashes**: files already on disk that belong to another mod, or to nothing the app
  installed, are listed before an install, which waits for **Install Anyway**.
- Before an install you're told about required mods that are disabled, too new, the wrong version, in
  conflict or missing for your SPT, and about versions marked not for Fika when you run Fika.
- **Unsubscribe from all** on a collection, a Steam-style Unsubscribe question with **Don't ask
  again**, and a warning when installed mods still use the one you remove.

## Subscribed items

![Subscribed items](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/07-subscribed-cards.png)

- One toolbar (search, sort, Filters, views, **⋯**), and **Group by** inside the Cards and List views:
  your own groups, sp-mod.com category or enabled/disabled, with dragging between your groups in
  every view. (TCF Mod Manager has the Cards, List and Groups views.)
- **Opens the way you left it**: filters, sort, grouping and view are remembered.
- **Presets** of which mods are on and which are off, as Mod Organizer 2's profiles keep them, with
  **Disable all mods** and **Enable all mods** for troubleshooting. Applying one names every change
  first, warns about missing requirements, backs up your profiles, and can be undone or put back.
- **Pick several mods** as Steam's library does (Ctrl+click, Shift+click, Ctrl+A), with Unsubscribe
  in the selection bar alongside Update, Enable and Disable.
- An on/off switch and a blue **Update** button on each mod, and **Update all**.
- **Held-back updates**: an update that would break another installed mod is flagged, left out of
  Update all and never announced by update notifications.
- A mod that doesn't run on your SPT names the newest version that does, and a mod that needs a
  missing or disabled mod is flagged, offline and for hand-installed mods too.
- Pinned mods sort to the top with a **PINNED** tag; a **RE-UPLOADED** tag with **Get it again**; a
  **Recently installed** sort; and a **profile-change warning** first when removing a mod sp-mod.com
  says may change your profile.
- **SPT upgrade check**: pick a newer SPT and see which mods are ready, need an update, or have
  nothing yet.

## Collections

![Your collections](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/08-your-collections.png)

- **Public collections from sp-mod.com**: browse, search (also by who made them), filter by SPT
  version and sort six ways.
- A collection's page shows each item's note from its author, the version each mod gets on your SPT,
  and how much of it you already have.
- **Subscribe to all** at the newest version for your SPT, or for the collection's SPT, with **Add
  Only** or **Overwrite My Subscriptions** shown before anything changes. **Update all (N)**.
- **Favorite** public collections; Favorites marks the ones that changed since you last looked.
- **Your collections** (TCF Mod Manager's mod lists) as a grid of cards, with **Add from code**,
  **Follow a shared file**, **Add to Collection** and **Save to Collection** added to saving what's
  installed and importing list files.
- **Share with friends** by share code (one line for Discord) or a shared folder kept up to date,
  with a notice saying what changed and offering Sync or Later.

## Play

- **Stop server** (red), and the server **without its window**, its log live on the Play page.
- **Close game**, and an option to close the game when the server stops. **Stop server** then closes
  the game first, while the server can still answer it, so the game quits cleanly.
- **Mod tools**: every .exe an installed mod put in your SPT folder (SVM's Greed, give-ui...), with
  Open, its folder and Hide.
- "Running" means running from this SPT install; another SPT copy elsewhere doesn't block it.
- **Start the game from SSPTMM (on by default).** A profile list and a **PLAY** button
  that start the server and the game with no SPT launcher window, for SPT 4.1.3 to 4.1.6. The SPT
  team didn't make it and doesn't support it.

## Tools

- **Diagnose logs** (new): reads the server, BepInEx and game logs without changing anything, says in
  plain words what went wrong and, when the log shows it, which mod it came from. **Copy a report for
  help** takes out your user name, your user folder, profile ids and IP addresses with a port.
- **Configs**: earlier versions of a config file can be brought back.

## Safety

- **SPT profile backups** before installs, updates, removals, disabling, collections and presets,
  whenever the profiles changed; the last 10 per install are kept.
- **Your BepInEx settings stay yours**: an archive's `.cfg` is only placed where none exists yet, or
  over the app's own untouched copy.
- Read-me files, licences and pictures in an archive are never dropped into SPT. Archives with
  `plugins\` or `patchers\` but no `BepInEx\` around them are placed correctly.
- Server prepatches in `user\patchers\` are installed, updated and removed with their mod; empty
  folders a mod ships are created and removed with it.
- **A check after every install and update**: each file on disk, server configs included, is
  compared with the archive, and the download card names anything missing or different.
- Files a mod tries to put in SPT's own user folders are left out and named in a warning. An install
  that would place nothing fails and changes nothing.

## Help and About

- **Help** rewritten for the Steam layout, with topics for collections, Mod tools and Diagnose logs.
  **Open the full guide** opens this wiki.
- **Getting started** on Workshop Home ticks off three steps from your actual install.
- **About** shows the version and credits, and points out a newer SSPTMM release on GitHub - only if
  you agree when it first asks. It never downloads or installs anything.
- **Report a problem** goes to this repository's Issues.

## Privacy

- No telemetry. Every server SSPTMM talks to, what it sends and every file it writes are listed in
  [Files and network](Files-and-Network).
- The `Referer` that sp-mod.com's picture server needs is sent only to sp-mod.com.
- Closing SSPTMM with update notifications off removes its Windows notification registration.

## Fixed compared with TCF Mod Manager 1.19.0-beta

- Server prepatches in `user\patchers` were never placed, updated or removed (with Skills Extended,
  raid results could be lost).
- SVM's Greed said "couldn't find Preset folder" (the empty `Presets` folder was never created).
- An update replaced your tuned BepInEx `.cfg` with the mod's defaults.
- A mod's required items weren't offered again after you removed them and subscribed again.
- A mod list entry with no version installed the oldest release, not the newest for your SPT.
- 1.2.0-beta to 1.2.0 didn't count as an update; "-hotfix", "-fix" and "-patch" now count as newer.
- Failed downloads and installs left nothing in the log.
- Install dates came from the folder's date, so a mod copied with its archive's dates could look a
  year old.
- With the exe in the SPT folder itself, every install, update and removal was refused. (Fixed in
  TCF Mod Manager 1.19.1 too; SSPTMM ported that fix.)

## Different or left out

- **Always dark.** TCF Mod Manager has light and dark themes (following Windows by default); SSPTMM
  keeps Steam's dark look.
- **No self-update.** TCF Mod Manager can download and install its own updates; SSPTMM only says a
  newer release is out, and you unzip it yourself. It never offers TCF Mod Manager's updates, since
  installing one would replace SSPTMM.
- **Save as default on Subscribed items is gone**: the page remembers how you left it instead.
- **"Hide mods with AI content" is gone**: sp-mod.com's API has no such field, so it never hid
  anything.
- **Its own settings.** SSPTMM keeps its own `Data` folder and doesn't read TCF Mod Manager's
  settings.
- **Newer TCF Mod Manager work isn't included.** SSPTMM no longer follows TCF Mod Manager's
  releases. From the releases since 1.19.0-beta it has taken only the install fix above; TCF Mod
  Manager's sp-mod.com list import and its page-loading changes are not in SSPTMM.
- **Translations**: Steam's terms use Steam's own German, French, Italian and Russian wording; text
  that is new in SSPTMM shows in English in those languages until it is translated.

## Kept the same on purpose

- The hidden folders inside an SPT install (`.tcfmm-removed`, `.tcfmm-work`, `.tcfmm-duplicates`),
  so both apps can work on the same install and Undo still finds what it set aside.
- Mod list files: each app opens the other's.
- The Server Map: SSPTMM speaks the same protocol, so it works with TheCrimsonFckr's Server Map mod
  and with TCF Mod Manager on the same server.
