# SSPTMM - Steamified SPT Mod Manager

A Windows app for finding, installing and keeping track of [SPT](https://sp-tarkov.com) (Single
Player Tarkov) mods, laid out and styled like Steam's Workshop. The mods and collections come from
[sp-mod.com](https://sp-mod.com); nothing needs an account or an API key.

- **Workshop** - Home, Browse with Steam's filters and sorts, collections, authors' pages, an item
  page with its description exactly as its author wrote it, change notes, versions and comments.
- **Subscribe** - installs a mod with what it needs, three downloads at once; Unsubscribe removes it
  (with Undo for a while afterwards). Install from file for an archive you already have.
- **Subscribed items** - everything in your SPT install, as cards, groups or a list, with updates,
  enable/disable, Update all and your own groups.
- **Collections** - your own mod lists, and public ones from sp-mod.com.
- **Play** - start the server and the game; Dependencies and Conflicts, Configs, the Server Map,
  copies of your SPT profiles before anything changes the install, and Help (F1).

## Based on TCF Mod Manager

SSPTMM is built on **[TCF Mod Manager](https://sp-mod.com/mod/2945/tcf-mod-manager) by
TheCrimsonFckr**: its mod handling, install and removal, and much of what is under the Steam look is
that app's work. SSPTMM is a separate app with its own look, names and version numbers; newer
TCF Mod Manager releases are merged in from time to time (the About page says which one it is
based on). TCF Mod Manager's own README, with its technical notes, is in
[docs/tcf-mod-manager-readme.md](docs/tcf-mod-manager-readme.md).

Problems with SSPTMM belong here, in this repository's
[Issues](https://github.com/trappuss/SSPTMM/issues) - not with TCF Mod Manager.

## Requirements

- Windows 10 or 11
- An SPT install (SPT 3.x or 4.x)

## Building and running

Double-click **`steam-ui-build-and-run.bat`**. It installs the .NET 9 SDK into the folder if the PC
has none, builds, runs the tests, publishes a self-contained `SSPTMM.exe` into `dist\steam-ui\` and
starts it. The build keeps its own `Data\` folder (settings, caches, logs) beside the exe, so it
never touches another copy of the app.

On first start, open **Options** and point **SPT Install Folder** at your SPT folder - the one with
`EscapeFromTarkov.exe` in it - and press Save.

## More

- [docs/steam-workshop-ui.md](docs/steam-workshop-ui.md) - how each Steam page was matched, what was
  measured, and every change round by round.
- [docs/sp-mod-guide.md](docs/sp-mod-guide.md), [docs/server-map-guide.md](docs/server-map-guide.md)
