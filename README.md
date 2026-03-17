# RenoDX Commander (RDXC)

A desktop manager for HDR game mods on Windows. Auto-detects your game libraries, installs ReShade, Display Commander, RenoDX, and Luma Framework mods — all in a few clicks.

![RDXC Game View](screenshots/game_view.png)

> **⚠ Single-player only:** RDXC installs ReShade with full addon support, which may be flagged by anti-cheat in online/multiplayer games. Uninstall ReShade before playing online.

---

## Download

Grab the latest installer from the [Releases page](https://github.com/RankFTW/RenoDXChecker/releases/tag/RDXC).

Requires Windows 10/11 (x64) and [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

---

## Quick Start

1. **Run RDXC** — games are auto-detected from Steam, GOG, Epic, EA App, Ubisoft, Xbox/Game Pass, Battle.net, and Rockstar on every launch.
2. **Pick a game** from the sidebar. Use search or filter chips to narrow the list.
3. **Install components** from the detail panel — ReShade, Display Commander, and RenoDX each have a one-click install button.
4. **Launch the game**, press **Home** to open ReShade, go to **Add-ons**, and configure RenoDX.

---

## Features

- **8-store game detection** — Steam, GOG, Epic, EA App, Ubisoft Connect, Xbox/Game Pass, Battle.net, Rockstar. New games appear automatically on every launch.
- **One-click install/update/uninstall** for ReShade, Display Commander, RenoDX addons, and Luma Framework mods
- **Version display** — installed ReShade and Display Commander version numbers shown directly on the component row
- **DC Mode** (3 levels) — controls how Display Commander loads alongside ReShade for better compatibility
- **Shader pack management** — 7 HDR shader packs with four deploy modes (Off, Minimum, All, User)
- **Per-game overrides** — DLL naming, shader mode, DC mode, wiki name mapping, per-component Update All inclusion, reset overrides
- **Drag-and-drop** — drop a game `.exe` to add it, or drop an `.addon64`/`.addon32` to install it
- **Remote manifest** — game-specific overrides updated server-side without app releases, including engine overrides and DLL name overrides
- **UE-Extended & Native HDR** — automatic detection and addon assignment for Unreal Engine games
- **Engine detection** — Unreal, Unity, and custom engine names detected and displayed with icons
- **ReShadePreset.ini auto-deploy** — place a preset in the RDXC inis folder to have it copied to every game install automatically
- **Foreign DLL protection** — detects DXVK, Special K, ENB, etc. before overwriting
- **Auto-update** — checks for new RDXC versions on launch
- **Settings page** — DC Mode, shader mode, deploy actions, preferences, logs, about, and credits all in one place

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Game not detected | Click **Add Game** in the toolbar or drag the game's `.exe` onto the window |
| Xbox games missing | Click **Refresh** — RDXC uses the PackageManager API |
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

> RDXC is an unofficial third-party tool, not affiliated with or endorsed by the RenoDX project, Crosire, pmnoxx, or the Luma Framework. All mod files are downloaded from their official sources at runtime and are not redistributed.

---

## Links

[RenoDX](https://github.com/clshortfuse/renodx) · [RenoDX Wiki](https://github.com/clshortfuse/renodx/wiki/Mods) · [ReShade](https://reshade.me) · [Display Commander](https://github.com/pmnoxx/display-commander) · [Luma Framework](https://github.com/Filoppi/Luma-Framework) · [Luma Mods List](https://github.com/Filoppi/Luma-Framework/wiki/Mods-List) · [HDR Guides](https://www.hdrmods.com)

[RenoDX Discord](https://discord.gg/gF4GRJWZ2A) · [HDR Den Discord](https://discord.gg/k3cDruEQ) · [RDXC Support](https://discordapp.com/channels/1296187754979528747/1475173660686815374) · [Ultra+ Discord](https://discord.gg/pQtPYcdE)

[Support RDXC on Ko-Fi ☕](https://ko-fi.com/rankftw)


