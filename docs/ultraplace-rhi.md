---
layout: default
title: RHI
permalink: /tools/rhi/
---

# RHI — Simplified PC Gaming

RHI is a desktop app that manages HDR mods, ReShade, frame limiters, DLSS/Streamline, and more across your entire PC game library. Auto-detects games from 8 storefronts with one-click install for everything. Built by RankFTW.

## What it does

- Auto-detects games from Steam, GOG, Epic, EA, Ubisoft, Xbox/Game Pass, Battle.net, and Rockstar
- Detects each game's engine (Unreal Engine with version, Unity, RE Engine) and graphics API automatically
- One-click install, update, and uninstall for ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, RE Framework, Luma Framework, and DXVK
- DLSS & Streamline version management — swap SR, RR, FG independently, set DLSS presets and render scale per-game
- ReBAR (Resizable BAR) control per-game with mode and size limit options
- 43 shader packs with global or per-game selection
- Drag-and-drop mods, presets, and Luma archives
- Automatic Engine.ini and reshade.ini configuration for UE-Extended native HDR games
- Per-game overrides for DLL naming, shader mode, addon mode, ReShade channel, DXVK variant, and launch arguments
- Game launch directly from RHI with Steam overlay and playtime tracking
- Nexus Mods update detection (no API key needed)
- Automatic app and manifest updates

## Download

Get RHI from [GitHub](https://github.com/RankFTW/RHI/releases) or [NexusMods](https://www.nexusmods.com/site/mods/1710).

For a detailed user guide, see the [RHI documentation](https://github.com/RankFTW/RHI/blob/main/docs/DETAILED_GUIDE.md).

## Getting started

1. Download and run RHI — your games appear automatically
2. Pick a game and click Install on the components you want (ReShade, RenoDX, a frame limiter)
3. Launch the game, press Home to open ReShade, go to Add-ons, and configure RenoDX
4. Use Update All to keep everything current across your library

RHI deploys a pre-configured `reshade.ini` with unnecessary addons disabled and optimal settings for HDR. UE-Extended games get Engine.ini HDR settings deployed automatically — no manual INI editing needed.

## Tips

- If a game isn't detected, drag the .exe directly into the RHI window
- Check the per-game Info buttons for game-specific setup notes
- The Config button opens the game's Engine.ini folder directly
- If installing ReShade manually (without RHI), disable the "Generic Depth" and "Effect Runtime Sync" addons — they cost performance with no benefit when using RenoDX
- For UE-Extended native HDR games: enable the in-game HDR option if one exists

## Want to learn more about HDR?

See the [HDR guide]({{ '/hdr' | relative_url }}) for a deeper explanation of why HDR matters, what RenoDX does, and how to get the best results.
