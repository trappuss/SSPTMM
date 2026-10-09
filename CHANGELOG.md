# SSPTMM 1.4.0

## Updating

From 1.3.0 or 1.3.1: Help > About > **Install update**. From an older version: close SSPTMM, download `SSPTMM-1.4.0-win-x64.zip` and copy the contents of its `SSPTMM` folder over your SSPTMM folder. Your settings in `Data` stay as they are.

## New

- **Subscribed items looks like Steam's own lists.** A mod's card, List row and Groups row are now drawn the way Steam draws the rows of *Your Workshop Files*: a navy panel with a soft shadow and no outline, and behind it the mod's own picture, out of focus and fading to the right. Under the pointer a card lightens and its shadow turns white, as a Browse card's does. Before, the cards were see-through boxes that the page's grid ran through, with small grey text.
- **Easier to read.** The line under a mod's name (version, client or server, author) is larger and lighter. The tags under it - DISABLED, PINNED, RE-UPLOADED, the group and the collections - are one shape on one line, in line with the name; on a card, collections that don't fit are counted in a **+2** with their names in its tooltip, so closed cards are the same height (an addon's has one more line, saying what it is for).
- **Sort by Enabled first or Disabled first** on Subscribed items, each half by name. Pinned mods stay at the top, as in every order.
- **Options looks like Steam's settings.** The sections are listed down the left - General, Play, Look, Pages, Installing mods, Updates, Fika and servers, Advanced - and a click goes to one. Each section is one solid panel of plain rows with a line between them, in a column narrow enough that a switch sits near what it switches; before, the rows were see-through boxes across the whole window. The settings are regrouped to match: starting the game has a section of its own (Play), and the tabs, background, pictures and scrolling are together under Look.
- **Play starts the game from SSPTMM by default.** *Start the game from SSPTMM* is no longer experimental: on a new setup, Play on the Play page starts the server and the game itself, with no SPT launcher window (SPT 4.1.3 to 4.1.6; on any other version Play opens the SPT launcher as before). If your settings already say off - 1.2.x wrote that for everybody - it stays off: switch it on in Options if you want it. A setup that never ran 1.2.x has nothing written and gets the new default. It is still not made or supported by the SPT team.
- **Options > Picture behind each subscribed mod** switches the pictures behind the cards off, for a PC that scrolls the page slowly with them.

## Faster

- **Installing and updating.** Checking a mod's files used about a megabyte of memory per file however small the file was - 307 MB for a mod of 300 small files, now under 1 MB - and unzipping did the same. An update no longer re-reads every file of the old version that it is about to replace anyway, and removing a mod with thousands of files no longer re-checks the same folders for each one.
- **Workshop Home** read the collections file once per card, forty times a visit; it now reads it three times.
- **Typing in Browse's search**: the SPT-version check it runs on every mod for every letter typed takes a tenth of the time.
- **Mod pictures are downloaded once.** The Browse card, the popup over it and the item page each fetched the same picture again; the popup's picture no longer starts blank.
- **Starting up**: the saved catalog is read about three times faster.
- **Subscribed items**: the install records are read once per scan, not twice.

## Fixed

- **Smooth scrolling no longer stops for a moment as Subscribed items reaches more mods.** The page added 24 cards at once when you neared the end of what was loaded; it now adds them one at a time, further ahead.
- **A mod no longer shows the same collection twice** among its tags.
# SSPTMM 1.3.1

## Updating

From 1.3.0: Help > About > **Install update**. From an older version: close SSPTMM, download `SSPTMM-1.3.1-win-x64.zip` and copy the contents of its `SSPTMM` folder over your SSPTMM folder. Your settings in `Data` stay as they are.

## Fixed

- **The disable/enable warning keeps its buttons on screen.** With many mods picked, the list of names pushed the note and the buttons off the bottom of the window. The picked names now scroll in a box of their own, the list of affected mods takes the room that is left, and the buttons always show. A collection's replaced-versions list scrolls the same way, and no Steam dialog is ever taller than the screen.
- **Play no longer starts a profile SPT won't load.** When you remove a mod whose items or trader a profile still holds, SPT marks that profile invalid and the game stops on it at start - with **Wipe profile on next play** ticked too, because SPT never saves a wipe of a profile it won't load. Play now checks with the server first and says so, with what to do, instead of starting the game.
- **Diagnose names the item or trader.** For a profile SPT won't load, Diagnose now also says which item or trader from a removed mod is in it, explains that a wipe can't help, and names SPT's own fix: setting `removeModItemsFromProfile` and `removeInvalidTradersFromProfile` to true in `SPT\SPT_Data\configs\core.json` and restarting the server, so SPT removes what's missing from the profile itself.

# SSPTMM 1.3.0

## Updating from 1.2.0

Close SSPTMM and download `SSPTMM-1.3.0-win-x64.zip`. It holds a single folder, `SSPTMM`: copy its contents over your SSPTMM folder (whatever you named it). Your settings in the `Data` folder stay as they are. From 1.3.0 on, **Install update** on Help > About does this for you.

1.3.0 also carries 1.2.1, which was not released on its own - see its section below: the zip now unzips into a folder of its own (`SPT\SSPTMM\`), and SSPTMM's own sp-mod.com listing is never offered as a mod.

## New

- **Install update.** When a newer release is out, Help > About has an **Install update** button. It asks first, naming the version and its size; then SSPTMM downloads the release from GitHub, closes, puts the new version over its folder and starts again. Your settings, install records and backups in `Data` stay. Only files from SSPTMM's own GitHub releases are accepted. **Open the release page** is still there for updating by hand.
- **The Play page warns about mods that need something.** Next to the conflict warning, the Play page now names enabled mods that need a mod that isn't installed or is switched off - what Subscribed items flags on their cards - before you launch. Click it to go to Subscribed items. It never stops you from launching.
- **SPT is found by itself.** Unzipped into your SPT folder (`SPT\SSPTMM\`), SSPTMM sets the SPT install folder by itself on first start. Anywhere else, set it once in Options as before.

## Fixed

- **Settings no longer overwrite each other.** Two parts of SSPTMM saving settings at nearly the same time (one waiting on the network or a question in between) could undo the other's change. Each save now writes only what it changed.
- **An install cut off part-way is noticed.** If SSPTMM, Windows or the power stops in the middle of placing a mod's files, the next start puts that mod on record as incomplete - the files the install had changed or placed, and whatever is left of the previous version - so Unsubscribe can still remove them, and says which mod to reinstall.
- **The log file is written in order** when several things log at once.
- **Held-back updates are not announced.** The background update check now takes sp-mod.com's held-back list from the answer it already gets, so an update that would break another installed mod no longer raises a notification, and neither the item page nor the right-click menu offers Update for it (Update all already left it out). With a very long mod list the check is split, and then the held-back list from Subscribed items' own check is used.
- **Subscribed items no longer turns "Installed in the last 7 days" back on.** Sorting by Recently installed was read at the next start as that filter too (a leftover from when Recently installed moved between the two dropdowns), so the filter came back after every restart, however often it was cleared.
- **Every dialog is Steam's.** The windows that still had Windows' own title bar - the disable/enable warning, the SPT upgrade check, the Fika headless question, the downloads found by Monitor mode, a collection's replaced versions, adding mods to a collection, and Data files - are drawn as Steam's modal now, and the yes/no questions and notices that used Windows' message box are too. Questions that risk something (closing during an install, the Fika and dependency warnings) answer No to Enter.
- **A cut-off update keeps the right version label.** When an update was cut off before any of the new version's files were written, the mod stays on record at its previous version.
- **An addon on a collection gets a version that fits its parent.** A collection entry for an addon with no version named (or whose version is gone) now gets the newest version that works with the version its parent mod will be at, rather than the newest overall; when none fits, it is listed as not fetched, with the reason.
- **"Install anyway?" questions default to No.** The not-for-Fika and dependency-problem questions now answer No when you press Enter.
- **Dependencies page:** a mod whose version was read from its DLL (1.2.0.0) is no longer shown as behind the same release published with a label (1.2.0-hotfix).
- **Play page:** a Restart question for the server or headless client goes away when it stops by itself, instead of offering a Restart that then fails.
- **Configs:** the "SPT is running" note only counts SPT running from this install, not another copy.
- **Profile backups:** restoring when the SPT folder can't be used gives a translated message instead of an English error; the list follows a language change; and the copy taken before disabling or removing a hand-installed mod no longer holds up the window.
- **The log names every failed download,** including one with no download link, and records the full details of an unexpected error.
- The last unused trace of the old AI-content filter (which sp-mod.com refuses) is gone from the code; SSPTMM never sent it.

## From TCF Mod Manager

Several fixes above are ported from TCF Mod Manager's later releases (MIT), credited in the code: held-back updates in the background check (be28c66), the addon pick for collections (448af87, its NewestFittingVersion), the Play page's Restart question and the Configs running check (4315064, 99dd8f2), the log lines for failed downloads (673c324), the labelled-version comparison on the Dependencies page (5ec9304), the profile backup message, language refresh and off-thread disable backup (cdf20f9), the No default on the Fika question (83a8942), and the previous version's label in a cut-off update (d865b9f). The rest - the dependency-problem question's No default, the off-thread backup when removing a hand-installed mod - are SSPTMM's own.

## For developers

- **Two scripts in the repository root.** `SSPTMM-build-and-run.bat` brings in an update bundle when there is one, then builds, tests and runs; `SSPTMM-upload-to-github.bat` uploads the code and wiki, and makes the release when the version is new. The release, update and test scripts they replace are gone; the manual test checklist is in `tools\`.
- GitHub Actions builds and tests every push and pull request. On a `v*` tag it also publishes the release zip as a build artifact with its SHA-256 (it does not publish the release itself).

# SSPTMM 1.2.1

## Updating from 1.2.0

Close SSPTMM. The 1.2.1 zip holds a single folder, `SSPTMM`: copy its contents over your SSPTMM folder (whatever you named it). Your settings in the `Data` folder stay as they are. From now on, unzipping a new release to the same place puts its `SSPTMM` folder over yours.

## Changed

- **The download now unzips into a folder of its own.** The zip holds one folder, `SSPTMM`, so it can be unzipped straight into your SPT folder - you get `SPT\SSPTMM\` beside the game - or anywhere else. Installing, updating and removing mods works with SSPTMM there; only its own folder is kept out of reach.
- **SSPTMM's own sp-mod.com listing is never offered as a mod**, like TCF Mod Manager's: Browse, collections and links leave it out, so subscribing can't unzip a mod manager into your SPT folder.
- **"Works with SPT 3.x" is no longer claimed.** SSPTMM is made and tested for SPT 4.0 and 4.1; the README and wiki now say so.

# SSPTMM 1.2.0

## Updating from 1.1.0

Close SSPTMM, then unzip `SSPTMM-1.2.0-win-x64.zip` over your SSPTMM folder. Your settings in the `Data` folder stay as they are. SSPTMM 1.1.0 points this release out by itself (a dot beside Help and About); 1.0.0 has no such check.

The first time 1.2.0 starts, it asks whether it may keep checking GitHub for new releases at start (see Changed).

## New

- **Presets in Subscribed items.** A preset is a saved set of which mods are on and which are off, as Mod Organizer 2's profiles keep them - your Fika setup, everything off for troubleshooting, or anything else. **Presets** in the toolbar saves the current setup under a name, applies a preset, and has **Disable all mods** and **Enable all mods**.
  - Applying asks first, naming every mod it turns off and on, and warns when a mod left on would be missing something it needs, or when it turns off a mod sp-mod.com marks as changing your profile.
  - A copy of your SPT profiles is taken first, nothing is deleted (mods only move in and out of their `.disabled` folders), **Undo** puts it back, and **Put back** returns the mods to how they were before the last preset even after a restart.
  - Mods installed after a preset was saved are left as they are.

- **Play straight from SSPTMM (experimental, off by default).** Switch on **Options > Start the game from SSPTMM** and the Play page gets a profile list and a big **PLAY** button. Play starts the server if it isn't running, waits for it, then does what SPT's launcher does before a raid (removes the files SPT's launcher removes, applies SPT's game patches, fetches mod bundles) and starts the game with that profile. No SPT launcher window. The **...** button beside Play has the rest: wipe the profile on the next start, make a new profile, delete one, clear the game's cache, or open the SPT launcher instead. A copy of your profiles is kept before a wipe or delete.
  - It works with SPT 4.1.3 to 4.1.6. A newer 4.1 is tried, with a warning. Any other version (4.0 included) keeps the SPT launcher button.
  - It copies how SPT's 4.1 launcher works, from its source code. The SPT team didn't make it and doesn't support it. If the game won't start or acts strangely, switch it off and use the SPT launcher. When you ask for help, say the game was started from SSPTMM.
  - SSPTMM minimises itself when the game starts and comes back when it closes; **Leave SSPTMM open when the game starts** turns that off. While it is on, Start server no longer opens the SPT launcher (that switch is greyed out, with a note).
  - Different from SPT's launcher: it stops when the server and the game files are different SPT versions, where SPT's launcher only notes it in its log and starts anyway. It keeps the last three game logs, so **Diagnose logs** can still read them after the next start clears them.

## Changed

- **SSPTMM asks before it checks for new releases.** The first time it starts, SSPTMM asks whether it may ask GitHub for a newer release each time it starts; until you say yes it doesn't. About has **Check for new releases when SSPTMM starts** to change your answer, and **Check now** works either way. The Server Map mod's version is checked with it, and only when the Server Map is in use on this PC.

- **SSPTMM stands on its own.** It no longer follows TCF Mod Manager's releases. About and the README keep a credit to TCF Mod Manager by TheCrimsonFckr, which SSPTMM began as a fork of. Help's **Open the full guide** now opens SSPTMM's wiki on GitHub.

- **Every connection and file is written down.** The wiki's new [Files and network](https://github.com/trappuss/SSPTMM/wiki/Files-and-Network) page lists every server SSPTMM talks to - when, what is sent, and how to stop it - and everything it writes. SSPTMM has no telemetry.

- **Pictures tell only sp-mod.com where they are shown.** The `Referer` that sp-mod.com's picture server needs is now sent only to sp-mod.com, not to YouTube or other picture hosts.

- **Closing SSPTMM with update notifications off removes its Windows notification registration**, so deleting SSPTMM's folder leaves nothing behind. Uninstalling in the wiki says so.

- **Subscribed items opens the way you left it.** Its filters, sort, grouping and view are remembered by themselves, so the **Save as default** button there is gone, and so is its row in Options > Page defaults. A default you saved before is where it starts. The search box is not kept, and **Clear filters** puts the filters, sort and grouping back to the app's own. Browse keeps its Save as default.

## Fixed

- **Installs work with SSPTMM in the SPT folder itself.** With SSPTMM.exe in the SPT root rather than a folder of its own, every install, update and removal was refused, because every file counted as SSPTMM's own. Now only SSPTMM's own files and folders (`Data`, `Staging`, `Licenses` and the exe) are kept out of reach. A folder of its own still keeps the whole folder out of reach. (Ported from TCF Mod Manager 1.19.1's fix.)

- **An install that would place nothing now fails and changes nothing.** When every file in a mod's archive would land somewhere SSPTMM won't write (SPT's own files, or SSPTMM's), the install stops with a message saying so, and an update leaves the previous version and its record as they were.

- **"Close the game when the server stops" now closes the game promptly.** With the server already gone, the game can't finish quitting - it waits for the server to answer - so it sat there for up to fifteen seconds before it was killed, and looked as if nothing happened. Now **Stop server** closes the game first, while the server can still answer it, and the game quits cleanly in seconds. When the server goes any other way, the game is closed within about two seconds of it going, and killed two seconds after that if it hasn't gone. Each step is written to the app's log, and a game that couldn't be closed is shown as an error on the Play page.

# SSPTMM 1.1.0

## Updating from 1.0.0

Close SSPTMM, then unzip `SSPTMM-1.1.0-win-x64.zip` over your SSPTMM folder. Your settings in the `Data` folder stay as they are.

## New

- **Newer releases are pointed out.** When it starts, SSPTMM asks GitHub whether a newer release is out. If one is, a dot appears beside **Help** and **About**, and About names the version, its date and download size, shows its release notes and has a button to the release page. SSPTMM never downloads or installs anything by itself. **Check now** on About asks again.

- **SSPTMM has its own icon and mascot.** The window, taskbar, tray and exe icon and the art at the left of the Workshop banner now show SSPTMM's mascot in place of the placeholder.

## Changed

- **"Hide mods with AI content" is gone** from the Browse and Subscribed items filters. sp-mod.com has no such flag: its API sends no such field and refuses it as a filter, so the option never hid anything. A saved filter default that had it ticked is ignored.

## Fixed

- **The check after an install now covers server configs too.** Every file is compared with the archive's copy after an install, but server configs were all skipped, so one that went missing or came out different was never reported. Now only the configs that are meant to differ are skipped: those merged with your changes, kept as yours, or left alone.

# SSPTMM 1.0.0

SSPTMM (Steamified SPT Mod Manager) is a Windows app for finding, installing and keeping track of SPT (Single Player Tarkov) mods from sp-mod.com. It looks and works like Steam's Workshop. It is built on TCF Mod Manager 1.19.0-beta by TheCrimsonFckr, but it is a separate app with its own name, version numbers and settings folder. It does not touch an existing TCF Mod Manager install.

## Download and first start

1. Download `SSPTMM-1.0.0-win-x64.zip`.
2. Unzip it into a folder of its own. Never unzip it inside `BepInEx\plugins` or `user\mods`.
3. Run `SSPTMM.exe`.
4. Open **Options** (the gear at the top right). Set **SPT Install Folder** to your SPT game folder, the one with `EscapeFromTarkov.exe` in it, then press Save.

- Needs Windows 10 or 11. The app is self-contained, so you don't need to install .NET.
- Settings, caches and logs are kept in a `Data` folder beside the exe. SSPTMM doesn't read TCF Mod Manager's settings, so you set your SPT folder once here.
- SSPTMM and TCF Mod Manager can both be used on the same SPT install. Each app can open the other's mod list files.

## What's new compared with TCF Mod Manager

Everything TCF Mod Manager 1.19.0-beta does is still here, under Steam's names: Install is **Subscribe**, Remove is **Unsubscribe**, Installed is **Subscribed items** and Mod lists are **Collections**.

### Look & navigation
- Steam Workshop look throughout: Steam's colours, layout, grid background, buttons, menus, dialogs and tooltips. The theme is always dark.
- Six tabs across the top, laid out like Steam: Play, Workshop, Subscribed items, Collections, **Tools ▾** and **Help ▾**. Options is a gear, and an sp-mod.com button sits where Steam's Store Page is.
- A downloads bar along the bottom of the window shows what is downloading or installing. Click it to open Downloads.
- Right-click any mod, anywhere, for Open, Quick View, Subscribe/Unsubscribe, Update, Download only, Add to Collection, Copy link, View mod page, the author's page and Follow.
- Item, author and collection pages open over the page you came from. Esc, the mouse back button or Alt+Left steps back through them in order.
- Smooth scrolling. Options can switch it off, hide the Subscribed items and Collections tabs, or put a picture of your own behind the window in place of the Steam grid, with a slider to darken it.
- Pages show one line of explanation. A small "?" at the end of the line holds the rest.

### Workshop
- **Workshop Home**: a banner, the past week's most downloaded new mods, From Followed Authors, list tabs (Top Rated, Most Subscribed, Last Updated, New) and content types with counts.
- **Browse** with Steam's sidebar: SPT version tick rows, Content Type, Special Filters (Featured, Created by Followed) and tag boxes (Fika compatible, Has dependencies, Has addons, hide Contains ads, hide Subscribed).
- Steam's sort orders, mapped to what sp-mod.com counts: Top Rated All Time (endorsements), Most Recent, Last Updated, Total Unique Subscribers (downloads) and Most Favorited.
- Search options (Title & Description, Title Only, Description Only) and **Filter by Date** (posted or last updated between two dates).
- Each active filter shows as a chip on the results line. Click a chip to remove that filter.
- **Per page** offers 10, 15, 30, 50 or **Infinite**, where more items load as you scroll. Pages open on Infinite. Whatever you pick is remembered for each page.
- **Hover** over a card to see Steam's popup: a slideshow of the mod's pictures, its teaser, dates, SPT version and tags.
- **Quick View** (the magnifier on a card) shows pictures, author, stats, tags and the start of the description, with Subscribe and Add to Collection. The arrow keys step through the list.
- Every card shows a badge when the mod is installed, has an update or is disabled. The badge also appears on Home and on your collections.
- **Item page** with Description, Comments, Change Notes (every version, in Steam's style) and Versions tabs, a strip of pictures and videos, and a full-size picture viewer. YouTube videos play in the app.
- Descriptions show exactly as written on sp-mod.com, including tabs, tables, code, warnings and animated GIFs. Long descriptions load in parts, and GIFs pause when they are off screen.
- Required Items, ticked when they are installed. Required By lists mods that need this one, and More By lists the author's other mods.
- sp-mod.com's own details on the item page: the multiplayer-cheat warning, the "may change your profile" notice, VirusTotal scans, licence, plugin GUID, source code links, Passed Verification, and the list of files in a download.
- **Versions** tab: each version's SPT range (green when it runs on your SPT), size, downloads, Fika status and requirements, with **Install this version** or **Switch to this version**.
- **Comments** tab: sp-mod.com's real comments section, shown inside the app. Sign in once to reply or react.
- Content Type on an item page is a link. It opens Browse filtered to that type, top rated first.
- **Authors' pages** with their Workshop Items, Collections and Addons tabs, followers, member-since date and a link to their sp-mod.com page.
- **Follow authors.** An Authors you follow page lists them, and Home shows what they posted recently.

### Subscribing & downloads
- Subscribe finds what a mod needs first, then asks once in Steam's **Additional Required Items** dialog: Subscribe to Just This Item, Subscribe to All, or Cancel.
- Subscribing from a mod's page doesn't ask you to open its page again. Elsewhere, the "read the mod page first" window shows each page inside the app, and shows the change notes for an update.
- **Up to three downloads at once.** Installs still happen one at a time, in queue order. Downloads that fail for a passing reason are retried, and stalled ones pick up where they left off when the server allows it.
- Downloads are kept (up to 4 GB), so installing the same version again doesn't download it again. Options can turn this off or delete the kept files.
- **Download only**: a button beside Subscribe, and a right-click entry, that saves the mod's file to your download folder without changing anything in SPT.
- **Install from file** for an archive you already have (.zip, .7z, .rar, .tar, .tar.gz). You can also drop archives onto Subscribed items. Your file is copied, never moved.
- **File clashes**: before an install, you see any files already on disk that belong to another mod or to nothing the app installed, and the install waits for **Install Anyway**.
- Before an install, you're told about required mods that are disabled, too new, the wrong version, in conflict, or have no version for your SPT, and about versions sp-mod.com marks as not for Fika when your install runs Fika. You can stop there.
- **Unsubscribe from all** on a collection lets you set the items aside (disable them) or remove them.
- The Unsubscribe question is a Steam dialog with **Don't ask again**. Options can turn the question back on. While the question is off, config files are always kept.
- Removing a mod warns you when installed mods still use it, or when a hand-installed folder would be deleted.

### Subscribed items
- Cards, List and Groups views, all showing the mod's picture and the same details.
- One toolbar: search, sort, a **Filters** popup, the view buttons and a **⋯** menu (Install from file, SPT upgrade check, Rescan and more).
- **Group by** in Cards and List: your own groups, sp-mod.com category, or enabled/disabled. Drag mods between your groups in every view.
- **Recently installed** sort, newest first.
- **Pick several mods** the way Steam's library does it: Ctrl+click, Shift+click, Ctrl+A and Esc. A bar appears with Select all, Update selected, Enable, Disable, Unsubscribe and Clear.
- An **on/off switch** on each mod to enable or disable it.
- A blue **Update** button on each mod that has an update, and **Update all** at the top.
- **Held-back updates**: an update that would break another installed mod is flagged, and Update all leaves it alone.
- Mods are flagged when their installed version doesn't run on your SPT (naming the newest version that does), or when they need a mod that is missing or disabled. This also works offline and for hand-installed mods.
- **Pinned mods** always sit at the top and wear a PINNED tag. Applying a collection never disables them.
- **RE-UPLOADED** tag, with **Get it again**, when an author re-uploads the same version number with a different file size on sp-mod.com.
- **Profile-change warning**: removing a mod that sp-mod.com says may permanently change your profile puts that warning first, even with Don't ask again ticked. Removing several lets you keep those and remove the rest.
- **SPT upgrade check**: pick a newer SPT release to see which of your mods are ready, need an update first, or have nothing yet.
- Undo for the last disable, enable or removal sits right under the toolbar.
- An opened card is shorter, with the details behind **More details**. When nothing is installed, the page offers Browse the Workshop and Install from file.

### Collections
- **Public collections from sp-mod.com** (sp-mod.com/lists): browse them, search them (including by who made them), filter by SPT version and sort six ways. Hover popups and Quick View work here too.
- A collection's page shows its description, the author's note for each item, the version each mod gets on your SPT, and a line saying how much of it you already have.
- **Subscribe to all** installs each item at the newest version for your SPT. If the list was made for another SPT, you choose **For My SPT** or **For the Collection's SPT**. Before anything changes, you see what will happen, with **Add Only** or **Overwrite My Subscriptions**.
- **Update all (N)** on a collection updates its subscribed items.
- **Favorite** public collections. Your Favorites page marks the ones that changed since you last opened them.
- **Your collections** opens on a grid. Each card shows pictures of its first four mods, the author, item count, SPT version and how much is installed. Mods in a saved collection show whether you're subscribed to them.
- **Create from installed mods**, **Add from code**, **Add from file** and **Follow a shared file**.
- **Add to Collection** from an item page, Quick View or the right-click menu. **Save to Collection** makes your own copy of any collection.
- **Share with friends** in two ways:
  - **share code**: one line of text that fits in a Discord message;
  - **shared folder**: for example Dropbox, OneDrive, Google Drive or a network share, kept up to date automatically.
- When a friend's code or shared file changes, a notice says exactly what was added or removed and offers Sync or Later. A code you copy to the clipboard is noticed when you switch to the app.
- Importing a list file of one of your own lists asks whether to add it as a copy or replace yours.

### Play
- **Stop server** (red) while the server runs. Like Restart, it asks first.
- Options can open the SPT launcher as soon as the server is ready, and run the server **without its window** (its log opens on the Play page instead, which tells you if the server closed before it was ready).
- **Server log**: the server's log, live, under the server card.
- **Close game** on the launcher card while the game runs. It asks first, because a raid in progress is lost. A tick box, off by default, closes the game when the server stops.
- **Mod tools** card: lists every .exe that an installed mod put in your SPT folder (for example SVM's Greed or give-ui), with Open, its folder and Hide. For tools it knows (Greed, give-ui), it notes whether the server should be stopped or running.
- "Running" now means running from this SPT install. Another SPT copy running elsewhere doesn't block this one.

### Tools
- The Tools menu holds Configs, Dependencies and Conflicts, and the new Diagnose logs. Mod footprint and Server map appear there when you switch them on in Options.
- **Diagnose logs** (new) reads the server log, BepInEx's log and the game's error log, without changing anything. It explains in plain words what went wrong and names the mod: profiles that won't load, clothing from a missing mod, raid results that weren't saved, a server that couldn't start, plugins that didn't load, server mods that aren't running, two mods shipping the same bundle, and mod errors.
- Diagnose logs can **copy a report for help**, with your Windows user name, user folder, profile IDs and IP addresses taken out.
- **Configs**: earlier versions of a config file can be brought back through the editor.

### Safety
- **SPT profile backups**: your profiles are zipped before installs, updates, removals, disabling and applying a collection, whenever they changed since the last copy. The last 10 per install are kept, and Put back refuses while SPT is running. SPT's own profile backups are left out. Removing a hand-installed mod now takes a backup first too.
- **Your BepInEx settings stay yours.** An archive's `BepInEx\config` .cfg file is only placed where none exists yet, or over the app's own untouched copy. The download card tells you when your settings were kept.
- A config setting whose type changed in a new version of a mod is not carried over.
- Read-me files, licences and pictures in an archive are never dropped into your SPT folder. Archives with `plugins\` or `patchers\` but no `BepInEx\` folder around them are placed correctly.
- Server prepatches in `user\patchers\` are installed, updated and removed with their mod. Disabling a mod leaves its prepatch so your profile still loads.
- Empty folders that a mod ships (such as SVM's Presets) are created, and removed again with the mod.
- **Checks after every install and update**: each file on disk is compared with the archive. The download card names any file that is missing or different.
- Files a mod tries to put in SPT's own user folders (outside `user\mods` and `user\patchers`) are not placed, and the download card names each one in a warning.
- SSPTMM never installs TCF Mod Manager over itself, and never lists its own sp-mod.com listing as a mod to install.

### Help
- **Help** is rewritten for the Steam layout, with new topics for following a friend's collection, Mod tools and Diagnose logs. F1 and the "?" still open it at the page you're on.
- **Getting started** at the top of Workshop Home ticks off three steps from your actual install: SPT folder found, a mod installed, a profile created. It goes away once all three are done, or when you hide it.
- **About** replaces App update: version, the TCF Mod Manager release this is based on, credits and links.
- **Report a problem** goes to SSPTMM's GitHub Issues.

## Fixed

- **Server prepatches** in `user\patchers` were never placed, updated or removed. A mod update could leave a stale prepatch behind. With Skills Extended, the server then rejected the end-of-raid data and raid results were lost. An old leftover prepatch is now recognised by its mod's folder and handled on removal.
- **SVM's Greed said "couldn't find Preset folder"** because the empty `Presets` folder in SVM's archive was never created.
- **An update replaced your tuned BepInEx `.cfg`** with the mod's defaults, and a first install could replace one BepInEx had already written.
- **A mod's required items were not offered again** after you removed them and subscribed to the mod again in the same session.
- **A mod list entry with no version installed the oldest release.** It now installs the newest release for your SPT.
- **Pre-release versions**: going from 1.2.0-beta to 1.2.0 now counts as an update. "-hotfix", "-fix" and "-patch" versions count as newer than their release.
- **A kept download was thrown away and fetched again on every reinstall** when the file's real size differed from the size sp-mod.com listed.
- **Tidying kept downloads stopped after deleting one file.**
- **Failed downloads and installs left no trace in the log.** The reason was only shown on the download card.
- **Install dates**: mods this app installed now show the date of their last install or update. Before, they used the folder's date, and a mod copied with its archive's dates could look a year old.

## Known limits

- **Translations**: Steam terms use Steam's own German, French, Italian and Russian wording. Text that is new in SSPTMM (including reworded Help steps) shows in English in those languages until volunteers translate it.
- **Re-uploads**: SSPTMM can't tell when a file is swapped where it is hosted (for example on GitHub) and the sp-mod.com listing is left unchanged.
- **Interrupted installs**: if an install is cut off part-way (power cut, the app killed), it is not put back on the next start. Closing the window or quitting from the tray while files are being placed asks first.
- **Updates to SSPTMM**: SSPTMM doesn't check for or install its own updates. Get new versions from https://github.com/trappuss/SSPTMM. It also never offers TCF Mod Manager's updates, because installing those would replace SSPTMM.
- **Description search** uses sp-mod.com's search, which returns at most its 20 best matches. Mod names and teasers are still searched in full.
- **Comments** need the Microsoft Edge WebView2 runtime. Without it, the Comments tab offers to open them in your browser.
- **Follows and Favorites** are stored on this PC only, because sp-mod.com has no follows or collection favorites to sync with.
- **Required items** are all treated as required, because sp-mod.com doesn't mark any as optional.
- **No star ratings, time ranges or Most Popular**: sp-mod.com doesn't record them. Endorsements stand in for stars.
- The **Versions** tab can list fewer versions than sp-mod.com's own page, because sp-mod.com's data feed returns fewer.
- **Running the server without its window**: a server mod that draws on the console itself may fail in that mode.
- **Shared folders** on Dropbox, OneDrive or Google Drive have only been tested as plain local folders. A file caught mid-sync is skipped until the next check.
- A **share code** carries a hand-installed mod by name only.
- **Diagnose logs** can only read the game's error log from the latest game session, because the SPT launcher deletes the game's Logs folder each time it starts the game. The page tells you when that happened.
- **Open the full guide** in Help still opens TCF Mod Manager's guide on sp-mod.com. Most of it applies.
- Folders the app makes inside your SPT install keep TCF Mod Manager's names (`.tcfmm-removed`, `.tcfmm-work`, `.tcfmm-duplicates`), so both apps can work with the same install.

## Credits

- Built on **TCF Mod Manager** by **TheCrimsonFckr**. Its mod handling, install and removal, and much of what sits under the Steam look are that app's work. Please report SSPTMM problems on SSPTMM's GitHub Issues, not to TCF Mod Manager.
- Mods, collections, comments and author pages come from **sp-mod.com** and the people who publish there.
- Uses the Noto Sans font (SIL Open Font License).
- **Licence**: SSPTMM is under the MIT License, as TCF Mod Manager is, with both copyright notices in `LICENSE.txt` beside the exe. The `Licenses` folder holds the licences of everything the exe carries (the .NET runtime, WPF UI, SharpCompress and the rest), listed in `Licenses\THIRD-PARTY-NOTICES.md`.
- Steam and the Steam Workshop are trademarks of Valve Corporation. SSPTMM is an independent fan project and is not affiliated with or endorsed by Valve, Battlestate Games, the SPT project or sp-mod.com.
