# UPST

A desktop manager for HDR game mods on Windows. Auto-detects your game libraries, installs ReShade, RenoDX, and Luma Framework mods — all in a few clicks. Legacy Display Commander support available via opt-in toggle.

![UPST Game View](screenshots/game_view.png)

> **⚠ Single-player only:** UPST installs ReShade with full addon support, which may be flagged by anti-cheat in online/multiplayer games. Uninstall ReShade before playing online.

---

## Download

Grab the latest installer from the [Releases page](https://github.com/RankFTW/RenoDXChecker/releases/tag/RDXC).

Requires Windows 10/11 (x64) and [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

---

## Quick Start

1. **Run UPST** — games are auto-detected from Steam, GOG, Epic, EA App, Ubisoft, Xbox/Game Pass, Battle.net, and Rockstar on every launch.
2. **Pick a game** from the sidebar. Use search or filter chips to narrow the list.
3. **Install components** from the detail panel — ReShade, Display Commander, and RenoDX each have a one-click install button.
4. **Launch the game**, press **Home** to open ReShade, go to **Add-ons**, and configure RenoDX.

---

## Features

- **8-store game detection** — Steam, GOG, Epic, EA App, Ubisoft Connect, Xbox/Game Pass, Battle.net, Rockstar. New games appear automatically on every launch.
- **Graphics API detection** — scans game executables via PE header analysis to detect DirectX 11/12, Vulkan, and OpenGL. API badges shown on game cards.
- **Vulkan ReShade support** — installs ReShade as a global Vulkan implicit layer for Vulkan-rendered games, with per-game INI and shader deployment
- **One-click install/update/uninstall** for ReShade, RenoDX addons, and Luma Framework mods (Display Commander available via DC Legacy Mode)
- **Version display** — installed ReShade and Display Commander version numbers shown directly on the component row, including when DC Mode is active
- **DC Legacy Mode** — opt-in toggle in Settings that restores full Display Commander functionality for existing users. Off by default — all DC UI is hidden until enabled.
- **DC Mode** — On/Off toggle with a DLL filename picker controlling how Display Commander loads alongside ReShade (requires DC Legacy Mode)
- **Shader pack management** — 7 HDR shader packs with five deploy modes (Off, Minimum, All, User, Select) plus a per-game shader selection picker
- **Auto-save overrides** — all per-game settings save immediately when changed, no Save button needed
- **Per-game overrides** — DLL naming, shader mode/selection, DC mode, rendering path, wiki name mapping, wiki exclusion, per-component Update All inclusion, reset overrides
- **Seamless refresh** — after initial boot, refresh updates everything in the background without blanking the UI
- **DLL name conflict prevention** — ReShade and DC filename dropdowns cross-filter to prevent both using the same DLL name
- **Startup shader deployment** — on launch, shaders are automatically synced to all installed game folders for backwards compatibility
- **Drag-and-drop** — drop a game `.exe` to add it, drop an `.addon64`/`.addon32` to install it, or drop an archive to extract and install addons
- **Remote manifest** — game-specific overrides updated server-side without app releases, including engine overrides, DLL name overrides, and API overrides
- **UE-Extended & Native HDR** — automatic detection and addon assignment for Unreal Engine games
- **Engine detection** — Unreal, Unity, and custom engine names detected and displayed with icons
- **ReShadePreset.ini auto-deploy** — place a preset in the UPST inis folder to have it copied to every game install automatically
- **Foreign DLL protection** — detects DXVK, Special K, ENB, etc. before overwriting
- **Auto-update** — checks for new UPST versions on launch with stable and beta channels
- **Settings page** — DC Mode, shader mode, deploy actions, preferences, logs, about, and credits all in one place
- **Addon auto-detection** — watches your Downloads folder for new `renodx-*.addon64` / `.addon32` files and prompts you to install them. Configurable watch folder in Settings.
- **AddonPath support** — addon installs respect the `AddonPath` setting in `reshade.ini`, deploying addons to the configured folder instead of the game root
- **Luma author badges** — games in Luma mode show the Luma mod author (from the Luma wiki) in place of the RenoDX author badge
- **Filter mode persistence** — your selected filter tab is saved and automatically restored when you reopen the app
- **Toolbar redesign** — consistent teal accent styling with grouped sections separated by vertical dividers. Update button lights up purple when updates are available.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Game not detected | Click **Add Game** in the toolbar or drag the game's `.exe` onto the window |
| Xbox games missing | Click **Refresh** — UPST uses the PackageManager API |
| ReShade not loading | Check the install path via 📁 — `dxgi.dll` must be next to the game exe |
| Black screen (Unreal) | ReShade → Add-ons → RenoDX → set `R10G10B10A2_UNORM` to `output size` |
| UE-Extended not working | Turn on in-game HDR — UE-Extended requires native HDR output |
| Downloads failing | Click **Refresh**, or clear cache from Settings → Open Downloads Cache |
| Foreign DLL blocking install | Choose **Overwrite** in the dialog, or cancel to keep the existing file |
| Games/mods out of sync | Settings → **Full Refresh** to clear all caches |

For detailed documentation on DC Mode, shader packs, Luma Framework, per-game overrides, game detection methods, and more, see the [Detailed Guide](https://github.com/RankFTW/rdxc-manifest/tree/main?tab=readme-ov-file#renodx-commander--detailed-guide).

---

## Third-Party Components

| Component | Author | Licence |
|-----------|--------|---------|
| [ReShade](https://reshade.me) | Crosire | [BSD 3-Clause](https://github.com/crosire/reshade/blob/main/LICENSE.md) |
| [Display Commander](https://github.com/pmnoxx/display-commander) | pmnoxx | Source-available |
| [RenoDX](https://github.com/clshortfuse/renodx) | clshortfuse & contributors | [MIT](https://github.com/clshortfuse/renodx/blob/main/LICENSE) |
| [Luma Framework](https://github.com/Filoppi/Luma-Framework) | Pumbo (Filoppi) | Source-available |
| [7-Zip](https://www.7-zip.org/) | Igor Pavlov | [LGPL-2.1 / BSD-3-Clause](https://www.7-zip.org/license.txt) |

> UPST is an unofficial third-party tool, not affiliated with or endorsed by the RenoDX project, Crosire, pmnoxx, or the Luma Framework. All mod files are downloaded from their official sources at runtime and are not redistributed.

---

## Links

[RenoDX](https://github.com/clshortfuse/renodx) · [RenoDX Wiki](https://github.com/clshortfuse/renodx/wiki/Mods) · [ReShade](https://reshade.me) · [Display Commander](https://github.com/pmnoxx/display-commander) · [Luma Framework](https://github.com/Filoppi/Luma-Framework) · [Luma Mods List](https://github.com/Filoppi/Luma-Framework/wiki/Mods-List) · [HDR Guides](https://www.hdrmods.com)

[RenoDX Discord](https://discord.gg/gF4GRJWZ2A) · [HDR Den Discord](https://discord.gg/k3cDruEQ) · [UPST Support](https://discordapp.com/channels/1296187754979528747/1475173660686815374) · [Ultra+ Discord](https://discord.gg/pQtPYcdE)

[Support UPST on Ko-Fi ☕](https://ko-fi.com/rankftw)
