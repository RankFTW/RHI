# RHI — ReShade HDR Installer

**One tool. Every game. Full HDR, DLSS, and NVIDIA driver management from a single window.**

RHI detects your entire game library across every major store and handles ReShade, HDR mods, frame limiters, DLSS management, OptiScaler, shader packs, and NVIDIA driver profiles — all in one click.

---

## Download

**[Latest release](https://github.com/RankFTW/RenoDXChecker/releases/latest)** · **[Discord](https://discord.gg/yourserver)**

---

## What It Does

### One-Click HDR for Every Game

- **[RenoDX](https://github.com/clshortfuse/renodx)** — auto-detects and installs the right HDR mod for each game. Hundreds of bespoke game-specific mods, plus generic HDR support for every Unreal Engine and Unity game without one.
- **UE-Extended** — the default for all Unreal Engine games without a named mod. Automatically configures `reshade.ini` and `Engine.ini` with the correct HDR settings. No manual ini editing.
- **[Luma Framework](https://github.com/Filoppi/Luma-Framework)** — every DX11 Unreal Engine game in your library gets a Luma row automatically. Named mods for supported titles, generic support for everything else. Engine.ini keys, launch args, and TAA settings applied from the wiki automatically on install.
- **[DXVK + Lilium HDR](https://github.com/EndlesslyFlowering/dxvk)** — brings scRGB HDR output to older DX9 games that normally have no HDR path at all.
- **[RenoFX HDR Toolkit](https://github.com/clshortfuse/renofx)** — SDR-to-HDR conversion for any game that doesn't have a dedicated RenoDX mod yet.

### ReShade — Fully Managed

RHI replaces the ReShade installer entirely.

- Detects each game's graphics API via PE binary scanning and names the DLL correctly — `dxgi.dll`, `d3d9.dll`, `opengl32.dll` — automatically.
- Latest stable and nightly builds staged on every launch.
- Per-game channels: Stable, Nightly, Custom, Legacy, or No Addons. Mix freely across your library.
- Vulkan games use the global implicit layer — one-click install, no manual steps.
- Foreign DLL detection: if another tool (DXVK, Special K, ENB) is already in the game folder, you're warned before anything is overwritten.
- `reshade.ini` seeded and merged automatically — hotkeys, screenshot path, peak brightness, and DLSS paths all set without any manual configuration.

### DLSS 5 and Neural Rendering

- **DLSS5 ReShade AIO** — standalone Present-time Neural Rendering, DLSS/DLAA, and NVIDIA Frame Generation for games without native DLSS support. RHI selects the correct 32/64-bit release, installs its shaders and NVIDIA runtimes, and builds the required `host64` wrapper environment for 32-bit games automatically.
- **DLSS5 Feeder** — full pipeline install in one click: deploys feeder addon, DLSS5 consumer, `nvngx_dlss.dll`, `nvngx_dlssnr.dll`, pre-configured `ReShadePreset.ini`, and required shader packs. Auto-selects the right method for your game (32-bit, DX11, DX12).
- **ShortFuse DLSS Tool** — alternative neural rendering implementation for DX12 games.
- **DX11 Bridge variant** — DLSS5 Tool + bridge for games that need DX11 compatibility.
- **NR Cost Scaler** — wraps `nvngx_dlssnr.dll` to run neural rendering at reduced cost (default 75%). Toggle before installing a method to deploy it automatically.

### DLSS and Streamline Version Manager

- Swap SR, RR, FG, and Streamline versions per game from a dropdown. Every version from 2.x to current, plus Custom.
- Original DLLs backed up automatically. Restore the originals any time.
- Set global defaults once — Quick Apply pushes them to any game in one click.
- Batch Deploy: select multiple games from a checklist and push versions, presets, and render scales to all of them at once.
- DLSS presets (J/K/L/M for SR, D/E for RR, A/B for FG) written directly to NVIDIA driver profiles. No Profile Inspector needed.
- **DLSS SR Render Scale Override** — force 33–100% render resolution per game.
- **NVIDIA Override** — set the driver's "Latest DLL" flag per component directly from the version dropdown, equivalent to enabling it in NVIDIA App or Profile Inspector.
- Auto-update for DLSS SR, RR, and FG from the RHI manifest. Opt-in per-game.

### Multi Frame Generation

- **RTX 40 MFG Unlock** — enables DLSS MFG multipliers beyond 2x (up to 6x) on RTX 40 Series via Ultimate ASI Loader. One-click install from the Extras section.
- **MFG Ada Unlock** — in-memory MFG unlock addon for RTX 40 Series via the addon manager.
- **RTX 50 Series MFG** — configure Fixed or Dynamic MFG mode, frame count (2x–6x), and dynamic target frame rate from the DLSS section.

### OptiScaler

- Redirects DLSS API calls to FSR4/FSR3/XeSS for AMD and Intel GPUs, and adds frame generation support where the game doesn't have it.
- **Stable and Nightly channels** per game, selectable in the cog.
- **Per-GPU INI templates** — six pre-configured templates (NVIDIA, AMD+DLSS, AMD no-DLSS, each in Stable and Nightly). Seeded on first launch, user-editable, never overwritten.
- **Streamline + DLSS Enabler deploy** (Nightly) — toggle per game to deploy Streamline DLLs and DLSS Enabler to the OptiScaler folder. Pick the Streamline version per game.
- Frame Generation settings: FG Input, FG Output, FG Nvngx Replacement, HUD Fix — all configurable in the cog.
- **OptiPatcher** auto-deployed to all games with OptiScaler installed. Auto-updates silently.
- ReShade coexistence handled automatically — filenames adjusted, `LoadReshade=true` set.

### Frame Limiters

Two per-game frame limiters, mutually exclusive:

- **[ReLimiter](https://github.com/RankFTW/ReLimiter)** — precision frame pacing with predictive sleep, phase-locked timing grid, and closed-loop correction. VRR-aware, DLSS FG adaptive, Reflex-integrated.
- **[Display Commander](https://github.com/pmnoxx/display-commander)** — alternative limiter supporting both 32-bit and 64-bit games.

Both deploy as ReShade addons. Both participate in Update All.

### NVIDIA Driver Profile Management — Replaces NVIDIA App and Profile Inspector

All per-game driver settings written directly via NvAPI. No NVIDIA Profile Inspector, no NVIDIA App needed.

**Per-game overrides:**
- VSync Mode, Tear Control, Low Latency (Off/On/Ultra)
- Smooth Motion: Enable, Allowed APIs, Flip Pacing — automatically sets Low Latency to Ultra when enabled
- Power Management Mode
- G-Sync per-game toggle
- ReBAR: Enable (Off/On/Auto), Mode (Standard/Optimized), Size Limit
- RTX HDR: Enable/Configure with Peak Brightness, Contrast, Saturation, Middle Grey, Debanding
- DLSS Presets and Render Scale Override (no admin required)

**Global settings (base profile):**
- Shader Cache Size and Pre-Compile
- G-Sync Mode and Preferred Refresh Rate
- Global ReBAR
- FPS Limit (Frame Rate Limiter V3)
- Digital Vibrance per display — restored automatically on every app startup

**Export/Import:** back up all per-game NVIDIA profiles to a JSON file. Import restores everything after a driver reinstall.

### HDR Auto-Toggle

Enables Windows HDR when you launch a game through RHI and disables it when the game closes. Works with Steam, Epic, Xbox, and direct exe launches. Per-game override button next to Launch — purple when active.

### Shader Pack Management — 46 Packs

Every shader pack you'd ever want, all managed in one place:

- **Essential**: Lilium HDR Shaders (EndlesslyFlowering) — required for HDR tone mapping
- **Recommended**: crosire reshade-shaders, PumboAutoHDR, MaxG2D Simple HDR, clshortfuse shaders, RenoFX HDR Toolkit, smolbbsoop shaders
- **Extra**: 39 additional packs — SweetFX, OtisFX, qUINT, iMMERSE, Prod80, CorgiFX, ZenteonFX, CRT-Royale, VRToolkit, GShade-Shaders, QD-OLED APL Fixer, LumaBoost, and more

Select packs globally or override per game. Expand any pack to pick individual `.fx` files — tri-state checkbox for partial selection. Save selections as named profiles, export as zip, share directly into Discord.

Custom shaders go in `%LocalAppData%\RHI\Custom\reshade\Shaders\` — RHI deploys them alongside managed packs.

### ReShade Addon Management

Global addon manager from the toolbar. Enable/disable per game or globally. Custom addons in `%LocalAppData%\RHI\Custom\Addons\` automatically appear in the picker.

### Update All

One button in the toolbar updates ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, and RE Framework across your entire library. Per-game update exclusions respected. Lights up purple when updates are available.

Background check every 4 hours while running — works from the system tray without keeping the window open.

### Manifest-Driven Updates

Game data, mod links, Engine.ini configurations, launch fixes, and feature flags are all driven by a manifest fetched from GitHub on every launch. This means:

- New game support, fixes, and tweaks deploy to all users immediately — no app update needed
- Per-game Engine.ini overrides (like Black Myth: Wukong's custom HDR keys) delivered server-side
- Feature flags enable dev-preview features for all users via manifest toggle

---

## Supported Stores

Steam · Epic Games · GOG · Xbox / Game Pass · EA App · Ubisoft Connect · Battle.net · Rockstar · itch.io

Drag and drop any game's `.exe` onto the window to add it manually if it's not auto-detected.

---

## Requirements

- Windows 10 / 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- NVIDIA GPU recommended for driver profile features (ReShade and RenoDX work on all GPUs)
- Admin mode required for: ReBAR, Smooth Motion, Low Latency (Ultra), Vulkan ReShade, driver export/import

---

## Third-Party Components

| Component | Author |
|-----------|--------|
| [ReShade](https://reshade.me) | Crosire |
| [RenoDX](https://github.com/clshortfuse/renodx) | clshortfuse & contributors |
| [ReLimiter](https://github.com/RankFTW/ReLimiter) | RankFTW |
| [Display Commander](https://github.com/pmnoxx/display-commander) | pmnoxx |
| [RE Framework](https://github.com/praydog/REFramework-nightly) | praydog |
| [Luma Framework](https://github.com/Filoppi/Luma-Framework) | Pumbo (Filoppi) |
| [OptiScaler](https://github.com/optiscaler/OptiScaler) | OptiScaler contributors |
| [DXVK](https://github.com/doitsujin/dxvk) | doitsujin & contributors |
| [DXVK HDR-mod](https://github.com/EndlesslyFlowering/dxvk) | EndlesslyFlowering (Lilium) |
| [Ultimate ASI Loader](https://github.com/ThirteenAG/Ultimate-ASI-Loader) | ThirteenAG |
