# Subscribing and downloads

## Subscribe

1. **Subscribe** first works out what the mod needs.
2. If it needs other mods you don't have, Steam's **Additional Required Items** dialog asks once:
   **Subscribe to All**, **Subscribe to Just This Item**, or Cancel.
3. Outside the mod's own page, a window shows the mod's page first - its install steps,
   requirements and warnings, and the change notes for an update. **Continue** goes ahead.
   ([Options](Options) can turn this off.)
4. Before anything is placed, you are told about problems: required mods that are disabled, too new
   or the wrong version, conflicts, versions not for your SPT, versions not for Fika on a Fika
   install. You can stop there.
5. **File clashes**: files already on disk that belong to another mod, or to nothing the app
   installed, are listed, and the install waits for **Install Anyway**.

![Read the page first](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/16-read-page-first.png)

## Downloads

![Downloads](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/12-downloads.png)

- The **downloads bar** along the bottom always shows what is downloading or installing. Click it
  for the Downloads page.
- **Up to three downloads at once**; installs happen one at a time, in the order you queued them.
- A download that fails for a passing reason is retried, and a broken-off one carries on from where
  it stopped when the server allows it.
- Downloads are **kept** (up to 4 GB, oldest-used go first), so installing the same version again
  doesn't download it again. [Options](Options) > Downloads can turn this off or clear them.
- After every install or update, each file on disk is **checked against the archive**. The card
  names any file that is missing or different, and any file the mod tried to put in SPT's own
  folders (which is not placed).

## Download only

**Download only** (beside Subscribe, and on the right-click menu) saves the mod's file to your
download folder without changing anything in SPT. [Options](Options) > **Install mode** can make
that the default for everything (Monitor mode): you install by hand and confirm it on the card, so
update checks stay right.

## Install from file

Subscribed items' **⋯** menu > **Install from file** installs an archive you already have (.zip,
.7z, .rar, .tar, .tar.gz). You can also drop archives onto Subscribed items. Your file is copied,
never moved.

## Unsubscribe

- **Unsubscribe** asks first: what will be deleted, and whether to keep or delete the mod's config
  files. **Don't ask again** turns the question off (Options > Unsubscribing turns it back on);
  while it is off, config files are always kept.
- It warns when other installed mods still use this one, and - for mods sp-mod.com marks as making
  permanent profile changes - that removing (or turning off) the mod can leave a profile SPT won't
  load. See [Safety and backups](Safety-and-Backups).
- Removed files are kept for a while (Options > Keep removed mods), and **Undo removing** at the top
  of Subscribed items puts them back.
