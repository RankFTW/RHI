# Selectable layouts (planned for v2.9.0)

Ship two new main-page layouts alongside the current one, and let the user pick in Settings.
Planned 3 Nov 2026; nothing built yet.

- **Classic**: today's layout (7 collapsible, reorderable sections stacked in the detail panel).
- **Tabs** (mockup A): same sections, one at a time behind a tab strip; components as cards.
- **Spotlight** (mockup D): big game header with Steam artwork, a "What's installed" strip, "Tune this game" tiles.

Mockups (clickable): https://claude.ai/artifact/AP3uSweupyAqVZrV1GBZQz (private to RankFTW).
Source copy: `docs/mockups/layouts-2.9.0/` (`Main.dc.html` = A, `Spotlight.dc.html` = D; `Rail` = B and `Table` = C were the rejected options).

## Open decisions

1. Does A's tidier top bar apply to every layout, or only to Tabs? (Recommendation: every layout; it's independent of the page layout.)
2. Default for new installs: Classic? (Recommendation: yes.)

## Why it has to be built differently this time

RHI already removed two extra layouts: Grid View (Jul 2026, ~5,000 lines) and Simple View (v2.7.6, commit `4736159`).
Simple View reused the detail builders and only toggled section visibility (good, cheap), but it had its own page and
height logic that needed separate fixes, and every new feature had to be checked in it too.

Rules for the new layouts:

- **Everything inside a section stays shared.** Cog dialogs, install handlers, Game Overrides, DLSS, Driver, Neural Rendering,
  Extras and Management keep their current builders. A new feature in a section then works in every layout.
- **A layout only arranges the section containers** (and the header / component area). No per-layout copies of settings UI.
- **Build each section once, then show/hide.** Switching tabs must never rebuild a panel (UI freeze history: read
  "UI Freeze Investigation" in `.kiro/steering/rhi-knowledge.md` first).
- **C# only, no new XAML elements** (CLAUDE.md XAML constraint). The 7 section containers already exist in `MainWindow.xaml`.
- Reuse the old layout setting: `ViewLayout` still exists and `GameNameService` forces `ViewLayout.Detail` on load
  (`.claude/rules/rhi-ui.md`, Compact View section). Repurpose it or replace it with a new `MainLayout` setting.
- Keep rich UI as-is inside sections: summaries, sliders, multi-line status, and the compact two-per-row cog grids.

## Plan

1. **Layout host, Classic only, no visual change.** Put the section containers behind one host that owns their placement
   (today: `DetailPanel` StackPanel + `ApplySectionOrder()` + collapse handling). Classic becomes its first layout. Ship and test.
2. **Tabs (A).**
   - Tab strip built in C# above the sections; tab order = `DetailSectionOrder`.
   - Overview tab: component cards plus the existing section summaries (`MakeSectionSummaryInlines`) as cards that open their tab.
   - Component cards: type, name, version pill (green current, purple update, grey not installed), one main button plus Info / ⚙ / uninstall.
     Frame limiter card gets a ReLimiter / Display Commander switch.
   - Header: title + favourite star, quiet tags (store, engine, API, bitness, mod author), path with Open folder / Config folder,
     links row (Nexus, PCGW, UW fix), large Launch split button (dropdown: exe, arguments, desktop shortcut),
     HDR / RES as labelled toggles ("HDR on launch", "Resolution switch").
3. **Top bar** (if decision 1 = everywhere): Library / Available Mods (New tag) / Shaders & Addons as navigation; Refresh as an icon next to
   "Checked Xm ago"; Update All as the one prominent button ("All up to date" when nothing to do, purple with a count otherwise);
   Quick Start, Links, Help, Donate, Patch Notes, MOTD in one Help menu; Settings as an icon.
   Sidebar: All / Installed / Favourites / Hidden as one segmented control, engine/API/store chips behind a Filters button.
4. **Spotlight (D).** Needs game artwork (below). Optional cover-tile row; "Tune this game" tiles open the same section panels.
5. **Setting**: Settings › General › Layout: Classic / Tabs / Spotlight. Persist via `SaveNameMappings()` like other settings.

Ship and test each step on its own.

## Game artwork (for Spotlight, optionally Tabs/sidebar)

Any game that's also on Steam can use Steam artwork, whatever store it's owned on.

- **AppID**: Steam games have `card.SteamAppId`. Other stores: PCGW data (`database/pcgw_data.json`, `steam_appid`), already used by the
  store badge's Steam Community link (`DetailPanelBuilder.StoreBadge.cs`).
  Checked against RankFTW's library: Xbox 8/9, Epic 4/5, GOG 1/3 (Diablo isn't on Steam), EA 0/2. The EA misses are name differences
  ("Dragon Age Inquisition" vs "Dragon Age: Inquisition"); use the same normalised name matching RHI uses elsewhere, or a manifest entry.
- **Fixed CDN paths** (work for most games):
  `https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/header.jpg` (wide header), `library_hero.jpg` (banner),
  `library_600x900.jpg` (tall cover), `logo.png`.
  Tested: Gears of War: E-Day (3010850) and AI Limit (2407270): all four load.
- **Newer games use hashed paths**: Valor Mortis (2828710): `library_hero.jpg` loads but `header.jpg`, `library_600x900.jpg` and `logo.png` 404.
  Fallback: `https://store.steampowered.com/api/appdetails?appids={appid}&filters=basic` returns `header_image` / `capsule_image`
  (`https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appid}/{hash}/header.jpg?t=…`), which downloads fine.
  The store API is rate limited: call it only after a CDN 404, and cache the resolved URL.
- **Caching**: download once into `%LocalAppData%\RHI\` (e.g. `artwork\{appid}\`), never on every launch; respect `LifetimeToken`; use the shared DI `HttpClient`;
  no GitHub token on these hosts. Games not on Steam get a plain header.

## Related facts found while planning

- Three of RankFTW's favourites (Phantom Blade Zero, SILENT HILL: Townfall, The Blood of Dawnwalker) have a 6.8.0 Nightly ReShade DLL,
  the same situation as the Discord report of "6.8.0 Nightly" update badges (an old nightly installed on games now set to Stable).
- ReShade's update check compares file size only; the current stable and nightly 64-bit DLLs are both 5,592,064 bytes, so a nightly on a
  Stable game can go unflagged. A hash comparison (only when sizes match, cached by size + mtime) would fix it. Not done yet.
