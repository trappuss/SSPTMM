# SSPTMM 1.1.0

## Updating from 1.0.0

Close SSPTMM, then unzip `SSPTMM-1.1.0-win-x64.zip` over your SSPTMM folder. Your settings in the `Data` folder stay as they are.

## New

- **Newer releases are pointed out.** When it starts, SSPTMM asks GitHub whether a newer release is out. If one is, a dot appears beside **Help** and **About**, and About names the version, its date and download size, shows its release notes and has a button to the release page. SSPTMM never downloads or installs anything by itself. **Check now** on About asks again.

- **Play straight from SSPTMM (experimental, off by default).** Switch on **Options > Start the game from SSPTMM** and the Play page gets a profile list and a big **PLAY** button. Play starts the server if it isn't running, waits for it, then does what SPT's launcher does before a raid (removes the files SPT's launcher removes, applies SPT's game patches, fetches mod bundles) and starts the game with that profile. No SPT launcher window. The **...** button beside Play has the rest: wipe the profile on the next start, make a new profile, delete one, clear the game's cache, or open the SPT launcher instead. A copy of your profiles is kept before a wipe or delete.
  - It works with SPT 4.1.3 to 4.1.6. A newer 4.1 is tried, with a warning. Any other version (4.0 included) keeps the SPT launcher button.
  - It copies how SPT's 4.1 launcher works, from its source code. The SPT team didn't make it and doesn't support it. If the game won't start or acts strangely, switch it off and use the SPT launcher. When you ask for help, say the game was started from SSPTMM.
  - Different from SPT's launcher: it stops when the server and the game files are different SPT versions, where SPT's launcher only notes it in its log and starts anyway. It keeps the last three game logs, so **Diagnose logs** can still read them after the next start clears them.

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
