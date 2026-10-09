# SSPTMM - the Steam Workshop look

SSPTMM (Steamified SPT Mod Manager) began as a fork of TCF Mod Manager and is now its own app,
built on that app's code (see Round 22). It dresses the app as Steam's Workshop: the same palette, type, layout
and wording as `steamcommunity.com/app/<id>/workshop/`, with every mod-manager feature still
there underneath. This file records **where each value came from**, **what maps to what**, and
**every place the fork knowingly differs from Steam or rests on a guess**.

The scripts, in the repo root (each writes what it did to `logs\`):

- **`SSPTMM-update-and-run.bat`** - the everyday one. It takes the newest
  `steam-workshop-ui.bundle` from `Claude outputs\` (moving the `steam-workshop-ui` branch forward
  only, and switching to it if another branch is checked out - it never drops commits), closes this
  build if it is open, clears the old build output (keeping `dist\SSPTMM\Data`) and then runs the
  build script below. Kept out of git on purpose (see `.gitignore`): it switches the branch, and git
  must not replace the script while it runs.
- **`SSPTMM-build-and-run.bat`** - builds and runs what is already there: installs a local .NET 9
  SDK if the PC has none, runs the tests, publishes `dist\SSPTMM\SSPTMM.exe` and starts it. That
  build keeps its own `Data\` folder, so it does not touch another install. (A build from before the
  rename, in `dist\steam-ui\`, is moved to `dist\SSPTMM\` once.)
- **`SSPTMM-push-to-github.bat`** - sends the branch to https://github.com/trappuss/SSPTMM. Also
  kept out of git.
- **`SSPTMM-release-to-github.bat`** - publishes a release: builds the newest commit (bundle or
  branch) in a folder under `%TEMP%`, runs the tests, zips `release\SSPTMM-<version>-win-x64.zip`,
  then - after you type Y - pushes it as `main` and `steam-workshop-ui`, tags `v<version>` (from
  `build\Directory.Build.props`), creates the GitHub release with `CHANGELOG.md` as its notes and
  copies `wiki\` to the GitHub wiki. Fast-forward only, never forces; safe to run again. Needs the
  GitHub CLI (offers to install it with winget). Also kept out of git.
- **`tools\SSPTMM-measure-scrolling.bat`** - the scrolling measurement; writes
  `logs\perf-report.txt`.

Older rounds below name the scripts by their names at the time (`steam-ui-*.bat`).

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
author) keep Steam's solid #1B2838, as Steam's own pages do; since rounds 17 and 18 all three show
the grid instead.

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
  changing every update path at once. (Round 15 checked: for your installed mods, and older
  versions of them, its pick is the newest version for the SPT release - the app's own.)
- Dropping an archive on the window, and install records per SPT install: both done in round 15.

## Fifteenth round: checked against a real SPT 4.1.6 install (2026-09-26)

With your install folder (`G:\G Games\SPT4.1\SPT4.1 Game`) read, never written: its layout, server
log, profiles, SPT's own configs, and the plugins and server mods in it, run through the app's own
scanner. What that showed, and what changed:

**Confirmed.** The server lives in `SPT_Runtime` (already supported); profiles are in
`SPT_Runtime/user/profiles`; the server log is `SPT_Runtime/user/logs/spt/spt<date>.log` (the Play
page finds it; the launcher's log beside it is left out); SPT's own plugins are `com.SPT.*`; Fika's
plugins are `com.fika.core` and `com.fika.headless` (named as optional dependencies by two of your
mods). Those three round-14 HUNCHes are settled; only Fika's server mod name is still unseen.

**SPT 4 server mods are read properly.** They have no package.json; the app took their DLL's file
version, which for Dynamic Maps' server half is 1.0.4.0 while the mod (and SPT's own log) say
1.2.1. The app now reads each server mod's own metadata class from its DLL - GUID, name, author,
version, SPT range, dependencies - without loading it, the same values SPT logs when it starts
(all nine of yours matched). So: the right version; a server-only mod matched to its sp-mod.com
page by its GUID (All The Clothes); a mod's two halves on one card when only the server half's GUID
matches; server mods' own dependencies checked (RCTA Peely needs WTT CommonLib); no "files report
1.2.1.0 / 1.0.4.0" note for halves of one release. Anything a mod works out at run time rather than
writing as a literal is left unread (the old fallback stands) - never guessed.

**Dependencies are checked per side.** A plugin's dependency is met only by a plugin, a server
mod's only by a server mod: SAIN's two halves share the GUID `me.sol.sain`, and its server half
being there does nothing for a plugin that needs the client half.

**A plugin's two versions.** A DLL can carry a file version and its own [BepInPlugin] version, the
one BepInEx loads it as (ORBIT: 2.0.0.42986 and 2.0.0). Both are kept; whichever one sp-mod.com
publishes is used, and with nothing published to compare, the plugin's own when the file version
only adds a build number.

**Profile copies leave out SPT's own backups.** SPT keeps up to 15 copies of its own inside
`user/profiles/backups` (one per server start). Every app copy zipped them too (your log: "13
profile files" for one profile), every server start counted as a change, and putting a copy back
could bring back backups SPT had since removed. Left out now, including from older copies.

**Drop archives on Subscribed items** to install them, exactly like Install from file. Moving a mod
between groups still works; an archive dragged out of 7-Zip or WinRAR is copied before they delete
their temporary file.

**Install records per SPT install.** What the app installed (and the copies of files those installs
replaced) used to be one list for every install: a mod installed on one showed on another as that
version, and removing it there went by the first install's file list. Each install now keeps its
own (`Data/InstallRecords/<key>/`, with `install.txt` naming the install). The old list is shared
out the first time each install is opened, by which install each record's own files are in. Two
things to know:
- A record whose files are in two installs goes to the first one opened (before, both used it).
- An install moved to a new folder starts with no records: its mods show as installed by hand
  (versions read from their files) until next installed or updated from here. Taking over the
  records of a folder that has gone was tried and dropped: a folder renamed for a while looks the
  same, and an unrelated install with the same mods would have taken them too.

**Not done.** More tests for older code (Browse filters, collections, list import) - the time went
to the records change and two review rounds instead.

## Sixteenth round: the server without its window (2026-09-26)

**Options > Starting the game > Run the server without its window.** Start server then starts
SPT.Server with no console window and opens the server log on the Play page. With "Open the
launcher once the server is up" also on, the launcher opens as soon as the server listens.

- SPT 4.1.6's server, when it cannot start (its port is taken, a mod fails its checks), writes
  "Press any key to exit..." and waits for a key. With no window nobody could press one, so the
  server is given no input: it finds none and exits, and the Play page says it closed before it was
  ready, with the log open. A server that is still not listening after three minutes is said too.
- Stop server (and Restart) asks a server with no window to stop with Ctrl+C, which SPT answers by
  shutting down properly (the same as closing its window); it is killed only if it has not gone in
  time. A server hosted in Windows Terminal, which has no window of its own, was killed outright
  before; it is now asked the same way.
- The server keeps running if you close this app, as a visible one does; stop it from the Play page.
- A restart of a server with no window opens the log and watches it come back up.

HUNCH: that Windows 11 set to open console programs in Windows Terminal leaves a program started
with no window alone (the console it gets is never shown, so there is nothing to hand over) - not
checkable here. SPT's own code only checks whether its output is redirected and reads a key on
failure (both handled); a server mod that draws on the console itself (cursor, window size) may
fail without a window - none of yours do.

## Seventeenth round: what you already have, at a glance (2026-09-26)

**Home.** The From Followed Authors cards and the rows under Top Rated, Most Subscribed, Last
Updated and New carry the same subscribed badge as Browse and the Mods row (a green check, or the
warning or update mark), top left of the picture.

**Your collections.** Each mod on a saved list shows the same mark before its name when it is
subscribed, matched by sp-mod.com id or by GUID, with the status as its tooltip. Addons are not
marked. The marks follow installs and removals while the page is open.

**A collection's page** now shows the window's grid (or your own picture) instead of the solid
#1B2838, like Home and Browse. The page it was opened over is hidden while it is up and comes back
as it was. (Item and author pages too since round 18.)

**Hover popups on a collection's items and an author's items.** Both Steam pages have one, and
neither is Browse's: measured on the live pages, a plain box 300px wide, 14 10 inside, no radius,
border or shadow, with the title over the start of the description, 1px above the item and 3-4px to
its right. A collection's is #417A9B (title 15px white, text 12px #C6D4DF); an author's is #363C45
(title 14px white, text 12px #B0AEAC). The text under the title is the mod's sp-mod.com teaser. An
author's addons get the same popup in place of the plain tooltip they had.

## Eighteenth round: Subscribed items' three views alike, the grid everywhere (2026-09-26)

**Group by, in Cards and List.** A dropdown beside the sort: Not grouped (as before), Grouped by your
groups, Grouped by category. Grouped, the mods sit in the same sections as the Groups view, with the
same header - fold, count, what is disabled, and for a group enable/disable/invert, move, rename and
delete. Every mod is shown then, without pages (a page boundary would cut a section in two); Per page
and the pager step aside. (Every group shows, empty ones too, since round 19.) Your groups come in the Groups view's order (Sort groups), or their manual order
while that view is sorted by category. A folded group or category is folded in every view. Save as
default keeps the choice; Clear filters goes back to it, as it does with the sort.

**The same mod, the same facts, in every view.** List's rows and the Groups view's rows now show the
mod's picture (56px in List, as on a card; 40px in the compact Groups rows) and what an addon is for.
The Groups view's rows show the group and DISABLED chips the cards and List had. A group chip is left
off wherever the section around it already names the group.

**Sections are synced, not rebuilt.** Typing in the search or changing a filter keeps the sections
and cards that are still there, so an open card stays open instead of replaying its opening, as the
flat views already did (ItemsSync). The Groups view's sections are synced the same way.

**Rename** puts the group's name in a box ready to type over; before, the box had to be clicked first.

**Item and author pages over the grid.** Like a collection's page (round 17), they are transparent,
and the page they were opened over is hidden while they are up (the Workshop strip too, keeping its
space, so nothing underneath moves).

**Tooltips left-aligned.** WPF UI's tooltip style justifies text (read from its compiled style in
Wpf.Ui 4.3); every plain tooltip is now left-aligned, as Steam's are.

**Tests.** ModGroupStore (groups, order, moves, assignments, folding, a damaged file), ArchiveLayout
(each packaging it reads, and the ones it must not guess at) and the page defaults in settings.json:
1224 in all.

**Not done.** Dragging a mod between groups in Cards and List (done in round 19). Pushing to GitHub
and measuring the signed-in Steam values need you (a fork's address; a Steam sign-in).

## Nineteenth round: dragging between groups in every view (2026-09-26)

**Cards and List, grouped by your groups,** take the Groups view's drag: pick up a card or a row and
drop it on another group's section (or Ungrouped) to move it there. A click still opens or closes the
card - only a move past the system's drag distance starts a drag, and the card is not toggled when it
ends. Near the top or bottom edge the list scrolls, and the wheel scrolls it, as in the Groups view.
Grouped by category, nothing is dragged: a category is not something a mod is put into.

**Every group shows** in those sections now, empty ones too with "Drag mods here", so there is always
somewhere to drop - round 18 left empty sections out.

**A file dragged in from Explorer** no longer scrolls the list at its edges; it installs wherever it
is dropped, as before.

**Pushing to your fork.** steam-ui-push-to-github.bat (beside the other .bat files) sends the
steam-workshop-ui branch to github.com/trappuss/SSSPTMM: the newer of the bundle and your own branch,
never forced, and it adds the fork as a remote named "fork" the first time.

**Signed-in Steam values** are still unmeasured: the browser used for measuring is not signed in to
Steam, and signing in is yours to do.

## Twentieth round: the item page's Content Type opens Browse (2026-09-27)

On Steam the Content Type on an item page is a link: Browse, that type only, top rated
(`browsesort=toprated&requiredtags[]=...`, read off the live page). Here clicking it opens Browse on
that content type, sorted Most endorsed (the home page's Top Rated), with every other filter at
Browse's own default (Save as default, or the app's) - the link carries nothing else, so a search or
filter left on Browse earlier does not narrow it. Measured: the link is the value's own #EBEBEB,
not underlined, unchanged under the pointer - only the hand cursor says it is one; a tooltip names
where it goes. The item page closes on the way, as with any navigation.

**Test fix (same round).** On your PC 17 tests failed - everything that disables, enables or applies
a list. The in-use guard looks for SPT's game and server among the machine's real processes, and one
whose location Windows will not show counts as blocking every install, the tests' temporary ones
included. Reproduced here with a server process the test run could not read (the same tests fail
with InstallInUse; with no such process they pass). The tests now see no processes (TestSetup); the
guard itself is unchanged, and still refuses to touch an install while SPT runs.

## Twenty-first round: merged with the original's 1.19.0-beta (2026-10-02)

The original app's 1.19.0-beta (92 commits since the last merge base) is merged in. The choice made
for the one part both had rewritten - installing and removing mods - was **the original's**:

- Removing a mod moves its files into a holding folder in the install, with **Undo** on Subscribed
  items and "Keep removed mods" in Options deciding how long they stay.
- SPT's, BepInEx's and the game's own files are never placed over or removed; a few areas take new
  files only. A file another mod or a hand install put there is kept in `Data\overwritten` before
  it is written over, and put back when the mod is removed.
- **Dependencies and Conflicts** lists mods installed twice or shipping clashing files.
- One list of install records again (`Data\installed-mods.json`), each record stamped with the SPT
  install it was made in. The fork's per-install lists (`Data\InstallRecords\`) are moved into it,
  stamped, the first time this build starts; the old folder is renamed `InstallRecords.before-1.19`
  and kept. Where the same mod had a record in two installs, the install the app is set to wins and
  the other stays in that folder (logged). Pages read only the records for the install the app is
  set to (and unstamped ones), as the per-install lists did.

The fork's own additions are kept on top of it:

- Archives with a read-me, licence or picture beside the wrapper folder, or beside `BepInEx\` and
  `user\` at the top - those files are never put in the SPT folder (the original places a loose
  file in the install root where nothing is there yet; the fork keeps documents and pictures out).
- `plugins\` and `patchers\` without the `BepInEx` folder around them.
- Install from file, kept downloads and three downloads at once, copies of the SPT profiles before
  an install or a removal, the per-install "SPT is running" check.
- The warning before an install that would write over another mod's files (now worked out from the
  same layout rules the install uses), and "needs X, which is not installed / is disabled" on
  Subscribed items' cards.

What the fork had that is gone with the original's install core:

- The crash journal: an install stopped part-way (power cut, the app killed) is no longer put back
  on the next start. Closing the window or quitting from the tray while one is being placed still
  asks first.
- The fork's own record of files an install replaced (`ReplacedFiles`): the original keeps its own
  copies in `Data\overwritten` from now on. Copies the fork made before the merge stay in
  `InstallRecords.before-1.19\<install>\ReplacedFiles` and are not put back by the app.
- A `BepInEx\config\*.cfg` in an archive was placed over the user's at first; Round 24 brought
  back the fork's rule.

From the original, in the Steam layout:

- **Help** is a hub tab, and the "?" in the title bar and F1 open it at the page on screen.
- **Monitor mode** (Options > Install mode): every install button can download only. The item page
  has a small button beside Subscribe for the other way round, for that one mod; Update all and
  collections follow the setting.
- Update notifications and running in the tray, one copy of the app per folder (a second start
  brings the first forward), server map reporting, and the SPT-version question before installing
  a version that is not for the installed SPT.
- Options is laid out the original's way, in sections; the fork's settings sit in it (Starting the
  game, Tabs and background and Scrolling under General; Downloads and Unsubscribing under
  Installing mods; SPT profile backups under Advanced), with On/Off switches like the rest.

Not done in this round: Help's own text still names the original's sidebar and page names
("Installed", "Browse" in the sidebar).

## Round 22: SSPTMM, its own app (2026-10-02)

The fork becomes its own tool: **SSPTMM - Steamified SPT Mod Manager**, built on TCF Mod Manager
by TheCrimsonFckr and credited as such.

- **Name everywhere a person sees one:** the title bar, the exe (`SSPTMM.exe`, described to Windows
  as "Steamified SPT Mod Manager" - the name its notification settings and Task Manager show), the
  tray, notifications, error titles, Help, and the logs (`Data\logs\ssptmm-<date>.log`; the old
  `tcfmm-` logs age out as before). sp-mod.com sees the user agent `SSPTMM/<version>`. A mod list
  file says `SSPTMM` as the app that wrote it; either app reads the other's.
- **Its own version:** 1.0.0. The TCF Mod Manager release it is based on is on the About page
  (`SelfMod.OriginalVersion` - bump it with each merge of the original; removed in round 39).
- **About** replaces App update in the hub: the name, version, what it is, the credit and links to
  this app's GitHub repository and to TCF Mod Manager's page. The original's update check no longer
  runs at all, and Help leaves out its pages on updating the app. The Server Map mod is still
  checked, and the dot on About means only that it is behind.
- **Report a problem** in Help goes to this app's GitHub Issues; **Open the full guide** still opens
  TCF Mod Manager's sp-mod.com page and says so - most of it applies.
- **Placeholder art:** `src\TCFModManager.App\Assets\AppIcon.ico` (the icon) and
  `Assets\WorkshopBanner.png` (512x512, the Workshop banner's picture) - plain originals in Steam's
  blues. Replace either file with your own art and rebuild.
- **README** is SSPTMM's own; TCF Mod Manager's was kept in `docs\tcf-mod-manager-readme.md` for its
  technical notes (removed in round 39).
- **Kept on purpose:** the code's names (`TCFModManager.*` projects and namespaces), so newer TCF
  Mod Manager releases still merge; and the folders this app makes inside an SPT install
  (`.tcfmm-removed`, `.tcfmm-work`, `.tcfmm-duplicates`) and the Server Map mod's files, so what is
  held for Undo is still found, and an install shared with TCF Mod Manager stays readable by both.
- **The Server Map** sends this app's version as before; machines are listed with "App version"
  rather than "TCF Mod Manager", since either app can be on the other end.
- **The GitHub repository** is renamed SSSPTMM -> SSPTMM (on GitHub, by hand); the push script uses
  the new name and moves a remote still pointing at either old one.

## Round 23: Help for the Steam layout (2026-10-02)

Help was the original's, written for its sidebar and pages. Every topic was checked against the
Steam layout (file and line for each change kept with the round's notes) and 18 of 53 corrected:

- No "sidebar": pages are named as the tabs at the top; Browse is reached through Workshop.
- Browse: the SPT tick or cross and the addon count are in the box a card opens on hover; the SPT
  versions are tick rows on the left; Hide installed is the minus box beside Subscribed under MOD
  TAGS; Save as default is in the gear beside the search box.
- Subscribing: from a mod's page or the small + on a card; the Additional Required Items question
  comes first, then the read-the-page window for pages not seen yet.
- Subscribed items: "Unsubscribe", Undo removing at the top, Keep removed mods in Options.
- Downloads: up to three at once, installed one at a time in queue order.
- Monitor mode's small button: beside Subscribe on a mod's page, in the update dialog, on addon and
  Dependencies rows (not on a card's +).
- A disabled mod: only Update greys out; Unsubscribe says to turn it on first.
- The light/dark theme topic is left out (the Steam look has one theme), as are the original's
  pages on updating the app.
- Report a problem's tooltip names GitHub Issues; Downloads' own description and empty message, the
  update dialog's disabled-mod notice and Unsubscribe's tooltip use the new names too.

The German, French, Italian and Russian text for the changed steps falls back to English until it
is translated.

## Round 24: BepInEx settings are the user's again (2026-10-02)

Checked first: with 1.19's install, an update put the mod's default `BepInEx\config\*.cfg` over the
one the user had tuned (the tuned copy went into the holding folder, where Keep removed mods
deletes it after its time), and a first install put the archive's defaults over a `.cfg` BepInEx
had already written. The fork's rule is back, built on 1.19's own "keep the user's documents" path:

- An archive's `.cfg` is placed only where none is there yet - or over the copy this app placed,
  when it is still exactly as placed (its fingerprint matches), so an untouched default still
  follows the mod's new version.
- A kept file the previous version placed stays in the record with that version's fingerprint, so
  it keeps reading as changed: the next update leaves it too, and Unsubscribe leaves it in place.
  One that was there before the mod was installed is not recorded at all.
- The download card says so ("Your settings in BepInEx\config were already there and were kept"),
  and names the files in its tooltip. The file-clash warning before an install leaves these out.
- `BepInEx\config\BepInEx.cfg` stays BepInEx's own, never placed or removed, as in 1.19.
- 1.19's tests that used a `.cfg` as "a file someone else put there" use a hand-placed plugin file
  instead; the new rule has its own tests (ForkBepInExConfigTests).

## Round 25: fewer tabs - Steam's header and downloads bar (2026-10-02)

The first of three tidy-ups (tabs, then the Subscribed items toolbar, then the explanation text).
The row of eleven tabs is down to six, laid out the way Steam's client lays out its own:

- **Play, Workshop, Subscribed items, Collections** stay tabs.
- **Tools ▾** holds Configs, Dependencies and Conflicts, and - when switched on in Options - Mod
  footprint and the Server map. **Help ▾** holds Help and About; the "newer version" badge shows on
  the Help tab and on About. Both open the way the Workshop's Browse and Your Items menus do: on
  pointing at them, closing a quarter-second after the pointer leaves, and also on a click. The
  tab is underlined while any page in its menu is open.
- **Options** is a gear beside the sp-mod.com button, blue while Options is open.
- **Downloads** is no longer a tab. A bar runs along the bottom of the window as Steam's does:
  "DOWNLOADS / Manage" when nothing is going on, and while something is, "DOWNLOADING" or
  "INSTALLING" with the item, how many of the queue are done, and a progress bar. Clicking it opens
  Downloads.
- Help's steps that said "open the X tab" now say which menu, the gear, or the bar.

Checked under Wine: each menu opens and its entries go to their pages; the gear lights on Options;
a download-only of a mod showed the bar's progress line and then returned it to idle. Seven strings
(Tools and six for the bar) are new and fall back to English, as do the reworded Help steps.

## Round 26: Subscribed items, tidied (2026-10-02)

The second tidy-up, of the whole Subscribed items page. Before, 21 controls sat above the first
mod, over three rows, with up to four more appearing and shifting the row.

- **One toolbar row:** search, sort, **Filters**, the three views as icons, and **⋯**.
  - Filters opens one popup with update status, enabled/disabled, category and group. The five
    tick boxes that had their own dropdown are plain tick boxes in the same popup. Below them are
    "show in sections", Save as default and Clear filters. The button reads "Filters (2)" and has
    a blue edge while two filters are narrowing the list. Sort and sections are not counted.
  - ⋯ holds Install from file, SPT upgrade check, Rescan, Multi select, open/close every mod
    (Cards and List only) and List badges. Each entry's explanation opens to the left of the menu.
    Under Wine the first entry's tooltip came up with the menu and covered the rest.
  - Cards, List and Groups are icons at the right, as Steam switches its library between grid and
    list. Each names itself, and what it is for, on hover.
  - Per page moved beside the pager.
- **Only when there is something to act on:** the conflict count, and Undo for the last disable,
  enable or removal, on a line under the toolbar.
- **Multi select** has a Done button on its bar, since it is switched on from the ⋯ menu.
- **An opened card** shows the installed version, the newest one and any warning. The GUID,
  folder name, dates, Fika/ads/AI flags, who installed it and the folder buttons are behind
  "More details". Its old minimum height, which made every opened card as tall as a ten-line one,
  went too.
- **Nothing installed:** in place of the toolbar over "No mods found", the page says what it is
  for, with "Browse the Workshop" and "Install from file...". When the search and filters hide
  everything, it says so and offers Clear filters. That message is not shown in the Groups view,
  whose group headers already say "0 mods".
- The "installed by" line said Remove for the button labelled Unsubscribe. It now names
  Unsubscribe in all five languages, using each language's existing name for that button.
- Six Help steps name the new places: the Filters button, the ⋯ button and the view icons. A
  seventh says to press Done. Their German, French, Italian and Russian text falls back to
  English until translated, as do the twelve new strings.

Checked under Wine:

- A dropdown inside the Filters popup chooses without closing it, and the count and edge follow.
- Clear filters brings every mod back.
- Every ⋯ entry tried does what it says. The log shows Rescan's scan.
- Multi select and Done work.
- More details and Fewer details work, and a warning shows without opening More details.
- List and Groups views work.
- With the test install's mods moved aside, the empty page shows and its button opens the
  Workshop. The mods were then put back.
- Disabling a mod showed Undo, and Undo put it back and the line went away.

## Round 27: one line of explanation, the rest behind a "?" (2026-10-02)

The third tidy-up. Pages explained everything up front. There were four sentences under Start
server, a three-line warning over Configs, a paragraph on Collections, and Options rows whose
hint ran across the window. Now each says one line, and a small "?" at the end of it holds the
rest. The "?" (InfoTip) shows the full text when hovered or tabbed to.

- **Play:** the server, launcher and headless-client cards each have a one-line summary.
- **Configs:** InfoBar's template has a title and a message and nothing else, so there is no room
  for a "?". The message is one line, and hovering the warning shows the whole of it.
- **Collections:** "Pick a list on the left..." on show; what a mod list is behind the "?". Both
  halves were already translated as one string's two paragraphs, so the translations carry over.
- **Options:** every hint is one line.
  - Nine rows had put their whole explanation in the hint and now have a short one: Starting the
    game, Tabs and background, Scrolling, Install mode, Keep removed mods, Downloads,
    Unsubscribing, and Server Map's "Only answer this network" and "Report this machine".
  - Every row that already kept its longer explanation in a hover-the-row tooltip also has the
    "?", so that explanation can be found.
  - Install mode's short line is the first sentence of its old hint, in all five languages.
- Sixteen strings are new. Thirteen fall back to English until translated. The other three (the
  Collections pair and Install mode) are made from the existing translations.
- The Collections line came out smaller once it held a Run and the "?" than it had as plain
  text, measured against a screenshot from before. Its size is now set (14). Why it happened
  is not known. Play's lines, inside cards, kept their size.

Found while checking, and left for its own round (round 28): 18 strings still name pages by their old
names ("the Installed page", "the Mod lists page") or call Unsubscribe "Remove". One also places
Save as default "beside Clear filters", where it no longer is.

## Round 28: pages called by their names (2026-10-02)

The 18 strings found in round 27, and three more a wider search turned up (21 in all), now use
the names on screen:

- "the Installed page" is now **Subscribed items**.
- "the Mod lists page" (and "Opens Mod lists") is now the **Collections** page. "Mod list" stays
  as the word for the thing itself, as the Collections page still uses it.
- "Remove it ... where Remove shows" is now **Unsubscribe**.
- Save as default is placed where it now is: in Subscribed items' Filters, and under the gear
  beside Browse's search box.
- Options' page-default lines say "Subscribed items:" rather than "Installed:".

Their German, French, Italian and Russian text was dropped and falls back to English until
translated. Left as it was, it pointed to pages that no longer go by those names. Two Russian
strings had not been translated anyway.

Two Options rows round 27 missed:

- SPT profile backups' text comes from a binding, not a fixed string, so it was not found then.
  It now has a one-line summary and a "?".
- Data files kept its explanation as a tooltip on the whole card rather than its header, and now
  has the "?" too.

Every row tooltip on the page was then listed, to confirm none is left without a "?". The two
others with no "?" are button tooltips, not row explanations.

## Round 29: picking, switching and updating as Steam does; Getting started (2026-10-02)

Ideas from the Modrinth App's installed-content page and onboarding (read in its GPL-3.0 source;
only the ideas are borrowed), each done the way Steam would:

- **Picking several mods**, as in Steam's library.
  - Ctrl+click adds or drops one, and Shift+click takes everything between the last one picked
    and this one, in the order on screen. Ctrl+A picks every mod the filters show; Esc drops
    them all.
  - A tick box shows on the card or row under the pointer, and on every one once anything is
    picked. A picked card or row has the accent tint and edge; Groups rows have it now too.
  - While anything is picked, a bar runs along the bottom of the page: "3 ITEMS SELECTED",
    Select all, Update selected, Enable, Disable, Unsubscribe and Clear.
  - The Multi select mode, its "..." entry and its Done button are gone.
- **The plug icon is an on/off switch:** blue with the knob right while on, grey with it left while
  off. It asks for the change through the same command and dependency warning. Because it is a
  Button, not a ToggleButton, a cancelled warning leaves it showing the truth.
- **Steam's blue Update** on a card or row whose mod has an update this page can apply. It is the
  same test Update all uses (CanUpdateHere, set in MarkUpdatableCards). One click goes through the
  same prompts and the download queue. Its tooltip is the version it installs.
- **Getting started**, at the top of Workshop Home, where the app opens. It lists three steps,
  each ticked from the install itself rather than from what was clicked:
  - The SPT folder is found.
  - This app has installed a mod.
  - The install has a profile, which only exists once the server and launcher have been used.

  Each step that isn't done has a button to the page that does it. The list is read again whenever
  Workshop Home is shown. It goes for good once all three are done, or when hidden
  (AppSettings.GettingStartedDone). An existing user, with all three done long ago, never sees it.
- Help says Ctrl+click, the switch and the card's Update. Seventeen new strings, and eight Help or UI
  lines changed. Their translations fall back to English until translated.

Checked under Wine:

- The switches.
- The hover tick box.
- Ctrl+click then Shift+click picked exactly the six between, and the bar counted them.
- Update selected stayed off with no updatable mod picked.
- Esc dropped the picks.
- Groups rows tint when picked.
- A switch turned a mod off, and Undo put it back.
- The card's Update showed on a mod made to look out of date in the test data, and took that mod
  through the "read its page first" prompt.
- Getting started showed two steps done on the test install. Go to Play opened Play. With a profile
  added in the install's own profiles folder, coming back ticked the third step and the list went
  for good. Hide hid it for good.

## Round 30: Your collections, and sharing them with friends (2026-10-02)

- **Collections opens on a grid**, like Steam's Your Collections (YourCollectionsPage).
  - Each card shows:
    - a 2x2 mosaic of the first four mods' pictures;
    - the name, who made it, how many items it has, and its SPT version;
    - how much of it is installed ("5 of 7 installed", or green "All installed");
    - chips for Update, Shared, Following and From server.
  - Buttons across the top: Add from code, Add from file, Follow a shared file, and Create from
    installed mods (blue).
  - A card opens the collection page. The old page is now titled "Manage collections" and opens
    from "Manage in Collections". The collection page's "Your collections" breadcrumb comes back
    to the grid.
- **Share with friends**, on the collection page.
  - **A share code**: one line of text to paste in Discord or anywhere else
    (ModListShareCode, "SSPTMM1." followed by deflated JSON).
    - It carries the list's id, revision, policy, SPT version and, for each mod, its sp-mod.com
      id, version and scope.
    - Names are looked up again when the code arrives. A 97-mod list fits in a Discord message,
      and a test checks that it stays under 2000 characters.
    - A mod installed by hand travels by name alone.
    - The sharer's name is asked once and kept (AppSettings.ShareName).
  - **A shared folder**, such as a Dropbox, OneDrive or Google Drive folder, or a network share.
    - The owner picks a folder, and the list's file is kept up to date there from then on. It is
      rewritten whenever the list changes, and the revision goes up only when the mods change.
    - The friend picks that file with "Follow a shared file" (AppSettings.SharedFiles and
      FollowedFiles).
- **Seeing what changed.** When a newer code is pasted, or a followed file has changed, a notice
  says exactly what changed, for example "1 added (Amands' Sense - Updated) · 1 removed (All
  Quests Checkmarks)". It offers Sync or Later.
  - Sync opens the preview with "Match the pack exactly" (Exclusive), so nothing is applied until
    Apply is pressed.
  - What counts as news is one rule, ModListDiff.IsNews: a higher revision, or the same revision
    with different mods.
  - Names, GUIDs and folders are not counted as changes.
- **A code on the clipboard** is noticed when the window gets focus. It is offered once, with Add
  or Not now, and is not offered again once dismissed.
- **Arrivals that clash** are asked about:
  - Your own list arriving back is added as a copy.
  - An older revision than the one already held gives "Keep mine" or "Use the older one".
- **Subscribe to all on a friend's pack** makes Overwrite the green default, matching the choice
  "Match the pack exactly".
- **Strings and Help:** 79 new strings. Help has a new topic, "Follow a friend's collection", and 11
  Help lines were changed. Their translations fall back to English until translated.
- **Tests:** 12 new (share code round trip, a cut-off or newer code, a code found inside a chat
  message, the size limit, and the diff rules). 1574 pass.

Checked under Wine:

- The grid's statuses and chips.
- Copying a code from the Share dialog. The code decodes outside the app.
- The clipboard notice and Add.
- An updated code showed the change line above. Sync opened the preview ("3 missing · 7 to
  disable", revision 2).
- The shared folder file was written with the sharer's name. An edit by the owner raised it to
  revision 2.
- Following that file through the file dialog read revision 3. The owner's revision 4 brought up
  the update notice and the Update and Following chips, and Sync stored revision 4.
- Create from installed mods showed "All installed".
- Add from code was prefilled from the clipboard. An older code gave the "Keep mine" choice.
- The breadcrumb came back to the grid, and Manage opened "Manage collections" with the
  Collections tab lit.

## Round 31: Per page opens on Infinite and remembers the last pick (2026-10-02)

- **Every page with a Per page opens on Infinite** until a size is picked there. Those pages are
  Browse, Subscribed items (it used to open on 12), Browse Collections and an author's Workshop
  Items.
- **The author page gains Infinite.** Its row now reads "Per page: 9 18 30 Infinite". The next 30
  items are added as the page nears its bottom, and the line reads "Showing 1-30 of 37 entries"
  until they are.
- **The size picked is kept straight away**, per page (AppSettings.PageSizes, PageSizeMemory),
  with no Save as default needed.
  - It outlasts a restart and Clear filters, since it is how the page is shown rather than what it
    shows.
  - A size saved earlier with Save as default is still used on a page where nothing has been
    picked since.
- **Per page is also at the top while the list is infinite**, beside the sort on Browse and Browse
  Collections, and in the author page's top bar. The one under the grid comes after every card,
  so on the infinite list it was only reached once all 742 mods had been added.
- The Save as default tooltips and the Options text now say that Per page is kept by itself. The
  translations of those three lines fall back to English until translated.
- **Tests:** 9 new (PageSizeMemory, and settings.json with and without the sizes). 1583 pass.

Checked under Wine:

- Subscribed items opened on Infinite. After picking 8, a restart opened it on 8.
- Browse: the top Per page showed while infinite. Picking 15 hid it and paged the grid. After a
  restart and Clear filters, Browse was still on 15.
- Browse Collections: after picking 30, a restart and Clear filters, it was still 30 (11 pages).
- An author with 37 items showed 30, and scrolling down added the rest. 9 paged them 1 2 3, and
  Infinite from the bottom row went back.

## Round 32: server prepatches fixed, Close game, collection hovers, Download only (2026-10-03)

- **Bug fixed: `SPT_Runtime\user\patchers\` was never placed, updated or removed.**
  - The report: Skills Extended 3.1.1 kept a stale server enum prepatch. The server then rejected
    every end-of-raid payload ("could not be converted to ...SkillTypes"), and raid results were
    lost.
  - The cause: ProtectedInstallPaths treated everything under `<server>\user\` except `user\mods\`
    as SPT's own, so the prepatch was skipped and only mentioned on the download card as one of
    "SPT's own files".
  - The fix: `user\patchers\<GUID>\` is now mod territory. SPT ships nothing there (sp-tarkov wiki,
    SPT_41/modding/EnumExtensions.md).
  - A removal tidies the emptied `<GUID>` folder and leaves `user\patchers` itself
    (InstallPathGuard.PrepatchContainers). It is kept out of the shared container list, which also
    names a record's mod folders.
  - Disabling a mod leaves its prepatch in place, so a profile that uses the mod's values still
    loads.
  - 12 tests (ForkPrepatchInstallTests) reproduce the report on an SPT 4.1 layout. Without the fix,
    7 of them fail.
- **Close game** on the Play page's launcher card, while the game runs.
  - It asks on the card first, like Stop server, because a raid in progress is lost.
  - It asks the game window to close, and kills it only if it is still open 5 seconds later.
  - Only a game running from this install is touched. On a machine that runs a Fika headless
    client, a windowless EscapeFromTarkov is left alone.
- **"Close the game when the server stops"** tick box, off by default (AppSettings.CloseGameWithServer).
  - App-wide (GameCloser), so it doesn't depend on the Play page being open.
  - The server counts as stopped once it has been seen running and then seen down twice, 3 seconds
    apart.
  - A restart from the Play page holds it off.
- **Collection pages:** a row whose mod the catalog has gets Browse's hover popup (picture slideshow,
  posted and updated dates, SPT version, tags) through CardHover.Card. Addons and unknown entries
  keep the plain box.
- **Download only, easy to find.**
  - A labelled button beside Subscribe on a mod's page, shown whether or not the mod is subscribed
    to, and a right-click entry everywhere (BrowseViewModel.DownloadOnlyAsync).
  - It saves the file to the download folder and changes nothing in SPT.
  - 1.19's unlabelled icon stays only in Monitor mode's download-only setting, where it installs.
  - The "anyway?" questions (no version for this SPT, Fika, requirements) now say "Download it
    anyway?" for a download.
  - Help's monitor.other topic is rewritten for this.
- **Research** (log diagnostics, SVM-style mods) is in the SPT project doc
  `claude/ssptmm-research-logs-and-svm.md`, with proposals that are not built yet.
- **Strings and tests:** 17 new strings and 4 changed. 1595 tests pass.

Checked under Wine:

- The game was run from a stand-in SPT 4.1 folder (a windowed EscapeFromTarkov.exe and an
  SPT.Server.exe that listens on 6969).
- Close game asked on the card, the game window closed itself, and the page said so.
- With the box ticked, a restart from the page left the game open. Killing the server closed the
  game about 10 seconds later, and the launcher stayed open.
- The DeltaMOD collection's BigBrain row showed the full popup.
- Download only from the right-click menu saved VisitAPI-1.3.5.zip after a "Download it anyway?"
  question. From the page of a subscribed mod (Quick Sell) it saved QuickSell-v4.2.0.zip, and its
  installed files were unchanged.

## Round 33: the user\patchers bug traced on the real install; a check after every install (2026-10-03)

### The trace
From the manager's own log, its install records, the holding folders and the cached archives on the
user's install. All of it was read only. Times are local.

- **09-29 20:14:31:** Skills Extended 3.0.3 was installed by a build from before the upstream
  "Resilience, stage 1" merge (1075f6e). That build had no protected-path rule, so it placed and
  recorded the 132-byte prepatch. The file's timestamp matches to the second.
- **10-02 19:45:58:** the update to 3.1.1, on a build with the rule:
  - the install logged "kept the install's own …EnumExtensions.json; the archive's copy was not
    placed";
  - the removal half logged "Refused (Protected)";
  - the new record dropped the file.
- **10-02 23:04 (uninstall):** the prepatch was no longer in the record, so it was never touched.
- **10-02 23:06 (reinstall):** "kept the install's own" again. The old file had never left. Neither
  `.tcfmm-removed` nor `.tcfmm-work` held a copy, and nothing restored one.
- **The archives were right:** the cached 3.1.1 archive's prepatch is 285 bytes, includes
  SignalsIntelligence, and is byte-identical to the user's hand-written fix. The stale file is
  byte-identical to the 3.0.3 archive's copy.

### Fixes, on top of round 32's
- **A. A prepatch's owner is read from its folder.** In `user\patchers\<GUID>\`, the GUID names the
  mod (InstallPathGuard.PrepatchFolderOf). A file there that no record owned now counts as an
  earlier copy of the same mod when the GUID matches, and a removal holds it rather than putting it
  back. Without this, the user's own install, whose correct prepatch belongs to no record after the
  bug, would have kept the prepatch through an uninstall.
- **B. A check after every install and update (InstallVerification).**
  - Every archive file is fingerprinted before placing, then compared with what is on disk
    afterwards (size and SHA-256).
  - Each file is reported as missing, a different size, different contents or unreadable. The
    download card names every such file in a warning, and the log has one line for each.
  - Left out are the files kept on purpose: refused protected files, the user's kept settings and
    documents, and configs that were merged or kept.
  - In the test suite's 155 real installs it reported nothing.
- **C. Refused files aimed at SPT's user folder are named.** These are files outside user\mods and
  user\patchers (ProtectedInstallPaths.IsUnderServerUser). They are now named in a warning on the
  card, instead of being counted with SPT's and the game's own files.
- **SSPTMM-test.bat** builds the app, runs the prepatch and install-check tests on their own, then
  the whole suite, and writes logs\test.log.
- **Strings and tests:** 6 new strings; 19 new tests. 1614 pass.

Checked under Wine: installing a hand-made archive with a file in `user\cache\` showed the warning
on its card, naming `user/cache/zz-test.json`, and the file was not placed.

Later the same day:

- **What happened:** an SVM 2.2.3 install failed on the user's PC. The log stopped at "using the
  archive kept from before", with no install line and no reason.
- **Why the log was silent:** the download queue's Settle put a failure's reason on the card only,
  so no failed download or install was ever logged. It now logs a line naming the mod, the card's
  reason and the exception.
- **The likely cause:** SPT's server log shows the server still running two minutes before the
  attempt. The refusal to install while SPT runs happens before the install's first log line,
  which matches the log. This is likely, not confirmed.
- **Checked under Wine:** a stand-in SPT.Server.exe running inside the test install produced the
  same refusal, and the log now reads "... failed: Close SPT.Server.exe before installing a mod ...
  (ModInstallException: InstallInUse)".

## Round 34: Mod tools on the Play page (2026-10-03)

- **A "Mod tools" card fills itself** with every .exe an installed mod put in the SPT folder
  (Core's ModTools):
  - from this app's install records, which covers SVM's Greed.exe in the game folder and give-ui's
    app inside its server mod;
  - for a mod installed by hand, from the .exe files inside its own mod folder.
- **Each row** shows:
  - the exe's own icon (the shell's, through SHGetFileInfo), its name and "from <mod>";
  - **Open**, which starts it from its own folder through the shell;
  - a folder button for the mod's server folder when it has one (SVM's presets), else the exe's
    folder;
  - **Hide**.
- **Server notes for the tools this app knows** (ModTools.Known):
  - give-ui says "Needs the server running", with Start server beside it while the server is down;
  - Greed says to use it with the server stopped, in the caution colour while the server is up.
  - The notes follow the Play page's poll.
- **A disabled mod's tool is greyed** and can't be opened. That includes Greed.exe, which stays in
  the game folder: the mod counts as disabled when its files in mod containers are all in the
  ".disabled" ones.
- **Hidden tools** are kept by their enabled install-relative path (AppSettings.HiddenModTools). A
  "Show N hidden tools" link lists them dimmed, with Show on Play.
- **When the card refreshes:** whenever the Play page is shown, and whenever the installed index
  changes (installs, removals, moves).
- **Help:** a new topic, play.tools.
- **Strings and tests:** 26 new strings; 10 new tests. 1624 pass.

Checked under Wine. The test used two installs from file, shaped like SVM 2.2.3 (Greed.exe at the
root) and give-ui 5.0.0 (the exe in its server mod folder), with Wine's notepad.exe standing in for
both tools:

- Both rows appeared with their icons and notes.
- Open on Greed started it, and the page said "Opened Greed."
- A stand-in server running inside the install switched the notes over.
- Hide wrote the key and showed the "Show 1 hidden tool" link. The link showed the tool dimmed with
  Show on Play, which brought it back.
- Greed's folder button opened `[SVM] Server Value Modifier`.
- Disabling TestSVM greyed Greed, with "TestSVM-2.2.3 is disabled, so this can't be opened."

## Round 35: empty folders, Subscribed items, removal warnings, re-uploads, Diagnose logs (2026-10-03)

### Empty folders an archive ships are created

- **The cause of Greed's "couldn't find Preset folder":** SVM 2.2.3's archive (mod-236-14936,
  checked on the user's PC) has one empty folder, `SPT_Runtime/user/mods/[SVM] Server Value
  Modifier/Presets/`. The installer placed files only, so the folder never existed until Greed
  made it.
- **Now** (ModInstallService.EmptyFoldersIn / CreateEmptyFolders): every folder in the archive with
  nothing below it is mapped like a file (content root, bare BepInEx, server root) and created,
  but only inside a mod's own folder (ModFolderOf) or a prepatch's GUID folder, and only where the
  placed-path check allows. A shipped `user/cache/` or bare `BepInEx/plugins/` is not created.
- **Removal** (TidyEmptyModFolders) now also removes the empty subfolders left in the mod's own
  folders, so the mod's folder goes with it. Shared folders such as `user/mods` stay. Only on a real
  removal (not an update), and never in a mod folder another record also has files in - an addon's
  removal from its parent's folder used to take the parent's empty Presets\ with it (found in the
  release audit; test added).
- **Tests:** 3 new (ForkEmptyFoldersTests). 1627 pass.

### Subscribed items

- **Recently installed** in Sort by: newest first, mods with no date last. A card's install date is
  now the install record's (the last install or update) when this app installed it; the folder's
  creation time is only used for hand-installed mods. A loose DLL copied with its archive's dates
  read as a year old.
- **Grouped by enabled / disabled** in the grouping picker (Cards and List): an Enabled section,
  then a Disabled one, each in the page's sort order, each folding. Nothing can be dropped on them.
- **Pinned mods** (the pin was there, but said "Pin" and showed only a 12px icon):
  - always sit at the top, whatever the sort; each run keeps the sort's order;
  - wear a PINNED tag beside DISABLED on cards, List rows and grouped rows;
  - the button says what it does: "Keep through collections" / "Stop keeping", with tooltips
    saying applying a collection never disables a pinned mod.

### Before removing a mod that changes the profile

sp-mod.com marks some mods "may make permanent changes to your profile, and may not be removable
without starting a new profile" (the item page's notice; `shows_profile_binding_notice`, true for
293 of the 1,404 mods in the user's cached catalog, SVM and Skills Extended among them).

- **Removing one** puts that warning first in the Unsubscribe question, in the caution colour, and
  the question is asked even with "Don't ask again" ticked.
- **Removing several** (Unsubscribe selected, a collection's Unsubscribe from all) names the marked
  ones in a check of its own: **Keep these, remove the rest** (default), **Remove all**, or Cancel.
- **The wording does not suggest turning the mod off instead.** A turned-off server mod isn't loaded
  either, so for the profile it is the same as removing it. The warning says so, and points at Undo
  removing and the profile backups.
- **A hand-installed mod's removal now takes a profile backup first** too, as an app-installed one's
  always did - the warning says one is kept.

### Same-version re-uploads

- **Checked against sp-mod.com's catalog, no extra requests:** the catalog fetch already carries each
  version's size and creation time (`content_length`, `created_at` with include=versions); they are
  now kept. A card gets a **RE-UPLOADED** tag, a line saying what changed, and **Get it again**, which
  installs that exact entry over itself, when:
  - a later entry has the same version number and a different size (ten mods in the catalog have a
    second entry; every one whose sizes are listed has the same size twice - a double submit - so
    none of those is flagged); or
  - the size listed for the installed entry differs from the one listed when it was installed (HUNCH:
    that sp-mod.com updates it when an author replaces a file; not seen yet). Installs now record the
    archive's size and the listed size (InstalledModRecord.ArchiveBytes, ListedBytes).
- **Not caught:** a file swapped where it is hosted with the listing left alone. Skills Extended 3.1.1
  is like that: listed at 109,594,837 bytes, served from its GitHub release at 109,594,883 (measured
  2026-10-03). Seeing that would mean asking the download link for the size, which may count as a
  download on sp-mod.com - not done.
- **Two bugs that measurement turned up, fixed:**
  - Kept downloads were checked against the listed size, so Skills Extended 3.1.1's kept 110 MB
    archive would be thrown away and fetched again on every reinstall. Each kept archive now has a note
    of the size listed when it was downloaded (`<archive>.listed`); it stands while the listing is
    unchanged, and goes when the listing changes.
  - Trimming kept downloads stopped after deleting one file (a deleted FileInfo's Length throws, and
    the whole tidy-up gave up). It now deletes as many as it takes.

### Diagnose logs (Tools)

Proposal A from the research doc. Reads, without changing anything:

- **The server's newest log, its last run only** (from where SPT's mod loader starts). On SPT 4.1
  that is `<server>\user\logs\spt\spt<date>.log`.
- **BepInEx\LogOutput.log** (the last game session) and the **errors log in the newest
  Logs\log_\*** folder. The page says when the launcher deletes the Logs folder each start
  (Launcher.log's "Recursive Removal").

**Checks**, each one a line seen in the user's own SPT 4.1.6 logs unless marked:

| Finding | From |
|---|---|
| A profile won't load | "Failed to load profile with ID ... marked as invalid" |
| A profile wears clothing from a missing mod | InvalidModdedClothingException |
| Raid results weren't saved | an error on /client/match/local/end that can't read an enum (SkillTypes) |
| The server couldn't start | "Failed to start the web server ... AddressAlreadyInUse" |
| Plugins that didn't load: missing dependency, incompatible, skipped, duplicate, load error | BepInEx's Chainloader lines, in LogOutput.log or relayed into the server log (duplicate and load error: BepInEx source) |
| A server mod isn't running | enabled 4.x server mod missing from the run's "Mod: ... loaded" list (not said for a mod added after that run) |
| SPT's mod loader reported a problem | SPT source (locale strings), shown verbatim |
| A server error answering the game / a task that keeps failing | "Error handling request" / "Scheduled event ... failed", traced to the mod in the stack trace |
| A mod reported an error | an error whose logger is a mod's code, or a plugin's message relayed by SPT |
| Two mods ship the same bundle | "Unable to add bundle" - on the user's install SPTBetterRearSights and WTT-ContentBackport both ship barrel_vpo215_600mm_366tkm.bundle |
| A bundle needs files that aren't there | the game's errors log ("has dependency ... not found in manifest") |
| A mod logged errors in the game | a plugin's own error lines in LogOutput.log |

**Which mod:** read from the mods' own files (LogModLocator): [BepInPlugin] names and GUIDs from every
plugin DLL, server mods' declared names and GUIDs, the namespaces each mod's DLLs define types in
(matched to loggers and stack frames, longest wins, shared ones name nobody), and bundle files. A
plugin name beats a server mod's name (SAIN's two halves share one).

**Each card:** what it means in plain words, the mod, how often, the line it came from (file and line),
and Show in Subscribed items / Open its folder / Open the log (plus Go to Play, Profile backups).

**Copy a report for help:** the findings and their lines, then up to 50 errors no check knew. Taken out
(LogRedactor): the user folder becomes %USERPROFILE%, the Windows user name <user>, 24-character ids
id-<6 hex of their SHA-256> (the same each time), IPv4 addresses with a port <ip>:port (not
127.0.0.1). No whole logs and no profile.

Run against the user's real logs and mods (staged read-only, 2026-10-03): 157 mods located in under a
second; the last run gave the bundle clash, two server mods installed after that run (since removed),
Epic's All In One's bundle with missing dependencies, and AllQuestsCheckmarks' 13 in-game errors.
Earlier days' logs gave the invalid profile, the missing clothing and LateToTheParty's Fika message.

Checked under Wine with sample logs: every card kind drew, Show in Subscribed items filtered to the
mod, and the copied report read as above. Help topic diagnose.read.

- **Strings and tests (whole round):** 88 new strings, 8 rewritten; 26 new tests (empty folders 3,
  re-uploads and kept downloads 10, diagnosis 13). 1650 pass.

## Round 36: 1.1.0 - newer releases pointed out, AI filter removed, the after-install check (2026-10-03)

### Newer releases (About)

At start (after the catalog's first requests, as before) SSPTMM asks GitHub's REST API for
`/repos/trappuss/SSPTMM/releases/latest` (GitHubReleaseCheck): no sign-in, a User-Agent of
`SSPTMM/<version>`, and the html media type so the notes arrive as `body_html` and render with the
same HtmlText behaviour as a mod's changelog. The answer was measured from the user's PC on
2026-10-03 against the published v1.0.0: `tag_name` "v1.0.0", `html_url`, `published_at`, one asset
`SSPTMM-1.0.0-win-x64.zip` of 67,467,802 bytes, 22,350 characters of `body_html`.

- Shown only as the existing dot beside Help and About, and on About: "Feature update: SSPTMM 1.1.0
  is out", its date and zip size, how to update, **Open the release page**, and What's new. No
  banner. **Check now** asks again. SSPTMM never downloads or installs anything.
- 404 (no release yet) and a tag that isn't a version are "nothing newer", but "You have the newest
  release" is said only after a release was found and compared (ShowNewestRelease); a spent rate limit (403 or
  429 with `x-ratelimit-remaining: 0` or `retry-after`), another refusal, no connection and a timeout
  each get their own line on About and in the log.
- Checked under Wine: with a canned 1.1.0 answer against a 1.0.0 build (the dot, the About block,
  What's new); a canned 1.1.0 against 1.1.0 ("You have the newest release"); and the real request,
  which this build machine's proxy refuses with 403, shown as "GitHub answered with error 403"
  (Check now repeats it). The canned answer was a temporary code change, removed before commit.

### "Hide mods with AI content" removed

sp-mod.com has no AI flag: on 2026-10-03 `/api/v0/mods` and `/api/v0/mod/2945` carried no
`contains_ai_content` field, `filter[contains_ai_content]` was refused (400), and the site's own
filters offer nothing like it. The option is gone from the filter list on Browse and Subscribed
items; a saved default naming it no longer parses and is ignored (SavedFilterDefaults). The model
field and the "Contains AI content" tag stay, so the tag would show if sp-mod.com ever sends it.

### The check after install covers server configs

The check that compares every placed file with the archive skipped every server config in the
config report. It now skips only the outcomes that leave something other than the archive's copy
on purpose (Merged, KeptMine, NotUpdated, Preserved, Removed - ConfigFileOutcome.IsArchivesCopy);
Added, Unchanged, DefaultsUpdated and Replaced are checked like any file.

### Icon and mascot

The user's mascot (assets/ssptmm-mascot.png, transparent) replaces both placeholders. Everything is
generated from it by build/branding/make_branding.py, so a new mascot is one file and one run:

- **AppIcon.ico** (16-256 px): the mascot on a square Steam-blue tile (#171A21 to #2A475E), boxed
  like the mods' thumbnails around it (a first, rounded version was replaced at the user's request).
  The tile is there because the mostly white mascot vanishes on a light taskbar. 16-32 px show only
  the helmeted head - the whole mascot is a smudge that small. The exe, window, title bar and tray
  all take it from this one file.
- **WorkshopBanner.png** (512 px, shown at 203): the same tile with the mascot over "SSPTMM" and
  "Steamified SPT Mod Manager" in Noto Sans.
- **docs/images**: ssptmm-banner.png (README and wiki header), ssptmm-social.png (1280x640, for the
  repository's social preview - set by hand in Settings > General) and ssptmm-icon-256.png.
- **Screenshots** retaken with 1.1.0 under Wine, page by page as before (same window, same data;
  Diagnose against sample logs of the same kinds as before, About with canned GitHub answers - a
  temporary change, removed before commit). The square icon came after the retake, so the title-bar
  icon and banner art in those captures were swapped for the new build's pixels at the same spots
  (16-read-page-first is dimmed by exactly x0.2, so the swap was dimmed the same way).

### Also

- The stray file `e -i HEAD~3` (a `git log` printout committed in TCF Mod Manager's history) is
  removed from the repo. TCF Mod Manager's own repo still has it; a later merge keeps it deleted
  unless upstream changes it.
- SSPTMM-release-to-github.bat takes the release notes from the version's own CHANGELOG.md section
  (`# SSPTMM <version>` up to the next one) rather than the whole file, and stops if there is none.
- Version 1.1.0. 8 new strings, 2 rewritten, 1 removed; 24 new tests. 1676 pass.

## Round 37: experimental - Play starts the game itself (2026-10-03)

Options > **Start the game from SSPTMM** (off by default, an EXPERIMENTAL pill and a warning box on
the card) turns the Play page's launcher card into a profile list, a big green **PLAY** and a
**...** menu. Play starts the server if needed, then does what SPT's 4.1 launcher does and starts
the game. Steam's own library page is the model: one big Play, everything else behind a small
button beside it.

### Source

SPT's launcher for 4.1, github.com/SP-Tushonka/launcher at 21225a2 (2026-09-18), read file by file;
SptLauncherApi and SptDirectLaunch follow it, with the files cited in their comments.

- The server speaks HTTPS with a self-signed certificate at http.json's ip and port (127.0.0.1:6969
  by default). Bodies go both ways as zlib; answers are `{"Response": ...}`; a request with a body
  is a PUT. Routes used: `/launcher/v2/` ping, types, login, register, remove, version, profiles,
  profile, wipe; `/singleplayer/bundles`; `/files/bundle/<name>`.
- Before the game starts: version check; cleanup (BattlEye, Logs, ConsistencyInfo,
  EscapeFromTarkov_BE.exe, Uninstall.exe, UnityCrashHandler64.exe, WinPixEventRuntime.dll unless
  LauncherSettings.json's ExcludeFromCleanup names them; hwecho.dll always); wipe if asked; clear
  `user/sptappdata` if ClearCacheOnLaunch; restore every `*.spt-bak`, then apply each
  `SPT_Data/Launcher/Patches/<patch>/**/*.delta` (HDiffPatch) from the backup; fetch bundles not
  already right (size and time, or CRC32); start
  `EscapeFromTarkov.exe -force-gfx-jobs native -token=<profileId> -config={'BackendUrl':'https://<ip:port>','Version':'live','MatchingVersion':'live'}`.
- The connection is only ever made to this PC (loopback or one of its own addresses, checked in
  ConnectCallback, never through a proxy); that is why the server's self-signed certificate is
  accepted.
- 4.0 is not offered: its launcher checks game ownership, and SSPTMM does not touch that.

### Deviations from SPT's launcher

- A server and game-files version mismatch stops Play with both versions named; SPT's launcher
  logs it and starts anyway (GameHelper.cs: it sets an error message but returns "no mismatch").
- The last three game logs are copied to `Data/GameLogs/<install>` before cleanup deletes `Logs`,
  and Diagnose logs falls back to them.
- A copy of the profiles (ProfileBackups, reasons "wipe" and "profile-delete") is taken before a
  wipe or a delete.
- Patched files are written to `*.spt-new` and moved into place; a failed patch deletes its
  half-written file and stops (files patched before it stay patched - the next start restores every
  backup first, as SPT's launcher does).
- Before a wipe or a delete, Play stops if the copy of the profiles can't be taken
  (ProfileBackups.EnsureBackupBefore).

### UI

- Play page: the profile ComboBox (name, then level, side and edition), PLAY (SteamGreenButton,
  stretched to the ComboBox's height, as is the ... button), status line (red on a problem) with a
  progress bar, Close game while it runs. The ... menu: wipe on the next Play (warned in caution
  colour under the row), New profile... (name and edition inline), Delete this profile...
  (confirm inline), Clear the game's cache, Open the SPT launcher instead, Settings....
- SSPTMM minimises when the game starts (Leave SSPTMM open when the game starts turns that off) and
  comes back to its earlier state when the game closes.
- While it is on, Options > Starting the game's "open the SPT launcher" switch is greyed with a
  note, because Play no longer uses it.
- A version it doesn't support (anything but 4.1.x) keeps the classic card with a note; 4.1 later
  than 4.1.6 works with a note that it is untested.

### Tested

- 34 unit tests against a fake server (ForkDirectLaunchTests).
- A Linux harness against a fake HTTPS 4.1.6 server: real TLS through the local-only connect,
  profiles, types, cleanup with ExcludeFromCleanup, kept logs, a real SPT delta applied to a copy of
  the user's own backup (result's SHA-256 equal to the user's patched Assembly-CSharp.dll), a
  bundle fetched into `user/cache/bundles/<CRC>/`, and the exact game arguments.
- Under Wine, the app itself against the same server: PLAY from a stopped server through to the
  game started (args checked), wipe (backup first), new profile, delete (backup first), clear cache,
  Close game, and switching the option off and on. The copies of the user's game files used here
  were deleted afterwards.
- Not tested: the real SPT server and game. That needs the user's PC.

### Also

- SharpHDiffPatch.Core 2.3.0 (MIT) applies the patches; it brings ZstdSharp.Port (MIT) and
  Hi3Helper.ZstdNet (BSD-3). Its native libzstd.dll is not in the publish output, and SharpHDiffPatch
  then uses ZstdSharp (CompressionStreamHelper.CreateZstdStream). Licence texts for those, HDiffPatch
  and Zstandard added to `Licenses\` and THIRD-PARTY-NOTICES.md.
- Help topic "Play straight from SSPTMM (experimental)"; wiki Play, Options and Safety pages; a new
  screenshot, 01b-play-direct.png.
- An independent review of the change found no high-severity problem; its findings were fixed: a
  wipe or delete no longer goes ahead when the profile copy fails; file and config errors stop Play
  with a message instead of the crash dialog; Cancel reaches the profile actions; containment
  checks for written files fail closed (SptDirectLaunch.IsSafelyInside).
- Closing the game when the server stops: on the user's PC the watch fired each time and the game
  began quitting (its own logs show the quit), but the user saw the game stay open. What the close
  did next was never logged (a failed kill went to Debug), so every step of a stop or close is now
  logged at Info - asked, closed, killed, or still running - a failure as a warning, and a failed
  close shows as an error on the Play page. The cause is still open until a run with this logging.
- 86 new strings; 40 new tests. 1716 pass.

## Round 38: presets, a page that remembers itself, closing the game with the server (2026-10-04)

### Closing the game when the server stops

The user's own log (with Round 37's step logging) settled why the game "didn't close": every close
with the server already gone was asked, sat the full five seconds and was killed (19:43, 22:09,
22:52 on 2026-10-04: 3 of 3), while every close with the server still up finished by itself in
2-5 s. The game's quit asks the server to cancel invites and log out (its backend log: cancel-all,
then logout, each waiting for an answer), and with no server it waits. With the watch's 3 s poll
and two down-checks on top, the game stayed up some fifteen seconds after Stop server and was
closed by the user's hand or read as not closed.

- **Stop server** with the box ticked closes the game first (normal 5 s grace), then the server
  (PlayViewModel.ConfirmStopServerAsync, CloseGameBeforeServerAsync). Wine test: game asked, gone in
  2 s, then the server.
- Any other stop: GameCloser polls every second (off the UI thread), still two down-checks, and gives
  the game ServerGoneGrace (2 s) before killing it. Wine test with a fake game that refuses to close:
  server killed at :43.5, noticed :45.4, game killed :47.4.
- Stop server's message says both outcomes when the game couldn't be closed.

### Subscribed items remembers its filters

InstalledViewModel.Remember saves the filters, sort, grouping, view and page size half a second after
the last change, into AppSettings.InstalledDefaults (where Save as default wrote), so an old default
is where 1.2.0 starts. Not kept: the search box; "Updates available" chosen by a notification click;
a category that fell back to All because nothing installed is in it (offline, with no catalog, that
is every category) - the remembered one stays until a category is picked, and comes back when it is
installed again. A change made before the first scan is saved once the lists exist; a pending save is
written on exit. Clear filters now resets to the app's own defaults (it used to reset to the saved
default); the Save as default button and Options' Subscribed items row are gone. Browse is unchanged.
Wine test: List view + Z-A survived a restart.

### Presets

Core's ModPresets: one entry per mod folder or loose DLL, keyed by its install-relative path as it
reads when enabled (DisabledModPaths.ToEnabledRelativePath), so a client+server mod with one half off
is kept that way. Plan() lists what moves; mods the preset doesn't name are left alone; a mod in the
install twice (enabled and .disabled copies) is left alone and said, and never counts as matching.
Data\mod_presets.json, per install (the ProfileBackups install hash), written with SafeFile backups; a
damaged file is set aside and read as empty, a file that can't be read is never written over.

Applying (InstalledViewModel.Presets): rescan; confirm naming every mod turned off and on, the hard
dependencies a mod left on would lose (ModDependencyGraph), mods sp-mod.com marks as changing the
profile, mods skipped; the install-queue and SPT-running checks again after the dialog; a profile copy
that must succeed (EnsureBackupBefore, reason "preset"); the mods as they were kept as Put back; then
ModDisableService off and on, one Undo step. Disable all / Enable all are presets made on the spot.
The menu (InstalledPage.xaml.cs) runs each action after it has closed. Mod Organizer 2's profiles are
the model; "Presets" because SPT's profiles are something else.

Wine test against a fixture of public mods: save, Disable all (10 folders), restart, Put back, a
preset breaking SAIN's BigBrain dependency (warned), rename, delete.

### Version

1.1.0 had already been released from aa978a5 (GitHub's tag v1.1.0 and main, checked 2026-10-05), so
Round 37's direct launch and this round are 1.2.0: Directory.Build.props, a new CHANGELOG section
(1.1.0's put back exactly as released), the code comments added since, and the version digit in the
screenshots' title bars (taken from a 1.2.0 window; 15b keeps its older version on purpose).

### Review and audit

An independent review found no high-severity problem; fixed from it: the re-check after the dialog,
the category fallback, a scan already running, counts by mod rather than folder, mods in two places,
the exit flush, settings write errors, the UI-thread poll, the Stop server message. A release audit
added: the changelog lines for minimise-while-playing and the greyed launcher switch, *.bundle and
.claude/ in .gitignore, licence notices for the Windows SDK projection and C#/WinRT (in the exe since
the 1809 target), a scrubbed account name in a test, the original app's unused screenshots removed
from assets, and stale pointers to files not in the repository.

## Round 39: 1.2.0 - on its own, asking first, every connection written down (2026-10-05)

TCF Mod Manager 1.19.1 and its Server Map addon 4.0 came out. Read through for what applies here:

- **Ported: the install guard (TCF 1.19.1's fix).** With the exe in the SPT root rather than a folder
  of its own, every file below the exe's folder counted as the app's, so every install, update and
  removal was refused. InstallPathGuard now claims only the app's own items there (`AppOwnedNames`,
  with SSPTMM's exe, pdb and `Licenses` added) and the whole folder only when the app has one to
  itself. An archive whose every file is refused now fails with NothingToPlace before anything is
  touched, instead of saving an empty record over the previous version. Files left out because they
  would land on the app's own items are reported apart from SPT's protected files. Tests:
  AppFolderGuardTests.
- **Already here:** the Server Map addon speaks the protocol SSPTMM already speaks; the
  sort fix was covered by Subscribed items' own sort.
- **Not taken:** sp-mod.com list import (unfinished in the original, and it adds AngleSharp); the
  performance changes touch code SSPTMM replaced.

**On its own.** SSPTMM no longer follows the original's releases. What a user sees says SSPTMM
everywhere, with one credit line kept where the MIT licence and the original's author are owed it:
About ("SSPTMM began as a fork of TCF Mod Manager by TheCrimsonFckr, under the MIT License"), the
README's Credits, the wiki's Home and `LICENSE`. `SelfMod.OriginalVersion` is gone; Help's guide
link opens SSPTMM's own wiki rather than the original's sp-mod.com page; `docs\sp-mod-guide.md` and
`docs\tcf-mod-manager-readme.md` (the original's texts) are removed. The Server Map guide still says
the mod is TheCrimsonFckr's. Code names and SPT-side folders are unchanged (see Upstream updates).

**Asking first.** sp-mod.com's content guidelines ask that update checks have the user's consent
and that every connection be documented:

- The GitHub release check (and the Server Map mod's version check that rides with it) no longer
  runs at start until the user answers a one-time question (`AppSettings.CheckForNewReleases`,
  null = not asked yet). **Check at start** / **Don't check**; About has a checkbox to change it.
  **Check now** always works. The Server Map mod is checked only when it is installed or its page is
  on.
- Pictures send `Referer: https://sp-mod.com/` only to sp-mod.com's own hosts
  (SpModRefererHandler), not to YouTube or a description's picture host.
- Closing SSPTMM with update notifications off now removes the Windows notification registration
  (HKCU AppUserModelId and CLSID keys, the icon copy in %LocalAppData%) - UpdateToasts.UnregisterIfOff,
  on exit only, because the toolkit registers once per process and can't register again after
  Uninstall until a restart. `Data\notifications-registered` marks that the toolkit was touched, so
  an install that never had notifications on never touches it.
- The one-time question: X or Esc saves nothing and checks nothing; it is asked again next start.
  The answer is saved over a fresh load of settings.json, since the Server Map can save while the
  dialog is open (SettingsService has no merge).
- `wiki\Files-and-Network.md` lists every server SSPTMM talks to, when, what is sent and how to
  stop it, and every file and folder it writes. Uninstalling (wiki) now says to switch
  notifications off first and to clear `%TEMP%\SSPTMM*`.

HUNCH: whether sp-mod.com counts the Server Map's once-a-minute reporting (after its own consent
dialog) as needing anything more than the documentation it now has.

## Round 40: 1.2.1 - a folder of its own in the zip, its own listing hidden (2026-10-05)

sp-mod.com disabled SSPTMM's listing (mod 3111) pending proper review and testing. Against the
content guidelines:

- **The zip holds one folder, `SSPTMM\`** (release script: published into `release\<pkg>\SSPTMM`
  and zipped from `release\<pkg>`). The guidelines expect an archive to unzip straight into the
  SPT root; this one then gives `<SPT>\SSPTMM\`, a folder of its own - the layout InstallPathGuard
  already treats as the app's whole folder. Checked under Wine: 1.2.1 run from
  `<SPT>\SSPTMM\`, SVM subscribed (Greed.exe placed at the root) and unsubscribed (removed, kept
  in `.tcfmm-removed`, config set aside in the app's Data). The Server Map finds its folder from the
  SPT path either way. (The single-file exe doesn't start under Wine at all - CoreLib load error
  0x8007046C, in any folder - so the Wine run used a normal publish.)
- **SSPTMM's own listing is never offered**, like TCF Mod Manager's: `SelfMod.IsOwnListing`
  (2945 and 3111) behind BrowseViewModel.IsSelf, public collections and mod lists. Tests:
  SelfModTests.
- **Claims trimmed to what was tested:** SPT 3.x is no longer claimed (only 4.0 and 4.1 were used).
- **Manual test checklist:** `SSPTMM-test-checklist.bat` runs `tools\ssptmm-test-checklist.ps1`,
  which asks P/F/S and a note for each advertised feature on a fresh SPT install and writes
  `test-reports\SSPTMM-<version>-test-<date>.md`. Checked with PowerShell 7 here; written for 5.1.
- **The release script is tracked** (no longer in .gitignore): it never moves the branch.

## Round 41: 1.3.0 - self-update, safer saves and installs, Play warns about missing needs (2026-10-06)

- **Settings saves merge** (SettingsService): every part of the app loads its own AppSettings copy
  and saved it whole, so overlapping load/save pairs lost each other's changes. Load now keeps a
  JSON snapshot per copy (ConditionalWeakTable, by identity); Save, under one static lock, applies
  only what that copy changed onto the file as it is now (objects recursively, so dictionary keys
  merge; lists are one value). A never-loaded copy is written whole. Tests: SettingsMergeTests.
- **AppLog** writes each line under one lock and re-drains a queue filled while draining ended, so
  lines no longer interleave or wait for the next message.
- **SPT found around the app** (SptRootResolver.InstallAroundApp): with no install set and the
  exe's parent folder holding EscapeFromTarkov.exe, that folder is set at start and logged. Only the
  parent is checked - the layout the 1.2.1 zip gives. Tests: InstallAroundAppTests.
- **Install journal** (InstallJournal): a note in `Data\install-journal\` from before placement until
  the install record is saved. Left behind = cut off; at start the planned paths that pass
  InstallPathGuard and exist - minus files already there at Begin and unchanged since (size and
  write time), unless the previous record owned them - plus what is left of the previous version
  (with its fingerprints) are recorded as an incomplete install of that mod, and the user is told
  once the window is up. A journal whose mod has a finished record newer than it is just removed;
  one that can't be read now is left; a damaged one is set aside. A failure inside InstallAsync once
  placing has begun recovers at once; before that the journal just goes. Tests: InstallJournalTests.
- **Review fixes:** settings that fell back to defaults, or whose merge result doesn't read back
  (a wrong-typed value, a duplicate key), are written whole, which repairs the file. The update
  link is checked as a parsed URI (https, github.com, default port, no user info, path under the
  repository's release downloads). Auto-detect also needs SPT's server beside the game exe, so a
  live game folder is never picked.
- **Install update** (About): TCF's AppUpdateInstaller, pointed at SSPTMM - the zip asset of the
  latest GitHub release, accepted only under `github.com/trappuss/SSPTMM/releases/download/`, staged in
  `.tcfmm-update\`, applied by its PowerShell script after the app exits (robocopy /E, never /MIR).
  A SteamDialog confirm names version and size first. Applying could not be run under Wine; staging
  is tested (SelfUpdateStagingTests).
- **Play page needs warning**: ModConflicts.ScanWithNeedsAsync also returns enabled mods with a
  missing or disabled-only dependency (InstalledViewModel.MissingDependencies, shared with the
  card flag). The Play card shows when there are conflicts or needs. The conflict warning itself
  was already there from TCF.
- **CI**: `.github/workflows/build.yml` - build and test on windows-latest; on `v*` tags it checks the
  props version, publishes the single-file zip as an artifact with its SHA-256. No release is made.
- **Exe compression measured, not adopted**: EnableCompressionInSingleFile took the exe from 170.1 MB
  to 73.4 MB on disk but the zip only from 68.1 to 67.5 MB, at a start-up cost (decompressing to
  memory each start) that could not be measured here. Left off.

## Round 42: 1.3.0 - fixes taken back from TCF Mod Manager (2026-10-06)

TCF Mod Manager's 70 commits since the fork (8bde5ed..87c4231) were read one by one against
SSPTMM. Most were already here (several were ported from SSPTMM: profile backups, the queue, kept
.cfg, empty folders, labelled versions, config kind changes). Taken back, each marked in the code:

- **Held-back updates in the background check** (be28c66): UpdateCheckService keeps the blocked and
  not-for-SPT answers of the /mods/updates call it already makes; UpdateWatcher hands them to
  HeldBackUpdates.Apply before picking what to announce, and leaves held-back updates out. A ticket
  taken before asking (`Begin`) drops an answer older than the last question asked; an answer split
  over several requests (long installs, MaxModsQueryLength) is not applied, since a blocker in one
  part can't hold back an update in another. The item page's Update button and the right-click
  menu (`ModActions.CanUpdate`) follow. Not taken: upstream's "safe older version" offer
  (HeldBackVersions) - more plumbing through the update paths than this round wanted.
- **Addon on a collection fits its parent** (448af87): Core `NewestFittingVersion.ForParent` and the
  parent-version map in ModListService.ResolveAsync (list's named version, else installed). Mods
  keep SSPTMM's rule (newest for this SPT, else newest - the queue asks before one that won't run).
  `per_page=50` on /addon/{id}/versions checked live (200).
- **Small ones:** Restart question cleared when the target stops by itself (4315064 X4); Configs'
  running note per install (99dd8f2); failed downloads logged including no-link, unexpected
  exceptions with stack (673c324); install-anyway questions default to No (83a8942); DLL-read
  versions vs labelled releases on the Dependencies page (5ec9304); ProfileBackups.Restore throws
  NoInstallFolder, the backups list rebuilds on a language change, and two backups run off the UI
  thread (cdf20f9); the unused `filter[contains_ai_content]` emit lines removed (SSPTMM had dropped
  the option first, round 36; the API answers it 400).
- **Install journal**: keeps the previous version's label when nothing of the new one was written
  (no planned file is new or changed since Begin - so the previous version's files at the same paths
  still count as old, with their fingerprints). Upstream keeps the old record in that case too;
  its journal (d865b9f) is otherwise weaker. Removing a hand-installed mod re-checks that SPT isn't
  running after its (now background) profile copy, before moving configs.
- **Not taken:** GradualFill / InstallFingerprint (Subscribed items performance - can't be measured
  here), a right-click menu acting on the selection, the sp-mod list import window (Collections
  covers it), detached-parent and count checks in the list parser (no real page found with one).

## Round 43: 1.3.0 - the last non-Steam dialogs, and a filter that came back (2026-10-06)

- **"Installed in the last 7 days" at every start:** InstalledViewModel.DefaultUpdateFilter read a
  saved Sort = "RecentlyInstalled" as that filter - a migration from upstream's c486519, which moved
  Recently installed from Sort by to the update-status dropdown. SSPTMM brought the sort back
  (5ba33de), so sorting by it turned the filter on at the next start whatever the dropdown said, and
  clearing it only held until a restart. The migration is gone; only the saved update status counts.
  Checked under Wine with UpdateStatus "All" + Sort "RecentlyInstalled" saved: the page opens on Any.
- **SteamModalWindow** (Views/SteamModalWindow.cs, SteamModalWindowStyle in SteamStyles.xaml): the
  SteamDialog look for dialogs with more in them - glow, the blue line, 22px bold title (the Window's
  Title, also its drag handle), the X, Esc, the main window dimmed while it is open (DimsOwner),
  #ACB2B8 text with the theme's text brushes re-pointed, headings white. The seven FluentWindows that
  were left (ModDisableConfirmation, SptUpgrade, InstallRole, DownloadConfirm, ModListVersionChange,
  ModListAddMod, DataFiles - the last two resizable with a grip, Data files not modal) are on it; their
  ui:Buttons are SteamModal green/blue/grey. ModPageWindow (a browser) keeps its window.
- **SteamMessageBox**: MessageBox.Show's arguments and answer over SteamDialog, with Common_Yes/No/OK
  in all five languages; all 21 calls moved, except the crash report, which stays Windows' own box
  (it has to show when the app's resources are what broke). Closing answers Cancel, else No, else OK.
  Called off the UI thread, it hops onto it.
- Steam's tooltips are light grey on purpose (the community tooltip), so they were left as they are.

## Round 44: 1.3.1 - long disable lists, and profiles SPT won't load (2026-10-08)

- **ModDisableConfirmationWindow** is a six-row grid: the picked names in a ScrollViewer capped at
  110px, the affected list in the `*` row (MinHeight 60), note and buttons below. The replaced-versions
  list in ModListVersionChangeWindow scrolls the same way. SteamModalWindow caps every dialog at the
  work area's height less 40px (at least 320). Checked under Wine with 47 picked and 30 affected.
- **Invalid profiles.** Measured from SPT 4.1's server source: ProfileMigrationService marks a
  profile InvalidOrUnloadableProfile when ProfileValidatorHelper finds a removed mod's item, clothing
  or trader (unless core.json's fixes.removeModItemsFromProfile is true); GameCallbacks.GameStart then
  refuses it (505001) and SaveServer.SaveProfileAsync skips it - so LauncherV2Controller.Wipe's change
  is never saved and a wipe can't help. SptDirectLaunch.RunAsync now asks /launcher/v2/profiles after
  login and returns DirectLaunchProblem.ProfileInvalid (before the clean-up and the wipe) when the
  profile is marked. LogDiagnoser reads InvalidModdedItemException / InvalidModdedTraderException
  (fixer-mod_item_found, fixer-trader_found) as ProfileItemMissing / ProfileTraderMissing with the id.
  The messages name SPT's own fix: removeModItemsFromProfile and removeInvalidTradersFromProfile in
  SPT_Data\configs\core.json, then a server restart.

## Round 45: the tool itself, measured for speed (2026-10-08)

Nothing here changes what the app does or shows. Measured on Windows 11 with a throwaway console
program calling Core, against copies of a real Data folder (catalog of 1,424 mods and 6,127 versions;
install records for 122 mods, 5,555 files; three collections, 270 entries) and, read-only, a real
SPT 4.1 install with 180 mod folders. Times are medians of warm runs.

Changed:

- **Hashing a file** (FileFingerprint.Compute) read through a FileStream given a 1 MB buffer, which
  .NET allocates in full for every file. 300 small files: 307 MB allocated and 73 full collections,
  48 ms; now 0.2 MB, none, 26 ms, same digests. An install hashes every file twice.
- **Zip extraction** gave each entry's file the same 1 MB buffer: 330 MB allocated for 320 entries,
  23 MB now. The time did not move (290 ms either way; the disk decides it).
- **SPT version constraints** (SptVersionRange.TryParse) are remembered by their text. Browse's filter
  parsed every version's constraint on every keystroke, twice with an SPT line ticked, and the
  catalog holds 135 distinct ones: 6.0 ms and 9.2 MB per pass, now 0.6 ms and 0.3 MB.
- **Workshop Home** read the collections file once per card, forty times a visit: 51 ms of the UI
  thread. Now once per row of cards, three reads of 1.2 ms.
- **The install records** (1.3 MB) take 10 ms to read and were read twice per Subscribed items
  scan, on the UI thread. Now once: the re-upload check is handed the records the scan read.
  Moving that read into the scan's background work was tried and taken back out - saves made from
  the UI thread replace the file, and a read open on another thread at that moment could fail them.
- **The catalog cache** is read and written as UTF-8 bytes, not through a string: load 29 ms and
  14 MB, now 8 ms and 6 MB; the file written is byte-for-byte the same.
- **An update** no longer hashes each old file it is about to replace anyway (the answer was not
  used). **A removal** no longer re-checks a folder's parents for every file in it.
  **InstallPathGuard.IsLink** asks the disk once per folder, not twice. None of the three was timed
  on its own: no test mod here has the thousands of files where they show.
- **Mod pictures** are kept as downloaded (24 MB, by address) as well as decoded: the Browse card,
  the popup over it and the item page ask for 245, 254 and 268 pixels, which all fit the same
  384-pixel copy, and each fetched it again.
- **Tests**: ForkPrepatchInstallTests failed in 3 of 15 runs of the suite, before any of this.
  AppPathsTests deletes Data\LegacyConfigs, which the install tests copy configs into before merging
  them; it now runs on its own (a collection with DisableParallelization). 25 runs, no failure.

Measured and left alone:

- **ReadyToRun**: the window is up in 1,041 ms against 1,171 ms (medians of 8 warm starts each,
  offline, catalog cached, no SPT folder set), for an exe of 246.3 MB against 170.1 MB. Not adopted:
  a tenth of a second for 76 MB. Compiling only the app's own assemblies was not tried.
- **The Play page's 2-second poll** asks Windows for its process list five to seven times on the UI
  thread: 8.4 ms a tick with 378 processes running. Under a frame; left as it is.
- **InstalledModScanner.Scan**: 121 ms for 180 mod folders, off the UI thread. Reading the DLLs'
  metadata is 33 ms of it, and 10 of those are opening the files, so skipping the type walk for
  DLLs that cannot be plugins would save about 4 ms. Not worth a second code path.
- **Reading the collections file** is 1.2 ms and settings 0.2 ms, so the places that read them two
  or three times in a row (Your collections, the Subscribed items scan, start-up) stay as they are.
- **SafeFile's backup check** parses the old file before finding no backup is due: about 10 ms per
  save of the install records, off the UI thread. Left; reordering it changes when a warning is logged.

## Round 46: Subscribed items, on Steam's own rows (2026-10-08)

The cards were hard to read and did not look like the rest of the Workshop: WPF UI's stock expander
on the theme's card fill (black at 20%, so the page grid ran through every title), an 11px line in
#8F98A0 under the name, and collection chips in the accent blue on mid grey.

A first pass drew them on the Browse card's gradient with a white edge and left the disabled ones
at full strength. Seen on a real install that was worse in places: 132 outlined boxes, each with
its own glow. It was taken back out and the rows of Steam's "Your Workshop Files" lists were read
instead (workshop_userfiles.css, 2026-10-08):

- `.workshopItemCollectionContainer`: `background-color: #273b52`, `border-radius: 4px`,
  `box-shadow: 2px 2px 10px rgba(0,0,0,0.3)`. No border.
- `.backgroundImg`: the item's own picture across the row's width, `filter: blur(30px)`,
  `opacity: 0.3`, `top: -30%`, masked left to right from black (at -25%) to nothing. That is the
  light behind a Steam row - not a highlight of the row's, the mod's picture out of focus.
- `.workshopItemCollection:hover`: `background-color: rgba(190,209,228,0.2)`.

A card and a List row are now that (InstalledPage's SubscribedBackdrop): the navy panel, the mod's
picture blurred behind it and fading to the right, a drop shadow and no edge. Under the pointer it
takes the 20% tint and its shadow turns white, as a Browse card's does. A ticked one has the accent
edge and tint. The blur is done on a 64px copy of the picture (a deviation of 3px there for Steam's
30px on a 650px row), kept as a bitmap and stretched: 132 blurs at a card's size were not wanted.

Also:

- **Disabled mods are greyed out again**, the whole card at 45%, as before the first pass.
- **DISABLED is a tag like the others.** It was a second row under the picture, in another shape,
  and made a disabled card taller than its neighbours. It, PINNED, RE-UPLOADED and the group now
  sit on one line with the collections' tags, under the facts line and in line with the name.
- **The facts line** (version, client/server, author) is 12px in Steam's body grey, #ACB2B8, in
  all three views; an opened card's Installed version and Latest published too.
- **A collection's tag** is light blue on that blue at 20%.
- **The Groups view's sections** have a solid dark fill (#1B1D21) and no edge.

Layout, controls and words are otherwise as they were.

Checked in pictures of Cards, List and Groups with cards opened, ticked, disabled and (faked)
under the pointer, from a copy of the app run on a hidden Windows desktop with software rendering,
against a copy of a real install's 132 mod folders. Not checked: a real pointer, scrolling speed
with a graphics card (each card is two cached shadow layers and one cached 64px picture more than
before), a light Windows theme, and a background picture behind the cards.

## Round 47: the cards without effects; Play from SSPTMM for everyone (2026-10-08)

**Subscribed items was very slow to scroll after round 46** (reported from a real PC; it could not
be timed here - the pictures in this log are drawn without a graphics card, on a desktop nothing is
shown on, where WPF neither presents frames nor redraws for a window capture, so two attempts at a
scroll benchmark measured nothing). What round 46 had put in every card: a BlurEffect, an opacity
and an opacity mask over the picture, and two DropShadowEffect layers - and half the cards sit
inside a 45% opacity of their own, where all of that is worked out again on every frame. Keeping
the picture's layer as a bitmap was tried first and was not enough. All of it is gone:

- **The picture** is blurred, faded and dimmed once, in code, on a background thread
  (Behaviors/CardBackdrop): shrunk to 64px, three passes of a box blur (a deviation of 3.5px, for
  Steam's 30px on a 650px row), Steam's mask and its 30%, kept as a frozen 16 KB brush that every
  card showing that picture paints with. It uses the card's own thumbnail - no second download or
  decode.
- **The shadow** is worked out once for a small rounded rectangle and cut into four corners and
  four edges; a card draws those eight pieces, edges stretched (Behaviors/CardShadow). The white
  one under the pointer is the same, made the same way.
- **A disabled mod has no picture behind it.** At 45% it could barely be seen.
- **Options > Picture behind each subscribed mod** switches the pictures off (AppSettings.
  CardPictures, on by default).

The page looks the same as it did after round 46, in pictures of Cards and List.

**Play from SSPTMM is no longer experimental and is on by default** (AppSettings.DirectLaunch).
The EXPERIMENTAL tags on its Options card and on the Play card are gone, the Options note is titled
"Not made by the SPT team", and Help, the README and the wiki say on by default. A settings file
from before holds "false" whether or not anyone chose it and is left as it is. It is still only
used on SPT 4.1.3 to 4.1.6; Play opens SPT's launcher on any other.

## Round 48: cards a few at a time; sorting by enabled and disabled (2026-10-08)

Round 47's cards were reported "much better" on the PC that had found round 46's slow, with one
thing left: with smooth scrolling on, the glide stuttered "as new mods come into view", and not
with it off.

- **The infinite list adds its cards two at a time.** It added 24 at once when the view came
  within 800px of the end, and building 24 cards is one long moment in which nothing else is drawn -
  a glide stops dead in it, a jump hides it. Each step now adds two and waits its turn behind
  drawing and input (Dispatcher Background), asking for the next one itself until the end is 1600px
  away again (InstalledPage.QueueLoadMore). In the copy run for pictures the longest wait for one
  scroll step went from 272-447 ms to 103 ms - a copy that draws no frames, so a sign of less
  work at once and not a frame time.
- **Sort by: Enabled first, Disabled first** (ModSortOption), each half by name. Pinned mods stay
  at the top, as in every order. The page remembers it like the others.

## Round 49: 1.4.0 - one line of tags, greying without an opacity, Groups rows (2026-10-08)

- **Tags stay on one line** (Behaviors/OneLinePanel, the collections' ItemsPanel): what does not
  fit is left out and counted in a "+2" drawn after the last tag, with every collection's name in
  the tooltip. On a card the state tags (DISABLED, PINNED, RE-UPLOADED, the group) come first and
  the collections take the rest of the line, which is there (20px) whether or not a mod has a tag -
  so every closed card is the same height. Before, a mod in three collections was a line taller
  than its neighbours.
- **A disabled mod is greyed out in its colours, not by an opacity.** One Opacity of 45% on a whole
  card makes WPF draw the card to one side and fade that, for 63 cards of a 132-mod install, every
  frame. The 45% is now the alpha of the panel's fill, the name, the facts line and the expander's
  arrow; only the thumbnail, the status and switch, and the tags still carry an opacity, each a
  small thing on its own. A disabled card has no shadow, and its opened body is at full strength
  (it was faded with the rest). In pictures the closed cards are the same as before.
- **Groups rows are Steam's rows too**: the same backdrop as a card and a List row, in place of the
  flat dark row. Fill, hover and picked come from the backdrop.
- **1.4.0**: version, CHANGELOG, and docs\images 07 (Subscribed items: cards, list, groups), taken
  from a copy of the app on a hidden desktop with a dozen well-known mods and made-up collection
  names. The Play and Options pictures (01, 01b, 14) still show 1.3.1's and need retaking on a real
  install: a copy has no server to start and its paths are a temp folder's.

## Values that could not be measured (marked HUNCH in the source)

- Round 49: that greying in colours is quicker than the opacity it replaces (the reason to expect it
  is above; no frame time was taken).
- Round 48: two cards a step and 1600px ahead.
- Round 47: that the cards are now as quick to scroll as they were before round 46. Nothing in
  them is an effect any more, which is the reason to expect it; the speed itself was not measured.
- Round 46: the shade over an opened card's body (black at 20% - Steam's rows do not open), the
  collection tag's blue (#67C1F5, Steam's store tag as remembered) and the Groups sections' fill.
  The row itself is measured.
- Round 45: how much sooner a picture shows from the kept download (it saves one request to
  files.sp-mod.com; not timed - the app was not run against the site for this), and the 24 MB kept.

- Round 37: everything about the real SPT 4.1 server and game under direct launch - only a fake
  server was used here. Also whether 4.1 releases after 4.1.6 start the game the same way. And the
  bundle cache: SPT's launcher puts it under `<game>\SPT_Runtime`, SSPTMM under the server exe's
  folder - the same place in a standard 4.1 install, not checked for others.

- Round 35: that sp-mod.com updates a version's listed size when its file is replaced; and which of two
  entries with one version number the download link serves (the link is by number).

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
- Fika's server mod: that its folder name contains "fika" and "server", or its ModGuid starts
  "com.fika." (not installed on the SPT 4.1.6 install checked in round 15; its plugins' GUIDs,
  the log and the profiles locations were confirmed there).
- A folder named by 7-Zip or WinRAR for a dragged-out archive is in the temp folder and deleted
  when the drop ends (known from how they work, not seen here).

- The downloads bar (40px on `#171A21`, its text sizes and greys) and the Options gear (36px,
  Steam's grey button colours): Steam draws both in its client, not on a web page, so they are
  chosen to match the Workshop palette, not measured.
- The "?" (InfoTip): a 15px ring in the Workshop's secondary grey, brightening under the pointer.
- The on/off switch (34x18, #1A9FFF on, #3D4450 off), the selection bar (#23262E with a blue top
  line) and the Getting started tiles (Steam green #75B022 behind a tick): Steam draws switches
  and bars in its client and has no checklist, so there was nothing to measure.
- Ctrl+click and Shift+click picking as in Steam's library: from how Steam behaves, not checked
  against a Steam client here.
- Your collections (round 30). Steam's own page needs a login, so the grid was not measured
  against it. These values were chosen, not measured:
  - two columns;
  - 152px cards with a 2x2 mosaic;
  - the chip colours: Update blue, Shared and Following grey, and From server;
  - the look of the clipboard and update notices.
- Per page (round 31). Steam has no Infinite and no Per page at the top, so these were chosen,
  not measured:
  - "Infinite" placed last on the author page's row;
  - the top Per page, which is shown only while the list is infinite.
- Close game (round 32). None of this was checked against the real game:
  - **How long a server must be down before it counts as stopped** (two checks 3 seconds apart):
    chosen, not measured.
  - **That a Fika headless client from the same install has no window:** a headless client is
    started without graphics, but this was not checked against a real one.
  - **Whether real Tarkov closes within 5 seconds when asked:** only a stand-in was closed here. A
    game that is still open after that is killed.
- Mod tools (round 34):
  - **Greed (SVM) wants the server stopped:** taken from its built-in help as read in the
    2026-10-03 research, not run here.
  - **give-ui wants it running:** from its sp-mod.com page.
  - **The real tools:** neither real tool was launched here; Wine's notepad stood in.
  - **The card's look** (row tint, 32px icon, button order): chosen, since Steam has no such card.
- That cloud-sync folders (Dropbox, OneDrive, Google Drive) deliver the shared file promptly and
  whole. Only a plain local folder was tested here. A file caught mid-sync reads as damaged and
  is ignored until the next check, but that was not seen happen.
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

TCF Mod Manager's own update check never runs (SelfMod.IsFork, since round 22), because installing
the original's download would replace SSPTMM. New SSPTMM versions are published as GitHub releases;
since round 36 SSPTMM can ask GitHub for the newest one at start (GitHubReleaseCheck) - telling
only, never installing - and since round 39 only once the user has said yes.

Since 1.2.0 SSPTMM no longer follows TCF Mod Manager's releases. A fix in the original that also
applies here is ported by hand, as a commit of its own that says where it came from (round 39 did
this for TCF Mod Manager 1.19.1's install guard). The code's internal names (`TCFModManager.*`) and
the folders inside an SPT install (`.tcfmm-*`, `TCFModManager\ServerMap`) stay as they are, so an
install shared with the original stays readable by both and the Server Map mod keeps working.

## Licence

SSPTMM is under the MIT License (`LICENSE`), with two copyright lines: TheCrimsonFckr for TCF Mod
Manager, and trappuss for SSPTMM. MIT because TCF Mod Manager is MIT: sp-mod.com lists it under
"MIT License", both on its page and in the API's `include=license` (checked 2026-10-03). Its GitHub
repository has no LICENSE file of its own, so that listing is the only written grant.

The release build carries the .NET and Windows Desktop runtimes (self-contained) and the NuGet
libraries; each one's licence text ships in `Licenses\` beside the exe, taken from the package
itself (SharpCompress's package has none, so its `LICENSE.txt` comes from the repository at tag
0.50.4). `THIRD-PARTY-NOTICES.md` lists what each file covers. When a package is added or
updated, add or refresh its licence file in `build/steam-ui/licenses/` and its row there.
