## v2.9.3 Beta 2

Changes since v2.9.3 Beta 1.

### New

**Source engine games**
- RenoDX's Source engine mod now installs from RHI in one click for the games in the Source list (Half-Life 2, Portal, Portal 2, Left 4 Dead 2, Team Fortress 2, Garry's Mod and more), with a Source Engine badge on the game.
- RHI does the whole setup from the RenoDX instructions: ReShade as d3d9.dll in the right folder, DXVK (stable) loaded through ReShade, and the reshade.ini and dxvk.conf settings. 32-bit games use the bin folder and 64-bit games the main folder. Garry's Mod gets both versions set up (bin for 32-bit, bin\win64 for 64-bit), sharing one set of shaders. Updating the mod keeps it all up to date, and uninstalling removes it again.
- -insecure is added to the game's launch options in RHI. It turns VAC off, so don't join VAC-secured servers while the mod is installed. If you start the game from Steam, add -insecure to its launch options there too.
- In the game, set High Dynamic Range to Full in the advanced video settings.
- If RHI already installed ReShade in a 32-bit Source game's main folder, it's moved to bin.

**Screenshots in Spotlight**
- New picture button beside the game's name in Spotlight: opens your ReShade screenshots of the game in the side panel, newest first.
- Click a screenshot to open it. Right-click it to set it as the game's banner, copy it, show it in its folder or delete it (to the Recycle Bin).

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

**Display modes (Monitor / TV)**
- Switch a game between your displays from its page. Each mode swaps only what you pick for that game: one game might swap peak brightness and its frame rate cap, another those plus its keys and DLSS settings.
- Turn it on in Settings › Display › Display modes. The switch then appears beside the HDR and resolution buttons in Classic+, Tabs and Spotlight, and in the game menu (right-click a game).
- It starts with Monitor and TV. Add up to 6 modes (e.g. Handheld), rename and reorder them, and give each mode default values.
- What a mode can swap: RenoDX peak, game and UI brightness; RTX HDR peak brightness; ReLimiter, Display Commander and NVIDIA driver frame rate caps; DLSS frame generation (off, 2x to 6x or dynamic); DLSS preset and render scale; the ReShade preset; ReShade overlay and screenshot keys, ReShade controller navigation and the ReLimiter OSD key; and, when you launch from RHI, Windows HDR, which display gets HDR, the resolution and the launch arguments.
- Each game remembers its own values per mode. When you switch away from a mode, what the game has then (including changes you made in game) is kept for that mode and put back next time.
- Pick what games swap in Settings, or give a game its own list with the ⚙ beside its mode switch. That ⚙ also sets the game's values for each mode.
- Values stored in the game's files are written as soon as you switch, so they also apply when you start the game from Steam or a shortcut. HDR, resolution and launch arguments apply when you launch from RHI.
- Switch all games: put every game in one mode at once, e.g. for an evening on the TV.
- ReShade and ReLimiter installs and updates, and the Settings buttons that write peak brightness, keys or ReLimiter settings to all games, keep a game's display mode values.

### Changes

**Game Pass games**
- Game Pass games installed to an Xbox games folder (the Xbox app's default) now use that folder instead of the protected WindowsApps one. The folder no longer changes when the game updates, so installed mods stay recognised, and RHI can read the game's files to detect its graphics API and DLSS like any other game.
- Existing installs carry over on their own: nothing is moved or reinstalled.

**Game search**
Search now works much like Steam's:
- Punctuation and spaces are ignored: fear finds F.E.A.R., half life finds Half-Life 2, spiderman finds Marvel's Spider-Man.
- Words in any order: batman lego finds LEGO Batman.
- Initials, or part of them: gta, rdr2, ff7, hl2, and mw3 finds Call of Duty: Modern Warfare III.
- Roman numerals as numbers: gta 3 finds Grand Theft Auto III, final fantasy 7 finds Final Fantasy VII.
- Small typos are fine: resident eviaa still finds Resident Evil. Longer words allow up to two letters off, short words fewer.

**Settings**
- New Tabs button beside Find a setting: shows one section at a time as tabs, like the Tabs layout. Search still finds settings in every section and opens the right tab. Click it again for the full list.

**Shader picker**
- Bigger, and uses the space: packs are cards in two columns, highlighted when selected, with a green tick when downloaded and their full description.
- Show shaders opens a pack's shader list inside its card.
- Each category, and the top of the list, shows how many packs are selected.

**Achievements**
- New Show hidden button beside the achievement search, in Spotlight and in the RHI overlay. It shows the name and description of hidden achievements you haven't unlocked yet, which may spoil the story. Off by default.

**Top bar and game list sizes**
- Resize the top bar in every layout: drag its bottom edge, or pick a size in Settings › General › Top bar size. It changes size on its own, on top of the interface size.
- Resize the game list in Classic+ and Tabs: drag its right edge, or pick a size in Settings › General › Game list size. Its text, rows and width change together.
- RHI remembers the sizes. Double-click an edge to reset it.

**Game settings in Spotlight**
- New cog button beside the game's name in Spotlight (next to the screenshots button): opens Game settings in the side panel, with settings from the Settings page that apply to that game only.
- RHI overlay: turn the overlay off for one game. Alt + ` then goes to the game instead of opening the overlay, and the controller shortcut does nothing in it.

**Other**
- The ReShade Addon Manager (global and per game) is wider, so addon descriptions are easier to read.
- Spotlight: removed the Manage components button from What's installed. Click any component tile to open the components.

### Bug Fixes

**RHI overlay**
- Fixed the overlay opening over other apps, such as Discord, File Explorer and MSI Afterburner. It now only opens over games in your RHI library.
- Fixed the overlay not recognising Game Pass games installed to an Xbox games folder.

**HDR and resolution when a game closes**
- Fixed HDR and resolution not going back after you refreshed or restarted RHI while a game it started was running. RHI now remembers the games it started (Launch button or an RHI shortcut): they still show as running, and HDR and resolution go back when they close. If a game closed while RHI wasn't open, RHI switches them back the next time it starts.
- Fixed HDR and resolution never going back for games started through a launch exe from RHI's game data, and staying changed when RHI couldn't find a way to start the game.
- Fixed one game turning HDR off or changing the resolution back while another game started from RHI was still running.

**Game Pass games**
- Fixed Game Pass games installed to an Xbox games folder never showing as running when launched from RHI, so HDR and resolution weren't switched back when they closed.
- Fixed Game Pass games with GITC Uplift showing an unknown graphics API after every launch.
- Fixed Game Pass games getting the wrong game folder, a folder-style name (Halo- Campaign Evolved) and not launching when Windows couldn't list installed apps.

**RenoDX**
- Fixed a RenoDX addon saved as a second download (for example renodx-ue-extended (1).addon64) not being recognised, so the game showed Download from Discord. Installing or updating now also removes such copies, so the game no longer loads the mod twice, and dropping one onto RHI installs it under its proper name.
- Fixed Download from Discord staying on a game after uninstalling an unrecognised RenoDX addon, until RHI was restarted.

**Vulkan ReShade**
- Fixed Vulkan games keeping an old ReShade version (for example 6.7.3 after 6.8.0 came out). Installing ReShade on a Vulkan game now also brings the shared Vulkan ReShade up to the chosen channel, and Vulkan games now show when a ReShade update is available, so Update All updates them.
- Fixed choosing Nightly or Stable on a Vulkan game sometimes not switching the ReShade build.
- Fixed choosing a Legacy version on a Vulkan game doing nothing. It now asks to apply the version to all Vulkan games, like the other channels, and switches it.

**Other**
- Fixed some GOG games (Ghost Song and other Unity games) showing as 32-bit, so ReShade and RenoDX installed the 32-bit files. RHI was reading GOG's uninstaller instead of the game. Affected games correct themselves on the next launch.
- Fixed achievement pictures missing for older Steam games such as Half-Life 2.

### Manifest Updates

- Ghost Song is always treated as a 64-bit game.
