# Selectable layouts (planned for v2.9.0)

Ship two new main-page layouts alongside the current one, and let the user pick in Settings.
Planned 3 Nov 2026; nothing built yet.

- **Classic**: today's layout (7 collapsible, reorderable sections stacked in the detail panel).
- **Tabs** (mockup A): same sections, one at a time behind a tab strip; components as cards.
- **Spotlight** (mockup D): big game header with Steam artwork, a "What's installed" strip, "Tune this game" tiles.

Mockups (clickable): https://claude.ai/artifact/AP3uSweupyAqVZrV1GBZQz (private to RankFTW).
Source copy: `docs/mockups/layouts-2.9.0/` (`Main.dc.html` = A, `Spotlight.dc.html` = D; `Rail` = B and `Table` = C were the rejected options).

## Decisions (3 Nov 2026)

1. A's tidier top bar applies to **every layout**.
2. New installs default to **Classic**.
3. Build **one step at a time** (steps below), user tests each before the next.
4. Steam artwork in **Spotlight only** (downloaded only while Spotlight is in use).

5. (3 Nov, later) **Tabs must end up looking and behaving exactly like mockup A** (`docs/mockups/layouts-2.9.0/Main.dc.html`):
   use its sizes, colours and spacing, not the app's current section look.
6. **Section tabs are redrawn like the mockup** (Overrides as grouped cards, DLSS / Driver as label-above-dropdown grids,
   Neural Rendering as method cards, Extras as a list, Management as tiles). The mockup's section contents were placeholders,
   so each redrawn tab must still hold **every** setting the section has today, arranged in the mockup's style. Classic keeps
   today's sections. Order: Management, Extras, Driver Settings, DLSS / Streamline, Neural Rendering, Game Overrides.
   Settings logic (load, save, apply) must be shared with Classic's builders, not copied; only the arrangement differs.

### Mockup A tokens (for the Tabs build)

- Page `#0B0F17`, panels/top bar/sidebar `#0E131D`, cards `#121824` with `#1F2737` border, radius 12, padding 16, gap 12.
- Text: primary `#E6EAF2`, secondary `#AEB7C7`, muted `#8D97AA`, control text `#C9D1DE`. Accent `#6FA8FF`. Links `#8DB8FF`.
- Section headings (Overview): 13 px, 600, `#8D97AA`, uppercase, letter spacing 0.08em.
- Tabs: 14 px, padding 10 0, gap 24, selected 600 `#E6EAF2` with 2 px accent underline, others `#AEB7C7`; strip bottom border `#1F2737`.
- Component card: caption 12 `#8D97AA`, name 18/600, min height 172, one description line 13 `#AEB7C7`; grid auto-fit min 260 px, gap 12.
  Card border purple `#4A3A7A` when an update is available.
- Pills (**6 px radius rectangles, not ovals**: user preference over the mockup, 3 Nov; 3 10 padding, 12/600): green `#13261C`/`#24563A`/`#7FE0A0`, purple `#2A2050`/`#4A3A7A`/`#D9C8FF`, grey `#151C29`/`#263042`/`#AEB7C7`.
- Buttons 38 high, radius 8: main blue `#141F33`/`#2A3D63`/`#A9C6FF` (600), update purple `#2A2050`/`#4A3A7A`/`#D9C8FF`,
  icon 38×38 `#141A26`/`#263042`/`#C9D1DE` (info circle, gear icons), uninstall `#1F1217`/`#4A2630`/`#FF9AA8` (×).
- Segmented switch: track `#0E131D` (sidebar `#121824`) border `#1F2737` radius 9 padding 3; on `#22304A` `#E6EAF2` 600, off transparent `#AEB7C7`, 30 high.
- Glance tile: padding 14 16, radius 12, title 14/600, summary 13 `#AEB7C7` with values `#7FE0A0`; auto-fit min 220 px.
- Inputs/dropdowns: 36 high, radius 8, `#0E131D` bg, `#263042` border, 13 px; label above 12 `#AEB7C7`, gap 6.

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
   *Done 3 Nov 2026*: `DetailLayoutHost.cs` (container map, order, collapse state); every section builder goes through it.
   Still Classic-specific and left in `DetailPanelBuilder`: the ≡ drag code and each section's header row (arrow, title, summary).
2. **Tabs (A).** Split into 2a (setting + tab strip, *done 3 Nov 2026*), 2b (Overview tab + component cards, *built 3 Nov 2026*), 2c (header).
   *Built 3 Nov 2026, waiting for the user's review*: header, Overview, all six section tabs redrawn, top bar, sidebar
   switch + Filters, footer colours. How it's put together: "Tabs layout" in `.claude/rules/rhi-ui.md`.
   Known differences from the mockup: the sidebar keeps the real store icons (the mockup's S / X letters were placeholders);
   Extras rows put the description under the name instead of beside it; the footer keeps today's layout (only colours changed);
   the top bar, sidebar and footer only change in Tabs for now (decision 1 says every layout: one switch in `ApplyTabsChrome`).
   2b decision: component cards for Tabs only (user's choice), built so they copy the hidden Components rows instead of
   repeating their rules (`DetailLayoutHost.Overview.cs`, `MainWindow.ComponentRows.cs`). No ReLimiter / Display Commander switch yet: both show as cards.
   - Tab strip built in C# above the sections; tab order is fixed (`DetailLayoutHost.FixedTabOrder`: Overview, Game Overrides, Extras, DLSS / Streamline, Driver Settings, Neural Rendering, Management), separate from Classic's drag order (user's choice, 3 Nov). (2a: `DetailLayoutHost`, Settings › General › Game page › Layout; the setting from step 5 was pulled forward so Tabs can be tested.)
   - Overview tab: component cards plus the existing section summaries (`MakeSectionSummaryInlines`) as cards that open their tab.
   - Component cards: type, name, version pill (green current, purple update, grey not installed), one main button plus Info / ⚙ / uninstall.
     Frame limiter card gets a ReLimiter / Display Commander switch.
   - Header: title + favourite star, quiet tags (store, engine, API, bitness, mod author), path with Open folder / Config folder,
     links row (Nexus, PCGW, UW fix), large Launch split button (dropdown: exe, arguments, desktop shortcut),
     HDR / RES as labelled toggles ("HDR on launch", "Resolution switch").
3. **Top bar** (if decision 1 = everywhere): Available Mods (New tag) / Shaders & Addons as navigation (no Library tab: both open over the game page, so it stayed lit behind them; removed 3 Nov at the user's request. The brand and the Settings gear, lit while Settings is open, lead back to the games); Refresh as an icon next to
   "Checked Xm ago"; Update All as the one prominent button ("All up to date" when nothing to do, purple with a count otherwise);
   Quick Start, Links, Help, Donate, Patch Notes, MOTD in one Help menu; Settings as an icon.
   Sidebar: All / Installed / Favourites / Hidden as one segmented control, engine/API/store chips behind a Filters button.
4. **Spotlight (D).** Needs game artwork (below). Mockup: `docs/mockups/layouts-2.9.0/Spotlight.dc.html`.
   Decisions (3 Nov 2026):
   - **Shelf = every game** as cover tiles (replaces the sidebar list), with the All / Installed / Favourites / Hidden switch
     and the Filters chips; the search box filters it too.
   - **Top bar = the Tabs top bar** (decision 1), not the mockup's pill bar.
   - **Tune tiles open a slide-in panel** from the right, over the page (close to return). The panel holds the section
     arranged as in Tabs (same `Arrange*` code, same shared builders).
   - **Hero holds everything the Tabs header has**: name, favourite star, Hide, tags, path + Open folder / Config folder,
     links, Launch split button (renamed Play in the mockup), HDR on launch, Resolution switch. Steam hero artwork behind it
     with a dark fade for legibility; plain dark banner for games not on Steam.
   - **What's installed**: status tiles (dot + name + version) copied from the hidden Components rows like the Overview cards;
     "Manage components" opens the Overview cards in the slide-in panel.
   - **Tune this game**: the At a glance tiles (live summaries), one per section.
   Build steps (user tests each): (a) artwork service; (b) Layout option + hero; (c) shelf replacing the sidebar;
   (d) What's installed + Tune tiles; (e) slide-in panel with the sections.
   Performance: covers decoded at tile size (`DecodePixelWidth`), loaded as they scroll into view, the shelf is never rebuilt
   on game selection (freeze history).
   *Built 3 Nov 2026 in one go (user's request), waiting for the user's review.* How it's put together: "Spotlight layout" in
   `.claude/rules/rhi-ui.md`. Artwork: `Services/SteamArtworkService.cs`; covers whose fixed CDN path 404s come from Steam's
   `IStoreBrowseService/GetItems` (`include_assets`: `library_capsule`, `library_hero`, `header`, all with hashed paths).
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
