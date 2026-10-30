[center][size=6][b]RHI — ReShade HDR Installer[/b][/size]

[i]HDR mods, ReShade, DLSS and NVIDIA driver settings for your whole PC game library, from one window.[/i][/center]

RHI finds your games on Steam, Epic, GOG, EA App, Ubisoft Connect, Xbox / Game Pass, Battle.net and Rockstar. It then installs, updates and removes ReShade, RenoDX and Luma HDR mods, frame limiters, DLSS/Streamline files, OptiScaler, Neural Rendering tools and more, with per-game control over every one of them.

[center][img]https://raw.githubusercontent.com/RankFTW/RHI/main/screenshots/game_view.png[/img][/center]

[color=#FF6600][b]Single-player only.[/b][/color] ReShade with addon support, OptiScaler and DLSS/Streamline changes can trigger anti-cheat in online games. Uninstall them before playing multiplayer.

[b]Links:[/b] [url=https://github.com/RankFTW/RHI/blob/main/docs/DETAILED_GUIDE.md]Detailed Guide[/url] · [url=https://github.com/RankFTW/RHI]GitHub[/url] · [url=https://discord.gg/ultraplace]Discord[/url]

[line]

[size=5][b]HDR for Every Game[/b][/size]

[b]RenoDX[/b]: hundreds of game-specific HDR mods, matched and installed automatically and kept up to date.

[b]Unreal Engine and Unity[/b]: games without a dedicated mod get a generic one. UE4/UE5 games get UE-Extended, with reshade.ini and Engine.ini configured for you. Unity games get the generic Unity addon, in 32-bit and 64-bit versions.

[b]Luma Framework[/b]: named Luma mods, plus generic Luma for DX11 Unreal Engine games. Engine.ini keys, launch arguments and TAA settings from the Luma wiki are applied on install. DX9 Luma games get dgVoodoo2 automatically.

[b]RTX HDR and Windows Auto HDR[/b]: enable either per game. RTX HDR peak brightness, contrast, saturation, middle grey and debanding are written straight to the game's NVIDIA profile.

[b]DXVK + Lilium HDR[/b]: scRGB HDR for DX8–DX11 games, with presets from Safest to Experimental.

[b]DOF Fix[/b]: recommended for Unreal Engine 5.0–5.6 games.

[b]Available Mods[/b]: one searchable list of every game with a RenoDX, Luma or Ultra+ mod.

[line]

[size=5][b]Replaces the ReShade Installer[/b][/size]

[list]
[*]Picks the right DLL name for each game from its graphics API and bitness (dxgi.dll, d3d9.dll, d3d8.dll, opengl32.dll), or use your own name per game
[*]Per-game channels: Stable, Nightly, Custom (your own DLL), No Addons, and Legacy (older versions)
[*]Vulkan games use ReShade's global Vulkan layer
[*]Warns before overwriting another tool's DLL (DXVK, Special K, ENB) and backs up what it replaces
[*]Keeps reshade.ini set up: hotkeys, screenshot folder, peak brightness and effect list style, applied to one game or all of them. Lock any game's ini to stop RHI touching it
[*][b]56 shader packs[/b]: Lilium HDR, crosire, PumboAutoHDR, MaxG2D, RenoFX, UltraShade, iMMERSE, qUINT, SweetFX and many more. Pick packs or single .fx files, globally or per game. Save selections as profiles and share them as a zip
[*][b]Addon manager[/b]: the official ReShade addon list plus extras, globally or per game, and your own .addon64/.addon32 files
[*][b]ReShade presets[/b]: deploy presets to any game, along with the shaders they need
[/list]

[line]

[size=5][b]DLSS and Streamline[/b][/size]

[list]
[*]Swap Super Resolution, Ray Reconstruction, Frame Generation, Neural Rendering and Streamline versions per game. Originals are backed up and can be restored at any time
[*]Presets (including the DLSS 4.5 presets L and M) and render scale (33–100%, or DLAA), written to the NVIDIA driver profile. No Profile Inspector needed
[*]NVIDIA Override: turn on the driver's "use latest DLL" override per component
[*]Defaults + Quick Apply: choose your preferred versions and presets once, apply them to any game in one click
[*]Batch Deploy: push versions and presets to many games at once
[*]Optional auto-update to the newest DLSS or Streamline release
[/list]

[line]

[size=5][b]Neural Rendering (DLSS 5)[/b][/size]

Every game has a Neural Rendering section that installs one of four methods, and recommends the right one for the game:

[list]
[*][b]ShortFuse DLSS Tool[/b]: most games with native DLSS (DX9, DX11, DX12, Vulkan)
[*][b]DLSS5 Tool[/b]: DX12 games with native DLSS
[*][b]DLSS5 Tool + DX11 Bridge[/b]: DX11 and Vulkan games with native DLSS
[*][b]DLSS5 Feeder[/b]: games without DLSS, including OpenGL, DX9 and 32-bit games
[/list]

Pin versions of each tool and of nvngx_dlssnr.dll, and optionally add the NR Cost Scaler to run the neural model at reduced cost.

[line]

[size=5][b]Frame Generation and Upscaling[/b][/size]

[b]OptiScaler[/b]: Stable, Nightly (with build pinning), DLSS NR and F5 DLSSNR variants. Upscaler, frame generation, Reflex, preset and Neural Rendering settings are set from a menu that writes OptiScaler.ini for you. GPU-specific templates for NVIDIA, AMD and Intel. OptiPatcher included.

[b]Multi Frame Generation[/b]: Fixed or Dynamic MFG (2x–6x) with a target frame rate on RTX 50 Series.

[b]Unlocks for older cards[/b]: RTX 40 MFG, MFG Ada Unlock, 20/30 FG Unlock, RTX Encore (beta) and DLSS Enabler.

[b]Ultimate ASI Loader[/b] and [b]dgVoodoo2[/b], with a filename picker.

[line]

[size=5][b]Frame Limiters[/b][/size]

Choose one per game. Both support 32-bit and 64-bit games, and installing either one turns off the driver frame limiter for that game.

[b]ReLimiter[/b]: frame pacing limiter with an on-screen display, VRR caps and DLSS hooks.

[b]Display Commander[/b]: alternative limiter with display tools.

[line]

[size=5][b]NVIDIA Driver Settings[/b][/size]

[i]Written directly to the driver: no NVIDIA App or Profile Inspector needed.[/i]

[b]Per game:[/b] VSync, Tear Control, Low Latency, Smooth Motion (RTX 40+), Power Mode, G-Sync, ReBAR (enable, mode, size).

[b]Global:[/b] Shader Cache Size and Pre-Compile, G-Sync and G-Sync Mode, FPS Limit, Preferred Refresh Rate, VSync, Power Mode, ReBAR.

[list]
[*][b]Digital Vibrance[/b] per display, reapplied every time RHI starts
[*][b]DLDSR Control[/b]: save and switch DLDSR factor setups without NVIDIA Control Panel
[*][b]Backup / Restore[/b] of your per-game profiles, for example after a driver reinstall
[*][b]Output colour[/b]: colour depth (8/10/12 bpc) and dynamic range per display
[/list]

[line]

[size=5][b]Everyday Conveniences[/b][/size]

[list]
[*][b]Update All[/b]: ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, RE Framework, DXVK, Luma and DOF Fix across your library. You choose what's included. [b]Automatic Updates[/b] can install them in the background
[*][b]Game launch[/b] from RHI, the tray, the taskbar jump list or a desktop shortcut. Custom exe and arguments per game
[*][b]HDR and resolution auto-toggle[/b]: Windows HDR and your target resolution switch on when a game starts and back when it closes, on the monitors you choose
[*][b]Drag and drop[/b] a game exe, addon, archive, Luma mod, ReShade preset or link onto the window
[*]Per-game links to [b]Nexus Mods, PCGamingWiki, ultrawide fixes and Ultra+[/b]
[*][b]RE Framework[/b] for RE Engine games, and [b]Ryubing[/b] emulator support
[*]New game support and fixes arrive through a remote manifest, without waiting for an app update
[/list]

[line]

[size=5][b]Requirements[/b][/size]

[list]
[*]Windows 10 or 11 (64-bit)
[*][url=https://dotnet.microsoft.com/download/dotnet/8.0].NET 8 Desktop Runtime[/url]. The installer adds the Windows App Runtime if it's missing
[*]An NVIDIA GPU for DLSS presets, driver settings and Neural Rendering. ReShade, RenoDX, Luma and OptiScaler work on any GPU
[*]Admin Mode (Settings → System & Maintenance) for per-game driver settings, ReBAR and DLDSR Control
[*][b]Recommended:[/b] a free GitHub token (Settings → GitHub API). Without one, GitHub allows only 60 requests an hour, which can make downloads and update checks fail
[/list]

[line]

[i]RHI is free. ReShade, RenoDX, Luma, OptiScaler, DXVK, dgVoodoo2, the shader packs and every other third-party component are the work of their respective authors. Full credits are in the Detailed Guide.[/i]
