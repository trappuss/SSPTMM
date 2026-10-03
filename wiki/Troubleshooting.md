# Troubleshooting

**Start with [Diagnose logs](Diagnose-Logs)** (Tools menu). It reads SPT's logs and says what went
wrong and which mod it came from, and **Copy a report for help** gives you something to paste when
you ask.

## The window title shows no SPT version

The SPT Install Folder is one level too deep or too high. Pick the folder with
`EscapeFromTarkov.exe` in it (Options > SPT Install Folder), and press **Save**.

## A mod didn't install or update

- Close the game and the server first - files in use can't be replaced.
- Open **Downloads** (the bar at the bottom): the card says why, including any file that is
  missing or different after the install.

## A hand-installed mod shows the wrong version, or no updates

Open it in Subscribed items > **Details and versions**, pick the version you installed and record it
as installed (nothing is downloaded), so update checks are right. A mod whose name doesn't match any
sp-mod.com listing shows as not on sp-mod.com.

## A profile won't load after removing a mod

Put the mod back (**Undo removing** at the top of Subscribed items, or install it again), or
restore a copy from Options > **SPT profile backups**. See [Safety and backups](Safety-and-Backups).

## A mod's tool says it can't find something (SVM's Greed: "couldn't find Preset folder")

Reinstall the mod with SSPTMM 1.0.0 or newer: empty folders a mod ships (such as SVM's `Presets`)
are created now. Older versions placed files only.

## The Comments tab is empty

Comments need the Microsoft Edge WebView2 runtime (part of Windows 10/11 updates). Without it, the
tab offers to open them in your browser.

## Reporting a problem

1. Help > **Report a problem** (GitHub Issues).
2. Say what you did and what happened.
3. Attach today's log from `Data\logs` beside `SSPTMM.exe` - the file named `ssptmm-` and the date -
   and, for problems in the game, a [Diagnose logs](Diagnose-Logs) report.
4. For more detail: put an empty file named `verbose` (no extension) next to `SSPTMM.exe`, do it
   again, and send the new log.
