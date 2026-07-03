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
- [UE-Extended Auto-Configuration](#ue-extended-auto-configuration)
- [Ryubing Emulator Support](#ryubing-emulator-support)
- [Frame Rate Limiters](#frame-rate-limiters)
- [DLSS & Streamline Manager](#dlss--streamline-manager)
- [DLSS & Streamline Defaults](#dlss--streamline-defaults)
- [Nvidia Profile Overrides](#nvidia-profile-overrides)
- [Multi Frame Generation](#multi-frame-generation)
- [Global Nvidia Settings](#global-nvidia-settings)
- [Admin Mode](#admin-mode)
- [Profile Export and Import](#profile-export-and-import)
- [OptiScaler](#optiscaler)
- [Shader Packs](#shader-packs)
- [ReShade Addon Management](#reshade-addon-management)
- [Game Launch](#game-launch)
- [HDR Auto-Toggle](#hdr-auto-toggle)
- [Running Game Indicator](#running-game-indicator)
- [Peak Brightness (Nits)](#peak-brightness-nits)
- [DOF Fix](#dof-fix)
- [Per-Game Overrides](#per-game-overrides)
- [ReShade Presets](#reshade-presets)
- [Nexus Mods and PCGamingWiki Links](#nexus-mods-and-pcgamingwiki-links)
- [UW Fix and Ultra+ Links](#uw-fix-and-ultra-links)
- [Vulkan ReShade Support](#vulkan-reshade-support)
- [DXVK](#dxvk)
- [Foreign DLL Protection](#foreign-dll-protection)
- [Drag-and-Drop](#drag-and-drop)
- [Addon Auto-Detection](#addon-auto-detection)
- [Update All](#update-all)
- [Auto-Update](#auto-update)
- [Remote Manifest](#remote-manifest)
- [Message of the Day](#message-of-the-day)
- [Performance](#performance)
- [Data Storage](#data-storage)
- [Troubleshooting](#troubleshooting)
- [Third-Party Components](#third-party-components)

---

## Layout and Views

RHI has three view modes, a Settings page, and an About page. Your chosen view, window size, and window position are remembered across restarts. Fresh installs default to Simple View.

### Detail View

A game list sidebar on the left, and a multi-section detail panel on the right. Selecting a game shows four sections:

1. **Components** — game info badges, install/update/uninstall buttons for each component
2. **Game Overrides** — per-game settings (DLL naming, shaders, addons, DXVK, RS channel, bitness, API)
3. **Nvidia Profile Overrides** — DLSS/Streamline management and driver profile settings (VSync, Latency, Smooth Motion, Power, ReBAR)
4. **Management** — change install folder, reset overrides, copy diagnostic report

### Grid View

A card-based layout showing all games as a grid of tiles. Each card shows the game name, platform icon, API badge, installed component dots, wiki status, and update highlights. Click a card to open a management popout with the same install/uninstall controls and overrides available in Detail View.

### Simple View

A paged layout showing the same content as Detail View, split across three navigable pages:

- **Page 1** — Components (game info, install buttons)
- **Page 2** — Game Overrides
- **Page 3** — Nvidia Profile Overrides + Management

Use the arrow buttons on the sides to cycle between pages. The window locks to a fixed compact size in Simple View and restores your previous size when you switch back.

### Toolbar

| Button | What it does |
|--------|-------------|
| Refresh | Re-scans your game library and fetches the latest mod info from all sources. |
| Shaders/Addons | Dropdown: Global Shaders (choose shader packs) and ReShade Addons (manage addon toggles). |
| Update All | Updates ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, and RE Framework across all eligible games. Lights up purple when updates are available. |
| Links | Dropdown with quick links: RenoDX Wiki, Luma Wiki, RHI GitHub, ReLimiter GitHub, Display Commander GitHub. |
| Help | Dropdown: Discord support channel, this guide, Ko-fi, and About page. |
| Views | Dropdown: Simple, Detail, and Grid view. |
| Settings | Opens the Settings page. |

### Sidebar (Detail View)

- **Search box** — filters games in real time. Matches across game name, store, engine, graphics API, bitness, mod name, mod author, and more. Type "UW Fix" or "Ultra+" to filter to games with those links.
- **Filter chips** — All Games, Favourites, Installed, Unreal, Unity, Other, RenoDX, Luma, Hidden. Your selected filter is saved and restored on reopen.
- **Custom filter chips** — click the "+" button next to the search bar to save any search query as a named chip. Custom chips use a teal colour scheme. Right-click to delete.
- **Game/installed counts** — shows how many games are visible and how many have mods installed.
- **Game entries** — each row shows a platform icon, game name, and a green dot when updates are available.

### Detail Panel

When a game is selected, the detail panel shows:

- **Header** — game name, Launch button (green ▶), Luma toggle, mod author badge(s) linking to Ko-fi where available.
- **Info card** — action buttons (Nexus Mods, PCGW, UW Fix, Ultra+ on the left; Hide, Favourite on the right) and badges for platform, engine, wiki status, graphics API, UE-Extended/Native HDR, and bitness.
- **Install path** — monospace text showing the resolved game directory.
- **Components section** — install/update/uninstall buttons for each component, with per-addon Info buttons.
- **Game Overrides section** — all per-game settings inline.
- **Nvidia Profile Overrides section** — DLSS/Streamline row and driver settings row.
- **Management section** — Change install folder, Reset folder, Reset Overrides, Copy Report.

### Status Bar

The bottom bar shows the game count and current operation on the left, a single-player warning in the centre, and the app version number with a Patch Notes link on the right.

---

## Settings Page

Click **Settings** in the toolbar. Click **Back to Games** to return. The page is organized into 9 labelled cards.

| Card | What's in it |
|------|-------------|
| Game Library | Add Game (manual detection), Check For Updates (bypass 4-hour cooldown). |
| ReShade & Display | Left: Screenshot path + subfolder combo + hotkeys + Apply to All. Right: Peak Brightness (Auto + nits) + HDR Auto-Toggle (Off/On) + Apply to All. |
| DLSS / Streamline Settings | Batch Deploy, Configure Defaults (versions/presets/render scales), On-Screen Indicator (Enabled/Disabled), Auto-Update DLSS (Off/On), Auto-Update Streamline (Off/On). |
| Global NVIDIA Driver Settings | Shader Cache Size, Shader Pre-Compile, G-Sync Mode, Preferred Refresh Rate, VSync, ReBAR + Size Limit, Export/Import/Reset Profiles, Clear Shader Cache. |
| Component Settings | Left: ReLimiter OSD hotkey + Shared Presets + DLSS Hooks + Apply. Right: OptiScaler GPU type + DLSS inputs + hotkey + Apply. |
| Shaders & Addons | Custom Shaders toggle, Cache All Shaders toggle, Addon Watch Folder + Browse + Reset. |
| Update & Deployment | Global Update Inclusion (per-component toggles), Mass INI Deployment (reshade.ini, relimiter.ini, DC.ini, OptiScaler.ini, Mass Preset Install). |
| System & Maintenance | Full Refresh, Purge Cache, Admin Mode (Off/On), Drop Helper (Off/On — disables the Discord drag-drop overlay, restart required). |
| Data & Folders | AppData Folder, Downloads Folder, Custom Folder, Logs Folder, Copy Logs. |

---

## Game Detection

RHI scans all supported stores on every launch and merges newly installed games into its cached library. Games on a disconnected drive are preserved in cache until reconnected. Per-store detection failures are isolated — one store failing won't block others.

### Supported Stores

| Store | How RHI finds games |
|-------|-------------------|
| Steam | Reads `libraryfolders.vdf` and `appmanifest_*.acf` files across all library folders. |
| GOG | Registry keys under `HKLM\SOFTWARE\GOG.com\Games`. |
| Epic Games | Manifest `.item` files in `ProgramData\Epic\EpicGamesLauncher\Data\Manifests`. |
| EA App | `installerdata.xml` manifests, registry keys, default EA Games folders, EA Desktop config. |
| Ubisoft Connect | Registry keys under `HKLM\SOFTWARE\Ubisoft\Launcher\Installs`, `settings.yml`, default folder. |
| Xbox / Game Pass | Windows `PackageManager` API with `MicrosoftGame.config` detection. Falls back to `.GamingRoot`, registry, and folder scanning. |
| Battle.net | Uninstall registry entries, `Battle.net.config`, default folder scanning. |
| Rockstar Games | Uninstall registry entries, launcher `titles.dat`, default folder scanning. |

### Engine Detection

| Engine | How it's detected |
|--------|------------------|
| Unreal Engine | Unreal-specific files and folder structures. Version detected from CrashReportClient or Build.version when available. |
| Unreal (Legacy) | Unreal Engine 3 games identified by legacy folder layouts. |
| Unity | `UnityPlayer.dll`, `Mono`, `MonoBleedingEdge`, `il2cpp`, `GameAssembly.dll`. |
| RE Engine | `re_chunk_000.pak` in the game directory. |
| Custom | Engine names from the remote manifest (e.g. "Silk Engine", "Frostbite"). |

### Adding Games Manually

- **Add Game** (Settings page) — click the button, pick the game's exe, then name it.
- **Drag and drop** — drag a game's `.exe` onto the RHI window. Engine and game root are auto-detected.

### Multi-Game Split

Games containing multiple titles in one folder (e.g. Mass Effect Legendary Edition) are split into separate entries via the remote manifest. Each sub-game gets independent mod management.

---

## Graphics API Detection

RHI scans game executables using PE header import table analysis. Results are cached to disk.

| API | Badge | What RHI looks for |
|-----|-------|--------------------|
| DirectX 8 | DX8 | `d3d8.dll` import |
| DirectX 9 | DX9 | `d3d9.dll` import |
| DirectX 10 | DX10 | `d3d10.dll` / `d3d10_1.dll` import |
| DirectX 11/12 | DX11/12 | `d3d11.dll` / `d3d12.dll` import |
| Vulkan | VLK | `vulkan-1.dll` import |
| OpenGL | OGL | `opengl32.dll` import |

The detected API drives automatic ReShade DLL naming:
- DX9 → `d3d9.dll`
- OpenGL → `opengl32.dll`
- Default → `dxgi.dll`

Dual-API games show both APIs (e.g. `DX11/12 / VLK`). Per-game API overrides and manifest overrides are available for games where PE detection fails.

---

## Components

The detail panel Components section shows rows for each managed mod:

| Component | Description |
|-----------|-------------|
| RE Framework | Required for RE Engine games. Shown only for those games. |
| ReShade | Core injection framework. Install/Reinstall/Update, Copy INI, Uninstall. |
| RenoDX | HDR mod addon. Install/Update, Info, Uninstall. |
| Luma | Luma Framework. Shown only in Luma mode. |
| ReLimiter | Frame pacing addon. Install, Info, Copy INI, Uninstall. |
| Display Commander | Alternative frame limiter. Install, Info, Copy INI, Uninstall. |
| OptiScaler | Upscaler redirection. Install, Info, Copy INI, Uninstall. |
| DXVK | DirectX-to-Vulkan. Managed via the DXVK dropdown in Overrides. |

### Per-Addon Info Buttons

Every component has an **Info** button showing game-specific context:

1. **Manifest notes** — game-specific notes from the remote manifest
2. **Wiki content** — compatibility data from the relevant wiki
3. **Generic description** — what the addon does in general

Info buttons are highlighted **blue** when they have content. ReLimiter and Display Commander also show changelogs.

### Version Display

Each component shows its installed version number. Purple text indicates an available update.

### Dependency Enforcement

- **ReShade required** — RenoDX, ReLimiter, Display Commander require ReShade first
- **RE Framework required** — RE Engine games need RE Framework before ReShade

---

## ReShade

[ReShade](https://reshade.me) is the core injection framework. RHI downloads the latest build on startup and stages it locally.

### Build Channels

The RS Channel dropdown in per-game overrides offers:

| Channel | Description |
|---------|-------------|
| Global | Uses the global default (Stable or Nightly from Settings). |
| Stable | Official reshade.me releases. |
| Nightly | GitHub Actions nightly builds. |
| Custom | Your own ReShade DLLs from `%LocalAppData%\RHI\Custom\ReShade\`. |
| Legacy | Pin to a specific older version (6.0.0+). Opens a version picker. |
| No Addons | Standard ReShade without addon support. Disables all addon rows. |

Per-game channel overrides let you mix channels across your library. Legacy and Custom exclude games from ReShade update checks automatically.

### Vulkan Games

Vulkan games use a global implicit layer instead of a per-game DLL. See [Vulkan ReShade Support](#vulkan-reshade-support).

### Copy INI

Copies `reshade.ini` from the template folder to the game directory, preserving existing game-specific settings.

### Foreign DLL Detection

Before installing, RHI checks if an existing file belongs to another tool (DXVK, Special K, ENB) via binary signature scanning. A confirmation dialog appears before overwriting.

---

## RenoDX

[RenoDX](https://github.com/clshortfuse/renodx) is an HDR mod framework running as a ReShade addon.

### How Mods Are Matched

RHI fetches the RenoDX wiki on startup and matches detected games by name. Games without a named mod fall back to generic engine addons (Unreal Engine, Unity, UE-Extended). The manifest can override name matching.

### UE-Extended Toggle

Unreal Engine games show a toggle to switch between the game's specific named mod and the generic UE-Extended addon. Toggling uninstalls the current addon and switches mode — the user clicks Install to get the new one.

### External-Only Games

Some games are redirected to Nexus Mods or Discord via the manifest. The install button opens the external link.

### HDR Gaming Database

The RenoDX Info button links to [hdrmods.com](https://www.hdrmods.com) entries where available.

---

## RE Framework

[RE Framework](https://github.com/praydog/REFramework-nightly) is required for ReShade injection on RE Engine games.

One-click install with game-specific builds. Version tracking and auto-update included. When OptiScaler is active on supported RE Engine games, the pd-upscaler branch is automatically used instead.

---

## Luma Framework

[Luma Framework](https://github.com/Filoppi/Luma-Framework) is a DX11 HDR modding framework.

### Luma Mode

A toggle in the Components header switches between RenoDX mode and Luma mode. When enabled:
- RenoDX and standard ReShade rows are hidden
- The Install Luma button appears
- ReLimiter and Display Commander remain available

### Luma + RenoDX Coexistence

Games in the manifest `lumaRenodxCompat` list can run both simultaneously. The RenoDX row stays visible in Luma mode for these games.

### Drag-and-Drop Install

Drag a Luma mod archive (zip/7z) onto the window. RHI detects it by the `Luma/d3dcompiler_47*.dll` marker inside. A game picker opens, and the mod is extracted with shader deployment and reshade.ini configuration.

### Luma Updates

Luma mods are checked for updates and included in Update All.

---

## UE-Extended Auto-Configuration

When installing UE-Extended (generic Unreal Engine RenoDX), two files are configured automatically:

### reshade.ini

A `[renodx]` section with `Set_Path=0` and all `Upgrade_*=0` keys. Tells RenoDX to upgrade the game's native HDR path. Only adds missing keys — never overwrites existing values. Removed on UE-Extended uninstall.

### Engine.ini

HDR settings (`r.AllowHDR=1`, `r.HDR.EnableHDROutput=1`, etc.) deployed to the game's config folder (`%LocalAppData%\{ProjectName}\Saved\Config\{Platform}\`). File set read-only to prevent the engine from overwriting. The Config button in the detail panel opens the resolved folder.

---

## Ryubing Emulator Support

Drag `Ryujinx.exe` into RHI to add Ryubing. It appears as a single card managing Switch game addons.

### Setup

1. Place RenoVK DLL in `%LocalAppData%\RHI\Custom\ReShade\` as `ReShade64.dll`
2. Set RS Channel to Custom in Overrides
3. Install ReShade (Vulkan layer — requires admin)
4. Click Install RenoDX — downloads all 9 Souperman9 addons

All addons are deployed simultaneously. Each reads the emulator log to detect which game is running. No swapping needed between games.

---

## Frame Rate Limiters

Two mutually exclusive frame limiters. Only one can be installed per game.

### ReLimiter

Frame pacing addon with configurable OSD hotkey and shared presets. 64-bit only (v3.0.0+). Default `relimiter.ini` is seeded on first install.

### Display Commander

Alternative limiter supporting both 32-bit and 64-bit. DLL naming overrides toggle controls both ReShade and DC filenames together.

---

## DLSS & Streamline Manager

For games with DLSS or Streamline DLLs detected, the DLSS/Streamline section appears in Nvidia Profile Overrides.

### Version Swapping

| Component | DLL | Notes |
|-----------|-----|-------|
| DLSS SR | `nvngx_dlss.dll` | Choose any version independently |
| DLSS RR | `nvngx_dlssd.dll` | Choose any version independently |
| DLSS FG | `nvngx_dlssg.dll` | Choose any version independently |
| Streamline | `sl.*.dll` | All files updated as a set |

Versions are downloaded on-demand and cached at `%LocalAppData%\RHI\DLSS\{version}\`, `DLSS-D\{version}\`, `DLSS-G\{version}\`, `Streamline\{version}\`.

### Backups & Restore

Original DLLs are backed up with `.original` extension. Select the item marked with `(D)` in the dropdown to restore. "Restore DLSS/SL" button reverts all components and resets presets.

### DLSS Presets

Per-game presets written directly to NVIDIA driver profiles:

| Component | Presets |
|-----------|---------|
| SR | Default, J, K, L, M |
| RR | Default, D, E |
| FG | Default, A, B |

No NVIDIA Profile Inspector needed. Changes apply instantly. Silently no-ops on AMD/Intel.

### DLSS Render Scale Override

Force a custom render resolution per game (33–100%) for both SR and Ray Reconstruction. Options range from 100% DLAA down to 33% Ultra Performance.

### Custom Files

Place custom DLLs in `%LocalAppData%\RHI\Custom\DLSS\` and `Custom\Streamline\`. Select "Custom" from the dropdown.

### Driver Override Detection

When NVIDIA App has "Latest DLL" or "Use recommended preset" active, RHI detects it and greys out the affected dropdown with a "Driver Override Active" warning.

### Batch Deploy

From Settings: select games via checklist, pick versions for each component, select presets, and deploy to all selected games at once. Auto-creates NVIDIA profiles for games without one. Games with v1.x DLLs are shown disabled.

---

## DLSS & Streamline Defaults

Configure preferred default versions, presets, and render scales from the Settings page. The "Configure Defaults" button opens a 4-column dialog (SR | RR | FG | SL) with:

- **Version** dropdown per component
- **Preset** dropdown (SR/RR/FG)
- **Render Scale** dropdown (SR/RR only)

"Default (don't change)" as the first option means that component is skipped during Quick Apply.

### Quick Apply

A button in the DLSS/Streamline section of the per-game Nvidia Profile Overrides panel. Applies all non-default settings from your configured defaults to the selected game in one click. Downloads versions on-demand. Skips components the game doesn't have and components with driver overrides active.

---

## Nvidia Profile Overrides

The Nvidia Profile Overrides section appears below Game Overrides in the detail panel. It has two rows:

### DLSS / Streamline Row

Horizontal columns for SR, RR, FG, and SL. Each column shows:
- Version dropdown (managed versions + Custom + Default)
- Preset dropdown
- Render Scale dropdown (SR/RR only)

Plus Quick Apply button and Restore DLSS/SL button.

Only visible when the game has DLSS or Streamline files detected.

### Driver Settings Row

Per-game NVIDIA driver profile settings in 4 columns:

| Column | Settings |
|--------|----------|
| VSync | VSync Mode, VSync Tear Control, Low Latency (Off/On/Ultra) |
| Smooth Motion | Enable (Off/On), Allowed APIs, Flip Pacing FS, Flip Pacing Win |
| Power | Power Mode, Restore Defaults button |
| ReBAR | Enable (Off/On/Global), Mode (Standard/Optimized), Size Limit |

All settings are written per-game to the NVIDIA driver profile. **Requires admin privileges** — the entire row is greyed out when not running as admin. Enable Admin Mode in Settings for persistent elevation.

---

## Multi Frame Generation

A "Multi Frame Gen" button in the FG column opens a per-game dialog for RTX 50 Series GPUs:

| Setting | Options |
|---------|---------|
| FG Mode | Default / Fixed / Dynamic |
| Frame Count | Fixed: 2x–6x. Dynamic: Up to 2x–6x |
| Target Frame Rate | Off / Max Refresh Rate / 60–500 FPS (Dynamic only) |

A first-time warning explains the RTX 50 Series requirement (driver 572.16+ for Fixed, 595.97+ for Dynamic). Restore Defaults clears MFG settings.

---

## Global Nvidia Settings

Located in the Settings page, these write to the global/base NVIDIA driver profile (affects all games):

| Setting | Options |
|---------|---------|
| Shader Cache Size | Off, 128MB–100GB, Unlimited |
| Shader Pre-Compile | Disabled, Low, Medium, High |
| G-Sync Mode | Off, Fullscreen only, Fullscreen / Windowed |
| Preferred Refresh Rate | Application Setting, Highest Available |
| Global ReBAR Enable | Off / On (requires admin) |
| Global ReBAR Size | 512MB, 1GB (default), 1.5GB, 2GB, 4GB, 6GB |
| DLSS On-Screen Indicator | Enabled / Disabled (registry-based) |

When Global ReBAR is enabled, per-game ReBAR dropdowns show "Global (On/Off)" and "Global (1GB)" as the first option — selecting it inherits from global.

---

## Admin Mode

Task Scheduler-based persistent elevation. Located in Settings → Data & Custom Files.

- **Off** — RHI runs as a normal user. ReBAR, Low Latency (ULL), Smooth Motion, and some driver settings cannot be written.
- **On** — Creates a scheduled task named "RHI Admin Mode". On subsequent launches, RHI silently relaunches through the task with admin privileges — no UAC prompt each time.

Toggling On triggers a one-time UAC prompt to create the task. Toggling Off deletes it. Drag-and-drop continues to work when elevated (UIPI bypass).

---

## Profile Export and Import

Back up all per-game NVIDIA driver profile settings to a JSON file. Located in the Settings page (Global Nvidia Settings section).

### Export

Scans all games in RHI, finds their matching NVIDIA profiles, and saves all settings to `%LocalAppData%\RHI\nvidia_profiles_backup.json`. Includes global profile settings (Shader Cache, G-Sync, Refresh Rate, ReBAR). Only profiles for games in RHI are exported — not all 7800+ driver profiles.

### Import

Reads the backup file, creates missing profiles, registers exe associations, and applies all saved settings. ReBAR settings are applied via elevated helper to ensure they persist. The session count of imported profiles is reported on completion.

Use case: restore your per-game NVIDIA settings after a driver update wipes custom profiles.

---

## OptiScaler

[OptiScaler](https://github.com/optiscaler/OptiScaler) is a 64-bit middleware that redirects upscaler calls between DLSS, FSR, and XeSS.

### Install

One-click install deploys OptiScaler.dll (renamed to a proxy DLL), companion files, DLSS DLLs, and OptiPatcher (AMD/Intel only). A first-time warning explains its purpose.

### ReShade Coexistence

When both are installed, ReShade is renamed to `ReShade64.dll`. OptiScaler loads it via `LoadReshade=true`. Uninstalling OptiScaler restores the original ReShade filename.

### DLL Naming

OptiScaler.dll is renamed to: user DLL override → manifest override → `winmm.dll` (Vulkan) → `dxgi.dll` (default).

### OptiScaler Wiki

The Info button shows compatibility data from the OptiScaler wiki (Working/Partially Working/Not Working, supported upscalers, notes).

### DLSS Auto-Download

Latest DLSS SR, RR, and FG DLLs are staged automatically from the RHI DLSS manifest on startup.

---

## Shader Packs

RHI maintains 46 ReShade shader packs, deployed per-game from a shared staging folder.

### Categories

- **Essential** — Lilium HDR Shaders (required for HDR tone mapping). Selected by default.
- **Recommended** — Core packs: crosire reshade-shaders, PumboAutoHDR, smolbbsoop, MaxG2D Simple HDR, clshortfuse shaders, potatoFX.
- **Extra** — Community packs covering colour grading, film emulation, CRT simulation, VR tools, screen-space effects, and more.

### Per-Game Shader Overrides

| Mode | Behaviour |
|------|-----------|
| Global | Uses the global shader selection |
| Select | Opens a picker for game-specific packs |
| Custom | Uses shaders from your custom directories |
| Off | No managed shaders deployed |

### Custom Shaders

Place custom shaders in `%LocalAppData%\RHI\reshade\Custom\Shaders\` and textures in `Custom\Textures\`. Enable the Custom Shaders toggle in Settings.

### Shader Cache

Toggle in Settings. When enabled, all packs are pre-downloaded on startup. When disabled, packs fetch on-demand when needed.

---

## ReShade Addon Management

A curated addon manager for ReShade addons from the official Addons.ini list.

### Global Toggles

Click "ReShade Addons" in the toolbar. Toggle addons On/Off globally. Enabled addons are deployed to all games with ReShade installed.

### Per-Game Addon Overrides

| Mode | Behaviour |
|------|-----------|
| Global | Uses the globally enabled set |
| Select | Per-game addon picker |
| Off | Removes all managed addons |

### Special Addons

- **RenoDX DevKit** — development tool for mod authors
- **DLSS Fix** — makes ReShade draw on native game frames. Auto-configures reshade.ini with DLSS/Streamline paths.

---

## Game Launch

Launch games from the green "▶ Launch" button or by double-clicking in the sidebar.

### Priority Chain

1. User exe override (Overrides panel)
2. Manifest exe override
3. Steam `-applaunch` (with overlay and playtime tracking)
4. Epic protocol URL (when no launch args set)
5. Direct exe (largest .exe in install path)

### Launch Arguments

Set per-game command-line arguments in the Overrides panel. Steam games pass args via `-applaunch`. Epic protocol is skipped when args are set.

### HDR Auto-Toggle

Automatically enables Windows HDR when a game is launched through RHI and disables it when the game exits. Useful for users who keep their desktop in SDR.

- **Global setting**: Off/On in the ReShade & Display card on the Settings page.
- **Per-game override**: "HDR" button next to Launch — purple when active, grey when inactive. Click to flip.
- **Process monitoring**: For direct exe launches, monitors the process handle directly. For Steam/Epic protocol launches, polls for a process running from the game's install path (1-second intervals, up to 60 seconds to find it).
- **Always enables**: The toggle always calls EnableHdr regardless of current state (detection is unreliable on some HDR-capable displays).
- **Disables on exit**: Only when the game process exits. If the app can't find the game process (protocol launch timeout), HDR stays on.

### Running Game Indicator

The sidebar item turns green when a game launched through RHI is currently running. Returns to normal when the game exits. Only tracks games launched via the Launch button (not externally launched games).

### Peak Brightness (Nits)

Set your monitor's peak brightness once in the Settings page (ReShade & Display → HDR & Peak Brightness). The "Auto" button reads your monitor hardware. The value is written as `ToneMapPeakNits` to all `[renodx-preset*]` sections in reshade.ini on every deploy — installs, updates, mass deploy, and INI redeploy.

### DOF Fix

One-click install for Unreal Engine 5.0–5.6 games that have depth-of-field stepping/tiling artifacts (common on NVIDIA GPUs). Appears as a component row in the Optional section.

- Eligibility: UE 5.0–5.6 detected (or forced via manifest `dofFixForceGames`), 64-bit only.
- Participates in Update All.
- Engine badge toggle: click the "Unreal Engine" badge to force UE5 eligibility on Game Pass games where version detection fails.
- Manifest-forced games (e.g. Clair Obscur, Avowed) have the badge locked — can't be toggled off.
- Toggling the badge OFF uninstalls the DOF Fix addon if it was installed.

---

## Per-Game Overrides

All controls save immediately when changed.

| Override | Description |
|----------|-------------|
| Game name | Editable — rename persists across restarts |
| Wiki mod name | Match to a different wiki entry |
| Wiki exclusion | Exclude from wiki lookups entirely |
| DLL naming | Toggle enables both ReShade and DC filename overrides |
| Update Inclusion | Per-component toggles (RS, RDX, UL, DC, OS, REF) |
| Shader Mode | Global / Select / Custom / Off |
| Addon Mode | Global / Select / Off |
| Bitness | Auto / 32-bit / 64-bit |
| Graphics API | Auto / DX8 / DX9 / DX10 / DX11/12 / Vulkan / OpenGL |
| RS Channel | Global / Stable / Nightly / Custom / Legacy / No Addons |
| DXVK | Off / Development / Stable / Lilium HDR |
| Launch executable | Custom exe path |
| Launch arguments | Command-line args for the game |
| HDR auto-toggle | Per-game "HDR" button next to Launch — overrides the global HDR setting |
| Config button | Opens the Engine.ini config folder |

### Reset Overrides

Clears all per-game settings back to defaults (including DXVK, RS channel, shader/addon modes, bitness, API, DLL names, launch settings).

### Bitness Override Auto-Uninstall

Changing the bitness override when components are installed triggers automatic uninstall of all components (ReShade, DC, RenoDX, ReLimiter, OptiScaler, DXVK, RE Framework, Luma) to provide a clean slate for the new bitness.

---

## ReShade Presets

### Preset Folder

Place `.ini` presets in `%LocalAppData%\RHI\inis\reshade-presets\`. The "Select ReShade Preset" button in Overrides lists files with checkboxes. Click Deploy to copy selected presets to the game folder.

### Drag-and-Drop

Drag a ReShade preset onto the window. RHI validates it, saves it, deploys to a chosen game, and offers to auto-install required shader packs.

### Mass Preset Install

From Settings: select presets, choose target games, and optionally install required shader packs.

---

## Nexus Mods and PCGamingWiki Links

### Nexus Mods

RHI fetches the public Nexus game catalogue and matches games by name. Clickable button on the detail panel info card.

### Nexus Update Alerts

Automatic update detection via GraphQL API (no API key). When a Nexus-hosted mod is updated, the card shows purple with "Update RenoDX" that opens the Nexus page.

### PCGamingWiki

Resolved via Steam AppID. Falls back to OpenSearch when no AppID is available. Clickable button on the info card.

---

## UW Fix and Ultra+ Links

### UW Fix

Ultrawide/resolution fix links from Lyall, RoseTheFlower, and p1xel8ted. Button appears when a fix is available. Search "UW Fix" to filter.

### Ultra+

Ultra+ mod links from theultraplace.com. Button appears when available. Search "Ultra+" to filter.

---

## Vulkan ReShade Support

### How It Works

ReShade is installed as a global Vulkan implicit layer via `C:\ProgramData\ReShade\`. A per-game `reshade.ini` and `RDXC_VULKAN_FOOTPRINT` marker enable managed shader deployment.

### Lightweight Install

When the layer is already registered, clicking Install on a Vulkan game is instant (INI + footprint + shaders only, no admin needed).

### Dual-API Games

Games with DirectX + Vulkan show a rendering path toggle. Switching modes reinstalls ReShade in the correct mode.

---

## DXVK

DXVK translates DirectX 8/9/10 to Vulkan. Managed as a per-game component.

### Enabling

The DXVK dropdown appears in Overrides for DX8, DX9, and DX10 games. Options:

| Option | Behaviour |
|--------|-----------|
| Off | DXVK disabled / uninstalled |
| Development | Nightly builds from doitsujin/dxvk |
| Stable | Tagged releases |
| Lilium HDR | EndlesslyFlowering fork with scRGB HDR output |

Selecting any variant triggers the install flow. Selecting Off uninstalls.

### DX8/DX9 Games

- **Development/Stable** — DXVK deployed as `dxgi_dxvk.dll` in proxy mode. ReShade stays as `d3d9.dll` with a `[PROXY]` chain.
- **Lilium HDR** — DXVK deployed as `d3d9.dll` directly. ReShade uses the Vulkan layer. Enables SM5 HDR shaders. On uninstall, local ReShade is restored automatically.

### DX10/DX11 Games

Standard mode. ReShade switches to Vulkan layer. DXVK DLLs go in the game root (or OptiScaler plugins folder for coexistence).

### Anti-Cheat Blacklist

Games with known anti-cheat are blacklisted via the manifest. The dropdown is disabled.

---

## Foreign DLL Protection

Before overwriting an existing DLL, RHI checks if it belongs to DXVK, Special K, ENB, or another tool via binary signature scanning. A confirmation dialog appears. During Update All, foreign DLLs are silently skipped.

---

## Drag-and-Drop

RHI supports drag-and-drop for adding games and installing mods. Works even when elevated.

| File type | What happens |
|-----------|-------------|
| `.exe` | Opens add-game dialog with auto-detection |
| `.addon64` / `.addon32` | Install dialog with game picker |
| `.zip`, `.7z` (Luma archive) | Luma install with game picker |
| `.zip`, `.7z`, `.rar` etc. | Extract and find addon files |
| `.ini` (ReShade preset) | Validate, save, deploy, offer shader install |
| URL (`.url` shortcut) | Download and process as addon |

---

## Addon Auto-Detection

### File Watcher

Monitors your Downloads folder (configurable) for `renodx-*.addon64`, `renodx-*.addon32`, and archives with "renodx" or "luma" in the filename. Prompts to install when detected.

### Named Pipe Forwarding

Double-clicking an addon in Explorer opens RHI (or forwards to the running instance) and triggers install.

---

## Update All

Updates ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, and RE Framework across all eligible games. Per-game exclusions are respected. Foreign DLLs are silently skipped.

### Cooldown

4-hour cooldown between update checks. Full Refresh bypasses it.

### Rate Limit Handling

GitHub API 403 responses cancel all remaining API calls for the session.

---

## Auto-Update

RHI checks for new versions on launch via GitHub Releases API. Beta Opt-In checks both stable and beta releases.

---

## Remote Manifest

Fetched from GitHub on every launch. Provides game-specific overrides without app updates:

- Game blacklist, install path overrides, wiki name mapping
- Engine/API/bitness overrides, DLL name overrides
- Game notes (per-component), forced external links
- DLSS skip lists, launch exe overrides, split game definitions
- Legacy ReShade versions, install warnings
- Shader pack and addon pack overrides
- NVIDIA profile exe exclusions

---

## Message of the Day

A message fetched from `motd.md` on GitHub. Shown as a dialog once per unique message (tracked by SHA256 hash). Empty file = no dialog.

---

## Performance

- **Instant launch from cache** — game list loads immediately, full scan runs in background
- **Parallel shader pack checks and deployments**
- **PE-level and game-level API caches**
- **DLSS trusted path cache** — after 3 confirmations, fast `File.Exists` checks replace recursive scans
- **DLSS skip cache** — games confirmed without DLSS are auto-exempted
- **4-hour update cooldown** with rate-limit detection
- **NVIDIA profile lookup cache** — expensive profile matching runs once per game per session
- **Manifest skip lists** — skip known DLSS-free games immediately

---

## Data Storage

Everything under `%LocalAppData%\RHI\`:

| Path | Contents |
|------|----------|
| `game_library.json` | Detected games, hidden list, manually added games |
| `installed.json` | RenoDX mod install records |
| `aux_installed.json` | ReShade, ReLimiter, DC, OptiScaler install records |
| `settings.json` | All settings and per-game overrides |
| `downloads\` | Cached downloads (shaders, renodx, luma, misc) |
| `DLSS\{ver}\`, `DLSS-D\{ver}\`, `DLSS-G\{ver}\`, `Streamline\{ver}\` | Versioned DLL caches |
| `Custom\DLSS\`, `Custom\Streamline\`, `Custom\ReShade\` | User-provided DLLs |
| `reshade\` | Staged shader packs and custom shaders |
| `reshade-nightly\` | Nightly ReShade DLLs |
| `dxvk-development\`, `dxvk-stable\`, `dxvk-lilium\` | DXVK staging per variant |
| `LegacyReshade\{ver}\` | Legacy ReShade versions |
| `optiscaler\` | OptiScaler staging |
| `addons\` | Downloaded addon files + versions.json |
| `logs\` | Session logs (max 10 kept) |
| `nvidia_profiles_backup.json` | Profile Export data |
| `nexus_baselines.json` | Nexus update tracking |
| `dlss_trusted_paths.json` | DLSS fast-detection cache |
| `dlss_scan_cache.json` | DLSS skip cache |

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Game not detected | Add Game in Settings or drag the exe onto the window |
| Xbox games missing | Click Refresh |
| ReShade not loading | Check install path via 📁 — DLL must be next to the game exe |
| Black screen (Unreal) | ReShade → Add-ons → RenoDX → set R10G10B10A2_UNORM to output size |
| UE-Extended not working | Enable HDR in the game's display settings first |
| Downloads failing | Click Refresh, or clear cache from Settings |
| DLSS presets not applying | Enable Admin Mode in Settings |
| Driver settings greyed out | Enable Admin Mode — requires elevation |
| Everything out of sync | Settings → Full Refresh |

---

## Third-Party Components

| Component | Author | Licence |
|-----------|--------|---------|
| [ReShade](https://reshade.me) | Crosire | [BSD 3-Clause](https://github.com/crosire/reshade/blob/main/LICENSE.md) |
| [RenoDX](https://github.com/clshortfuse/renodx) | clshortfuse & contributors | [MIT](https://github.com/clshortfuse/renodx/blob/main/LICENSE) |
| [ReLimiter](https://github.com/RankFTW/ReLimiter) | RankFTW | Source-available |
| [Display Commander](https://github.com/pmnoxx/display-commander) | pmnoxx | [GPL-3](https://github.com/pmnoxx/display-commander/blob/main/LICENSE) |
| [RE Framework](https://github.com/praydog/REFramework-nightly) | praydog | [MIT](https://github.com/praydog/REFramework/blob/master/LICENSE) |
| [Luma Framework](https://github.com/Filoppi/Luma-Framework) | Pumbo (Filoppi) | Source-available |
| [OptiScaler](https://github.com/optiscaler/OptiScaler) | OptiScaler contributors | Source-available |
| [DXVK](https://github.com/doitsujin/dxvk) | doitsujin & contributors | [Zlib](https://github.com/doitsujin/dxvk/blob/master/LICENSE) |
| [DXVK HDR-mod](https://github.com/EndlesslyFlowering/dxvk) | EndlesslyFlowering (Lilium) | [Zlib](https://github.com/EndlesslyFlowering/dxvk/blob/HDR-mod/LICENSE) |
| [7-Zip](https://www.7-zip.org/) | Igor Pavlov | [LGPL-2.1 / BSD-3-Clause](https://www.7-zip.org/license.txt) |
