## v2.9.3 Beta 3

Changes since v2.9.3 Beta 2.

### New

**Colour themes**
- New Colour theme setting in Settings › General. Pick Default, OLED Black, Midnight Blue, Ember, Forest, Kiro, Slate, High Contrast or Colour-blind Friendly and RHI changes colour straight away, no restart needed.
- Custom lets you choose your own colours: background, panels, text, accent, buttons and the status colours (installed, update available, warning, error). Start from any built-in theme and adjust it from there. RHI warns you if text would be hard to read.
- Colour-blind Friendly swaps the green, purple, amber and red status colours for blue, pink, yellow and orange, which stay distinct with red-green colour blindness.

**Display modes (Monitor / TV)**
- Save sets of the settings RHI applies to a game and switch between them from its page, e.g. one for your monitor and one for your TV, or for trying different setups. Modes don't change your display themselves.
- Turn it on in Settings › Display › Display modes. The switch then appears beside the HDR and resolution buttons in Classic+, Tabs and Spotlight, and in the game menu (right-click a game).
- It starts with Monitor and TV. Add up to 6 modes (e.g. Handheld), rename and reorder them, and set each mode's values with Edit... (e.g. DLDSR on, DLSS preset M at 50% and HDR on). Every game you switch to that mode gets them.
- What a mode can swap: RenoDX peak, game and UI brightness; RTX HDR peak brightness; ReLimiter, Display Commander and NVIDIA driver frame rate caps; DLSS frame generation (off, 2x to 6x or dynamic); DLSS Super Resolution and Ray Reconstruction presets and render scales; the ReShade preset; ReShade overlay and screenshot keys, ReShade controller navigation and the ReLimiter OSD key; and, when you launch from RHI, Windows HDR, which display gets HDR, the resolution and the launch arguments.
- Custom mode for one game: the ⚙ beside a game's mode switch can make a Custom mode just for that game. Tick the settings it changes and set their values right there. It shows as Custom beside your other modes only for that game; settings it doesn't tick get the game's normal values while it's picked. Delete it from the same ⚙.
- Games start with their mode off and use your global settings. Click a game's active mode again (or pick Off in the ⚙ or the game menu) to turn its mode off: it goes back to your global settings (peak brightness, ReShade keys, ReLimiter frame rate cap and OSD key; the NVIDIA frame rate cap follows the global one), and settings with no global value go back to what the game had before you first switched it. HDR and resolution go back to the game's HDR and Resolution buttons.
- A mode only needs the values that differ: anything a mode has no value for uses the game's normal value (your global setting, or what the game had before modes).
- Values stored in the game's files are written as soon as you switch, so they also apply when you start the game from Steam or a shortcut. HDR, resolution and launch arguments apply when you launch from RHI.
- Switch all games: put every game in one mode at once, e.g. for an evening on the TV, or turn every game's mode off. Deleting a mode turns off the games in it.
- ReShade and ReLimiter installs and updates, and the Settings buttons that write peak brightness, keys or ReLimiter settings to all games, keep a game's display mode values while it's in a mode. Settings › Display modes says so at the top.

**Foveated DLSS5 Tool**
- New Foveated switch in Neural Rendering for the DLSS5 Tool and DLSS5 Tool + DX11 Bridge methods. It installs FoveatedDLSS5 (by maximedgoulet) instead of the stock add-on: Neural Rendering runs at full resolution in the centre of the screen, at lower resolution around it, and not at all at the edges. The author measured about 65% more fps, and the gain is biggest on ultrawide screens.
- With the switch on, the version list shows Foveated releases. Latest keeps it up to date on its own, and RHI's stock DLSS5 Tool updates no longer replace it. Switching while installed swaps the add-on in place.
- Each download is checked against the hash published with the release.
- It's a pre-release built on an older DLSS5 Tool (7.5.0-rc5). Turn on foveation and set the zones in the add-on's own settings in the ReShade overlay.

**All OptiScaler settings**
- New All Settings section in the OptiScaler ⚙: every setting in the game's OptiScaler.ini, with the options the installed OptiScaler build lists in its own ini. Custom builds and forks show their own settings and values (like F5's FG outputs) without waiting for an RHI update.
- Pick a section or search all settings; hover one for its description. Nothing is written until you press Apply, and the old ini is backed up first.
- Applied changes are saved for that game and written again when OptiScaler is reinstalled, updated or switched to another version.
- New Save ini and Load saved ini buttons at the bottom of the OptiScaler ⚙. Save a game's OptiScaler.ini under any name (as many as you like), or add one from a file, then load it into any game. The game keeps using it after a reinstall or update.

**Screenshots in Spotlight**
- New picture button beside the game's name in Spotlight: opens your ReShade screenshots of the game in the side panel, newest first.
- Click a screenshot to open it. Right-click it to set it as the game's banner, copy it, show it in its folder or delete it (to the Recycle Bin).

**Compile shaders now**
- New Compile shaders now button in Settings › Driver Settings, under Shader pre-compile. It runs NVIDIA's shader compiler straight away (the NVIDIA App's Compile now) instead of waiting for your PC to be idle, so games stutter less the first time you play them after a driver update. It rebuilds the shaders your games have already saved, so it has nothing to do right after Clear shader cache. It uses your Shader pre-compile level, takes a few minutes, and the same button stops it.

**ANR FG preset**
- New ANR FG preset in the OptiScaler ⚙. It installs ANR FG (Apex Neural Rendering frame generation, beta) and sets OptiScaler up to run it in place of the game's DLSS frame generation. Turn on DLSS frame generation in the game to use it.
- Needs OptiScaler Nightly, or a fork or custom build based on a recent Nightly (RHI checks the installed build), and a game with DLSS frame generation. Works on NVIDIA RTX 20 and newer and GTX 16, AMD RX 5000 and newer, and Intel Arc.
- RHI downloads the newest ANR FG release from Apex Compute's GitHub and checks it against the release's published hashes. Applying the preset again updates it, and your ANR settings (nr_fg.ini) are kept.
- Warning: ANR FG is closed source. Apex Compute publishes only the DLL, not its code, so nobody outside Apex Compute can check what it does. RHI tells you this before the first install, and you decide whether to go ahead.

**HSF frame generation**
- New HSF row in Extras › MFG Unlocks. HSF (Hunter's Sloppy Frames, by Capt-Hunter) is a ReShade add-on that adds DLSS frame generation, up to 6x, to DirectX 9, 11, 12 and Vulkan games that don't have it, 32-bit games included (Vulkan: 64-bit games).
- One click installs everything it needs: the add-on, HSF's Streamline and frame generation files, HSF.ini, HSF.fx and iMMERSE LaunchPad for optical flow, with HSF's licence files in its own folder. Set the multiplier and turn FRAME GEN on in HSF's tab in the ReShade overlay.
- DirectX 12 games work from the first launch: RHI sets up HSF's DirectX 12 mode before the game starts, so there's no restart. It follows your Rendering API override, and the ⚙ can set it up for a game RHI doesn't know runs DirectX 12.
- RHI keeps HSF up to date in the games it installed it in. Each update brings HSF's clean HSF.ini, as HSF asks, with your HSF settings carried over. The ⚙ shows the installed version and DirectX 12 mode, and turns LaunchPad off if you use LumeniteFX or the game's own motion instead.
- RTX 20, 30 and 40 cards: installing HSF offers Universal MFG Unlocker too, which those cards need for HSF's frame generation (RTX 40 for more than 2x). RHI detects your graphics card and doesn't ask on RTX 50. 64-bit games get the usual DLL name choice next to the game's exe; 32-bit games get it in HSF's own folder, where HSF loads it. It comes without the RTXMFG-XeSS folder, which HSF doesn't use.
- Vulkan games use ReShade's Vulkan layer. RHI tells you when a DirectX copy of ReShade (dxgi.dll) in the game's folder would keep HSF out.
- Needs ReShade with add-on support and an RTX 40 or newer GPU (RTX 20 and 30 through Universal MFG Unlocker). Never use it in games with online anti-cheat.

### Changes

- GITC Uplift 2.0 starts every game session with NR off. A new Start with NR on switch next to iMMERSE Launchpad in the Neural Rendering section turns it on at game start, as before 2.0.
- New GITC Uplift card in Settings › Components with an NR on/off key: the key that turns Uplift's NR on and off in game. RHI writes it into games when it installs Uplift, and Write key to all Uplift games writes it to every game with Uplift.
- Refresh now fully rescans the selected game, as Full Refresh does for every game: DLSS that a game update added or removed shows straight away, with no Full Refresh needed.
**Game search**
- Words in any order: batman lego finds LEGO Batman.
- Part of a game's initials: mw3 finds Call of Duty: Modern Warfare III.
- Small typos are fine: resident eviaa still finds Resident Evil. Longer words allow up to two letters off, short words fewer.
- When a game's full name or whole words match, near misses are hidden: sifu shows only Sifu, clone shows only games with Clone in the name.
- Best match first: the game whose name is what you typed, then names starting with it, then whole words, then the rest. resident evil 4 puts Resident Evil 4 at the top.
- Stores, engines, graphics APIs and mod authors only match whole words: st no longer brings up every Steam game while you type Stellar Blade, and un no longer every Unity and Unreal game. Type steam, unreal, dx12 or vulkan to list those games.

**Game settings in Spotlight**
- New cog button beside the game's name in Spotlight (next to the screenshots button): opens Game settings in the side panel, with settings from the Settings page that apply to that game only.
- RHI overlay: turn the overlay off for one game. Alt + ` then goes to the game instead of opening the overlay, and the controller shortcut does nothing in it.

**DLSS5 Feeder 2.0**
- New Feeder installs use the motion vectors built into Feeder 2.0.0, so LumeniteFX is no longer downloaded for it. Games that already use LumeniteFX keep it, and Feeder's own menu can switch them over.
- Installing Feeder now adds it to the game's ReShade preset instead of replacing the preset, so your other effects and their settings stay.
- The Neural Rendering status shows Built-in motion vectors instead of LumeniteFX for those games, and the Feeder description mentions its new in-game menu (Alt+C).

**Other**
- Spotlight: the Covers at bottom / top button is now a cog by the game counts. It has Covers below the game page and a new No background option, which gives the search, filters and covers the page colour instead of their own panel colour.
- GITC Uplift now installs iMMERSE Launchpad, which gives it motion vectors in games without DLSS, and turns it on above the Uplift effect in ReShade. It's on by default in games without DLSS, including existing installs. Games with DLSS use their own motion vectors: turn on the new iMMERSE Launchpad toggle under the Neural Rendering section if those look wrong in a game.
- Spotlight: the covers are centred whenever they all fit on screen, with any filter. Before, only Favourites and collections were centred.
- Neural Rendering: added a GITC Uplift link to the links at the bottom of the section.
- Settings › Display & HDR: new HDR button on game pages and Resolution button on game pages options hide either button on game pages. A game's own HDR and resolution choice still applies when you launch it from RHI.
- RTX Encore's DLL name picker can recommend a different name for a game when it needs one, and lists the recommended name first. Thanks emptyes for the first version of this.
- OptiScaler ⚙: FG Nvngx Override now lists custom frame generation providers found in OptiScaler's custom folder (such as ANR FG) by name.
- Installing RTX Encore, 20/30 FG Unlock or ANR FG (through its OptiScaler preset) now first tells you the mod is closed source (its author publishes only the DLL, not the code), with Install, Cancel and Don't show this again. The row's Info button opens its GitHub page.

### Bug Fixes

- Fixed a GITC Uplift update while a game was running leaving its add-on and helper on different versions. From Uplift 2.0 the two must match, so RHI now updates both together or waits for the next check.
- Fixed RTX Encore missing from the Extras summary (the collapsed section header and Spotlight's Extras tile) in games where it is installed.
**Vulkan ReShade**
- Fixed Vulkan games keeping an old ReShade version (for example 6.7.3 after 6.8.0 came out). Installing ReShade on a Vulkan game now also brings the shared Vulkan ReShade up to the chosen channel, and Vulkan games now show when a ReShade update is available, so Update All updates them.
- Fixed choosing Nightly or Stable on a Vulkan game sometimes not switching the ReShade build.
- Fixed choosing a Legacy version on a Vulkan game doing nothing. It now asks to apply the version to all Vulkan games, like the other channels, and switches it.

**Other**
- Fixed Spotlight showing no cover or banner, and no Steam page, PCGamingWiki info or achievements, for games whose store adds the platform to the name, such as Microsoft Store games ending in - Windows, (Windows 10), (Windows PC), (PC), for Windows, or just Windows or PC (Goat Simulator 3 PC). Store names with an edition (Dishonored Definitive Edition, NBA 2K26 Standard Edition) use the main game's page, and remakes PCGamingWiki lists with a year (Resident Evil 3 (2020)) are found too. Demos and playtests without artwork of their own now use the full game's.
- Fixed removing DLSS5 Feeder deleting the game's ReShade preset when the preset was there before Feeder.
- Fixed DLSS5 Feeder's motion vector choice being written to reshade.ini, where Feeder doesn't read it. It's now set in the preset, as Feeder's own installer does.
- Fixed a 32-bit DLSS5 Feeder install possibly pairing the add-on with a host64 helper from another Feeder release after a 64-bit game was set to an older Feeder version. Feeder 2.0.0 refuses a mismatched pair, so RHI now downloads a matching pair if needed.
- Fixed the HDR Gaming Database link (KoKlusz's HDR analysis of the game) missing from every game's RenoDX and Luma Info: the database changed its game list to a table, which RHI couldn't read.
- Fixed ReBAR changes (turning ReBAR on or off globally, the ReBAR mode and per-game ReBAR) not being saved since v2.8.9. RHI reported them as done, but the helper that writes them couldn't find a file it needs.
- Fixed a fresh Display Commander install getting no DisplayCommander.ini since v2.8.9.
- Fixed games with HSF installed showing HSF's own DLSS frame generation and Streamline files as the game's, which let DLSS updates replace them.
- Fixed buttons briefly flashing bright when the pointer moved over or off them with Windows animation effects on. Thanks Hyydro for finding the cause.
- Fixed Update History missing updates for GITC Uplift, Universal MFG Unlocker, DLSS Enabler, DLSSNR Cost Scaler, Ultimate ASI Loader, DXVK, RE Framework, UE DOF Fix, ReShade Nightly, DLSS and Streamline files and Nexus mods, and listing DLSS5 Feeder and DX11 Bridge updates as RenoDX DLSS5.
- Fixed OptiScaler updates deleting custom upscalers and frame generation providers in OptiScaler's custom folder.
- Fixed OptiPatcher never updating: a change in its release notes stopped RHI reading its version, so it always showed as up to date and new game patches never arrived.
- Fixed the OptiScaler ⚙'s FG Nvngx Override being greyed out and not saved when FG Input was DLSSG via Nvngx, where OptiScaler uses it.

### Manifest Updates

- Phantom Blade Zero: mods now go in the game's exe folder (platform\steam), where the ASI Loader and OptiScaler need to be. RHI moves existing installs there on its own.
- PD-Upscaler REFramework (Resident Evil 2, 3, 4, 7 and Village) installs again. Its per-game downloads were removed, so it now uses the universal PD-Upscaler build.
- Dead Space 2 and Dead Space 3 are no longer matched to a RenoDX mod from the wiki.
- RTX Encore recommends winmm.dll in Euro Truck Simulator 2, which RTX Encore 1.0.0-rc.3 needs there. Already installed under another name? Reinstall it and pick winmm.dll.
- DLSS5 Feeder's description now says it has its own motion vectors and an in-game menu (Alt+C).
- New recommended OptiScaler version: Feeder NR, the DLSS5 Feeder author's Neural Rendering fork. From v0.8.93, Feeder's menu changes its settings while the game runs.
