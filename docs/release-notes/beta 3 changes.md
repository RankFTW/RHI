## v2.9.3 Beta 3

Changes since v2.9.3 Beta 2.

### New

**Display modes (Monitor / TV)**
- Save sets of the settings RHI applies to a game and switch between them from its page, e.g. one for your monitor and one for your TV, or for trying different setups. Modes don't change your display themselves. Each mode swaps only what you pick for that game: one game might swap peak brightness and its frame rate cap, another those plus its keys and DLSS settings.
- Turn it on in Settings › Display › Display modes. The switch then appears beside the HDR and resolution buttons in Classic+, Tabs and Spotlight, and in the game menu (right-click a game).
- It starts with Monitor and TV. Add up to 6 modes (e.g. Handheld), rename and reorder them, and give each mode default values.
- What a mode can swap: RenoDX peak, game and UI brightness; RTX HDR peak brightness; ReLimiter, Display Commander and NVIDIA driver frame rate caps; DLSS frame generation (off, 2x to 6x or dynamic); DLSS preset and render scale; the ReShade preset; ReShade overlay and screenshot keys, ReShade controller navigation and the ReLimiter OSD key; and, when you launch from RHI, Windows HDR, which display gets HDR, the resolution and the launch arguments.
- Each game remembers its own values per mode. When you switch away from a mode, what the game has then (including changes you made in game) is kept for that mode and put back next time.
- Games start with their mode off and use your global settings. Click a game's active mode again (or pick Off in the ⚙ or the game menu) to turn its mode off: it goes back to your global settings (peak brightness, ReShade keys, ReLimiter frame rate cap and OSD key; the NVIDIA frame rate cap follows the global one), and settings with no global value go back to what the game had before you first switched it. HDR and resolution go back to the game's HDR and Resolution buttons.
- A mode only needs the values that differ: anything a mode has no value for uses the game's normal value (your global setting, or what the game had before modes).
- Pick what games swap in Settings, or give a game its own list with the ⚙ beside its mode switch. That ⚙ also sets the game's values for each mode.
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

### Changes

**Game search**
- Words in any order: batman lego finds LEGO Batman.
- Part of a game's initials: mw3 finds Call of Duty: Modern Warfare III.
- Small typos are fine: resident eviaa still finds Resident Evil. Longer words allow up to two letters off, short words fewer.

**Game settings in Spotlight**
- New cog button beside the game's name in Spotlight (next to the screenshots button): opens Game settings in the side panel, with settings from the Settings page that apply to that game only.
- RHI overlay: turn the overlay off for one game. Alt + ` then goes to the game instead of opening the overlay, and the controller shortcut does nothing in it.

**Other**
- Spotlight: the Covers at bottom / top button is now a cog by the game counts. It has Covers below the game page and a new No background option, which gives the search, filters and covers the page colour instead of their own panel colour.
- GITC Uplift now installs iMMERSE Launchpad, which gives it motion vectors in games without DLSS, and turns it on above the Uplift effect in ReShade. It's on by default in games without DLSS, including existing installs. Games with DLSS use their own motion vectors: turn on the new iMMERSE Launchpad toggle under the Neural Rendering section if those look wrong in a game.
- Neural Rendering: added a GITC Uplift link to the links at the bottom of the section.
- RTX Encore's DLL name picker can recommend a different name for a game when it needs one, and lists the recommended name first. Thanks emptyes for the first version of this.

### Bug Fixes

**Vulkan ReShade**
- Fixed Vulkan games keeping an old ReShade version (for example 6.7.3 after 6.8.0 came out). Installing ReShade on a Vulkan game now also brings the shared Vulkan ReShade up to the chosen channel, and Vulkan games now show when a ReShade update is available, so Update All updates them.
- Fixed choosing Nightly or Stable on a Vulkan game sometimes not switching the ReShade build.
- Fixed choosing a Legacy version on a Vulkan game doing nothing. It now asks to apply the version to all Vulkan games, like the other channels, and switches it.

**Other**
- Fixed buttons briefly flashing bright when the pointer moved over or off them with Windows animation effects on. Thanks Hyydro for finding the cause.

### Manifest Updates

- Dead Space 2 and Dead Space 3 are no longer matched to a RenoDX mod from the wiki.
- RTX Encore recommends winmm.dll in Euro Truck Simulator 2, which RTX Encore 1.0.0-rc.3 needs there. Already installed under another name? Reinstall it and pick winmm.dll.
