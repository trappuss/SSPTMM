# Diagnose logs

**Tools > Diagnose logs** reads SPT's logs, explains in plain words what went wrong, and names the
mod it came from when the log names it in a way that can be traced. Nothing on the page changes any
file.

![Diagnose logs](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/11-diagnose.png)

## What it reads

| Log | Where | What is read |
|---|---|---|
| Server | `<server>\user\logs\spt\spt<date>.log` | Only the server's **last run** in its newest log - what a run before a fix said is no longer true. |
| BepInEx | `BepInEx\LogOutput.log` | The last game session (BepInEx rewrites it each start). |
| Game errors | `Logs\log_<date>_<version>\... errors.log` | The newest game session. |

The SPT launcher deletes the game's `Logs` folder each time it starts the game, so only the last
session's game logs exist. The page says so when it sees that.

Logs only describe the last run: after changing something, start the server or the game again, then
press **Read the logs again**.

## What it recognises

| Card | Means |
|---|---|
| A profile won't load | SPT marked a profile invalid. Put back the mod whose items or clothing it holds, or restore a profile backup. |
| A profile wears clothing from a missing mod | A clothing mod was removed while a character wore its clothes. |
| Raid results weren't saved | The game sent a value the server doesn't know (for example a skill) - usually a mod's game half is loaded but its server half (such as its prepatch in `user\patchers`) is not. |
| The server couldn't start | Its port is in use - most often a server that is still running. |
| A mod didn't load | BepInEx skipped a plugin: something it needs is missing or too old, it doesn't work with another mod, a mod it needs didn't load, a newer copy is installed, or it failed to load. |
| A server mod isn't running | Installed and enabled, but the server's last start didn't list it. |
| SPT's mod loader reported a problem | SPT's own message, shown as it is. |
| A server error answering the game / A server task keeps failing | Traced to the mod whose code the error came from, when it did. |
| A mod reported an error | A mod's own error line in the server log. |
| Two mods ship the same bundle | SPT could only use one of them. |
| A bundle needs files that aren't there | The game couldn't fully load a mod's bundle. |
| A mod logged errors in the game | A plugin's own error lines in BepInEx's log. |

Each card has the line it was read from (file and line number), and buttons: **Show in Subscribed
items**, **Open its folder**, **Open the log** - plus **Go to Play** or **Profile backups** where they
help. Errors no card knows are counted under the list.

## Copy a report for help

**Copy a report for help** copies what was found, the lines behind it and up to 50 other errors - not
whole logs, and never a profile. Taken out first:

- your user folder (becomes `%USERPROFILE%`) and your Windows user name (`<user>`);
- profile and item ids (each becomes `id-` and six characters, the same each time);
- other players' IP addresses (`<ip>:port`).

Paste it into a [GitHub issue](https://github.com/trappuss/SSPTMM/issues) or wherever you ask for
help.
