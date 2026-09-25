# Steam Workshop UI fork

This fork of TCFModManager dresses the app as Steam's Workshop: the same palette, type, layout
and wording as `steamcommunity.com/app/<id>/workshop/`, with every mod-manager feature still
there underneath. This file records **where each value came from**, **what maps to what**, and
**every place the fork knowingly differs from Steam or rests on a guess**.

Build and run: double-click `steam-ui-build-and-run.bat` in the repo root. It installs a local
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
| Most Recent / Last Updated / Most Subscribed / Top Rated All Time | Newest / Last updated / Most downloaded / Most endorsed |
| SPECIAL FILTERS | Featured include / exclude / only |
| CONTENT TYPE | Category |
| Tag rows [+]/[-] | The attribute filters: [+] Fika compatible, Has dependencies, Has addons; [-] Contains ads, AI content, Subscribed |
| Required items | The shown version's dependencies (sp-mod.com sends no optional flag today) |
| Change Notes | Every published version with its changelog |
| File Size | The shown version's download size |

## Where the fork differs from Steam, on purpose

- **No star ratings.** The catalog has none; endorsements sit where the stars would.
- **No time frame** in the sort panel, and **Most Popular** is not offered: the catalog records no
  per-period popularity. The home page's first list is "Top Rated" (endorsements), named as such.
- **The week's row** is "released in the past week, most downloaded first" - the nearest honest
  reading of Steam's "in the past week" row.
- **Discussions, Comments, About, Awards, Favorite, Add to Collection** are left out: nothing in
  the app sits behind them.
- **Tag rows with one direction.** Each app filter goes one way, so the other box is shown disabled
  rather than removed.
- **The hub header stays put** while pages scroll (on Steam it scrolls away) - it is the app's
  navigation.
- **Status badge on card previews** (installed / update / disabled, and the pin): a Workshop page
  cannot know what is on your disk; a mod manager's grid has to.
- **Quick subscribe** on a card's hover, where Steam shows a quick-look magnifier.
- **Banner art and copy are the app's own**; Steam's slogan and game art are not reused.
- **Theme is always dark.** Steam has no light theme.
- **Subscribed items keeps the upstream page's three views,** multi-select and groups; it gains the
  Steam title, previews on its cards and the Steam palette.

## Values that could not be measured (marked HUNCH in the source)

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

The app's self-updater still points at the upstream TCFModManager listing on sp-mod.com. When the
upstream author publishes a newer version, this build will offer it, and installing it replaces
this fork with the upstream app.
