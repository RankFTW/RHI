---
layout: default
title: RHI
permalink: /tools/rhi/
---

# RHI: ReShade HDR Installer

RHI sets up HDR mods, ReShade, DLSS and NVIDIA driver settings for your whole PC game library, from one window. It finds your games across eight storefronts, works out what each one needs, and installs, updates and removes it all for you. Built by RankFTW.

**[Download](https://github.com/RankFTW/RHI/releases/latest)** · **[NexusMods](https://www.nexusmods.com/site/mods/1710)** · **[Detailed Guide](https://github.com/RankFTW/RHI/blob/main/docs/DETAILED_GUIDE.md)** · **[Discord](https://discord.gg/ultraplace)**

> **Single-player only.** ReShade with addons, OptiScaler and DLSS/Streamline changes can trigger anti-cheat. Uninstall them before playing online.

## HDR for every game

- **RenoDX**: finds and installs the right HDR mod for each game and keeps it updated. Games without their own mod get a generic one: UE-Extended for Unreal Engine 4 and 5 (with `reshade.ini` and `Engine.ini` set up for you), and the generic Unity addon.
- **Luma Framework**: named Luma mods, plus generic Luma for DX11 Unreal Engine games, with the wiki's recommended settings applied on install.
- **RTX HDR and Windows Auto HDR**: turn either on per game, with RTX HDR brightness, contrast and saturation written straight to the NVIDIA profile.
- **DXVK + Lilium HDR**: HDR for older DX8 to DX11 games.
- **Available Mods**: one searchable list of every game with a RenoDX, Luma or Ultra+ mod.

## ReShade, fully managed

- Picks the right DLL name for each game automatically, or use your own.
- Stable, Nightly, Legacy or your own custom ReShade build, per game.
- 56 shader packs and the full addon list, chosen globally or per game.
- Keeps `reshade.ini` set up with your hotkeys, screenshot folder and peak brightness. Lock any game to stop RHI touching it.
- Warns before replacing another tool's DLL and backs up anything it overwrites.

## DLSS, frame generation and Neural Rendering

- Swap DLSS Super Resolution, Ray Reconstruction, Frame Generation and Streamline versions per game. Originals are backed up.
- Set DLSS presets and render scale in the driver profile, without NVIDIA Profile Inspector. Save your defaults once and apply them anywhere, or batch deploy to many games.
- **Neural Rendering (DLSS 5)**: installs the right tool for each game, including games without native DLSS.
- **OptiScaler**: Stable, Nightly and DLSS NR builds, configured from a menu instead of by hand.
- **Multi Frame Generation** on RTX 50 Series, and MFG and frame generation unlocks for older cards.

## Frame limiters

Install **ReLimiter** or **Display Commander** on any game, 32-bit or 64-bit. RHI turns off the driver frame limiter for that game so they don't fight.

## NVIDIA driver settings

Set VSync, Low Latency, Smooth Motion, G-Sync, ReBAR and more per game, plus global settings like shader cache size and FPS limit. Also includes Digital Vibrance, DLDSR switching, output colour depth and profile backups. You don't need NVIDIA App or Profile Inspector.

## Everyday conveniences

- **Update All** keeps everything current across your library, or let Automatic Updates do it in the background.
- Launch games from RHI, the tray or the taskbar.
- Turn Windows HDR on and switch resolution when a game starts, and back again when it closes.
- Drag and drop a game exe, mod, archive, preset or link onto the window.
- Quick links to Nexus Mods, PCGamingWiki, ultrawide fixes and Ultra+ for every game.
- New game support and fixes arrive without waiting for an app update.

## Getting started

1. Download and run RHI. Your games appear automatically.
2. Pick a game and install what you want: ReShade, RenoDX, a frame limiter.
3. Launch the game, press Home to open ReShade, go to Add-ons and set up RenoDX.
4. Use Update All to keep everything current.

## Tips

- Game not detected? Click **+** next to the search box, or drag its `.exe` onto the window.
- Check each game's Info button for setup notes.
- The Config button opens the game's config folder (where `Engine.ini` lives for Unreal Engine games).
- For UE-Extended games, turn on the game's own HDR option if it has one.
- Add a free GitHub token in Settings to avoid download and update check failures. It needs no permissions.
- Turn on Admin Mode in Settings for per-game driver settings, ReBAR and DLDSR.

## Requirements

- Windows 10 or 11 (64-bit) and the .NET 8 Desktop Runtime.
- An NVIDIA GPU for DLSS, driver and Neural Rendering features. ReShade, RenoDX, Luma and OptiScaler work on any GPU.

## Want to learn more about HDR?

See the [HDR guide]({{ '/hdr' | relative_url }}) for why HDR matters, what RenoDX does and how to get the best results.
