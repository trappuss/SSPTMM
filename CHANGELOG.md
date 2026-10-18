# SSPTMM 1.4.4

## Updating

From 1.3.0 or later: Help > About > **Install update**. From an older version: close SSPTMM, download `SSPTMM-1.4.4-win-x64.zip` and copy the contents of its `SSPTMM` folder over your SSPTMM folder. Your settings in `Data` stay as they are.

## New

- **Friends can suggest mods for your collection.** A friend opens the collection you shared, presses **Save to Collection** to make a copy of their own, adds the mods they'd like and shares that copy back to you. When you add it - **Add from code**, a code on your clipboard, or **Add from file** - SSPTMM sees it is a copy of one of yours and lists the mods it has that yours doesn't, all ticked. **Add to mine** puts the ticked ones in your collection; nothing is taken out of it, and the mods their copy left out are only counted. **Keep as its own collection** adds the copy like any other. Share yours again and everyone gets them - a collection kept in a shared folder is updated there straight away. A share code only says what it was copied from when your friend has 1.4.4 or later; from an older version it arrives as a collection of its own. *Asked for in feedback.*

## Fixed

- **Help's Collections how-tos said "Open YourCollectionsPage"** on their button instead of "Open Collections".

# SSPTMM 1.4.3

## Updating

From 1.3.0 or later: Help > About > **Install update**. From an older version: close SSPTMM, download `SSPTMM-1.4.3-win-x64.zip` and copy the contents of its `SSPTMM` folder over your SSPTMM folder. Your settings in `Data` stay as they are.

## New

- **Back and Forward list the places that way.** Right-click an arrow to jump straight to one. Options > General > **Home page** can be **Where I left off**, and a mod page you go back to opens at the tab and the place you were reading, not at its top.
- **Workshop cards open from the keyboard.** Tab to a mod or collection card and press Enter or Space; screen readers see each card as a button with the mod's name.
- **Renaming a group to a name another group has offers to merge them** - its mods go into the other group - instead of leaving two groups of one name.
- **Change mods while SPT is running, if you say so.** Options > Installing mods > **Allow changes while SPT is running**: installing, removing, switching mods on or off, Undo and applying a list then ask "SPT is still running - change the mods anyway?" instead of refusing, as Mod Organizer 2 does. Off by default: files the game has open may not move, and SPT and the game only see a change after a restart. One yes covers a batch. Profiles are never put back while SPT runs.
- **Pick which mods to update.** The arrow beside Update all, or a right-click on it, lists every update waiting - each mod's picture, name and versions (`2.0.0 → 2.1.0`), marked when it was installed by hand or is switched off. Each row has its own update button, which turns into a spinner with the download's percentage, then Installing, then a green tick; a failed one shows a retry button. **Skip** on a row skips that update, as the right-click menu's Skip this update does. The list's own Update all does the rest. A collection page's Update all has the same arrow, listing that collection's waiting updates - without Skip there, since a public collection's item updates to the version its page names.
- **A hand-installed mod no longer holds back every other update.** When Update all includes mods you installed yourself, the warning about them now offers **Update all**, **Only the others** - which updates the rest and leaves those as they are - or Cancel. Before, No cancelled the whole batch. The same on a collection page's Update all.
- **Update all goes in dependency order.** A library is updated before the mods that use it, so each installs against the new version of what it needs.
- **Disabled mods can be updated, and stay disabled.** Update, Update all and the update dialog switched off mods were refused until they were turned back on; now the mod is switched on for the update and off again after. (Not for a mod sharing its folder with another.) One switched back on while the update waited in the queue is updated and left on.
- **Open client folder / Open server folder** on a mod's right-click menu in Subscribed items, as on its card.
- **Copy error and Report on GitHub.** A failed download has a **Copy error** button: why it failed, with the versions of SSPTMM, SPT and Windows. The crash message can copy the error or open a new GitHub issue with it filled in, and so can Diagnose logs (**Report on GitHub**, beside Copy report). Your Windows user name and user folder are taken out; nothing is sent until you submit the issue yourself.
- **An archive SSPTMM doesn't recognise is no longer refused.** One without BepInEx or user folders - a DLL and a read-me, a server mod's folder on its own - shows what it holds and asks whether it is a client mod (BepInEx\plugins) or a server mod (user\mods), suggesting the likelier; Cancel refuses it as before. The same for Install from file. A mod packed in one folder keeps that folder's name (`BepInEx\plugins\SAIN`), unless the name carries a version; loose files go in a folder named for the mod.
- **Rename a mod.** Right-click a mod in Subscribed items > **Rename...** to give it a name of your own; empty gives it back its own. A listed mod keeps the name through updates.
- **Drop archives anywhere on the window** to install them, not only on Subscribed items - it opens to ask about them.
- **Pause downloads.** The Downloads page's **Pause** stops new downloads from starting until **Resume**; one already under way finishes.
- **Animations** can be turned off: Options > Look. The Workshop cards stop rising under the pointer and pages change without sliding in.
- **Catalogue address**, Options > Advanced: a mirror of sp-mod.com that answers the same API, for when sp-mod.com is down. It is checked before it is kept and used from the next start; comments, mod lists and links stay sp-mod.com's.
- **Keyboard shortcuts.** Ctrl+F goes to the page's search box and F5 does what its Rescan or Refresh button does, where it has one. Help > Getting started > **Keyboard shortcuts** lists them all.
- **Paste or drop a mod's link.** With a sp-mod.com mod link copied, Ctrl+V outside a text box opens that mod's page in SSPTMM; dragging the link from a browser onto the window does the same, Subscribed items included.
- **Interface size**, Options > Look: everything in the window from 80% to 200%, and Ctrl+= / Ctrl+- / Ctrl+0 at any time. Pages lay themselves out again at the new size; menus, tooltips and dialogs keep theirs.
- **Skip this update.** Right-click a mod with an update > **Skip this update (version)**: the mod stops showing as out of date, and Update all and update notifications leave it out, until a newer version comes out. **Stop skipping** on the same menu takes it back.
- **Go back to the version before an update.** Right-click an updated mod > **Go back to (version)** puts back the copy kept when it was updated, with its config files as they were - offline, and even when the author has taken that version down. The version you leave is kept the same way, so you can go forward again. Settings you have changed since the update stay as you set them, and a file of another mod that the update replaced is put back. Works while Options > Keep removed mods still holds the old copy; a switched-off mod is switched on first.
- **Notes on a mod.** Right-click > **Note...**: a line of your own (needs a config change, broke with another mod...), shown under the mod's name in every view and found by the search.
- **The right-click menu acts on every picked mod.** With several mods picked (Ctrl+click, Shift+click, Ctrl+A), right-click offers Enable, Disable, Update and Unsubscribe for all of them, and Add to group.
- **Free space checked before a big download.** When the queue would need more room than the SPT drive has - the downloads unpacked and installed, with some to spare - it is paused before anything starts, and says how much is needed and free; **Download anyway** goes on regardless.
- **Accent colour**, Options > Look: Steam blue or green, orange, purple, red, pink, or any colour typed as six hex digits. Every blue in the window follows it at once - buttons, tabs, links, switches, sliders, the header's glow - in its lighter and darker shades too, and Steam's navy cards, bars and muted blue text take its hue as well, as dark as they were: a purple accent gives plum cards, a green one deep green.
- **OLED black**, Options > Look: the pages black instead of Steam's navy. Cards, bars and dialogs stay a little lighter so they still stand off the page.
- **Card size**, Options > Look: Compact, Normal or Roomy cards and rows on Subscribed items - smaller pictures and more to a row, or larger and fewer.
- **Blur the background picture**, Options > Look > Tabs and background: a Blur slider under Darken, for a picture of your own behind the window.
- **Colours for groups and mods.** Click the dot before a group's name, or right-click its header, to give the group a colour: each of its mods gets a strip of it down the card's left edge, in every view. Right-click a mod > **Colour** gives it one of its own, which wins over its group's - for several picked mods at once too. Ten colours to pick from, or **Custom colour...** for any six hex digits.
- **Filter by colour.** Filters > Colour shows only the mods drawn in one colour, their own or their group's, or those with none; typing `color:red` (or `colour:`, or a hex like `color:#5BA32B`) in the search box does the same.
- **Look files**, Options > Look: **Save look...** puts the accent, OLED black, card size, the background picture itself with its darkness and blur, and your groups' and mods' colours in one `.ssptmm-look` file; **Load look...** puts them back, on this PC or another. Groups take the colours of groups with the same name.
- **Play on another PC's server - a Fika host, or a friend's SPT.** Play's card has a server list when there is more than this PC's to pick from: the servers you added in SPT's launcher are on it, and **...** > **Add a server...** adds one by name and address (`203.0.113.7`, `host:6969`, an `https://` link). With another PC's server picked, Play doesn't start a server here: it checks that one answers, shows the profiles on it - New profile, Delete and Wipe act on that server's - and starts the game against it, after doing here what SPT's launcher does here (the clean-up, SPT's game patches, the mods' bundles). Its certificate is trusted the first time and remembered; if it changes, Play stops and says so, and **Trust the new certificate** is there for when the host has set SPT up again. Wipe and Delete keep no copy, as the profiles are on the other PC. Your SPT version and mods still have to match the host's - for Fika, the host's Fika version too. *Asked for in a comment.*
- **Play with Fika (experimental).** Options > Fika and servers > **Play with Fika**: Automatic, On or Off. Automatic switches the other-server options above on when Fika is found in the install - its plugin, or its server mod - and Options says which it found. On a PC that hosts Fika, Play's card lists this PC's network addresses with the port from Fika's own `fika.jsonc` (Fika ignores `http.json`'s), with **Copy address**, and the two ports Fika's guide says to forward: TCP for the server, and UDP 25565 to whoever hosts a raid. It warns when `fika.jsonc` only listens on `127.0.0.1`, which friends can't reach, and when it isn't written yet because the server hasn't run with Fika. Playing on someone else's server while this PC's own server is running gets a warning too: with Fika only the host runs one, and Fika's FAQ names a stray one as a reason friends don't see each other. Off keeps Play to this PC's server and remembers the servers for later.
- **Told when the downloads finish.** When the queue empties while SSPTMM is in the tray, a Windows notification says how many were done and how many failed; while it is minimised or behind other windows, its taskbar button flashes.

- **All of SSPTMM in German, French, Italian and Russian**, not only Steam's terms: about a third of the app - Help, Options' explanations, most messages, everything new - showed in English in those languages, and none of it does now. Dates follow the language (day first, 24-hour clock) instead of English's "Oct 16, 2026 @ 2:34PM".
- **简体中文 - Simplified Chinese**, new: Options > General > Language, and chosen by itself on a Windows set to Chinese. Chinese in the app and in mod names is drawn with Microsoft YaHei UI.
- These translations are machine-made and haven't been read by native speakers yet - corrections are welcome on GitHub, and Options names each one as such under the language list.

## Fixed

- **The downloads bar spoke English in every language** ("DOWNLOADS - Manage") until a download started. *Reported by Praxideke.*
- **Presets tell apart two mods in one folder.** A preset kept such a folder as one entry, so it couldn't keep one of its mods on and the other off, and when one was off it left the folder alone as "in two places". Each now has its own entry and is switched by its own files, as its card is. Presets saved before this go by the folder's entry for both.
- **Switching off one of two mods in one folder no longer warns about the other.** The warning listed mods that need the mod staying on, and could name the folder's other mod as the one being switched off.
- The Options gear at the top right now has a name for screen readers.
- A mod sharing its folder with another named the other by its own name on its card, not the name you gave it with Rename.
- **Presets** saved while two mods shared a folder, applied after one of them left it, no longer guess which mod's on or off to use when the two differed: the folder is left as it is and listed as not in the preset.
- Going **Back** to a collection deleted since no longer left the back and forward history one step out from the page shown.
- **The Play page could stop with an error** while checking mods for conflicts, when a mod installed by SSPTMM shared a folder with another and its files could not be found inside that folder - a single DLL dropped straight into `BepInEx\plugins`, or a file removed while the check ran. That mod is now left out of the shared-folder cards instead, and the Play page logs anything else that goes wrong there rather than stopping.
- **A collection's Update all counts what Subscribed items' does.** For one of your own collections it could list updates Subscribed items leaves out and miss ones it offers. Opened before Subscribed items, it didn't know which updates sp-mod.com holds back - two Manimal's backports showed as waiting. A mod saved in the collection before sp-mod.com listed it (DynamicMaps Extended) wasn't recognised, so its update was missing. And a switched-off mod was always left out, though Subscribed items now updates it and switches it off again. The collection now uses Subscribed items' own rule, and its count changes as soon as sp-mod.com answers.
- **Files and network** in the wiki said Start the game from SSPTMM is off by default; it has been on by default since 1.4.0.
- The Play card said "the server starts by itself if it isn't running" with another PC's server picked. It now says the game connects to that server.
- The Play card's **...** button has a name for screen readers.

# SSPTMM 1.4.2

## Updating

From 1.3.0 or later: Help > About > **Install update**. From an older version: close SSPTMM, download `SSPTMM-1.4.2-win-x64.zip` and copy the contents of its `SSPTMM` folder over your SSPTMM folder. Your settings in `Data` stay as they are.

## New

- **Back, Forward and Home, as in Steam.** Arrows at the top left, beside the title, go back and forward through the pages you've been on, with the mod, collection and author pages you opened along the way - and so do the mouse's back and forward buttons and Alt+Left / Alt+Right. Home (Alt+Home) goes to your home page, which is also where SSPTMM opens: Options > General > **Home page** - Workshop home (as before), Browse, Subscribed items, Collections or Play.
- **Put several mods in a group at once.** On Subscribed items, drag one of several picked mods (Ctrl+click, Shift+click) and they all go to the group it is dropped on; before, only the one under the pointer went. *Found by FoxHead.*
- **Add to group on the right-click menu.** Right-click a mod: **Add to group** lists your groups, **New group...** asks for a name and makes it, and **Remove from group** puts the mod back in Ungrouped - on every picked mod when it is one of them. Mods sp-mod.com has no listing for get the menu too. *Suggested by FoxHead.*
- **Diagnose logs can let SPT clean a profile it won't load.** When a profile is refused for holding items or a trader from a mod that is gone, **Turn them on** sets SPT's own `removeModItemsFromProfile` and `removeInvalidTradersFromProfile` to true in `core.json` - after asking, and keeping the file as it was beside it - so the server removes what's missing when it next starts. When they are already on, the card says so. Play offers the same button when it stops on "SPT won't load this profile".

## Fixed

- **Two mods installed into one folder both count as installed.** SPT Stance Overhaul and SPT Realism Common Lib both put their files in `BepInEx\plugins\Realism`. SSPTMM installed both, then saw one folder and gave it one card - so Stance Overhaul looked as if it had never installed: Subscribe never stuck, and installing anything that needed it queued it again. Each now has its own card, and switching one on or off, updating it or unsubscribing it touches only its own files. Mods put into one folder by hand get a card each too, matched by what their DLLs declare; SSPTMM can't tell their files apart, so switching one switches both, the cards say so, and removing one of them is refused. *Found by FoxHead.*
- **New group... with a name already taken uses that group** instead of making a second one of the same name, from the menu and from the Groups bar.
- The profile messages named `SPT\SPT_Data\configs\core.json`; on SPT 4.1 it is under `SPT_Runtime`. They now say `SPT_Data\configs` in the server's folder.

# SSPTMM 1.4.1

## Updating

From 1.3.0 or later: Help > About > **Install update**. From an older version: close SSPTMM, download `SSPTMM-1.4.1-win-x64.zip` and copy the contents of its `SSPTMM` folder over your SSPTMM folder. Your settings in `Data` stay as they are.

## New

- **A mod's addons have their own tab.** They were at the foot of the Description tab, under however long the description was, with nothing higher up saying they were there. A mod with addons now has an **Addons (5)** tab and a row under the Subscribe box that opens it. The list is drawn as the rest of the item page is, each addon with its author and downloads, its version picker and buttons at the right; with more than six there is a search box (one mod has 34). What is installed comes first, then what fits your version of the mod. An addon on an author's page opens its mod at that tab.
- **Install Anyway.** An addon says which versions of its mod it works with, and SSPTMM refused to install one that didn't match - but authors often leave that as it was while the mod moves on (every one of SAIN's five addons asks for a SAIN older than today's). The button now reads **Install Anyway**, in grey, and asks first, saying what it needs and what you have; No is the default. The same when the mod itself isn't found in your install. A mod that doesn't fit your SPT version was already asked about this way.

## Fixed

- **A mod's addons follow what you install.** Subscribe on a mod's page and its addons went on saying to install the mod first until the page was closed and opened again, and an addon just installed still offered Install. The list now updates as installs land.

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
