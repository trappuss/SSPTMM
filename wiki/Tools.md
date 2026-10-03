# Tools

The **Tools ▾** menu holds the pages you need less often.

![Tools menu](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/09a-tools-menu.png)

## Configs

![Configs](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/09-configs.png)

Every config file your mods keep - BepInEx `.cfg` files and server mods' JSON - by mod, with an
editor.

- Every file is **backed up before it is saved**, and earlier versions can be brought back.
- **When this mod updates** chooses what an update does with this mod's config files: merge your
  changes into the new file (the default), keep your file as it is, or take the new file.
- **Files I authored...** marks a folder inside the mod holding files you wrote (presets, for
  instance): updates leave it as it is, and removing the mod keeps it.
- **Settings kept elsewhere...** points at a settings file the mod keeps somewhere unusual, so
  updates carry your changes into it too. **This isn't a config** takes a file off the list.

A wrong setting can break the mod or your game, and the mod's author isn't the one to put it right.

## Dependencies and Conflicts

![Dependencies](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/10-dependencies.png)

- **Conflicts** first: a mod installed twice, or two mods shipping different copies of the same
  file. **Keep this** picks one; the other is moved aside, not deleted.
- **Dependencies**: every installed mod that declares what it needs, and whether each need is met -
  read from the mods' own files, so it covers mods installed by hand too.

## Diagnose logs

Reads SPT's logs and says what went wrong and which mod it came from. See
[Diagnose logs](Diagnose-Logs).

## Mod footprint (optional)

Turn it on in [Options](Options) > Pages. It reads the files each installed mod ships and describes
how much of the game it is positioned to touch, and where any cost would land - client or server,
CPU, GPU, memory or start-up. Nothing on it is measured while the game runs.

## Server map (optional)

Turn it on in [Options](Options) > Pages. It connects to an SPT server running the **Server Map**
mod, shows what that server runs, compares it with your install, and can show the machines that
report to it. The mod goes on the server, not here. The full guide is
[docs/server-map-guide.md](https://github.com/trappuss/SSPTMM/blob/main/docs/server-map-guide.md).
