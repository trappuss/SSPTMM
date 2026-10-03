# Installing

## What you need

- Windows 10 or 11.
- An SPT install (SPT 3.x or 4.x).

The download is **self-contained**: you don't need to install .NET.

## Steps

1. Open [Releases](https://github.com/trappuss/SSPTMM/releases/latest) and download
   `SSPTMM-<version>-win-x64.zip`.
2. Unzip it into **a folder of its own**, for example `C:\Games\SSPTMM`.
   - **Not** inside `BepInEx\plugins` or `user\mods` - SSPTMM is not a mod.
   - Not inside a folder Windows protects, such as `Program Files`.
3. Run `SSPTMM.exe`.
4. Go on to [First start](First-Start).

## Where it keeps its files

Everything SSPTMM writes about itself - settings, the sp-mod.com catalog cache, kept downloads,
profile backups and logs - is in a `Data` folder **beside `SSPTMM.exe`**. So:

- it never touches TCF Mod Manager or another copy of SSPTMM;
- moving the folder moves everything with it;
- deleting the folder removes it completely (your SPT install is not affected).

Inside your SPT folder it uses a few hidden working folders with TCF Mod Manager's names
(`.tcfmm-removed`, `.tcfmm-work`, `.tcfmm-duplicates`), so both apps can work on the same install.

## Updating

SSPTMM doesn't update itself. To update, download the new release and unzip it **over** the old
folder (or into a new folder and copy your old `Data` folder across). Your settings stay.

## Uninstalling

Close SSPTMM and delete its folder. Mods it installed stay in SPT.
