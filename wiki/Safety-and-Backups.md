# Safety and backups

## SPT profile backups

Before SSPTMM installs, updates, removes or disables a mod, applies a collection, or wipes or
deletes a profile from [Play](Play#play-experimental), it zips your SPT profiles (`user\profiles`) -
whenever they changed since the last copy. The last 10 are kept per install. Options > **SPT profile
backups** lists them, takes one by hand, and puts one back (it refuses while SPT is running, and
takes a copy of the profiles as they are first, so a put-back can itself be undone). SPT's own
backups are left out.

## Mods that change your profile

sp-mod.com marks some mods "may make permanent changes to your profile, and may not be removable
without starting a new profile" - SVM and Skills Extended among them. Removing one (or turning it
off - a turned-off server mod isn't loaded either) can leave a profile SPT won't load. So:

- removing one puts that warning first, and the question is asked even with Don't ask again ticked;
- removing several lets you **Keep these, remove the rest**;
- if a profile won't load afterwards, put the mod back with **Undo removing**, or install it again.
  [Diagnose logs](Diagnose-Logs) shows "A profile won't load" when it happens.

## What is never overwritten

- **Your BepInEx settings.** A mod's `BepInEx\config` file is only placed where none exists yet, or
  over the app's own untouched copy.
- **Your config changes** carry into updates (see [Tools > Configs](Tools#configs)).
- **SPT's own files.** Files a mod tries to put in SPT's `user` folder outside `user\mods` and
  `user\patchers`, or over SPT's, BepInEx's or the game's own files, are not placed, and the
  download card names each one.
- **Other mods' files.** A file that belongs to another mod, or to nothing the app installed, waits
  for **Install Anyway**, and files this mod replaced are kept and put back when it is removed.

## Removing only what was installed

SSPTMM records every file it places. Unsubscribing removes exactly those - and leaves any that
changed since, or that another mod also uses. Removed files go to `.tcfmm-removed` inside your SPT
folder for a while first (Options > Keep removed mods), so **Undo** can bring them back.

## Checked after every install

After every install and update, each file on disk is compared with the archive. The download card
names any file that is missing or different.

## Server prepatches

Mods like Skills Extended ship a server **prepatch** in `user\patchers\<GUID>\`. SSPTMM installs,
updates and removes it with its mod. Disabling a mod leaves its prepatch in place, so a profile that
uses the mod's values still loads.
