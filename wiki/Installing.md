# Installing

## What you need

- Windows 10 or 11.
- An SPT 4.0 or 4.1 install.

The download is **self-contained**: you don't need to install .NET.

## Steps

1. Open [Releases](https://github.com/trappuss/SSPTMM/releases/latest) and download
   `SSPTMM-<version>-win-x64.zip`.
2. Unzip it into your SPT folder (the one with `EscapeFromTarkov.exe`). The zip holds a single
   folder, `SSPTMM`, so you get `SPT\SSPTMM\` - a folder of its own beside the game.
   - You can unzip it anywhere else instead, for example `C:\Games`.
   - **Not** inside `BepInEx\plugins` or `user\mods` - SSPTMM is not a mod.
   - Not inside a folder Windows protects, such as `Program Files`.
3. Run `SSPTMM\SSPTMM.exe`.
4. Go on to [First start](First-Start). Unzipped into your SPT folder, SSPTMM finds the SPT install
   around it by itself (from 1.3.0); anywhere else, you point it at SPT once in Options.

## Where it keeps its files

Everything SSPTMM writes about itself - settings, the sp-mod.com catalog cache, kept downloads,
profile backups and logs - is in a `Data` folder **beside `SSPTMM.exe`** (plus a `Staging` folder
there for archives fetched by hand on the Downloads page). So:

- it never touches another mod manager's settings or another copy of SSPTMM;
- moving the folder moves everything with it;
- deleting the folder removes it completely (your SPT install is not affected).

Inside your SPT folder it uses a few hidden working folders with TCF Mod Manager's names
(`.tcfmm-removed`, `.tcfmm-work`, `.tcfmm-duplicates`), so both apps can work on the same install.

## Updating

SSPTMM tells you when a newer release is out (a dot beside **Help** and **About**). From 1.3.0,
About has an **Install update** button: after you confirm, SSPTMM downloads the release from
GitHub, closes, puts the new version over its folder and starts again. Your settings in `Data`
stay. It needs Windows PowerShell (every Windows 10 and 11 PC has it) and a folder SSPTMM can write to.

Or update by hand: close SSPTMM, download the new release and unzip it to the same place - its
`SSPTMM` folder lands **over** yours.

Coming from 1.2.0 or earlier, whose zip had no folder inside? Copy the contents of the new zip's
`SSPTMM` folder over your SSPTMM folder instead, whatever you named it.

## Uninstalling

1. If you ever turned **Update notifications** on (Options > Updates), make sure it is off - switch it
   on and off once if it was switched off in 1.1.0 or earlier. Closing SSPTMM with it off removes the
   registration Windows keeps for SSPTMM's notifications.
2. Close SSPTMM, and stop SPT's server if SSPTMM started it (Play > **Stop server**).
3. Delete SSPTMM's folder - the exe and its `Data` folder. If they are there, also delete
   `%TEMP%\SSPTMM` and `%TEMP%\SSPTMM-dropped` (short-lived working copies). Nothing else of SSPTMM is
   left on the PC - [Files and network](Files-and-Network) lists everything it writes.

Mods it installed stay in SPT and keep working. Two things inside your SPT folder are worth knowing:

- Mods you disabled are in `.disabled` folders beside the ones SPT loads from
  (`BepInEx\plugins.disabled`, `SPT_Runtime\user\mods.disabled` and so on). Move a mod back out to
  enable it again, or delete it.
- The hidden working folders (`.tcfmm-removed`, `.tcfmm-work`, `.tcfmm-duplicates`) hold mods you
  unsubscribed from (kept for Undo) and set-aside duplicates. Delete them once you no longer need
  them - unless you also use TCF Mod Manager, which uses the same folders.
