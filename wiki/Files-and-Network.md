# Files and network

Everything SSPTMM writes, and every server it talks to. SSPTMM has no telemetry, no analytics and
no crash reporting: errors go to its own log file and nowhere else.

## Who SSPTMM talks to

Requests to sp-mod.com, GitHub and picture hosts carry a `User-Agent` of `SSPTMM/<version>`; mod
downloads, SPT servers and the built-in browser send their own defaults. Like any connection, the
server you connect to also sees your IP address.

| Server | When | What is sent | Can it be turned off? |
|---|---|---|---|
| **sp-mod.com API** (`https://sp-mod.com/api/v0/...`) | When the Workshop loads (the catalog of mods, addons and SPT versions - reused for 20 minutes, the SPT versions for a day), and as you browse, search, hover and open pages | What the page asks for: search text, filters (including your SPT version), a mod's id. **Subscribed items** and the **update watcher** send your installed mods' ids and versions (`/mods/updates`), and installs send a mod's dependencies (`identifier:version`) | The catalog is what the app is for. The update watcher is **off** by default (Options > Update notifications) |
| **sp-mod.com pages** (`https://sp-mod.com/lists`, `/list/<id>`, members' pages) | The Collections pages, collection hovers, authors' pages | What the page needs: search text and an SPT version, with the site's own form token. Cookies are kept in memory only | Not visiting those pages |
| **Pictures**: `files.sp-mod.com`, `i.ytimg.com` (video thumbnails), and whatever host a mod description's picture points to | When the picture is on screen | Nothing beyond the request. Only sp-mod.com's own hosts are told the request comes from sp-mod.com (a `Referer`), as `files.sp-mod.com` requires | - |
| **Mod downloads**: the download link a mod's author gave sp-mod.com - any host (GitHub, Google Drive and so on), following its redirects | Only when you subscribe, install, update or apply a collection; a mod's dependencies are shown to you first | A plain download request (resumed downloads send `Range` and `If-Range`) | It only happens when you ask |
| **sp-mod.com in a built-in browser** (WebView2): comments, a mod's page before download, embedded YouTube videos (`youtube-nocookie.com`) | When you open the Comments tab, a mod page, or a video | What a browser sends to those sites. A sign-in to sp-mod.com is kept in `Data\WebView2`. Links away from sp-mod.com open in your own browser | Not opening those |
| **GitHub** (`https://api.github.com/repos/trappuss/SSPTMM/releases/latest`) | At start, **only if you said yes** to the one-time question; and when you press **Check now** on Help > About | Nothing beyond the request | Help > About > **Check for new releases when SSPTMM starts** |
| **sp-mod.com API** for the **Server Map mod** (`/api/v0/addon/<id>/versions`) | With the GitHub check above, and only when the Server Map is in use on this PC (the mod is installed, or its page is on) | Nothing beyond the request | As above |
| **An SPT server you connect to** with the **Server Map** page (`https://<address>:<port>`, routes under `/tcfservermap/`) | Only with the Server Map page on and a server address set: at start, before you launch, and once a minute while its page is open | The server's shared key, once you trust the server | Options > Pages > Server map (**off** by default) |
| **The same server, reporting** (`/tcfservermap/report`) | About once a minute while SSPTMM runs (the server can ask for another interval) - **only after you answer Share** to "Share this machine with the server?", asked once per server | A random id for this PC, the name shown on the map (your PC's name unless you set **Name on the map**), whether it hosts, plays or runs a headless client, whether the game is running, SPT and SSPTMM versions, and your installed mods (name, id, version, GUID, folder, disabled) when they change | Options > Server map connection > **Report this machine to the server** (switching it off takes this PC off the map), or the page off |
| **Your own SPT server** (`https://127.0.0.1:6969`, or the address in its `http.json`) | Only with **Start the game from SSPTMM** on (Options, **off** by default), when you press Play or use its menu | The profile name and edition; it downloads mod bundles from the server. Addresses that aren't this PC are refused | Options > Start the game from SSPTMM |

**Opens in your browser** (not a request from SSPTMM itself): sp-mod.com, mod and addon pages,
GitHub (the repository, releases, this wiki, Issues), and links you click in a description.

## What SSPTMM writes

### Beside SSPTMM.exe

`Data\` and `Staging\` are made beside the exe. Deleting the folder removes all of it.

| Path | What |
|---|---|
| `Data\settings.json` | All settings, including your answers to its questions and, for the Server Map, the shared key and this PC's random id |
| `Data\installed-mods.json` | What SSPTMM installed, and every file it placed |
| `Data\mod_cache.json`, `addon_cache.json`, `spt_versions.json`, `collections_index.json`, `dependency_flags.json` | Cached sp-mod.com data |
| `Data\mod_presets.json` | Your presets, per SPT install, and how the mods were before the last one was applied |
| `Data\mod_lists.json`, `mod_groups.json` | Your collections and groups |
| `Data\mod_configs.json`, `config_updates.json` | Your per-mod config choices, and what updates did to configs |
| `Data\downloads.json`, `update_notifications.json`, `mod_footprints.json` | Monitor mode's saved archives, announced updates, footprint readings |
| `Data\backups\` | Rolling copies of the files above, and any found damaged |
| `Data\logs\ssptmm-<date>.log` | The daily log (old ones are pruned) |
| `Data\ProfileBackups\` | Copies of your SPT profiles (see [Safety and backups](Safety-and-Backups)) |
| `Data\GameLogs\` | The last three game sessions' logs, kept by **Start the game from SSPTMM** |
| `Data\config-backups\`, `ConfigBaselines\`, `LegacyConfigs\`, `overwritten\` | Config backups and baselines, configs kept from removed mods, files an install replaced |
| `Data\Downloads\` | Downloaded archives, kept for reinstalls (up to 4 GB) |
| `Data\WebView2\` | The built-in browser's profile and cookies |
| `Data\Background\`, `SharedCollections\` | Your chosen background picture; the last shared copy of each collection |
| `Data\notifications-registered` | An empty marker: Windows has a notification registration for SSPTMM (see below) |
| `Data\ServerMap\` | Only on a Server Map server, where SSPTMM sits in `<SPT>\TCFModManager`: the key, the published list, the map |
| `Staging\` | The default folder for archives you download by hand |

### Inside your SPT folder

- The mods you install, in `BepInEx\plugins`, `BepInEx\patchers` and the server's `user\mods`.
- Disabled mods, moved into `.disabled` folders beside those.
- Hidden working folders: `.tcfmm-work\` (during an install), `.tcfmm-removed\` (mods you
  unsubscribed from, kept for Undo), `.tcfmm-duplicates\` (set-aside duplicates).
- Mod config files you edit, and configs carried over on an update.
- `user\profiles\` only when you put back a profile copy.
- With **Start the game from SSPTMM** on, what SPT's own launcher does before the game starts:
  removes `BattlEye`, the old `Logs` and a few files SPT's launcher removes, applies SPT's game patches
  (keeping `*.spt-bak` copies), and fetches mod bundles into `user\cache\bundles`.

### Elsewhere on your PC

- `%TEMP%\SSPTMM\` and `%TEMP%\SSPTMM-dropped\`: short-lived working copies of archives, cleaned up
  after use (dropped files after a day).
- Your Windows **Downloads** folder, only in Monitor mode, where SSPTMM saves archives for you to
  install by hand (you can choose another folder).
- Folders you pick yourself, when you export or share something.
- **Windows notifications**: only once **Update notifications** has been on, Windows' notification
  library registers SSPTMM for your user: two keys under `HKEY_CURRENT_USER\Software\Classes`
  (`AppUserModelId\...` and `CLSID\{...}`) and a copy of the icon in
  `%LocalAppData%\ToastNotificationManagerCompat\Apps\`. When SSPTMM closes with update
  notifications off, it removes all three. (Switched off in 1.1.0 or earlier? Switch it on and off
  once, then close SSPTMM.)

Nothing else: no services, no start-up entries, no other registry keys, nothing in `%AppData%`.
