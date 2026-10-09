# Options

The gear at the top right. Each setting has a one-line explanation and a **?** with the rest.

![Options](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/14-options.png)

## General

| Setting | What it does |
|---|---|
| SPT Install Folder | Your SPT **game** folder, with `EscapeFromTarkov.exe` in it. Its SPT version sets Browse's version filter and flags mods that don't run on it. |
| Language | The app's own text. Mod descriptions stay in their author's language. |
| Window | How the window opens: remember the last size and position (default), or other choices. F11 switches full screen at any time. |
| Starting the game | Start server also opens the SPT launcher once the server is up; run the server without its window. |
| Picture behind each subscribed mod | On by default. On Subscribed items, each mod's own picture, out of focus, sits behind its card or row, as Steam draws its rows. Off: plain cards - worth trying if the page is slow to scroll. Takes hold the next time the page is opened. |
| Start the game from SSPTMM | On by default. Play on the Play page starts the server and the game itself, with no SPT launcher window. See [Play](Play#play-from-ssptmm). Also: leave SSPTMM open when the game starts. While it is on, Start server no longer opens the SPT launcher. |
| Tabs and background | Hide the Subscribed items or Collections tab (both stay under Workshop > Your Items), or put a picture of your own behind the window. |
| Scrolling | Smooth scrolling, as a web browser does, or jump at once. |

## Installing mods

| Setting | What it does |
|---|---|
| Install mode | Install mods for you, or only download them (you install by hand and confirm). |
| Download folder | Where downloaded-only mods are saved. Empty means your Windows Downloads folder. |
| When a downloaded mod shows up installed | Ask you to confirm it, or note it on the mod's card. Confirming keeps update checks right. |
| Open each mod's page first | Show the mod's page (install steps, requirements, warnings) before anything downloads. |
| Keep removed mods | How long unsubscribed mods are kept, so Undo can bring them back. They are kept in `.tcfmm-removed` inside your SPT folder. |
| Downloads | Keep downloaded files for reinstalling (up to 4 GB), or clear them. |
| Unsubscribing | Ask what will be deleted, and what to do with config files, before unsubscribing. |

## Updates

| Setting | What it does |
|---|---|
| Update notifications | A Windows notification when an installed mod has a new release. Only your installed mods are asked about. |
| Check every | How often. |

## Pages

| Setting | What it does |
|---|---|
| Mod footprint page | Adds the Mod footprint page to Tools. |
| Server map | Adds the Server map page to Tools. |
| Page defaults | What Save as default stored for Browse. (Subscribed items remembers its own filters.) |

## Fika and servers

| Setting | What it does |
|---|---|
| What this machine is | A player's PC, a Fika headless client, or both - a headless client needs the mods that decide how a raid goes and none of the ones that draw things at a player. |
| Fika headless launcher | Only needed if `FikaHeadlessManager.exe` isn't at the top of the install folder. |
| Server map connection | The address and port from your SPT launcher, and the key if the server has one; what this machine shares. |

## Advanced

| Setting | What it does |
|---|---|
| Data files | View or hand-edit the JSON files under `Data\`, including `installed-mods.json`. |
| SPT profile backups | Copies of your profiles, with Put back. See [Safety and backups](Safety-and-Backups). |
