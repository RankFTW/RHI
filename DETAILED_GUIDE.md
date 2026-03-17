# RenoDX Commander — Detailed Guide

This document covers everything RDXC does in depth. For a quick overview, see the [README](README.md).

---

## Table of Contents

- [Layout](#layout)
- [Settings Page](#settings-page)
- [Game Detection](#game-detection)
- [Graphics API Detection](#graphics-api-detection)
- [Components](#components)
- [Vulkan ReShade Support](#vulkan-reshade-support)
- [DC Mode](#dc-mode)
- [Foreign DLL Protection](#foreign-dll-protection)
- [UE-Extended & Native HDR](#ue-extended--native-hdr)
- [Shader Packs](#shader-packs)
- [Luma Framework](#luma-framework)
- [Per-Game Overrides](#per-game-overrides)
- [INI Presets](#ini-presets)
- [Remote Manifest](#remote-manifest)
- [Update All](#update-all)
- [Auto-Update](#auto-update)
- [Data Storage](#data-storage)
- [Troubleshooting](#troubleshooting)
- [Third-Party Components](#third-party-components)

---

## Layout

RDXC offers two view modes — Detail View and Grid View — plus a global Settings page.

### Detail View

The default layout with a game list sidebar on the left and a detail panel on the right. RDXC remembers which view you were last using and restores it on launch.

### Grid View

An alternative card-based layout showing all games as a grid of cards. Toggle between views with the view switch button in the toolbar. Each card shows:

- Game name and platform icon
- Graphics API badge (e.g. DX12, VLK, DX11/12 / VLK)
- Installation status dots for RenoDX (RDX), ReShade (RS), and Display Commander (DC)
- Wiki status icon (✅ Working, 🚧 In Progress, ⚠️ May Work, ❓ Unknown, 💬 Discord-only)
- Update-available highlight border
- A Manage popout for quick access to install/uninstall/override controls without switching to Detail View

Games in Luma mode do not show a wiki status icon on the grid card.

### Compact UI Mode

A third layout option with an alphabetical game list on the left, the selected game's card and overrides in the centre, and all toolbar buttons vertically on the right. Toggle with the 📐 button. Full UI and Compact UI each remember their own window size independently.

### Toolbar

| Control | Function |
|---------|----------|
| **RDXC logo + title** | App branding (hidden in Compact mode) |
| **Add Game** | Manually add a game folder |
| **Refresh** | Rescan game library and fetch latest mod info |
| **Deploy...** | Flyout menu: Deploy DC Mode to All, Deploy Shaders to All, Update All RenoDX / ReShade / Display Commander. Deploy Shaders and Deploy DC Mode show confirmation dialogs before executing. |
| **Support** | Open the RDXC support channel on Discord |
| **Settings** | Navigate to the Settings page |
| **View toggle** | Switch between Detail View and Grid View |

### Game List Sidebar (Detail View)

- **Search box** — filters games in real-time as you type. The ✕ clear button appears as soon as you start typing.
- **Filter chips** — All Games, Favourites, Installed, Unreal, Unity, Other, RenoDX, Luma, Hidden
- **Game/installed counts** — how many games are visible and how many have mods installed
- **Game list** — each entry shows a platform icon, game name, and a green dot if updates are available

Selecting a game loads its details in the panel to the right. Favouriting or unfavouriting a game preserves scroll position. Refresh restores the previous scroll position and selection.

### Detail Panel

When a game is selected, the detail panel shows:

- **Game name** with badges for platform source, engine type (including custom engine names from the manifest), wiki status, mod author(s), UE-Extended / Native HDR
- **Graphics API badge** — shows detected rendering APIs (e.g. DX12, VLK, or multi-API combos like DX11/12 / VLK)
- **Install path** in monospace text
- **Components table** — ReShade, Display Commander, RenoDX, and Luma (when applicable), each with status, install/reinstall/update button, options menu, and uninstall button
- **Version display** — installed ReShade and DC version numbers shown directly on the component row (e.g. `6.7.3`). Purple text with version number indicates an update is available; green text after updating.
- **Rendering path toggle** — for dual-API games (DirectX + Vulkan), a toggle to choose which rendering path ReShade targets
- **Vulkan ReShade status** — shows the ReShade version number with "(Vulkan)" underneath in green when Vulkan ReShade is active
- **Overrides section** — all per-game settings inline with descriptions
- **Utility buttons** — favourite, discussion link, game info/notes (ℹ), hide/unhide, and a folder menu (open in Explorer, change install folder — opens directly in the game's current folder — reset/remove game)

When no game is selected, the panel shows a placeholder prompting you to pick one.

---

## Settings Page

Click **Settings** in the toolbar to open the Settings page. Click **← Back to Games** to return.

| Section | Contents |
|---------|----------|
| **Display Commander Mode** | DC Mode selector (Off / Mode 1 / Mode 2) with inline explanation, plus Deploy DC Mode to All button |
| **Shader Deploy Mode** | Shader mode selector (Off / Minimum / All / User) with inline explanation, plus Deploy Shaders to All button |
| **Preferences** | Skip Update Check toggle, Beta Opt-In toggle, Verbose Logging toggle — each with inline description |
| **Full Refresh** | Clears all caches and re-scans everything from disk |
| **Crash & Error Logs** | Open Logs Folder and Open Downloads Cache buttons, with paths shown inline. A new session log is created on every launch (max 10 kept). |
| **About** | Version (including beta suffix when applicable), author, disclaimer |
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

Games on a disconnected drive are preserved in the cache until the drive is reconnected. Per-platform detection failures are isolated — one store failing won't block others.

### Engine Detection

RDXC detects game engines automatically:

| Engine | Detection Method |
|--------|-----------------|
| **Unreal Engine** | Presence of Unreal-specific files and folder structures |
| **Unity** | `UnityPlayer.dll`, `Mono` folder, `MonoBleedingEdge` folder, `il2cpp` folder, `GameAssembly.dll` |
| **Custom** | Engine overrides from the remote manifest (e.g. `"Silk"`, `"Source 2"`, `"Creation Engine"`) |

Custom engine names display with a dedicated engine icon. `"Unreal"` and `"Unity"` overrides affect filter category and mod assignment; other strings are display-only and filter into Other.

### 32-bit / 64-bit Detection

RDXC automatically detects whether a game is 32-bit or 64-bit by examining the PE header of the game executable. The remote manifest can override this with `thirtyTwoBitGames` and `sixtyFourBitGames` flags, which take priority over auto-detection.

### Adding Games Manually

- **Add Game** button — enter the game name and pick the install folder.
- **Drag and drop** — drag a game's `.exe` onto the RDXC window. RDXC detects the engine type (Unreal, Unity, or Unknown), infers the game root folder by recognising store markers and engine layouts, and guesses the game name from folder structure. A confirmation dialog lets you edit the name before adding. Added games appear in their correct alphabetical position immediately.

### Drag-and-Drop

RDXC supports drag-and-drop for multiple file types:

| File Type | Behaviour |
|-----------|-----------|
| **Game `.exe`** | Opens an add-game dialog with auto-detected engine and name |
| **`.addon64` / `.addon32`** | Opens an install dialog with a game picker (auto-selects based on filename) |
| **Archives** (`.zip`, `.7z`, `.rar`, `.tar`, `.gz`, `.bz2`, `.xz`) | Extracted using bundled 7-Zip. Any addon files inside are found and offered for install. If multiple addons are found, a picker dialog lets you choose. |

Drag-and-drop works even when RDXC is running as administrator (UIPI bypass via `WM_DROPFILES`). File extensions are validated before any network or file activity.

---

## Graphics API Detection

RDXC scans game executables using PE header import table analysis to detect which graphics APIs a game uses.

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
| DX11/12 + VLK | `DX11/12 / VLK` | ✅ |
| DX11/12 + OGL | Not shown together | ❌ |
| DX9 + anything | DX9 shown alone | ❌ |
| DX10 + anything | DX10 shown alone | ❌ |
| OGL + anything | OGL shown alone | ❌ |

### Manifest API Overrides

The remote manifest supports comma-separated API tags (e.g. `"DX12, VLK"`) for games like Red Dead Redemption 2 that load Vulkan dynamically and can't be detected via PE imports alone.

---

## Components

The detail panel shows a Components section with up to four rows:

| Row | Component | Controls |
|-----|-----------|----------|
| **ReShade** | ReShade | Install / Reinstall / Update — ⋯ menu (Copy INI) — ✕ Uninstall |
| **Display Commander** | Display Commander | Install / Reinstall / Update — ⋯ menu (Copy TOML) — ✕ Uninstall |
| **RenoDX** | RenoDX Mod | Install / Reinstall / Update — UE Extended options — ✕ Uninstall |
| **Luma** | Luma Framework | Install / Uninstall (shown only in Luma mode) |

### Version Display

The status label next to ReShade and Display Commander install buttons shows the installed version number (e.g. `6.7.3`) instead of just "Installed". Falls back to "Installed" if no version information is available. When an update is available, the text turns purple and shows the current version number. After updating, it switches to the new version in green.

### Mod Author Badges

Named mods from the RenoDX wiki display the mod author as a bordered badge on the detail panel info line. Multiple authors (e.g. "oopydoopy & Voosh") each get their own badge. Generic Unreal Engine mods show "ShortFuse", UE-Extended mods show "Marat", and generic Unity mods show "Voosh".

### ReShade Detection Under Non-Standard Filenames

ReShade installations using non-standard DLL filenames (e.g. `d3d11.dll`, `dinput8.dll`, `version.dll`) are detected via binary signature scanning (`IsReShadeFileStrict`) as a fallback. Reinstalling correctly removes the old non-standard DLL before placing the new one.

---

## Vulkan ReShade Support

RDXC provides full Vulkan implicit layer support for ReShade, enabling ReShade injection for Vulkan-rendered games without per-game DLL injection.

### How It Works

1. **Global Vulkan layer** — RDXC installs ReShade as a Vulkan implicit layer via the Windows registry (`HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers`). This makes ReShade available to all Vulkan games system-wide.
2. **Layer manifest** — A bundled `ReShade64.json` manifest with correct `device_extensions` and `disable_environment` fields is deployed alongside the ReShade DLL to `C:\ProgramData\ReShade\`.
3. **Per-game INI** — A dedicated `reshade.vulkan.ini` with Vulkan-tuned depth buffer settings is deployed to each game folder.
4. **Footprint file** — An `RDXC_VULKAN_FOOTPRINT` marker file is placed in the game folder to enable managed shader deployment.

### Dual-API Games

Games detected with both DirectX and Vulkan show a rendering path toggle in the detail panel. Switching from DirectX to Vulkan automatically:

- Uninstalls DX ReShade (DLL-based)
- Uninstalls Display Commander
- Removes `reshade.ini` and managed shaders from the game folder
- Restores `reshade-shaders-original` if present

### Vulkan ReShade Status

When `reshade.ini` is present in a Vulkan game's folder, the detail panel shows the ReShade version number with "(Vulkan)" underneath in green, matching the installed styling of DLL-based ReShade games.

### Per-Game Uninstall

A ✕ button appears for Vulkan games that have `reshade.ini` deployed. Clicking it removes:

- `reshade.ini` from the game folder
- The `RDXC_VULKAN_FOOTPRINT` file
- Managed `reshade-shaders` folder
- Restores `reshade-shaders-original` if present

This does not affect the global Vulkan layer — only the per-game artifacts.

### Vulkan DC Mode Defaults

Vulkan games default to DC Mode Off unless explicitly overridden by the user or manifest. The DC Mode dropdown in overrides shows "Exclude (Off)" for Vulkan games and updates automatically when switching rendering path to Vulkan.

### Footprint and Shader Deployment

The `RDXC_VULKAN_FOOTPRINT` file controls shader deployment to Vulkan game folders:

- **Present** → shader sync deploys `reshade-shaders/` to the game folder
- **Absent** → shader sync skips the game folder
- **DC installed** → footprint is automatically removed (DC manages shaders globally)
- **DC uninstalled** → footprint is restored so shaders deploy correctly again

---

## DC Mode

DC Mode controls how Display Commander loads alongside ReShade. Configure it on the Settings page.

| Mode | ReShade | DC Installed As |
|------|---------|-----------------|
| **OFF** (default) | `dxgi.dll` in the game folder | `zzz_display_commander.addon64` |
| **Mode 1** | Not in game folder — DC loads ReShade from shared folder | `dxgi.dll` |
| **Mode 2** | Not in game folder — DC loads ReShade from shared folder | `winmm.dll` |

When DC Mode is active, ReShade is synced to `%LOCALAPPDATA%\Programs\Display_Commander\Reshade\` and Display Commander loads it from there at runtime. Per-game ReShade installs are removed automatically. Individual games can override the global DC Mode via the Overrides section.

The DC AppData shader folder sync always uses the global shader deploy mode, not per-game overrides. Per-game shader overrides only apply to standalone ReShade game folders.

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

When installing ReShade or DC as `dxgi.dll` (or `winmm.dll` in Mode 2), RDXC checks whether an existing file belongs to another tool (DXVK, Special K, ENB, etc.) using binary signature scanning. The scan matches on `reshade.me` or `crosire` strings unique to the actual ReShade binary, and rejects files over 15 MB as too large to be ReShade. This prevents tools like OptiScaler from being misidentified.

If the existing file is unidentified, a confirmation dialog asks whether to overwrite. During Update All, foreign files are silently skipped.

---

## UE-Extended & Native HDR

Unreal Engine games with native HDR are automatically assigned UE-Extended via the remote manifest. These display "Extended UE Native HDR" as their engine badge. In-game HDR must be turned on for UE-Extended to work.

The UE-Extended toggle now appears for every Unreal Engine game that does not have a named mod on the RenoDX wiki, not just games explicitly listed in the manifest. A compatibility warning dialog pops up when enabling UE-Extended, advising that not all games are compatible and to check the Notes section for any game-specific information.

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
| DC Mode OFF (DLL ReShade) | `<game folder>\reshade-shaders\Shaders\` and `\Textures\` |
| DC Mode OFF (Vulkan ReShade) | `<game folder>\reshade-shaders\Shaders\` and `\Textures\` (requires `RDXC_VULKAN_FOOTPRINT`) |

User-owned shader folders are preserved by renaming to `reshade-shaders-original` before deployment and restored when the shader mode is set to Off or when Vulkan ReShade is uninstalled.

### Shader Sync After DC Removal

When Display Commander is uninstalled from a game that still has ReShade installed, RDXC automatically detects the `rsInstalled && !dcInstalled` scenario and deploys shaders to the game folder using the effective shader mode. For Vulkan games, the footprint file is also restored.

---

## Luma Framework

[Luma Framework](https://github.com/Filoppi/Luma-Framework) by Pumbo (Filoppi) is a DX11 modding framework adding HDR support via the ReShade addon system. RDXC detects Luma-compatible games and shows a toggle badge in the detail panel. Luma badges appear on all eligible game cards by default.

### How Luma Mode Works

- RenoDX, ReShade, and Display Commander are **automatically uninstalled** and hidden. Only **Install Luma** is available.
- Installing Luma deploys the mod zip, `reshade.ini`, and Lilium HDR shaders — everything self-contained.
- Uninstalling or toggling off removes all Luma files.
- The ℹ popup shows Luma-specific notes from the wiki and remote manifest.
- Overrides disables "Exclude from wiki" while Luma is active.
- Games listed in the remote manifest automatically start in Luma mode on first detection.

Luma downloads are restricted to trusted GitHub URLs under `https://github.com/Filoppi/`.

---

## Per-Game Overrides

The Overrides section appears below Components in the detail panel. A hint message "You must press Save for changes to apply." is displayed between the Reset and Save buttons.

| Override | Effect |
|----------|--------|
| **Game name (editable)** | Rename the game — persists across Refresh and restarts, including for games with custom folder overrides |
| **Wiki mod name** | Match to a different wiki entry. Also applies to Luma matching. |
| **↩ Reset** | Restore original name and clear wiki mapping |
| **DLL naming override** | Custom filenames for ReShade and DC via dropdown combo boxes with common DLL suggestions (`dxgi.dll`, `d3d11.dll`, `dinput8.dll`, `version.dll`, `winmm.dll`, `d3d12.dll`, `xinput1_3.dll`, `msvcp140.dll`, `bink2w64.dll`, `d3d9.dll`). Existing installs are renamed in place — no reinstall needed. Takes priority over any manifest-defined DLL names. |
| **Global update inclusion** | Three toggle switches (ReShade, DC, RenoDX) controlling whether the game is included in bulk updates for each component. All default to On. Legacy single-toggle settings are auto-migrated. |
| **DC Mode** | Follow Global / Exclude (Off) / Force Mode 1 / Force Mode 2. Vulkan games default to Exclude (Off). |
| **Shader Mode** | Global / Off / Minimum / All / User. Per-game shader mode only applies when DC Mode is OFF. |
| **Rendering Path** | For dual-API games: DirectX or Vulkan. Switching from DirectX to Vulkan triggers automatic cleanup of DX artifacts. |
| **Wiki exclusion** | Exclude the game from wiki lookups |
| **Reset Overrides** | Reset all override settings back to defaults (game name, wiki name, DC mode, shader mode, DLL override, all three update toggles, and wiki exclusion) |
| **Save Overrides** | Apply changes and refresh status |

In Compact mode, the previously selected game card is automatically re-selected after saving overrides.

---

## INI Presets

RDXC bundles a default `reshade.ini` seeded on first launch. It's deployed alongside ReShade on every install using a merge strategy — template keys always take precedence, but any game-specific settings not in the template (e.g. addon configs, effect toggles, custom keybinds) are preserved.

Config files in `%LOCALAPPDATA%\RenoDXCommander\inis\`:

| File | Copied When |
|------|-------------|
| `reshade.ini` | Every ReShade or DC install, or via 📋 button on the ReShade row. Merged into existing INI if present. |
| `reshade.vulkan.ini` | Deployed alongside `reshade.ini` for Vulkan games. Contains depth buffer preprocessor definitions tuned for Vulkan rendering. |
| `ReShadePreset.ini` | Automatically alongside `reshade.ini` if the file exists in the inis folder |
| `DisplayCommander.toml` | Via 📋 button on the Display Commander row |

To use a custom ReShade preset, place your `ReShadePreset.ini` in the inis folder. It will be copied to every new game install automatically.

The 📋 INI button deploys both `reshade.ini` and `reshade.vulkan.ini` when a game has Vulkan support.

---

## Remote Manifest

RDXC fetches a remote manifest from GitHub on every launch, providing game-specific overrides without app updates. The manifest is fetched from the GitHub API with a `raw.githubusercontent.com` fallback, and cached locally for offline use.

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
| **DLL name overrides** | Set ReShade and/or DC install filename per game (e.g. `"Mirror's Edge": { "reshade": "d3d9.dll", "dc": "winmm.dll" }`). User-set overrides take priority. Games with manifest DLL overrides show the toggle turned on automatically with filenames pre-filled. |
| **API overrides** | Comma-separated API tags (e.g. `"DX12, VLK"`) for games that can't be detected via PE imports |
| **Snapshot URL overrides** | Direct addon download URL when wiki lacks one |
| **DC mode overrides** | Force a specific DC mode level per game |
| **Luma default games** | Games that auto-start in Luma mode on first detection |
| **Shader packs** | Per-game shader pack configuration |
| **"Extended UE" tag** | Automatically assigns UE-Extended addon and marks as native HDR |

---

## Update All

The Deploy... flyout provides three Update All actions for ReShade, Display Commander, and RenoDX across all eligible games. Each component respects its own per-game inclusion toggle — a game excluded from ReShade updates can still receive DC and RenoDX updates. Games with DLL overrides or foreign DLLs are also skipped. Updates are flagged by comparing stored file sizes against remote sources.

Deploy Shaders and Deploy DC Mode show confirmation dialogs before executing bulk operations.

---

## Auto-Update

RDXC checks for new versions on launch by querying the GitHub Releases API. Disable via Settings → Preferences → Skip update check on launch.

### Stable and Beta Channels

When Beta Opt-In is enabled in Settings, RDXC checks both the stable release (`RDXC` tag) and the beta release (`RDXC-BETA` tag). The version resolver determines which update to offer:

- Stable always wins over beta at the same or higher base version
- Beta is only offered when its base version exceeds the latest stable, OR when the current app is already on a beta and a newer beta at the same base version is available
- No update is offered if all candidates are at or below the current version

The app encodes its beta status in the 4th component of the assembly version: `1.4.8.0` = stable, `1.4.8.1` = beta 1, `1.4.8.2` = beta 2, etc.

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
| `inis\` | Preset config files (`reshade.ini`, `reshade.vulkan.ini`, `ReShadePreset.ini`, `DisplayCommander.toml`) |
| `reshade\` | Staged shader packs and custom shaders |
| `logs\` | Session logs (timestamped, max 10 kept) and crash reports |

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Game not detected | Click **Add Game** in the toolbar or drag the game's `.exe` onto the window |
| Xbox games missing | Click **Refresh** — RDXC uses the PackageManager API |
| ReShade not loading | Check the install path via 📁 — `dxgi.dll` must be next to the game exe |
| ReShade not detected | If using a non-standard DLL name, RDXC should detect it via binary signature scanning. Try **Refresh**. |
| Black screen (Unreal) | ReShade → Add-ons → RenoDX → set `R10G10B10A2_UNORM` to `output size` |
| UE-Extended not working | Turn on in-game HDR — UE-Extended requires native HDR output |
| Downloads failing | Click **Refresh**, or clear cache from Settings → Open Downloads Cache |
| Foreign DLL blocking install | Choose **Overwrite** in the dialog, or cancel to keep the existing file |
| Games/mods out of sync | Settings → **Full Refresh** to clear all caches |
| Drag-and-drop not working | Ensure RDXC is running. Drag-and-drop uses Win32 shell handling and works even as administrator. |
| Vulkan ReShade not showing as installed | Check that `reshade.ini` exists in the game folder. The Vulkan layer must also be installed globally. |
| Shaders missing after DC uninstall | Click **Refresh** or **Deploy Shaders** — RDXC will detect the missing shaders and redeploy them. For Vulkan games, the footprint file is also restored. |
| Auto-update not triggering for beta | Ensure Beta Opt-In is enabled in Settings. The beta release on GitHub must have a parseable version in the title (e.g. "RDXC 1.4.8 beta 2") and the asset must be named `RDXC-Setup.exe`. |
| Games showing as installed after manual file removal | Click **Refresh** — RDXC verifies files exist on disk and cleans up stale records. |
| DLL override not applying from manifest | Click **Refresh** — manifest DLL overrides are applied on every refresh, renaming existing files to match. |

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
