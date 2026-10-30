# RHI — Detailed Guide

This guide covers every feature in RHI (ReShade HDR Installer). For a short overview, see the [README](../README.md). For help, join the [Discord](https://discord.gg/ultraplace).

> **Single-player only.** ReShade with addon support, OptiScaler and DLSS/Streamline changes can trigger anti-cheat in online games. Uninstall them before playing multiplayer.

---

## Contents

1. [Getting Started](#getting-started)
2. [The Main Window](#the-main-window)
3. [Game Detection](#game-detection)
4. [Components](#components)
5. [ReShade](#reshade)
6. [RenoDX](#renodx)
7. [RTX HDR and Auto HDR](#rtx-hdr-and-auto-hdr)
8. [Luma Framework](#luma-framework)
9. [RE Framework](#re-framework)
10. [DOF Fix](#dof-fix)
11. [Frame Limiters](#frame-limiters)
12. [Shader Packs](#shader-packs)
13. [ReShade Addons](#reshade-addons)
14. [ReShade Presets](#reshade-presets)
15. [DLSS / Streamline](#dlss--streamline)
16. [Neural Rendering (DLSS 5)](#neural-rendering-dlss-5)
17. [Driver Settings and Multi Frame Generation](#driver-settings-and-multi-frame-generation)
18. [Extras](#extras)
19. [OptiScaler](#optiscaler)
20. [DXVK and dgVoodoo2](#dxvk-and-dgvoodoo2)
21. [Game Overrides](#game-overrides)
22. [Management](#management)
23. [Launching Games](#launching-games)
24. [Settings Page](#settings-page)
25. [Updates](#updates)
26. [Drag-and-Drop and Addon Detection](#drag-and-drop-and-addon-detection)
27. [Vulkan Games and Ryubing](#vulkan-games-and-ryubing)
28. [Links: Nexus, PCGamingWiki, UW Fix, Ultra+](#links-nexus-pcgamingwiki-uw-fix-ultra)
29. [Remote Data](#remote-data)
30. [Data Storage and Logs](#data-storage-and-logs)
31. [Troubleshooting](#troubleshooting)
32. [Third-Party Components](#third-party-components)

---

## Getting Started

### Requirements

- Windows 10 or 11 (64-bit).
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). The installer also installs the Windows App Runtime if it's missing.
- An NVIDIA GPU for DLSS presets, driver settings and Neural Rendering. ReShade, RenoDX, Luma and OptiScaler work on any GPU.

### First launch

The first time RHI starts, a **Welcome to RHI** window asks how you want ReShade handled:

- **Manage ReShade for me**: the normal setup. RHI installs, updates and configures ReShade and shaders.
- **I'll manage it myself**: ReShade is excluded from Update All, Cache All Shaders is turned off and global shaders are set to Off. You can change any of these later in Settings.

RHI then scans your library. Later launches show the cached library immediately and rescan in the background.

### Add a GitHub token (recommended)

Most of what RHI downloads comes from GitHub. Without a token, GitHub allows 60 requests an hour, and once that's used up, downloads and update checks fail until the hour resets. A token raises the limit to 5,000.

1. Go to [github.com/settings/tokens](https://github.com/settings/tokens) and create a **classic** token. Don't tick any boxes; it needs no permissions.
2. In RHI, open **Settings → GitHub API**, paste the token (it starts with `ghp_` or `github_pat_`) and click **Save**.

RHI checks the token, stores it in `%LOCALAPPDATA%\RHI\github_api.txt` and uses it straight away, including after restarts. **Clear** removes it. The token is only ever sent to `api.github.com`. There is also a **Connect GitHub** sign-in option, but a token is more reliable, and a saved token takes priority over the sign-in.

### Quick Start

The **Quick Start** toolbar button opens an in-app walkthrough: selecting a game, installing ReShade, RenoDX and Luma, shaders, DOF Fix, frame limiters, DLSS/Streamline, OptiScaler, settings, NVIDIA driver settings, Vulkan games, adding games manually, updating, and Full Refresh. **← Back to Games** returns to your library.

---

## The Main Window

![The RHI main window](../screenshots/game_view.png)

RHI has one layout: the game list on the left, the selected game's detail panel on the right, the toolbar on top and the status bar at the bottom. Settings, About and Quick Start open as full pages with a **← Back to Games** button. The window remembers its size, position and maximised state. Its minimum width is 1220 pixels.

### Toolbar

| Button | What it does |
|---|---|
| **New Mods Available** | Only shown when new RenoDX, Luma or Ultra+ mods have appeared since you last looked. Lists them; **Dismiss** hides the button until more arrive. |
| **Quick Start** | Opens the in-app Quick Start guide. |
| **Available Mods** | A searchable list of every supported game with **RenoDX**, **Luma** and **Ultra+** columns. ✓ = complete, 🔨 = in progress, ✗ = none. Click a tick to open that mod's page. Type `RenoDX`, `Luma` or `Ultra+` at the start of the search to show only that type, optionally followed by a name (e.g. `luma dead`). Includes Source engine games supported by RenoDX's Source addon; RHI lists these but can't install them yet. Disabled until mod data has loaded. |
| **Refresh** | Rescans your library and fetches the latest mod information. |
| **Shaders/Addons** | **Global Shaders** (shader pack picker) and **Global Addons** (ReShade addon manager). |
| **Update All** | Updates installed components across your library. Highlighted when updates are available. See [Updates](#updates). |
| **Links** | RenoDX Wiki, Luma Wiki, RHI GitHub, ReLimiter GitHub. |
| **Help** | Discord, Guide (this page), Ko-fi, About. |
| **Donate** | "Support the Mod Authors": the mod authors whose work RHI installs, with a Ko-fi button for each. |
| **Settings** | Opens the [Settings page](#settings-page). |

### Sidebar

- **Search box** ("Filter games..."): filters live by game name, store, engine, graphics API, bitness, and RenoDX or Luma mod name and author. It also understands keywords such as `DLSS`, `Ray Reconstruction`, `Frame Generation`, `Streamline`, `RE Engine`, `RE Framework`, `32-bit`, `64-bit`, `UW Fix` and `Ultra+`.
- **+** buttons next to the search box:
  - The green **+** (**Manually add a game**): see [Adding a game manually](#adding-a-game-manually).
  - A second **+** appears while you're searching: **Save current search as a custom filter** saves the search as a named chip (like the teal DLSS, Vulkan or Xbox chips). Click the chip to toggle it; right-click it to delete it.
- **Filter chips**: **All Games**, **Installed** (games with ReShade installed), **Favourites** and **Hidden** pick one view. **Unreal**, **Unity**, **Other**, **RenoDX** and **Luma** narrow it further and can be combined. Hidden games only appear under Hidden. Your selected filter is remembered.
- A count line: games shown, games with ReShade, and hidden games.
- Each game row shows its store icon and name, a **green dot** when any of its components has an update (unless excluded from Update All), and turns **green** while a game launched through RHI is running.
- **Double-click** a game to launch it. **Right-click** for Open Folder, Favourite, Hide/Show, Discussion / Instructions and View Notes (when available), and **Create Desktop Shortcut**.

### Detail panel

**Header**

- Game name and install path, with mod author badges (click to open their Ko-fi page where known).
- **▶ Launch**, then **HDR** and **RES** (per-game HDR and resolution auto-toggle; purple means on).
- **Nexus**, **PCGW**, **UW Fix** and **Ultra+** link buttons, shown when a page exists for the game.
- Store and engine badges. Click the engine badge on games whose Unreal version is unclear to switch between "Unreal Engine" and "Unreal Engine 5" (this controls [DOF Fix](#dof-fix) eligibility).
- Installed RenoDX/Luma addon names with a ✓ (complete) or 🔨 (in progress) status icon, wiki status, graphics API badges and a 32-bit/64-bit badge.
- **Hide/Show**, **Favourite**, **Config** (opens the game's config folder, shown for games where RHI can find it) and **Browse** (opens the game folder).

**Sections**

Below the header are seven sections:

1. **Components**: ReShade, HDR mods, DOF Fix and frame limiters.
2. **Game Overrides**: per-game settings.
3. **Neural Rendering**: DLSS 5 tools.
4. **DLSS / Streamline**: DLSS versions, presets and render scale.
5. **Driver Settings**: per-game NVIDIA driver settings.
6. **Management**: install folder, reset and report tools.
7. **Extras**: ASI Loader, OptiScaler, frame generation unlocks, DXVK and dgVoodoo2.

Click a section heading to collapse or expand it. Drag the **≡** handle to reorder sections. A collapsed section shows a one-line summary, for example installed versions or active overrides. Order, collapsed state and summaries are remembered.

### Status bar

- **Left**: what RHI is doing.
- **Centre**: an anti-cheat reminder.
- **Right**:
  - The **version number**. Click it to check for an app update.
  - **Patch Notes**.
  - **MOTD**: shows the current message of the day again.
  - **Updates**: a dated history of every component update, showing old → new versions. It persists between sessions and can be cleared.

---

## Game Detection

### Supported stores

| Store | How it's detected |
|---|---|
| Steam | `libraryfolders.vdf` and app manifests in every library folder |
| Epic Games | The classic launcher's manifests and the new Epic Games Launcher's install list |
| GOG | Registry |
| EA App | Install records |
| Ubisoft Connect | Registry and install folders. Ubisoft games use their folder name (e.g. `Watch_Dogs2`) |
| Xbox / Game Pass | Installed packages |
| Battle.net | Install records |
| Rockstar | Install records |

Every launch shows your cached library first, then rescans the stores and merges in new games. Games that disappear from a scan (for example on a disconnected drive) stay in the list. If one store fails, the others are still scanned.

### Engine detection

RHI identifies the engine from the game's files:

- **Unreal Engine**: UE4/UE5 folder layout, with the version read from the game's files where possible. UE1–3 games show as **Unreal (Legacy)**.
- **Unity**: `UnityPlayer.dll` and Unity data folders.
- **RE Engine**: RE Engine data files.
- **Other**: anything else. RHI then uses PCGamingWiki data or the remote manifest to label the engine where it can (e.g. Frostbite, REDengine, Source).

Games with an unknown engine aren't fully rescanned on every launch, only when their folder changes or on Full Refresh.

### Graphics API detection

RHI reads each game executable's imports to find its graphics API and bitness. Badges: **DX8**, **DX9**, **DX10**, **DX11**, **DX12**, **VLK** (Vulkan) and **OGL** (OpenGL), combined when a game uses more than one (e.g. `DX11/12`, `DX11/12 / VLK`). Unity games are read from their `boot.config`. When the executable can't be read (Game Pass, protected folders), RHI uses PCGamingWiki data. The remote manifest and your own **Graphics API** override always take priority.

The API decides the ReShade DLL name, whether DXVK and generic Luma are offered, and which Neural Rendering methods are available.

### Adding a game manually

- Click **+** (Manually add a game) next to the search box, choose the game's `.exe`, give it a name and click **Add Game**. Or drag the `.exe` onto the window.
- Manually added games show the store **Manual**.
- To remove one, use **Management → Reset / Remove game**.

### Renaming, hiding and favourites

- **Rename**: Game Overrides → **Game name (editable)**, then press Enter. **Reset** goes back to the detected name. Your settings move with the game.
- **Hide** and **Favourite**: header buttons or the right-click menu. Saved per game and store.
- Some multi-game folders (collections) are split into separate entries by the remote manifest.

---

## Components

The **Components** section lists, in order:

- **RE Framework** (RE Engine games only).
- **ReShade**.
- **HDR Mods**: **RenoDX HDR** and **Luma** (when a Luma mod exists).
- **Recommended**: **DOF Fix** (eligible Unreal Engine 5 games).
- **Frame limiters — Choose one**: **ReLimiter** and **Display Commander**.

OptiScaler, DXVK and the frame generation tools are in the [Extras](#extras) section.

### Row controls

Each row has a status/version label, **Info**, an install button, **⚙** (settings) and **✕** (uninstall).

- **Status**: green and underlined when installed (click to open the release page), purple **Update** when an update is available, **Ready** when not installed.
- **Install button**: shows **Install**, **Reinstall**, **Update** (purple), **Installing...**, or **⚠ ReShade required**.
- **Info**: blue when there's information for this game, greyed out when there isn't. A **◄** on the install button means Info has game-specific notes worth reading first.

### Dependencies

- RenoDX, Luma, ReLimiter, Display Commander and DOF Fix need ReShade. This is skipped if you've excluded ReShade from Update All for that game (for example, because you manage ReShade yourself).
- On RE Engine games, RE Framework must be installed before ReShade.
- ReLimiter and Display Commander can't both be installed.
- On the **No Addons** ReShade channel, RenoDX, ReLimiter and Display Commander are unavailable.

---

## ReShade

### Where it comes from

RHI downloads ReShade with addon support from reshade.me, and the Nightly build from ReShade's GitHub builds. Both are cached so installs are instant.

### DLL name

RHI names the ReShade DLL from the game's API and bitness:

| Game API | ReShade file |
|---|---|
| DX10 / DX11 / DX12 | `dxgi.dll` |
| DX9 | `d3d9.dll` |
| DX8 | `d3d8.dll` |
| OpenGL | `opengl32.dll` |
| Vulkan | Global Vulkan layer (see [Vulkan Games](#vulkan-games-and-ryubing)) |

To use a different name, pick it under Game Overrides → **DLL naming overrides → ReShade**. If OptiScaler or Display Commander already uses ReShade's name, RHI installs ReShade as `ReShade64.dll`/`ReShade32.dll` so they can load it.

### ReShade Channel

Set per game in Game Overrides → **ReShade Channel**:

| Channel | What it does |
|---|---|
| **Stable** (default) | Latest release with addon support. |
| **Nightly** | Latest development build. Only re-downloaded when it changes. |
| **Custom** | Your own ReShade DLL. Put it in `%LOCALAPPDATA%\RHI\Custom\ReShade\` and choose it in the "Select Custom ReShade" dialog. If you replace the file, RHI redeploys it on the next refresh or update check. |
| **No Addons** | Standard ReShade without addon support. Switching removes addon ReShade, RenoDX, ReLimiter and Display Commander. Shaders still work. |
| **Legacy...** | Pick an older version. Legacy games are excluded from ReShade updates. |

On Vulkan games, changing the channel applies to all Vulkan games (they share one layer), after you confirm.

### ReShade ⚙ settings

Available once the game has a `reshade.ini`:

- **Deploy ReShade.ini**: merges RHI's template into the game's ini. Template keys replace matching keys; keys only in the game's ini are kept. Applies your hotkeys, screenshot folder and peak brightness.
- **Deploy ReShadePreset.ini**: copies `%LOCALAPPDATA%\RHI\inis\ReShadePreset.ini` to the game.
- **Open ReShade.ini**, **Open ReShade.log**, **Copy ReShade.log to clipboard**.
- **Overlay Key** and **Screenshot Key** for this game.
- **Keep ReShade.ini Updated** (Yes/No): when **No**, RHI never changes this game's `reshade.ini` on install, update or any "Apply to All Games" action.

The `reshade.ini` template lives in `%LOCALAPPDATA%\RHI\inis\`. You can edit it; RHI won't overwrite your changes.

### Global ReShade settings

In **Settings → ReShade & Display**:

- **Screenshots & Hotkeys**: screenshot folder (with optional per-game subfolders), Overlay key, Screenshot key, effect list style (Tabs/Tree), and **Apply to All Games**.
- **HDR & Peak Brightness**: your display's peak brightness in nits (type it or click **Auto**). It's written as `ToneMapPeakNits` to RenoDX presets on every install, update and ini deploy. The ⚙ chooses whether that happens automatically and which presets (1, 2, 3) get it. **Apply to All Games** writes it everywhere now.

"Apply to All Games" skips games with **Keep ReShade.ini Updated** set to No.

### Foreign DLL protection

If a game folder already has a `dxgi.dll` (or other ReShade name) that isn't ReShade, such as DXVK, Special K or ENB, RHI warns you and asks before overwriting. Anything it does overwrite is backed up first.

### Game Pass games

RHI can't write inside `C:\Program Files\WindowsApps\`. For Game Pass games installed elsewhere, if a game update moves the game to a new versioned folder, RHI reinstalls ReShade there automatically.

---

## RenoDX

[RenoDX](https://github.com/clshortfuse/renodx) mods replace a game's tonemapping to give true HDR output. They load as ReShade addons.

### Where mod data comes from

By default, RHI uses the **RHI Database**: a curated list of named mods, UE-Extended games, Unity games and Source engine games. You can switch to the **RenoDX Wiki** in Settings → **Mod Data Source**.

### How mods are matched

RHI matches each game by name (ignoring ™ and ® symbols). If the match is wrong or missing, set **Wiki mod name** in Game Overrides. To stop a game matching at all, set **Wiki lookup** to **Excluded**.

When there's no named mod:

| Game | Fallback |
|---|---|
| Unreal Engine 4/5 | **UE Extended** (Marat's addon). Games with native HDR get **UE Extended Native HDR**. |
| Unity | **Generic Unity** (NotVoosh's addon, 32-bit and 64-bit). RHI also writes per-game settings from the database. |

Some mods are hosted outside GitHub. For these the button reads **Download from Nexus** (or Discord) and opens the page. Nexus-hosted mods are still checked for updates.

The ✓ or 🔨 next to the installed addon name shows whether the mod is complete or still in progress.

### RenoDX ⚙ settings

- **UE-Extended Settings** (Unreal games):
  - **UE-Extended** (Off/On): switch between UE-Extended and the generic UE addon.
  - **Set Maximum Nits** (with **Auto**): peak brightness for this game only.
  - **Upgrade Path**.
  - **Engine.ini HDR** (Off/On): writes Engine.ini HDR settings for games without an in-game HDR option. Turn it off for SDR.
  - **LUT Update Every Frame** (Off/On).
- **Compatibility Settings**: dropdowns for the RenoDX compatibility keys in this game's `reshade.ini` (swapchain proxy, encoding, resource upgrades and so on). Run the game once with RenoDX installed to generate them.
- **RenoDX Presets**: **Export Presets** saves this game's RenoDX presets to `RHI-RenoDX-Preset.txt` in the game folder and copies them to the clipboard. **Import Presets** restores them.
- **RTX HDR** and **Auto HDR**: see [below](#rtx-hdr-and-auto-hdr).

### UE-Extended auto-configuration

When UE-Extended is installed, RHI:

- Adds the required `[renodx]` keys to `reshade.ini` (only missing keys; yours are kept).
- Writes the HDR keys to the game's `Engine.ini`, in the game's config folder, and makes the file read-only so the game doesn't overwrite it. Some games use a custom Engine.ini supplied by the manifest instead.

Turning UE-Extended off removes these changes.

---

## RTX HDR and Auto HDR

Both are alternatives to a RenoDX mod, so turning either on **uninstalls RenoDX** for that game. Only one of the two can be on at a time. Both are in the RenoDX ⚙ menu.

### RTX HDR

NVIDIA's driver-level SDR-to-HDR conversion. It requires an NVIDIA RTX GPU and the NVIDIA App with its overlay and Game Filters enabled.

- **Enable RTX HDR** writes starting values to the game's NVIDIA profile: peak brightness from your global Peak Nits, Contrast +25 (gamma 2.2), Saturation −25, middle grey Auto.
- The install button becomes **Configure RTX HDR**, which opens **RTX HDR Settings**:
  - **Peak Brightness**: 400–2000 nits.
  - **Contrast**: −100 to +100, with Gamma 2.0 / 2.2 / 2.4 shortcuts.
  - **Middle Grey**: 10–100, or Auto.
  - **Saturation**: −100 to +100.
  - **Debanding**: No / Low / High, plus indicator modes. Changing it needs admin.
  - **Save as Default** / **Set Default**.
- Turning RTX HDR off removes all its settings from the profile.

### Auto HDR

Windows 11 Auto HDR, forced on for this game through the registry. It works on any GPU, even with Windows' global Auto HDR setting off.

- **Enable Auto HDR**. The install button then becomes **Configure Auto HDR**, with a brightness slider from 0 (no boost) through 50 (default) to 100 (about 1000 nits).
- Changes take effect the next time the game starts.

---

## Luma Framework

[Luma](https://github.com/Filoppi/Luma-Framework) mods rebuild a game's rendering for HDR, often with DLSS support added.

### Which games get Luma

- **Named mods** from the Luma wiki, GitHub releases and Nexus Mods. Nexus-only mods show **Get on Nexus Mods** and are checked for updates.
- **Generic Luma** for DX11 Unreal Engine games (including Legacy Unreal). UE5 games only get it if you set their Graphics API override to DX11. Games the Luma wiki marks as incompatible are skipped.

### Installing

Click **Install Luma** (ReShade must be installed). RHI:

1. Extracts the mod into the game folder.
2. Deploys its newest `nvngx_dlss.dll` (backing up the game's own).
3. Sets up ReShade and `reshade.ini` for Luma.
4. Installs dgVoodoo2 on DX9 games that need it.
5. For generic Luma: applies the Engine.ini keys and launch arguments from the Luma wiki (or `-dx11` on games with both DX11 and DX12), plus TAA settings.

The version shows as **Build N**. Luma mods that were installed by dropping an archive can't be reinstalled automatically: drop the archive onto the window again.

### Luma ⚙ settings (Unreal games)

- **HDR on First Boot** (Default/Off/On).
- **TAA Settings** (Off/On).

### Luma with RenoDX

Normally a game uses either Luma or RenoDX. Generic Luma games, and games the manifest marks as compatible, can have both.

### Uninstalling

Uninstalling Luma removes its files, restores the original `nvngx_dlss.dll`, resets `reshade.ini`, and removes the launch argument and Engine.ini keys it added. ReShade and your shaders stay installed.

---

## RE Framework

For RE Engine games (Resident Evil, Monster Hunter, Devil May Cry, Street Fighter 6 and others), [RE Framework](https://github.com/praydog/REFramework-nightly) is installed as `dinput8.dll` and must be installed before ReShade. RHI downloads the nightly build and keeps it updated.

On some games with OptiScaler installed, the manifest switches RE Framework to the PD-Upscaler build.

**⚙ RE Framework Settings → Delete _storage_ folder** clears RE Framework's saved data for the game.

---

## DOF Fix

A fix for the depth-of-field in Unreal Engine 5.0–5.6 games. It appears under **Recommended** in Components for 64-bit UE 5.0–5.6 games and any game the manifest adds. It needs ReShade and is included in Update All. **Info** shows its release notes.

If a game's engine badge just says "Unreal Engine", click the badge to mark it as Unreal Engine 5 and make DOF Fix available.

---

## Frame Limiters

Two ReShade-addon frame limiters sit under **Frame limiters — Choose one**. You can install one per game. Both:

- need ReShade, and aren't available on the No Addons channel;
- support 32-bit and 64-bit games;
- update automatically and are included in Update All;
- turn off the NVIDIA driver frame limiter for that game when installed (uninstalling restores the global setting).

### ReLimiter

[ReLimiter](https://github.com/RankFTW/ReLimiter) is a frame pacing limiter with an on-screen display.

- Installs the addon plus `relimiter.ini` (from RHI's template, if the game doesn't have one).
- **Settings → Component Settings → ReLimiter**: OSD key, **Shared Presets** (all games share one OSD presets file), **DLSS Hooks** (shows DLSS info on the OSD; turn off if a game crashes), **Target FPS** (VRR caps from 59 FPS at 60 Hz up to 431 FPS at 500 Hz, or Custom 20–1000), and **Apply to All Games**.
- **⚙ ReLimiter Settings**: Deploy relimiter.ini, open or copy the ReLimiter log, and per-game **Target FPS** and **DLSS Hooks**.

### Display Commander

[Display Commander](https://github.com/pmnoxx/display-commander) is an alternative limiter with display tools. It installs as `zzz_display_commander.addon64`/`.addon32` (or the name set in DLL naming overrides) with `DisplayCommander.ini`. **⚙ → Deploy DisplayCommander.ini** resets its ini from RHI's template.

---

## Shader Packs

RHI manages 56 ReShade shader packs. 50 are built in, and the remote manifest can add, update or disable packs without an app update.

| Category | Packs |
|---|---|
| **Essential** | Lilium HDR Shaders (needed for most HDR tools) |
| **Recommended** | crosire reshade-shaders, PumboAutoHDR, MaxG2D Simple HDR Shaders, clshortfuse ReShade shaders, RenoFX HDR Toolkit, UltraShade |
| **Extra** | Everything else, including iMMERSE, qUINT, METEOR, SweetFX, OtisFX, prod80, CorgiFX, ZenteonFX, CShade, CRT-Royale, VRToolkit, GShade-Shaders, QD-OLED APL Fixer, LumaBoost, Glamarye Fast Effects, smolbbsoop, potatoFX and more |

### Global shader mode

**Settings → Shaders & Addons → Shader Management**:

- **RHI Managed**: RHI deploys your selected packs to every game.
- **Custom**: RHI deploys only your own shaders.
- **Off**: RHI doesn't deploy shaders unless a game has its own override.

**Cache All Shaders** (On by default) downloads every pack at startup so installs are instant. Turn it off to download packs only when needed.

### Choosing packs

Toolbar **Shaders/Addons → Global Shaders** opens **Select Shader Packs**:

- Search packs and individual shaders.
- Tick a whole pack, or expand it and tick single `.fx` files. A partly ticked pack shows a mixed checkbox. Shaders a pack depends on are ticked automatically.
- **Expand All**, **Collapse All**, **Deselect All**.
- A **Custom Shaders** section lists your own files.
- **Deploy** saves the selection and copies it to your games. Only new or changed files are copied.

### Shader profiles

The **Profiles** panel in the picker saves named selections:

- **New**, **Save**, rename and delete.
- **Export** zips the profile and copies it to the clipboard, ready to paste into Discord. **Import** loads one.
- **Open Custom Folder** opens your custom shaders folder.

### Per-game shaders

Game Overrides → **Shaders**:

| Mode | Effect |
|---|---|
| **Global** | Follows the global setting. |
| **Custom** | Only your custom shaders. |
| **Select** | Opens the picker for this game only. You can load a profile here; choose Select again to change it. |
| **Off** | No shaders for this game. |

Per-game **Custom** and **Select** still work when the global mode is Off.

### Custom shaders

Put your own shaders in `%LOCALAPPDATA%\RHI\reshade\Custom\Shaders\` and textures in `%LOCALAPPDATA%\RHI\reshade\Custom\Textures\`. Subfolders are kept.

---

## ReShade Addons

### Addon manager

Toolbar **Shaders/Addons → Global Addons** opens the **ReShade Addon Manager**. It lists the official ReShade addon list plus extras added by RHI, such as RenoDX DevKit, DLSS Fix, ReShade HDR Metadata, RenoDX Upgrade, Depth3D Cursor Add-on and ReShade Screenshot Discord Fix. Each entry links to its repository and usage notes. Addons that can't be used together are blocked with a message telling you which one to turn off first.

Tools that RHI manages elsewhere (Neural Rendering methods, MFG Ada Unlock) aren't in this list.

### Per-game addons

Game Overrides → **Addons**: **Global**, **Select** (pick addons for this game only) or **Off**. Not available on the No Addons ReShade channel.

### Custom addons

Put `.addon64`/`.addon32` files in `%LOCALAPPDATA%\RHI\Custom\Addons\`. They appear under **Custom Addons** in both pickers.

### Where addons go

Addons are copied to the game folder, or to the folder set as `AddonPath` in the game's `reshade.ini`. RHI only removes files it installed. Updated addons are refreshed in your games at startup.

---

## ReShade Presets

Put preset `.ini` files in `%LOCALAPPDATA%\RHI\inis\reshade-presets\`.

- **Per game**: Game Overrides → **Select ReShade Preset**. Tick presets and click **Deploy**. RHI then offers to install the shaders and textures the preset needs.
- **Drag and drop**: drop a preset `.ini` onto the window. RHI checks it's a ReShade preset, saves it, deploys it to the game you pick and offers to install its shaders.
- **Many games at once**: Settings → Update & Deployment → **Mass Preset Install**.

---

## DLSS / Streamline

The **DLSS / Streamline** section manages a game's DLSS and Streamline files and its DLSS driver settings. It shows content once RHI has found DLSS or Streamline files in the game.

### Detection

RHI scans the whole game folder in the background and caches the results. A game checked three times without finding DLSS is skipped after that. If you've added DLSS to a game or the section is missing, use **Settings → System & Maintenance → Full Refresh**.

### Columns

| Column | File | Controls |
|---|---|---|
| **DLSS Super Resolution** | `nvngx_dlss.dll` | Version, Preset, Render Scale |
| **Ray Reconstruction** | `nvngx_dlssd.dll` | Version, Preset, Render Scale |
| **Frame Generation** | `nvngx_dlssg.dll` | Version, Preset, **Multi Frame Gen** |
| **Neural Rendering** | `nvngx_dlssnr.dll` | Version, Preset, **Deploy DLL**, **✕** |
| **Streamline** | `sl.*.dll` | Version, **Quick Apply**, **Restore DLSS/SL** |

### Changing versions

Each version dropdown contains:

- **Default (x.x.x)**: the version the game shipped with. Choosing it restores the original file.
- Every version RHI knows about, newest first. Versions download when you pick them.
- **Custom**: your own DLL from `%LOCALAPPDATA%\RHI\Custom\DLSS\` (or `Custom\Streamline\`).
- **NVIDIA Override** (Super Resolution, Ray Reconstruction, Frame Generation): sets the driver's "use latest DLL" flag, so the driver loads its own newest DLL. This is the same as the DLSS override in NVIDIA App or Profile Inspector. Choosing any other version turns it off.

Streamline is swapped as a set: only the `sl.*.dll` files the game already has are replaced.

A component the game doesn't have shows **None**. DLSS 1.x Super Resolution, Ray Reconstruction and Streamline can't be swapped (they're incompatible with newer versions); DLSS 1.x Frame Generation can.

The dropdown always shows the version that's actually in the game folder. If RHI can't write there (the game is running, access is denied, or a Game Pass folder changed after an update), it shows **Couldn't change DLSS files** and puts things back as they were.

### Backups

The first time RHI replaces a DLSS file, it keeps the original. Streamline originals are also zipped to `%LOCALAPPDATA%\RHI\StreamlineBackups\`. If the game didn't have a file before, RHI removes it on restore.

### Presets

Written to the game's NVIDIA driver profile. They work without other tools and don't need admin.

| Component | Options |
|---|---|
| Super Resolution | Default, E/F (CNN), J/K (first-gen transformer, DLSS 4), L/M (second-gen transformer, DLSS 4.5), NVIDIA Recommended |
| Ray Reconstruction | Default, D/E (first-gen transformer), F (second-gen transformer), NVIDIA Recommended |
| Frame Generation | Default, A, B, C, NVIDIA Recommended |
| Neural Rendering | Default, NVIDIA Recommended |

**Default** removes the override, so the game uses the driver's default. New presets can be added through the remote manifest without an app update.

### Render Scale

Super Resolution and Ray Reconstruction: **Off** (the game decides), 100% DLAA, 99% DLAA Alt, 88% DLAA Lite, 77% Ultra Quality, 75% Quality+, 67% Quality, 58% Balanced, 50% Performance, 45% Performance−, 33% Ultra Perf, or **Custom** (type 33–100 and press Enter).

### Neural Rendering column

- **Deploy DLL** copies the selected `nvngx_dlssnr.dll` version into the game. With Custom selected it copies from `Custom\DLSS\`.
- **✕** removes it.
- This column and the Neural Rendering section share the same NR version.

### Quick Apply and Restore

- **Quick Apply** applies your [DLSS defaults](#dlss--streamline-settings) to this game in one click. It skips components the game doesn't have, and it's greyed out until you set defaults.
- **Restore DLSS/SL** puts back every original DLL, resets presets and render scales to Default, clears Multi Frame Generation settings and turns off NVIDIA Override.

### NVIDIA profiles

RHI finds the game's NVIDIA driver profile by name, then by its executables. The remote manifest can point a game at a specific profile. If there's no profile, RHI creates one the first time you change a setting.

---

## Neural Rendering (DLSS 5)

The **Neural Rendering** section installs DLSS 5 neural rendering tools. Installing any method also installs ReShade if it isn't already installed.

### Methods

Pick a method from **Method**. Methods that don't suit the game are greyed out, and RHI pre-selects the best one.

| Method | For | What it installs |
|---|---|---|
| **ShortFuse DLSS Tool** | Most games with native DLSS (DX9, DX11, DX12, Vulkan; 64-bit) | ShortFuse's DLSS addon plus the full DLSS and Streamline set |
| **DLSS5 Tool** | DX12 games with native DLSS (64-bit) | The DLSS5 Tool addon, upgrades DLSS to the newest version and adds the NR DLL |
| **DLSS5 Tool + DX11 Bridge** | DX11 and Vulkan games with native DLSS (64-bit) | As DLSS5 Tool, plus the bridge addon |
| **DLSS5 Feeder** | Games without DLSS: DX11, DX12, Vulkan, OpenGL, DX9 and 32-bit | The Feeder addon, DLSS5 Tool, DLSS and NR DLLs, the required shaders and a ready-made ReShade preset. DX9 games also get dgVoodoo2; 32-bit games get the 64-bit host set up |

RHI picks **DLSS5 Feeder** for 32-bit, OpenGL and no-DLSS games, **DLSS5 Tool + DX11 Bridge** for DX11/Vulkan games with DLSS, and **ShortFuse DLSS Tool** otherwise.

Click **Install Neural Rendering** (or **Install Feeder Addon**). **✕** removes the method. Switching methods removes the old one first.

### Versions

- **SF Version**, **DLSS5 Tool Version**, **Feeder Version** and **Bridge Version** (as relevant): **Latest** (auto-updates) or a pinned release.
- **NR DLL Version**: Latest or a specific `nvngx_dlssnr.dll` build.

Changing a version while installed swaps just that file.

### Options

- **NR Cost Scaler** (Off by default): also installs a proxy that runs the neural model at reduced cost (75% by default). Set it before installing; it's locked while a method is installed. ShortFuse DLSS Tool has this built in.
- **ZZZ Load Order** (ShortFuse only): renames the addon so it loads last.
- **ShortFuse ⚙ → Auto-configure ReShade for FrameGen** (Off by default): sets ReShade up to load through ASI Loader with Streamline hooks. Not needed on ShortFuse v0.54 or later.

RHI tracks shared files such as `nvngx_dlssnr.dll`, so removing one tool doesn't delete a file another tool (OptiScaler, RTX Encore) still uses.

---

## Driver Settings and Multi Frame Generation

### Driver Settings

The **Driver Settings** section (its heading shows your driver version) changes this game's NVIDIA profile. Changes apply immediately. **It needs admin**: without [Admin Mode](#system--maintenance), the section is dimmed.

| Group | Settings |
|---|---|
| **VSync** | **Mode** (App Controlled, Force Off, Force On, Fast Sync, or the global value), **Tear Control** (Standard/Adaptive), **Low Latency** (Off/On/Ultra) |
| **Smooth Motion** (RTX 40+) | **Enable**, **Allowed APIs**, **Flip Pacing**. Turning it on sets Allowed APIs to All and locks Low Latency to Ultra; turning it off restores your previous Low Latency |
| **Other** | **Power Mode** (Optimal Performance, Adaptive, Maximum Performance), **G-Sync** (Enabled/Disabled), **Restore Defaults** |
| **ReBAR** | **Enable** (Auto/Off/On), **Mode** (Standard/Optimized), **Size Limit** (512MB–4GB). Mode and size need Enable = On |

**Restore Defaults** resets this game's whole profile (presets, render scale, MFG, overrides and driver settings) and restores its original DLSS/Streamline files. It can't be undone.

### Multi Frame Generation (RTX 50 Series)

Click **Multi Frame Gen** in the Frame Generation column (needs a DLSS Frame Generation DLL). A one-time notice explains that MFG needs an RTX 50 Series GPU, with driver 572.16+ for Fixed and 595.97+ for Dynamic.

- **FG Mode**: Default, **Fixed** (2x–6x) or **Dynamic** (up to 2x–6x).
- **Target Frame Rate** (Dynamic): Off, Max Refresh Rate, VRR caps from 59 FPS (60 Hz) to 431 FPS (500 Hz), or Custom (20–1000).

Changes apply immediately. Defaults for Dynamic MFG are in Settings → DLSS / Streamline Settings → **DMFG Defaults**.

For RTX 20, 30 and 40 Series cards, see the frame generation unlocks in [Extras](#extras).

---

## Extras

The **Extras** section holds optional tools:

- **ASI Loader**.
- **OptiScaler** (see [OptiScaler](#optiscaler)).
- **MFG Unlocks**: **RTX Encore (Beta)**, **DLSS Enabler**, **20/30 FG Unlock**, **RTX 40 MFG**, **MFG Ada Unlock**.
- **API Upgrades**: **DXVK** and **dgVoodoo2** (see [DXVK and dgVoodoo2](#dxvk-and-dgvoodoo2)).

Several of these are "proxy" DLLs that load by taking a system DLL's name. When you install one, RHI asks which name to use. Names RHI's other components already use are disabled. If the game already has a file with that name, RHI backs it up and restores it on uninstall.

| Tool | What it does |
|---|---|
| **ASI Loader** ([Ultimate ASI Loader](https://github.com/ThirteenAG/Ultimate-ASI-Loader)) | Loads `.asi` plugins. Recommended names are marked. If the chosen name already exists, the existing file is renamed to `<name>Hooked.dll` and still loaded. |
| **RTX Encore (Beta)** | Frame generation (X2–X6) and Smooth Motion on RTX 30, Neural Rendering on RTX 20/30/40. Its ⚙ can deploy the NR DLL. |
| **DLSS Enabler** | DLSS Frame Generation support through a proxy DLL (`version.dll` works for most games). Not available while OptiScaler is installed, as OptiScaler includes it. |
| **20/30 FG Unlock** | DLSS Frame Generation (up to 6X) on RTX 20 and 30 Series, DX12 only, NVIDIA driver R580 or newer. Installs a proxy DLL (start with `version.dll`; if FG doesn't appear in the game, try the next name), `dlssg_sm86.ini` and the `dlssg_sm86` folder; uninstall removes all of it. Its ⚙ sets Frame Generation on/off, Engine (DLSS or XeSS), Max Frames (2X–6X), Preset (A, B, or C for FG Lite), Speed Tier and Super Resolution (preset M/L, with faster INT8 kernels), and opens the ini and log. Settings are kept when the mod updates. |
| **RTX 40 MFG** | Multi frame generation beyond 2x (up to 6x) on RTX 40 Series. Press Backspace in-game for its menu. |
| **MFG Ada Unlock** | In-memory 3x/4x+ MFG unlock for RTX 40 Series, loaded as a ReShade addon (needs ReShade). |

RTX 40 MFG and MFG Ada Unlock can't be installed together, and 20/30 FG Unlock is blocked while MFG Ada Unlock is installed.

---

## OptiScaler

[OptiScaler](https://github.com/optiscaler/OptiScaler) lets a game's DLSS, FSR or XeSS inputs drive a different upscaler (FSR 3/4, XeSS, DLSS), and can add or replace frame generation. It's 64-bit only; the row is disabled on 32-bit games.

### Before your first install

A one-time reminder points you to **Settings → Component Settings → OptiScaler**:

- **GPU Type**: NVIDIA, AMD or Intel.
- **DLSS Inputs** (AMD/Intel): whether the game's DLSS inputs should be used.
- **Hotkey** for the OptiScaler menu (Insert by default), with **Apply to All Games**.

GPU Type and DLSS Inputs choose which ini template new installs get.

### Variants

Choose in **⚙ → OptiScaler Version** before installing:

| Variant | Source |
|---|---|
| **Stable** | Official release. |
| **Nightly** | Daily build. **Nightly Build** picks "Latest (auto-update)" or pins one of the last 30 builds. Pinned games aren't updated. |
| **DLSS NR** | Community fork with DLSS Neural Rendering. Also installs `nvngx_dlssnr.dll`. |
| **F5 DLSSNR** | janblade's multi-pass Neural Rendering fork. Also installs `nvngx_dlssnr.dll` and its forwarder. |

Changing variant uninstalls the current one; click Install to install the new one. Each variant updates on its own track.

### What Install does

1. Copies OptiScaler into the game under its proxy name (`dxgi.dll` by default, `winmm.dll` on Vulkan games), backing up any file it replaces.
2. Writes `OptiScaler.ini` from your template, unless the game already has one. `LoadReshade` and `LoadAsiPlugins` are always on, and your hotkey is applied.
3. Copies RHI's newest DLSS, Ray Reconstruction and Frame Generation DLLs into the game (originals backed up).
4. Installs **OptiPatcher**, which updates automatically.
5. For Nightly and DLSS NR with **Streamline/DLSS Enabler** set to Yes, installs Streamline and DLSS Enabler into the game's `OptiScaler` folder.

Change the proxy name under Game Overrides → **DLL naming overrides → OptiScaler** (dxgi, winmm, d3d11, d3d12, dbghelp, version, wininet or winhttp).

### Working with ReShade

If ReShade uses the same file name, RHI renames ReShade to `ReShade64.dll` and OptiScaler loads it. Uninstalling OptiScaler gives ReShade its normal name back. If a game crashes with both, try giving ReShade a different name (for example `d3d12.dll`) in DLL naming overrides.

### ⚙ OptiScaler Settings

Settings are written straight into the game's `OptiScaler.ini`. Until OptiScaler is installed, only the version can be changed.

- **All variants**: **Framerate Limit** (Off or VRR caps), **Upscaler API** and **Upscaler** (choices depend on DX11, DX12 or Vulkan).
- **Frame Generation Settings** (Nightly, DLSS NR, F5): FG Enabled, Streamline/DLSS Enabler and its version, FG Input, FG Output, FG Nvngx Override, HUD Fix, Force Reflex, Use Games Reflex Markers.
- **Additional Settings**: DLSS SR Preset, DLSS RR Preset, Render Scale, Disable Flip Metering.
- **Engine.ini Settings** (Unreal games): Dilated Motion Vectors, FSR Crash Fix, FSR-FG Swapchain, Upscaler Plugin.
- **Neural Rendering Settings** (DLSS NR, F5): NR Runtime version, NR Enabled, Run Before SR, Passes, Working Scale, Apply to Finished Picture.
- **Presets** (non-Stable variants): four global slots. **Save** stores the current settings; click a slot to apply it to any game.
- **Deploy OptiScaler.ini** replaces the game's ini with a fresh copy of your template. It doesn't keep the settings above.

### Info and templates

On Stable, **Info** shows RHI's notes for the game, or its entry from the OptiScaler compatibility lists. On the other variants it opens the variant's GitHub page.

RHI ships 12 ini templates (NVIDIA, AMD/Intel with DLSS inputs, and AMD/Intel without, for each variant) in `%LOCALAPPDATA%\RHI\inis\`. Edit them freely; RHI never overwrites them.

### Uninstalling

Removes everything OptiScaler installed and restores the originals, including DLSS files. Other mods' files in the `plugins` folder are left alone.

---

## DXVK and dgVoodoo2

### DXVK

[DXVK](https://github.com/doitsujin/dxvk) runs DX8–DX11 games through Vulkan. Combined with Lilium's HDR fork, it gives older games scRGB HDR output.

- Found under **Extras → API Upgrades** on DX8, DX9, DX10 and DX11 games that don't also use DX12 or Vulkan.
- Not offered for games with anti-cheat (blocked by the manifest).

**⚙ DXVK Settings**:

- **Variant**: **Lilium HDR** (default), **Development** (nightly) or **Stable** (release).
- **Lilium Preset**: from **Safest** (swapchain only, very compatible) to **Experimental** (upgrades more render targets: better HDR but more likely to cause issues).
- **Deploy dxvk.conf**.
- **Prefer DXGI Swapchain** and **DXVK as Native**: NVIDIA present-method settings.

When DXVK is installed, ReShade moves to the Vulkan layer and the game's API badge changes (e.g. `DX9 / VLK`). On DX9 only some Lilium shaders work; PumboAutoHDR is a good choice there. **✕** restores ReShade and the original API.

### dgVoodoo2

[dgVoodoo2](https://github.com/dege-diosg/dgVoodoo2) runs DX9 games through DX11. It's offered on DX9 games and installed automatically by Luma and DLSS5 Feeder where needed. Its ⚙ has a **Version** picker. If ReShade was installed as `d3d9.dll`, RHI moves it to `dxgi.dll`.

---

## Game Overrides

Per-game settings. Each one saves as soon as you change it.

| Setting | What it does |
|---|---|
| **Game name (editable)** | Renames the game (press Enter). Your settings move with it. **Reset** restores the detected name. |
| **Wiki mod name** | The exact mod name to match, when RHI matched the wrong RenoDX mod or none. |
| **Wiki lookup** | **Included** / **Excluded**. Excluded stops RenoDX matching for this game. |
| **DLL naming overrides** | Dropdowns for **ReShade**, **Display Commander** and **OptiScaler** file names. Changing one renames the installed file straight away; `--------` goes back to the default. **Reset DLL Names** resets all three. |
| **ReShade Channel** | Stable, Nightly, Custom, No Addons or Legacy (see [ReShade Channel](#reshade-channel)). |
| **Bitness** | Auto, 32-bit or 64-bit. Changing it uninstalls the game's components first, so they can be reinstalled with the right bitness. |
| **Graphics API** | Auto, DirectX8, DirectX9, DirectX10, DirectX11, DirectX12, Vulkan or OpenGL. Switching to or from Vulkan changes how ReShade is installed. Use this when a game supports more than one API and you want a specific one. |
| **Update Inclusion** | Choose which of this game's components Update All updates: ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, plus DXVK and RE Framework where relevant. |
| **Shaders** / **Addons** | Per-game shader and addon modes (see [Shader Packs](#per-game-shaders) and [ReShade Addons](#per-game-addons)). |
| **Select ReShade Preset** | Deploy ReShade presets (see [ReShade Presets](#reshade-presets)). |
| **Launch executable** | The exe to launch instead of the auto-detected one. **Browse** to pick it, **Reset** to clear it. |
| **Launch arguments** | Command-line arguments used when launching from RHI. |

The HDR and RES buttons, DXVK, and the NVIDIA settings are also per game, but they live in their own sections.

---

## Management

- **Change install folder**: point RHI at the correct game folder.
- **Reset / Remove game**: resets the folder to the auto-detected one, or removes a manually added game.
- **Reset Overrides**: resets this game's overrides: name and wiki match, wiki lookup, shader and addon modes, DLL names, update inclusion, bitness, API, ReShade channel (reinstalling Stable if needed), DXVK, launch exe and arguments, and DLSS presets. The HDR and RES buttons aren't affected.
- **Copy Report**: builds a report of this game's detection and install state for bug reports. RHI first asks you to correct any wrong values with overrides, then lets you add a description and copies the report to the clipboard.

---

## Launching Games

### Ways to launch

**▶ Launch**, double-clicking a game in the sidebar, the tray menu, the taskbar jump list, or a desktop shortcut made with **Create Desktop Shortcut** (right-click a game). Shortcuts launch through RHI, so HDR and resolution switching still happen.

### How RHI launches a game

RHI uses the first of these that applies:

1. Your **Launch executable** override.
2. An exe set by the remote manifest.
3. **Steam**: through Steam (`steam://rungameid`), or `steam.exe -applaunch` when you've set launch arguments. Steam's overlay and playtime tracking work.
4. **Epic**: through the Epic launcher. Not used when you've set launch arguments, since some Epic games fail to start with them.
5. **Xbox / Game Pass**: through the app's Windows entry.
6. Otherwise, the largest `.exe` in the game folder (ignoring uninstallers, crash reporters and launchers).

The game starts in its own folder. While it's running, its sidebar row is green.

### HDR auto-toggle

Turns Windows HDR on when a game starts and off when it closes.

- **Global**: Settings → ReShade & Display → **HDR Auto-Toggle**. The ⚙ chooses which monitors to switch; with none ticked, only the primary monitor switches.
- **Per game**: the **HDR** button next to Launch (purple = on). It overrides the global setting.

For games started through Steam, Epic or Xbox, RHI watches for a process running from the game folder for up to 60 seconds. If it can't find one, HDR is left on. If the game starts through a wrapper (such as SKSE or Mod Organizer 2), RHI follows the real game process.

### Resolution auto-toggle

Switches to a target resolution when a game starts and back when it closes.

- **Global**: Settings → Resolution & Colour Control → **Target Resolution** and **Resolution Auto-Toggle** (⚙ for monitors).
- **Per game**: the **RES** button next to HDR.

### System tray and jump list

Settings → System & Maintenance → **System Tray**:

- **Close to Tray**: closing the window hides RHI to the tray. Background update checks, HDR switching and addon detection keep running. Double-click the tray icon to reopen it.
- **Recent Games**: your last five launched games in the tray menu and the taskbar jump list.
- **Start with Windows**: starts RHI minimised to the tray when you sign in. Works with Admin Mode.

Starting RHI again while it's running brings the existing window to the front.

---

## Settings Page

### Component Updates

- **Check For Updates**: checks every component and the app straight away.
- **Background Checks**: **On** (default) checks at startup and every 4 hours. **Minimal** only refreshes the manifests, PCGamingWiki data and DLSS versions automatically; components are then checked on Refresh or Update All.
- **Automatic Updates**: **No** (default) or **Yes**. With Yes, component updates found by any check are installed in the background. Games that are running are updated after they close. App updates still ask first.

### ReShade & Display

Screenshots, hotkeys, effect list style, peak brightness and HDR Auto-Toggle. See [Global ReShade settings](#global-reshade-settings) and [HDR auto-toggle](#hdr-auto-toggle).

### Resolution & Colour Control

- **Target Resolution** and **Resolution Auto-Toggle** (see [Resolution auto-toggle](#resolution-auto-toggle)).
- **Output Colour Settings** (NVIDIA displays): pick a display, set **Colour Depth** (8/10/12 bpc) and **Dynamic Range** (Full/Limited), then click **Apply**.

### DLDSR Control

Switch NVIDIA DLDSR factors without opening NVIDIA Control Panel. Needs Admin Mode.

1. In NVIDIA Control Panel, set the DSR factors you want (Manage 3D Settings → DSR – Factors).
2. In RHI, click **Capture Current** and name the state (e.g. "2.25x only").
3. Repeat for each setup you want, including one with DLDSR off.

Then pick a state under **Saved States** and click **Apply**. **DSR Smoothness** has its own slider and **Set** button. Applying restarts the graphics driver, so the screen goes black for about 15 seconds. The **ⓘ** button has step-by-step help.

### Mod Data Source and GitHub API

- **Mod Data Source**: **RHI Database** (default) or **RenoDX Wiki**. Changing it refreshes immediately.
- **GitHub API**: your GitHub token (see [Add a GitHub token](#add-a-github-token-recommended)).

### DLSS / Streamline Settings

- **Configure Defaults**: your preferred version, preset and render scale for Super Resolution, Ray Reconstruction, Frame Generation and Neural Rendering, plus the Streamline version. Leave a column on **Default** to leave it unchanged. Used by Quick Apply and Batch Deploy.
- **DMFG Defaults**: default **Frame Count** and **Target FPS** for Dynamic Multi Frame Generation.
- **Batch Deploy**: tick games (with **Select All** / **Deselect All**), choose versions and presets, then click **Deploy**. **Restore** puts back the originals for the ticked games. **Auto-create NVIDIA profiles** creates missing profiles as needed. Games already on the chosen version are skipped.
- **DLSS On-Screen Indicator**: shows DLSS information in games. Needs admin.
- **G-Sync On-Screen Indicator**.
- **Auto-Update DLSS** and **Auto-Update Streamline** (both Off by default): when a new version is released, games on the previous newest version are moved to it. Games where you chose an older version are left alone.

### Global NVIDIA Driver Settings

Apply to every game that doesn't have its own setting:

- **Shader Cache Size** (512MB up to Unlimited; default 16GB) and **Shader Pre-Compile**.
- **G-Sync** and **G-Sync Mode** (Off, Fullscreen only, Fullscreen/Windowed).
- **FPS Limit**: Off, VRR caps or Custom. Games with ReLimiter or Display Commander are set to Off so the limiters don't fight.
- **Preferred Refresh Rate**, **VSync**, **Power Mode**.
- **ReBAR** and **ReBAR Size Limit** (need admin).
- **Digital Vibrance**: per-monitor saturation from 0 to 100 (50 = neutral). Reapplied every time RHI starts.
- **Create Missing Profiles**: creates NVIDIA profiles for library games that don't have one, so global settings apply to them.
- **Backup Profiles** / **Restore Profiles**: saves your library games' NVIDIA settings (driver settings, DLSS presets, render scales, MFG, RTX HDR) to `%LOCALAPPDATA%\RHI\nvidia_profiles_backup.json`, and restores them, for example after a driver reinstall. NVIDIA Override flags and per-game FPS limits aren't included.
- **Reset Nvidia Profiles**: removes every per-game override and resets the global settings. Can't be undone.
- **Clear Shader Cache**: deletes NVIDIA's DirectX and OpenGL shader caches.

### Component Settings

- **ReLimiter**: see [ReLimiter](#relimiter).
- **OptiScaler**: GPU Type, DLSS Inputs and Hotkey. See [OptiScaler](#before-your-first-install).

### Shaders & Addons

**Shader Management**, **Cache All Shaders** and **Addon Watch Folder** (see [Drag-and-Drop and Addon Detection](#addon-watch-folder)).

### Update & Deployment

- **Update Inclusion**: choose which components Update All covers for every game (ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, RE Framework).
- **Mass INI Deployment**: deploy `reshade.ini`, `relimiter.ini`, `DisplayCommander.ini` or `OptiScaler.ini` to many games at once, or **Mass Preset Install**.

### System & Maintenance

- **Full Refresh**: clears RHI's caches and rescans everything from scratch. Use it when something looks out of date or a game's DLSS section is missing.
- **Purge Staging Files**: deletes cached downloads (DLSS, Streamline and component files) to free space. They download again when needed.
- **Admin Mode**: RHI starts elevated without a UAC prompt each time (through a scheduled task called "RHI Admin Mode"). Needed for per-game driver settings, ReBAR, Low Latency Ultra, Smooth Motion, DLDSR Control and the DLSS indicator. Turning it on or off asks for UAC once.
- **Drop Helper** (on by default): lets you drag files from Discord and browsers onto RHI while it's running as admin. Needs a restart to take effect.
- **System Tray**: see [System tray and jump list](#system-tray-and-jump-list).

### Data & Custom Files

- **AppData Folder**, **Downloads Folder**, **Custom Folder**, **Logs Folder**.
- **Copy Logs**: zips your logs and copies them to the clipboard to paste into Discord. Logs hide your Windows username and account IDs.
- **Export Game Data**: zips your library's detection data (API, exe paths, engine, store IDs) and copies it to the clipboard for sharing.

---

## Updates

### Update All

**Update All** in the toolbar updates every installed component that has an update, in this order: ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, RE Framework, DXVK, Luma and DOF Fix.

- Components excluded in Settings → Update & Deployment → **Update Inclusion** (all games) or Game Overrides → **Update Inclusion** (one game) are skipped. So are Legacy ReShade and pinned OptiScaler Nightly builds.
- Only mods with an update are reinstalled.
- Files that belong to another tool (foreign DLLs) are never overwritten.

### Automatic and background checks

With **Background Checks** on, RHI checks at startup and every 4 hours, even from the tray. Shader packs, addons and Extras tools also update at startup. With **Automatic Updates** on, updates are installed without asking.

Every component update is logged in the **Updates** window (status bar).

### App updates

RHI checks for a new version at startup, on Check For Updates, and when you click the version number. **Update Now** downloads the installer, closes RHI and runs it.

### GitHub rate limit

Without a token, GitHub allows 60 requests an hour. When that runs out, downloads and checks fail for the rest of the hour. RHI saves requests by remembering what it has already checked, but adding a [GitHub token](#add-a-github-token-recommended) is the fix.

---

## Drag-and-Drop and Addon Detection

### Drag-and-drop

Drop onto the RHI window:

| You drop | RHI does |
|---|---|
| A game `.exe` | Adds the game (engine and folder detected automatically) |
| `.addon64` / `.addon32` / `.addon` | Asks which game, then installs it. Luma addons go through the Luma install |
| A Luma `.zip` / `.7z` | Installs the Luma mod |
| Another archive (`.zip`, `.7z`, `.rar`, `.tar`, `.gz` and more) | Extracts it and installs the addons inside |
| A ReShade preset `.ini` | Saves it, deploys it to a game and offers to install its shaders |
| A link (`.url` file, or a link dragged from a browser or Discord) | Downloads it and handles the file |

When RHI runs as admin, Windows blocks drops from non-admin apps such as Discord; the **Drop Helper** setting works around this.

### Addon Watch Folder

RHI watches a folder (your **Downloads** folder by default; change it in Settings → Shaders & Addons) for new RenoDX addons (`renodx-*.addon64`/`.addon32`) and archives with "renodx" or "luma" in the name, and offers to install them. Addon files are removed after a successful install; archives are left where they are.

### Opening addons from Explorer

Double-clicking an `.addon64` or `.addon32` file opens it in RHI (or passes it to RHI if it's already running) and starts the install.

---

## Vulkan Games and Ryubing

### Vulkan games

Vulkan games use ReShade's Vulkan layer, installed once in `C:\ProgramData\ReShade\` (this needs admin; RHI asks for permission). After that, each Vulkan game gets its own `reshade.ini` and shaders, and its status shows **(Vulkan)**.

All Vulkan games share the one layer, so a ReShade channel change on one Vulkan game applies to all of them.

For games that support both DirectX and Vulkan, set Game Overrides → **Graphics API** to the one you play with.

### Ryubing (Nintendo Switch emulator)

Drag `Ryujinx.exe` onto the window to add Ryubing. It's treated as a Vulkan game. Installing RenoDX on it installs the RenoDX addons for every supported Switch game at once (shown as "Ryubing (9 games)").

---

## Links: Nexus, PCGamingWiki, UW Fix, Ultra+

Buttons in the game header, shown when a page exists:

- **Nexus**: the game's Nexus Mods page.
- **PCGW**: the game's PCGamingWiki page. RHI's PCGamingWiki data also fills in engine, API and config folder information when it can't detect them.
- **UW Fix**: an ultrawide fix from Lyall, RoseTheFlower or p1xel8ted.
- **Ultra+**: the game's page on The Ultra Place.

Search `UW Fix` or `Ultra+` in the sidebar to list games that have one.

---

## Remote Data

RHI downloads a few files from its GitHub repository so fixes reach everyone without an app update:

- **Manifest**: game name fixes, install path and engine/API overrides, notes and install warnings, launch fixes, Engine.ini settings, link overrides, extra shader and addon packs, NVIDIA profile fixes, mod author details and feature switches. If it can't be downloaded, RHI uses the last copy.
- **DLSS manifest**: the DLSS and Streamline versions offered in the dropdowns.
- **PCGamingWiki data**: engine, API and config information for more than 55,000 games.
- **Message of the day**: shown once per new message, and again from the **MOTD** button.

---

## Data Storage and Logs

Everything RHI stores is in `%LOCALAPPDATA%\RHI\`:

| Path | Contents |
|---|---|
| `settings.json` | All settings and per-game overrides |
| `game_library.json` | Detected, manual, hidden and favourite games |
| `installed.json`, `aux_installed.json`, `luma_installed.json`, `reframework_installed.json`, `dxvk_installed.json` | Install records |
| `update_log.json` | The Updates history |
| `shader_profiles.json` | Shader profiles |
| `nvidia_profiles_backup.json` | NVIDIA profile backup |
| `github_api.txt` | Your GitHub token |
| `inis\` | ini templates (reshade.ini, relimiter.ini, OptiScaler templates…) and `reshade-presets\` |
| `Custom\` | Your own ReShade DLLs, DLSS/Streamline DLLs and addons |
| `reshade\Custom\` | Your own shaders and textures |
| `logs\` | Session logs and crash reports (last 10 of each) |
| `downloads\` and staging folders | Cached downloads (safe to delete with Purge Staging Files) |

Settings and install records keep a `.bak` copy and are recovered automatically if a file is damaged.

If RHI's window ever stops responding, RHI restarts itself after a few seconds and reopens the game you had selected.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Downloads or update checks failing, components "unavailable" | Add a [GitHub token](#add-a-github-token-recommended). |
| Game not detected | Click **+** next to the search box, or drag the game's exe onto the window. |
| Wrong game folder | Management → **Change install folder**. |
| Wrong RenoDX mod matched | Game Overrides → **Wiki mod name**. |
| ReShade doesn't load | Check the install folder points at the folder with the game's exe. Try a different name under DLL naming overrides. |
| Game crashes with OptiScaler and ReShade | Give ReShade a different name (e.g. `d3d12.dll`) under DLL naming overrides. |
| UE-Extended shows no HDR | Turn HDR on in the game's own settings, or turn on **Engine.ini HDR** in the RenoDX ⚙ for games without an HDR option. |
| Driver Settings or DLDSR greyed out | Turn on **Admin Mode** in Settings → System & Maintenance. |
| DLSS section missing or out of date | Settings → System & Maintenance → **Full Refresh**. |
| "Couldn't change DLSS files" | Close the game and try again. Game Pass folders may be protected. |
| Game Pass ReShade gone after a game update | RHI reinstalls it on the next launch. |
| Need disk space | Settings → **Purge Staging Files**. |
| Reporting a problem | **Copy Logs** (Settings) and/or **Copy Report** (Management), then paste into [Discord](https://discord.gg/ultraplace). |

---

## Third-Party Components

RHI downloads these from their official sources. All credit goes to their authors.

| Component | Author | Licence |
|---|---|---|
| [ReShade](https://reshade.me) | crosire | [BSD 3-Clause](https://github.com/crosire/reshade/blob/main/LICENSE.md) |
| [RenoDX](https://github.com/clshortfuse/renodx) | clshortfuse & contributors | [MIT](https://github.com/clshortfuse/renodx/blob/main/LICENSE) |
| [RenoDX UE-Extended](https://github.com/marat569/renodx) | Marat | See repository |
| [RenoDX Unity](https://github.com/NotVoosh/renodx-unity) | NotVoosh | See repository |
| [Luma Framework](https://github.com/Filoppi/Luma-Framework) | Pumbo (Filoppi) & contributors | Source-available |
| [ReLimiter](https://github.com/RankFTW/ReLimiter) | RankFTW | Source-available |
| [Display Commander](https://github.com/pmnoxx/display-commander) | pmnoxx | [GPL-3](https://github.com/pmnoxx/display-commander/blob/main/LICENSE) |
| [RE Framework](https://github.com/praydog/REFramework-nightly) | praydog | [MIT](https://github.com/praydog/REFramework/blob/master/LICENSE) |
| [OptiScaler](https://github.com/optiscaler/OptiScaler) | OptiScaler contributors | See repository |
| [OptiScaler DLSS NR fork](https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass) | wilsjo2 | See repository |
| [OptiScaler F5 DLSSNR fork](https://github.com/janblade/OptiScaler-F5-DLSSNR-Multipass) | janblade | See repository |
| [DXVK](https://github.com/doitsujin/dxvk) | doitsujin & contributors | [Zlib](https://github.com/doitsujin/dxvk/blob/master/LICENSE) |
| [DXVK HDR-mod](https://github.com/EndlesslyFlowering/dxvk) | EndlesslyFlowering (Lilium) | [Zlib](https://github.com/EndlesslyFlowering/dxvk/blob/HDR-mod/LICENSE) |
| [dgVoodoo2](https://github.com/dege-diosg/dgVoodoo2) | Dege | Freeware |
| [Ultimate ASI Loader](https://github.com/ThirteenAG/Ultimate-ASI-Loader) | ThirteenAG | See repository |
| [DLSS5 Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder) | jlrouzies-fr | See repository |
| [DLSS5 DX11 Bridge](https://github.com/NIGos/dlss5-bridge) | NIGos | See repository |
| [RTX 40 MFG Unlock](https://github.com/dashdogy/RTX40MFG-Unlock) | dashdogy | See repository |
| [MFG Ada Unlock](https://github.com/mavismmg/MFGAdaUnlock-RenoDx) | mavismmg | See repository |
| [20/30 FG Unlock](https://github.com/sdli1995/dlssg_for_sm86) | sdli1995 | See repository |
| [RTX Encore](https://github.com/SilyNoMeta/rtx-encore) | SilyNoMeta | See repository |
| [7-Zip](https://www.7-zip.org/) | Igor Pavlov | [LGPL-2.1 / BSD-3-Clause](https://www.7-zip.org/license.txt) |

NVIDIA DLSS and Streamline files are NVIDIA's. Shader packs are the work of their authors; each pack links to its source in the shader picker. Game data comes from [PCGamingWiki](https://www.pcgamingwiki.com) and the [HDR Gaming Database](https://github.com/KoKlusz/HDR-Gaming-Database).
