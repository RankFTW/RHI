[center][size=6][b]RHI — ReShade HDR Installer[/b][/size]

[i]HDR, ReShade, DLSS and NVIDIA driver settings for your whole PC game library, from one window.[/i][/center]

RHI finds your games on Steam, Epic, GOG, EA App, Ubisoft Connect, Xbox / Game Pass, Battle.net and Rockstar, then installs, updates and removes the mods that make them look and run better. One click per game, with everything backed up and reversible.

[center][img]https://raw.githubusercontent.com/RankFTW/RHI/main/docs/images/game_view.png[/img][/center]

[color=#FF6600][b]Single-player only.[/b][/color] ReShade with addon support, OptiScaler, frame generation mods and DLSS/Streamline changes can trigger anti-cheat in online games. Uninstall them before playing multiplayer.

[b]Links:[/b] [url=https://github.com/RankFTW/RHI/blob/main/docs/DETAILED_GUIDE.md]Detailed Guide[/url] · [url=https://github.com/RankFTW/RHI]GitHub[/url] · [url=https://discord.gg/ultraplace]Discord[/url]

[line]

[size=5][b]Why RHI[/b][/size]

[list]
[*][b]HDR in a minute.[/b] Pick a game, click Install twice. RHI finds the right RenoDX or Luma mod, installs ReShade under the right name and keeps both updated
[*][b]No more tool juggling.[/b] It replaces the ReShade installer, DLSS Swapper, NVIDIA Profile Inspector and a folder of downloaded mods
[*][b]Safe to try.[/b] RHI backs up what it replaces, warns before touching another tool's files, and removes cleanly
[*][b]Per-game control.[/b] Every setting can be set once for all games and changed for any one of them
[*][b]Always current.[/b] New game support and fixes arrive without waiting for an app update
[/list]

[line]

[size=5][b]HDR for Every Game[/b][/size]

[list]
[*][b]RenoDX[/b]: hundreds of game-specific HDR mods, matched automatically. Unreal Engine, Unity and Source engine games without their own mod get a generic one, set up for you
[*][b]Luma Framework[/b]: named Luma mods, plus generic Luma for DX11 Unreal Engine games
[*][b]RTX HDR and Windows Auto HDR[/b]: switch either on per game and tune it without opening the NVIDIA App
[*][b]DXVK + Lilium HDR[/b]: scRGB HDR for DX8 to DX11 games
[*][b]Available Mods[/b]: one searchable list of every game with a RenoDX, Luma or Ultra+ mod
[/list]

[line]

[size=5][b]ReShade, Fully Managed[/b][/size]

[list]
[*]The right DLL name for every game's graphics API, or your own
[*]Stable, Nightly, Legacy, No Addons or your own build, per game. Vulkan games handled too
[*][b]57 shader packs[/b], an addon manager and ReShade presets, for all games or just one. Save shader selections as profiles and share them
[*]Your hotkeys, screenshot folder and peak brightness written to every game
[/list]

[line]

[size=5][b]DLSS, Streamline and Neural Rendering[/b][/size]

[list]
[*]Swap [b]Super Resolution, Ray Reconstruction, Frame Generation and Streamline[/b] versions per game. Originals are always restorable
[*]Set [b]presets[/b] (including the DLSS 4.5 presets L and M) and [b]render scale[/b] straight in the driver profile
[*][b]Quick Apply[/b] your favourite setup to a game in one click, or [b]Batch deploy[/b] it to many
[*][b]Neural Rendering (DLSS 5)[/b] on almost any game, with five methods and the right one picked for you: GITC Uplift, ShortFuse DLSS Tool, DLSS5 Tool (with optional foveated rendering), DX11 Bridge and DLSS5 Feeder
[/list]

[line]

[size=5][b]Frame Generation for Any Card[/b][/size]

[list]
[*][b]Multi Frame Generation[/b] (2x to 6x, fixed or dynamic) on RTX 50
[*][b]HSF[/b] and [b]RTX Encore[/b]: frame generation in games that don't have it
[*][b]Universal MFG Unlocker, MFG Unlock, 20/30 FG Unlock and DLSS Enabler[/b]: multi frame generation on RTX 20, 30 and 40, and on AMD and Intel cards
[*][b]OptiScaler[/b]: Stable, Nightly, Neural Rendering forks and your own builds, with ready-made presets and every ini setting in a searchable menu
[/list]

[line]

[size=5][b]NVIDIA Driver Settings, Without the NVIDIA App[/b][/size]

[list]
[*][b]Per game[/b]: VSync, Low Latency, Smooth Motion, G-Sync, FPS limit, ReBAR, LOD bias and texture filtering
[*][b]Global[/b]: shader cache and Compile shaders now, G-SYNC on each monitor, DLDSR setups, digital vibrance, output colour depth, and RTX Video Super Resolution and HDR
[*][b]Monitors[/b]: resolution, refresh rate and HDR for every connected display
[*]Back up and restore your profiles after a driver reinstall
[/list]

[line]

[size=5][b]Frame Limiters and Smoother Motion[/b][/size]

[list]
[*][b]ReLimiter[/b] or [b]Display Commander[/b]: one per game, with VRR-aware caps
[*][b]BFI Presenter[/b]: black frame insertion for sharper motion
[*][b]Experimental[/b]: FrameWarp reprojection, URTI ray-traced reflections and CRCGI global illumination
[/list]

[line]

[size=5][b]A Launcher You'll Want to Use[/b][/size]

[list]
[*][b]Three layouts[/b]: Classic+, Tabs, and Spotlight with cover art, banners and full controller support (Xbox, PlayStation and Steam controllers)
[*][b]RHI overlay[/b]: press Alt + ` in a game for your achievements, screenshots, playtime, notes and mod setup. Nothing is installed into the game
[*][b]Display modes[/b]: save a Monitor setup and a TV setup, and switch a game (or every game) between them in one click
[*][b]HDR and resolution switching[/b] when a game starts, and back when it closes
[*][b]Collections[/b], Steam-style search, Steam and Xbox achievements, colour themes, and interface scaling from 50% to 200%
[*][b]Update All[/b], optional automatic updates, and [b]backup and restore[/b] that carries your whole setup to a new PC
[/list]

Everything is covered step by step in the [url=https://github.com/RankFTW/RHI/blob/main/docs/DETAILED_GUIDE.md]Detailed Guide[/url].

[line]

[size=5][b]Requirements[/b][/size]

[list]
[*]Windows 10 or 11 (64-bit) and the [url=https://dotnet.microsoft.com/download/dotnet/8.0].NET 8 Desktop Runtime[/url]. If the Windows App Runtime 2.5 is missing, RHI says so and offers the download
[*]An NVIDIA card for DLSS presets, driver settings and Neural Rendering. ReShade, RenoDX, Luma and OptiScaler work on any card
[*][b]Recommended:[/b] a free GitHub token with no permissions (Settings → Accounts & Data → GitHub token). Without one, GitHub allows only 60 requests an hour, which can make downloads and update checks fail
[/list]

[line]

[i]RHI is free. ReShade, RenoDX, Luma, OptiScaler, DXVK, HSF, RTX Encore, the shader packs and every other third-party component are the work of their respective authors. Full credits are in the Detailed Guide.[/i]
