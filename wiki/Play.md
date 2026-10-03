# Play

![Play](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/01-play.png)

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

## Mod tools

Some mods ship a program of their own - SVM's **Greed** configurator, give-ui's app. The
**Mod tools** card lists every `.exe` an installed mod put in your SPT folder, by itself, each with:

- **Open** - starts it from its own folder;
- a folder button - the mod's folder (for SVM, where its presets are);
- **Hide** - takes it off the card. "Show N hidden tools" brings it back.

For tools it knows, the card says what they need: give-ui needs the server **running** (with Start
server beside it), Greed is used with the server **stopped**. A tool whose mod is disabled is greyed
out. The card only appears when at least one tool is found.
