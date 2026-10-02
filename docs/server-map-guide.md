# Server Map how do I…?

Server Map lets an SPT server publish the mod list it expects players to be running, so TCF Mod
Manager can tell someone what they are missing **before** they launch instead of after a raid fails
to load. With the Server Map mod 0.2.0, it also shows **who is on the server**: every machine that
agrees to report, whether it is in game, and whether it has what the list asks for.

Two halves, and they are separate installs:

**The Server Map mod** > goes on the machine running the SPT server. Installed by the server operator.

**The Server Map page** > lives in TCF Mod Manager. Installed by anyone joining that server.


#### Three things it does not do:
- It never sends mod files. The server publishes a *list*; mods still only ever download from
  sp-mod.com. There is no route for a server to push you a file.
- It never reports anything about your install without asking. Connecting asks the server who it
  is. Your machine only appears on the server's map after you press **Share**, and only on that
  server - switch it off in Options and it leaves the map straight away.
- It never blocks you playing. The check on the Play page says what it found and leaves the launch
  buttons working.

Client and server must be on the same SPT line a 4.1 client cannot join a 4.0.13 server, and that
is SPT's rule, not this mod's. So a published list only ever reaches people already on your version.

# If you run the server 
How Do I?...

# {.tabset}
## Get the Server Map mod?

It is **not bundled with TCF Mod Manager** it is a separate download, published as an addon on the
app's mod page at sp-mod.com. That is deliberate: it is installed on a different machine from the
app, by a different person, and a player joining your server needs none of it.

In the app: switch the Server map page on (**Options** → **Pages** → **Server map**), then
**Options** → **Fika and servers** → **Server map connection** → **Get the Server Map mod**. That opens the page in your
browser.

**There is one zip per SPT line** - `...-SPT4.1.zip` and `...-SPT4.0.13.zip`. Take the one that
matches your server. The payload inside is the same build either way; only the stub differs.

If you take the wrong one, SPT refuses to load the stub and says so - it will not half-work.

## Install the Server Map mod?

The zip is laid out exactly as it installs, so installing it is one extraction.

1. **Stop the server.** The payload DLL is locked while it runs, and a copy over a locked file
   fails silently you will then spend an hour debugging the previous build.
2. **Extract the zip into your SPT root** - the folder that holds `SPT_Runtime\` (4.1) or `SPT\`
   (4.0), and `TCFModManager\`. The **stub** lands in `user\mods\TCFMM.ServerMap\`, where SPT
   looks for mods; the **payload** lands in `TCFModManager\ServerMap\payload\`, outside it, so
   removing the feature later is one folder deletion.
3. **Start the server.**

## Where exactly does the payload folder go?

The stub starts in its own folder and walks **up** the tree looking for
`TCFModManager\ServerMap\payload\`, so it does not care which SPT layout you have. Two real
examples:

```
SPT 4.0.13
  \SPT\user\mods\TCFMM.ServerMap\   <- stub
  \TCFModManager\ServerMap\payload\ <- payload
  \TCFModManager\Data\ServerMap\    <- key and published list

SPT 4.1.x
  \SPT_Runtime\user\mods\TCFMM.ServerMap\ <- stub
  \TCFModManager\ServerMap\payload\       <- payload
  \TCFModManager\Data\ServerMap\          <- key and published list
```

If TCF Mod Manager is already installed on that machine, it lives in that same `TCFModManager`
folder so `ServerMap\payload\` goes right next to the app.

**`Data\` is the folder to keep.** Your key and your published list live there, with the app's
settings and install history. Replacing `TCFModManager\` wholesale when you deploy takes all of it
with you lay a new build over the old one instead, the way the app's own updater does.

## Know when it is loaded?

Watch the server's console on startup:

```
[TCFMM ServerMap] Ready - serving /tcfservermap from <path>
```

That is the only line you need. Two others tell you what went wrong:

- `No payload found, so this mod does nothing. Looked for TCFMM.ServerMap.Payload.dll in: …` the
  stub loaded but cannot find the payload. The message lists every folder it checked; put the
  payload in one of them. **The server starts normally**, which is the point: deleting the payload
  folder is the supported way to switch the feature off.
- `Found <path> but could not load it: …` the payload is there but broken. If it complains about
  casting `IServerMapPayload` to `IServerMapPayload`, there is a stray copy of
  `TCFMM.ServerMap.Shared.dll` in the payload folder. There should not be one; delete it.

## Find my server's key?

Every route except the handshake needs a shared key, so a stranger who finds your port cannot read
your list. The server generates one **on startup** before anyone connects, because it is the
thing you send out with the address.

It is in a text file in the app's data folder:

```
<...>\TCFModManager\Data\ServerMap\servermap-key.txt
```

24 characters in six groups of four. Send it to your players along with the address and port. It is
compared with dashes, spaces and case ignored, so it does not matter how they paste it.

The key is generated **once**, the first time the server starts without one. After that it is left
alone restarting the server does not change it, and neither does updating the mod. The file is the
key: whatever is in it is what the server expects.

**If TCF Mod Manager is on the same machine as the server**, you never need to open this file: the
app reads it and fills the key in on its own. See "How do I fill in my own server's details".

## Change my server's key?

**Options** → **Server map connection** → **Generate a new key**. It only appears on the machine actually
running the server.

Everyone holding the old key stops being able to see what your server publishes the moment you press
it, so only do this if a key has gone somewhere it should not have and send the new one out
afterwards.

The server picks the change up on its own. **It does not need restarting.**

## Publish a mod list to my server?

1. Get the server's install the way you want it, then on **Mod lists** press **Capture** to make a
   list of what is there. (Or pick an existing list.)
2. Check the scopes see the next question. This is the step people skip and regret.
3. With the list selected, press **Publish to this server**.

That writes the list into `TCFModManager\Data\ServerMap\` on this machine. Nothing is installed,
enabled or disabled; no files on the game folder change.

The list is re-read whenever its file changes, so **publishing again takes effect without
restarting the server**.

The published list gets a red **Serving** badge on the Mod lists page, so among a dozen personal
lists you can see which one other people are being handed.

#### warning
**Do not apply your published list on the machine that hosts.** Scope decides what a machine takes
from a list a *server* served it; a list of your own applies whole, every entry whatever its scope.
So a list pruned down to what players need names none of your server's mods, and applying it on the
server box sets aside `fika-server`, SVM and the Server Map mod itself which is the thing serving the
list. Nothing is deleted and **Undo** puts it straight back, but your server stops publishing until
you do.

Keep **two lists on the server box**: the full one you apply there, and the pruned one you publish.
A list is a manifest rather than a copy of anything, so a second one costs nothing.

## Stop a server mod being sent to my players?

Mark it **Server only**.

The problem: your server needs `fika-server`, the Server Map mod itself, and anything else living
in `user\mods`. None of those are on The Forge, and none of them belong on a player's machine. But
if you leave them off your list entirely, applying your own list on the server switches them off.

So every entry on a list has a **scope**, named after the machines that get the mod. What is in the
name gets it; what is not, does not.

| Scope | Who installs it |
|---|---|
| **Server + Client + Headless** | everything. A mod with both halves, by default |
| **Server + Client** | the server and the players, but not the headless |
| **Client + Headless** | every machine running the game. A plugin, by default |
| **Client only** | players only. HUD tweaks, sound packs, anything drawn at a person |
| **Headless only** | the headless box alone |
| **Server only** | the server's own mods. `fika-server`, the Server Map mod, SVM |

A **Server only** entry is not fetched by a player, not counted as missing, and never appears in
their "you are behind" list.

Scope is worked out for you when you capture a list, from where the mod's files actually live. To
change one: select the list, find the row, and press the **scope button** to cycle it. The full set
leads into **Client + Headless** first, because taking a HUD mod off the headless is the edit
anybody actually makes. Then **Save**, then **Publish to this server** again.

The **scope filter** above the list matches one exactly, which is the question you have while tidying
one: what have I already pruned, and what is still carrying the capture default?

## Serve a Fika headless only what it needs?

A headless runs the game but nobody sits at it. It needs the mods that decide how a raid goes - bots,
items, weapons, locations - and has no use for the ones that draw things at a player. Served a
player's list it installs a pile of HUD tweaks and sound packs it will never show anyone.

One list still covers everybody. The machine reading it takes the entries scoped to it.

- **Capture gives every plugin to the headless** (`Client + Headless`) and you take away what it does
  not need. Nothing on disk separates a bot overhaul from a HUD widget, so the guess goes the safe
  way: a headless carrying a spare mod costs nothing anyone can see, while one missing an item or bot
  mod is felt by everybody in the raid it is hosting.
- **A headless still takes Server only entries.** It is a full SPT install, and a server-only entry
  is a whole mod rather than half of one. **Server + Client** is how you say the server and the
  players need this and the headless does not.
- **The headless machine has to know what it is.** On that machine: **Options** → **What this
  machine is**. The app asks by itself when you set the install folder and it finds
  `FikaHeadlessManager.exe`; until somebody answers, the machine is treated as an ordinary player,
  which installs more rather than less.

Lists written before this keep meaning what they meant: **Client** used to mean "not the server",
because the server and a player were the only two machines a list could describe, so an entry written
then is read as **Client + Headless** now.

## Update the list my server is serving?

Edit the list, **Save** it, then **Publish to this server** again. That is all the server picks
up the new file on its own.

**The list you publish stays yours to edit.** A copy of it coming back off your own server never
replaces the original, so hosting and playing on one machine does not turn your own list read-only.

**Publishing moves the revision when what you published has changed.** That number is what tells
connected players their copy is stale: they see "the server is publishing revision 4; you have
revision 3" on their Play page. Republishing an unchanged list changes nothing, so the number still
means something when it does move.

If somebody's copy looks stale anyway, they can press **Refresh from server** on the list itself,
which asks again whether or not the revision has moved.

## Let people connect from outside my network?

Server Map uses the SPT server's own port there is no second listener so if people can already
join your server, they can already reach it.

If you are setting that up from scratch, in `SPT_Data/configs/http.json`:

- `ip` → `0.0.0.0`
- `backendIp` → your machine's LAN address
- allow TCP **6969** inbound, and forward it if you want people outside your network

Then give people your external address, the port, and the key.

## Keep it to my own network?

If only people at home - or on your Tailscale - ever play, turn on **Only answer this network** in
**Options - Server map connection** on the server machine. Needs the Server Map mod **0.2.0** or
later, stub included.

From then on the server refuses every Server Map request from outside its own network, even the
handshake, and the player sees *This server only answers machines on its own network*. It takes
effect straight away; no restart.

What counts as your network:

- the server machine itself
- the private ranges a router hands out - `192.168.x.x`, `10.x.x.x`, `172.16-31.x.x`
- **Tailscale** peers (`100.64.0.0/10`), so a group on Tailscale still gets in. ZeroTier uses
  private ranges already.

It only covers Server Map. Whether people can join the SPT server itself is still `http.json` and
your router.

The switch writes `TCFModManager\Data\ServerMap\servermap.json`; `{ "lanOnly": true }` there by
hand does the same.

## See who is on my server?

The **Server map** page on any machine connected to your server - yours included - lists every
machine that reports to it, with your server first. Needs the Server Map mod **0.2.0** or later.

Your server's own row comes from **TCF Mod Manager on the server machine**: connect it to your own
server and press **Share** like anyone else. The mod never scans your install - the row shows what
the app installed and keeps a record of.

Each machine is asked once before it reports anything, so a player who says no simply is not on the
map. Machines that do not run TCF Mod Manager never appear.

The list lives in `TCFModManager\Data\ServerMap\clients.json`. A machine not heard from for 30 days
is forgotten on its own.

## Update the mod?

**TCF Mod Manager tells you when to.** Its **App update** page has a **Server Map mod** card on the
machine that runs the server: the version installed, the newest on sp-mod.com, and a warning - with
a coloured dot on the sidebar item - when a newer one is out. It also warns when the stub in
`user\mods` is older than the payload, which is easy to miss and stops LAN-only letting anyone in.
Players connected to your server see the same card for your server's version, telling them to ask
you.

1. **Stop the server.**
2. Extract the new zip for your SPT line into your SPT root, over the old files.
3. Start the server.

**Leave `TCFModManager\Data\ServerMap\` alone.** Your key, your published list and the map's
`clients.json` are there, and none of them are in the zip.

The app never updates the mod itself - it is installed by hand, on the server machine - so the card
only says it is behind and links the mod's page.

## Remove it?

Delete the `TCFModManager\ServerMap\payload\` folder. The server starts normally, the stub logs one
line and does nothing. Delete `user\mods\TCFMM.ServerMap\` too if you want it gone entirely.
`TCFModManager\Data\ServerMap\` holds your key, your list and the map - delete it as well if you
will not be putting the mod back.

{.endtabset}

# If you are joining a server
How do I?...

# {.tabset}
## Turn the Server map page on?

It is off by default, because without a server running the mod there is nothing for it to show.

**Options** → **Pages** → switch **Server map** on. The **Server map connection** settings, under
**Fika and servers**, only appear while it is on.

## Connect to a server?

**Options** → **Fika and servers** → open **Server map connection**:

1. **Server address** the same address you put in the SPT launcher. Not `0.0.0.0`: that is a bind
   address, not one you can dial.
2. **Port** 6969 unless the operator says otherwise.
3. **Shared key** paste what the operator sent you.
4. Press **Connect**.

The **Server map** page in the sidebar then shows the server's name, its SPT version, and whether it
publishes a list.

## Fill in my own server's details?

When the app is on the server machine? You do not. If a Server Map server is installed on this machine, 
the app finds its key file andfills the box in for you every time you open Options or the Server map page, 
so it appears the first time you start the server without restarting the app, and it follows the 
key if you rotate it.

**Use this machine's key** is still there for the one case that needs it: if you have typed a
different server's key into the box, the app leaves it alone rather than overwriting it, and that
button puts your own back.

On any machine that is not running a server, no file is found and the box stays empty which is
correct. A key belongs to one server.

## Tell the app this machine is a headless?

**Options** → **What this machine is**, directly under the install folder. Two switches: whether
anybody plays here, and whether this machine runs a Fika headless client. A dedicated headless box is
the second without the first.

The app asks the question by itself when you set the install folder and find `FikaHeadlessManager.exe`
at the top of it. **Ask me later** stores nothing and brings it back; until it is answered the
machine is treated as an ordinary player.

Nothing on disk tells a headless install from a player's one, so this cannot be worked out for you: a
headless has `SPT.Server.exe`, BepInEx and a game client exactly like a player's. If your headless
manager lives somewhere else, or is named something else, point **Options** → **Fika headless
launcher** at it - it does not have to be inside the install folder.

This only ever narrows a list a server served you. Your own lists apply whole, and nothing here
changes what you can install by hand.

## Get the server's mod list?

You do not have to ask for it. Connecting fetches it, and it is fetched again on its own whenever the
server's revision moves. **Fetch again** on the **Server map** page asks for it even when the
revision has not moved, for a copy you have edited or deleted.

The list is saved here as a new mod list, badged red as **From server** on the Mod lists page. It
stays exactly as the server wrote it you cannot edit it. If you want a version of your own, use
**Make a copy**. **Refresh from server**, on the list itself, is the same re-ask from the page where
the list actually lives.

## Install what the server expects?

The quick way: when you are behind, press **Review and install** on the Play page's check or on
your own card on the Server map page. It opens the server's list with the preview already done.

Or by hand, like any other list:

1. **Mod lists** → select the list badged **From server**.
2. **Preview** to see exactly what would change.
3. **Apply** to queue the downloads.

The preview shows coloured counts at the top - *1 missing*, *2 incorrect versions*, *1 disabled*,
*72 correct* - and a badge on every row in the same colours. An incorrect version shows the move,
for example *3.0.0 → 3.1.2*. The Apply button says what it is about to do: *Install 1 mod*.

Everything comes from sp-mod.com as usual, and you only get the entries meant for this machine: a
player is never asked to install `fika-server`, and a headless is not sent the mods that only draw
things at a person. Whether this machine is a player, a headless, or both is answered under
**Options** → **What this machine is**.

## Keep my own mods as well as the server's?

You do not have to choose. A server's list and one of your own can be **active at the same time**,
which is what the two separate tick marks on the Mod lists page mean.

Following a server does not drop the personal list you were already following, and applying a list
of your own will not sweep away the mods the server requires they are protected from your own list's
tidy-up.

It works the other way too: a list a server hands you never disables anything of yours, whatever its
author chose. The server says what you need, not what you may not have.

## Check I am ready before I play?

Open the **Play** page. It checks automatically, and **Check again** re-runs it.

| What it says | What it means |
|---|---|
| **Ready to join** | your install matches the list, at the versions it names |
| **Your install doesn't match this server** | it names exactly which mods are outstanding |
| **This server's mod list has changed** | the server publishes a newer revision than you hold fetch it |
| **Couldn't check the server** | it did not answer; you are compared against your last fetched copy |

It names what is outstanding, with versions where a version is wrong: *SAIN (3.0.0 → 3.1.2)*.
**Review and install** takes you straight to the fix.

It never stops you launching. If it says you are three mods behind and you want to try anyway, the
buttons still work.

## Find one mod on a long list?

A served list can run to eighty mods. Above the list contents there is a **search box**, a **scope
filter** (All scopes, then each of the six scopes) and a **sort** (A-Z / Z-A).

Search matches the mod's name, the name the list stored, and the folder it installs into so you
can find something by the folder name if that is what you know it by.

The header reads "12 of 76 mods on this list" while a filter is on, so a filtered view is never
mistaken for a short list.

## Show up on the server's map?

The first time you connect to a server with the Server Map mod 0.2.0 or later, the Server map page
asks **Share this machine with the server?**

- **Share** - from then on this machine reports once a minute while TCF Mod Manager is running,
  including when it is only in the tray.
- **Don't share** - nothing is sent. The page remembers the answer.

What is shared: the machine's name, what it is (server, player, headless), whether the game is
running, and the mods installed on it with their versions. Everyone holding the server's key can see
it. You are asked once per server, and again if the server's certificate changes.

**Options** → **Server map connection** has the same answer as a switch, **Report this machine to
the server**, and **Name on the map** - leave it empty to use the computer's name.

## See who else is on the server?

The **Server map** page lists every machine that reports to the server, the server first and your
own marked *This machine*:

- **In game**, **App open**, or **Last seen** some time ago.
- A coloured line for where it stands against the server's list - green when it has everything,
  amber or red naming what is outstanding.
- Mods it has that are not on the list, and an expander with everything it has installed.

It refreshes every minute while the page is open. **Refresh** sends your own report first, so your
card always shows what you see on your own screen.

## Stop showing up on the map?

**Options** → **Server map connection** → switch **Report this machine to the server** off. The
machine leaves the map straight away. Switching the Server map page off stops the reports as well.

## Stop using a server's list?

Delete it on the Mod lists page, or apply one of your own. To stop connecting altogether, switch the
**Server map** toggle off in Options the page disappears from the sidebar and nothing is
contacted.

{.endtabset}

# When something is wrong 
Troubleshooting...

# {.tabset}
## The server does not have a published playlist

The server is running the mod but is not publishing anything. Either the operator has not published
one yet, or the file did not land in `TCFModManager\Data\ServerMap\`. Operators: check the file is
there, and that there is either exactly one `.tcfmodlist` in the folder or one named
`published.tcfmodlist` several files with no preferred name is ambiguous, so the server serves
nothing rather than guessing.

## This server's certificate has changed

SPT serves a self-signed certificate, so the app remembers the exact one your server presented the
first time and checks it every time after. A change means one of three things:

- the operator reinstalled or moved the server, and it generated a new certificate normal
- you are connecting to a different machine than you think
- someone is sitting between you and the server

The app shows both fingerprints and will not connect until you decide. Ask the operator whether they
expected it. If yes, **Trust the new certificate**. If you are not sure, do not.

**Forget this certificate** clears what is remembered, so the next connection starts fresh.

## It says the key was rejected

Keys are compared ignoring dashes, spaces and case, so a formatting difference is not the cause. Ask
the operator to re-read `servermap-key.txt` it is regenerated only if the file is deleted, so a
mismatch usually means you have an old one.

## My headless installed a pile of player mods

It is reading as an ordinary player, which is what an unanswered machine is treated as. On that
machine: **Options** → **What this machine is**. If the app never asked, it did not find
`FikaHeadlessManager.exe` at the top of the install folder point **Options** → **Fika headless
launcher** at the one you have.

Operators: check the list too. A row still scoped **Client + Headless** goes to the headless by
design that is the capture default, and pruning it is the operator's job.

## The map says the server mod needs updating

The server is running Server Map 0.1.0, which publishes a list but has no map. Everything else -
the list, the pre-launch check, Review and install - works as before. The operator needs the 0.2.0
zip.

## My machine is not on the map

Work through these:

1. Did you press **Share**? Check **Options** → **Server map connection** → **Report this machine
   to the server**.
2. Is the Server map page switched on? Reports stop when it is off.
3. Has this machine connected since the server's certificate changed? A new certificate is asked
   about again.
4. Press **Refresh** on the Server map page - it reports first, then reads the map.

## It says the server only answers its own network

The operator has turned on **Only answer this network**, and your machine is outside it. Either
ask them to turn it off, or join their network - Tailscale is the usual way.

If you **are** on the same network and still see it, the server's stub is older than its payload:
replace `user\mods\TCFMM.ServerMap\` with the stub from the same zip. Without it the server cannot
see who is asking, so it refuses everyone.

## Nothing answers at all

Work outwards:

1. Can you join the server in SPT at all? If not, this was never going to work it uses the same
   port.
2. Is the address the one you use in the SPT launcher, and not `0.0.0.0`?
3. Did the server log the `Ready - serving /tcfservermap` line on startup?

{.endtabset}

# What it does not do yet

Worth knowing so you are not looking for it:

- **Machines that do not run TCF Mod Manager never appear on the map.** Someone who installs mods
  by hand is invisible to it.
- **One server per install.** The app connects to one server at a time.
- **Nothing is applied automatically.** A server can never change your install; every fetch, apply
  and download is something you press.
