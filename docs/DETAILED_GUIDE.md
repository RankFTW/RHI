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
- [UE-Extended & Native HDR](#ue-extended--native-hdr)
- [ReLimiter](#ultra-limiter)
- [Shader Packs](#shader-packs)
- [Luma Framework](#luma-framework)
- [Per-Game Overrides](#per-game-overrides)
- [INI Presets](#ini-presets)
- [Remote Manifest](#remote-manifest)
- [Update All](#update-all)
- [Auto-Update](#auto-update)
- [Patch Notes](#patch-notes)
- [Data Storage](#data-storage)
- [Troubleshooting](#troubleshooting)
- [Third-Party Components](#third-party-components)

---

## Layout

RHI offers two view modes — Detail View and Grid View — plus a global Settings page. RHI remembers its window size and position across restarts.

### Detail View

The default layout with a game list sidebar on the left and a detail panel on the right. RHI remembers which view you were last using and restores it on launch.

### Grid View

An alternative card-based layout showing all games as a grid of cards. Toggle between views with the view switch button in the toolbar. Each card shows:

- Game name and platform icon
- Graphics API badge (e.g. DX12, VLK, DX11/12 / VLK)
- Installation status dots for RenoDX (RDX), ReShade (RS), and ReLimiter (UL)
- Wiki status icon
- Update-available highlight border
- A Manage popout for quick access to install/uninstall/override controls without switching to Detail View

Games in Luma mode do not show a wiki status icon on the grid card.

### Toolbar

| Control | Function |
|---------|----------|
| **RHI logo + title** | App branding |
| **Refresh** | Rescan game library and fetch latest mod info. After the initial boot, refresh runs invisibly in the background — game cards stay visible throughout. |
| **Update** | Update ReShade and RenoDX for all eligible games in one click |
| **Help** | Flyout menu with Discord (opens the RHI support channel), Guide (opens the detailed guide), and Ko-fi (support link) |
| **View toggle** | Switch between Detail View and Grid View |
| **Settings** | Navigate to the Settings page |
### Game List Sidebar (Detail View)

- **Search box** — filters games in real-time as you type. The X clear button appears as soon as you start typing.
- **Filter chips** — All Games, Favourites, Installed, Unreal, Unity, Other, RenoDX, Luma, Hidden. The Installed filter shows games with ReShade installed. Engine and mod filters can be combined (e.g. Unreal + RenoDX shows Unreal games with RenoDX mods). Your selected filter is saved and automatically restored when you reopen the app.
- **Game/installed counts** — how many games are visible and how many have mods installed
- **Game list** — each entry shows a platform icon, game name, and a green dot if updates are available

Selecting a game loads its details in the panel to the right. Favouriting or unfavouriting a game preserves scroll position. Refresh restores the previous scroll position and selection.

### Detail Panel

When a game is selected, the detail panel shows:

- **Game name** with badges for platform source, engine type (including custom engine names from the manifest), wiki status, mod author(s), UE-Extended / Native HDR
- **Graphics API badge** — shows detected rendering APIs (e.g. DX12, VLK, or multi-API combos like DX11/12 / VLK)
- **Install path** in monospace text
- **Components table** — ReShade, ReLimiter, RenoDX, and Luma (when applicable), each with status, install/reinstall/update button, options menu, and uninstall button
- **Rendering path toggle** — for dual-API games (DirectX + Vulkan), a toggle to choose which rendering path ReShade targets
- **Vulkan ReShade status** — shows the ReShade version number with "(Vulkan)" underneath in green when Vulkan ReShade is active
- **Overrides section** — all per-game settings inline with descriptions
- **Utility buttons** — favourite, discussion link, game info/notes, hide/unhide, and a folder menu (open in Explorer, change install folder, reset/remove game)

When no game is selected, the panel shows a placeholder prompting you to pick one.

### Status Bar

The bottom status bar shows:

- **Status text** (left) — game count, installed count, or current operation status
- **Single-player warning** (centre) — a reminder that ReShade with addon support may trigger anti-cheat in online/multiplayer games
- **Patch Notes** (right) — opens a dialog showing recent patch notes

---

## Settings Page

Click **Settings** in the toolbar to open the Settings page. Click **Back to Games** to return.

| Section | Contents |
|---------|----------|
| **Add Game** | Manually add a game that wasn't automatically detected |
| **Full Refresh** | Clears all caches and re-scans everything from disk |
| **Preferences** | Skip Update Check toggle, Beta Opt-In toggle, Verbose Logging toggle — each with inline description |
| **Crash & Error Logs** | Open Logs Folder, Open Downloads Cache, and ReShade staging path — with paths shown inline |
| **About** | Version, app description, disclaimer, and single-player warning |
| **Credits** | Third-party components with descriptions, licences, and links |

All settings apply immediately — no separate save action required.
---

## Game Detection

RHI re-scans all stores on every launch and merges newly installed games into its cached library automatically.

| Store | Detection Method |
|-------|-----------------|
| **Steam** | Reads `libraryfolders.vdf` and `appmanifest_*.acf` files across all library folders |
| **GOG** | Registry keys under `HKLM\SOFTWARE\GOG.com\Games` |
| **Epic Games** | Manifest `.item` files in `ProgramData\Epic\EpicGamesLauncher\Data\Manifests` |
| **EA App** | `installerdata.xml` manifests, registry keys, default EA Games folders, and EA Desktop local config path discovery |
| **Ubisoft Connect** | Registry keys under `HKLM\SOFTWARE\Ubisoft\Launcher\Installs`, `settings.yml` game installation path, and default Ubisoft Game Launcher games folder |
| **Xbox / Game Pass** | Windows `PackageManager` API — identifies games by `MicrosoftGame.config` presence. Falls back to `.GamingRoot` file parsing, registry, and folder scanning |
| **Battle.net** | Windows Uninstall registry entries (filtered by Blizzard/Activision publisher), `Battle.net.config` default install path, and default folder scanning |
| **Rockstar Games** | Windows Uninstall registry entries (filtered by Rockstar publisher), launcher `titles.dat` install paths, and default folder scanning |

Games on a disconnected drive are preserved in the cache until the drive is reconnected. Per-platform detection failures are isolated — one store failing won't block others.

### Engine Detection

RHI detects game engines automatically:

| Engine | Detection Method |
|--------|-----------------|
| **Unreal Engine** | Presence of Unreal-specific files and folder structures |
| **Unity** | `UnityPlayer.dll`, `Mono` folder, `MonoBleedingEdge` folder, `il2cpp` folder, `GameAssembly.dll` |
| **Custom** | Engine overrides from the remote manifest (e.g. `"Silk"`, `"Source 2"`, `"Creation Engine"`) |

Custom engine names display with a dedicated engine icon. `"Unreal"` and `"Unity"` overrides affect filter category and mod assignment; other strings are display-only and filter into Other.

### 32-bit / 64-bit Detection

RHI automatically detects whether a game is 32-bit or 64-bit by examining the PE header of the game executable. The remote manifest can override this with `thirtyTwoBitGames` and `sixtyFourBitGames` flags, which take priority over auto-detection.

### Adding Games Manually

- **Add Game** button (on the Settings page) — enter the game name and pick the install folder.
- **Drag and drop** — drag a game's `.exe` onto the RHI window. RHI detects the engine type (Unreal, Unity, or Unknown), infers the game root folder by recognising store markers and engine layouts, and guesses the game name from folder structure. A confirmation dialog lets you edit the name before adding. Added games appear in their correct alphabetical position immediately.

### Drag-and-Drop

RHI supports drag-and-drop for multiple file types:

| File Type | Behaviour |
|-----------|-----------|
| **Game `.exe`** | Opens an add-game dialog with auto-detected engine and name |
| **`.addon64` / `.addon32`** | Opens an install dialog with a game picker (auto-selects based on filename). If the filename doesn't match any game, the picker defaults to the currently selected game in the sidebar. |
| **Archives** (`.zip`, `.7z`, `.rar`, `.tar`, `.gz`, `.bz2`, `.xz`) | Extracted using bundled 7-Zip. Any addon files inside are found and offered for install. If multiple addons are found, a picker dialog lets you choose. |

Drag-and-drop works even when RHI is running as administrator (UIPI bypass via `WM_DROPFILES`). File extensions are validated before any network or file activity.

### Addon Auto-Detection

RHI watches your Downloads folder (configurable in Settings) for new `renodx-*.addon64` / `.addon32` files and automatically prompts you to install them. Double-clicking an addon file in Explorer opens RHI and triggers the install flow. If RHI is already running, the file is forwarded to the existing instance via named pipe. All entry points (watcher, drag-and-drop, file association, archive extraction) enforce the `renodx-` filename prefix to avoid triggering on unrelated addon files.

### AddonPath Support

Addon installs (RenoDX and ReLimiter) respect the `AddonPath` setting in `reshade.ini`. If the `[ADDON]` section contains an `AddonPath=` line, addons are deployed to that folder instead of the game root. Relative paths are resolved against the game directory. Uninstall, update detection, and addon scanning all check the same resolved path.
---

## Graphics API Detection

RHI scans game executables using PE header import table analysis to detect which graphics APIs a game uses.

### Detected APIs

| API | Badge | Detection |
|-----|-------|-----------|
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

Graphics API detection also drives automatic ReShade DLL naming. When no user or manifest DLL override is set, RHI uses the detected APIs to choose the correct ReShade filename:

- **DX9 detected** → ReShade is installed as `d3d9.dll`
- **OpenGL-only** → ReShade is installed as `opengl32.dll`
- **All other cases** → ReShade is installed as `dxgi.dll` (default)

See [Components > Automatic DLL Naming for OpenGL and DX9 Games](#automatic-dll-naming-for-opengl-and-dx9-games) for the full priority chain.

### Manifest API Overrides

The remote manifest supports comma-separated API tags (e.g. `"DX12, VLK"`) for games like Red Dead Redemption 2 that load Vulkan dynamically and can't be detected via PE imports alone.

---

## Components

The detail panel shows a Components section with up to four rows:

| Row | Component | Controls |
|-----|-----------|----------|
| **ReShade** | ReShade | Install / Reinstall / Update — menu (Copy INI) — Uninstall |
| **ReLimiter** | ReLimiter | Install / Reinstall / Update — Copy INI — Uninstall |
| **RenoDX** | RenoDX Mod | Install / Reinstall / Update — UE Extended options — Uninstall |
| **Luma** | Luma Framework | Install / Uninstall (shown only in Luma mode) |

### Version Display

The status label next to the ReShade install button shows the installed version number (e.g. `6.7.3`) instead of just "Installed". Falls back to "Installed" if no version information is available. When an update is available, the text turns purple and shows the current version number. After updating, it switches to the new version in green.

### Mod Author Badges

Named mods from the RenoDX wiki display the mod author as a bordered badge on the detail panel info line. Multiple authors (e.g. "oopydoopy & Voosh") each get their own badge. Generic Unreal Engine mods show "ShortFuse", UE-Extended mods show "Marat", and generic Unity mods show "Voosh". Games in Luma mode show the Luma mod author from the Luma wiki (e.g. "Pumbo", "XgarhontX") in place of the RenoDX author.

### Automatic DLL Naming for OpenGL and DX9 Games

RHI automatically selects the correct ReShade DLL filename based on the game's detected graphics API:

| Detected API | ReShade Filename |
|--------------|-----------------|
| DirectX 9 | `d3d9.dll` |
| OpenGL only | `opengl32.dll` |
| All other APIs | `dxgi.dll` (default) |

DX9 takes precedence — if a game imports both `d3d9.dll` and `opengl32.dll`, ReShade is installed as `d3d9.dll`. OpenGL naming only applies when OpenGL is the sole detected API.

The full priority chain for ReShade DLL naming is:

1. **User DLL override** (set in Per-Game Overrides) — always wins
2. **Manifest `dllNameOverrides`** — per-game overrides from the remote manifest
3. **Automatic API-based naming** — DX9 → `d3d9.dll`, OpenGL-only → `opengl32.dll`
4. **Default** — `dxgi.dll`

### ReShade Detection Under Non-Standard Filenames

ReShade installations using non-standard DLL filenames (e.g. `d3d11.dll`, `dinput8.dll`, `version.dll`, `d3d9.dll`, `opengl32.dll`) are detected via binary signature scanning (`IsReShadeFileStrict`) as a fallback. Reinstalling correctly removes the old non-standard DLL before placing the new one.
---

## Vulkan ReShade Support

RHI provides full Vulkan implicit layer support for ReShade, enabling ReShade injection for Vulkan-rendered games without per-game DLL injection.

### How It Works

1. **Global Vulkan layer** — RHI installs ReShade as a Vulkan implicit layer via the Windows registry (`HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers`). This makes ReShade available to all Vulkan games system-wide.
2. **Layer manifest** — A bundled `ReShade64.json` manifest with correct `device_extensions` and `disable_environment` fields is deployed alongside the ReShade DLL to `C:\ProgramData\ReShade\`.
3. **Per-game INI** — A dedicated `reshade.vulkan.ini` with Vulkan-tuned depth buffer settings is deployed to each game folder.
4. **Footprint file** — An `RDXC_VULKAN_FOOTPRINT` marker file is placed in the game folder to enable managed shader deployment.

### Dual-API Games

Games detected with both DirectX and Vulkan show a rendering path toggle in the detail panel. Switching from DirectX to Vulkan automatically:

- Uninstalls DX ReShade (DLL-based)
- Removes `reshade.ini` and managed shaders from the game folder
- Restores `reshade-shaders-original` if present

### Vulkan ReShade Status

When `reshade.ini` is present in a Vulkan game's folder, the detail panel shows the ReShade version number with "(Vulkan)" underneath in green, matching the installed styling of DLL-based ReShade games.

### Per-Game Uninstall

A uninstall button appears for Vulkan games that have `reshade.ini` deployed. Clicking it removes:

- `reshade.ini` from the game folder
- The `RDXC_VULKAN_FOOTPRINT` file
- Managed `reshade-shaders` folder
- Restores `reshade-shaders-original` if present

This does not affect the global Vulkan layer — only the per-game artifacts.

### Footprint and Shader Deployment

The `RDXC_VULKAN_FOOTPRINT` file controls shader deployment to Vulkan game folders:

- **Present** — shader sync deploys `reshade-shaders/` to the game folder
- **Absent** — shader sync skips the game folder

---

## Foreign DLL Protection

When installing ReShade as `dxgi.dll`, RHI checks whether an existing file belongs to another tool (DXVK, Special K, ENB, etc.) using binary signature scanning. The scan matches on `reshade.me` or `crosire` strings unique to the actual ReShade binary, and rejects files over 15 MB as too large to be ReShade. This prevents tools like OptiScaler from being misidentified.

If the existing file is unidentified, a confirmation dialog asks whether to overwrite. During Update All, foreign files are silently skipped.

---

## UE-Extended & Native HDR

Unreal Engine games with native HDR are automatically assigned UE-Extended via the remote manifest. These display "Extended UE Native HDR" as their engine badge. In-game HDR must be turned on for UE-Extended to work.

The UE-Extended toggle now appears for every Unreal Engine game that does not have a named mod on the RenoDX wiki, not just games explicitly listed in the manifest. A compatibility warning dialog pops up when enabling UE-Extended, advising that not all games are compatible and to check the Notes section for any game-specific information.
---

## ReLimiter

[ReLimiter](https://github.com/RankFTW/Ultra-Limiter?tab=readme-ov-file#ultra-limiter--comprehensive-feature-guide) is an optional per-game component downloaded from GitHub on demand. See the linked comprehensive feature guide for full details on what ReLimiter does and all available settings.

### 32-bit and 64-bit Support

ReLimiter supports both 32-bit and 64-bit games. RHI automatically selects the correct addon file based on the game's detected bitness:

| Game Bitness | Addon File |
|--------------|------------|
| 64-bit | `relimiter.addon64` |
| 32-bit | `relimiter.addon32` |

Both variants are downloaded from the same GitHub releases endpoint and cached separately so they don't overwrite each other.

### Install / Reinstall / Uninstall

ReLimiter can be installed, reinstalled, or uninstalled on a per-game basis from the Components table in the detail panel. The correct addon file (`relimiter.addon64` or `relimiter.addon32`) is selected automatically based on the game's bitness, downloaded from its GitHub release when first needed, and cached locally.

### INI Configuration

RHI bundles a default `relimiter.ini` that is seeded to the RHI inis folder (`%LOCALAPPDATA%\RHI\inis\`) on first launch if one doesn't already exist. A copy button on the ReLimiter component row copies this INI to the game folder, matching the existing ReShade INI workflow. You can customise the INI in the inis folder and it will be used for all future copies.

### Update Detection

Updates are detected by comparing the locally cached file against the remote release using both file size and SHA-256 hash. When a newer version is available, the status dot turns orange and the install button changes to Update.

### Status Indicator

A coloured status dot indicates the current state:

| Colour | Meaning |
|--------|---------|
| **Green** | Installed and up to date |
| **Orange** | Update available |

The ReLimiter status dot is hidden when a game is in Luma mode.

### Feature Guide Link

When ReLimiter is installed, the status label shows a clickable "Installed" link that opens the [ReLimiter comprehensive feature guide](https://github.com/RankFTW/Ultra-Limiter?tab=readme-ov-file#ultra-limiter--comprehensive-feature-guide) in the browser.
---

## Shader Packs

RHI downloads and maintains a comprehensive collection of 37+ ReShade shader packs, merged into a shared staging folder and deployed per-game. This includes all shader packs from the official ReShade installer plus additional community packs.

### Categories

Shader packs are organised into three categories:

- **Essential** — Always-on packs required for HDR tone mapping. Lilium HDR Shaders is the essential pack, selected by default on fresh installs (can be unticked in the global shader picker if not wanted).
- **Recommended** — Core HDR and post-processing packs that cover the most common use cases: crosire reshade-shaders (master), PumboAutoHDR, smolbbsoop shaders, MaxG2D Simple HDR Shaders, clshortfuse ReShade shaders, and potatoFX.
- **Extra** — A large library of community shader packs covering everything from cinematic colour grading and film emulation to VR tools, CRT simulation, retro filters, screen-space reflections, global illumination, artistic effects, and more. Includes packs from SweetFX, OtisFX, Depth3D, qUINT, iMMERSE, METEOR, ZenteonFX, GShade-Shaders, CShade, prod80, CobraFX, and many others.

The global shader picker lets you choose exactly which packs to deploy. Per-game shader overrides allow different games to use different subsets.

### Per-Game Shader Overrides

Per-game shader overrides in both Detail View and Grid View allow different games to use different subsets of shader packs. Select mode opens a picker to choose specific shader packs for that game. Clicking Deploy in the per-game picker immediately deploys the chosen shaders to the game folder.

### Deploy Destinations

| Scenario | Destination |
|----------|-------------|
| DLL ReShade | `<game folder>\reshade-shaders\Shaders\` and `\Textures\` |
| Vulkan ReShade | `<game folder>\reshade-shaders\Shaders\` and `\Textures\` (requires `RDXC_VULKAN_FOOTPRINT`) |

User-owned shader folders are preserved by renaming to `reshade-shaders-original` before deployment and restored when the shader mode is set to Off or when Vulkan ReShade is uninstalled.

Custom shaders: `%LOCALAPPDATA%\RHI\reshade\Custom\Shaders\` and `\Textures\`.

### Startup Shader Deployment

On launch, RHI ensures shader packs are fully downloaded before syncing shaders to all installed game folders. Games with ReShade installed will have the correct global or per-game shaders deployed automatically, even if they were installed by an older version that didn't deploy shaders.
---

## Luma Framework

[Luma Framework](https://github.com/Filoppi/Luma-Framework) by Pumbo (Filoppi) is a DX11 modding framework adding HDR support via the ReShade addon system. RHI detects Luma-compatible games and shows a toggle badge in the detail panel. Luma badges appear on all eligible game cards by default.

### How Luma Mode Works

- RenoDX and ReShade are **automatically uninstalled** and hidden. Only **Install Luma** is available.
- Installing Luma deploys the mod zip, `reshade.ini`, and Lilium HDR shaders — everything self-contained.
- Uninstalling or toggling off removes all Luma files.
- The info popup shows Luma-specific notes from the wiki and remote manifest.
- Overrides disables "Exclude from wiki" while Luma is active.
- Games listed in the remote manifest automatically start in Luma mode on first detection.

Luma downloads are restricted to trusted GitHub URLs under `https://github.com/Filoppi/`.

---

## Per-Game Overrides

The Overrides section appears below Components in the detail panel. All controls save immediately when changed — no Save button needed.

| Override | Effect |
|----------|--------|
| **Game name (editable)** | Rename the game — persists across Refresh and restarts, including for games with custom folder overrides |
| **Wiki mod name** | Match to a different wiki entry. Also applies to Luma matching. |
| **Reset** | Restore original name and clear wiki mapping |
| **DLL naming override** | Custom filenames for ReShade via dropdown combo boxes with common DLL suggestions (`dxgi.dll`, `d3d11.dll`, `dinput8.dll`, `version.dll`, `winmm.dll`, `d3d12.dll`, `xinput1_3.dll`, `msvcp140.dll`, `bink2w64.dll`, `d3d9.dll`). Existing installs are renamed in place — no reinstall needed. Takes priority over any manifest-defined DLL names. |
| **Global update inclusion** | Two toggle switches (ReShade, RenoDX) controlling whether the game is included in bulk updates for each component. All default to On. Legacy single-toggle settings are auto-migrated. |
| **Shader Mode** | Global / Off / Minimum / All / User / Select. Select mode opens a picker to choose specific shader packs for this game. |
| **Rendering Path** | For dual-API games: DirectX or Vulkan. Switching from DirectX to Vulkan triggers automatic cleanup of DX artifacts. |
| **Wiki exclusion** | Exclude the game from wiki lookups |
| **Reset Overrides** | Reset all override settings back to defaults (game name, wiki name, shader mode, DLL override, update toggles, and wiki exclusion) |
---

## INI Presets

RHI bundles default INI files seeded on first launch. They are deployed alongside their respective components on every install using a merge strategy — template keys always take precedence, but any game-specific settings not in the template (e.g. addon configs, effect toggles, custom keybinds) are preserved.

Config files in `%LOCALAPPDATA%\RHI\inis\`:

| File | Copied When |
|------|-------------|
| `reshade.ini` | Every ReShade install, or via copy button on the ReShade row. Merged into existing INI if present. |
| `reshade.vulkan.ini` | Deployed alongside `reshade.ini` for Vulkan games. Contains depth buffer preprocessor definitions tuned for Vulkan rendering. |
| `relimiter.ini` | Via copy button on the ReLimiter row. Copied to the game folder (or AddonPath) as-is. |
| `ReShadePreset.ini` | Automatically alongside `reshade.ini` if the file exists in the inis folder |

To use a custom ReShade preset, place your `ReShadePreset.ini` in the inis folder. It will be copied to every new game install automatically.

The INI copy button on the ReShade row deploys both `reshade.ini` and `reshade.vulkan.ini` when a game has Vulkan support.

---

## Remote Manifest

RHI fetches a remote manifest from GitHub on every launch, providing game-specific overrides without app updates. The manifest is fetched from the GitHub API with a `raw.githubusercontent.com` fallback, and cached locally for offline use.

### Manifest Fields

| Field | Effect |
|-------|--------|
| **Blacklist** | Excluded non-game apps |
| **Install path overrides** | Correct wrong install paths |
| **Wiki name overrides** | Map detected game name to wiki mod name |
| **Wiki status overrides** | Force a specific wiki status icon |
| **Wiki unlinks** | Ignore false fuzzy wiki matches, fall back to generic engine addon |
| **Game notes** | Append or replace wiki notes |
| **Luma game notes** | Luma-specific notes shown when game is in Luma mode |
| **Native HDR list** | Auto-assign UE-Extended |
| **UE-Extended games list** | Mark games for UE-Extended addon |
| **32-bit / 64-bit flags** | Override auto-detected bitness (`thirtyTwoBitGames`, `sixtyFourBitGames`) |
| **Engine overrides** | Force a specific engine label; `"Unreal"` / `"Unity"` affect mod assignment and filter, any other string is display-only |
| **DLL name overrides** | Set ReShade install filename per game (e.g. `"Mirror's Edge": { "reshade": "d3d9.dll" }`). User-set overrides take priority. Games with manifest DLL overrides show the toggle turned on automatically with filenames pre-filled. |
| **API overrides** | Comma-separated API tags (e.g. `"DX12, VLK"`) for games that can't be detected via PE imports |
| **Snapshot URL overrides** | Direct addon download URL when wiki lacks one |
| **Luma default games** | Games that auto-start in Luma mode on first detection |
| **Shader packs** | Per-game shader pack configuration |
| **"Extended UE" tag** | Automatically assigns UE-Extended addon and marks as native HDR |

---

## Update All

The **Update** button in the toolbar updates ReShade and RenoDX across all eligible games in one click. Each component respects its own per-game inclusion toggle — a game excluded from ReShade updates can still receive RenoDX updates. Games with foreign DLLs are skipped. Updates are flagged by comparing stored file sizes against remote sources.

---

## Auto-Update

RHI checks for new versions on launch by querying the GitHub Releases API. Disable via Settings > Preferences > Skip update check on launch.

### Stable and Beta Channels

When Beta Opt-In is enabled in Settings, RHI checks both the stable release (`RDXC` tag) and the beta release (`RDXC-BETA` tag). The version resolver determines which update to offer:

- Stable always wins over beta at the same or higher base version
- Beta is only offered when its base version exceeds the latest stable, OR when the current app is already on a beta and a newer beta at the same base version is available
- No update is offered if all candidates are at or below the current version

The app encodes its beta status in the 4th component of the assembly version: `1.5.5.0` = stable, `1.5.5.1` = beta 1, `1.5.5.2` = beta 2, etc.

---

## Patch Notes

RHI shows a patch notes dialog on first launch after an update, displaying the most recent version changes in a scrollable markdown view.
---

## Data Storage

Everything under `%LOCALAPPDATA%\RHI\`:

| Path | Contents |
|------|---------|
| `game_library.json` | Detected games, hidden list, manually added games |
| `installed.json` | RenoDX mod install records |
| `aux_installed.json` | ReShade and ReLimiter install records |
| `settings.json` | All settings, per-game overrides, and persisted filter mode |
| `downloads\` | Cached downloads |
| `inis\` | Preset config files (`reshade.ini`, `reshade.vulkan.ini`, `relimiter.ini`, `ReShadePreset.ini`) |
| `reshade\` | Staged shader packs and custom shaders |
| `logs\` | Session logs (timestamped, max 10 kept) and crash reports |

### Session Logging

A new session log file is created every time RHI starts, named with a timestamp (e.g. `session_2025-03-14_12-30-00.txt`). All activity is logged to the session file automatically — no need to enable Verbose Logging first. Old session logs are automatically pruned to keep a maximum of 10 on disk. The Verbose Logging toggle in Settings enables additional detail in log entries.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Game not detected | Click **Add Game** on the Settings page or drag the game's `.exe` onto the window |
| Xbox games missing | Click **Refresh** — RHI uses the PackageManager API |
| ReShade not loading | Check the install path — the ReShade DLL (`dxgi.dll`, `d3d9.dll`, or `opengl32.dll`) must be next to the game exe |
| ReShade not detected | If using a non-standard DLL name, RHI should detect it via binary signature scanning. Try **Refresh**. |
| Black screen (Unreal) | ReShade > Add-ons > RenoDX > set `R10G10B10A2_UNORM` to `output size` |
| UE-Extended not working | Turn on in-game HDR — UE-Extended requires native HDR output |
| Downloads failing | Click **Refresh**, or clear cache from Settings > Open Downloads Cache |
| Foreign DLL blocking install | Choose **Overwrite** in the dialog, or cancel to keep the existing file |
| Games/mods out of sync | Settings > **Full Refresh** to clear all caches |
| Drag-and-drop not working | Ensure RHI is running. Drag-and-drop uses Win32 shell handling and works even as administrator. |
| Vulkan ReShade not showing as installed | Check that `reshade.ini` exists in the game folder. The Vulkan layer must also be installed globally. |
| Shaders missing after uninstall | Click **Refresh** — RHI will detect the missing shaders and redeploy them. For Vulkan games, the footprint file is also restored. |
| Auto-update not triggering for beta | Ensure Beta Opt-In is enabled in Settings. The beta release on GitHub must have a parseable version in the title and the asset must be named `RDXC-Setup.exe`. |
| Games showing as installed after manual file removal | Click **Refresh** — RHI verifies files exist on disk and cleans up stale records. |
| DLL override not applying from manifest | Click **Refresh** — manifest DLL overrides are applied on every refresh, renaming existing files to match. |

---

## Third-Party Components

| Component | Author | Licence |
|-----------|--------|---------|
| [ReShade](https://reshade.me) | Crosire | [BSD 3-Clause](https://github.com/crosire/reshade/blob/main/LICENSE.md) |
| [RenoDX](https://github.com/clshortfuse/renodx) | clshortfuse & contributors | [MIT](https://github.com/clshortfuse/renodx/blob/main/LICENSE) |
| [Luma Framework](https://github.com/Filoppi/Luma-Framework) | Pumbo (Filoppi) | Source-available |
| [ReLimiter](https://github.com/RankFTW/Ultra-Limiter) | RankFTW | Source-available |
| [HtmlAgilityPack](https://github.com/zzzprojects/html-agility-pack) | ZZZ Projects Inc. | [MIT](https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | Microsoft / .NET Foundation | [MIT](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | Adam Hathcock | [MIT](https://github.com/adamhathcock/sharpcompress/blob/master/LICENSE.txt) |
| [7-Zip](https://www.7-zip.org/) | Igor Pavlov | [LGPL-2.1 / BSD-3-Clause](https://www.7-zip.org/license.txt) |