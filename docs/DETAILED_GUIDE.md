# RHI — Detailed Guide

This document covers everything RHI does in depth. For a quick overview, see the [README](../README.md).

---

## Table of Contents

- [Layout](#layout)
- [Settings Page](#settings-page)
- [Game Detection](#game-detection)
- [Graphics API Detection](#graphics-api-detection)
- [Components](#components)
- [Vulkan ReShade Support](#vulkan-reshade-support)
- [Foreign DLL Protection](#foreign-dll-protection)
- [UE-Extended and Native HDR](#ue-extended-and-native-hdr)
- [Frame Rate Limiters](#frame-rate-limiters)
- [OptiScaler](#optiscaler)
- [Shader Packs](#shader-packs)
- [ReShade Addon Management](#reshade-addon-management)
- [Luma Framework](#luma-framework)
- [Per-Game Overrides](#per-game-overrides)
- [INI Presets](#ini-presets)
- [Remote Manifest](#remote-manifest)
- [Update All](#update-all)
- [Auto-Update](#auto-update)
- [Patch Notes](#patch-notes)
- [Drag-and-Drop](#drag-and-drop)
- [Addon Auto-Detection](#addon-auto-detection)
- [AddonPath Support](#addonpath-support)
- [Performance](#performance)
- [Data Storage](#data-storage)
- [Troubleshooting](#troubleshooting)
- [Third-Party Components](#third-party-components)

---

## Layout

RHI offers three view modes — Detail View, Grid View, and Compact View — plus a global Settings page and an About page. Window size and position are remembered across restarts.

### Detail View

The default layout with a game list sidebar on the left and a detail panel on the right. RHI remembers which view you were last using and restores it on launch.

### Grid View

A card-based layout showing all games as a grid. Toggle between views with the view switch button in the toolbar. Each card shows:

- Game name and platform icon
- Graphics API badge (e.g. DX12, VLK, DX11/12 / VLK)
- Installation status dots for RenoDX (RDX), ReShade (RS), ReLimiter (UL), Display Commander (DC), and OptiScaler (OS)
- Wiki status icon
- Update-available highlight border
- A Manage popout for quick access to install/uninstall/override controls

The grid view card flyout matches the detail panel: same component order (ReShade → RenoDX → Luma (when applicable) → separator → ReLimiter → DC), mutual exclusion greying, and "Frame limiters — Choose one" separator. The overrides flyout includes addons section, preset selector, ReShade Without Addon Support toggle, DC DLL override, DC update exclusion, bitness override, API override, change folder, remove game, copy report, wiki exclusion toggle, and full Reset Overrides support.

Games in Luma mode do not show a wiki status icon on the grid card.

### Compact View

A paged layout that shows the same content as Detail View but split across three navigable pages: Game Card, Overrides, and Management. Use the left/right arrow buttons on the sides to cycle between pages. The window locks to a fixed size in Compact View and restores your previous size when switching back. The loading skeleton matches the compact layout on startup.

### Toolbar

| Control | Function |
|---------|----------|
| Refresh | Rescan game library and fetch latest mod info. After initial boot, runs invisibly in the background. |
| Update All | Update ReShade, RenoDX, ReLimiter, Display Commander, and OptiScaler for all eligible games in one click. Lights up purple when updates are available. |
| Help | Flyout with Discord (RHI support channel), Guide (this document), About page, and Ko-fi |
| View toggle | Cycle between Detail View, Grid View, and Compact View. The button shows the name of the current view mode. |
| Settings | Navigate to the Settings page |

### Game List Sidebar (Detail View)

- **Search box** — filters games in real-time as you type (placeholder: "Filter games...")
- **Filter chips** — All Games, Favourites, Installed, Unreal, Unity, Other, RenoDX, Luma, Hidden, plus custom filter chips. Engine and mod filters can be combined. Your selected filter is saved and restored on reopen.
- **Custom filter chips** — save any search query as a named filter chip by clicking the "+" button next to the search bar. Custom chips use a teal colour scheme to distinguish them from built-in chips. Right-click to delete. Saving a custom filter clears the search box and auto-activates the new chip.
- **Universal keyword search** — matches across all game card properties: store, engine, graphics API (DX11, VLK, etc.), bitness, mod name, mod author, Luma mod name/author, Vulkan rendering path, and RE Engine/RE Framework games.
- **Game/installed counts** — how many games are visible and how many have mods installed
- **Game list** — each entry shows a platform icon, game name, and a green dot if updates are available

### Detail Panel

When a game is selected:

- **Game name** displayed above the info card, with mod author badge(s) on the right
- **Info card** — bordered section containing action buttons (Nexus Mods, PCGW links on the left; Hide, Favourite on the right) and info badges for platform, engine type, wiki status, graphics API, UE-Extended / Native HDR, and 32-bit/64-bit indicator
- **Install path** in monospace text below the game name
- **Installed addon filename** badge when a mod is installed
- **Browse button** — on the badges row, right-aligned
- **Per-addon Info buttons** — each component row has an Info button showing game-specific notes, wiki compatibility data, or addon descriptions
- **Components table** — ReShade → RenoDX → separator ("Frame limiters — Choose one") → ReLimiter → Display Commander, and Luma (when applicable), each with status, Info button, install/reinstall/update button, options menu, and uninstall button
- **Rendering path toggle** — for dual-API games (DirectX + Vulkan)
- **Overrides section** — all per-game settings inline
- **Management section** — Change install folder, Reset folder, Reset Overrides, Copy Report in a dedicated bordered section below overrides

### Status Bar

- **Status text** (left) — game count, installed count, or current operation
- **Single-player warning** (centre)
- **Version number and Patch Notes** (right) — app version from assembly, plus a link that opens a dialog showing recent changes

---

## Settings Page

Click **Settings** in the toolbar. Click **Back to Games** to return. The Settings button is disabled during initial load to prevent navigation before games are loaded.

| Section | Contents |
|---------|----------|
| Add Game | Manually add a game that wasn't auto-detected. Enter the game name and pick the install folder. |
| Full Refresh | Clears all caches (including API detection caches) and re-scans everything from disk. Use when games or mods appear out of sync. |
| Preferences | Skip Update Check on Launch, Verbose Logging, Custom Shaders toggle, Screenshot Path (with Browse and Open buttons, optional per-game subfolder, Apply to All writes to all reshade*.ini variants) |
| Hotkeys | ReShade UI Hotkey (capture key combo, applies to all reshade.ini files), ReLimiter OSD Hotkey (capture key combo in ReLimiter's native format, applies to all relimiter.ini files), OptiScaler Overlay Hotkey (default: Insert, applies to all OptiScaler.ini files) |
| Crash and Error Logs | Open Logs Folder, Open Downloads Cache, ReShade staging path |
| OptiScaler Settings | GPU type (NVIDIA/AMD/Intel), DLSS input toggle (AMD/Intel only), overlay hotkey, Apply to All Games, OptiScaler Compatibility List link |
| Mass INI Deployment | Deploy reshade.ini, relimiter.ini, DisplayCommander.ini, or OptiScaler.ini to all games with the corresponding component installed |
| Mass ReShade Preset Install | Select presets from the presets folder, choose target games via checkbox picker, optionally install required shader packs |

All settings apply immediately. Informational content (app description, credits & acknowledgements, disclaimers, and links) is on the About page, accessible from the Help flyout.

---

## Game Detection

RHI re-scans all stores on every launch and merges newly installed games into its cached library.

| Store | Detection Method |
|-------|-----------------|
| Steam | `libraryfolders.vdf` and `appmanifest_*.acf` files across all library folders |
| GOG | Registry keys under `HKLM\SOFTWARE\GOG.com\Games` |
| Epic Games | Manifest `.item` files in `ProgramData\Epic\EpicGamesLauncher\Data\Manifests` |
| EA App | `installerdata.xml` manifests, registry keys, default EA Games folders, EA Desktop local config |
| Ubisoft Connect | Registry keys under `HKLM\SOFTWARE\Ubisoft\Launcher\Installs`, `settings.yml`, default games folder |
| Xbox / Game Pass | Windows `PackageManager` API with `MicrosoftGame.config` detection. Falls back to `.GamingRoot` parsing, registry, and folder scanning |
| Battle.net | Uninstall registry entries (Blizzard/Activision publisher), `Battle.net.config` default path, default folder scanning |
| Rockstar Games | Uninstall registry entries (Rockstar publisher), launcher `titles.dat` paths, default folder scanning |

Games on a disconnected drive are preserved in the cache until the drive is reconnected. Per-platform detection failures are isolated — one store failing won't block others.

### Engine Detection

| Engine | Detection Method |
|--------|-----------------|
| Unreal Engine | Presence of Unreal-specific files and folder structures |
| Unreal (Legacy) | Unreal Engine 3 games identified by legacy folder layouts |
| Unity | `UnityPlayer.dll`, `Mono` folder, `MonoBleedingEdge` folder, `il2cpp` folder, `GameAssembly.dll` |
| RE Engine | RE Engine games detected for REFramework compatibility |
| Custom | Engine overrides from the remote manifest (e.g. `"Silk Engine"`, `"Creation Engine"`, `"BlackSpace Engine"`) |

Special manifest engine values:
- `"Unreal"` — treated as Unreal Engine (filters into Unreal, eligible for UE-Extended)
- `"Unreal (Legacy)"` — treated as Unreal Engine 3 (filters into Unreal)
- `"Unity"` — treated as Unity (filters into Unity, eligible for generic Unity addon)
- Any other string is stored as-is and displayed in the engine badge. The game filters into Other.

Custom engine names display with a dedicated engine icon.

### 32-bit / 64-bit Detection

RHI detects whether a game is 32-bit or 64-bit by examining the PE header of the game executable. The remote manifest can override this with `thirtyTwoBitGames` and `sixtyFourBitGames` lists, which take priority over auto-detection.

### Adding Games Manually

- **Add Game** button (Settings page) — enter the game name and pick the install folder
- **Drag and drop** — drag a game's `.exe` onto the RHI window. RHI detects the engine type, infers the game root folder by recognising store markers and engine layouts, and guesses the game name from folder structure. A confirmation dialog lets you edit the name before adding.

---

## Graphics API Detection

RHI scans game executables using PE header import table analysis to detect which graphics APIs a game uses.

### Detected APIs

| API | Badge | Detection |
|-----|-------|-----------|
| DirectX 8 | DX8 | PE import of `d3d8.dll` |
| DirectX 9 | DX9 | PE import of `d3d9.dll` |
| DirectX 10 | DX10 | PE import of `d3d10.dll` / `d3d10_1.dll` |
| DirectX 11 | DX11 | PE import of `d3d11.dll` |
| DirectX 12 | DX12 | PE import of `d3d12.dll` |
| Vulkan | VLK | PE import of `vulkan-1.dll` |
| OpenGL | OGL | PE import of `opengl32.dll` |

### Multi-Exe Scanning

All `.exe` files in the install directory and common subdirectories (`bin`, `binaries`, `x64`, `win64`, etc.) are scanned. This ensures games like Baldur's Gate 3 with multiple executables are detected correctly.

### Multi-API Display

Game cards show all detected APIs for dual-API games. Only valid multi-API combinations are displayed:

| Combination | Display | Valid |
|-------------|---------|-------|
| DX11/12 + VLK | `DX11/12 / VLK` | Yes |
| DX11/12 + OGL | Not shown together | No |
| DX9 + anything | DX9 shown alone | No |
| DX10 + anything | DX10 shown alone | No |
| OGL + anything | OGL shown alone | No |

### Automatic ReShade DLL Naming

Graphics API detection drives automatic ReShade DLL naming. When no user or manifest DLL override is set:

- **DX9 detected** → ReShade is installed as `d3d9.dll`
- **OpenGL-only** → ReShade is installed as `opengl32.dll`
- **All other cases** → ReShade is installed as `dxgi.dll` (default)

DX9 takes precedence — if a game imports both `d3d9.dll` and `opengl32.dll`, ReShade is installed as `d3d9.dll`.

See [Components > Automatic DLL Naming](#automatic-dll-naming-for-opengl-and-dx9-games) for the full priority chain.

### Manifest API Overrides

The remote manifest supports comma-separated API tags (e.g. `"DX12, VLK"`) for games like Red Dead Redemption 2 that load Vulkan dynamically and can't be detected via PE imports alone. Valid tokens: `DX8`, `DX9`, `DX10`, `DX11`, `DX12`, `Vulkan`/`VLK`, `OpenGL`/`OGL`.

---

## Components

The detail panel shows a Components section with up to five rows, separated into two groups by a labeled divider ("Choose one from below"):

| Row | Component | Controls |
|-----|-----------|----------|
| ReShade | ReShade | Install / Reinstall / Update — Copy INI — Uninstall |
| RenoDX | RenoDX Mod | Install / Reinstall / Update — Info — UE-Extended options — Uninstall |
| Luma | Luma Framework | Install / Uninstall (shown only in Luma mode) |
| — | *separator* | "Frame limiters — Choose one" |
| ReLimiter | ReLimiter | Install / Reinstall / Update — Info — Copy INI — Uninstall |
| Display Commander | Display Commander | Install / Reinstall / Update — Info — Copy INI — Uninstall |
| — | *separator* | "── Optional ──" |
| OptiScaler | OptiScaler | Install / Reinstall / Update — Info — Copy INI — Uninstall |

Each component row also has an **Info** button between the status text and the install button. See [Per-Addon Info Buttons](#per-addon-info-buttons) below.

ReLimiter and Display Commander are mutually exclusive — only one frame rate limiter can be installed per game at a time. When one is installed, the other's install button is greyed out and the row is visually dimmed, making the mutual exclusivity clear at a glance. Removing one re-enables the other.

### Per-Addon Info Buttons

Each component row (RE Framework, ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, Luma) has an "Info" button that opens a dialog with context about that addon for the selected game.

**Content resolution** follows a three-tier priority:

1. **Manifest notes** — game-specific notes from the remote manifest (`gameNotes`, `lumaGameNotes`)
2. **Wiki content** — compatibility data from the relevant wiki (OptiScaler wiki, HDR Gaming Database, RenoDX wiki)
3. **Generic description** — a general description of what the addon does

**Visual differentiation:**

- Buttons with per-game content (manifest notes or wiki data) are highlighted in **blue** so you can spot them at a glance
- Buttons with only a generic description use a **muted** style
- Tooltips indicate what content is available before clicking

The old header-level "ℹ" (Info) and "💬" (Discussion) buttons have been removed. Their functionality — game notes, wiki links, and discussion links — has moved to the per-addon RenoDX Info button.

### RE Framework Requirement for RE Engine Games

RE Engine games require RE Framework to be installed before ReShade can be installed. When RE Framework is not present, the ReShade install button shows "⚠ RE Framework required" and is greyed out. This prevents broken setups where ReShade is installed without the framework needed for proper injection on RE Engine titles.

### Version Display

The status label next to install buttons shows the installed version number (e.g. `6.7.3`) instead of just "Installed". Falls back to "Installed" if no version information is available. When an update is available, the text turns purple and shows the current version. After updating, it switches to the new version in green.

### Mod Author Badges

Named mods from the RenoDX wiki display the mod author as a bordered badge on the detail panel info line. Multiple authors each get their own badge. Generic Unreal Engine mods show "ShortFuse", UE-Extended mods show "Marat", and generic Unity mods show "Voosh". Author badges are clickable links to Ko-fi donation pages where available. Games in Luma mode show the Luma mod author in place of the RenoDX author. Author display names can be overridden via the remote manifest (e.g. wiki handle `"oopydoopy"` displays as `"Jon"`).

### Clickable Status Links

- ReShade "Installed" → links to [reshade.me](https://reshade.me)
- RenoDX "Installed" → links to the game's wiki page (or the mods list)
- ReLimiter "Installed" → links to the [ReLimiter feature guide](https://github.com/RankFTW/ReLimiter?tab=readme-ov-file#relimiter--comprehensive-feature-guide)
- Display Commander status text is underlined when installed (clickable link)

Version numbers and author donation badges show a hand cursor on hover when they are clickable.

### Automatic DLL Naming for OpenGL and DX9 Games

RHI automatically selects the correct ReShade DLL filename based on the game's detected graphics API:

| Detected API | ReShade Filename |
|--------------|-----------------|
| DirectX 9 | `d3d9.dll` |
| OpenGL only | `opengl32.dll` |
| All other APIs | `dxgi.dll` (default) |

The full priority chain for ReShade DLL naming:

1. **User DLL override** (set in Per-Game Overrides) — always wins
2. **Manifest `dllNameOverrides`** — per-game overrides from the remote manifest
3. **Automatic API-based naming** — DX9 → `d3d9.dll`, OpenGL-only → `opengl32.dll`
4. **Default** — `dxgi.dll`

### ReShade Detection Under Non-Standard Filenames

ReShade installations using non-standard DLL filenames (e.g. `d3d11.dll`, `dinput8.dll`, `version.dll`, `winmm.dll`, `d3d9.dll`, `opengl32.dll`) are detected via binary signature scanning as a fallback. The scan matches on `reshade.me` or `crosire` strings unique to the actual ReShade binary, and rejects files over 15 MB as too large to be ReShade. Reinstalling correctly removes the old non-standard DLL before placing the new one.

Common DLL names available in the override dropdown: `dxgi.dll`, `d3d11.dll`, `dinput8.dll`, `version.dll`, `winmm.dll`, `d3d12.dll`, `xinput1_3.dll`, `msvcp140.dll`, `bink2w64.dll`, `d3d9.dll`.

---

## Vulkan ReShade Support

RHI provides full Vulkan implicit layer support for ReShade, enabling ReShade injection for Vulkan-rendered games without per-game DLL injection.

### How It Works

1. **Global Vulkan layer** — RHI installs ReShade as a Vulkan implicit layer via the Windows registry (`HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers`), making ReShade available to all Vulkan games system-wide.
2. **Layer manifest** — A bundled `ReShade64.json` manifest is deployed alongside the ReShade DLL to `C:\ProgramData\ReShade\`.
3. **Per-game INI** — A dedicated `reshade.vulkan.ini` with Vulkan-tuned depth buffer settings is deployed to each game folder.
4. **Footprint file** — An `RDXC_VULKAN_FOOTPRINT` marker file is placed in the game folder to enable managed shader deployment.

### Lightweight Install

When the global Vulkan layer is already registered, clicking the ReShade install button on a Vulkan game performs a fast lightweight deploy (INI + footprint + shaders only) without requiring administrator privileges or reinstalling the layer.

### Dual-API Games

Games detected with both DirectX and Vulkan show a rendering path toggle in the detail panel. Switching from DirectX to Vulkan automatically uninstalls DX ReShade, removes `reshade.ini` and managed shaders, and restores `reshade-shaders-original` if present.

### Per-Game Uninstall

An uninstall button appears for Vulkan games that have `reshade.ini` deployed. Clicking it removes `reshade.ini`, the footprint file, and managed shaders from the game folder. This does not affect the global Vulkan layer.

---

## Foreign DLL Protection

When installing ReShade, RHI checks whether an existing DLL belongs to another tool (DXVK, Special K, ENB, etc.) using binary signature scanning. The scan matches on `reshade.me` or `crosire` strings unique to the actual ReShade binary, and rejects files over 15 MB as too large to be ReShade.

If the existing file is unidentified, a confirmation dialog asks whether to overwrite. During Update All, foreign files are silently skipped to avoid accidentally replacing third-party DLLs.

---

## UE-Extended and Native HDR

Unreal Engine games with native HDR are automatically assigned UE-Extended via the remote manifest. These display "Extended UE Native HDR" as their engine badge. In-game HDR must be turned on for UE-Extended to work.

The UE-Extended toggle appears for every Unreal Engine game that does not have a named mod on the RenoDX wiki. A compatibility warning dialog pops up when enabling UE-Extended, advising that not all games are compatible and to check the Notes section for game-specific information.

Games on the `nativeHdrGames` list are flagged for native HDR support. Games on the `ueExtendedGames` list are marked for the UE-Extended addon. Both lists are maintained in the remote manifest and can be updated without an app release.

Native HDR games show a specific message in the RenoDX Info button: "This game uses UE-Extended with native HDR. You must enable HDR in the game's display settings for the mod to work."

---

## Frame Rate Limiters

RHI supports two frame rate limiters: [ReLimiter](https://github.com/RankFTW/ReLimiter?tab=readme-ov-file#relimiter--comprehensive-feature-guide) and Display Commander. They are mutually exclusive per game — only one can be installed at a time. When one is installed, the other's install button is greyed out. Removing one re-enables the other.

### ReLimiter

ReLimiter is an optional per-game frame pacing addon downloaded from GitHub on demand.

#### 32-bit and 64-bit Support

RHI automatically selects the correct addon file based on the game's detected bitness:

| Game Bitness | Addon File |
|--------------|------------|
| 64-bit | `relimiter.addon64` |
| 32-bit | `relimiter.addon32` |

Both variants are downloaded from the same GitHub releases endpoint and cached separately so they don't overwrite each other.

#### Install / Update / Uninstall

The correct addon file is selected automatically based on the game's bitness, downloaded from its GitHub release when first needed, and cached locally. Legacy `ultra_limiter.addon64` / `ultra_limiter.addon32` files are cleaned up automatically during install.

#### INI Configuration

RHI bundles a default `relimiter.ini` seeded to `%LOCALAPPDATA%\RHI\inis\` on first launch. A copy button on the ReLimiter component row copies this INI to the game folder (or AddonPath if configured). Customise the INI in the inis folder and it will be used for all future copies.

Installing ReLimiter for the first time on a game automatically copies `relimiter.ini` from the AppData INI folder to the game directory. If the INI already exists in the game folder it is left untouched, so per-game customisations are never overwritten.

#### Update Detection

Updates are detected by comparing the locally cached file against the remote release using both file size and SHA-256 hash. When a newer version is available, the status changes and the install button shows "Update". Version metadata is tracked per-bitness so 32-bit and 64-bit updates are independent.

#### Status Indicators

| Colour | Meaning |
|--------|---------|
| Green | Installed and up to date |
| Orange | Update available |

The ReLimiter status dot is hidden when a game is in Luma mode.

### Display Commander

Display Commander (DC) is an alternative frame rate limiter, using the LITE variant downloaded from GitHub on demand.

#### 32-bit and 64-bit Support

Like ReLimiter, DC automatically selects the correct file based on the game's detected bitness and caches 32-bit and 64-bit variants separately.

#### Install / Update / Uninstall

DC is installed with one click from the detail panel. The install respects mutual exclusion — if ReLimiter is already installed, the DC install button is disabled. Uninstalling DC re-enables the ReLimiter button.

#### DLL Naming Override

A single "DLL naming overrides" toggle controls both the ReShade and DC filenames together. Turning it ON enables both dropdowns with safe defaults (ReShade64.dll and the default DC addon name); turning it OFF reverts both to their default filenames in one action. Each dropdown is editable — you can type a custom DLL name and press Enter. The dropdowns filter out the other component's current filename to prevent conflicts.

The DC DLL naming resolution priority chain:

1. **User DLL override** (set in Per-Game Overrides) — always wins
2. **Manifest `dllNameOverrides`** (the `dc` field) — per-game overrides from the remote manifest
3. **Default** — standard DC addon filename

#### INI Configuration

RHI bundles a default `DisplayCommander.ini` seeded to `%LOCALAPPDATA%\RHI\inis\` on first launch. A 📋 button on the DC component row (and in the card flyout) copies this INI to the game folder. Customise the INI in the inis folder and it will be used for all future copies.

Installing Display Commander for the first time on a game automatically copies `DisplayCommander.ini` from the AppData INI folder to the game directory. If the INI already exists in the game folder it is left untouched, so per-game customisations are never overwritten.

#### Update Detection

DC is checked for updates on startup alongside other components. When an update is available, the sidebar badge and purple update styling appear. Update All now includes DC for eligible games.

#### DC Detection on Game Scan

RHI detects existing DC installations when scanning game folders, including files with custom DLL override names via tracking records. DC version is read from PE file info on scan (not just metadata) to avoid showing placeholder version strings.

#### Global Update Exclusion

A per-game DC update exclusion toggle in the overrides panel lets you pin a specific DC version on certain games, excluding them from Update All.

---

## OptiScaler

[OptiScaler](https://github.com/optiscaler/OptiScaler) is a 64-bit middleware DLL that intercepts upscaler calls (DLSS, FSR, XeSS) from games and redirects them to alternative upscaling backends. RHI manages the full OptiScaler lifecycle: download, staging, install, uninstall, update detection, INI configuration, ReShade coexistence, DLL naming overrides, and hotkey configuration.

OptiScaler is shown in the detail panel under an "── Optional ──" separator, below the frame rate limiter rows. It is available for all detected games regardless of upscaler detection, but only enabled for 64-bit games. 32-bit games show the OptiScaler row greyed out with strikethrough text.

### Install / Update / Uninstall

One-click install from the detail panel or card flyout. On first install, a one-time warning dialog explains OptiScaler's purpose and single-player recommendation. The install copies `OptiScaler.dll` (renamed to the effective DLL name), `OptiScaler.ini`, and all companion files (`fakenvapi.dll`, `dlssg_to_fsr3.dll`, FFX SDK DLLs) to the game folder. The latest DLSS DLLs (`nvngx_dlss.dll`, `nvngx_dlssd.dll`, `nvngx_dlssg.dll`) are also deployed from the staging cache. For AMD/Intel GPUs, `OptiPatcher.asi` is deployed to the game's `plugins` folder.

Uninstalling removes the renamed OptiScaler DLL, `OptiScaler.ini`, and all companion files. If ReShade was renamed to `ReShade64.dll`, it is restored to its correct filename.

Updates replace the DLL and companion files while preserving the existing `OptiScaler.ini` in the game folder.

### INI Configuration

RHI seeds a default `OptiScaler.ini` to `%LOCALAPPDATA%\RHI\inis\` on first install. The 📋 button on the OptiScaler row copies this INI to the game folder. Customise the INI in the inis folder and it will be used for all future copies.

`LoadReshade=true` is always enforced in the deployed INI, regardless of whether ReShade is currently installed. This ensures ReShade will be loaded correctly if installed later.

### ReShade Coexistence

When OptiScaler is installed alongside ReShade, RHI automatically renames the ReShade DLL to `ReShade64.dll`. OptiScaler loads ReShade from this exact filename via the `LoadReshade=true` setting.

- Installing OptiScaler when ReShade is present → ReShade DLL renamed to `ReShade64.dll`
- Installing ReShade when OptiScaler is present → ReShade deployed as `ReShade64.dll`
- Uninstalling OptiScaler when `ReShade64.dll` exists → ReShade restored to its correct filename (user override > manifest override > API-based default > `dxgi.dll`)
- While OptiScaler is installed, the ReShade DLL naming override dropdown is disabled with a tooltip explaining that OptiScaler controls the ReShade DLL name

### DLL Naming

OptiScaler.dll must be renamed to a proxy DLL filename when deployed. The default is `dxgi.dll`. Supported names: `dxgi.dll`, `winmm.dll`, `d3d12.dll`, `dbghelp.dll`, `version.dll`, `wininet.dll`, `winhttp.dll`.

The DLL name is resolved using a priority chain:

1. **User DLL override** (set in Per-Game Overrides) — always wins
2. **Manifest `optiScalerDllOverrides`** — per-game overrides from the remote manifest
3. **Vulkan auto-detection** — Vulkan games automatically use `winmm.dll`
4. **Default** — `dxgi.dll`

The OptiScaler DLL name dropdown in the overrides panel filters out filenames already used by ReShade or Display Commander for the same game to prevent conflicts.

### Hotkey Configuration

The OptiScaler overlay toggle key is configured from the Settings page alongside the ReShade and ReLimiter hotkeys. The default is `Insert`. Changing the hotkey writes `ShortcutKey=` to `OptiScaler.ini` in the inis folder. "Apply to All Games" updates the hotkey in all game folders where OptiScaler is installed.

### 32-bit Game Limitations

OptiScaler is 64-bit only. For 32-bit games, the OptiScaler row is displayed with reduced opacity, strikethrough text, and disabled interaction.

### Per-Game Update Exclusion

A per-game OptiScaler update exclusion toggle in the overrides panel lets you pin a specific OptiScaler version on certain games, excluding them from Update All.

### Detection

RHI detects existing OptiScaler installations by scanning DLLs in game folders for OptiScaler binary signatures and checking for `OptiScaler.ini` presence. Detected installations are tracked and displayed correctly without requiring reinstallation through RHI. The foreign DLL protection system recognises OptiScaler DLLs and does not flag them as foreign.

### OptiScaler Wiki Integration

RHI fetches compatibility data from the OptiScaler wiki at startup, covering both the standard and FSR4 compatibility lists. The OptiScaler Info button displays this data in a dialog showing:

- Working status (e.g. Working, Partially Working, Not Working)
- Supported upscalers
- Notes and known issues
- Direct link to the game's wiki page

The remote manifest includes an `optiScalerWikiNames` section for mapping Steam game names to their OptiScaler wiki equivalents, handling naming differences (e.g. Resident Evil, S.T.A.L.K.E.R., Borderlands® 4, Assassin's Creed).

---

## Shader Packs

RHI downloads and maintains a collection of 41 ReShade shader packs, merged into a shared staging folder and deployed per-game.

### Categories

- **Essential** — Lilium HDR Shaders, required for HDR tone mapping. Selected by default on fresh installs (can be unticked in the global shader picker).
- **Recommended** — Core HDR and post-processing packs: crosire reshade-shaders (master), PumboAutoHDR, smolbbsoop shaders, MaxG2D Simple HDR Shaders, clshortfuse ReShade shaders, and potatoFX.
- **Extra** — Community shader packs covering cinematic colour grading, film emulation, VR tools, CRT simulation, retro filters, screen-space reflections, global illumination, artistic effects, and more. Includes packs from SweetFX, OtisFX, Depth3D, qUINT, iMMERSE, METEOR, ZenteonFX, GShade-Shaders, CShade, prod80, CobraFX, and others.

### Per-Game Shader Overrides

Per-game shader overrides allow different games to use different subsets of shader packs. Select mode opens a picker to choose specific packs for that game. Clicking Deploy immediately deploys the chosen shaders to the game folder.

### Shader Pack Dependencies

Shader packs can declare dependencies on other packs. When a pack with dependencies is selected in the picker, its required packs are automatically checked. For example, selecting Azen automatically selects smolbbsoop shaders. The dependency is one-way — deselecting the required pack independently is still allowed.

### Deploy Destinations

| Scenario | Destination |
|----------|-------------|
| DLL ReShade | `<game folder>\reshade-shaders\Shaders\` and `\Textures\` |
| Vulkan ReShade | `<game folder>\reshade-shaders\Shaders\` and `\Textures\` (requires `RDXC_VULKAN_FOOTPRINT`) |

User-owned shader folders are preserved by renaming to `reshade-shaders-original` before deployment and restored when ReShade is uninstalled or when Vulkan ReShade is uninstalled.

### Custom Shaders

Place custom shaders in `%LOCALAPPDATA%\RHI\reshade\Custom\Shaders\` and textures in `%LOCALAPPDATA%\RHI\reshade\Custom\Textures\`. Enable the Custom Shaders toggle in Settings to include them in deployments.

### Startup Deployment

On launch, RHI ensures shader packs are fully downloaded before syncing shaders to all installed game folders. Games with ReShade installed will have the correct global or per-game shaders deployed automatically.

---

## ReShade Addon Management

RHI includes a curated ReShade addon manager that lets you browse, download, and manage addons from the official ReShade Addons.ini list.

### Addon Manager

Click the "ReShade Addons" button in the main toolbar header to open the Addon Manager. A one-time warning dialog explains that addons are advanced features before the manager opens for the first time.

The manager shows all available addons with their name and description. Each addon has a toggle switch:

- **Toggle on** — downloads the addon (if not already cached) and enables it globally. The addon is deployed to all games with ReShade installed.
- **Toggle off** — disables the addon globally. Files remain cached in the staging area for later use.

Repository-only addons (no download URLs) are filtered out of the manager.

### Global Deployment

Enabled addons are automatically deployed to every game with ReShade installed. When ReShade is installed on a new game, enabled addons are deployed there too. When ReShade is uninstalled, managed addons are removed from the game folder. Clicking Refresh syncs addons to all games with ReShade installed. The correct bitness variant (`.addon32` or `.addon64`) is selected based on the game's detected bitness. Addons without the required variant are skipped.

### Auto-Update

On startup, RHI checks all downloaded addons for updates using GitHub release tags or HTTP ETags. Newer versions are downloaded automatically. Existing versions are retained on failure.

### Per-Game Addon Overrides

Each game's override panel includes an Addons section with a Global toggle:

- **Global on** — the game uses the globally enabled addon set
- **Global off** — opens a per-game addon picker with the same toggle-based UI. Toggle on downloads if needed and enables for that game only.

Per-game addon selections are persisted across restarts.

### RenoDX DevKit Addon

The RenoDX DevKit addon is always available in the addon manager alongside the official Addons.ini entries, with download URLs for both 32-bit and 64-bit variants from the RenoDX GitHub releases.

### DLSS Fix Addon

The DLSS Fix addon makes ReShade draw on native game frames instead of frame gen frames, and hides DLSS upscaling from ReShade. It is always available in the addon manager with a "How to use" link to the RenoDX wiki. 64-bit only.

### Addon Storage

| Path | Contents |
|------|----------|
| `%LOCALAPPDATA%\RHI\addons\` | Downloaded addon files (`.addon32`, `.addon64`) |
| `%LOCALAPPDATA%\RHI\addons\versions.json` | Version tracking per addon |
| `%LOCALAPPDATA%\RHI\addons_cache.ini` | Cached Addons.ini for offline fallback |

### Override Panel Layout

The override panel is organised as:

- **Top row** — Game name, wiki mapping, wiki exclusion
- **Middle row** — Bitness/API overrides (left), Global update inclusion (right)
- **Bottom row** — Shaders and preset selector (left), Addons and Normal ReShade toggle (right)

Folder management buttons (Change install folder, Reset folder, Reset Overrides, Copy Report) are in a separate bordered section below the overrides panel.

---

## Luma Framework

[Luma Framework](https://github.com/Filoppi/Luma-Framework) by Pumbo (Filoppi) is a DX11 modding framework adding HDR support via the ReShade addon system. RHI detects Luma-compatible games and shows a toggle badge in the detail panel.

### How Luma Mode Works

- RenoDX and ReShade are automatically uninstalled and hidden. Only Install Luma is available.
- Installing Luma deploys the mod zip, `reshade.ini`, and Lilium HDR shaders.
- Uninstalling or toggling off removes all Luma files.
- The info popup shows Luma-specific notes from the wiki and remote manifest.
- Games listed in the remote manifest `lumaDefaultGames` automatically start in Luma mode on first detection.
- The Luma mode toggle is centered in the Components header with clear "Click to enable/disable Luma" text for improved visibility.
- The Luma install row includes a 📋 button for deploying `reshade.ini` to the game folder, matching the ReShade row.
- After installing Luma, ReLimiter and Display Commander become available immediately without requiring a manual Refresh.

### Trusted Downloads

Luma downloads are restricted to trusted GitHub URLs under `https://github.com/Filoppi/`. This prevents arbitrary file downloads when installing Luma mods.

### Luma Notes

Luma-specific notes are sourced from both the Luma wiki and the remote manifest `lumaGameNotes` field. These notes appear in the info popup when a game is in Luma mode and provide game-specific setup instructions.

---

## Per-Game Overrides

The Overrides section appears below Components in the detail panel. All controls save immediately when changed.

| Override | Effect |
|----------|--------|
| Game name (editable) | Rename the game — persists across Refresh and restarts |
| Wiki mod name | Match to a different wiki entry (also applies to Luma matching) |
| Reset | Restore original name and clear wiki mapping |
| Wiki exclusion | Exclude the game from wiki lookups |
| DLL naming overrides | A single toggle controls both ReShade and Display Commander filenames together. Turning ON enables both dropdowns with safe defaults; turning OFF reverts both to their default names. Each dropdown filters out the other component's current filename to prevent conflicts. Both dropdowns are editable (supports manual DLL names via Enter key). |
| Global update inclusion | "Update Inclusion" button opens a dialog with checkboxes for ReShade, RenoDX, ReLimiter, Display Commander, and OptiScaler. A compact colour-coded summary line shows the current state at a glance. All default to On. |
| Shader Mode | Global / Select / Custom. Global uses the global shader selection. Select opens a picker for specific shader packs. Custom uses shaders from custom shader directories. |
| Addon Mode | Global / Select. Global uses the globally enabled addon set. Select opens a per-game addon picker. |
| Rendering Path | For dual-API games: automatically determined based on detected APIs. Vulkan-only and dual-API games route to the Vulkan global layer install. |
| Bitness override | Dropdown: Auto, 32-bit, or 64-bit. Overrides PE header auto-detection. |
| Graphics API override | Dropdown: Auto, DirectX8, DirectX9, DirectX10, DX11/DX12, Vulkan, OpenGL. "Auto" uses the auto-detected value from PE header scanning. |
| Reset Overrides | Reset all override settings back to defaults |
| Copy Report | Generate a diagnostic code containing game info, overrides, and component status for Discord or GitHub issues |
| Change install folder | Pick a different install folder for the game |
| Reset folder / Remove game | Reset the install folder or remove a manually added game |
| Select ReShade Preset | Deploy `.ini` preset files from the reshade-presets folder to the game |
| ReShade Without Addon Support | Toggle to switch from addon-enabled ReShade to standard ReShade (without addon support). When enabled, all addons are removed, addon rows are dimmed and disabled, and the addon override toggle is locked off. Toggle back to restore addon ReShade and re-deploy addons. |

---

## INI Presets

RHI bundles default INI files seeded on first launch. They are deployed alongside their respective components using a merge strategy — template keys take precedence, but game-specific settings (addon configs, effect toggles, custom keybinds) are preserved.

Config files in `%LOCALAPPDATA%\RHI\inis\`:

| File | Deployed When |
|------|---------------|
| `reshade.ini` | Every ReShade install, or via copy button. Merged into existing INI if present. |
| `reshade.vulkan.ini` | Alongside `reshade.ini` for Vulkan games. Vulkan-tuned depth buffer settings. |
| `reshade.rdr2.ini` | Red Dead Redemption 2 only. Overlay key set to END to avoid keybind conflicts. |
| `relimiter.ini` | Via copy button on the ReLimiter row. Copied to the game folder (or AddonPath) as-is. |
| `DisplayCommander.ini` | Via 📋 button on the Display Commander row. Copied to the game folder (or AddonPath) as-is. |
| `OptiScaler.ini` | Via 📋 button on the OptiScaler row. Copied to the game folder with `LoadReshade=true` enforced. |
| `ReShadePreset.ini` | Automatically alongside `reshade.ini` if the file exists in the inis folder. |

To use a custom ReShade preset, place your `ReShadePreset.ini` in the inis folder. It will be copied to every new game install automatically.

---

## ReShade Presets

Place `.ini` preset files in `%LOCALAPPDATA%\RHI\inis\reshade-presets\`. The "Select ReShade Preset" button in the overrides panel lists all files in this folder with checkboxes. Click "Deploy" to copy the selected presets to the game's install folder. If the folder is empty, the dialog offers to open it in Explorer.

You can also drag and drop a ReShade preset `.ini` file directly onto the RHI window. RHI validates the file as a genuine ReShade preset, saves it to the presets folder, lets you pick a target game, and copies it to the game directory. After deploying, RHI offers to automatically resolve and install the shader packs required by the preset — parsing the `Techniques=` line, matching `.fx` files against known shader packs, switching the game to per-game shader mode, and deploying the matched packs. The same shader install prompt also appears when deploying presets from the existing preset selection dialog.

---

## Remote Manifest

RHI fetches a remote manifest from GitHub on every launch, providing game-specific overrides without app updates. The manifest is fetched from the GitHub API with a `raw.githubusercontent.com` fallback, and cached locally for offline use.

### Manifest Fields

| Field | Effect |
|-------|--------|
| `blacklist` | Excluded non-game apps (e.g. Steamworks redistributables, launchers) |
| `installPathOverrides` | Correct wrong install paths (e.g. `"Cyberpunk 2077": "bin\\x64"`) |
| `wikiNameOverrides` | Map detected game name to wiki mod name |
| `wikiStatusOverrides` | Force a specific wiki status icon for a game |
| `wikiUnlinks` | Ignore false fuzzy wiki matches, fall back to generic engine addon |
| `gameNotes` | Game-specific notes with optional URL and label, shown in the info popup |
| `lumaGameNotes` | Luma-specific notes shown when game is in Luma mode |
| `nativeHdrGames` | Auto-assign UE-Extended for Unreal games with native HDR |
| `ueExtendedGames` | Mark games for the UE-Extended addon |
| `thirtyTwoBitGames` | Override auto-detected bitness to 32-bit |
| `sixtyFourBitGames` | Override auto-detected bitness to 64-bit |
| `engineOverrides` | Force a specific engine label (e.g. `"Silk Engine"`, `"Creation Engine"`) |
| `dllNameOverrides` | Set ReShade install filename per game. User-set overrides take priority. |
| `graphicsApiOverrides` | Comma-separated API tags for games that can't be detected via PE imports |
| `snapshotOverrides` | Direct addon download URL when wiki lacks one |
| `lumaDefaultGames` | Games that auto-start in Luma mode on first detection |
| `forceExternalOnly` | Redirect install to an external URL with a custom label (e.g. Discord) |
| `dcModeOverrides` | Override the default DC mode for specific games |
| `donationUrls` | Ko-fi links for mod authors, updated without app releases |
| `authorDisplayNames` | Override wiki maintainer handles with display names |

---

## Update All

The **Update All** button in the toolbar updates ReShade, RenoDX, ReLimiter, Display Commander, and OptiScaler across all eligible games in one click. The button lights up purple when updates are available.

### Per-Component Toggles

Each component respects its own per-game inclusion toggle — a game excluded from ReShade updates can still receive RenoDX updates. These toggles are set in the Per-Game Overrides section under "Global update inclusion".

### Foreign DLL Skipping

Games with foreign DLLs (non-ReShade files detected via binary signature scanning) are silently skipped during Update All to avoid accidentally replacing third-party DLLs like DXVK, Special K, or ENB.

---

## Auto-Update

RHI checks for new versions on launch by querying the GitHub Releases API. Disable via Settings > Preferences > Skip update check on launch.

### Stable and Beta Channels

When Beta Opt-In is enabled in Settings, RHI checks both the stable release and the beta release. The version resolver determines which update to offer:

- Stable always wins over beta at the same or higher base version
- Beta is only offered when its base version exceeds the latest stable, or when the current app is already on a beta and a newer beta is available
- No update is offered if all candidates are at or below the current version

The app encodes its beta status in the 4th component of the assembly version: `1.6.6.0` = stable, `1.6.6.1` = beta 1, etc.

---

## Patch Notes

RHI shows a patch notes dialog on first launch after an update, displaying the most recent version changes in a scrollable markdown view. The dialog can also be opened at any time from the Patch Notes link in the status bar.

---

## Drag-and-Drop

RHI supports drag-and-drop for adding games and installing mods. Drag-and-drop works even when RHI is running as administrator (UIPI bypass via `WM_DROPFILES`).

### Supported File Types

| File Type | Behaviour |
|-----------|-----------|
| Game `.exe` | Opens an add-game dialog with auto-detected engine, inferred game root, and suggested name |
| `.addon64` / `.addon32` | Opens an install dialog with a game picker (auto-selects based on filename, falls back to currently selected game) |
| `.zip`, `.7z`, `.rar`, `.tar`, `.gz`, `.bz2`, `.xz`, `.tgz` | Extracted using bundled 7-Zip. Addon files inside are found and offered for install. Multiple addons trigger a picker dialog. |
| ReShade preset `.ini` | Validated as a genuine ReShade preset, saved to the presets folder, and deployed to a chosen game. RHI then offers to auto-install required shader packs by parsing the `Techniques=` line and matching `.fx` files against known packs. |
| URL (`.url` shortcut) | Parsed and processed as an addon download URL |

### Extension Validation

File extensions are validated before any network or file activity. Only the following extensions are accepted: `.exe`, `.addon64`, `.addon32`, `.ini`, `.zip`, `.7z`, `.rar`, `.tar`, `.gz`, `.bz2`, `.xz`, `.tgz`. Files with unrecognised extensions are silently skipped.

---

## Nexus Mods & PCGamingWiki Links

Each detected game can show clickable Nexus Mods and PCGamingWiki (PCGW) buttons in the detail panel info card.

### Nexus Mods Resolution

RHI fetches the public Nexus Mods game catalogue (`games.json`) on startup, caches it locally for 24 hours, and builds a normalized-name lookup dictionary. When a game is detected, its name is matched against the dictionary to find the Nexus Mods page URL.

### PCGW Resolution

PCGW links are resolved via Steam AppID through a priority chain:

1. Manifest `steamAppIdOverrides` (highest priority)
2. Cached AppID from previous resolution
3. Steam AppID parsed from ACF filename during detection
4. `steam_appid.txt` in the game install directory
5. Steam Store search API (rate-limited to 1 request/second)

When an AppID is resolved, the PCGW URL is constructed as `https://www.pcgamingwiki.com/api/appid.php?appid={id}`. For games without a Steam AppID, an OpenSearch fallback queries the PCGW wiki directly.

### Manifest Overrides

Games where automatic resolution fails can be overridden in the remote manifest:

- `nexusUrlOverrides` — maps game name to Nexus Mods URL
- `steamAppIdOverrides` — maps game name to Steam AppID (integer)
- `pcgwUrlOverrides` — maps game name to PCGW URL

### Caching

- Nexus games list: `%LOCALAPPDATA%\RHI\nexus_games.json` (24-hour TTL)
- Steam AppID cache: `%LOCALAPPDATA%\RHI\steam_appid_cache.json` (permanent, grows as games are resolved)

---

## Addon Auto-Detection

RHI watches your Downloads folder for new addon files and prompts you to install them.

### Downloads Folder Watching

The default watch folder is the system Downloads directory. You can change it in Settings via the Browse button, or reset it to the default. RHI monitors for new `renodx-*.addon64` and `renodx-*.addon32` files appearing in the watched folder.

### Archive Auto-Install

The watch folder also detects `.zip`, `.7z`, and `.rar` archives containing "renodx" in the filename. When a matching archive appears (e.g. from a Nexus Mods download), RHI automatically extracts it using bundled 7-Zip, finds the addon files inside, and starts the install flow — no drag-and-drop needed.

### Named Pipe Forwarding

Double-clicking an addon file in Explorer opens RHI and triggers the install flow. If RHI is already running, the file path is forwarded to the existing instance via a named pipe, avoiding duplicate instances.

### Filename Prefix

All entry points enforce the `renodx-` filename prefix to avoid triggering on unrelated addon files. Only files matching `renodx-*.addon64` or `renodx-*.addon32` are processed by the auto-detection system.

---

## AddonPath Support

Addon installs (RenoDX and ReLimiter) respect the `AddonPath` setting in `reshade.ini`.

### Path Resolution

If the `[ADDON]` section of `reshade.ini` contains an `AddonPath=` line, addons are deployed to that folder instead of the game root. Relative paths are resolved against the game directory.

### Affected Operations

Uninstall, update detection, and addon scanning all check the same resolved AddonPath. This ensures that addons installed to a custom path are correctly detected, updated, and removed.

---

## Performance

RHI includes several optimisations to reduce startup and refresh times:

- **Parallel shader pack checks** — `EnsureLatestAsync` uses `Task.WhenAll` instead of sequential foreach, cutting ~10 seconds from launch.
- **Parallel game folder shader syncs** — game folder shader syncs run via `Task.WhenAll` instead of sequentially.
- **Parallel card building** — game cards are constructed using `Parallel.ForEach` with `ConcurrentBag`.
- **PE-level API cache** — `GraphicsApiDetector` caches `DetectAllApis` results to `%LOCALAPPDATA%\RHI\api_cache.json`, keyed by file path + last write time. Subsequent launches skip PE header scanning entirely.
- **Game-level API cache** — `MainViewModel` caches full `DetectGraphicsApi` + `_DetectAllApisForCard` results to `%LOCALAPPDATA%\RHI\game_api_cache.json`, keyed by install path.
- **WindowsApps skip** — `ScanAllExesInDir`, `DetectGraphicsApi`, and `_DetectAllApisForCard` return immediately for `\WindowsApps\` paths (always access-denied, wasted time on retries).
- **WindowsApps addon/detection skip** — `ScanForInstalledAddon`, OptiScaler binary detection, and ReShade proxy DLL scanning also skip WindowsApps paths.
- **Debounced PCGW cache writes** — concurrent cache writes during startup are collapsed into a single disk write via a 500ms debounce timer.
- **Optimised OptiScaler detection** — scans only the 7 known proxy DLL names instead of every DLL in the game folder.
- **DLC blacklisting** — DLC content packs and launcher components are blacklisted from game detection to avoid wasted scanning.
- **Cache clearing** — Full Refresh (`forceRescan=true`) clears both API caches and rescans everything fresh.

---

## Data Storage

Everything under `%LOCALAPPDATA%\RHI\`:

| Path | Contents |
|------|----------|
| `game_library.json` | Detected games, hidden list, manually added games |
| `installed.json` | RenoDX mod install records (game name, path, addon filename, hash, snapshot URL, remote file size) |
| `aux_installed.json` | ReShade, ReLimiter, Display Commander, and OptiScaler install records (game name, path, addon type, installed filename, source URL) |
| `settings.json` | All settings, per-game overrides, and persisted filter mode |
| `ul_meta.json` | ReLimiter version metadata (per-bitness) |
| `dc_meta.json` | Display Commander version metadata |
| `api_cache.json` | PE-level graphics API detection cache (keyed by file path + last write time) |
| `game_api_cache.json` | Game-level API detection cache (keyed by install path) |
| `downloads\` | Cached downloads (separate files for 32-bit and 64-bit ReLimiter and DC) |
| `optiscaler\` | Staged OptiScaler release (DLL, companion files, default INI, version tag) |
| `optipatcher\` | Staged OptiPatcher release (`.asi` file, version tag) |
| `dlss\` | Staged DLSS DLLs (`nvngx_dlss.dll`, `nvngx_dlssd.dll`, `nvngx_dlssg.dll`) with independent version files |
| `addons\` | Downloaded ReShade addon files and version tracking (`versions.json`) |
| `addons_cache.ini` | Cached Addons.ini for offline fallback |
| `inis\` | Preset config files (`reshade.ini`, `reshade.vulkan.ini`, `relimiter.ini`, `DisplayCommander.ini`, `OptiScaler.ini`, etc.) |
| `reshade\` | Staged shader packs and custom shaders |
| `logs\` | Session logs (timestamped) and crash reports |

### Session Logging

A new session log file is created every time RHI starts, named with a timestamp (e.g. `session_2025-03-14_12-30-00.txt`). All activity is logged automatically. The Verbose Logging toggle in Settings enables additional detail.

### Log Pruning

Old session logs are pruned to keep a maximum of 10 on disk. The oldest logs are deleted first when the limit is exceeded.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Game not detected | Click **Add Game** on the Settings page or drag the game's `.exe` onto the window |
| Xbox games missing | Click **Refresh** — RHI uses the PackageManager API which may need a moment |
| ReShade not loading | Check the install path via 📁 — the ReShade DLL (`dxgi.dll`, `d3d9.dll`, or `opengl32.dll`) must be next to the game executable |
| ReShade not detected | If using a non-standard DLL name, RHI should detect it via binary signature scanning. Try **Refresh**. |
| Black screen (Unreal) | In ReShade → Add-ons → RenoDX, set `R10G10B10A2_UNORM` to `output size` |
| UE-Extended not working | Turn on in-game HDR — UE-Extended requires native HDR output |
| Downloads failing | Click **Refresh**, or clear cache from Settings → Open Downloads Cache |
| Foreign DLL blocking install | Choose **Overwrite** in the confirmation dialog, or cancel to keep the existing file |
| Games/mods out of sync | Settings → **Full Refresh** to clear all caches and re-scan |
| Drag-and-drop not working | Ensure RHI is running. Drag-and-drop works even as administrator (UIPI bypass). |
| Vulkan ReShade not showing as installed | Check that `reshade.ini` exists in the game folder. The Vulkan layer must also be installed globally. |
| Shaders missing after uninstall | Click **Refresh** — RHI will detect the missing shaders and redeploy them |
| Games showing as installed after manual file removal | Click **Refresh** — RHI verifies files exist on disk and cleans up stale records |
| ReLimiter OSD hotkey not working | Re-apply the hotkey from Settings — fixed in v1.8.2 for multi-word keys like Page Up |
| DLL override not applying from manifest | Click **Refresh** — manifest DLL overrides are applied on every refresh |

---

## Third-Party Components

| Component | Author | Licence |
|-----------|--------|---------|
| [ReShade](https://reshade.me) | Crosire | [BSD 3-Clause](https://github.com/crosire/reshade/blob/main/LICENSE.md) |
| [RenoDX](https://github.com/clshortfuse/renodx) | clshortfuse & contributors | [MIT](https://github.com/clshortfuse/renodx/blob/main/LICENSE) |
| [ReLimiter](https://github.com/RankFTW/ReLimiter) | RankFTW | Source-available |
| [Display Commander](https://github.com/lobotomyx/display-commander) | lobotomyx | Source-available |
| [RE Framework](https://github.com/praydog/REFramework-nightly) | praydog | [MIT](https://github.com/praydog/REFramework/blob/master/LICENSE) |
| [Luma Framework](https://github.com/Filoppi/Luma-Framework) | Pumbo (Filoppi) | Source-available |
| [OptiScaler](https://github.com/optiscaler/OptiScaler) | OptiScaler contributors | Source-available |
| [HtmlAgilityPack](https://github.com/zzzprojects/html-agility-pack) | ZZZ Projects Inc. | [MIT](https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | Microsoft / .NET Foundation | [MIT](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | Adam Hathcock | [MIT](https://github.com/adamhathcock/sharpcompress/blob/master/LICENSE.txt) |
| [7-Zip](https://www.7-zip.org/) | Igor Pavlov | [LGPL-2.1 / BSD-3-Clause](https://www.7-zip.org/license.txt) |
