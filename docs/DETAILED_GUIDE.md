# RHI — Detailed Guide

This document covers every feature in RHI. For a quick overview, see the [README](../README.md).

---

## Table of Contents

- [Layout and Views](#layout-and-views)
- [Settings Page](#settings-page)
- [Game Detection](#game-detection)
- [Graphics API Detection](#graphics-api-detection)
- [Components](#components)
- [ReShade](#reshade)
- [RenoDX](#renodx)
- [RE Framework](#re-framework)
- [Luma Framework](#luma-framework)
- [Frame Rate Limiters](#frame-rate-limiters)
- [OptiScaler](#optiscaler)
- [Shader Packs](#shader-packs)
- [ReShade Addon Management](#reshade-addon-management)
- [Per-Game Overrides](#per-game-overrides)
- [ReShade Presets](#reshade-presets)
- [Nexus Mods and PCGamingWiki Links](#nexus-mods-and-pcgamingwiki-links)
- [UW Fix and Ultra+ Links](#uw-fix-and-ultra-links)
- [Vulkan ReShade Support](#vulkan-reshade-support)
- [DXVK](#dxvk)
- [Foreign DLL Protection](#foreign-dll-protection)
- [UE-Extended and Native HDR](#ue-extended-and-native-hdr)
- [Drag-and-Drop](#drag-and-drop)
- [Addon Auto-Detection](#addon-auto-detection)
- [Update All](#update-all)
- [Auto-Update](#auto-update)
- [Remote Manifest](#remote-manifest)
- [Performance](#performance)
- [Data Storage](#data-storage)
- [Troubleshooting](#troubleshooting)
- [Third-Party Components](#third-party-components)

---

## Layout and Views

RHI has three view modes, a Settings page, and an About page. Your chosen view, window size, and window position are all remembered across restarts.

### Detail View

The default layout. A game list sidebar sits on the left, and a detail panel fills the right side. Selecting a game shows its full info card, component install buttons, and overrides panel.

### Grid View

A card-based layout showing all games as a grid. Each card displays the game name, platform icon, graphics API badge, status dots for installed components (RenoDX, ReShade, ReLimiter, Display Commander, OptiScaler), wiki status, and an update-available highlight. Click a card to open a management popout with install/uninstall controls and overrides — the same options available in Detail View.

### Compact View

A paged layout that shows the same content as Detail View, split across three navigable pages: Game Card, Overrides, and Management. Use the arrow buttons on the sides to cycle between pages. The window locks to a fixed size in Compact View and restores your previous size when you switch back.

### Toolbar

| Button | What it does |
|--------|-------------|
| Refresh | Re-scans your game library and fetches the latest mod info. After the first boot, this runs in the background without blanking the UI. |
| Update All | Updates ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, and RE Framework across all eligible games. Lights up purple when updates are available. |
| View toggle | Cycles between Detail, Grid, and Compact View. Shows the name of the current mode. |
| Help | Opens a flyout with links to the RHI support channel, this guide, the About page, and Ko-fi. |
| Settings | Opens the Settings page. |

### Sidebar (Detail View)

- **Search box** — filters games in real time as you type. Matches across game name, store, engine, graphics API, bitness, mod name, mod author, and more. You can also type "UW Fix" or "Ultra+" to filter to games with those links.
- **Filter chips** — All Games, Favourites, Installed, Unreal, Unity, Other, RenoDX, Luma, Hidden. Your selected filter is saved and restored on reopen.
- **Custom filter chips** — click the "+" button next to the search bar to save any search query as a named chip. Custom chips use a teal colour scheme. Right-click to delete.
- **Game/installed counts** — shows how many games are visible and how many have mods installed.
- **Game entries** — each row shows a platform icon, game name, and a green dot when updates are available.

### Detail Panel

When a game is selected, the detail panel shows:

- **Game name** above the info card, with mod author badge(s) on the right. Author badges link to Ko-fi donation pages where available.
- **Info card** — a bordered section with action buttons (Nexus Mods, PCGW, UW Fix, Ultra+ on the left; Hide, Favourite on the right) and badges for platform, engine, wiki status, graphics API, UE-Extended / Native HDR, and bitness.
- **Install path** in monospace text.
- **Components section** — install/update/uninstall buttons for each component, with per-addon Info buttons.
- **Overrides section** — all per-game settings inline.
- **Management section** — Change install folder, Reset folder, Reset Overrides, Copy Report.

### Status Bar

The bottom bar shows the game count and current operation on the left, a single-player warning in the centre, and the app version number with a Patch Notes link on the right.

### Loading

On startup, RHI shows a skeleton loading screen that matches your last-used view mode. On subsequent launches, the game list loads instantly from cache and the skeleton is replaced almost immediately. The full scan runs in the background and merges any changes (new games, updated statuses) into the already-visible list.

---

## Settings Page

Click **Settings** in the toolbar. Click **Back to Games** to return. The Settings button is disabled during initial load to prevent navigation issues.

| Section | What's in it |
|---------|-------------|
| Add Game | Manually add a game that wasn't auto-detected. Enter the name and pick the install folder. |
| Full Refresh | Clears all caches (API detection, shader, PCGW, etc.) and re-scans everything from disk. Bypasses the 4-hour update check cooldown. |
| Preferences | Skip Update Check on Launch, Verbose Logging, Shader Cache toggle, Custom Shaders toggle. |
| Screenshot Path | Set a global screenshot save path written to all managed reshade.ini files. Optional per-game subfolder toggle. Browse and Open buttons. |
| Hotkeys | ReShade UI Hotkey, ReShade Screenshot Key (default: Print Screen), ReLimiter OSD Hotkey, OptiScaler Overlay Hotkey (default: Insert). Each applies to all managed INI files. |
| Logs and Cache | Open Logs Folder, Open Downloads Cache, ReShade staging path. |
| OptiScaler Settings | GPU type (NVIDIA / AMD / Intel), DLSS input toggle (AMD/Intel only), overlay hotkey, Apply to All Games button, OptiScaler Compatibility List link. |
| Global Update Check Toggles | Disable update checks for individual components: RenoDX, ReShade, ReLimiter, Display Commander, OptiScaler. |
| Shared OSD Presets | Toggle for ReLimiter — when enabled, all games share the same OSD presets from a central file. "Apply to All Games" writes the setting to all deployed relimiter.ini files. |
| Mass INI Deployment | Deploy reshade.ini, relimiter.ini, DisplayCommander.ini, or OptiScaler.ini to all games with the corresponding component installed. |
| Mass ReShade Preset Install | Select presets from the presets folder, choose target games via a checkbox picker with Select All / Deselect All, and optionally install required shader packs. |
| RE Framework Exclusion | Globally exclude RE Framework from Update All. The per-game version of this toggle appears in the Update Inclusion dialog for RE Engine games. |
| Build Channels | Choose between Stable and Nightly builds for ReShade and DXVK. ReShade Stable uses reshade.me releases; Nightly uses the latest GitHub Actions build. DXVK Development uses nightly.link builds; Stable uses tagged releases. Switching channels clears the staging cache, downloads from the new source, and flags installed games for update. |

---

## Game Detection

RHI scans all supported stores on every launch and merges newly installed games into its cached library. Games on a disconnected drive are preserved in the cache until the drive is reconnected. Per-store detection failures are isolated — one store failing won't block others.

### Supported Stores

| Store | How RHI finds games |
|-------|-------------------|
| Steam | Reads `libraryfolders.vdf` and `appmanifest_*.acf` files across all library folders. |
| GOG | Registry keys under `HKLM\SOFTWARE\GOG.com\Games`. |
| Epic Games | Manifest `.item` files in `ProgramData\Epic\EpicGamesLauncher\Data\Manifests`. |
| EA App | `installerdata.xml` manifests, registry keys, default EA Games folders, EA Desktop local config. |
| Ubisoft Connect | Registry keys under `HKLM\SOFTWARE\Ubisoft\Launcher\Installs`, `settings.yml`, default games folder. |
| Xbox / Game Pass | Windows `PackageManager` API with `MicrosoftGame.config` detection. Falls back to `.GamingRoot` parsing, registry, and folder scanning. |
| Battle.net | Uninstall registry entries (Blizzard/Activision publisher), `Battle.net.config` default path, default folder scanning. |
| Rockstar Games | Uninstall registry entries (Rockstar publisher), launcher `titles.dat` paths, default folder scanning. |

### Engine Detection

RHI identifies game engines to determine which mods are compatible:

| Engine | How it's detected |
|--------|------------------|
| Unreal Engine | Unreal-specific files and folder structures. |
| Unreal (Legacy) | Unreal Engine 3 games identified by legacy folder layouts. |
| Unity | `UnityPlayer.dll`, `Mono` folder, `MonoBleedingEdge` folder, `il2cpp` folder, `GameAssembly.dll`. |
| RE Engine | `re_chunk_000.pak` in the game directory. |
| Custom | Engine names from the remote manifest (e.g. "Silk Engine", "Creation Engine", "Frostbite"). Displayed with a dedicated engine icon. |

### 32-bit / 64-bit Detection

RHI reads the PE header of the game executable to determine bitness. The remote manifest can override this with `thirtyTwoBitGames` and `sixtyFourBitGames` lists. A badge shows "32-bit" or "64-bit" on the detail panel.

### Adding Games Manually

- **Add Game** (Settings page) — enter the game name and pick the install folder.
- **Drag and drop** — drag a game's `.exe` onto the RHI window. RHI detects the engine, infers the game root folder, and guesses the name. A confirmation dialog lets you edit the name before adding. The new game is auto-selected in the sidebar.

### Multi-Platform Games

If a game is installed on multiple platforms (e.g. Steam and Xbox), both copies appear in the sidebar with their respective platform icons so you can manage mods for each install independently.

### DLC Collapsing

DLC and expansion packs that share the base game's install folder are automatically collapsed to the base game entry, keeping the sidebar clean.

---

## Graphics API Detection

RHI scans game executables using PE header import table analysis to detect which graphics APIs a game uses. Results are cached to disk so subsequent launches skip the scan entirely.

### Detected APIs

| API | Badge | What RHI looks for |
|-----|-------|--------------------|
| DirectX 8 | DX8 | PE import of `d3d8.dll` |
| DirectX 9 | DX9 | PE import of `d3d9.dll` |
| DirectX 10 | DX10 | PE import of `d3d10.dll` / `d3d10_1.dll` |
| DirectX 11 | DX11 | PE import of `d3d11.dll` |
| DirectX 12 | DX12 | PE import of `d3d12.dll` |
| Vulkan | VLK | PE import of `vulkan-1.dll` |
| OpenGL | OGL | PE import of `opengl32.dll` |

### Multi-Exe Scanning

All `.exe` files in the install directory and common subdirectories (`bin`, `binaries`, `x64`, `win64`, etc.) are scanned. This catches games like Baldur's Gate 3 that have multiple executables.

### Multi-API Display

Dual-API games show both APIs on the card (e.g. `DX11/12 / VLK`). Only valid combinations are displayed — DX11/12 + Vulkan is shown, but DX9 + DX12 is not.

### Automatic ReShade DLL Naming

The detected API drives the ReShade DLL filename:

1. **User DLL override** (per-game overrides) — always wins
2. **Manifest override** — per-game DLL name from the remote manifest
3. **API-based** — DX9 → `d3d9.dll`, OpenGL-only → `opengl32.dll`
4. **Default** — `dxgi.dll`

When a game imports both DX9 and DX11 (e.g. Assassin's Creed Unity), DX11/DX12 takes priority over legacy DX9.

### Manifest API Overrides

The remote manifest supports comma-separated API tags (e.g. `"DX12, VLK"`) for games like Red Dead Redemption 2 that load Vulkan dynamically and can't be detected via PE imports alone.

---

## Components

The detail panel shows a Components section with rows for each mod, separated by labelled dividers:

| Row | Component | Controls |
|-----|-----------|----------|
| ReShade | ReShade | Install / Reinstall / Update · Copy INI · Uninstall |
| RenoDX | RenoDX mod | Install / Reinstall / Update · Info · Uninstall |
| Luma | Luma Framework | Install / Uninstall (only shown in Luma mode) |
| — | *"Frame limiters — Choose one"* | |
| ReLimiter | ReLimiter | Install / Reinstall / Update · Info · Copy INI · Uninstall |
| Display Commander | Display Commander | Install / Reinstall / Update · Info · Copy INI · Uninstall |
| — | *"── Optional ──"* | |
| OptiScaler | OptiScaler | Install / Reinstall / Update · Info · Copy INI · Uninstall |

### Per-Addon Info Buttons

Every component row has an **Info** button that opens a dialog with context about that addon for the selected game. Content follows a three-tier priority:

1. **Manifest notes** — game-specific notes from the remote manifest.
2. **Wiki content** — compatibility data from the relevant wiki (OptiScaler wiki, HDR Gaming Database, RenoDX wiki).
3. **Generic description** — a general description of what the addon does.

**ReLimiter and Display Commander** Info buttons fetch the project's `CHANGELOG.md` from GitHub and display the patch notes for the installed version plus the two previous versions, rendered as markdown.

Buttons with content are highlighted in **blue**. Buttons with only a generic description use a muted style. An arrow indicator (◄) appears on the install button when the Info button has content available.

### Version Display

Each component row shows the installed version number (e.g. `6.7.3`) instead of just "Installed". When an update is available, the text turns purple. After updating, it shows the new version in green.

### Mod Author Badges

Named mods from the RenoDX wiki display the mod author as a bordered badge. Multiple authors each get their own badge. Author badges are clickable links to Ko-fi donation pages where available. Games in Luma mode show the Luma mod author instead. Author display names can be overridden via the remote manifest.

### Dependency Enforcement

- **ReShade required** — RenoDX, ReLimiter, and Display Commander require ReShade to be installed first. Their install buttons show "⚠ ReShade required" and are greyed out until ReShade is in place.
- **RE Framework required** — RE Engine games require RE Framework before ReShade can be installed. The ReShade button shows "⚠ RE Framework required" until RE Framework is present.

---

## ReShade

[ReShade](https://reshade.me) is the core injection framework that all other components build on. RHI downloads the latest ReShade build on startup and stages it locally.

### Install / Update / Uninstall

Click the Install button on the ReShade row. RHI places the ReShade DLL in the game folder using the correct filename (see [Automatic ReShade DLL Naming](#automatic-reshade-dll-naming)). Uninstalling removes the DLL, and if addons were deployed, those are removed too.

### ReShade Without Addon Support

A per-game toggle in the overrides panel switches from addon-enabled ReShade to standard ReShade. When enabled:

- All addons (RenoDX, ReLimiter, Display Commander, managed addon packs) are removed from the game folder.
- Addon rows are dimmed and disabled.
- The addon override toggle is locked off.

Toggle back to restore addon ReShade and re-deploy addons. The setting persists per-game across restarts.

### ReShade Detection Under Non-Standard Filenames

If ReShade was installed using a non-standard DLL name (e.g. `d3d11.dll`, `dinput8.dll`, `version.dll`), RHI detects it via binary signature scanning. The scan matches on `reshade.me` or `crosire` strings unique to the actual ReShade binary, and rejects files over 15 MB.

### Copy INI

The Copy INI button (📋) on the ReShade row copies `reshade.ini` from the AppData INI folder to the game directory. If the game already has a `reshade.ini`, existing game-specific settings (addon configs, effect toggles, custom keybinds) are preserved — only template keys are overwritten.

---

## RenoDX

[RenoDX](https://github.com/clshortfuse/renodx) is an HDR mod framework that runs as a ReShade addon. RHI downloads game-specific RenoDX mods from the RenoDX wiki and installs them as `.addon64` or `.addon32` files.

### How Mods Are Matched

RHI fetches the RenoDX wiki mods list on startup and matches detected games by name. Games that don't have a named mod on the wiki fall back to generic engine addons (Unreal Engine, Unity, UE-Extended). The remote manifest can override wiki name matching for games with unusual names.

### HDR Gaming Database Links

The RenoDX Info button links to the [HDR Gaming Database](https://www.hdrmods.com) when a game has an HDR analysis entry, giving you quick access to detailed HDR breakdowns.

### External-Only Games

Some games are redirected to external download sources (Discord, Nexus Mods) via the remote manifest `forceExternalOnly` field. These show a badge indicating where to get the mod, and the install button opens the external link.

---

## RE Framework

[RE Framework](https://github.com/praydog/REFramework-nightly) by praydog is required for ReShade injection on RE Engine games (Monster Hunter Wilds, Resident Evil series, Devil May Cry 5, Street Fighter 6, Dragon's Dogma 2, etc.).

### Install / Update / Uninstall

One-click install from the detail panel. Each RE Engine game downloads its own game-specific build. The DLL is cached per game so reinstalls are instant. Version tracking and auto-update checking are included — RE Framework is part of the Update All batch.

### PD-Upscaler REFramework

When OptiScaler is installed on Resident Evil 2, 3, 4, 7, or Village, RHI automatically downloads and installs the pd-upscaler branch of REFramework required for OptiScaler compatibility. The standard REFramework is backed up and restored when OptiScaler is uninstalled. The version display updates to show "PD-Upscaler" while OptiScaler is active.

### RE Framework Exclusion

RE Framework can be excluded from Update All both per-game (via the Update Inclusion dialog) and globally (via the Settings page toggle). The RE Framework checkbox only appears for RE Engine games.

---

## Luma Framework

[Luma Framework](https://github.com/Filoppi/Luma-Framework) by Pumbo (Filoppi) is a DX11 modding framework that adds HDR support via the ReShade addon system.

### How Luma Mode Works

When a game supports Luma, a toggle appears in the Components header. Enabling Luma mode:

- Hides RenoDX and standard ReShade. Only the Install Luma button is available.
- Installing Luma deploys the mod zip, `reshade.ini`, and Lilium HDR shaders.
- Uninstalling or toggling off removes all Luma files.
- ReLimiter and Display Commander remain available in Luma mode.

Games listed in the remote manifest `lumaDefaultGames` automatically start in Luma mode on first detection.

### Luma Update Detection

Luma mods check for updates automatically. When a newer Luma-Framework build is released, installed Luma games show an update badge. The installed build number is displayed in the component status (e.g. "Build 428"). Luma is included in Update All.

### Luma Notes

Luma-specific notes are sourced from both the Luma wiki and the remote manifest. These appear in the Luma Info button and provide game-specific setup instructions.

### Trusted Downloads

Luma downloads are restricted to trusted GitHub URLs under `https://github.com/Filoppi/` to prevent arbitrary file downloads.

---

## Frame Rate Limiters

RHI supports two frame rate limiters: [ReLimiter](https://github.com/RankFTW/ReLimiter) and [Display Commander](https://github.com/pmnoxx/display-commander). They are mutually exclusive per game — only one can be installed at a time. When one is installed, the other's row is visually dimmed with a greyed-out install button.

### ReLimiter

An optional per-game frame pacing addon downloaded from GitHub.

- **Bitness** — RHI automatically selects `relimiter.addon64` or `relimiter.addon32` based on the game. ReLimiter v3.0.0+ is 64-bit only; 32-bit games show the row with strikethrough.
- **INI** — a default `relimiter.ini` is seeded to the AppData INI folder on first launch. Installing ReLimiter for the first time on a game auto-copies this INI to the game directory. If the INI already exists, it's left untouched.
- **OSD Hotkey** — configure the ReLimiter overlay hotkey from the Settings page. Applies to all managed `relimiter.ini` files.
- **Shared OSD Presets** — when enabled in Settings, all games share the same OSD presets from a central file instead of each game having its own.
- **Updates** — detected by comparing the cached file against the remote release using file size and SHA-256 hash.

### Display Commander

An alternative frame rate limiter, also downloaded from GitHub.

- **Bitness** — supports both 32-bit and 64-bit games.
- **DLL Naming** — a single "DLL naming overrides" toggle controls both the ReShade and DC filenames together. Each dropdown filters out the other component's current filename to prevent conflicts. Both dropdowns are editable — you can type a custom name and press Enter.
- **INI** — a default `DisplayCommander.ini` is seeded on first launch. First-time installs auto-copy it to the game folder.
- **Updates** — checked on startup alongside other components. Included in Update All.

---

## OptiScaler

[OptiScaler](https://github.com/optiscaler/OptiScaler) is a 64-bit middleware DLL that intercepts upscaler calls (DLSS, FSR, XeSS) and redirects them to alternative backends. RHI manages the full lifecycle: download, staging, install, uninstall, update detection, INI configuration, ReShade coexistence, DLL naming, and hotkey setup.

OptiScaler appears under an "── Optional ──" separator in the detail panel. It's available for all games but only enabled for 64-bit titles. 32-bit games show the row greyed out with strikethrough.

### Install / Update / Uninstall

One-click install. On first install, a warning dialog explains OptiScaler's purpose. The install copies:

- `OptiScaler.dll` (renamed to the effective DLL name)
- `OptiScaler.ini`
- Companion files (`fakenvapi.dll`, `dlssg_to_fsr3.dll`, FidelityFX SDK DLLs)
- DLSS DLLs (`nvngx_dlss.dll`, `nvngx_dlssd.dll`, `nvngx_dlssg.dll`)
- OptiPatcher ASI plugin (AMD/Intel GPUs only, deployed to the `plugins` folder)

Game-owned files are backed up to `.original` before overwriting and restored on uninstall. Only genuine game files are backed up — OptiScaler's own companion files are not.

### DLSS Auto-Download

The latest NVIDIA DLSS Super Resolution, Ray Reconstruction, and Frame Generation DLLs are automatically downloaded and staged on startup, sourced from the DLSS Swapper manifest. Each DLL has independent version tracking and auto-updates.

### OptiPatcher

For AMD/Intel GPU users, OptiPatcher is automatically downloaded and deployed during OptiScaler install. It enables DLSS/DLSSG inputs without GPU spoofing. Version-tracked and cleaned up on OptiScaler uninstall.

### ReShade Coexistence

When OptiScaler is installed alongside ReShade, RHI automatically renames the ReShade DLL to `ReShade64.dll`. OptiScaler loads ReShade from this filename via `LoadReshade=true`.

- Installing OptiScaler when ReShade is present → ReShade renamed to `ReShade64.dll`
- Installing ReShade when OptiScaler is present → ReShade deployed as `ReShade64.dll`
- Uninstalling OptiScaler → ReShade restored to its correct filename
- While OptiScaler is installed, the ReShade DLL naming override dropdown is disabled

### DLL Naming

OptiScaler.dll is renamed to a proxy DLL filename when deployed. The name is resolved using:

1. **User DLL override** (per-game overrides) — always wins
2. **Manifest override** — per-game from the remote manifest
3. **Vulkan auto-detection** — Vulkan games automatically use `winmm.dll`
4. **Default** — `dxgi.dll`

Supported names: `dxgi.dll`, `winmm.dll`, `d3d12.dll`, `dbghelp.dll`, `version.dll`, `wininet.dll`, `winhttp.dll`.

### INI Configuration

RHI seeds a default `OptiScaler.ini` to the AppData INI folder. `LoadReshade=true` and `LoadAsiPlugins=true` are always enforced. Pre-generated INI templates are bundled for each GPU configuration (NVIDIA, AMD/Intel with DLSS, AMD/Intel without DLSS).

### OptiScaler Settings (Settings Page)

- **GPU type** — NVIDIA, AMD, or Intel. Determines which INI template is used and whether OptiPatcher is deployed.
- **DLSS input toggle** — AMD/Intel only. Controls whether DLSS inputs are replaced.
- **Overlay hotkey** — default: Insert. Written as Windows Virtual Key Code hex values.
- **Apply to All Games** — writes the current settings to all game folders where OptiScaler is installed.

### OptiScaler Wiki Integration

The OptiScaler Info button shows compatibility data from the OptiScaler wiki:

- Working status (Working, Partially Working, Not Working)
- Supported upscalers
- Notes and known issues
- Direct link to the game's wiki page

Both the standard and FSR4 compatibility lists are included.

### Per-Game Update Exclusion

A toggle in the overrides panel lets you pin a specific OptiScaler version on certain games, excluding them from Update All.

---

## Shader Packs

RHI downloads and maintains 41 ReShade shader packs, merged into a shared staging folder and deployed per-game.

### Categories

- **Essential** — Lilium HDR Shaders, required for HDR tone mapping. Selected by default on fresh installs.
- **Recommended** — Core HDR and post-processing packs: crosire reshade-shaders, PumboAutoHDR, smolbbsoop shaders, MaxG2D Simple HDR Shaders, clshortfuse ReShade shaders, and potatoFX.
- **Extra** — Community packs covering cinematic colour grading, film emulation, VR tools, CRT simulation, retro filters, screen-space reflections, global illumination, artistic effects, and more. Includes packs from SweetFX, OtisFX, Depth3D, qUINT, iMMERSE, METEOR, ZenteonFX, GShade-Shaders, CShade, prod80, CobraFX, and others.

### Shader Cache

The "Shader Cache" toggle on the Settings page controls whether shader packs are bulk-downloaded on startup. When disabled, packs are fetched only when needed — when you select them in the picker, install ReShade, or deploy a preset. The shader selection dialog shows a green ✓ next to each pack that's already cached locally. Existing cached shaders are never deleted by the app.

Shader packs from GitHub Releases (Lilium, PumboAutoHDR) skip the GitHub API call on startup if the files are already cached and extracted.

### Per-Game Shader Overrides

Each game can use a different subset of shader packs:

- **Global** — uses the global shader selection.
- **Select** — opens a picker to choose specific packs for that game.
- **Custom** — uses shaders from your custom shader directories.

### Shader Pack Dependencies

Packs can declare dependencies. Selecting Azen in the picker automatically selects smolbbsoop shaders. The dependency is one-way — deselecting the required pack independently is still allowed.

### Custom Shaders

Place custom shaders in `%LOCALAPPDATA%\RHI\reshade\Custom\Shaders\` and textures in `%LOCALAPPDATA%\RHI\reshade\Custom\Textures\`. Enable the Custom Shaders toggle in Settings to include them in deployments.

### Deploy Destinations

| Scenario | Where shaders go |
|----------|-----------------|
| DLL ReShade | `<game folder>\reshade-shaders\Shaders\` and `\Textures\` |
| Vulkan ReShade | Same path, requires `RDXC_VULKAN_FOOTPRINT` marker file |

User-owned shader folders are preserved by renaming to `reshade-shaders-original` before deployment and restored when ReShade is uninstalled.

### Startup Deployment

On launch, shaders are automatically synced to all installed game folders in parallel. Games with ReShade installed will have the correct global or per-game shaders deployed automatically.

---

## ReShade Addon Management

RHI includes a curated addon manager for browsing, downloading, and toggling ReShade addons from the official Addons.ini list.

### Addon Manager

Click "ReShade Addons" in the toolbar to open the manager. A one-time warning explains that addons are advanced features. Each addon has a toggle:

- **On** — downloads the addon (if not cached) and enables it globally. Deployed to all games with ReShade installed.
- **Off** — disables the addon globally. Files stay cached for later use.

### Global Deployment

Enabled addons are automatically deployed when ReShade is installed on a game, removed when ReShade is uninstalled, and synced on every Refresh. The correct bitness variant (`.addon32` or `.addon64`) is selected based on the game.

### Per-Game Addon Overrides

Each game's override panel has an Addons section with a Global toggle:

- **Global on** — the game uses the globally enabled addon set.
- **Global off** — opens a per-game addon picker. Toggle individual addons for that game only.

### Special Addons

- **RenoDX DevKit** — always available in the manager alongside official addons, with 32-bit and 64-bit variants.
- **DLSS Fix** — makes ReShade draw on native game frames instead of frame gen frames, and hides DLSS upscaling from ReShade. 64-bit only.

### Auto-Update

On startup, RHI checks all downloaded addons for updates using GitHub release tags or HTTP ETags. Newer versions are downloaded automatically.

---

## Per-Game Overrides

The Overrides section appears below Components in the detail panel. All controls save immediately when changed.

| Override | What it does |
|----------|-------------|
| Game name (editable) | Rename the game. Persists across Refresh and restarts. |
| Wiki mod name | Match to a different wiki entry (also applies to Luma matching). |
| Wiki exclusion | Exclude the game from wiki lookups entirely. |
| DLL naming overrides | A single toggle controls both ReShade and Display Commander filenames. Turning ON enables both dropdowns with safe defaults; turning OFF reverts both. Each dropdown is editable (type a custom name, press Enter). |
| Update Inclusion | Button opens a dialog with checkboxes for ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, and RE Framework (RE Engine games only). A colour-coded summary line shows the current state. |
| Shader Mode | Global / Select / Custom. |
| Addon Mode | Global / Select. |
| Rendering Path | For dual-API games — switches between DirectX and Vulkan ReShade. |
| Bitness override | Auto, 32-bit, or 64-bit. Overrides PE header detection. |
| Graphics API override | Auto, DirectX8, DirectX9, DirectX10, DX11/DX12, Vulkan, OpenGL. |
| ReShade Without Addon Support | Toggle to switch from addon-enabled to standard ReShade. |
| OptiScaler DLL name | Override the OptiScaler proxy DLL filename. |
| Select ReShade Preset | Deploy preset files from the presets folder. |
| Copy Report | Generate a diagnostic code for Discord or GitHub issues. |
| Change install folder | Pick a different install folder. |
| Reset folder / Remove game | Reset the install folder or remove a manually added game. |
| Reset Overrides | Reset all override settings back to defaults. |

---

## ReShade Presets

### Preset Folder

Place `.ini` preset files in `%LOCALAPPDATA%\RHI\inis\reshade-presets\`. The "Select ReShade Preset" button in the overrides panel lists all files in this folder with checkboxes. Click "Deploy" to copy selected presets to the game folder.

### Drag-and-Drop Preset Install

Drag a ReShade preset `.ini` onto the RHI window. RHI validates it as a genuine preset, saves it to the presets folder, lets you pick a target game, and copies it to the game directory. After deploying, RHI offers to automatically install the required shader packs by parsing the `Techniques=` line and matching `.fx` files against known packs.

### Auto-Deploy Preset

Place a `ReShadePreset.ini` in the AppData INI folder and it will be copied to every new game install automatically alongside `reshade.ini`.

### Mass Preset Install

From the Settings page, select presets, choose target games via a checkbox picker, and optionally install required shader packs — all in one flow.

---

## Nexus Mods and PCGamingWiki Links

Each game can show clickable Nexus Mods and PCGamingWiki (PCGW) buttons in the detail panel info card.

### Nexus Mods

RHI fetches the public Nexus Mods game catalogue on startup, caches it for 24 hours, and matches games by normalized name. Games that can't be matched automatically can be overridden in the remote manifest.

### PCGamingWiki

PCGW links are resolved via Steam AppID through a priority chain:

1. Manifest `steamAppIdOverrides`
2. Cached AppID from previous resolution
3. Steam AppID parsed from ACF filename during detection
4. `steam_appid.txt` in the game directory
5. Steam Store search API (rate-limited)

For games without a Steam AppID, an OpenSearch fallback queries the PCGW wiki directly. PCGW lookups have a 5-second timeout and automatically disable for the session after the first failure.

---

## UW Fix and Ultra+ Links

### UW Fix (Ultrawide Fix)

If a game has an ultrawide/resolution fix available, a "UW Fix" button appears on the game card next to the Nexus and PCGW buttons. Clicking it opens the fix page directly.

RHI checks three sources for ultrawide fixes, with tiered priority:

1. [Lyall](https://github.com/Lyall) — game-specific fix repositories
2. [RoseTheFlower](https://github.com/RoseTheFlower) — ultrawide fix collection
3. [p1xel8ted](https://github.com/p1xel8ted) — ultrawide fix collection

All three sources are fetched once and cached for 24 hours. The remote manifest includes overrides for edge cases where automatic name matching fails.

### Ultra+

If a game has an Ultra+ mod on [theultraplace.com](https://theultraplace.com), an "Ultra+" button appears on the game card. Clicking it opens the Ultra+ page for that game.

### Searching

Typing "UW Fix" or "Ultra+" in the search bar filters to games that have those links, just like searching for engine names or authors.

### Visual Style

Nexus, PCGW, UW Fix, and Ultra+ link buttons are underlined with a hand cursor on hover.

---

## Vulkan ReShade Support

RHI provides full Vulkan implicit layer support for ReShade, enabling injection for Vulkan-rendered games without per-game DLL placement.

### How It Works

1. **Global Vulkan layer** — RHI installs ReShade as a Vulkan implicit layer via the Windows registry (`HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers`), making ReShade available to all Vulkan games system-wide.
2. **Layer manifest** — a `ReShade64.json` manifest is deployed alongside the ReShade DLL to `C:\ProgramData\ReShade\`.
3. **Per-game INI** — a `reshade.vulkan.ini` with Vulkan-tuned depth buffer settings is deployed to each game folder.
4. **Footprint file** — an `RDXC_VULKAN_FOOTPRINT` marker file is placed in the game folder to enable managed shader deployment.

### Lightweight Install

When the global Vulkan layer is already registered, clicking Install on a Vulkan game performs a fast lightweight deploy (INI + footprint + shaders only) without requiring admin privileges.

### Dual-API Games

Games with both DirectX and Vulkan show a rendering path toggle. Switching from DirectX to Vulkan uninstalls DX ReShade, removes `reshade.ini` and managed shaders, and restores `reshade-shaders-original` if present.

### Per-Game Uninstall

Uninstalling removes `reshade.ini`, the footprint file, and managed shaders from the game folder. The global Vulkan layer is not affected.

---

## DXVK

DXVK is a DirectX-to-Vulkan translation layer that can improve performance and enable ReShade compute shaders on older DX8/DX9/DX10 games. RHI manages DXVK as a per-game component with the same lifecycle as other managed components.

**This feature is a work in progress and has only been tested by the developer.**

### Enabling DXVK

The DXVK toggle appears in the Overrides panel for DX8, DX9, and DX10 games. It is hidden for DX11, DX12, OpenGL, and Vulkan games. Games with DX12 detected alongside their primary API (dual-API) also hide the toggle, as do Unreal Engine DX11 games that likely support DX12.

Toggling DXVK on triggers the install flow (with a warning dialog). Toggling off uninstalls DXVK and restores the original game DLLs.

### What happens on install

1. The required DXVK DLLs (e.g. `d3d9.dll`, `d3d11.dll`, `dxgi.dll`) are copied from staging to the game folder based on the game's API and bitness.
2. Game-original DLLs are backed up with a `.original` extension and restored on uninstall.
3. A default `dxvk.conf` is deployed (HDR enabled, borderless fullscreen, latency sleep).
4. If ReShade is installed as a DX proxy DLL, it is switched to Vulkan layer mode automatically. When DXVK is disabled, ReShade switches back with the correct API-specific filename.
5. OptiScaler coexistence is handled — filename conflicts are resolved by routing DLLs to the OptiScaler plugins folder.

### Build variants

Choose between Development (nightly builds via nightly.link) and Stable (tagged releases) in the Build Channels section on the Settings page.

### Anti-cheat blacklist

Games with known anti-cheat (Fortnite, Apex Legends, Valorant, etc.) are blacklisted via the remote manifest. The DXVK toggle is disabled for these games.

---

## Foreign DLL Protection

When installing ReShade, RHI checks whether an existing DLL at the target filename belongs to another tool (DXVK, Special K, ENB, etc.) using binary signature scanning.

- If the file is identified as ReShade, the install proceeds normally.
- If the file is unidentified, a confirmation dialog asks whether to overwrite.
- During Update All, foreign files are silently skipped to avoid accidentally replacing third-party DLLs.

The detection also recognises OptiScaler DLLs and does not flag them as foreign.

---

## UE-Extended and Native HDR

Unreal Engine games with native HDR support are automatically assigned UE-Extended via the remote manifest. These display "Extended UE Native HDR" as their engine badge.

### How to Use

In-game HDR must be turned on for UE-Extended to work. The RenoDX Info button shows a message explaining this for native HDR games.

### UE-Extended Toggle

The toggle appears for every Unreal Engine game that does not have a named mod on the RenoDX wiki. A compatibility warning pops up when enabling it, advising that not all games are compatible.

### Manifest Lists

- `nativeHdrGames` — games flagged for native HDR support.
- `ueExtendedGames` — games marked for the UE-Extended addon.

Both lists are maintained in the remote manifest and can be updated without an app release.

---

## Drag-and-Drop

RHI supports drag-and-drop for adding games and installing mods. Works even when running as administrator (UIPI bypass).

### Supported File Types

| File type | What happens |
|-----------|-------------|
| Game `.exe` | Opens an add-game dialog with auto-detected engine, inferred game root, and suggested name. |
| `.addon64` / `.addon32` | Opens an install dialog with a game picker. Auto-selects based on filename, falls back to the currently selected game. |
| `.zip`, `.7z`, `.rar`, `.tar`, `.gz`, `.bz2`, `.xz`, `.tgz` | Extracted using bundled 7-Zip. Addon files inside are found and offered for install. |
| ReShade preset `.ini` | Validated, saved to the presets folder, deployed to a chosen game, with optional auto shader install. |
| URL (`.url` shortcut) | Parsed and processed as an addon download URL. |

Only recognised extensions are accepted: `.exe`, `.addon64`, `.addon32`, `.ini`, `.zip`, `.7z`, `.rar`, `.tar`, `.gz`, `.bz2`, `.xz`, `.tgz`. Unrecognised files are silently skipped.

---

## Addon Auto-Detection

RHI watches your Downloads folder for new addon files and archives.

### Downloads Folder Watching

The default watch folder is the system Downloads directory (configurable in Settings). RHI monitors for new `renodx-*.addon64` and `renodx-*.addon32` files and prompts you to install them.

### Archive Auto-Install

The watch folder also detects `.zip`, `.7z`, and `.rar` archives containing "renodx" in the filename. When a matching archive appears (e.g. from a Nexus Mods download), RHI automatically extracts it, finds the addon files inside, and starts the install flow.

### Named Pipe Forwarding

Double-clicking an addon file in Explorer opens RHI and triggers the install flow. If RHI is already running, the file path is forwarded to the existing instance via a named pipe.

### AddonPath Support

Addon installs (RenoDX and ReLimiter) respect the `AddonPath` setting in `reshade.ini`. If the `[ADDON]` section contains an `AddonPath=` line, addons are deployed to that folder instead of the game root. Uninstall, update detection, and addon scanning all check the same resolved path.

---

## Update All

The **Update All** button updates ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, and RE Framework across all eligible games in one click.

### Per-Component Toggles

Each component respects its own per-game inclusion toggle. A game excluded from ReShade updates can still receive RenoDX updates. Set these in the Update Inclusion dialog in per-game overrides.

### Foreign DLL Skipping

Games with foreign DLLs (non-ReShade files detected via binary scanning) are silently skipped during Update All.

### Update Check Cooldown

Update checks have a 4-hour cooldown. Launching the app multiple times no longer hammers the GitHub API — checks are skipped if the last successful check was recent. Full Refresh bypasses the cooldown.

### Rate Limit Handling

GitHub API rate limiting (403 responses) is detected and handled gracefully. If a 403 is received, all remaining API calls for the session are skipped instead of each one failing independently.

---

## Auto-Update

RHI checks for new versions on launch by querying the GitHub Releases API. Disable via Settings → Preferences → Skip update check on launch.

### Stable and Beta Channels

When Beta Opt-In is enabled, RHI checks both stable and beta releases. Stable always wins over beta at the same or higher base version. Beta is only offered when its version exceeds the latest stable, or when you're already on a beta and a newer one is available.

---

## Remote Manifest

RHI fetches a remote manifest from GitHub on every launch, providing game-specific overrides without app updates. The manifest is cached locally for offline use.

### Key Fields

| Field | What it does |
|-------|-------------|
| `blacklist` | Excluded non-game apps (launchers, DLC, tools). |
| `installPathOverrides` | Correct wrong install paths (e.g. `"Cyberpunk 2077": "bin\\x64"`). |
| `wikiNameOverrides` | Map detected game name to wiki mod name. |
| `wikiStatusOverrides` | Force a specific wiki status icon. |
| `wikiUnlinks` | Ignore false fuzzy wiki matches. |
| `gameNotes` | Game-specific notes with optional URL and label, shown in Info buttons. |
| `lumaGameNotes` | Luma-specific notes shown in Luma mode. |
| `nativeHdrGames` | Auto-assign UE-Extended for Unreal games with native HDR. |
| `ueExtendedGames` | Mark games for the UE-Extended addon. |
| `thirtyTwoBitGames` / `sixtyFourBitGames` | Override auto-detected bitness. |
| `engineOverrides` | Force a specific engine label. |
| `dllNameOverrides` | Set ReShade/DC install filename per game. |
| `graphicsApiOverrides` | Comma-separated API tags for games that can't be detected via PE imports. |
| `snapshotOverrides` | Direct addon download URL when wiki lacks one. |
| `lumaDefaultGames` | Games that auto-start in Luma mode. |
| `forceExternalOnly` | Redirect install to an external URL (Discord, Nexus). |
| `dcModeOverrides` | Override the default DC mode for specific games. |
| `donationUrls` | Ko-fi links for mod authors. |
| `authorDisplayNames` | Override wiki maintainer handles with display names. |
| `authorOverrides` | Set mod authors via manifest for games not on the wiki. |
| `optiScalerWikiNames` | Map game names to OptiScaler wiki equivalents. |
| `nexusUrlOverrides` | Manual Nexus Mods URL overrides. |
| `steamAppIdOverrides` | Manual Steam AppID overrides for PCGW resolution. |
| `pcgwUrlOverrides` | Manual PCGW URL overrides. |

---

## Performance

RHI includes several optimisations for fast startup and refresh:

- **Instant launch from cache** — on subsequent launches, the game list loads from cache and displays immediately. The full scan runs in the background and merges changes.
- **Parallel shader pack checks** — shader packs are verified in parallel, not sequentially.
- **Parallel game folder shader syncs** — shader deployments to game folders run in parallel.
- **Parallel card building** — game cards are constructed using parallel processing.
- **PE-level API cache** — graphics API detection results are cached to disk. Subsequent launches skip PE header scanning.
- **Game-level API cache** — full API detection results cached per install path.
- **WindowsApps skip** — `\WindowsApps\` paths are skipped for API detection, addon scanning, OptiScaler detection, and ReShade proxy scanning (always access-denied).
- **Debounced PCGW cache writes** — concurrent writes during startup are collapsed into a single disk write.
- **Optimised OptiScaler detection** — scans only the 7 known proxy DLL names instead of every DLL.
- **DLC blacklisting** — DLC content packs and launcher components are excluded from game detection.
- **Shader pack API optimisation** — shader packs from GitHub Releases skip the API call when cached files are already present.
- **4-hour update check cooldown** — avoids redundant GitHub API calls on repeated launches.
- **GitHub rate-limit detection** — a single 403 response cancels all remaining API calls for the session.
- **PCGW timeout** — 5-second timeout with automatic session-wide disable after the first failure.
- **Full Refresh** — clears all caches and rescans everything fresh.

---

## Data Storage

Everything is stored under `%LOCALAPPDATA%\RHI\`:

| Path | Contents |
|------|----------|
| `game_library.json` | Detected games, hidden list, manually added games. |
| `installed.json` | RenoDX mod install records. |
| `aux_installed.json` | ReShade, ReLimiter, Display Commander, and OptiScaler install records. |
| `settings.json` | All settings, per-game overrides, and persisted filter mode. |
| `ul_meta.json` | ReLimiter version metadata (per-bitness). |
| `dc_meta.json` | Display Commander version metadata. |
| `api_cache.json` | PE-level graphics API detection cache. |
| `game_api_cache.json` | Game-level API detection cache. |
| `downloads\` | Cached downloads organised into subdirectories: `shaders/`, `renodx/`, `framelimiter/`, `luma/`, `misc/`. |
| `optiscaler\` | Staged OptiScaler release (DLL, companion files, INI, version tag). |
| `optipatcher\` | Staged OptiPatcher release. |
| `dlss\` | Staged DLSS DLLs with independent version files. |
| `addons\` | Downloaded ReShade addon files and `versions.json`. |
| `addons_cache.ini` | Cached Addons.ini for offline fallback. |
| `inis\` | Preset config files (`reshade.ini`, `reshade.vulkan.ini`, `relimiter.ini`, `DisplayCommander.ini`, `OptiScaler.ini`, etc.) and `reshade-presets\` subfolder. |
| `reshade\` | Staged shader packs and custom shaders. |
| `logs\` | Session logs (timestamped) and crash reports. Max 10 logs kept on disk. |
| `reports\` | Saved game reports from Copy Report. |
| `nexus_games.json` | Cached Nexus Mods game catalogue (24-hour TTL). |
| `steam_appid_cache.json` | Cached Steam AppID lookups (permanent). |

### Session Logging

A new log file is created every time RHI starts, named with a timestamp (e.g. `session_2025-03-14_12-30-00.txt`). The Verbose Logging toggle in Settings enables additional detail. Old logs are pruned to keep a maximum of 10 on disk.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Game not detected | Click **Add Game** on the Settings page or drag the game's `.exe` onto the window. |
| Xbox games missing | Click **Refresh** — RHI uses the PackageManager API which may need a moment. |
| ReShade not loading | Check the install path via 📁 — the ReShade DLL must be next to the game executable. |
| ReShade not detected | If using a non-standard DLL name, RHI should detect it via binary signature scanning. Try **Refresh**. |
| Black screen (Unreal) | In ReShade → Add-ons → RenoDX, set `R10G10B10A2_UNORM` to `output size`. |
| UE-Extended not working | Turn on in-game HDR — UE-Extended requires native HDR output. |
| Downloads failing | Click **Refresh**, or clear cache from Settings → Open Downloads Cache. |
| Foreign DLL blocking install | Choose **Overwrite** in the confirmation dialog, or cancel to keep the existing file. |
| Games/mods out of sync | Settings → **Full Refresh** to clear all caches and re-scan. |
| Drag-and-drop not working | Ensure RHI is running. Drag-and-drop works even as administrator (UIPI bypass). |
| Vulkan ReShade not showing | Check that `reshade.ini` exists in the game folder. The Vulkan layer must also be installed globally. |
| Shaders missing after uninstall | Click **Refresh** — RHI will detect the missing shaders and redeploy them. |
| ReLimiter OSD hotkey not working | Re-apply the hotkey from Settings. |
| Xbox games losing mods after update | RHI detects Game Pass path changes and migrates installed mods automatically. If mods still appear missing, click **Refresh**. |
| OptiScaler update badge won't go away | Click **Refresh** — the version number updates after a fresh scan. |

---

## Third-Party Components

| Component | Author | Licence |
|-----------|--------|---------|
| [ReShade](https://reshade.me) | Crosire | [BSD 3-Clause](https://github.com/crosire/reshade/blob/main/LICENSE.md) |
| [RenoDX](https://github.com/clshortfuse/renodx) | clshortfuse & contributors | [MIT](https://github.com/clshortfuse/renodx/blob/main/LICENSE) |
| [ReLimiter](https://github.com/RankFTW/ReLimiter) | RankFTW | Source-available |
| [Display Commander](https://github.com/pmnoxx/display-commander?tab=readme-ov-file#display-commander) | pmnoxx | [GPL-3](https://github.com/pmnoxx/display-commander/blob/main/LICENSE) |
| [RE Framework](https://github.com/praydog/REFramework-nightly) | praydog | [MIT](https://github.com/praydog/REFramework/blob/master/LICENSE) |
| [Luma Framework](https://github.com/Filoppi/Luma-Framework) | Pumbo (Filoppi) | Source-available |
| [OptiScaler](https://github.com/optiscaler/OptiScaler) | OptiScaler contributors | Source-available |
| [7-Zip](https://www.7-zip.org/) | Igor Pavlov | [LGPL-2.1 / BSD-3-Clause](https://www.7-zip.org/license.txt) |
