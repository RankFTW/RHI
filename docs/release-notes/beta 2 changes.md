## v2.9.3 Beta 2

Changes since v2.9.3 Beta 1.

### New

**Source engine games**
- RenoDX's Source engine mod now installs from RHI in one click for the games in the Source list (Half-Life 2, Portal, Portal 2, Left 4 Dead 2, Team Fortress 2, Garry's Mod and more), with a Source Engine badge on the game.
- RHI does the whole setup from the RenoDX instructions: ReShade as d3d9.dll in the right folder, DXVK (stable) loaded through ReShade, and the reshade.ini and dxvk.conf settings. 32-bit games use the bin folder and 64-bit games the main folder. Garry's Mod gets both versions set up (bin for 32-bit, bin\win64 for 64-bit), sharing one set of shaders. Updating the mod keeps it all up to date, and uninstalling removes it again.
- -insecure is added to the game's launch options in RHI. It turns VAC off, so don't join VAC-secured servers while the mod is installed. If you start the game from Steam, add -insecure to its launch options there too.
- In the game, set High Dynamic Range to Full in the advanced video settings.
- If RHI already installed ReShade in a 32-bit Source game's main folder, it's moved to bin.

### Changes

**Game Pass games**
- Game Pass games installed to an Xbox games folder (the Xbox app's default) now use that folder instead of the protected WindowsApps one. The folder no longer changes when the game updates, so installed mods stay recognised, and RHI can read the game's files to detect its graphics API and DLSS like any other game.
- Existing installs carry over on their own: nothing is moved or reinstalled.

**Game search**
- Punctuation and spaces are ignored, like Steam: fear finds F.E.A.R., half life finds Half-Life 2, spiderman finds Marvel's Spider-Man.
- Search by initials: gta, rdr2, ff7, hl2.
- Type Roman numerals as numbers: gta 3 finds Grand Theft Auto III, final fantasy 7 finds Final Fantasy VII.

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

**Other**
- Fixed some GOG games (Ghost Song and other Unity games) showing as 32-bit, so ReShade and RenoDX installed the 32-bit files. RHI was reading GOG's uninstaller instead of the game. Affected games correct themselves on the next launch.
- Fixed achievement pictures missing for older Steam games such as Half-Life 2.

### Manifest Updates

- Ghost Song is always treated as a 64-bit game.
