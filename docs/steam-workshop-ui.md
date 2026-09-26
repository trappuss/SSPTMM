# Steam Workshop UI fork

This fork of TCFModManager dresses the app as Steam's Workshop: the same palette, type, layout
and wording as `steamcommunity.com/app/<id>/workshop/`, with every mod-manager feature still
there underneath. This file records **where each value came from**, **what maps to what**, and
**every place the fork knowingly differs from Steam or rests on a guess**.

Update, rebuild and run: double-click `steam-ui-rebuild-and-run.bat` in the repo root. It takes the
newest `steam-workshop-ui.bundle` from `Claude outputs\` (moving the `steam-workshop-ui` branch
forward only, and switching to it if another branch is checked out - it never drops commits),
closes this build if it is open, clears the old build output (keeping
`dist\steam-ui\Data`) and then runs the script below. It is kept out of git on purpose (see
`.gitignore`): it switches the branch, and git must not replace the script while it runs.

Build and run what is already there: double-click `steam-ui-build-and-run.bat` in the repo root. It installs a local
.NET 9 SDK if the PC has none, runs the tests, publishes `dist\steam-ui\TCFModManager.exe`, and
starts it. That build keeps its own `Data\` folder, so it does not touch an existing install.

## How the look is applied

| Piece | File | Notes |
|---|---|---|
| Palette | `src/TCFModManager.App/Themes/SteamTheme.xaml` | WPF UI 4.3.0's `Dark.xaml` with its colours replaced. Merged after `ui:ThemesDictionary`, so it wins every key. WPF UI brushes read colours with `StaticResource`, so the brushes had to be copied with them - overriding colour keys alone changes nothing. |
| Steam styles | `src/TCFModManager.App/Themes/SteamStyles.xaml` | Keyed styles only, never implicit ones (an implicit style for a WPF UI control replaces its real style - see `AppTheme.cs`). |
| Accent | `AppTheme.ApplySteamAccent` | `#1A9FFF`, tertiary `#66C0F4`, applied through `ApplicationAccentColorManager`. |
| Theme mode | `AppTheme.ApplyOnly` | Always dark, never follows Windows, no Mica. The Options theme card is hidden; the stored setting is untouched. |
| Font | `Themes/Fonts/NotoSans-*.ttf`, `App.ApplySteamFont` | See "Font" below. |
| Shell | `MainWindow.xaml` | NavigationView still hosts and caches every page, but with a minimal template (its three required parts) and no sidebar. Navigation is the hub tabs, through `Services/AppNavigation.cs`. |

## Font

Steam sets its pages in **Motiva Sans**. Its licence (quoted at the top of Steam's own
`motiva_sans.css`) restricts use to Valve, so the fork does not bundle it and does not load it
from a Steam install either. The next family in Steam's own font stack is **Noto Sans**, which
is SIL OFL 1.1 and ships in `Themes/Fonts/` with its licence. Windows does not agree with every
engine on whether Light and Medium are families of their own or weights of "Noto Sans", so
`SteamFontLight` and `SteamFontMedium` name both and the styles also set `FontWeight`; Segoe UI is
the last fallback.

## Measurements

Taken 2026-09-24 from the live site with headless Chromium (computed styles plus pixel samples):
the Garry's Mod Workshop home, its browse page, and two item pages.

**Global**
- Title bar (Steam's global header): `#171D25`.
- Workshop background: `#262B34` under a 2px grid every 80px and a 1px grid every 16px, both
  white at 6%. The item page is the older design: flat `#1B2838`.
- Text: primary `#DFE3E6`, body `#ACB2B8`, dim `#8F98A0`, minor `#61686D`, bright titles `#F1F2F3`.
- Radius 2px on buttons, fields and dropdowns.

**Hub header**: title 22px `#F1F2F3`; "Store Page" button `#1A9FFF` (hover `#56ABFF`), 13px, 36px
high; tabs 15px/500, `#C0C5CB` at rest, white on hover and when current, 3px `#1A9FFF` bar.

**Banner and strip**: banner `rgba(0,0,0,.24)`, 203px, headline 22px/800, body 16px, "Learn More"
16px underlined. Strip `rgba(0,0,0,.37)`, 47px, 15px white items with 10px padding, 2px white bar.

**Browse**
- "Browsing: ..." 18px `#F1F2F3`, hint in the light face; "N entries matching filters" 14px `#ACB2B8`.
- Search: `#1C2025` field inside an 8px `#3D4450` frame, 260x42; italic `#ACB2B8` hint.
- Sort: face `rgba(0,0,0,.37)` 283x42, 15px `#C4C4C4`; panel "SORT ORDER" with radio rows
  (`#3D4450` dot, `#1A9FFF` when chosen).
- Sidebar 300px: `radial-gradient(85.9% 84.18% at 34.28% 100%, rgba(41,50,61,.443) 27.5%, rgba(28,30,36,.396) 67.94%)`;
  headings 13px white upper case; dropdowns `#3D4450` 36px.
- Tag boxes 20x20: rest white 20%, hover `#3B3D40`, [+] on `#5DB7C7`, [-] on `#D3AC42`, black symbol.
- Cards 245px, 16px apart: `radial-gradient(62% 99% at 17% 35%, #3C4149, #1B1D21)`, shadow
  `rgba(0,0,0,.58) 1px 1px 10px`; 245px square preview on `#070707`; title 17px white, author 12px
  white 63.5%. Hover: lifts 5px, shadow becomes `rgba(255,255,255,.25)`.
- Hover popup: `#344352`, 270px, radius 2, shadow `rgba(0,0,0,.75) 4px 4px 10px`; title 17px, one
  line 13px, Posted/Updated 13px at 80%, chips `rgba(255,255,255,.09)` 12px.
- Pager: "Per page:" 16px, options 10/15/30/50 (default 30); arrows `#1A9FFF` 41x24, `#21537A`
  with a `#71757A` arrow when disabled; numbers 15px, current `#ACB2B8`.
- Gray button `#3D4450`, hover `#67707B`.

**Workshop home**: row panel `rgba(0,0,0,.4)` radius 7, 16px padding; heading 20px/600; "View All"
outlined `#1A9FFF`; row cards 183px; list tabs 20px/600; list cards two to a row, 114px high, 100px
thumbnail inset 7px, 19px title, `#3D4450` chips; content types 15px `#ACB2B8`, counts `#636B74`.

**Item page**: breadcrumbs 12px `#8F98A0`; title 30px light; tabs 14px/500 with the 3px blue bar;
header panel `linear-gradient(rgba(64,128,183,.4) 5%, rgba(64,128,183,.2) 95%)`, preview 636x358 on
black; labels `#5C8699` 13px, values `#EBEBEB` 14px, stats `#8F98A0` 13px, change-notes link
`#2F89BC`; small buttons `rgba(0,0,0,.4)` `#939393` (hover `#AAAAAA`), radius 3, 30px; subscribe box
`rgba(21,21,21,.36)` with a `#35465E` edge, "Subscribe to download" 13px light `#5C8699`, name 20px
bold; right panels 295px `linear-gradient(to right, rgba(0,0,0,.2), rgba(0,0,0,.5))`, titles 11px
upper case `#8F98A0`, required items `rgba(84,133,183,.2)` 14px `#EBEBEB`, stats numbers `#6088BB`,
labels `#939393`; "DESCRIPTION" 11px `#61696D`.

**Subscribe button** (`btn_green_white_innerfade`, Steam's `buttons.css`): outer
`#A4D007 -> #536904`, inner `#799905 -> #536904`, text `#D2E885`; hover outer
`#B6D908 -> #80A006`, inner `#A1BF07 -> #80A006`, text white. Subscribed shows a check and
"Subscribed"; under the pointer it becomes an X and "Unsubscribe" (Steam's own three states).

## What maps to what

| Steam | This app |
|---|---|
| App hub tabs (All, Discussions, ... Workshop ...) | Play, Workshop, Configs, Dependencies, (Mod footprint), (Server map), Downloads, Options, App update |
| Store Page | sp-mod.com |
| Workshop > Home | Workshop Home (new page) |
| Workshop > Browse | Browse |
| Your Items > Subscribed items | Installed |
| Your Items > Your collections | Mod lists |
| Subscribe / Subscribed / Unsubscribe | Install / installed / Remove (same code paths) |
| Star rating | Endorsements (and favourites, downloads) - the counts sp-mod.com publishes |
| Top Rated All Time / Most Recent / Last Updated / Total Unique Subscribers | Most endorsed / Newest / Last updated / Most downloaded (and the app's Most favourited, last) |
| SPECIAL FILTERS | Featured include / exclude / only; Created by Followed |
| CONTENT TYPE | Category |
| Tag rows [+]/[-] | The attribute filters: [+] Fika compatible, Has dependencies, Has addons; [-] Contains ads, Subscribed |
| Filter chips on the count line | One per filter in force: search (with its search option), content type, each SPT line, each tag, featured, followed, posted and updated dates |
| Additional Required Items dialog | The mod's missing dependencies, before the read-the-page gate |
| "Added to your Subscriptions" bar | Shown once an install asked for on the item page has landed |
| Required items | The shown version's dependencies (sp-mod.com sends no optional flag today) |
| Comments | sp-mod.com's own comments, embedded |
| Screenshots / videos | The pictures and videos in the description |
| Change Notes | Every published version with its changelog |
| File Size | The shown version's download size |

## Where the fork differs from Steam, on purpose

- **No star ratings.** The catalog has none; endorsements sit where the stars would.
- **No time frame** in the sort panel, and **Most Popular** is not offered: the catalog records no
  per-period popularity. The home page's first list is "Top Rated" (endorsements), named as such.
- **The week's row** is "released in the past week, most downloaded first" - the nearest honest
  reading of Steam's "in the past week" row.
- **Discussions, About, Awards, Favorite** are left out: nothing in the app sits behind them.
- **Tag rows with one direction.** Each app filter goes one way, so the other box is shown disabled
  rather than removed.
- **The hub header stays put** while pages scroll (on Steam it scrolls away) - it is the app's
  navigation.
- **Status badge on card previews** (installed / update / disabled, and the pin): a Workshop page
  cannot know what is on your disk; a mod manager's grid has to.
- **Quick subscribe** on a card's hover, beside Steam's quick-look magnifier.
- **Banner art and copy are the app's own**; Steam's slogan and game art are not reused.
- **Theme is always dark.** Steam has no light theme.
- **Subscribed items keeps the upstream page's three views,** multi-select and groups; it gains the
  Steam title, previews on its cards and the Steam palette.

## Second round: Browse, descriptions, item page, downloads

**Infinite Browse.** Per page gains *Infinite*, now the default: 30 cards, and 30 more each time the
page comes within about two rows of its end. The numbered pager is hidden while it is on. A page
size saved with Save as default still wins.

**Descriptions in sp-mod.com's markdown style.** The API hands descriptions over as the HTML the
site renders its markdown to. A survey of the 200 most downloaded and most recently updated mods
(2026-09-24) found: paragraphs, h1-h6, bold/italic/strikethrough, inline code and code blocks,
links, pictures (png/jpg/gif/webp, two svg), nested lists, tables, rules, blockquotes (plain and
`is-warning`), YouTube embeds and tab sets (46% of mods; they nest). `Core/Markup/SpModMarkup.cs`
reads all of it (HtmlAgilityPack) - checked against all 200: no word of text lost - and
`App/Behaviors/Markup/MarkupRenderer.cs` lays it out with sp-mod.com's measured styles:

| Element | sp-mod.com (16px body) |
|---|---|
| h1 / h2 / h3 / h4 | 30/36, 24/32, 20/28, 18/28, bold white, 16px above, 8px below |
| paragraph | 8px above and below |
| list item | 28px indent, 8px apart |
| inline code | 12.8px, `#D1D5DC` on `#364153` |
| code block | 14px on 20px lines, 14px inside |
| blockquote | `#101828`, 4px `#0092B8` bar, 16px inside, 16px above and below |
| warning | `#432004`, 4px `#D08700` bar, text `#FEF9C2` |
| rule | 2px `#333`, 16px either side |
| tab buttons | upper case, 4px 12px, radius 4 4 0 0, 4px apart; `#0F172B` white; chosen `#1D293D`, bold `#53EAFD`, 2px `#0092B8` underline |
| tab content | `#101828`, 16px inside, radius 4 4 16 16, 4px below the tabs |
| YouTube | the video's thumbnail at 16:9 on black with a play button |

Sizes scale from the site's 16px body to the Workshop page's 14px. Animated GIFs play
(XamlAnimatedGif). A link to another sp-mod.com mod opens its item page; a picture or video opens
in the full-size viewer, which steps through every picture and video of that description.

**Item page.**
- Tabs in Steam's order - Description, Comments, Change Notes - then sp-mod.com's Versions.
- Steam's highlight strip under the preview: the mod's picture, then every picture and video from
  its description (116x65 on black, `#97C0E3` 1px frame on the chosen one, Steam's grey slider).
- CREATED BY names open Browse searched for that author.
- REQUIRED BY lists the mods whose current release needs this one; MORE BY lists the author's
  other mods.
- Versions: each version's SPT range (green when it runs on your install, red when not), size,
  downloads, release date, Fika, what it needs, and *Install this version* (or *Switch to this
  version*, through the update dialog, when another version is installed).
- Subscribe shows the download's progress inside the button.
- Mouse back button and Alt+Left leave the page; Browse keeps its scroll position.

**Comments.** sp-mod.com has no API for comments; its page loads them with its own scripts. The
Comments tab shows the site's real comments section in a web view (WebView2), with the rest of
the page hidden, so they can be read, replied to and reacted to after signing in once (the
sign-in lasts: the profile is in `Data\WebView2`). Other sites' links open in the browser.
Without a WebView2 runtime the tab offers to open them in the browser.

**Downloads.** Subscribe now looks up what the mod needs before asking, so the mod and every
missing dependency are confirmed in one window instead of a second one appearing mid-download.
The read-the-mod-page gate still asks for each page, but *Open page* shows the real sp-mod.com page
inside the app (so it still counts as a visit), stepping through several with Next. Without
WebView2 it opens the browser as before; the Options switch that turns the gate off is unchanged.

**Subscribed items** has its own hub tab, and an *Update all* button when anything has an update.

**Smooth scrolling.** Every scrolling page glides: 100px per wheel notch over 200ms, easing out;
turns during a glide add to it. A list inside a page scrolls before the page does.

### Where this round differs from sp-mod.com or the upstream app, and why

- **Code blocks get a faint shade** (`rgba(0,0,0,.2)`). On sp-mod.com they sit on the page's own
  colour with no box; on the Workshop blue that read as ordinary text in another font.
- **Inline code has no rounded padding** - a box would stop a long path from wrapping.
- **Table columns** are shared out by how much text each holds; a FlowDocument table cannot size
  columns to content the way the site's does.
- **The Versions tab has no count.** The API lists fewer versions than the site's own tab counts
  (30 against 74 for SAIN) - a number that disagrees with the site would read as a fault.
- **Subscribe waits for one lookup** before its window opens. Upstream queued first and asked
  about dependencies once the download had started; asking once for everything needs the answer
  first. If the lookup cannot be made, it falls back to the upstream order.
- **Updates are measured against the newest release for your SPT**, not the newest release
  overall. Upstream compared against the newest overall and then checked its SPT, so a mod whose
  newest release was for the next SPT line never showed an update for yours (SAIN 4.4.2 on SPT
  4.0.13 read "up to date" with 4.4.3 out).
- **Required by** covers mods that have a release for your SPT, asked about at the version the app
  would install - about eight batched calls per session.
- **Under Wine, GIF thumbnails are left blank.** WPF's GIF decoding crashes the whole process
  there (reproduced with Wine 9 on every GIF tried, in a bare WPF program); description GIFs
  still play, through XamlAnimatedGif. Windows is unaffected.

## Third round: audit, Steam and sp-mod.com gaps (2026-09-25)

**Fixed after an audit of the second round.**
- The app's own sp-mod.com listing can no longer be opened as an item page or installed, from any
  link (a changelog link to it would have offered a working Subscribe that put the manager into
  the SPT folder).
- Infinite Browse stays bounded: installs, removals and addon counts redraw only the cards that
  changed instead of rebuilding the whole list; the picture of any card more than two screens from
  the view is let go (and comes back from the cache as it nears); the thumbnail cache is a
  least-recently-used cache with a byte budget instead of growing for the whole session.
- "Has dependencies" gets its data again: the lookup runs when a card is drawn, not only when its
  hover popup opens.
- `steam-ui-rebuild-and-run.bat` compares the bundle with the `steam-workshop-ui` branch itself,
  not with whatever is checked out, so it can no longer move that branch past commits of yours
  while another branch is checked out; both scripts work from a network share and with `&` in the
  folder path.
- Home redraws after Browse's installed index has caught up (it raced it before); a mod that
  cannot be opened says why on Home and on the item page; a description link that cannot open
  its item page opens in the browser.
- A failure reading the install during Subscribe's lookup falls back to the queue's own check
  instead of the crash dialog.
- Late WebView2 starts no longer play a video that was closed, show comments for an item no
  longer open, or open pages in the browser a second time.
- The picture viewer opens over the update dialog; mod links inside that dialog go to the browser
  (the item page would open underneath it).
- Subscribed items: only the page's own instance rescans after installs, and a rescan that finds a
  scan running waits for it instead of being dropped.
- Required by tolerates a mod stored twice in the catalog and asks again, two minutes on, after a
  lookup that could not reach every mod.
- Formatted tooltips ("12 downloads") show their format; times are in this PC's time zone (they
  were UTC); item page dates carry the year when it is not this year, as Steam's do.
- The Change Notes count is every version, not the twenty loaded.

**From Steam.**
- *Additional Required Items* - Steam's own dialog when Subscribe finds missing requirements:
  Subscribe to Just This Item / Subscribe to All / Cancel. The list's pages open from it and count
  for the read-the-page gate that follows.
- Steam's modal dialog (newmodal): `#25282E` with a top-left glow, the `#00CCFF`-`#3366FF` line
  along the top, a 22px bold title, 16px `#ACB2B8` text, green/blue/grey buttons and the page
  dimmed to 80% black behind it. The read-the-page gate is drawn the same way.
- The *This item has been added to your Subscriptions* bar under the subscribe box.
- Change Notes as Steam's page: "Showing 1-20 of 30 entries", an "Update: Sep 24 @ 2:34AM"
  headline in light blue, the version and its SPT range where Steam names the author, the notes in
  a dark box, and *Show older updates*.
- Filter chips on Browse's count line, each removing its filter; *Clear search* in search fields.
- The sort panel's and the Your Items menu's measured gradients; SORT ORDER in Steam's order and
  wording (Total Unique Subscribers, in Steam's own translations).
- Steam's tooltip: a light grey gradient with dark text.

**From sp-mod.com.**
- The site's two notices, in its words and colours: the multiplayer-cheat warning (red) and the
  profile-binding notice (amber), above the subscribe box.
- VirusTotal scans per version (Versions tab) and for the shown version (header), from
  `include=virus_total_links`.
- The licence links to its text; the plugin GUID; source code links (`include=source_code_links`).
- A version's dependencies name the newest accepted version: "BigBrain (1.4.0)".
- A mod whose page has no comments section says so on the Comments tab instead of showing the
  whole page there.
- The "Contains AI content" tag row is gone: the API has no such field (asked 2026-09-25, it
  refuses `filter[contains_ai_content]` as an invalid filter), so it could never hide anything.

### Where this round differs, and why

- **Enter chooses Subscribe to All** in the required-items dialog; Steam's Enter chooses Just This
  Item. A Workshop game fetches what an item needs regardless; SPT does not, and a mod installed
  without its requirements fails to load, so the key that answers without reading gives the
  working install. The dialog's second and third sentences are the app's own: a click opens the
  sp-mod.com page, and the list already holds everything the items need in turn.
- **One tooltip style.** Steam's legacy item page uses a flat grey `#C2C2C2` tooltip; the newer
  Workshop pages' gradient is used everywhere.
- **Menus are not blurred behind.** Steam blurs what is under its menu panels; a WPF popup cannot.
- **The SPT line ticked by default shows as a chip.** Steam has no default tags; here the install's
  own line is ticked on opening, and the chip says so.
- **The infinite list is not virtualised.** The page scrolls as one (banner, sidebar and grid), and
  a virtualising panel needs to own its scrolling; the pictures are what is released instead.
- **The notices keep sp-mod.com's colours**; Steam has nothing like them.

## Fourth round: collections, right-click, Quick View, Play (2026-09-25)

**From Steam.**
- *Collections as Workshop pages.* A mod list opens as Steam's collection page: title bar and
  control row, ITEMS (n) with Subscribe to all / Unsubscribe from all / Save to Collection, rows
  with the square subscribe button, the authors' chips and Posted/Updated. An item opened from it
  goes over it; Esc (or back) returns to the collection. *Manage in Collections* opens the list
  on the Collections page.
- *Add to Collection* on the item page, in Quick View and in the right-click menu: tick the lists
  that should hold the mod, or name a new one. The item page lists *In N of your collections*.
- *Quick View* - the magnifier on a Browse card (and in the right-click menu): pictures, CREATED
  BY, posted/updated/file size and the counts, TAGS, the start of the description, Subscribe, Add
  to Collection and See More; the arrows (and ←/→) step through the list it was opened from.
  Measured from Steam's live page.
- *Search options* under Browse's gear: Title & Description / Title Only / Description Only, with
  Steam's chip wording ("Results for: "x" (title only)").
- *Filter by Date*: posted between, last updated between, with Steam's chips.
- *Follow* on an author (CREATED BY on the item page, and the right-click menu), *From Followed
  Authors - Recently posted items from authors you follow* on the front page, and *Created by
  Followed* among Browse's SPECIAL FILTERS.
- *Right-click menu on any mod* (cards, rows, related and required items, Subscribed items), in
  Steam's popup menu style (`#3D4450`, `#DCDEDF` 12px items, measured from shared_global.css):
  Open, Quick View, Subscribe/Unsubscribe, Update, Add to Collection..., Copy link, View mod page,
  the author's Workshop, Follow/Unfollow.
- Descriptions in Steam's BBCode styles (headings, quotes, code, tables, rules, links); tabs, notices
  and inline code keep sp-mod.com's features in Steam's palette.
- The Browse hover popup shows the mod's pictures under its title, a new one every two seconds when
  it has more than one.

**From sp-mod.com.**
- Descriptions are searched with sp-mod.com's own full-text search (the cached catalog has no
  descriptions). It answers with its best 20 matches at most (seen on every query tried), so a
  common word finds the 20 it ranks first; names and teasers are still matched locally in full.
- *Held-back updates*: after each scan of Subscribed items the whole install is checked with
  `/mods/updates`. An update that would break another installed mod ("Update to 3.0.6 held back:
  Black and Blue needs ~2.0.24") is said on the card, the item page and the status tooltip, and
  Update all / Update selected leave it alone (its own update dialog can still install it).
- *Passed Verification* (sp-mod.com's shield and words) on versions whose download sp-mod.com has
  checked, with *Files in this download* from the file-tree endpoint.
- YouTube videos play in the picture viewer (they showed a player error: YouTube refuses embeds
  opened without a web page around them, so the app serves one).

**The app.**
- Play: while the server runs, Start server becomes a red *Stop server* (asked about on the card
  first, like Restart). Options > *Starting the game*: open the SPT launcher as soon as the server
  listens on its port (http.json's port and 6969, ports already taken before the start not
  counted), within three minutes.
- Scrolling: while a page glides its content stops reacting to the pointer (no lift, hover popup or
  slideshow firing under a still pointer) until the glide ends; card bodies, their shadows, the
  collection frames and the window's grid background are drawn once into bitmaps and reused; the
  glide is 250ms.

### Where this round differs, and why

- **Unsubscribe from all sets the items aside (disabled) rather than deleting them**, after one
  confirmation; they come back from Subscribed items. (Fifth round: removing them is offered too.)
- **Subscribe to all hands over to the Collections page**, which shows what will change before
  anything does (Add Only applies the list additively, Overwrite My Subscriptions exclusively).
- **A collection made from Add to Collection applies additively** (it holds what you picked, not a
  whole install).
- **Quick View's stats**: Downloads and Favorites stand where Steam has Visitors and Subscribers;
  the endorsements stand where the stars are; there is no Favorite, vote or award button.
- **The right-click menu is the app's**: Steam's pages have no menu on an item.
- **Follows are kept on this PC** (settings.json), by sp-mod.com user id: sp-mod.com has no
  following of its own to sync with.
- **Mods with several credited authors count as theirs** for From Followed Authors and Created by
  Followed, as the CREATED BY panel lists them.
- **Stop server asks first**, as Restart does: a raid in progress does not survive it.

## Fifth round: file clashes, removing, followed authors, SPT fit, measuring (2026-09-25)

**From sp-mod.com.**
- *Files already there.* Before a mod is queued, the chosen version's file list (sp-mod.com's
  file-tree, kept for versions that passed its file check) is placed exactly as the install would
  place it. Files already on disk that belong to another installed mod, or to nothing this app
  installed, are listed with whose they are, and the install waits for *Install Anyway* (Cancel is
  the default). Not counted: the mod's own files (an update over itself is not asked about, nor a
  file its record already shares with another mod after an Install Anyway) and user data the install
  leaves in place (SVM's presets). A list sp-mod.com cut short says so. The items a mod needs,
  when they are installed along with it, are asked about the same way, each on its own. A version
  with no file list (not verified, or offline) installs as before.
- *Not for your SPT.* `/mods/updates` also names installed versions that do not run on this
  install's SPT and have no newer version that does. Subscribed items says so on the card (a warning
  glyph and the tooltip) and the item page, naming the newest version that does run here when there
  is one - sp-mod.com's pick, or else the newest catalog version whose SPT range covers this SPT.

**The app.**
- *Authors you follow*, under Your Items: each followed author with picture, how many items the
  catalog has from them and their newest date, *Their items* and Steam's Follow/Unfollow button;
  *View all their items* opens Browse filtered to Created by Followed. Steam's Workshop has no page
  listing followed authors; this one is laid out like its item rows.
- *Unsubscribe from all* offers *Set Aside* (the default; it asks about mods that need them, as
  setting aside always has), *Remove* or Cancel. Remove deletes the mods as Subscribed items'
  Remove does, config files copied aside first; when installed mods left behind use them, or
  hand-installed folders would be deleted, it lists those and waits for *Remove Anyway*. Set-aside
  items are skipped and said so, and anything that did not go cleanly is said.
- *Measuring on a real PC.* `steam-ui-measure.bat` starts the built app with `TCFMM_PERF=1`: for
  each second something scrolls it logs frames per second and the longest stall, and for each
  description how long it took to show; the .bat turns that into `Claude outputs\perf-report.txt`
  with the graphics card, screen and WPF's render tier. Off (and costing nothing) otherwise.

- *The page stays still when a card's Subscribe is clicked.* The button goes disabled while it
  works; keyboard focus then fell back to the card grid, which WPF scrolls into view, so Browse
  jumped to the grid's top. Lists of items are no longer focusable anywhere, and scroll areas no
  longer draw a focus frame (it outlined the whole item page after Subscribe's questions closed).

### Where this round differs, and why

- **The file-clash question is the app's own.** Steam gives every item its own folder, so it never
  has to ask; SPT mods share BepInEx and user/mods.
- **Descriptions: see the sixth round.** (The 510-550ms first measured here for SAIN turned out to
  be the whole item page opening, not its description - see below.)

## Sixth round: descriptions, lighter without changing them (2026-09-25)

Every description shows exactly what its author wrote, as before - the same text, pictures, GIFs,
tabs, tables and spacing. What changed is only when and how often the work is done. Measured under
the build machine's software renderer, with a harness that opens an item page by id and logs every
frame that took 50ms or more:

- *Long descriptions go in a part at a time.* The first part (a screen or two) is laid out with the
  page; the rest follows in parts of about 3,000 characters, each after the window has drawn and
  answered input. A long list or section is itself handed in item by item, and tabs inside a
  description go in the same way. MoreCheckmarks (12,000 characters, no tabs): the window was held
  still 1.13s as its page opened; now 0.77s, then four parts of 50-170ms. It is one document the
  whole time, blocks only held back and added in order: screenshots of the finished description
  laid out whole and in parts match pixel for pixel (one line differed by 3 of 255 in brightness,
  anti-aliasing). If cutting a document into parts ever fails, it is shown whole.
- *GIFs pause while none of them can be seen* - scrolled out of view, or on a hidden page or tab -
  and carry on from the same frame when any of it comes back. Fontaine's FOV Fix with its GIF off
  screen: the app's processor use went from 12.7% to 0.2%; on screen it plays as before.
- *Pictures are kept for the session* (up to 96 MB, least recently used out first), shared by
  descriptions, the item page's pictures, Quick View and the picture viewer. An item opened again
  shows its pictures at once instead of fetching, decoding and laying the page out again for each.

Measured and not changed:
- Most long descriptions on sp-mod.com are split into tabs by their authors (every one of the 40
  most downloaded mods over 5,000 characters but two), and only the chosen tab is laid out. For
  those the description is a small part of opening the page: SAIN's page held the window 0.84s with
  its description and 0.84s with none; a mod with a 1,200-character description, 0.42s. The rest
  is the page around the description; which part of it costs that was not measured this round.
- Decoding pictures at the size they are shown instead of up to 1,600px wide made no difference to
  scrolling (13.8 and 13.7 frames a second), so pictures are left as they were.
- Scrolling was 13-15 frames a second on every page tried, with or without a description - the
  software renderer's own ceiling. steam-ui-measure.bat measures it on a real PC.

## Seventh round: hover on Home, scrolling switch, pictures, item cover (2026-09-25)

**From Steam (measured on the live pages).**
- *Workshop Home reacts to the pointer like Browse.* On Steam's front page the carousel cards and
  the list rows lift 5px with the white glow, open the same hover popup as Browse and show the
  Quick View magnifier. Home's carousel, From Followed Authors and list rows now do all of that, with
  the app's quick Subscribe beside the magnifier. The popup is one control (CardHoverPopup, attached
  by CardHover) built the first time it opens.
- *The popup's teaser wraps to five lines*, as Steam's does (-webkit-line-clamp: 5 on 19px lines;
  the earlier "one line" was an item whose text was one line long). The popup was also being cut at
  WPF UI's tooltip width; it is 270px like Steam's now.
- *The item's picture heads the item page's right-hand column*: 268px square, 5px above Content
  Type, the mod's own picture cropped square as Browse's cards crop it; a click opens it in the
  picture viewer.

**The app.**
- *Options > Scrolling*: smooth scrolling on (the default) or off. Off, a wheel turn moves the same
  distance at once. Saved, and applied without a restart.
- *Pictures that did not load after scrolling.* Reproduced through a 1 MB/s link: after a fast
  scroll down Browse, half the cards on screen were still blank six seconds later. Downloads waited
  in the order asked for, six at a time, behind every card the scroll had passed (about 380 KB
  each), and one that failed was never tried again. Now pictures on screen go first; ones nothing
  wants any more are dropped before they are fetched and stopped part way; one download serves
  every picture asking for the same file; a failure that may pass is tried twice more; a picture
  still missing when it is shown again is asked for again. Same test after: 12 of the sampled
  places blank at six seconds instead of 30. Description pictures retry the same way.
- *Smaller copies of mod pictures.* sp-mod.com keeps its newer mod pictures (40-character names)
  at 192 and 384 pixels wide as well, in WebP, and shows those on its own pages. Where one covers
  the size shown, that copy is fetched: 30 times less to download on a sample of 23 (9.7 MB against
  0.3 MB). Older pictures (named by number) have none, and a card on a screen scaled past 150%
  needs more than 384 pixels, so those fetch the full picture, as does a machine that cannot read
  WebP - probed once at start and written to the log. The build machine cannot read WebP, so the
  smaller copies could not be seen working here; the full-picture path is what was tested.

## Eighth round: required items, unsubscribing, Subscribed items (2026-09-25)

**Fixed.**
- *A mod's dependencies were not offered again after they were removed.* Install Looting Bots with
  BigBrain, unsubscribe from both, subscribe to Looting Bots again: no Additional Required Items,
  and BigBrain was skipped. The queue counted its finished downloads as still queued; only ones
  still to run or running count now. Reproduced and checked with that mod pair.
- *The item page's REQUIRED ITEMS ticks the ones installed* (Steam's subscribed tick, `#66C0F4`),
  and updates as they are installed or removed.

**Unsubscribing.**
- *The question before one item is removed is Steam's modal* (it was a Windows message box), with
  *Don't ask again*. Ticked and answered, it turns the question off; ticked and cancelled, nothing
  changes. *Options > Unsubscribing* turns it back on. Unasked, config files are always kept (set
  aside in LegacyConfigs, as the question's first answer does), and a warning still comes first
  when other installed items use what is going or a hand-installed item's folders would be deleted,
  because the question was what listed those folders.
- *Multi select > Unsubscribe selected* removes every ticked item, addons and hand-installed ones
  included, the way a collection's Unsubscribe from all does: config files kept, the same warning
  first. Its question names the items, since ticked ones can be hidden by filters set since, and
  set-aside (disabled) ones are left alone and said so.
- *Where "Unsubscribe from all" is:* on a collection's page - Your Items > Your collections, pick a
  collection, View collection - above its items. It asks whether to set the items aside or remove
  them.

**Subscribed items.**
- *Per page: Infinite*, as Browse has it: no pager, 24 more cards as the list nears its bottom
  (Cards view builds each card, so not every card at once). Saved as default like the other sizes.
- *Groups > Sort groups: Category*: one section per sp-mod.com category, A to Z, items with none
  last, each foldable for as long as the app is open. Your own groups come back with any other
  choice; items cannot be dragged into a category, and the group controls step aside meanwhile.
- A card's buttons wrap onto a second line instead of cutting Unsubscribe off.

## Ninth round: copies of a mod, removing, Multi select everywhere (2026-09-26)

**Fixed.**
- *Unsubscribing a copy of a mod deleted the real one.* With a mod on disk twice - the install
  and a renamed copy, which matches the same listing by its GUID - both cards were given the
  install's record, so removing the copy deleted the files the record placed and left the copy.
  Reproduced with three renamed copies of Open Sesame's DLL. Now only the card holding a folder
  the record placed (judged on its client half, since a copy can be paired with the install's
  server half) is the install; a copy is shown as installed by hand and removing it deletes the
  copy. Browse's installed index, and so the item page's Unsubscribe, prefers the install.
- *"Removed X." was replaced at once* by the page's count from the rescan after it. It stays now,
  and the item page and right-click menu show the removal's own words.
- *A removal from the item page or right-click menu* works on a fresh scan, matched even when the
  mod was set aside or brought back since Browse last looked - a set-aside mod used to be
  "removed" by deleting only its record, its files staying where they were set aside.

**Removing.**
- *The question before one item goes names installed items that use it* (for example SAIN, which
  needs BigBrain), as the check before removing several already did.
- *The check before removing is worded for one item or several* and names whose folder each is.
- *Unsubscribe selected* shows the folder or file beside items that share a title (two copies of
  one mod).
- *Don't ask again* lines up with the dialog's text: WPF UI's check box keeps 11px of padding
  before its box (CheckBoxPadding 11,5,11,6), now taken off.

**Multi select in every view.** List and Groups have it too: a tick box on each row, a click on a
row ticks it, the same ticks in all three views; the group bar steps aside while selecting. A
click on a card's or row's header ticks it (WPF UI draws the header inside the expander's own
toggle, which used to open it instead).

**On your PC.**
- *Smaller picture copies*: your log says WebP can be read, so they are asked for. From this build
  the log also says, once a session, whether one was shown or had to give way to the full picture.
- *Scrolling*: still to be measured - run `steam-ui-measure.bat`; it writes
  `Claude outputs\perf-report.txt`.

## Tenth round: downloading without the browser trip (2026-09-26)

Downloads themselves were already quick (about a second each in the log from your PC); the slow
part was having to open every mod's page in a browser first. That page is where authors put
install steps, requirements and warnings, and the app already shows it on its own item page.

**The mod's page, in the app.**
- *Subscribe on the item page asks nothing more* - its page is the one on screen. Its required
  items are still asked about (Additional Required Items) and shown as below.
- *Everywhere else* (Browse's quick +, right-click, collections, mod lists, addons, the
  Dependencies page, Update selected) the dialog shows each item's page right there - Read here,
  the first one already open, rendered as the item page renders it - with Continue from the start
  and Open page still there. For an update it shows that version's change notes instead.
- *The update dialog asks nothing more*: it already shows every version's change notes.
- The app's own update keeps asking for its page to be opened (its release notes are there).
- Not done on purpose: loading the page quietly to tick a box (nobody would see the warnings, and
  the visits would count as views on the author's page).

**The queue.**
- Items are prepared in the order queued (version, dependency question), *downloaded up to three
  at once*, and installed one at a time, in order, as each download is there.
- *Downloads are kept* in Data\Downloads, up to 4 GB (those used longest ago go first; one file
  larger than that is not kept), so installing the same version again - subscribing again, putting
  a version back, re-applying a mod list - does not download it again (a second download would
  also count again on the author's page). An archive that fails to install is not kept. Options >
  Downloads stops keeping them, and its button deletes the ones kept.
- *A download that fails in a way that can pass* (connection dropped, host busy, file arrived
  short) is tried twice more, after 2 and 6 seconds.
- Not done on purpose: downloading ahead when pointing at a mod - every download goes through an
  sp-mod.com link that counts it.

**Fixed.**
- *The item page built every change note while its tab was hidden*: opening SAIN a second time
  built twenty of them with the description, doubling the time before it showed (241 to 495 ms on
  your PC; reproduced here). They are built when the tab is opened now.
- *Browse's installed mark stayed on a mod that had been set aside*; it follows now.

**Measuring.** Browse's stalls on your PC (up to 362 ms) did not show here: the app's own thread
was never busy for long under Wine, whose drawing is the slow part. `steam-ui-measure.bat` now
also lists what kept the app busy for 50 ms or more, by name, so the next report shows what they
are before anything is changed for them.

## Eleventh round: collections from sp-mod.com, playing GIFs (2026-09-26)

sp-mod.com's public mod lists (sp-mod.com/lists) are the Workshop's collections now. The API has
no list endpoints, so the app reads the site's own pages (`Core/SpModLists`); searching and the
SPT filter go through the page's own Livewire request, the way the site's page asks for them.

**Browse menu.** Browse opens Steam's two-column menu under the pointer: MODS (Top Rated All
Time, Most Recent, Last Updated - Steam's Most Popular has nothing to be measured by) and
COLLECTIONS (Most Recent, the only order sp-mod.com has). A click on Browse still goes to the
mods. Your Items opens under the pointer too, and on a click as before.

**Browsing: Collections.** Steam's collections browse page, measured: two cards to a row, 152px
high, the list's picture at the left (sp-mod.com's list glyph when it has none), title, "By
author", three lines of description, "Contains N items". Where Steam has stars: the SPT version
the list was made for, green when it is yours. A card of a list you subscribed to says so. Search,
SPT version filter, pages of twelve. A list's address pasted into the search box opens it.

**A collection's page** (public lists): the page as for your own collections, plus its picture,
who made it, "For SPT x", its description exactly as written (folded after a few lines; Show
more), each item's note from the list's author, and for each mod the version it gets on your SPT
version - or "No version for SPT x" - and whether the one installed is older. A line over the
items says how much of it you have: "12 of 96 mods subscribed · 3 can be updated · 2 have no
version for SPT 4.0.13 · 1 is no longer on sp-mod.com". View on sp-mod.com opens it there.

**Subscribe to all.** A list on sp-mod.com names mods, not versions (the version its page shows
is the newest for the list's SPT, worked out as the page is drawn). So subscribing pins each item
to the newest version for the SPT version it will run on:
- your SPT version when the list was made for it;
- when it was made for another, you choose: *For My SPT* (items with no version for it are left
  out) or *For the Collection's SPT*;
- addons get the newest release that fits the version of the mod they go with.
Items left out are named in the Add Only / Overwrite question, with why. The collection is then
kept as one of your collections (imported, "from" its author, linked to its page - subscribing
again brings the same copy up to date) and the Collections page shows what applying it will do
before anything does, as for your own. Nothing is stored if you cancel. Save to Collection makes
a copy of your own; Unsubscribe from all works as before. Opening your copy later reads the list
from sp-mod.com again, so what its author has changed since shows; offline, the copy shows.

**GIFs.** No mod's cover on sp-mod.com is a GIF today (every catalog thumbnail was checked), but
wherever one can be shown it now plays while on screen and stops off it: card covers, the hover
popup's slideshow, Quick View, and the item page's picture strip (which is where GIFs from
descriptions appear - Live Flea Prices' install video, for one, verified playing under Wine).
Description GIFs already played.

**Fixed.**
- *Installing from a downloaded .7z/.rar froze the window* for the whole install (since the tenth
  round's downloads-ahead); it runs off the window's thread again.
- *A list entry with no version installed the OLDEST release*: sp-mod.com answers oldest first
  unless asked otherwise. Now it is the newest for your SPT version, then the newest.
- Cancel then Retry on a download no longer fails the retry or shows the dependency question for
  the cancelled attempt; cancelling one waiting to install no longer sticks at "Cancelling..." or
  keeps its file marked in use.
- Browse redraws its installed marks once per run of installs, not once per install (a
  forty-mod collection was forty full scans).
- The app's own listing is never installed from any list.

## Twelfth round: authors' pages, the collection page again, tabs and background (2026-09-26)

**Browsing: Collections.** Every public list is read once an hour (27 pages, about seven
seconds; the first time, page 1 shows while the rest is read and a line counts the pages) and kept
in Data\collections_index.json. So search now also matches who made a list (sp-mod.com's own
search does not), and there are six orders: Most Recent, Oldest, Title A-Z, Title Z-A, Most Items,
Fewest Items. Per page: Infinite (more as you scroll - the default), 10, 15, 30, 50. Under the
pointer a card opens Steam's popup, measured: title, description, "Contains N items", the first
ten items' pictures and "+N" for the rest (read from the list's page once the pointer rests).

**An author's page** (Steam's myworkshopfiles), opened from every author's name: CREATED BY and the
last breadcrumb on an item page, "Created by" on a collection's rows and its author chips,
COLLECTION ASSEMBLED BY, Quick View, the right-click menu and Followed Authors. Header with their
picture and name (their sp-mod.com cover behind it), tabs Workshop Items and Collections, the
paging bar ("Showing 1-9 of 37 entries", Steam's page buttons, Per page: 9 18 30), items as
Steam's 200px squares with sp-mod.com's counts where Steam's stars are, their public lists as
Steam's collection rows (with the hover popup). Right column: followers on sp-mod.com, Follow,
Member since, Search their items in Browse (the old behaviour of a name, with Browse's filters),
View on sp-mod.com. Items come from the catalog (owned or credited); who they are and their lists
from their sp-mod.com page, read once a session. Someone with lists but no items opens on
Collections.

**The collection page, again,** to Steam's current collection page: Description / Comments tabs;
the header 950x470 with the title bar over the list's picture - or, without one, eighteen of its
items' pictures six by three (the first item's picture when there are fewer different ones);
the control row (Manage, Share = copy its address, View on sp-mod.com); then two columns - on
the left DESCRIPTION and ITEMS, each under Steam's rule and heading; on the right Steam's
gradient panel with the author chips, COLLECTION ASSEMBLED BY with their picture, and the
numbers (Items, Posted, Updated). Comments is sp-mod.com's own, as on an item page (the web view
code is now shared: Services\SpModComments); on that tab the comments come straight under the
tabs. A breadcrumb leads to the maker's page.

**Going back.** A collection, an author and an item page each open over the page area, and the
one they were opened from waits underneath: Esc (or back) walks back through them in order -
an item opened from an author opened from a collection returns to the author, then the
collection. Before, opening a collection from an item page closed the item.

**Options > Tabs and background.** The Subscribed items tab and a new Collections tab (beside it,
opening Your collections) can each be switched off; both pages stay under Workshop > Your Items. A
picture of your own can take the place of the Steam grid behind the whole window, with a slider
to darken it and Use the Steam grid to go back. The picture is copied into Data\Background, so
moving or deleting the original changes nothing. The Workshop pages over the page area (item,
collection, author) keep Steam's solid #1B2838, as Steam's own pages do.

## Thirteenth round: a collection's Quick View, Favorites, Update all, addons (2026-09-26)

**A collection's Quick View.** Steam's collection cards carry the magnifier too (measured: the
same shell as an item's Quick View, one column). Under the pointer each card on Browsing:
Collections shows it; it opens the title, CREATED BY (a link to their page), UPDATED, the
description, CONTAINS N ITEMS over nine of its items' pictures and "+N" for the rest (a picture
opens that item), Favorite, and See More (the collection's page). Arrows and the arrow keys step
through the cards on the page; Esc, the X or a click beside it closes it. Steam's rating,
visitors, tags, votes and awards have nothing behind them on sp-mod.com and are left out; the SPT
version sits where the rating does, as on the cards. "Contains N items" and "+N" count the entries
sp-mod.com says are no longer available too, so they match the card (and the hover popup now adds
up the same way).

**Favorite.** Steam's word for keeping a collection. The button is on a public collection's page
(before Share, where Steam has it) and in its Quick View; Your Items > Favorites lists them as
Steam's collection rows with the hover popup. sp-mod.com has no favorites for lists, so they are
kept in settings.json. Each favorite remembers what its page said the last time it was opened here
(when it was updated, how many entries); the Favorites page reads each one and marks those changed
since - "Changed since you last opened it" - until opened again. Removing one on that page and
putting it back keeps what had been seen. The label "Favorited" once pressed, and "Favorites" in
the Your Items menu, are HUNCH: Steam's signed-in menu and pressed button could not be seen
signed out.

**Update all on a collection.** Beside Subscribe to all, while any of its subscribed items has a
newer version for your SPT: "Update all (N)". It goes through exactly what Subscribed items' Update
all does (moved into Services\ModUpdates so the two cannot drift): one warning for hand-installed
mods, the page gate once for the lot with each version's change notes, then the download queue.
A public collection's item updates to the version the page names for your SPT; one of your own
collections' items only when Subscribed items itself says an update for your SPT is available. Never
an addon, a disabled mod, or a version sp-mod.com holds back.

**Addons on an author's page.** A tab when they have addons (sp-mod.com's; Steam has none): the
same squares as their items with "Addon for <mod>" under each; a click opens that mod's page, which
lists its addons with Install (the addon's own sp-mod.com page when the catalog lacks the mod).
Someone with addons and no items opens on it.

**Followers, measured again.** On Steam the count and "Followers" sit side by side (18px, 20px
over, 8px down), with a line under them on what following does; now as Steam has it. The line says
what following does here.

**Fixed.**
- A collection's page opened while the hover popup or Quick View was still reading it could fail
  with "A task was canceled": the shared read was tied to whoever asked first.

**import-bundle.bat** (in Claude outputs, next to the bundle): verifies the bundle, stops if there
are uncommitted changes to tracked files, then moves steam-workshop-ui forward to the bundle -
fast-forward only; if the branch has commits the bundle does not, it says so and changes nothing.

## Fourteenth round: the tool itself - data safety, right answers, network, new tools (2026-09-26)

Not the look this time: what the app does. Five audits of the whole codebase, each finding
reproduced before it was fixed, every phase reviewed again by a separate agent until nothing it
found was left, and each checked under Wine.

**Your data is not lost any more.**
- The fork never installs the upstream app over itself (the next upstream release would have
  replaced it); App update still says when a newer upstream version is out.
- Settings, collections, install records and the other data files are written so a crash or power
  cut leaves the old file or the new one, never half; the ones you make keep a `.bak`. A damaged
  file is kept aside as `<name>.corrupt-<time>`, the backup is put back, and a message says so. A
  file another program holds open is never saved over with defaults.
- One copy of the app per Data folder: a second start says so and closes.
- Installing is undoable at every step (a journal in the install's work folder): a failure, or the
  app closing half way, puts the install back exactly as it was - previous version included - at
  once or at the next start. Nothing that was there before is ever deleted by an undo; an undo that
  cannot finish (a file held open) holds every other change until it has.
- Files an install replaces - one placed by hand, another mod's copy of a shared library - are
  kept and put back when that mod is removed; mods stacked on one file (hand copy, A over it, B over
  A) end with the original whichever is removed first. A file another installed mod also lists is
  never deleted. BepInEx `.cfg` files in an archive never replace your settings.
- Read-me files and pictures beside an archive's content are not dropped into the SPT folder; a
  `plugins/` folder without `BepInEx/` around it goes where it belongs.
- Importing a list file of one of your own lists asks (import beside it, replace, cancel) instead
  of turning yours read-only.
- **SPT profile backups** (Options): `user/profiles` is zipped before installs, updates, removals,
  disabling and applying a collection, whenever the profiles changed since the last copy; the last
  10 per install are kept; Put back copies what is there first and refuses while SPT runs.

**Right answers.**
- A dependency a major version past what the mod needing it was made for reads "too new", not
  installed. Before an install, a required mod that is disabled, too new, not an accepted version,
  in conflict, or has nothing for your SPT is named, and you can stop there (asked once per batch).
- Subscribed items: a mod whose own files say it needs something that is not installed or is
  disabled says so - offline, and for mods installed by hand.
- Versions: 1.2.0-beta then 1.2.0 is an update; "-hotfix"/"-fix"/"-patch" come after their release.
- Offline with no saved catalog, Subscribed items still lists your SPT folder; a failed catalog load
  is tried again (after a minute) instead of leaving every page empty until Refresh.
- A config value whose type the new version changed is not carried over; `.json5` configs save.
- Play: "running" means running from this install.

**Speed and network.** The catalog is fetched compressed (about a quarter of the size) and not at
all when the saved copy is under 20 minutes old; one failing page is asked for again rather than
losing the other thirty-eight; a download with nothing arriving for 60 seconds gives up and is
retried, resuming where it broke off when the server can send exactly the rest of the same file;
the Server Map key is never sent on the handshake, and only to a server that is pinned or has
answered as one.

**New.**
- **Install from file...** (Subscribed items): an archive you already have (.zip, .7z, .rar, .tar,
  .tar.gz), installed the same way as a download. Matched to its sp-mod.com page by its plugin ID
  when exactly one fits; otherwise kept as a local item (named from the file, identified by what is
  inside), removable like any other, after saying what that costs. Your file is copied, never moved.
- **SPT upgrade check** (Subscribed items): pick a release; each installed mod is ready, needs an
  update first (to which version), has nothing yet, or has to be checked by hand.
- **Server log** (Play): the server's own log file, live, under its card - the server still runs in
  its own window.
- **Earlier versions of a config** (Configs): the copies kept at each save, brought back through
  the editor.
- **Fika**: a version sp-mod.com marks as not for Fika is asked about before it goes onto an install
  that runs Fika.

**Not done, and why.**
- sp-mod.com's recommended update version (`/mods/updates`) is not used for the update pick: the
  version comparison fix covers the wrong answer it would have fixed, and changing the pick means
  changing every update path at once.
- Dropping an archive on the window: the Subscribed items page's own drag and drop (groups) would
  have to be reworked to tell the two apart; the button does the same.
- Install records per SPT install (asked about before doing it, as said at the start).

## Values that could not be measured (marked HUNCH in the source)

- The smooth-scroll distance and time (100px, 250ms) - chosen to feel like a browser, not measured.
- Whether pointer churn during a scroll was a large part of the stutter: on this build machine the
  app runs under Wine with software rendering, whose own floor (~15 frames a second on a plain page)
  hides it. Please say whether scrolling is better on Windows.
- The Follow button's words ("+ Follow", "Following", "Unfollow" under the pointer): Steam only
  draws that button for someone signed in. Its colours are measured.
- Quick View's button hover colours (Steam shows them disabled until signed in).
- A collection's Favorite once pressed ("Favorited", a filled blue star) and the Your Items menu's
  "Favorites" entry: both only show for someone signed in.
- The page behind Quick View is darkened to about 40% (measured from a screenshot, not the CSS).
- The thumbnail cache's budget (160 MB) and how far from the view pictures are kept (two screens).
- Round 14, not checkable here (no real SPT 4 install on this machine): that Fika's plugins all
  use GUIDs starting "com.fika." and its server mod's folder name contains "fika"; that the SPT 4
  server writes its log under `<server folder>/user/logs` (the newest file there is shown, whatever
  it is called); that profiles live in `<server folder>/user/profiles`. Each is read from disk, so a
  different layout shows nothing rather than something wrong.

- Critical/error red (`#E05A5A`): no error state on the pages measured.
- Outlined "View All" hover fill.
- Whether Steam's file sizes count in 1000s or 1024s ("22.946 KB" reads as 1000s).
- The logged-in "Subscribed items" page itself: it needs a Steam login, so its layout was not
  measured; the page keeps the upstream layout with Steam's title and styling.

## Wording and translations

Steam's terms replace the app's where Steam has one: navigation, page titles, the main buttons,
sort and filter labels, and the Workshop pages. Messages about files, configs and installs keep
their precise wording - Steam has nothing to say about a BepInEx folder.

For German, French, Italian and Russian, every Steam term uses **Steam's own translation**, read
off the same pages with `?l=german` and so on. New strings Steam has no equivalent for are left
untranslated and fall back to English; the build's coverage report lists them for the volunteer
translators.

## Upstream updates

The app's self-updater points at the upstream TCFModManager listing on sp-mod.com. Since round 14
this build only says when a newer upstream version is out; it never installs it, because that
would replace this fork with the upstream app. A newer upstream version is worth merging into the
fork instead.
