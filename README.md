# ReShade HDR Installer

A desktop manager for HDR game mods on Windows. Auto-detects your game libraries, installs ReShade, RenoDX, ReLimiter, and Luma Framework mods — all in a few clicks.

![ReShade HDR Installer Game View](screenshots/game_view.png)

> **⚠ Single-player only:** RHI installs ReShade with full addon support, which may be flagged by anti-cheat in online/multiplayer games. Uninstall ReShade before playing online.

---

## Acknowledgements

ReShade HDR Installer would not be possible without the hard work of the entire RenoDX team and [Crosire](https://reshade.me), the creator of ReShade. Their dedication to open-source HDR modding is what makes tools like this one viable. Thank you to every mod author, contributor, and tester who keeps pushing PC HDR forward.

---

## Download

Grab the latest installer from the [Releases page](https://github.com/RankFTW/RenoDXChecker/releases).

Requires Windows 10/11 (x64) and [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

---

## Quick Start

1. **Run ReShade HDR Installer** — games are auto-detected from Steam, GOG, Epic, EA App, Ubisoft, Xbox/Game Pass, Battle.net, and Rockstar on every launch.
2. **Pick a game** from the sidebar. Use search or filter chips to narrow the list.
3. **Install components** from the detail panel — ReShade, ReLimiter, and RenoDX each have a one-click install button.
4. **Launch the game**, press **Home** to open ReShade, go to **Add-ons**, and configure RenoDX.

---

## Features

### Game Detection
- **8-store auto-detection** — Steam, GOG, Epic, EA App, Ubisoft Connect, Xbox/Game Pass, Battle.net, Rockstar
- **Graphics API detection** — PE header analysis detects DirectX 8/9/10/11/12, Vulkan, and OpenGL with API badges on game cards
- **Engine detection** — Unreal, Unity, and custom engine names (via remote manifest) detected and displayed with icons
- **32-bit / 64-bit detection** — automatic PE header detection with manifest override support
- **Drag-and-drop** — drop a game `.exe` to add it, drop an `.addon64`/`.addon32` to install it, drop an archive to extract and install addons, or drop a Discord/browser URL link to download and install an addon
- **Addon auto-detection** — watches your Downloads folder for new `renodx-*.addon64` / `.addon32` files and prompts to install

### Component Management
- **One-click install/update/uninstall** for ReShade, ReLimiter, RenoDX, and Luma Framework
- **ReLimiter** — frame pacing addon with 32-bit and 64-bit support, downloaded from GitHub on demand with automatic update detection
- **Automatic ReShade DLL naming** — installs as `opengl32.dll` for OpenGL-only games and `d3d9.dll` for DX9 games without manual configuration
- **Vulkan ReShade** — global implicit layer support with per-game INI, shader deployment, and rendering path toggle for dual-API games
- **Luma Framework** — auto-detects compatible games, manages install/uninstall with Luma-specific notes and author badges
- **UE-Extended & Native HDR** — automatic detection and addon assignment for Unreal Engine games
- **Foreign DLL protection** — detects DXVK, Special K, ENB, etc. before overwriting

### Shader Packs
- **37+ shader packs** in three categories: Essential (Lilium HDR), Recommended, and Extra (community packs)
- **Per-game shader overrides** — different games can use different shader subsets
- **Startup deployment** — shaders auto-synced to all installed game folders on launch

### Customisation
- **Per-game overrides** — DLL naming, shader mode/selection, rendering path, wiki name mapping, wiki exclusion, per-component Update All inclusion
- **Auto-save** — all per-game settings save immediately when changed
- **Remote manifest** — game-specific overrides updated server-side without app releases (engine, DLL names, API, bitness, notes, and more)
- **INI presets** — bundled `reshade.ini`, `reshade.vulkan.ini`, `relimiter.ini`, and optional `ReShadePreset.ini` with merge-on-deploy
- **AddonPath support** — addon installs respect the `AddonPath` setting in `reshade.ini`

### UI & Updates
- **Detail View and Grid View** — switch between a sidebar+panel layout and a card grid with manage popouts
- **Search and filter chips** — All Games, Favourites, Installed, Unreal, Unity, Other, RenoDX, Luma, Hidden
- **Update All** — bulk update ReShade, RenoDX, and ReLimiter across all eligible games in one click
- **Auto-update** — checks for new RHI versions on launch with stable and beta channels
- **Mod author badges** — clickable Ko-fi donation links for mod authors
- **Version display** — installed ReShade and ReLimiter version numbers shown on component rows

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Game not detected | Click **Add Game** in Settings or drag the game's `.exe` onto the window |
| Xbox games missing | Click **Refresh** — RHI uses the PackageManager API |
| ReShade not loading | Check the install path via 📁 — the ReShade DLL (`dxgi.dll`, `opengl32.dll`, or `d3d9.dll`) must be next to the game exe |
| Black screen (Unreal) | ReShade → Add-ons → RenoDX → set `R10G10B10A2_UNORM` to `output size` |
| UE-Extended not working | Turn on in-game HDR — UE-Extended requires native HDR output |
| Downloads failing | Click **Refresh**, or clear cache from Settings → Open Downloads Cache |
| Foreign DLL blocking install | Choose **Overwrite** in the dialog, or cancel to keep the existing file |
| Games/mods out of sync | Settings → **Full Refresh** to clear all caches |

For detailed documentation, see the [Detailed Guide](docs/DETAILED_GUIDE.md).

---

## Third-Party Components

| Component | Author | Licence |
|-----------|--------|---------|
| [ReShade](https://reshade.me) | Crosire | [BSD 3-Clause](https://github.com/crosire/reshade/blob/main/LICENSE.md) |
| [RenoDX](https://github.com/clshortfuse/renodx) | clshortfuse & contributors | [MIT](https://github.com/clshortfuse/renodx/blob/main/LICENSE) |
| [Luma Framework](https://github.com/Filoppi/Luma-Framework) | Pumbo (Filoppi) | Source-available |
| [ReLimiter](https://github.com/RankFTW/Ultra-Limiter) | RankFTW | Source-available |
| [7-Zip](https://www.7-zip.org/) | Igor Pavlov | [LGPL-2.1 / BSD-3-Clause](https://www.7-zip.org/license.txt) |

> RHI is an unofficial third-party tool, not affiliated with or endorsed by the RenoDX project, Crosire, or the Luma Framework. All mod files are downloaded from their official sources at runtime and are not redistributed.

---

## Links

[RenoDX](https://github.com/clshortfuse/renodx) · [RenoDX Wiki](https://github.com/clshortfuse/renodx/wiki/Mods) · [ReShade](https://reshade.me) · [Luma Framework](https://github.com/Filoppi/Luma-Framework) · [Luma Mods List](https://github.com/Filoppi/Luma-Framework/wiki/Mods-List) · [ReLimiter](https://github.com/RankFTW/Ultra-Limiter) · [HDR Guides](https://www.hdrmods.com)

[RenoDX Discord](https://discord.gg/gF4GRJWZ2A) · [HDR Den Discord](https://discord.gg/k3cDruEQ) · [RHI Support](https://discordapp.com/channels/1296187754979528747/1475173660686815374) · [Ultra+ Discord](https://discord.gg/pQtPYcdE)

[Support RHI on Ko-Fi ☕](https://ko-fi.com/rankftw)
