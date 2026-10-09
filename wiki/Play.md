# Play

![Play](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/01-play.png)

## Warnings before you launch

Above the buttons, a card appears only when something in your install may not load. It never stops
you from launching.

- **Conflicts** (red): the same mod installed twice, or two copies of a mod that differ. Click it
  for the details.
- **Mods that need something** (from 1.3.0, amber): enabled mods that need a mod that isn't
  installed or is switched off - the same flag Subscribed items shows on their cards. Up to three are
  named; click it to go to Subscribed items.

## SPT server

- **Start server** starts `SPT.Server.exe` in its own window. It keeps running if you close
  SSPTMM.
- While it runs: **Stop server** (red) and **Restart**. Both ask first.
- **Server log** shows the server's log, live, read from its log file - its own window is left
  alone.
- "Running" means running from **this** SPT install. A server from another SPT copy doesn't count.

[Options](Options) > **Starting the game** can open the SPT launcher as soon as the server is up,
and can run the server **without its window** (its log is then only on this page, which also says
if the server closed before it was ready).

## Game launcher

- **Open launcher** opens `SPT.Launcher.exe`, where you pick a profile and start the game. Start the
  server first.
- While the game runs: **Close game**. It asks first - a raid in progress is lost.
- **Close the game when the server stops** (off by default): when the server is seen stopping,
  however it stops, the game is closed too. It watches the SPT install currently set in Options.
  **Stop server** closes the game first, while the server can still answer it, so the game quits
  cleanly in a few seconds. When the server goes some other way (its window closed, a crash), the
  game is closed within about two seconds of the server going, and killed if it hasn't gone two
  seconds after that - with the server gone it can't finish quitting by itself.

## Play from SSPTMM

![Play from SSPTMM](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/01b-play-direct.png)

With [Options](Options) > **Start the game from SSPTMM** on (it is by default; an install that was
set up before that keeps what it had), the game card has a profile list and a **PLAY** button: pick a
profile and press it. SSPTMM then

1. starts the server if it isn't running, and waits for it (the server log is above if it fails);
2. checks the server and the game files are the same SPT version;
3. does what SPT's launcher does before the game starts: removes the files it removes (BattlEye,
   the old game logs and the rest; anything in its **ExcludeFromCleanup** setting is left), applies
   SPT's game patches, and fetches the mods' bundles;
4. starts the game with that profile, and shows **Playing as** *name*.

No SPT launcher window opens. With **Starting the game** set to run the server without its window,
everything happens in the background. SSPTMM minimises itself when the game starts and comes back
when it closes, unless **Leave SSPTMM open when the game starts** is on.

The **...** button beside Play has the rest:

- **Wipe this profile on the next Play** - the next Play starts the profile over with a new
  character. A copy of your profiles is kept first.
- **New profile...** - a name and an edition, as in SPT's launcher.
- **Delete this profile...** - asks first, and keeps a copy of your profiles first.
- **Clear the game's cache** - what SPT's launcher calls clearing the cache.
- **Open the SPT launcher instead**, and **Settings...**.

Copies kept before a wipe or delete are in Options > **SPT profile backups**.

It works with SPT 4.1.3 to 4.1.6. A newer 4.1 is tried, with a warning on the card. Any other
version, 4.0 included, keeps the **Open launcher** button.

This copies how SPT's 4.1 launcher works, from its source code. The SPT team didn't make it and
doesn't support it. If the game won't start or acts strangely, switch it off and use the SPT
launcher. When you ask for help, say the game was started from SSPTMM. Two differences from SPT's
launcher:

- If the server and the game files are different SPT versions, it stops and says so. SPT's launcher
  only notes it in its log and starts anyway.
- It keeps a copy of the last three game logs before they are cleared, so
  [Diagnose logs](Diagnose-Logs) can still read the last game's errors.

## Mod tools

![Mod tools, with SVM's Greed](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/17-play-mod-tools.png)

Some mods ship a program of their own - SVM's **Greed** configurator, give-ui's app. The
**Mod tools** card lists every `.exe` an installed mod put in your SPT folder, by itself, each with:

- **Open** - starts it from its own folder;
- a folder button - the mod's folder (for SVM, where its presets are);
- **Hide** - takes it off the card. "Show N hidden tools" brings it back.

For tools it knows, the card says what they need: give-ui needs the server **running** (with Start
server beside it), Greed is used with the server **stopped**. A tool whose mod is disabled is greyed
out. The card only appears when at least one tool is found.
