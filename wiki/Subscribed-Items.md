# Subscribed items

Every mod in your SPT folder - ones SSPTMM installed and ones you installed by hand - matched to
sp-mod.com so updates work.

![Cards](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/07-subscribed-cards.png)

## Views

The three buttons at the top right switch between **Cards**, **List** and **Groups**. All three show
the same details; click a card or row to open it.

| List | Groups |
|---|---|
| ![List](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/07b-subscribed-list.png) | ![Groups](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/07c-subscribed-groups.png) |

- **Groups** are your own: type a name and **Add group**, then drag mods into them - in every view.
  A group's header can enable, disable or invert all its mods at once.
- **Filters** also has **grouping** for Cards and List: by your groups, by sp-mod.com category, or
  **by enabled / disabled**.

## Sort, search and filter

- **Sort**: by name, author or group (A-Z / Z-A), or **Recently installed** (newest install or
  update first).
- **Search** by name, or **@name** for the author.
- **Filters**: update status (updates available, up to date, not on sp-mod.com, recently
  installed), enabled/disabled, category, group, and Fika compatible, hide ads, has dependencies,
  has addons, downloaded but not confirmed, has conflicts.
- The page **remembers** its filters, sort, grouping and view: it opens the way you left it. The
  search box is not kept. **Clear filters** puts the filters, sort and grouping back to the app's own.

## On each mod

- The **switch** enables or disables it. Disabling moves its files aside (nothing is deleted) and
  first checks what else needs it.
- A blue **Update** when an update for your SPT is out, and **Update all** at the top. An update
  that would break another installed mod is held back and says so: Update all leaves it out, and
  update notifications don't announce it.
- Flags when the installed version doesn't run on your SPT, or when it needs a mod that is missing
  or disabled - read from the mod's own files, so it works offline and for hand-installed mods.
- **PINNED** - see below. **RE-UPLOADED** - see below. **DISABLED**.
- Opened: installed and latest version, notes, **More details** (GUID, folders, dates, who
  installed it), **Details and versions**, **Keep through collections**, **Unsubscribe**.

## Presets

![Presets](https://raw.githubusercontent.com/trappuss/SSPTMM/main/docs/images/07d-subscribed-presets.png)

A preset is a saved set of which mods are on and which are off, as Mod Organizer 2's profiles keep
them - your Fika setup, everything off for troubleshooting, or anything else. **Presets** in the
toolbar:

- **Save the current setup as a preset...** - saves which mods are on and off right now, under a
  name. A mod with only its client or only its server half on is kept that way.
- Each preset has **Apply**, **Save the current setup over it**, **Rename...** and **Delete...**.
  The one your mods are set to now has a tick.
- **Disable all mods...** and **Enable all mods...** - for troubleshooting.
- **Put back how mods were before "..."** - returns the mods to how they were just before the last
  preset (or Disable all / Enable all) was applied. Kept after a restart, when Undo is gone.

Applying asks first and names every mod it turns off and on. It also says when a mod left on would
be missing something it needs, when it would turn off a mod sp-mod.com marks as changing your
profile, and how many mods it leaves alone: ones in the preset that are no longer installed, and
ones installed after the preset was saved (those stay as they are). Then:

- a copy of your SPT profiles is taken first - if that fails, nothing changes;
- mods are only moved in and out of their `.disabled` folders, as the switch on a mod does - nothing
  is deleted;
- **Undo** puts it back in one step;
- it is refused while SPT is running or a mod is being installed.

Presets are kept per SPT install, in `Data\mod_presets.json`.

## Pinned mods

**Keep through collections** pins a mod: applying one of your [collections](Collections) never
disables it, even when the collection leaves it out - for the HUD and quality-of-life mods you want
whatever collection you switch to. Pinned mods wear a **PINNED** tag and always sit at the top of the
list. **Stop keeping** unpins it.

## Re-uploaded mods

When an author uploads a new file under the **same version number** on sp-mod.com (with a different
size), the mod gets a **RE-UPLOADED** tag and a line saying what changed, and **Get it again**
installs that upload over what you have. A file swapped where it is hosted (for example on GitHub)
while sp-mod.com's listing stays the same can't be seen.

## Picking several

Ctrl+click, Shift+click, Ctrl+A and Esc work as in Steam's library. A bar appears at the bottom with
Select all, Update selected, Enable selected, Disable selected, Unsubscribe selected and Clear.

## The ⋯ menu

Install from file, **SPT upgrade check** (pick a newer SPT release and see which of your mods are
ready, need an update first, or have nothing yet), Rescan and more.

## Undo

After disabling, enabling or unsubscribing, **Undo ...** appears under the toolbar. Removed mods are
kept for a while (Options > Keep removed mods).
