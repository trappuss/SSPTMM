**TCF Mod Manager** is a Windows desktop app for finding, installing and keeping track of your SPT mods, built directly against the sp-mod catalog you're reading this on. Browse the full mod list, filter it down to what actually works on your SPT version, install with dependencies resolved for you, turn mods on and off without deleting anything, and see at a glance what's out of date.

It also keeps **mod lists** - save the set you run, switch between sets, send one to a friend, or follow the list a server publishes and know what you are missing before you launch. Plus a config editor, a start button for the server and the launcher, and an optional page that reads what each installed mod actually ships.

Rather install mods yourself? **Monitor mode** has the app download each mod for you to install by hand, and still keep track of what you have. And with **update notifications** on, it tells you when a mod you have gets a new release - even from the tray, with its window closed.

**Stuck on something?** The app has its own **Help** page in the sidebar: short step-by-step answers for every page, in your language. Press **?** in the title bar or **F1** to open it at the page you're on. This page is the full reference.

WPF with Fluent Design and .NET 9

The released build is entirely self-contained.

#### Guide and Information

##  {.tabset}

### Install
**You need**

- Windows
- An SPT install

**Steps**

1. Download `TCF-ModManager-<version>.zip` from this page.
2. Extract it into your SPT folder as `<SPT root>\TCFModManager\` - a sibling of `BepInEx\` and `user\`.
3. Run `TCFModManager.exe`.
4. Open **Options**, point it at your SPT install folder - the top one, with `EscapeFromTarkov.exe` and `BepInEx\` in it - and hit Save. The detected server version appears underneath - everything else keys off that.

#### warning
This is **not** a mod. Don't extract it into `BepInEx\plugins` or `user\mods`. It's a standalone application that manages that folder for you. Anywhere on disk works, really - it just needs to be told where SPT lives.

Once it's set up, the app keeps itself up to date from its own page here on sp-mod - see the **App updates** tab.

#### If the SPT version doesn't detect
The version is read from the server executable - `SPT.Server.exe`, or the older `Aki.Server.exe` - at the install root or under `SPT\` / `SPT_Runtime\`. Point Options at the install root, the folder with `EscapeFromTarkov.exe` in it, not at the server folder or any other subfolder. If you do pick `SPT\` or `SPT_Runtime\` itself, the app moves the setting up to the folder above it.


### Play
Starts the install rather than making you go and find it. It is the first page in the sidebar, and on a machine that only plays it is the only one you need before a session.

- **Start server** runs this install's server in its own console window. It keeps running if you close the app, and nothing on this page stops it - close its window when you are done.
- **Open launcher** opens `SPT.Launcher.exe`, where you pick a profile and start the game. Start the server first; the launcher has nothing to log in to without one.
- **Restart the server** sits beside it, enabled only while the server is actually running. It asks first, on the card, because a raid in progress does not survive one and there is no undo.
- **Fika headless client** gets its own card, and only on a setup that has a headless launcher. A headless hosts raids for other people rather than being the one you play on, so it never shares a button with the game.

#### Before you join
The page checks your install against the mod list you are following - your own, or one a server published - and says what is outstanding, with versions where a version is wrong: *SAIN (3.0.0 → 3.1.2)*. **Check again** re-runs it. When you are behind a server's list, **Review and install** opens that list on the Mod lists page with its preview already done.

It never blocks a launch. The mods it names may not matter for the raid you are about to play, the server may be wrong, and a launch held up by a warning nobody can override is worse than a mismatch.

#### information
Stopping on its own is deliberately not offered. A restart puts back what it took; a server left down is a state somebody has to notice, and this app is not the thing that should decide to leave it that way.

### Browse
The whole sp-mod catalog, fetched once and cached to disk so it opens instantly next time.

- **Search** by name, or by author with `@author`
- **Filter** by SPT release line, category, Fika compatibility and featured status
- **Sort** by newest, last updated, most downloaded, most favourited or most endorsed
- **Show** options for hiding ads, AI-generated content and **mods you already have installed**
- **Refresh cache** re-pulls the catalog when you want the newest listings

Each card shows the download count, the endorsement count when the mod has any, a status dot, and a badge for mods that pull in dependencies. A **pin** under the status dot means you have pinned that mod, so no mod list you apply will set it aside - see the Mod lists tab:

| Status | Meaning |
| --- | --- |
| Installed | You have it, and it's current |
| Update available | A newer version has been published |
| Disabled | You have it, but it's switched off - see the Disabling mods tab |
| Not installed | Available for your SPT version |
| Nothing compatible | The mod exists, but has no release for your SPT version |

Clicking a card opens its details, including version history and a link to its page here. Already have the mod and want a clean copy of it? Use **Redownload** - it fetches and reinstalls the version you're on, which is the quickest fix for files that got edited or corrupted.

#### information
SPT version constraints are resolved against the live SPT release list rather than parsed as version ranges, so what you see named is a release that actually exists - not the boundary version the mod author wrote the constraint against.


### Installed
Scans your SPT folder for what's actually there - client mods in `BepInEx\plugins` and `BepInEx\patchers`, server mods in `user\mods` - and matches them back to the catalog.

Same search and filters as Browse, plus filters for update status, enabled or disabled, and which group a mod is in. Each card shows the installed version, the latest published one, and the folder it lives in when that differs from the mod name.

Clicking any mod opens a dialog with:

- the full version history, with changelogs rendered from the mod page's own rich text
- a link to the mod page
- an **Update** button when an update applies, or **Redownload** when it doesn't

Mods can be removed from here too.

#### Mods you installed by hand
A mod this app installed is known exactly - its listing and version come from the install record. For one you installed yourself, the app reads what the mod says about itself: a client plugin's ID, name and version, and on SPT 4 a server mod's ID, name, version and the other server mods it needs. The ID matches it to its listing on sp-mod, whatever you named the folder. When nothing on sp-mod matches, the card is titled with the name the mod gives itself, with the folder shown as **Installed as**.

If a mod says it is a different version from the one you know you have, **Confirm as installed** in its details records the right one, and that wins from then on.

#### Three ways to look at the list
The buttons at the top of the page switch between them. They all show the same filtered, sorted mods - only the layout changes.

- **Cards** - the paginated grid. Turn on **Select** mode to tick several mods and act on them together.
- **Groups** - your own MO2-style separators. Make a group, drag mods into it, collapse the ones you're not working on, and enable, disable or invert a whole group in one click. Drag a mod to the top edge of the window and the list scrolls for you.
- **List** - one row per mod, scrolling continuously. Open a row for everything the app knows about that mod: its GUID, installed and published versions, install date, group, content flags, whether this app installed it or you did by hand, and the exact folders it occupies.

Sort by name, author or group. To see what you've just installed, pick **Installed in the last 7 days** in the update status filter - newest first. Every filter and sort applies to all three views.

#### information
Groups are yours to organise however you like - SPT never sees them. They do matter for one thing: disabling a whole group at once.

#### Patchers are shown with their mod
A BepInEx patcher belongs to a mod rather than being one, so a patcher folder is shown as part of the mod that placed it, labelled `Client + Patcher`, instead of turning up as a second entry. Patchers are usually named after their mod plus a word like `Patcher` or `Prepatch`, so that's how they're matched - `MoreBotsPrepatch` finds `MoreBotsAPI`. A patcher that can't be tied to a mod is still listed, labelled `Patcher only`.

Two things in `BepInEx\patchers` are deliberately left out, the same way core SPT plugins are: SPT's own preloader patcher, and general BepInEx utilities that mods bundle alongside themselves (currently FixPluginTypesSerialization). Neither is published on sp-mod, and neither is yours to manage from here.

#### warning
Mods installed with this app have every file they placed recorded, which is what makes a clean uninstall possible. Anything you installed by hand beforehand has no such record, so removing it takes its whole folder rather than a known file list.


#### Your configs aren't thrown away
If a server mod has config files of its own (`user\mods\<mod>\config\*.json`), removing it asks what you want done with them: keep them - they're moved to a timestamped folder under `Data\LegacyConfigs\`, with their original paths intact so the folder can be copied back over your SPT install - or delete them with the rest of the mod. Updating a mod always keeps a copy, without asking, and carries your settings into the new version - see the Configs tab.

Client mod settings live in `BepInEx\config`, outside the mod's own folder, so removing a mod never touches them.

#### Removing a mod can be undone
Remove doesn't delete anything straight away. The mod's files move to a hidden `.tcfmm-removed\` folder inside your SPT install, and **Undo** on the Installed page puts a removal back exactly as it was - with several held, it lists them so you can pick which, in any order. It never overwrites something that has taken a file's place since. How long removed mods are kept is up to you under **Options, Keep removed mods**: delete straight away, 1, 7, 14 (the default) or 30 days, or until you clear them; **Clear removed mods** frees the space now.

A removal only takes what is provably the mod's. It leaves a file that changed since it was installed, a file another installed mod also uses, and anything belonging to SPT, BepInEx or the game - and it puts back any file the mod had replaced when it went in. The result line says what was left and why. If the mod's folder has to stay because it holds files the mod didn't install - a note you added, say - the result says so, and that folder's card is marked as left behind by the removal.


### Disabling mods
Turn a mod off for a run without uninstalling it. Nothing is deleted, and nothing is lost.

Disabling moves the mod into a `.disabled` copy of the folder SPT loads it from - `user\mods` becomes `user\mods.disabled`, `BepInEx\plugins` becomes `BepInEx\plugins.disabled`. SPT doesn't look in those folders, so the mod simply isn't loaded. Enabling moves it straight back where it came from.

Its own files travel with it, server configs included, and client settings in `BepInEx\config` are never touched - so switching a mod off and back on again loses no settings.

**How to disable something**

- One mod, from its card, its List row, or its row in Groups view
- Several at once, by ticking them in Cards view's **Select** mode
- A whole group, with its **enable all** / **disable all** / **invert** buttons

Disabled mods stay in the list, dimmed and marked, and the enabled/disabled filter pulls up either set on its own.

**You get a warning before you break something.** If disabling a mod would take away something another mod depends on - or if you enable a mod whose own dependencies are still switched off - the app lists what's affected and offers to carry those along. Dependencies are read from the mods themselves, so this works offline and covers mods you installed by hand that were never matched to a listing here.

**Undo** puts the last change back.

#### information
Whether a mod is disabled is worked out purely from where it sits on disk. Move folders around by hand if you prefer - the app reads the result correctly on the next scan.


### Addons
Some mods have **addons** - extra content published against the mod itself rather than against an SPT release. A voice-command pack for a companion mod, a Linux build of a GUI, an overlay for a web tool.

Addons aren't listed separately, because an addon is no use without its parent. Open a mod - from Browse or from Installed - and its addons are listed at the bottom of the dialog, each with its own version picker and Install button.

The thing that makes an addon different from a mod is what it's measured against:

##### A mod version says: which **SPT release** it needs.
##### An addon version says: which version of its **parent mod** it needs

So the app checks each addon version against the mod you actually have installed. If none of them fit, the addon is still listed - with the reason, rather than quietly missing:

> Needs Raid Review ^1.5.0 - you have 1.4.2

Installed addons get their own card on the Installed page, labelled with the mod they belong to, and update the same way anything else does. **Mod lists cover them too** - capturing a list records your addons, and applying one installs, updates or disables them alongside your mods.

#### information
Installing an addon won't drag its parent mod in behind it - install the mod first, then its addon. Anything else the addon needs is offered the usual way, on the Downloads page. Applying a list that names an addon whose parent mod isn't installed (and isn't on that list either) skips it and tells you why, rather than downloading something nothing would load.

#### warning
A mod list containing an addon needs this version of the app or newer to open. Sharing one with someone on an older build will tell them the file is too new - send them a list without addons, or ask them to update. Lists with no addons in them are unaffected.

#### warning
**Update, Redownload and Remove are unavailable while a mod is disabled.** The record of what it installed points at folders it no longer occupies, so those actions would put files in the wrong place. Enable the mod first, then update it. Browse refuses a reinstall of a disabled mod for the same reason.

#### If a mod ends up in two places at once
A move interrupted partway - or one done by hand - can leave the same mod in both the normal folder and the `.disabled` one. When that happens the card says so and offers **Sort out**: pick which copy to keep, and the other is moved into a hidden `.tcfmm-duplicates` folder inside your SPT install rather than deleted. Undo puts that back too.


### Mod lists
A **mod list** is a named set of mods and the versions of them you run. Capture what you have now, switch between sets, send one to a friend, or follow the one a server publishes. It records mods and versions, never files, so capturing is instant however big your install is.

**Capture** saves whatever is enabled right now. **Preview** works out what applying it would do and nothing moves until you press **Apply**. The preview starts with coloured counts - *1 missing* in red, *2 incorrect versions* in amber, *1 disabled* in blue, *72 correct* in green, and grey for anything you have to fetch yourself, anything it would set aside and pinned mods. Every row below reads like the list itself - name, folder, version, scope - with a badge in the same colours and the icon the Installed page uses for that state. An incorrect version shows the move, *3.0.0 → 3.1.2*, and the Apply button says what it is about to do: *Install 1 mod*, *Install 2, update 1*.

**Apply** downloads what the list names and you do not have, enables what you have but had switched off, and moves anything the list does not name into the disabled folder. **Nothing is ever deleted.** Setting a mod aside is the same move the Installed page's disable button makes, and it is near-instant however large the mod is.

**Pin** the mods you want kept whatever list you apply - a HUD, a sound pack, the quality-of-life mods that don't belong on any one list. A pinned mod is never set aside: Preview lists it as **Pinned** instead, with a button to unpin it. Pin from that Preview row, or with **Pin** on the mod's opened card on the Installed page. A pinned mod shows a pin under its disable button on Installed, beside its name in any list's contents, and on its Browse card. Pins belong to your install and never travel with a list you share.

**Undo** puts the install back the way it was before the last apply. There is one undo point, kept up to date for you: applying a list replaces it, and using it clears it.

#### warning
The disable half needs the game and the server closed - BepInEx holds the files it has loaded open. Preview says so before you apply.

#### Editing a list
Three steps, and they are separate on purpose:

1. **Add mods** and the **X** on a row change the list on screen only.
2. **Save** writes the list. No downloads, nothing moved - the only thing it changes is what the list says.
3. **Apply** is the only thing on this page that touches your game folder.

**Add mods** offers what is installed here *and* the whole sp-mod catalog, so a list can name a mod this machine has never had. A mod added from the catalog is left unlocked - it means "the newest published version", because a version nobody here has run is not a version anybody has tested. **Refresh versions** re-reads the versions of every mod on the list that is installed here, for after an update round has moved a dozen of them.

Preview and Apply are switched off while there are unsaved changes, and the page says why: they work off the stored list, and running them against something the panel no longer agrees with is the one genuinely confusing state this page can reach.

#### Sharing a list
**Export** writes a `.tcfmodlist` file. Send it to whoever you like; **Import** reads it back.

What travels is a manifest - "install mod 2426 at version 5" - and never mod files. The receiving app downloads from sp-mod.com exactly as it would for any other install, which is faster than anything a person could serve you, costs the sender no bandwidth, raises no redistribution question, and keeps the mod author's download count honest.

- **A list you imported is read-only**, and so is one a server served you. **Make a copy** turns it into one of your own, pointing back at where it came from.
- **Mods the catalog cannot resolve** - GitHub-only mods, hand-built things - are listed by name rather than quietly dropped, so you know what to go and fetch yourself.
- **A locked version that has since been withdrawn** asks rather than failing: it offers the nearest version and lets you untick anything you would rather skip.

#### Who each mod is for
Every entry names the machines it is for. It is worked out for you at capture time from where the mod's files actually live, and shown on every row. The **scope button** cycles a row through the six, and the filter above the list matches one exactly - which answers the question you have when tidying a list: what have I already pruned, and what is still carrying the capture default?

| Scope | Who gets it |
| --- | --- |
| Server + Client + Headless | Everything. A mod with both halves, by default |
| Server + Client | The server and the players; not the headless |
| Client + Headless | Every machine running the game. A plugin, by default |
| Client only | Players only - HUD tweaks, sound packs, anything drawn at a person |
| Headless only | The headless box alone |
| Server only | The server's own mods - `fika-server` and friends |

What is in the name gets it; what is not, does not. Nothing on disk separates a bot overhaul from a HUD widget - both are a DLL in `BepInEx\plugins` - so a capture gives every plugin to the headless and you take away what it does not need. That direction is deliberate: a headless carrying a spare mod costs nothing anyone can see, while one missing an item or bot mod is felt by everybody in the raid it is hosting.

A **headless still takes Server only entries**. It is a full SPT install, and a server-only entry is a whole mod rather than half of one. **Server + Client** is how you say the server and the players need this and the headless does not.

#### information
Scope only ever narrows a list a server served you. **Your own lists describe your own install and apply whole** - which matters if you host and play on the same machine, see below.

#### If you run the server
Publishing needs the Server Map mod on the server - see the Server map tab. The steps here are the mod list half.

1. **Capture the list on the machine the server runs on**, so the server's own mods are in it and scoped `Server only` for you.
2. **Prune it for the people receiving it.** Take the headless off anything it does not need; leave the players everything they do.
3. **Publish to this server** writes it where the server serves it from. The list gets a red **Serving** badge so you can see which of a dozen personal lists is the one going out.
4. **Edit, Save, and publish again** to hand out a new version. The revision moves only when what you published actually changed, so republishing an unchanged list is a no-op and the number still means something.

On the joining side, a served list is stored read-only and marked as coming from that server. **Refresh from server** asks for it again whether or not the revision has moved - for a copy you doubt - and names what came back. It installs nothing; applying is still a separate step.

#### warning
**Do not apply your published client list on the machine that hosts.** A list of your own applies whole - every entry, whatever its scope - so one pruned down to what players need names none of your server's mods, and applying it there sets aside `fika-server`, SVM and the Server Map mod itself, which is the thing publishing the list. Nothing is deleted and **Undo** puts it straight back, but your server stops serving until you do.

The shape that works is **two lists on the server box**: the full one you apply there, and the pruned one you publish. They are cheap - a list is a manifest, not a copy of anything.

#### Following a server and your own list at once
Both can be active. Following a server does not cost you the personal list you were already following, and applying a list of your own will not sweep away what the server requires - the mods it asks for are spared.

A list a server hands you **never disables anything**, whatever its author chose. An operator writing a list is describing their install; applying that verbatim on your machine would set aside mods the server has never heard of and has no opinion about.


### Configs
Edit your mods' settings without leaving the app. The page finds every config file your installed mods create:

- **Client** - the files in `BepInEx\config`, one per plugin.
- **Server** - the files inside a server mod's own folder under `user\mods`.
- **BepInEx** - BepInEx's own settings, which are not a mod's.
- **Unclaimed** - a `BepInEx\config` file no installed plugin answers to. Almost always a mod you removed at some point, leaving its settings behind.

Each row shows the mod and the file's path inside its folder, so the twenty mods that all ship a `config.json` are never mixed up. There is a search box and a filter to narrow it to one kind.

The two halves behave differently, and the page says so above the editor: **a client mod's settings live outside its folder**, so they survive disabling and removing it; **a server mod's settings live inside its folder** and travel with it.

#### Editing is careful with your files
- **Nothing is ever reformatted.** For a lot of mods the comments in the config file are the only documentation those settings have. Saving changes the lines you changed and leaves everything else exactly as it was.
- **Broken JSON is refused, not written**, with the line that stopped making sense. Comments and trailing commas are fine - plenty of server mods ship both.
- **Every save keeps a copy of what was there first**, in `Data\config-backups\` inside the app's own folder, one timestamped folder per save. Copying one back over your install undoes a round of edits.
- **If the file changed underneath you** - the game wrote it, or you edited it in Notepad - the save stops and asks whether to overwrite, reload or leave it. Whichever you pick, what is on disk is copied aside first.
- **Shipped defaults** (`config.default.json` and the like) are listed but dimmed: they are the pristine copy, so editing one does nothing. They are shown rather than hidden because they are what you look at when you want the original value back.

#### warning
Changing a mod's settings is your call. If a change breaks the mod or your game that is not the author's problem to put right - only change settings you understand, and say so if you report a bug after editing a config.

#### information
BepInEx writes its config files back out when the game closes, so a client config edited mid-session is likely to be undone. A server config takes effect on the next server restart.

#### Your settings are carried across an update
Updating a server mod used to leave you with its defaults. Now the new version's config file is used **with the settings you changed carried into it** - so a setting the update adds arrives at its new default, a default the author changed reaches you, and the values you picked stay yours. Your file as it stood is still copied into `Data\LegacyConfigs\` first, every time, so nothing rests on the merge being right.

The file is the new version's text, edited in place: the author's comments, key order and layout are theirs, and only the values you had changed are swapped in.

**What an update does with a mod's configs is a per-mod choice**, set on this page while one of that mod's config files is selected, and shown read-only next to **Update** in the mod's details dialog:

- **Merge my changes** - the default, described above.
- **Keep mine** - your file is left exactly as it is. Right for a mod whose config the app can't read (see below); the cost is that a setting the new version adds will be missing, which some mods mind and some don't.
- **Take the new file** - the new defaults win. Your copy is still in `Data\LegacyConfigs\`.

After an update, the Configs page says what happened to the file you are looking at, and the Downloads list says it as it happens.

#### Mods that keep things somewhere unusual
Some mods don't follow any convention, so three buttons sit under that dropdown - all per mod, all remembered in `Data\mod_configs.json`:

- **Files I authored...** - a folder inside the mod holding files *you* wrote rather than settings the mod shipped. SVM's `Presets\` is the example. An update leaves the folder exactly as it is, and removing the mod keeps it like a config instead of deleting it. A folder holding hundreds of files is refused - that is a database, not your work.
- **Settings kept elsewhere...** - a settings file the mod keeps where nothing would look for one, so updates carry your changes into it. **SVM's `Loader\loader.json` is set up for you**: it holds which preset the server runs, and before this it was replaced on every update, quietly putting the server back on the default preset.
- **This isn't a config** - for the opposite case: a locale table or a barter list sitting in a folder literally called `config`. It stops being listed here, stops being merged, and is no longer kept when the mod is removed.

SVM's entry is filled in for you as a starting point, not a rule - clear either half and it stays cleared.

#### information
Three things a merge deliberately does not do. **A setting you added yourself** that neither version ships is reported rather than put back - there is no honest place to insert it. **A setting the new version has dropped** goes with it. And **a value list (a JSON array) is carried whole** rather than element by element, because there is no correct way to combine two lists.

#### warning
The first update of any mod after this feature arrived has nothing to compare against - the app has to have recorded what the previous version shipped, which it now does at every install - so that one update replaces the file and says so. From its next update on, that mod merges normally. A config the merge can't read (JSON5, or one big enough to be data rather than settings) is replaced too, and says which it was; set that mod to **Keep mine** if you edit it.

### Dependencies and Conflicts
**Conflicts** come first: mods that will fight when the game loads. Worked out from your install alone, so it works offline and for mods you installed by hand, and checked again every time you open the page.

- **The same plugin installed twice** - two folders registering one plugin, such as `SAIN` and `SAIN.4.4.3`. BepInEx loads only one, and you don't get to choose which.
- **The same server mod installed twice** - SPT skips a server mod whose ID it finds twice, so **neither copy loads**.
- **Different copies of one file** - two mods each shipping their own, different copy of a library. Only one copy loads, for everyone. Identical copies are fine and aren't listed.

Each lists every mod involved with the folder it's in, and a button to open it. For a mod installed twice, **Keep this one** keeps that copy and removes the others the normal way - so **Undo** on the Installed page can put them back. A mod in a conflict shows a red status on the Installed page, where **Show - Has conflicts** narrows the list to them and the status line counts them with a link here. The Play page warns too, without stopping you launching.

**Dependencies** resolves the dependency tree of every installed mod that declares one, and reports each dependency's state against what's actually on disk.

That includes **version conflicts** - where two installed mods want incompatible versions of the same dependency - which is the failure mode that usually shows up as an unexplained crash on load rather than an error message. Dependencies you've disabled are called out as disabled rather than missing.

Anything missing can be installed straight from the list.


### Mod footprint
An optional page - **off by default**, turned on under **Options - Mod footprint page** - with one row per installed mod, showing what each mod's files contain. Open a row for the full breakdown; rows sort by patch classes, components or size.

**Nothing is measured.** The app does not launch the game, load a mod or run any mod's code. Nothing is timed or profiled, no frame rates are reported, and there are no scores, ratings or rankings. Everything is read from the files on disk:

- **Patch classes shipped**, and components the engine would call on a timer - per-frame updates, physics steps, on-screen interface drawing, camera hooks - broken down by which.
- **Asset bundles** and their size, held in memory once loaded.
- **Whether it ships a preloader patcher**, which runs before the game loads.
- **Whether it has a server half** under `user\mods`, counted separately and never folded into the client figures.
- **Disk usage**, in files and megabytes.

#### What the figures do not cover
- It counts patch classes, not patched methods. One class can target several methods, so the real number can be higher.
- It cannot see what a patch touches. A patch on a per-frame method and a patch on a menu button are counted the same way.
- A declared component only runs if the mod creates and enables one, which the files do not show. Every line says *declares* or *ships*.
- Some mods are packed or obfuscated in ways it cannot parse. The breakdown says so rather than guessing.

#### information
Readings are cached in `Data\mod_footprints.json` and a mod is re-read only when its files change; **Rescan** forces a fresh read. Disabled mods are included and marked as not loaded. Nothing is uploaded - the page makes no network requests at all.


### Server map
An optional page - **off by default**, turned on in Options - that connects to an SPT server running the **Server Map mod** and shows what that server runs, so you can be ready before you launch rather than after a raid fails to load.

- **The mod goes on the server**, not on your machine. It is a separate download, published as an addon of this mod - **Options - Server map connection - Get the Server Map mod** opens its page - and a player joining a server needs none of it.
- **Nothing about your install is sent without asking.** Connecting asks the server who it is. With the Server Map mod 0.2.0 the page asks once whether to **Share** this machine on the server's map; say no and nothing is sent. **Options - Server map connection** has the same answer as a switch, and the name the machine shows.
- **The server serves a list, never files.** Mods are still only ever downloaded from sp-mod.com. A server that could push files at you would break the one rule this app is built on, so there is no route for it to do so.
- **Certificates are pinned on first use.** SPT serves a self-signed certificate, so the app remembers the exact one your server presented and tells you if it ever changes - which is what a machine-in-the-middle would look like. Trust the new one or refuse it.
- **A shared key** guards everything but the handshake. The operator gives it to you; on the server's own machine the app finds it by itself.
- **LAN-only, for the operator.** On the server machine, **Options - Server map connection - Only answer this network** makes the server refuse every request from outside its own network - Tailscale peers still count as inside.

**Connecting fetches the list for you** and saves it as a read-only mod list, marked as coming from that server; it is fetched again on its own whenever the server's revision moves. **Fetch again** asks for it even when the revision hasn't moved, for a copy you have edited or deleted.

Saving is not applying. From there it is an ordinary list - preview it on the Mod lists page, apply it, keep your own alongside it - and the Play page's check compares against it before you launch.

**The map** lists every machine that shares itself with the server, the server first and yours marked *This machine*: whether each is in game, has the app open or was last seen some time ago, and whether it has what the server's list asks for - named, with versions, in the usual status colours. When your own card is behind, **Review and install** takes you to the fix. Only machines running this app appear, and the map needs the Server Map mod 0.2.0 on the server.

#### If you run the server
The Server Map mod's own page carries the operator guide - installing the payload and the stub for your SPT line, where the key and the published list live (`TCFModManager\Data\ServerMap\`), rotating the key, and opening a port. The mod list half - capturing, pruning, publishing, and the one thing not to apply on the machine that hosts - is on the Mod lists tab.

#### warning
Client and server must be on the same SPT line: a 4.1 client cannot join a 4.0.13 server, and that is SPT's rule rather than this app's. A published list only ever reaches people already on your version.

### Downloads
The install queue. Items process one at a time; each resolves its dependencies and queues those alongside it.

- Live progress per item
- **Cancel** on any individual item - cancelling a mod also cancels the dependencies it dragged in
- **Clear finished** to tidy up
- Plain archive downloads, for when you'd rather install something by hand

#### How an install actually runs
The archive is downloaded and extracted into a hidden scratch folder inside your SPT install (`.tcfmm-work\`, swept of stale runs each time), then moved into place.

SPT's, BepInEx's and the game's own files are never replaced. When a mod replaces a file you put there yourself - a hand-installed mod's, say - the original is kept and goes back if the mod is removed. The result line says what was left as it was.

When you're updating, the previous version is only removed **after** the new one has downloaded and extracted successfully - a failed or cancelled download can't leave you with neither. Once files start being placed, the operation runs to completion rather than tearing out a half-installed mod.

#### warning
Installing and removing both refuse to start while **T***** or the SPT server is running** - those hold the very files being replaced. Close them first. The check runs again after the download finishes, in case SPT was launched while it was in progress.

#### information
Before anything is queued, the app asks you to open the mod's page here on sp-mod first - same as installing manually, and it keeps mod authors' page views and instructions in the loop.


### Monitor mode
For when you would rather install mods yourself. Instead of installing, the app downloads each mod's archive from sp-mod into a folder you choose and leaves the installing to you. Browsing, update checks, dependencies and mod lists all keep working.

**Turning it on.** In **Options - Monitor mode**, change **Install mods for me** to **Download only - I install them myself**. The **Download folder** below it is where archives go; leave it empty to use your Windows Downloads folder, wherever Windows keeps it. The app never deletes anything it saves there.

**One mod the other way round.** Whichever you pick, every install button has a smaller button beside it that does the opposite for that one mod - download just this one while the app installs everything else, or have the app install just this one while you download the rest. It is on Browse cards, in the update dialog, on addon rows and on the Dependencies and Conflicts page. Hover it to see which way round it is.

**What a download does.** It goes through the Downloads queue like any install, and its card says where the file was saved, with a **Show in folder** button. Nothing inside your SPT install is touched, so you can download while SPT is running. Any dependencies it offers to fetch are downloaded the same way. Files keep the name sp-mod serves them under and are never overwritten - a second copy is saved as `Name (2).zip`. An archive the app can't recognise as an SPT mod is still saved, and the card tells you to follow the mod page's install steps.

#### Once you've installed it
The next time the **Installed** page scans, it checks each download against your install. When every file in the archive is on disk at the size the archive says, the download counts as installed:

- **By default, you're asked.** A window after the scan lists every download that now looks installed, all ticked. **Mark installed** records the ones you leave ticked; any you untick aren't asked about again until you download a newer version. **Not now** leaves them all waiting, and won't ask again until the app is next started.
- **Or just note it.** Under **When a downloaded mod shows up installed** in Options, pick **Just note it on the mod's card**, and the mod's opened card says so and gets a **Confirm install** button instead.

Confirming only tells the app which version you have, so update checks and dependencies are right - nothing on disk changes. When only some of the files match - part of the mod is missing, or the old version is still there - the card says so and it is never confirmed. **Show - Downloaded, not confirmed** lists every mod in either state.

#### Mod lists in Monitor mode
Applying a list downloads what it would install or update, and says so in the preview before anything runs. The downloads go into a folder named after the list, inside your download folder - the switch under **Download folder** in Options puts them straight into the download folder instead. Mods the list enables or disables are still moved, since that places nothing new. Applying the list again doesn't download anything that is still waiting for you to install.

#### warning
**Your settings are only carried across an update the app does itself.** Copying a new version over your install by hand replaces the mod's config files with the author's defaults. The queue card warns you when a download holds config files you already have - back yours up first, or copy only the files you need.

#### information
Everything still comes from sp-mod. Monitor mode changes who places the files, not where they come from - it does not install archives from anywhere else.


### Update notifications
Off until you switch it on. With it on, the app asks sp-mod every so often whether any mod you have installed has a new release, and shows a Windows notification when one has - so you hear about an update without going to look for it. It works the same whether the app installs your mods or Monitor mode has you install them.

**Turning it on.** In **Options - Update notifications**, switch on **Notify me when an installed mod has an update**, and pick how often under **Check every** - from 30 minutes to 12 hours, every hour to start with. The first check runs one interval after the app starts, never at launch. **Check now** runs one straight away and says underneath what it found.

**What gets announced.**
- **Only what you have installed is asked about** - one request per check for the lot, not a trawl through the whole catalog. Addons are checked too.
- **An update means what the Installed page means by it:** the newest release that runs on your SPT. A notification never names an update the Installed page doesn't show.
- **Each release is announced once.** Restarting the app doesn't repeat it; a newer release of the same mod is news again.
- **Switching it on doesn't announce what's already there.** The first check notes every update the Installed page already shows, and from then on only new releases are announced. Updates that come out while the app is closed are announced on the first check after it starts.
- **Disabled mods aren't announced**, and neither is an update you've already downloaded in Monitor mode but not installed yet.
- **One notification per check**, however many it found - "3 mod updates available: SAIN 4.5.2, UI Fixes 6.0.2 and 1 more". A newer one replaces an older one still waiting in the Notification Centre.

**Clicking it** - the notification or its **Open** button - brings the app forward on the **Installed** page with **Show** set to **Needs update**. If the app has been closed since, clicking it starts the app on that page. There's no "update all" on the notification: updating goes through the app, where the running-SPT check and the dependency prompt can do their jobs.

When a check finds something, the Installed page and Browse's status dots pick up the new version without a refresh.

#### Keeping it running in the tray
Closing the window normally quits the app, and the checks stop with it. Under the notifications switch, turn on **Closing the window keeps the app running in the tray** and closing hides the window instead, leaving the app's icon in the notification area by the clock. The first time it happens, a notification says so. The switch is only available while notifications are on.

- **Click the icon** to open the window again.
- **Right-click it** for **Open**, **Check for updates now** and **Quit**. While this is on, Quit is how you actually close the app.
- **Launching the app again** while it's in the tray just brings the window back.

#### information
Only one copy of the app runs from a folder at a time - launching it again brings the running one forward instead of opening a second. A separate copy kept beside a second SPT install still runs alongside it.

#### warning
Windows has the last word on notifications. If none appear, check that TCF Mod Manager is allowed under **Windows Settings - System - Notifications**, and that Do not disturb isn't on. Windows also holds notifications back while a game is full screen, and the app doesn't try to get around that.


### App updates
The app has its own page here on sp-mod, the same as everything else you install through it. On launch it asks that page whether anything newer has been published. If there is, you get a banner and a badge on the **App update** item in the sidebar. That page is always there, with a **Check now** button, whether or not an update is waiting.

All of it goes through sp-mod: the check reads the public API, the download is the file this page's own Download button serves, and - exactly as with any other mod - **you're asked to open the mod page before anything is downloaded**.

**What the version number tells you.** The update page names the kind of change rather than leaving you to work it out:

| Change | Means |
| --- | --- |
| `x.x.`**`1`** | **Bug fix.** Fixes to how the current version already works. Nothing new to learn - safe to skip if nothing is broken for you. |
| `x.`**`1`**`.x` | **Feature update.** Something new, or something works differently. Worth reading the notes. |
| **`1`**`.x.x` | **Major update.** Significant changes. Read the notes and the mod page first. |

Closing the banner skips that release - it won't come back for that version, though anything published later will.

**How the swap works.** A running program can't overwrite itself, so this is done from outside it. The new release is downloaded into a hidden `.tcfmm-update\` folder next to the exe and checked before anything else happens. The app then starts a small script, closes itself, waits until it has fully exited, copies the new build in, and starts it again.

The copy only adds and replaces files - it never mirrors the folder - so `Data\`, `Staging\` and `LegacyConfigs\` are left exactly as they were. Your SPT path, install history and kept configs all survive an update.

#### information
**If anything goes wrong, the version you already have is left alone.** No write access, not enough disk space, a copy that doesn't complete - in every case the app comes back as it was, and the new build stays in `.tcfmm-update\payload\` so you can copy it over by hand. What happened is written to the app's log on the next launch.

#### warning
If the app lives somewhere Windows won't let it write to - inside `Program Files`, typically - it tells you up front instead of trying, and points you at this page. Keeping it in your SPT folder avoids this entirely.

The app doesn't list *itself* on the Browse page, since installing it into `BepInEx\plugins` would just leave SPT trying to load a second copy of the manager. Nothing else about Browse changes.


### Files & logs
Everything lives next to the exe:

| Path | What |
| --- | --- |
| `Data\settings.json` | SPT install path and app settings |
| `Data\installed-mods.json` | What this app installed, and every file it placed |
| `Data\mod_cache.json` | Cached catalog |
| `Data\spt_versions.json` | Cached SPT release list, refetched daily |
| `Data\dependency_flags.json` | Per-mod "has dependencies" answers, re-checked when a mod publishes |
| `Data\mod_groups.json` | Your groups, and which mod is in which |
| `Data\mod_lists.json` | Your mod lists, which ones you follow, your pinned mods, and the single undo point |
| `Data\downloads.json` | Monitor mode: each archive saved for you to install, what it would place, and whether you've confirmed it |
| `Data\update_notifications.json` | Update notifications: which releases have already been announced, so none is announced twice |
| `Data\addon_cache.json` | Cached addon catalog |
| `Data\mod_footprints.json` | Cached footprint readings, only if that page is on |
| `Data\config-backups\` | One timestamped folder per config save, laid out like your install |
| `Data\logs\tcfmm-<date>.log` | Daily log |
| `Staging\` | Default destination for manually downloaded archives |
| `Data\LegacyConfigs\` | Config files kept from removed and updated mods, one timestamped folder each |
| `Data\overwritten\` | Files an install replaced that weren't another installed mod's, kept to put back when that mod is removed |
| `Data\ConfigBaselines\` | A copy of the config files each mod version shipped, which is what lets an update tell your changes from the author's |
| `Data\mod_configs.json` | Your per-mod choice of what an update does with that mod's configs, and any unusual places it keeps them |
| `Data\ServerMap\` | On a server: the shared key (`servermap-key.txt`), the list it publishes, and the map's machines (`clients.json`) |
| `.tcfmm-update\` | Hidden. Only exists while an app update is downloading, or if one failed; cleaned up on the next launch |

Three more folders are created inside your **SPT install**, all hidden: `.tcfmm-work\` (scratch space while a mod installs, swept each run), `.tcfmm-duplicates\` (copies set aside by **Sort out**, kept until you delete them) and `.tcfmm-removed\` (removed mods, kept for Undo as long as **Keep removed mods** says).

`Data\installed-mods.json` - not folder names, not DLL file versions - is the authority on what's installed and at what version.

#### Logging
Info level by default, rotated daily as `tcfmm-<yyyyMMdd>.log`. To get Debug-level output in the same log, drop an empty file named `verbose` - no extension - next to the exe.


### Limitations

#### What it can and can't see
- **Mods nested a folder deeper** - `BepInEx\plugins\Author\ModName\mod.dll` rather than `BepInEx\plugins\ModName\mod.dll` - are listed under the outer folder's name with an unknown version.
- **Mods you installed by hand are matched by the ID they declare, then by folder name**, since there's no install record to read. If neither clearly points at one listing, the mod shows as not found on sp-mod: you can still see, group, disable and remove it, but not update it from here. A folder name that could plausibly be two different mods is deliberately left unmatched rather than guessed at.
- **A mod's version is only as good as what it declares.** Some authors forget to update it, and some mods work theirs out while the game runs, which the app can't read - those fall back to the DLL's file version. Either way, **Confirm as installed** puts it right.
- **A listing can number its releases differently from the mod it installs.** A helper mod declaring 0.0.7 on a listing whose releases are 4.x shows an update that never clears. Confirm as installed ends it.

#### Installing
- **Archives have to be packaged normally** - a `BepInEx\`, `user\`, `SPT\` or `SPT_Runtime\` folder at the top, optionally inside one wrapper folder. Anything else is refused with a message telling you to install it by hand, rather than being scattered into your install.
- **Everything in the archive gets installed.** Mods that ship optional variants in separate folders, or a readme, get all of it copied in. Choose-your-variant mods are worth installing by hand.
- **If two mods ship the same file, the second one installed wins.** Removing either one leaves the file for the other.
- **Mods installed before v1.19.0 are only fully protected after their next update or reinstall.** Until then their record has no fingerprints: removing one also takes a file you changed since (into `.tcfmm-removed\`, so it can be got back), and leaves its files in the install root or `EscapeFromTarkov_Data\Managed` in place.
- **You need roughly twice the archive's size free** on the SPT drive - the download and extraction are staged there before anything is placed.
- **Very large mods on a slow connection can time out** and have to be started again; downloads don't resume.

#### Groups and disabling
- **Groups follow folder names.** Renaming a mod's folder drops it out of its group - and out of anything you then disable by group. Drag it back into the group to fix it.
- **Copies set aside by "Sort out" stay put.** They sit in `.tcfmm-duplicates` inside your SPT install until you delete them; nothing prunes that folder for you.
- **Disabling doesn't reorder anything.** A server mod's `loadBefore` / `loadAfter` ordering relative to the mods still enabled is left to SPT.
- **Update, Redownload and Remove don't work on a disabled mod** - enable it first.

#### Versions and compatibility
- **Compatibility is judged from a mod's most recent releases**, not its whole history. A mod whose newest releases target a later SPT than yours reads as incompatible even if an older release of it would work - check the mod's page in that case.
- **Some version constraints can't be read.** Those mods show "SPT version unknown" and aren't filtered out, on the grounds that hiding something that might work is worse than showing it.
- **Beta and pre-release version numbers** aren't compared precisely, so an update may not be flagged for a mod you're running a pre-release of.
- **The catalog only covers SPT 3.10 and newer.** On older SPT, most of what you could install won't be listed.
- **"Installed" dates** come from the folder's creation date, so a mod updated in place still shows when you first installed it.

#### Mod lists
- **A list records mods and versions, never files.** Anything it names has to be gettable from sp-mod for the receiver to install it; mods that aren't are listed by name so they can be fetched by hand.
- **Scope only narrows a list a server served you.** Your own lists apply whole on the machine that owns them - which is why a server box wants its own list as well as the one it publishes.
- **There is one undo point**, replaced by each apply and cleared by using it. It puts mods back where they were; it is not a history.
- **An addon that ships inside its parent's folder can be installed and updated by a list, but not set aside by one** - there is no folder of its own to move. Disabling the parent takes it along.
- **A list you edited but never applied exports under its old revision number**, since a revision counts an apply. Apply before sharing if you want the receiver's copy to read as newer.

#### Configs
- **The first update of a mod after this app version can't merge.** Nothing recorded what its previous version shipped, so the update takes the new file and says so; from its next update on it merges. A copy of your file is always kept in `Data\LegacyConfigs\`.
- **A JSON5 config, or one large enough to be data rather than settings, is not merged** - it is replaced and reported. Set that mod to **Keep mine** if you edit its config.
- **A setting you added yourself is reported, not carried.** So is one the new version has dropped.
- **Settings kept somewhere unusual are only recognised once you say so** - a `Presets\` folder rather than `config\`, for instance. The three buttons on the Configs page are how you say so, and SVM comes set up already.
- **A folder of your own files is matched by name.** Renaming the mod's folder loses its entry, the same way a mod group does.

#### Monitor mode
- **Your settings aren't carried across a hand install.** Config protection only runs on an install the app does; copying an update over by hand replaces the mod's configs.
- **A hand install is recognised by file size, not contents.** A file of the right size is taken as the right file.
- **A mod installed somewhere other than where its archive lays it out isn't recognised** - into a different folder, or straight into a disabled one. It stays waiting; **Confirm as installed** in the mod's update dialog records the version by hand.
- **"Not now" lasts until the app is closed.** The next time it starts, anything still waiting is asked about again.

#### Update notifications
- **Checks only run while the app does.** Closing the window ends them unless the app is kept in the tray, and it doesn't start with Windows.
- **A mod you installed by hand can be announced once too often.** With no install record, its version is read from what the mod declares, and a declared version that lags behind the real one reads as out of date. It's announced once at most, and confirming the version on the mod's card ends it.
- **Checks are at least 30 minutes apart.** **Check now** is there when you want one sooner.

#### Server map
- **Client and server must be on the same SPT line**, which is SPT's rule rather than this app's.
- **The server publishes a list, not files.** The page reports this machine to the server's map only after you say **Share**, and only to that server.
- **Only machines running this app appear on the map.** A player who installs by hand is invisible to it.
- **A server's certificate is pinned on first connect.** If it changes you are asked before anything else happens, because that is also what an interception would look like.

#### Scope
- **One SPT install at a time.** The record of what's installed belongs to the app, not to the install it points at, so pointing Options at a second SPT folder will carry the first one's records across. Use a separate copy of the app per install.
- **The catalog refreshes once per session** in the background. Mods published while the app is open won't appear until you press Refresh cache or restart - except the mods you have installed, which update notifications re-read on every check while they're on.
- **It won't run while SPT does.** Installing or removing anything with T***** or the server open is refused, because those lock the files being replaced.


### Troubleshooting

#### The SPT version shows as unknown
Options needs your SPT install root - the folder with `EscapeFromTarkov.exe` and `BepInEx\` in it. The server exe (`SPT.Server.exe`, or `Aki.Server.exe`) is found from there, at the top or under `SPT\` / `SPT_Runtime\`, but not deeper.

#### Mods went into `SPT\SPT\...` or `SPT\BepInEx\...`
The install folder was set to the server folder - `SPT\` on 4.0, `SPT_Runtime\` on 4.1 - instead of the install root, so everything installed went one level too deep. Before v1.13.2 Options accepted that without complaint; from v1.13.2 the setting is moved up to the install root by itself the next time the app starts. Mods that already went into the wrong place stay there: look inside the server folder for a stray `BepInEx\` or a second `SPT\` / `SPT_Runtime\` / `user\` folder, and move what is in them into the real `BepInEx\plugins` at the install root and your server's `user\mods` - or delete them and install those mods again.

#### A mod I know exists shows "nothing compatible"
The mod has no version published for your SPT release line. That's a statement about the mod page, not about your install - check the mod's own versions list.

#### Downloads suddenly stall or fail
The sp-mod API is rate limited at the edge (roughly 40 requests per 10 seconds, 200 per minute). Heavy browsing can hit it. Give it a minute and retry; nothing is cached as a wrong answer in the meantime.

#### A mod I installed by hand isn't listed under Installed
It isn't in `BepInEx\plugins`, `BepInEx\patchers` or one of the `user\mods` layouts - see Limitations for the folder shapes that aren't scanned. A mod that *is* listed but shows as not found on sp-mod is there, just unmatched.

#### The Update or Remove button is greyed out
The mod is disabled. Enable it first - see the Disabling mods tab.

#### A mod I disabled is still loading in game
Check it isn't installed twice. If the same mod sits in both the normal folder and the `.disabled` one, its card says so and offers **Sort out** to keep one copy and set the other aside.

#### "Close T***** / SPT.Server before installing a mod"
Exactly what it says: those hold open the files being replaced. Close the game and the server window, then try again.

#### An install failed halfway
The files placed before it failed are recorded, so they stay under the app's control - install the mod again to complete it, or remove it to clear them out. The scratch folder `.tcfmm-work\` inside your SPT install is swept on the next run. The usual cause is SPT being started mid-install.

#### An app update didn't go through
Your existing version is untouched and still works - that's by design. The new build is sitting in `.tcfmm-update\payload\` next to the exe if you want to copy it over by hand, and `Data\logs\tcfmm-<date>.log` says what stopped it. The usual causes are no write access to the folder (move the app out of `Program Files`) and not enough free disk space.

#### I applied a list and my server's mods switched off
You applied a list that names none of them - usually the pruned one meant for players - on the machine that hosts. Nothing was deleted: press **Undo** on the Mod lists page and everything comes straight back. Keep two lists on that machine, one to apply there and one to publish.

#### The list from my server never updates
Press **Refresh from server** on the list, on the Mod lists page. It asks again whether or not the revision has moved. If you are the operator and clients aren't seeing a change, publish again after saving - publishing is what hands the new version out.

#### My headless installed a pile of mods it doesn't need
It is reading as an ordinary player. **Options - What this machine is** says whether anybody plays there and whether it runs a headless client; a served list is only trimmed once that is answered. If the app never asked, it didn't find `FikaHeadlessManager.exe` at the top of the install folder - point **Options - Fika headless launcher** at it.

#### No update notifications appear
First make sure a check has actually run: press **Check now** in **Options - Update notifications** and read the line under it. The first check after switching on only notes what's already there, so it never shows one. A check is also skipped while downloads are running, while no SPT install is set, and while sp-mod can't be reached - the log's `Updates` lines say which. If checks are finding updates and still nothing appears, Windows is holding them back: check **Windows Settings - System - Notifications** allows TCF Mod Manager, and that Do not disturb is off.

#### Reporting a bug
Grab `Data\logs\tcfmm-<date>.log` - ideally after adding the `verbose` marker file and reproducing the problem - and open an issue on the new [issues tab](https://sp-mod.com/mod/2945/tcf-mod-manager#issues).


### Translating
**Options - Language** picks the language the app's own text is read in. **System default** follows whatever display language Windows is set to.

Five languages ship: English, Deutsch, Français, Italiano and Русский.

#### warning
**Four of those five are machine translations and no native speaker has checked them.** They were made so that the app is usable in your language today rather than never - the alternative was English only, waiting for a volunteer to translate 1,290 strings from nothing. Expect to find wording that is clumsy, a term that isn't what your community actually says, and the occasional sentence that is simply wrong. The language picker in Options says which language a translation came from a person and which from a machine.

Mod names, descriptions, changelogs and category names come from sp-mod and stay in whatever language their author wrote them in, so the app being in your language does not put the catalog in it.

#### Found something wrong? That is the useful thing
You do not need git, an editor, or the whole file. One bad line is worth reporting on its own.

Open an issue on the new [issues tab](https://sp-mod.com/mod/2945/tcf-mod-manager#issues) with three things:

1. Which language.
2. What it says now - a screenshot is perfect, or just the text.
3. What it should say.

That is it. A dozen of those from someone who actually speaks the language is worth more than a whole new translation, and it is how these four stop being machine output.

#### Adding a language that isn't there
Rarer, and bigger, but no tooling from here and no build environment.

1. Take `src/TCFModManager.App/Localization/Strings.resx` from [the repository](https://github.com/TheCrimsonFckr/TCFModManager).
2. Open it in **ResXResourceManager** - the [standalone build](https://github.com/dotnet/ResXResourceManager/releases), not the Visual Studio extension. It shows every language side by side in a grid, one row per string, so you can read the English and the four existing translations while you fill in yours.
3. Add a column for your language and work down it. Most entries carry a note saying what the string is for and what each `{0}` gets replaced with.
4. Send the `Strings.<tag>.resx` it generates back: a pull request if you use git, the file attached to an issue if you don't. Either is fine.

#### warning
Leave anything in braces exactly as it is - `{0}`, `{1}`, and the part after a colon in `{0:N0}`. Those are where a count, a name or a file path is dropped in, and the bit after the colon is what formats a number for your region. The words either side of them can move anywhere your language needs them.

#### information
You don't have to finish, and you don't have to keep up. A string you haven't translated is shown in English, so a half-done language is a part-English app rather than a broken one - send what you have. The same holds after a release adds new text: your language carries on working and only the new strings read in English until somebody gets to them.

#### Things to keep in mind
**Counted sentences are split by form.** "1 mod installed" and "3 mods installed" are separate entries whose keys end `_one` and `_other`. English needs two forms; Russian needs three and already has them in this file, Czech four, Arabic six. Say which your language needs when you send it - the extra forms are a small change here, and the key names were built to take them.

**Two entries are not translations.** `Meta_LanguageName` is your language's name written in your language, the way the dropdown should list it - `Deutsch`, not `German`. `Meta_TranslationCredit` is yours: put your name in it and it appears under the language picker in Options.

**Some English in the app is deliberate.** The diagnostic log stays English so a log file means the same thing whoever reads it, and a handful of strings the app matches on rather than displays are kept out of the file entirely.


### Planning
- Mod lists / profiles - *Completed*
- Mod list sharing and handling (if you have played Arma modded or Total War modded, think like that) - *Completed*
- Mod syncing, getting on the same level as the server you are joining - *Completed*
- Fika headless support, so a headless is served only what it needs - *Completed*
- Window default sizes - *Completed*
- Default filtering and page defaults - *Completed*
- Config protection, so updating a mod stops replacing your settings - *Completed*
- Per-mod pins, so a mod you always want on survives any list you apply - *Completed*
- Server mapping, with who is on the server and where each machine stands - *Completed*
- Local languages based on OS - *Initial release, ongoing*
- Options to use app to monitor mods rather than manage them - *Completed*
- Update notifications, with the app kept running in the tray - *Completed*
- Additional mod footprint monitoring to show data around their usage on 
your systems - *Planning/developing*


### New Issues Feature (beta) 
If you find an issue, be it bug, spelling, language, want to request a feature or ask a question please use the new [Issues Tab](https://sp-mod.com/mod/2945/tcf-mod-manager#issues).
When reporting an issue please grab the `Data\logs\tcfmm-<date>.log` - ideally after adding the `verbose` marker file and reproducing the problem.


### Disclaimer
I use AI to help write this mod, everything is initially written by myself (I'm a
developer by profession), then passed though Claude (Fable model) for refinements and to highlight any additional changes the LLM thinks are worth making. Absolutely everything is reviewed and tested by myself before I commit anything. This is done because I simply do not have enough time in day to make full refinements and multiple passes myself. If you are not
comfortable using an application that uses AI as a tool then please do not download it.

{.endtabset}
