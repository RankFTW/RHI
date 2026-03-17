# RenoDX Commander — Detailed Guide

This document covers everything RDXC does in depth. For a quick overview, see the [README](../README.md).

---

## Layout

RDXC uses a single unified layout: a game list sidebar on the left and a detail panel on the right. Global settings live on a dedicated Settings page.

### Toolbar

| Control | Function |
|---------|----------|
| **RDXC logo + title** | App branding |
| **Add Game** | Manually add a game folder |
| **Refresh** | Rescan game library and fetch latest mod info |
| **Deploy...** | Flyout menu: Deploy DC Mode to All, Deploy Shaders to All, Update All RenoDX / ReShade / Display Commander |
| **Support** | Open the RDXC support channel on Discord |
| **Settings** | Navigate to the Settings page |

### Game List Sidebar

- **Search box** — filters games in real-time as you type
- **Filter chips** — Favourites, All Games, Unreal, Unity, Other, RenoDX, Luma, Hidden
- **Game/installed counts** — how many games are visible and how many have mods installed
- **Game list** — each entry shows a platform icon, game name, and a green dot if updates are available

Selecting a game loads its details in the panel to the right.

### Detail Panel

When a game is selected, the detail panel shows:

- **Game name** with badges for platform source, engine type, wiki status, UE-Extended / Native HDR
- **Install path** in monospace text
- **Components table** — ReShade, Display Commander, RenoDX, and Luma (when applicable), each with status, install/reinstall/update button, options menu, and uninstall button
- **Overrides section** — all per-game settings inline with descriptions
- **Utility buttons** — favourite, discussion link, game info/notes, hide/unhide, and a folder menu (open in Explorer, change install folder, reset/remove game)

When no game is selected, the panel shows a placeholder prompting you to pick one.

---

## Settings Page

Click **Settings** in the toolbar to open the Settings page. Click **← Back to Games** to return.

| Section | Contents |
|---------|----------|
| **Display Commander Mode** | DC Mode selector (Off / Mode 1 / Mode 2) with inline explanation, plus Deploy DC Mode to All button |
| **Shader Deploy Mode** | Shader mode selector (Off / Minimum / All / User) with inline explanation, plus Deploy Shaders to All button |
| **Preferences** | Skip Update Check toggle, Verbose Logging toggle — each with inline description |
| **Full Refresh** | Clears all caches and re-scans everything from disk |
| **Crash & Error Logs** | Open Logs Folder and Open Downloads Cache buttons, with paths shown inline |
| **About** | Version, author, disclaimer |
| **Credits** | Third-party components with licences and links |
| **Links** | External links to RenoDX, ReShade, Display Commander, Luma, Discord channels, etc. |

All settings apply immediately — no separate save action required.

---

## Game Detection

RDXC re-scans all stores on every launch and merges newly installed games into its cached library automatically.

| Store | Detection Method |
|-------|-----------------|
| **Steam** | Reads `libraryfolders.vdf` and `appmanifest_*.acf` files across all library folders |
| **GOG** | Registry keys under `HKLM\SOFTWARE\GOG.com\Games` |
| **Epic Games** | Manifest `.item` files in `ProgramData\Epic\EpicGamesLauncher\Data\Manifests` |
| **EA App** | `installerdata.xml` manifests, registry keys (`Origin Games`, `EA Games`, `Criterion Games`, `Respawn`, `BioWare`, `DICE`, `PopCap`, `Ghost Games`), default EA Games folders, and EA Desktop local config path discovery |
| **Ubisoft Connect** | Registry keys under `HKLM\SOFTWARE\Ubisoft\Launcher\Installs`, `settings.yml` game installation path, and default Ubisoft Game Launcher games folder |
| **Xbox / Game Pass** | Windows `PackageManager` API — identifies games by `MicrosoftGame.config` presence. Falls back to `.GamingRoot` file parsing, registry, and folder scanning |
| **Battle.net** | Windows Uninstall registry entries (filtered by Blizzard/Activision publisher), `Battle.net.config` default install path, and default folder scanning |
| **Rockstar Games** | Windows Uninstall registry entries (filtered by Rockstar publisher), launcher `titles.dat` install paths, and default folder scanning |

Games on a disconnected drive are preserved in the cache until the drive is reconnected.

### Adding Games Manually

- **Add Game** button — enter the game name and pick the install folder.
- **Drag and drop** — drag a game's `.exe` onto the RDXC window. RDXC detects the engine type (Unreal, Unity, or Unknown), infers the game root folder by recognising store markers and engine layouts, and guesses the game name from folder structure. A confirmation dialog lets you edit the name before adding.

### Drag-and-Drop Addon Install

Dragging a `.addon64` or `.addon32` file onto the window opens an install dialog. A game picker lets you choose the target game — RDXC auto-selects a match based on words in the addon filename. If a RenoDX addon already exists, the dialog warns it will be replaced.

---

## Components

The detail panel shows a Components section with up to four rows:

| Row | Component | Controls |
|-----|-----------|----------|
| **ReShade** | ReShade | Install / Reinstall / Update — ⋯ menu (Copy INI) — ✕ Uninstall |
| **Display Commander** | Display Commander | Install / Reinstall / Update — ⋯ menu (Copy TOML) — ✕ Uninstall |
| **RenoDX** | RenoDX Mod | Install / Reinstall / Update — UE Extended options — ✕ Uninstall |
| **Luma** | Luma Framework | Install / Uninstall (shown only in Luma mode) |

---

## DC Mode

DC Mode controls how Display Commander loads alongside ReShade. Configure it on the Settings page.

| Mode | ReShade | DC Installed As |
|------|---------|-----------------|
| **OFF** (default) | `dxgi.dll` in the game folder | `zzz_display_commander.addon64` |
| **Mode 1** | Not in game folder — DC loads ReShade from shared folder | `dxgi.dll` |
| **Mode 2** | Not in game folder — DC loads ReShade from shared folder | `winmm.dll` |

When DC Mode is active, ReShade is synced to `%LOCALAPPDATA%\Programs\Display_Commander\Reshade\` and Display Commander loads it from there at runtime. Per-game ReShade installs are removed automatically. Individual games can override the global DC Mode via the Overrides section.

### Why DC Mode is Recommended

Loading DC as `dxgi.dll` is the preferred method. As explained by pmnoxx, loading DC as a ReShade addon causes it to hook too late, breaking several features:

1. **Streamline / DLSS integration** — DLSS file swapping and settings control require early hooking
2. **FPS limiter** — Direct D3D11/D3D12 hooking doesn't work as an addon in games without native Reflex
3. **VSync control** — Toggling VSync fails when a RenoDX addon is also loaded (ReShade limitation)
4. **Flip swapchain upgrade** — Crash-free swapchain upgrades require `dxgi.dll` loading
5. **ASI loader** — Some addons require DC to be loaded as `dxgi.dll`
6. **General load order** — DLL load order is unpredictable; addon loading can break DC features

See the full [Display Commander feature list](https://github.com/pmnoxx/display-commander?tab=readme-ov-file#features).

---

## Foreign DLL Protection

When installing ReShade or DC as `dxgi.dll` (or `winmm.dll` in Mode 2), RDXC checks whether an existing file belongs to another tool (DXVK, Special K, ENB, etc.) using binary signature scanning. If unidentified, a confirmation dialog asks whether to overwrite. During Update All, foreign files are silently skipped.

---

## UE-Extended & Native HDR

Unreal Engine games with native HDR are automatically assigned UE-Extended via the remote manifest. These display "Extended UE Native HDR" as their engine badge. In-game HDR must be turned on for UE-Extended to work. Other UE games can be manually toggled via the UE options menu on the RenoDX row.

---

## Shader Packs

RDXC downloads and maintains 7 HDR shader packs, merged into a shared staging folder and deployed per-game.

### Included Packs

| Pack | Author |
|------|--------|
| [ReShade HDR Shaders](https://github.com/EndlesslyFlowering/ReShade_HDR_shaders) | EndlesslyFlowering (Lilium) |
| [PumboAutoHDR](https://github.com/Filoppi/PumboAutoHDR) | Filoppi (Pumbo) |
| [smolbbsoop shaders](https://github.com/smolbbsoop/smolbbsoopshaders) | smolbbsoop |
| [Reshade Simple HDR Shaders](https://github.com/MaxG2D/ReshadeSimpleHDRShaders) | MaxG2D |
| [reshade-shaders](https://github.com/clshortfuse/reshade-shaders) | clshortfuse |
| [potatoFX](https://github.com/CreepySasquatch/potatoFX) | CreepySasquatch |
| [reshade-shaders (slim)](https://github.com/crosire/reshade-shaders/tree/slim) | crosire |

### Deploy Modes

Configure on the Settings page:

| Mode | Behaviour |
|------|-----------|
| **Off** | No shaders deployed. Manage your own. |
| **Minimum** (default) | Lilium HDR Shaders only. |
| **All** | All 7 packs. |
| **User** | Custom folder only — no auto-downloaded packs. |

Custom shaders: `%LOCALAPPDATA%\RenoDXCommander\reshade\Custom\Shaders\` and `\Textures\`.

### Deploy Destinations

| Scenario | Destination |
|----------|-------------|
| DC Mode ON | `%LOCALAPPDATA%\Programs\Display_Commander\Reshade\Shaders\` and `\Textures\` |
| DC Mode OFF | `<game folder>\reshade-shaders\Shaders\` and `\Textures\` |

---

## Luma Framework

[Luma Framework](https://github.com/Filoppi/Luma-Framework) by Pumbo (Filoppi) is a DX11 modding framework adding HDR support via the ReShade addon system. RDXC detects Luma-compatible games and shows a toggle badge in the detail panel.

### How Luma Mode Works

- RenoDX, ReShade, and Display Commander are **automatically uninstalled** and hidden. Only **Install Luma** is available.
- Installing Luma deploys the mod zip, `reshade.ini`, and Lilium HDR shaders — everything self-contained.
- Uninstalling or toggling off removes all Luma files.
- The ℹ popup shows Luma-specific notes from the wiki and remote manifest.
- Overrides disables "Exclude from wiki" while Luma is active.

Luma downloads are restricted to trusted GitHub URLs under `https://github.com/Filoppi/`.

---

## Per-Game Overrides

The Overrides section appears below Components in the detail panel.

| Override | Effect |
|----------|--------|
| **Game name (editable)** | Rename the game — persists across Refresh and restarts |
| **Wiki mod name** | Match to a different wiki entry. Also applies to Luma matching. |
| **↩ Reset** | Restore original name and clear wiki mapping |
| **DLL naming override** | Custom filenames for ReShade and DC. Existing installs are renamed in place. Takes priority over any manifest-defined DLL names. |
| **Exclude from Update All** | Skip during bulk updates |
| **DC Mode** | Follow Global / Force Off / Force Mode 1 / Force Mode 2 |
| **Shader Mode** | Global / Off / Minimum / All / User. Per-game shader mode only applies when DC Mode is OFF. |
| **Save Overrides** | Apply changes and refresh status |

---

## INI Presets

RDXC bundles a default `reshade.ini` seeded on first launch. It's deployed alongside ReShade on every install, ensuring sensible defaults (disabled Generic Depth and Effect Runtime Sync addons, Home key overlay).

Config files in `%LOCALAPPDATA%\RenoDXCommander\inis\`:

| File | Copied When |
|------|-------------|
| `reshade.ini` | Every ReShade or DC install, or via 📋 button on the ReShade row |
| `ReShadePreset.ini` | Automatically alongside `reshade.ini` if the file exists in the inis folder |
| `DisplayCommander.toml` | Via 📋 button on the Display Commander row |

To use a custom ReShade preset, place your `ReShadePreset.ini` in the inis folder. It will be copied to every new game install automatically.

---

## Remote Manifest

RDXC fetches a remote manifest from GitHub on every launch, providing game-specific overrides without app updates:

- Blacklist (excluded non-game apps)
- Install path overrides
- Wiki name overrides (map detected game name to wiki mod name)
- Wiki status overrides (force a specific wiki status icon)
- Wiki unlinks (ignore false fuzzy wiki matches, fall back to generic engine addon)
- Game notes (append or replace wiki notes)
- Native HDR list (auto-assign UE-Extended)
- UE-Extended games list
- 32-bit / 64-bit game flags (override auto-detected bitness)
- Engine overrides — force a specific engine label for a game; `"Unreal"` / `"Unity"` affect mod assignment and filter, any other string (e.g. `"Silk"`) is display-only and filters into Other
- DLL name overrides — set the ReShade and/or DC install filename per game (e.g. `"Mirror's Edge": { "reshade": "d3d9.dll", "dc": "winmm.dll" }`); user-set overrides in the Manage panel take priority
- Snapshot URL overrides (direct addon download URL when wiki lacks one)
- DC mode overrides (force a specific DC mode level per game)
- Luma default games and game notes

Cached locally for offline use after first fetch.

---

## Update All

The Deploy... flyout provides three Update All actions for ReShade, Display Commander, and RenoDX across all eligible games. Games with overrides or foreign DLLs are skipped. Updates are flagged by comparing stored file sizes against remote sources.

---

## Auto-Update

RDXC checks for new versions on launch. Disable via Settings → Preferences → Skip update check on launch.

---

## Data Storage

Everything under `%LOCALAPPDATA%\RenoDXCommander\`:

| Path | Contents |
|------|---------|
| `game_library.json` | Detected games, hidden list, manually added games |
| `installed.json` | RenoDX mod install records |
| `aux_installed.json` | ReShade and DC install records |
| `settings.json` | All settings and per-game overrides |
| `downloads\` | Cached downloads |
| `inis\` | Preset config files |
| `reshade\` | Staged shader packs and custom shaders |
| `logs\` | Crash reports and verbose logs |

---

## Third-Party Components

| Component | Author | Licence |
|-----------|--------|---------|
| [ReShade](https://reshade.me) | Crosire | [BSD 3-Clause](https://github.com/crosire/reshade/blob/main/LICENSE.md) |
| [Display Commander](https://github.com/pmnoxx/display-commander) | pmnoxx | Source-available |
| [RenoDX](https://github.com/clshortfuse/renodx) | clshortfuse & contributors | [MIT](https://github.com/clshortfuse/renodx/blob/main/LICENSE) |
| [Luma Framework](https://github.com/Filoppi/Luma-Framework) | Pumbo (Filoppi) | Source-available |
| [HtmlAgilityPack](https://github.com/zzzprojects/html-agility-pack) | ZZZ Projects Inc. | [MIT](https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | Microsoft / .NET Foundation | [MIT](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | Adam Hathcock | [MIT](https://github.com/adamhathcock/sharpcompress/blob/master/LICENSE.txt) |
| [7-Zip](https://www.7-zip.org/) | Igor Pavlov | [LGPL-2.1 / BSD-3-Clause](https://www.7-zip.org/license.txt) |
