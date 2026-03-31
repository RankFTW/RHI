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
- [ReLimiter](#relimiter)
- [Shader Packs](#shader-packs)
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
- [Data Storage](#data-storage)
- [Troubleshooting](#troubleshooting)
- [Third-Party Components](#third-party-components)

---

## Layout

RHI offers two view modes — Detail View and Grid View — plus a global Settings page. Window size and position are remembered across restarts.

### Detail View

The default layout with a game list sidebar on the left and a detail panel on the right. RHI remembers which view you were last using and restores it on launch.

### Grid View

A card-based layout showing all games as a grid. Toggle between views with the view switch button in the toolbar. Each card shows:

- Game name and platform icon
- Graphics API badge (e.g. DX12, VLK, DX11/12 / VLK)
- Installation status dots for RenoDX (RDX), ReShade (RS), and ReLimiter (UL)
- Wiki status icon
- Update-available highlight border
- A Manage popout for quick access to install/uninstall/override controls

Games in Luma mode do not show a wiki status icon on the grid card.

### Toolbar

| Control | Function |
|---------|----------|
| Refresh | Rescan game library and fetch latest mod info. After initial boot, runs invisibly in the background. |
| Update | Update ReShade, RenoDX, and ReLimiter for all eligible games in one click |
| Help | Flyout with Discord (RHI support channel), Guide (this document), and Ko-fi |
| View toggle | Switch between Detail View and Grid View |
| Settings | Navigate to the Settings page |

### Game List Sidebar (Detail View)

- **Search box** — filters games in real-time as you type
- **Filter chips** — All Games, Favourites, Installed, Unreal, Unity, Other, RenoDX, Luma, Hidden. Engine and mod filters can be combined. Your selected filter is saved and restored on reopen.
- **Game/installed counts** — how many games are visible and how many have mods installed
- **Game list** — each entry shows a platform icon, game name, and a green dot if updates are available

### Detail Panel

When a game is selected:

- **Game name** with badges for platform, engine type, wiki status, mod author(s), UE-Extended / Native HDR, and 32-bit indicator
- **Graphics API badge** — detected rendering APIs
- **Install path** in monospace text
- **Installed addon filename** badge when a mod is installed
- **Components table** — ReShade, ReLimiter, RenoDX, and Luma (when applicable), each with status, install/reinstall/update button, options menu, and uninstall button
- **Rendering path toggle** — for dual-API games (DirectX + Vulkan)
- **Overrides section** — all per-game settings inline
- **Utility buttons** — favourite, discussion link, game info/notes, hide/unhide, folder menu (open in Explorer, change install folder, reset/remove game)

### Status Bar

- **Status text** (left) — game count, installed count, or current operation
- **Single-player warning** (centre)
- **Patch Notes** (right) — opens a dialog showing recent changes

---

## Settings Page

Click **Settings** in the toolbar. Click **Back to Games** to return.

| Section | Contents |
|---------|----------|
| Add Game | Manually add a game that wasn't auto-detected. Enter the game name and pick the install folder. |
| Full Refresh | Clears all caches and re-scans everything from disk. Use when games or mods appear out of sync. |
| Preferences | Skip Update Check on Launch, Beta Opt-In, Verbose Logging, Custom Shaders toggle, Screenshot Path |
| Crash and Error Logs | Open Logs Folder, Open Downloads Cache, ReShade staging path |
| About | Version (read from assembly), description, disclaimer, single-player warning |
| Credits | Third-party components with descriptions, licences, and links |

All settings apply immediately.

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

The detail panel shows a Components section with up to four rows:

| Row | Component | Controls |
|-----|-----------|----------|
| ReShade | ReShade | Install / Reinstall / Update — Copy INI — Uninstall |
| ReLimiter | ReLimiter | Install / Reinstall / Update — Copy INI — Uninstall |
| RenoDX | RenoDX Mod | Install / Reinstall / Update — UE-Extended options — Uninstall |
| Luma | Luma Framework | Install / Uninstall (shown only in Luma mode) |

### Version Display

The status label next to install buttons shows the installed version number (e.g. `6.7.3`) instead of just "Installed". Falls back to "Installed" if no version information is available. When an update is available, the text turns purple and shows the current version. After updating, it switches to the new version in green.

### Mod Author Badges

Named mods from the RenoDX wiki display the mod author as a bordered badge on the detail panel info line. Multiple authors each get their own badge. Generic Unreal Engine mods show "ShortFuse", UE-Extended mods show "Marat", and generic Unity mods show "Voosh". Author badges are clickable links to Ko-fi donation pages where available. Games in Luma mode show the Luma mod author in place of the RenoDX author. Author display names can be overridden via the remote manifest (e.g. wiki handle `"oopydoopy"` displays as `"Jon"`).

### Clickable Status Links

- ReShade "Installed" → links to [reshade.me](https://reshade.me)
- RenoDX "Installed" → links to the game's wiki page (or the mods list)
- ReLimiter "Installed" → links to the [ReLimiter feature guide](https://github.com/RankFTW/Ultra-Limiter?tab=readme-ov-file#ultra-limiter--comprehensive-feature-guide)

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

---

## ReLimiter

[ReLimiter](https://github.com/RankFTW/Ultra-Limiter?tab=readme-ov-file#ultra-limiter--comprehensive-feature-guide) is an optional per-game frame pacing addon downloaded from GitHub on demand.

### 32-bit and 64-bit Support

RHI automatically selects the correct addon file based on the game's detected bitness:

| Game Bitness | Addon File |
|--------------|------------|
| 64-bit | `relimiter.addon64` |
| 32-bit | `relimiter.addon32` |

Both variants are downloaded from the same GitHub releases endpoint and cached separately so they don't overwrite each other.

### Install / Update / Uninstall

The correct addon file is selected automatically based on the game's bitness, downloaded from its GitHub release when first needed, and cached locally. Legacy `ultra_limiter.addon64` / `ultra_limiter.addon32` files are cleaned up automatically during install.

### INI Configuration

RHI bundles a default `relimiter.ini` seeded to `%LOCALAPPDATA%\RHI\inis\` on first launch. A copy button on the ReLimiter component row copies this INI to the game folder (or AddonPath if configured). Customise the INI in the inis folder and it will be used for all future copies.

### Update Detection

Updates are detected by comparing the locally cached file against the remote release using both file size and SHA-256 hash. When a newer version is available, the status changes and the install button shows "Update". Version metadata is tracked per-bitness so 32-bit and 64-bit updates are independent.

### Status Indicators

| Colour | Meaning |
|--------|---------|
| Green | Installed and up to date |
| Orange | Update available |

The ReLimiter status dot is hidden when a game is in Luma mode.

---

## Shader Packs

RHI downloads and maintains a collection of 37+ ReShade shader packs, merged into a shared staging folder and deployed per-game.

### Categories

- **Essential** — Lilium HDR Shaders, required for HDR tone mapping. Selected by default on fresh installs (can be unticked in the global shader picker).
- **Recommended** — Core HDR and post-processing packs: crosire reshade-shaders (master), PumboAutoHDR, smolbbsoop shaders, MaxG2D Simple HDR Shaders, clshortfuse ReShade shaders, and potatoFX.
- **Extra** — Community shader packs covering cinematic colour grading, film emulation, VR tools, CRT simulation, retro filters, screen-space reflections, global illumination, artistic effects, and more. Includes packs from SweetFX, OtisFX, Depth3D, qUINT, iMMERSE, METEOR, ZenteonFX, GShade-Shaders, CShade, prod80, CobraFX, and others.

### Per-Game Shader Overrides

Per-game shader overrides allow different games to use different subsets of shader packs. Select mode opens a picker to choose specific packs for that game. Clicking Deploy immediately deploys the chosen shaders to the game folder.

### Deploy Destinations

| Scenario | Destination |
|----------|-------------|
| DLL ReShade | `<game folder>\reshade-shaders\Shaders\` and `\Textures\` |
| Vulkan ReShade | `<game folder>\reshade-shaders\Shaders\` and `\Textures\` (requires `RDXC_VULKAN_FOOTPRINT`) |

User-owned shader folders are preserved by renaming to `reshade-shaders-original` before deployment and restored when the shader mode is set to Off or when Vulkan ReShade is uninstalled.

### Custom Shaders

Place custom shaders in `%LOCALAPPDATA%\RHI\reshade\Custom\Shaders\` and textures in `%LOCALAPPDATA%\RHI\reshade\Custom\Textures\`. Enable the Custom Shaders toggle in Settings to include them in deployments.

### Startup Deployment

On launch, RHI ensures shader packs are fully downloaded before syncing shaders to all installed game folders. Games with ReShade installed will have the correct global or per-game shaders deployed automatically.

---

## Luma Framework

[Luma Framework](https://github.com/Filoppi/Luma-Framework) by Pumbo (Filoppi) is a DX11 modding framework adding HDR support via the ReShade addon system. RHI detects Luma-compatible games and shows a toggle badge in the detail panel.

### How Luma Mode Works

- RenoDX and ReShade are automatically uninstalled and hidden. Only Install Luma is available.
- Installing Luma deploys the mod zip, `reshade.ini`, and Lilium HDR shaders.
- Uninstalling or toggling off removes all Luma files.
- The info popup shows Luma-specific notes from the wiki and remote manifest.
- Games listed in the remote manifest `lumaDefaultGames` automatically start in Luma mode on first detection.

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
| DLL naming override | Custom filenames for ReShade via dropdown combo boxes with common DLL suggestions. Existing installs are renamed in place — no reinstall needed. Takes priority over manifest DLL names. |
| Global update inclusion | Three toggle switches (ReShade, RenoDX, ReLimiter) controlling whether the game is included in bulk updates. All default to On. |
| Shader Mode | Global / Off / Minimum / All / User / Select. Select mode opens a picker for specific shader packs. |
| Rendering Path | For dual-API games: DirectX or Vulkan. Switching triggers automatic cleanup. |
| Wiki exclusion | Exclude the game from wiki lookups |
| Reset Overrides | Reset all override settings back to defaults |

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
| `ReShadePreset.ini` | Automatically alongside `reshade.ini` if the file exists in the inis folder. |

To use a custom ReShade preset, place your `ReShadePreset.ini` in the inis folder. It will be copied to every new game install automatically.

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

The **Update** button in the toolbar updates ReShade, RenoDX, and ReLimiter across all eligible games in one click. The button lights up purple when updates are available.

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
| URL (`.url` shortcut) | Parsed and processed as an addon download URL |

### Extension Validation

File extensions are validated before any network or file activity. Only the following extensions are accepted: `.exe`, `.addon64`, `.addon32`, `.zip`, `.7z`, `.rar`, `.tar`, `.gz`, `.bz2`, `.xz`, `.tgz`. Files with unrecognised extensions are silently skipped.

---

## Addon Auto-Detection

RHI watches your Downloads folder for new addon files and prompts you to install them.

### Downloads Folder Watching

The default watch folder is the system Downloads directory. You can change it in Settings via the Browse button, or reset it to the default. RHI monitors for new `renodx-*.addon64` and `renodx-*.addon32` files appearing in the watched folder.

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

## Data Storage

Everything under `%LOCALAPPDATA%\RHI\`:

| Path | Contents |
|------|----------|
| `game_library.json` | Detected games, hidden list, manually added games |
| `installed.json` | RenoDX mod install records (game name, path, addon filename, hash, snapshot URL, remote file size) |
| `aux_installed.json` | ReShade and ReLimiter install records (game name, path, addon type, installed filename, source URL) |
| `settings.json` | All settings, per-game overrides, and persisted filter mode |
| `ul_meta.json` | ReLimiter version metadata (per-bitness) |
| `downloads\` | Cached downloads (separate files for 32-bit and 64-bit ReLimiter) |
| `inis\` | Preset config files (`reshade.ini`, `reshade.vulkan.ini`, `relimiter.ini`, etc.) |
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
| DLL override not applying from manifest | Click **Refresh** — manifest DLL overrides are applied on every refresh |

---

## Third-Party Components

| Component | Author | Licence |
|-----------|--------|---------|
| [ReShade](https://reshade.me) | Crosire | [BSD 3-Clause](https://github.com/crosire/reshade/blob/main/LICENSE.md) |
| [RenoDX](https://github.com/clshortfuse/renodx) | clshortfuse & contributors | [MIT](https://github.com/clshortfuse/renodx/blob/main/LICENSE) |
| [ReLimiter](https://github.com/RankFTW/Ultra-Limiter) | RankFTW | Source-available |
| [RE Framework](https://github.com/praydog/REFramework-nightly) | praydog | [MIT](https://github.com/praydog/REFramework/blob/master/LICENSE) |
| [Luma Framework](https://github.com/Filoppi/Luma-Framework) | Pumbo (Filoppi) | Source-available |
| [HtmlAgilityPack](https://github.com/zzzprojects/html-agility-pack) | ZZZ Projects Inc. | [MIT](https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | Microsoft / .NET Foundation | [MIT](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | Adam Hathcock | [MIT](https://github.com/adamhathcock/sharpcompress/blob/master/LICENSE.txt) |
| [7-Zip](https://www.7-zip.org/) | Igor Pavlov | [LGPL-2.1 / BSD-3-Clause](https://www.7-zip.org/license.txt) |
