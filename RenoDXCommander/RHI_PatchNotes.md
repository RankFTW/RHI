## v1.8.1

### New Features

**Compact View mode**
- New third view mode alongside Detail and Grid. Toggle between views using the toolbar button.
- Compact View shows the same game card, overrides, and management panels as Detail View, split across three navigable pages with left/right arrow buttons.
- The window locks to a fixed size in Compact View and restores your previous size when switching back.
- Your chosen view mode is remembered across app restarts.

**View-specific loading skeletons**
- The loading skeleton now matches your last-used view. Grid View shows a card grid skeleton, Detail and Compact Views show the detail panel skeleton.

**PD-Upscaler REFramework for OptiScaler on RE Engine games**
- When installing OptiScaler on Resident Evil 2, 3, 4, 7, or Village, RHI automatically downloads and installs the pd-upscaler branch of REFramework required for OptiScaler compatibility.
- The standard REFramework is backed up and restored when OptiScaler is uninstalled.
- The RE Framework version display updates in real time to show "PD-Upscaler" while OptiScaler is active.

**Global Update Check toggles**
- New settings section to globally disable update checks for individual components: RenoDX, ReShade, ReLimiter, Display Commander, and OptiScaler.
- When disabled, the component is skipped during startup update checks and Update All.

**Manifest author overrides**
- Mod authors can now be set via the remote manifest for games that aren't on the wiki. Author badges and donation links work the same as wiki-sourced authors.

### Changes

- The toolbar button now shows the name of the current view mode instead of the next mode.
- Manifest `forceExternalOnly` entries pointing to Nexus Mods now show a "Nexus" badge instead of "Discord".
- Nav arrow buttons in Compact View now match the toolbar button styling.

### Bug Fixes

**Xbox / Game Pass games no longer lose mods after game updates**
- When a Game Pass game updates, Windows changes its install folder path. Previously this caused RHI to lose track of installed mods, showing them as needing reinstall. RHI now detects the path change and migrates your installed mods (RenoDX, ReShade, Display Commander, OptiScaler) to the new folder automatically.

**ReShade detection for ReShade64.dll / ReShade32.dll**
- Fixed ReShade not being detected when installed using its own filename (ReShade64.dll or ReShade32.dll) rather than a proxy DLL name like dxgi.dll.

**RE Framework update check false positive with PD-Upscaler**
- Fixed all RE Framework games being flagged as needing an update when one game had the PD-Upscaler version installed.

**Manifest forceExternalOnly badge and label fixes**
- Fixed `forceExternalOnly` entries being skipped when the game was already marked as external-only from wiki matching. The manifest URL and label now always take priority.
- Fixed the Discord badge showing for games whose manifest entry points to Nexus Mods.

**Window position remembered in Compact View**
- Fixed the app not restoring its window position on startup when Compact View was the last-used mode.

**Manifest JSON parse error**
- Removed stray backtick characters in the blacklist that caused the manifest to fail parsing from the GitHub API.

## v1.8.0

### New Features

**OptiScaler integration**
- OptiScaler is now a fully managed component in RHI. One-click install, update, and uninstall for upscaler redirection (DLSS/FSR/XeSS) on 64-bit games.
- New OptiScaler Settings section on the Settings page — configure GPU type (NVIDIA/AMD/Intel), DLSS input replacement toggle (AMD/Intel only), and overlay hotkey. Settings are persisted and applied automatically on every install.
- First-time install warning prompts users to configure OptiScaler settings before proceeding.
- All OptiScaler files are deployed from the staging folder, including companion DLLs, INI files, and the `D3D12_Optiscaler` subfolder. Installer scripts, READMEs, and license files are excluded.
- Game-owned files are backed up to `.original` before overwriting and restored on uninstall.
- ReShade coexistence handled automatically — ReShade is renamed to `ReShade64.dll` when OptiScaler is installed, and restored to the correct filename on uninstall.
- Vulkan games automatically use `winmm.dll` as the OptiScaler DLL filename. User and manifest overrides still take priority.
- DLL naming override dropdown in the per-game overrides panel. Manifest support for per-game OptiScaler DLL name defaults.
- Per-game OptiScaler update exclusion toggle in the overrides panel.
- OptiScaler status included in Update All, Refresh, game report, skeleton loading screen, and card flyout.
- Binary signature detection for existing OptiScaler installations. Foreign DLL protection recognises OptiScaler DLLs.
- 32-bit games show the OptiScaler row greyed out with strikethrough.
- OptiScaler Compatibility List link on the Settings page, linking to the community-maintained wiki.

**OptiPatcher integration**
- OptiPatcher ASI plugin is automatically downloaded and deployed to the `plugins` folder for AMD/Intel GPU users during OptiScaler install. Enables DLSS/DLSSG inputs without GPU spoofing in supported games.
- Version-tracked and cleaned up automatically on OptiScaler uninstall.

**DLSS auto-download (Super Resolution, Ray Reconstruction, Frame Generation)**
- The latest NVIDIA DLSS Super Resolution (`nvngx_dlss.dll`), Ray Reconstruction (`nvngx_dlssd.dll`), and Frame Generation (`nvngx_dlssg.dll`) DLLs are automatically downloaded and staged on startup. Sourced from the DLSS Swapper manifest.
- Every OptiScaler install deploys all three DLLs directly to the game folder, enabling DLSS upscaling, Ray Reconstruction, and DLSS-FG even for games that don't ship with them. Game originals are backed up and restored on uninstall.
- Each DLL has independent version tracking and auto-updates on each app launch.

**ReShade dependency enforcement**
- RenoDX, ReLimiter, and Display Commander install buttons now require ReShade to be installed first. When ReShade is not installed, buttons show "⚠ ReShade required" and the rows are dimmed.

**Mass INI Deployment**
- New section on the Settings page to deploy reshade.ini, relimiter.ini, DisplayCommander.ini, or OptiScaler.ini to all games that have the corresponding component installed with a single button click. Custom hotkey and screenshot path settings are preserved.

**Mass ReShade Preset Install**
- New section on the Settings page. Select presets from your presets folder, choose which games to deploy them to via a checkbox game picker with Select All / Deselect All, and optionally install the required shader packs — all in one flow.

### Changes

- Pre-generated OptiScaler INI templates bundled for each GPU configuration (NVIDIA, AMD/Intel with DLSS, AMD/Intel without DLSS). FPS overlay, FPS cycle, and Frame Generation hotkeys are unbound by default to prevent keybind conflicts.
- `LoadReshade=true` and `LoadAsiPlugins=true` are enforced in all OptiScaler INI deployments.
- OptiScaler overlay hotkey written as Windows Virtual Key Code hex values matching OptiScaler's expected format.
- Global update inclusion toggles in the overrides panel replaced with a compact "Update Inclusion" button and a colour-coded summary line.
- Bitness and Graphics API dropdowns in the overrides panel are now side by side instead of stacked vertically.
- Frame limiter separator text updated to "Frame limiters — Choose one".
- Manifest `wikiUnlinks` now fully disconnects games from the wiki — no mod match, no generic UE/Unity fallback, no Discord badge.
- Single-player warning text updated: "ReShade with addon support and OptiScaler may trigger anti-cheat."
- Skeleton loading screen updated to reflect the current detail panel layout.

### Performance

- Startup time reduced by up to 60% through multiple optimisations:
  - PCGW cache writes debounced — ~45 concurrent file lock errors per startup eliminated.
  - OptiScaler detection now scans only the 7 known proxy DLL names instead of every DLL in the game folder.
  - WindowsApps game paths skipped for OptiScaler detection, ReShade proxy scanning, and addon file scanning.
  - DLC content packs (DOOM, Yakuza, Indiana Jones, MWII, Battle.net launcher components) blacklisted from game detection.
- Fixed NNShaders shader pack failing to download every startup (GitHub URL corrected from `main` to `master` branch).

### Bug Fixes

- Fixed "Unknown dxgi.dll Detected" warning appearing when installing ReShade after OptiScaler.
- Fixed ReShade not being deleted when uninstalling after OptiScaler was removed.
- Fixed ReShade being renamed to `opengl32.dll` instead of `dxgi.dll` on OptiScaler uninstall for games with both DX12 and OpenGL detected.

---

## v1.7.8

### Changes

**Deploy button disabled when no presets selected**
- The Deploy button in the preset picker is now greyed out until at least one preset is ticked.

**DLSS Fix addon filename preserved on deploy**
- The DLSS Fix addon now deploys to game folders using its original filename (`renodx-dlssfix.addon64`) instead of being renamed, which is required for it to work correctly.

**Terraria install note updated**
- The game note for Terraria now mentions that admin privileges are required for ReShade installation due to GAC symlink creation.

**Manifest overrides for Nexus and PCGW**
- Added manual Nexus Mods URL overrides for games where automatic name matching fails (Deadly Premonition 2, Dying Light: The Beast, Echoes of the End, Horizon Forbidden West, Morrowind, The Sinking City Remastered, Tunguska: The Visitation, X4: Timelines).
- Added PCGW URL override for The Sinking City Remastered.

## v1.7.7

### New Features

**Nexus Mods and PCGamingWiki links**
- Each detected game now shows Nexus Mods and PCGamingWiki buttons in the detail panel. Links are resolved automatically — Nexus Mods via the public game catalogue, PCGW via Steam AppID lookup or wiki search. Games that can't be matched automatically can be overridden in the manifest.

**DLSS Fix addon**
- New managed addon that makes ReShade draw on native game frames instead of frame gen frames, and hides DLSS upscaling from ReShade. Available in the ReShade Addons dialog with automatic update checking.

### Changes

**Detail panel layout refresh**
- Game name and mod author are now displayed above the info card rather than inside it, giving the header more breathing room.
- Info badges (addon file, engine, source, API, status) are grouped in a bordered card with the Nexus and PCGW links on the left, and Browse, Info, and Discussion buttons on the right.
- Hide and Favourite buttons sit on the top row alongside the Nexus/PCGW links. Favourite now shows as a text button that highlights yellow when active, matching the style of the other action buttons.
- Folder management buttons (Change install folder, Reset folder, Reset Overrides, Copy Report) have been moved out of the overrides section into their own dedicated section underneath.
- The ReShade preset selector and Normal ReShade toggle are now side by side with a divider, instead of stacked vertically.
- The Select ReShade Preset button now uses blue accent styling to match Select Shaders.

### Bug Fixes

**Shader toggle not updating after preset install**
- Installing a preset with shaders via drag-and-drop or the preset dialog now immediately updates the Global Shaders toggle in the overrides panel, instead of requiring a manual refresh.

**Preset folder link not clickable when empty**
- The folder path in the "No preset files found" dialog is now a clickable link matching the style used when presets are present.

## v1.7.6

### Highlights

**ReShade preset drag-and-drop with automatic shader install** — Drag a preset `.ini` onto RHI and it'll validate it, deploy it to a game, and offer to automatically install the required shader packs. No more hunting for which packs a preset needs. We audited 30 popular presets across Elden Ring, Skyrim, Cyberpunk, GTA V, FFXIV, and more — RHI's 41 shader packs cover every freely-available shader out there.

**ReShade Without Addon Support** — New per-game toggle to switch from addon-enabled ReShade to standard ReShade. All addons are cleanly removed and the rows dim out. Toggle back to restore everything.

### New Features

**ReShade overlay hotkey configuration**
- New "ReShade UI Hotkey" section on the Settings page lets you capture a key combination (with Ctrl/Shift/Alt modifiers) and apply it to all managed reshade.ini files. The hotkey persists across restarts and is automatically written to newly deployed INI files. When set to the default Home key, RDR2 is skipped to preserve its template END key.

**ReLimiter OSD hotkey configuration**
- New "ReLimiter OSD Hotkey" section on the Settings page lets you set the key combination to toggle the ReLimiter overlay in-game. Uses ReLimiter's native format (e.g. Ctrl+F12, Alt+P). Applies to all relimiter.ini files in game folders and the AppData template so new installs inherit the setting.

**ReShade Without Addon Support (per-game toggle)**
- New toggle in the game overrides panel lets you switch individual games from addon-enabled ReShade to standard ReShade (without addon support). When enabled, all addons (RenoDX, ReLimiter, Display Commander, managed addon packs) are removed from the game folder, addon rows are dimmed and disabled, and the addon override toggle is locked off. Toggling back restores addon ReShade and re-deploys addons. The setting persists per-game across app restarts.

**Automatic INI deploy on first install**
- Installing ReLimiter or Display Commander to a game for the first time now automatically copies your pre-configured `relimiter.ini` or `DisplayCommander.ini` from the AppData INI folder to the game directory. If the INI already exists in the game folder it's left untouched, so per-game customisations are never overwritten. If the source INI doesn't exist or the copy fails, the install continues normally — no error, no interruption.

**ReShade preset drag-and-drop with automatic shader install**
- You can now drag and drop a ReShade preset `.ini` file onto the RHI window to install it. RHI validates the file as a genuine ReShade preset, saves it to the presets folder, lets you pick a target game, and copies it to the game directory. After deploying, RHI offers to automatically resolve and install the shader packs required by the preset — parsing the `Techniques=` line, matching `.fx` files against known shader packs, switching the game to per-game shader mode, and deploying the matched packs. The same shader install prompt also appears when deploying presets from the existing preset selection dialog. A new Glamarye Fast Effects pack was added after auditing 30 popular presets — RHI's 41 shader packs now cover every freely-distributable shader used by real-world presets.

**Mutual-exclusion dimming for ReLimiter / Display Commander**
- When ReLimiter is installed, the Display Commander row is now visually dimmed (and vice versa), making the mutual exclusivity between the two much clearer at a glance.

### Changes

**Settings page layout redesign**
- Settings sections are now grouped into two-column layouts with vertical dividers: Add Game / Full Refresh, Screenshots / ReShade UI Hotkey, and Custom Shaders / Addon Watch Folder. Reduces scrolling and makes the page more compact.

**Preset folder path is now a clickable link**
- The presets folder path in the "Select ReShade Presets" dialog is now a hyperlink that opens the folder in Explorer when clicked.

### Bug Fixes

**Last selected game not restored on launch**
- The "remember last selected game" feature was broken — the saved selection was being overwritten by auto-select during init, and the library wasn't being saved on app close. Both issues are now fixed.

**DC, ReLimiter, and RE Framework update status lost on refresh**
- Display Commander, ReLimiter, and RE Framework update indicators weren't surviving a normal refresh — only RenoDX and ReShade statuses were being preserved when cards were rebuilt. A Full Refresh was needed to re-detect updates. All five components now carry their update status forward correctly.

**Shader and preset picker dialogs unreadable in dark mode**
- The root content grid was missing `RequestedTheme="Dark"`, so on PCs where Windows uses a non-dark theme, all WinUI controls (text boxes, combo boxes, toggles, checkboxes) inherited the system theme — dark text on dark backgrounds, light-colored input fields. Fixed by setting the dark theme on the root element so every control in the app inherits it. This also fixes the shader picker, preset picker, and all other dialogs.

**32-bit-only RenoDX mods showing "No mod available"**
- Games with only a 32-bit addon on the wiki (like Terraria, Sonic Generations, Tomb Raider 2013) were showing "No RenoDX mod available" because the wiki parser stored the `.addon32` URL separately and the UI only checked the 64-bit URL. The 32-bit URL is now promoted to the primary download when no 64-bit variant exists.

---

## v1.7.5

### Changes

**Downloads folder reorganised into subdirectories**
- The `%LocalAppData%\RHI\downloads\` folder is now organised into categorised subdirectories: `shaders/`, `renodx/`, `framelimiter/`, `luma/`, and `misc/`. Existing cached files are automatically migrated on first launch — no re-downloads needed. The migration is safe to interrupt and handles locked or duplicate files gracefully.

**Drag-and-drop game auto-selects**
- Dropping an .exe to add a game now automatically selects and scrolls to the new card, so you can interact with it immediately.

**Remember last selected game**
- RHI now remembers which game was selected when you close the app and restores that selection on next launch. If the game is no longer available, it falls back to the first game in the list.

**Copy Report now pastes as a file**
- The Copy Report button now saves a readable `.md` file and places it on the clipboard as a file attachment. Paste directly into Discord — no more wall of base64 text. Reports are also saved to `%LocalAppData%\RHI\reports\` for reference.

### Bug Fixes

**Display Commander update detection fixed**
- DC uses a fixed `latest_build` GitHub tag that never changes, so version comparison was always returning "no update". RHI now extracts the real version number (e.g. `0.13.153.3324`) from the release body text, enabling proper update detection.

**DC renamed to dxgi.dll no longer misdetected as ReShade**
- When Display Commander was renamed to `dxgi.dll` via the DLL naming override, the game scan incorrectly identified it as ReShade. The detection logic now checks DC's install record and skips filenames already claimed by DC. This also fixes ReShade not deploying when DC occupies the target filename slot.

**DLL override rename failure on existing files**
- Enabling a DLL naming override could fail with "Cannot create a file when that file already exists" if the target filename was already occupied. The rename now uses a fallback copy-delete-move pattern when the direct delete fails.

---

## v1.7.4

### New Features

**Copy Report button**
- New "Copy Report" button in the overrides panel. Copies a diagnostic code to your clipboard that you can paste in Discord or a GitHub issue. Includes game info, installed components, overrides, and an optional note. A confirmation prompt reminds you to correct overrides before submitting.

**ReShade preset selector**
- New "Select ReShade Preset" button in the overrides panel. Place `.ini` preset files in the `reshade-presets` folder and deploy them to any game with one click.

**Addon lifecycle tied to ReShade**
- Installing ReShade now automatically deploys your selected addons (global or per-game). Uninstalling ReShade removes managed addons from the game folder. Refresh syncs addons to all games with ReShade installed.

### Bug Fixes

**Uninstall buttons appearing grey**
- The ✕ remove buttons on component rows could appear grey instead of red until you hovered over them. Now always red when visible.

**Update All tooltip missing Display Commander**
- The Update All button tooltip now includes Display Commander in the list of components.

**Game report showing same values for detected and corrected**
- The Copy Report diagnostic now captures the raw auto-detected bitness and API before user overrides, so the detected vs corrected diff shows actual before/after values.

### Changes

**Overrides panel layout consolidated**
- The separate Manage section has been merged into the overrides panel. Change install folder, Reset folder, Reset Overrides, and Copy Report are now stacked in the left column with a vertical divider. The right column holds the new preset selector. Overall spacing has been tightened to reduce vertical height.

---

## v1.7.3

### Changes

**ReLimiter v3.0.0 — new repository**
- ReLimiter is now sourced from [github.com/RankFTW/ReLimiter](https://github.com/RankFTW/ReLimiter/releases). Downloads, updates, and feature guide links all point to the new repo.

**ReLimiter 64-bit only (for now)**
- ReLimiter v3.0.0 is 64-bit only. 32-bit games show the ReLimiter row with strikethrough styling and a disabled install button, matching the mutual exclusion pattern used for Display Commander. The 32-bit code paths remain in place and will activate automatically when a 32-bit release is published.

---

## v1.7.2

### Bug Fixes

**Addon deployment deleting RenoDX mods, ReLimiter, and Display Commander**
- The addon manager's stale file cleanup was removing any `.addon64`/`.addon32` file in game folders that wasn't in the enabled addon set — including RenoDX mods, ReLimiter, and Display Commander. Stale removal now only targets files that match a known addon from the official ReShade Addons.ini list. User-placed files and other managed components are never touched.

---

## v1.7.1

### New Features

**ReShade Addon Manager**
- New "ReShade Addons" button in the main header opens a curated addon manager. Browse all available ReShade addons from the official list, toggle them on/off with a single switch. Toggling on downloads the addon automatically, toggling off disables deployment but keeps files cached for later use.

**Global addon deployment**
- Enabled addons are automatically deployed to every game with ReShade installed. When ReShade is installed on a new game, enabled addons are deployed there too. Addons are auto-updated on startup.

**Per-game addon overrides**
- Each game's override panel now has an Addons section with a Global toggle. Switch to per-game mode to pick exactly which addons to deploy for that game, independent of the global set.

**RenoDX DevKit addon**
- The RenoDX DevKit addon is always available in the addon manager alongside the official ReShade addons list.

**First-time addon warning**
- A one-time warning dialog explains that addons are advanced features before opening the addon manager for the first time.

**Override panel layout change**
- Bitness/API moved to the middle row, Shaders and Addons now share the bottom row for a cleaner layout.

**QD-OLED APL Fixer shader pack**
- Added the QD-OLED APL Fixer by mspeedo as a managed shader pack. This shader compensates for the aggressive ABL dimming on QD-OLED screens by applying a measured HDR brightness boost based on real APL behavior.

### Bug Fixes

**Graphics API override not applying on Game Pass and some Steam games**
- The WindowsApps early-return in API detection was running before user overrides and manifest overrides, causing all Game Pass games to show Unknown API regardless of manifest data or user selections. User overrides and manifest overrides now take priority over the WindowsApps filesystem skip. The WindowsApps early-return now falls back to engine-type inference instead of returning Unknown.

**API and bitness override not refreshing detail panel**
- Changing the Graphics API or Bitness override in the overrides panel updated the card properties but didn't rebuild the detail panel. The ReShade install button stayed in DX mode even after switching to Vulkan. Both overrides now trigger a full detail panel rebuild so install buttons reflect the new state immediately.

**Manifest wiki name overrides visible in overrides panel**
- Wiki name mappings injected by the remote manifest (e.g., "Red Dead Redemption 2 (Vulkan)") were showing in the Wiki mod name text box, making it look like the user had set them. The box now only shows user-set mappings. Manifest mappings are hidden from the UI but still work internally for mod matching.

**Reset removing manifest wiki name mappings**
- Clicking Reset Overrides was deleting manifest-injected wiki name mappings from the settings file, breaking mod matching for games like Red Dead Redemption 2 until the next restart. Manifest-origin mappings are now protected from removal and excluded from settings persistence.

**Vulkan ReShade status text styled as link**
- The ReShade status text for Vulkan games showed "Not Installed" with an underline and hand cursor, making it look clickable. Fixed to show "Ready" in plain text matching the style of other components.

### Changes

**Strikethrough on mutually exclusive limiters**
- When ReLimiter is installed, the Display Commander label, status, and install button text are shown with strikethrough styling, and vice versa. This makes it visually clear that only one frame rate limiter can be active per game.

**Removed legacy startup dialogs**
- The one-time Display Commander removal warning and legacy Program Files cleanup dialogs have been removed. These are no longer needed.

**Shader pack dependencies**
- Shader packs can now declare dependencies. Selecting Azen in the shader picker automatically selects smolbbsoop shaders (required by Azen). The dependency is one-way — deselecting smolbbsoop independently is still allowed.

**Screenshot path applies to all ReShade INI variants**
- The "Apply to all games" screenshot path button now also writes to reshade2.ini, reshade3.ini, and any other reshade*.ini files in game folders, not just reshade.ini.

---

## v1.7.0

### New Features

**Display Commander reintegrated**
- Display Commander (DC) is back as a frame rate limiter option alongside ReLimiter. Install, reinstall, update, and uninstall DC with one click from the detail panel. DC uses the LITE variant downloaded from GitHub and supports both 32-bit and 64-bit games.

**Mutual exclusion between ReLimiter and DC**
- Only one frame rate limiter can be installed per game at a time. When one is installed, the other's install button is greyed out. Removing one re-enables the other.

**DC update detection and Update All**
- DC is checked for updates on startup alongside other components. When an update is available, the sidebar badge and purple update styling appear. Update All now includes DC for eligible games.

**Automatic archive install from downloads folder**
- The watch folder now detects .zip, .7z, and .rar archives containing "renodx" in the filename. When a matching archive appears (e.g. from a Nexus Mods download), RHI automatically extracts it, finds the addon files inside, and starts the install flow — no drag-and-drop needed.

**DC DLL naming override**
- A dedicated DC filename override toggle lets you rename the DC addon file for specific games (e.g. to winmm.dll or d3d9.dll). Works independently from the ReShade override. Each dropdown filters out the other component's current filename to prevent conflicts.

**DC global update exclusion**
- Per-game DC update exclusion toggle in the overrides panel lets you pin a specific DC version on certain games, excluding them from Update All.

**DC detection on game scan**
- RHI detects existing DC installations when scanning game folders, including files with custom DLL override names via tracking records.

**DC INI deploy button**
- Display Commander now has a 📋 button to copy DisplayCommander.ini to the game folder, matching the existing ReShade and ReLimiter INI deploy buttons. The bundled INI is seeded to the app data folder on first launch.

### Bug Fixes

**DLL override disable no longer deletes ReShade**
- Turning off the ReShade DLL naming override now renames the file back to dxgi.dll instead of deleting it.

**Loading overlay stuck after navigating to Settings**
- Going to Settings and back during initial loading no longer leaves the spinner and status text stuck on screen.

### Changes

**Component row order updated**
- The detail panel component order is now: ReShade → RenoDX → ReLimiter → DC. ReLimiter and DC are separated from the rows above by a labeled divider ("Choose one from below").

**Grid view card updated**
- The grid view card flyout now matches the detail panel: same component order (ReShade → RenoDX → separator → ReLimiter → DC), mutual exclusion greying, and "Choose one from below" separator. The overrides flyout now includes DC DLL override, DC update exclusion, bitness override, and API override — matching the detail panel feature-for-feature.

**Hand cursor on clickable links**
- Version numbers and author donation links in the detail panel now show a hand cursor on hover when they're clickable.

**DLL naming overrides section updated**
- The section header is now "DLL naming overrides" (plural). The ReShade and DC override toggles are independent — each can be enabled without the other. Dropdowns show "Select ReShade DLL name" / "Select DC DLL name" as placeholder text.

**Global update inclusion grid**
- The update inclusion toggles (ReShade, RenoDX, ReLimiter, DC) now use a 2×2 grid layout instead of a horizontal row to prevent overflow.

**Faster startup**
- Shader pack checks now run in parallel instead of sequentially, cutting ~10 seconds from launch.
- Game folder shader syncs run in parallel.
- Graphics API detection results are cached to disk — subsequent launches skip PE header scanning entirely.
- Xbox/WindowsApps game paths are skipped during API detection (always access-denied, wasted time on retries).
- Full Refresh clears all caches and rescans everything fresh.

**Search box placeholder updated**
- The search box now says "Filter games..." instead of "Search games..." to better reflect its purpose.

**Custom filter auto-select**
- Saving a custom filter now clears the search box and automatically activates the new filter chip.

**Custom filter chips visually distinct**
- Custom filter chips now use a teal color scheme (matching the toolbar buttons) to distinguish them from the built-in filter chips.

**Update button renamed**
- The toolbar Update button now reads "Update All" to clarify its batch operation.

---

## v1.6.9

### New Features

**Skeleton loading screen**
- The app now shows a skeleton loading screen on launch instead of a centered spinner. The sidebar and detail panel areas display animated placeholder shapes that mimic the real layout, so the UI feels responsive from the moment you open it. The placeholders pulse with a subtle shimmer animation and are replaced by real content once loading finishes.

**Universal keyword search**
- The search bar now matches across all game card properties — not just game name and maintainer. You can search by store (Steam, GOG, Epic), engine (Unreal, Unity), graphics API (DX11, VLK, DirectX12), bitness (32-bit, 64-bit), mod name, mod author, Luma mod name/author, Vulkan rendering path, and RE Engine/RE Framework games.

**Custom filter chips**
- Save any search query as a named filter chip by clicking the "+" button next to the search bar. Custom chips act as real independent filters — click to activate, click again to toggle off, switch between them freely. They combine with the search box and built-in filter chips. Right-click a custom chip to delete it. Custom filters persist across sessions.

**About page**
- All informational content (app description, credits & acknowledgements, disclaimers, and links) has been moved from the Settings page to a dedicated About page. Access it from the Help flyout → About. The Settings page now contains only actionable configuration sections.

### Bug Fixes

**Luma games showing false ReShade update indicator**
- Games in Luma mode no longer show a green update dot on the sidebar card. Luma uses its own bundled ReShade version, so the version mismatch with the latest ReShade is expected and no longer triggers update indicators.

**Luma games included in ReShade global updates**
- Games in Luma mode are now automatically excluded from Update All ReShade operations. This prevents Luma's bundled ReShade from being overwritten by the latest version.

### Changes

**Search now covers display API labels**
- Searching "DX11", "VLK", "OGL", or "DX9" now matches the short labels shown on game cards, in addition to the full enum names like "DirectX11". Dual-API games are also searchable by either API in their detected set.

**RE Engine games searchable**
- Typing "RE Engine" or "RE Framework" in the search bar now finds all RE Engine games.

**Graphics API override simplified to dropdown**
- The six individual API toggle switches have been replaced with a single dropdown selector (Auto, DirectX8, DirectX9, DirectX10, DX11/DX12, Vulkan, OpenGL). "Auto" uses the auto-detected value from PE header scanning.

**Graphics API and Bitness overrides consolidated**
- The Graphics API dropdown is now in the same section as the Bitness dropdown, stacked vertically in the left panel of the overrides row.

**Wiki exclusion moved to wiki section**
- The wiki exclusion toggle has been moved from the right column into the left column, directly below the wiki mod name field. The header text has been removed for a more compact layout.

**Overrides panel condensed**
- The bottom row of the overrides panel has been simplified from a two-column layout to a single stacked section for Bitness and Graphics API.

**Version number in status bar**
- The app version number is now displayed in the bottom-right corner of the status bar, next to the Patch Notes button.

---

## v1.6.8

### Bug Fixes

**DLL naming override dropdown empty**
- The DLL naming override dropdown in the detail panel overrides section was empty after the 1.6.7 layout refactor. The `ItemsSource` binding to the common DLL names list was accidentally dropped when the ComboBox was moved to the top row. The dropdown now shows all available filenames again.

### Changes

**Expanded DLL naming override list**
- Added `d3d10.dll`, `opengl32.dll`, and `ddraw.dll` to the DLL naming override dropdown. The list now covers all standard ReShade API names and common proxy DLLs, ordered by usage: API names first (`dxgi.dll`, `d3d9.dll`, `d3d10.dll`, `d3d11.dll`, `d3d12.dll`, `opengl32.dll`), then proxy names (`dinput8.dll`, `version.dll`, `winmm.dll`, `ddraw.dll`), then edge cases.

**Auto-update now checks /releases/latest first**
- The self-update check now queries the GitHub `/releases/latest` endpoint on the RHI repo as the primary source, supporting the new per-version release tags (e.g. "RHI 1.6.8"). Falls back to the fixed `RHI` tag and then the legacy RDXC repo if needed.

**64-bit badge on detail panel**
- 64-bit games now show a "64-bit" badge on the detail panel info line, matching the existing "32-bit" badge for 32-bit games. The badge updates live when the bitness override is changed.

**Graphics API override tooltip**
- The "Graphics API" label in the overrides panel now has a tooltip explaining the priority rule: only one API drives the ReShade DLL filename at a time (DX11/12 → Vulkan → OpenGL → DX10 → DX9 → DX8), and user overrides take precedence over manifest and auto-detected values.

**Screenshot folder Browse and Open buttons**
- The screenshot path setting now has an inline folder icon inside the text box to open a folder picker, and an "Open" button to launch the configured screenshot folder in Explorer.

---

## v1.6.7

### New Features

**Per-game bitness override**
- You can now manually override the auto-detected bitness (32-bit / 64-bit) for any game. A new Bitness dropdown in the overrides panel lets you choose Auto, 32-bit, or 64-bit. This addresses cases where PE header analysis misidentifies a game's architecture. The override persists across restarts and is cleared by Reset Overrides.

**Per-game graphics API override**
- You can now manually toggle which graphics APIs a game uses. A new Graphics API section in the overrides panel shows toggle switches for DirectX 8, DirectX 9, DirectX 10, DX11/DX12, Vulkan, and OpenGL. This is useful for correcting misdetected APIs or suppressing one API on a dual-API game. Overrides persist across restarts and are cleared by Reset Overrides.

### Bug Fixes

**Update dialog still said "RDXC"**
- The self-update notification dialog now correctly says "A new version of RHI is available" instead of the old RDXC branding.

**RenoDX DevKit addon deleted during mod install**
- Installing a RenoDX mod via drag-and-drop or toggling off Luma mode was deleting `renodx-devkit.addon64` and `renodx-devkit.addon32` from the game folder. DevKit files are now exempt from addon cleanup.

### Changes

**Overrides panel layout condensed to 3 rows**
- The overrides panel has been reorganized from 5 rows down to 3. The DLL naming override has been moved into the top row alongside wiki exclusion. The Rendering Path dropdown has been removed since the new API toggles make it redundant. Bitness and API overrides share a new row between shaders/global updates and the reset button.

**API toggles use horizontal card layout**
- The graphics API toggles are displayed in a horizontal wrapping layout with bordered cards, matching the style of the Global update inclusion toggles.

**DLL naming override text simplified**
- The toggle off-text now reads "Override ReShade filename" instead of "Using default filenames". The "ReShade filename" header above the dropdown has been removed to save vertical space.

---

## v1.6.6

### Bug Fixes

**RE Framework now downloads the correct game-specific build**
- Each RE Engine game now downloads its own RE Framework build (e.g. DMC5.zip for Devil May Cry 5, RE4.zip for Resident Evil 4) instead of using a single generic download for all games. Game names with trademark symbols (e.g. Street Fighter™ 6) are now matched correctly.

**Drag-and-drop no longer deletes third-party ReShade addons**
- Installing a RenoDX mod via drag-and-drop was deleting all non-ReLimiter `.addon64`/`.addon32` files from the game folder, including third-party addons like `ShaderToggler.addon64`. The cleanup now only removes `renodx-` prefixed files.

**Corrupt cached addon files no longer reused**
- The PE validation check now rejects files under 100 KB, preventing truncated or corrupt downloads (e.g. the 48 KB Unity generic mod) from being cached and reused. Users with a bad cache should delete `%LocalAppData%\RHI\downloads\` to force a fresh download.

**Add Game folder picker crash on some systems**
- The WinUI folder picker could throw a COMException on certain Windows configurations when adding a game manually. The picker now falls back to the native COM file dialog if the standard picker fails.

**Drag-and-drop version number now reads from correct path**
- Version numbers for mods installed via drag-and-drop were not displayed when the game uses a custom `AddonPath` in `reshade.ini`. The version is now read from the actual addon deploy folder.

**Discord/Nexus mod version numbers now displayed**
- External-only games (Discord/Nexus mods) were hardcoded to show "Installed" instead of the version number. They now show the version from PE info when available, matching wiki-installed mods.

**Version fallback for addons without PE version info**
- Addon files that lack embedded PE version resources (common with Discord-distributed mods) now display the file's last-modified date in `YY.MMDD.HHMM` format as a fallback, matching the RenoDX version style.

**ReLimiter version number not shown immediately after install**
- The ReLimiter version number was showing "Installed" instead of the version until a refresh. The install flow now falls back to reading the version from the metadata file when the remote version hasn't been fetched yet.

### Changes

**Component version numbers centered in detail panel**
- The version numbers for RE Framework, ReShade, ReLimiter, RenoDX, and Luma are now horizontally centered in the detail panel, aligning them on a common vertical axis.

**Consistent purple update styling across all components**
- ReLimiter and RE Framework install buttons and status text now turn purple when an update is available, matching the existing ReShade and RenoDX styling. Previously ReLimiter used amber text with blue buttons, and RE Framework used static blue buttons.

**ReLimiter available in Luma mode**
- ReLimiter can now be installed and managed when a game is in Luma mode. The ReLimiter row and status dot are always visible. When switching a game out of Luma mode, ReLimiter is automatically uninstalled alongside the Luma files.

**Status messages auto-fade after 4 seconds**
- Install, update, and removal confirmation messages now automatically disappear after 4 seconds. Error messages remain visible. Multiple messages across different components fade independently.

**Colored status messages**
- Install/update success messages now display in green with a ✅ icon. Removal messages display in red with a ✖ icon. Progress and default messages remain blue.

---

## v1.6.5

### New Features

**RE Framework support**
- RHI now detects RE Engine games (Monster Hunter Wilds, Resident Evil series, Devil May Cry 5, Street Fighter 6, Dragon's Dogma 2, Pragmata, etc.) by checking for `re_chunk_000.pak` in the game directory. Detected games display an "RE Engine" badge.
- One-click install, update, and uninstall of RE Framework (`dinput8.dll`) from praydog's GitHub nightly releases. Each game downloads its own game-specific build (e.g. DMC5.zip for Devil May Cry 5, RE4.zip for Resident Evil 4). The DLL is cached per game so reinstalls are instant.
- Version tracking and auto-update checking — RHI fetches the latest nightly release tag on startup and flags installed copies that are behind. RE Framework is included in the Update All batch operation.
- RE Framework status dot, install row, and progress indicator appear on game cards and the detail panel for RE Engine games, following the same layout as ReShade and ReLimiter. The version number is a clickable link to the REFramework nightly releases page.
- Install All on RE Engine games now chains: RenoDX → RE Framework → ReShade.

**Screenshot path settings**
- A new Screenshots section in Settings lets you set a global screenshot save path that is written to all managed `reshade.ini` files as `[SCREENSHOT] SavePath=<path>`.
- An optional per-game subfolder toggle appends the game name to the path, so each game's screenshots go to their own folder.
- Click Apply to update all existing `reshade.ini` files at once. Newly deployed INIs also include the configured path automatically.

**URL drag-and-drop install**
- You can now drag a Discord or browser link to an `.addon64`/`.addon32` file directly onto the RHI window to download and install it. RHI validates the URL, downloads the file to the local cache with a progress dialog, verifies it's a valid PE binary, and routes it through the standard game-matching and install flow.
- Also supports dragging `.url` shortcut files — RHI parses the URL from inside and processes it the same way.

### Bug Fixes

**RE Engine games not detected on existing installs**
- Games that were cached before the RE Engine detection was added are now re-scanned automatically, so the RE Framework row appears without needing a manual refresh.

**Update All button staying purple after all updates complete**
- The Update All button could remain purple after completing all updates because the "any updates available" check was counting hidden games, games with DLL overrides, games with missing install paths, and Vulkan games (whose ReShade uses the global layer). These cards are skipped by Update All but were still contributing to the button state. The check now uses the same eligibility criteria as the actual update operation.

**Cached addon files corrupted by HTML error pages**
- The download-based update check for GitHub Pages-hosted mods (generic Unity, UE-extended) could save an HTML error page as the cached addon file when the CDN returned a 200 OK with HTML content instead of the binary. This resulted in ~48KB files replacing multi-MB addons in the download cache. Downloads are now validated with a PE signature check before caching, and corrupted cache entries are automatically deleted so a fresh download is triggered.

**RE Framework status color mismatch**
- The RE Framework version text was using a different shade of green than all other components. The installing and update-available colors were also inconsistent. All RE Framework status colors now match ReShade, ReLimiter, and RenoDX.

### Changes

**RenoDX version number now shows full build info**
- The RenoDX version display now includes the hour/minute build number and drops the leading `0.` and century digits from the year. For example, `0.2026.0325.2215` is now shown as `26.0325.2215`.

**Vulkan ReShade button icons**
- The "Reinstall Vulkan ReShade", "Install Vulkan ReShade", and "Install Vulkan Layer" buttons now show the ↺ and ⬇ action icons matching all other component buttons.

**Toggle switch labels removed in grid view overrides**
- Removed the "Yes"/"No" text from the Global update inclusion toggle switches in the grid view overrides flyout to prevent text overflow.

---

## v1.6.4

### New Features

**32-bit ReLimiter support**
- RHI now installs the correct ReLimiter addon based on game bitness. 32-bit games receive `relimiter.addon32` and 64-bit games continue to use `relimiter.addon64`. Each variant is cached separately so installs don't interfere with each other.

**Automatic OpenGL ReShade DLL naming**
- Games detected with OpenGL as their only graphics API now have ReShade installed as `opengl32.dll` automatically, instead of the default `dxgi.dll`. User and manifest DLL overrides still take priority.

**Automatic DX9 ReShade DLL naming**
- Games detected with DirectX 9 now have ReShade installed as `d3d9.dll` automatically, instead of the default `dxgi.dll`. DX9 takes precedence when multiple APIs are detected. User and manifest DLL overrides still take priority.

---

## v1.6.3

### Bug Fixes

**Author badges now split on "and" separator**
- Games with multiple authors joined by "and" (e.g. "Jon and Forge") were displayed as a single badge with no donation link. Author strings are now split on both "&" and "and", so each author gets their own clickable badge linking to their Ko-fi page.

**Update All button no longer lights up for excluded games**
- The global Update All button was turning purple when updates were available for games excluded from Update All via overrides. The button now only reflects updates that would actually be acted on. Toggling an exclusion also immediately updates the button color.

**Legacy install folder cleanup**
- On first launch, RHI checks for old RenoDXCommander and UPST folders in Program Files from previous installs. If found, a dialog offers to remove them. Choosing "Keep" or "Remove" writes a marker so the prompt only appears once.

**Red Dead Redemption 2 dedicated ReShade INI**
- Red Dead Redemption 2 now uses a dedicated `reshade.rdr2.ini` configuration file instead of the generic `reshade.vulkan.ini`. When ReShade is installed for RDR2, this game-specific INI is deployed as `reshade.ini` in the game folder, ensuring optimal settings for RDR2's Vulkan renderer. The overlay key is set to END to avoid conflicts with RDR2's default keybindings.

---

## v1.6.2

### Highlights

**Rebranded to ReShade HDR Installer (RHI)**
- After much feedback and consideration, the app has been rebranded from Ultra Plus Support Tools (UPST) to ReShade HDR Installer (RHI). The executable, window title, settings directory, and all user-facing references now use the RHI name. Existing `%LocalAppData%\UPST` and `%LocalAppData%\RenoDXCommander` data folders are automatically migrated to `%LocalAppData%\RHI` on first launch — no manual action needed.

**Clickable author donation links**
- Mod author badges in the detail panel are now clickable links to the author's Ko-fi donation page. Supported authors: ShortFuse, Jon (oopydoopy), Forge, Voosh (NotVoosh), and Musa. Authors without a known donation page remain as regular non-clickable badges. If your donation link is missing, reach out on Discord and it will be added.

**Ultra Limiter rebranded to ReLimiter**
- All references to "Ultra Limiter" throughout the app have been renamed to "ReLimiter". The addon file is now `relimiter.addon64`. Existing `ultra_limiter.addon64` files in game folders are automatically replaced on update.

### Bug Fixes

**Grid view component row alignment**
- Fixed the "ReLimiter" name and "Installed" / "Update" status text overlapping in the grid view install popout. The name column width has been increased so all three component rows (ReShade, ReLimiter, RenoDX) align consistently.

**Drag-and-drop no longer deletes ReLimiter**
- Installing a RenoDX addon via drag-and-drop or double-click no longer removes `relimiter.addon64` from the game folder. The addon cleanup now excludes ReLimiter files alongside Display Commander files.

### Changes

**Component status text now clickable**
- The "Installed" status text for ReShade now links to [reshade.me](https://reshade.me). The "Installed" status text for RenoDX now links to the game's wiki page (or the mods list if no per-game page exists). ReLimiter's existing link to the feature guide is unchanged. Applies to both detail view and grid view popout.

**Installed filter now shows ReShade games**
- The Installed filter tab now shows games with ReShade installed, rather than games with RenoDX or Luma installed. This better reflects the typical workflow where ReShade is the base component that most users have deployed.

**Auto-update now checks RHI repo**
- The self-update check now looks for `RHI-Setup.exe` at the new `RankFTW/RHI` GitHub repo first, falling back to the legacy `RankFTW/RenoDXChecker` repo with `RDXC-Setup.exe` if the new endpoint has no release.

**ReLimiter global update exclusion toggle**
- A new "ReLimiter" toggle has been added to the per-game Global update inclusion section in both the detail view overrides panel and the grid view overrides flyout, alongside the existing ReShade and RenoDX toggles. When toggled off, the game is excluded from Update All for ReLimiter.
- The update badge (green dot) in the sidebar now respects all three exclusion flags — if a component's update is excluded, it no longer contributes to the badge.
- Reset Overrides now also resets the ReLimiter exclusion toggle back to included.

---

## v1.6.1

### Bug Fixes

**ReLimiter update detection fixed**
- Fixed UL updates not being detected when a new version was published on GitHub. The update check was comparing the remote file against a metadata file that had already been overwritten with the new version's hash during a previous check, so it always reported "no update." The check now hashes the actual installed file from a game folder as the ground truth reference, ensuring updates are always detected regardless of metadata state.
- Fixed a file locking bug where the SHA-256 stream was not disposed before the temp file cleanup, causing "file in use" errors in the session log.
- Added a GitHub Releases API pre-check that detects size-only changes instantly without downloading the full file. When sizes match, the full download + hash comparison still runs to catch same-size content changes.
- Cache-busting headers (`Cache-Control: no-cache`) are now sent on both the API and download requests to bypass GitHub CDN caching.

**ReLimiter update badge in sidebar**
- Games with a pending UL update now show the green update dot in the sidebar game list, matching the existing behavior for RenoDX and ReShade updates.

**Update All button includes ReLimiter**
- The global Update button now also updates ReLimiter for all games with a pending UL update. The tooltip has been updated to reflect this.
- The Update All button now turns purple when UL updates are available, not just for RenoDX and ReShade updates.

### Changes

**ReLimiter update visuals**
- The UL status dot stays green when an update is available (previously turned orange), keeping it consistent with the "still installed" state.
- The UL status text now shows "Update" instead of "Update Available" for a cleaner look.
- The UL update no longer overwrites the install metadata during the check — metadata is only updated when the user actually installs the update, so the update badge persists across app restarts until acted upon.
- When the update check pre-caches the new file, clicking Update uses the cached file directly instead of re-downloading.

---

## v1.6.0

### Highlights

**Rebranded to UPST**
- The app has been rebranded from RenoDX Commander (RDXC) to Ultra Plus Support Tools (UPST). The executable, window title, settings directory, and all user-facing references now use the UPST name.

**ReLimiter support**
- UPST can now install and manage the ReLimiter addon (`relimiter.addon64`). A new UL component row appears in the install flyout and detail panel alongside RenoDX, ReShade, and Luma, with install, reinstall, and uninstall buttons, status dot, and progress indicator.
- ReLimiter is automatically detected in game folders on refresh.
- The UL row is hidden when a game is in Luma mode.
- ReLimiter is downloaded from GitHub on demand rather than bundled with the app, keeping the install size smaller.
- Update detection compares file size and SHA-256 hash against the remote release. When an update is available, the status dot turns orange and the button shows "Update".
- For a full list of ReLimiter features and settings, see the [ReLimiter Feature Guide](https://github.com/RankFTW/ReLimiter?tab=readme-ov-file#relimiter--comprehensive-feature-guide).
- A bundled `relimiter.ini` is seeded to the UPST inis folder on first launch. A 📋 button on the ReLimiter row copies it to the game folder, matching the existing ReShade INI workflow.

**ReLimiter "Installed" link**
- The green "Installed" text for ReLimiter is now a clickable link that opens the ReLimiter feature guide on GitHub.

**Display Commander removed**
- All Display Commander functionality has been removed from the codebase. DC install/uninstall, DC Mode toggle, DC DLL picker, DC per-game overrides, DC shader deployment, DC update operations, DC status indicators, and all DC-related UI elements have been stripped.
- ReShade is now always installed as the standard filename (`dxgi.dll` or the DLL override name) — the DC-mode filenames (`ReShade64.dll` / `ReShade32.dll`) are no longer used.
- The DC Legacy Mode toggle in Settings has been removed.
- A one-time warning dialog appears on first launch of v1.6.0 advising users to manually remove any old Display Commander files from game folders via the Browse button.

### Other Changes

**Lilium shader pack now optional in global selection**
- The Lilium HDR shader pack is no longer locked in the global shader picker. You can now untick it if you don't want it deployed globally. Lilium is still selected by default on fresh installs.

**Overrides layout redesign**
- The Global Shaders toggle and DLL naming override are now displayed side-by-side on the same row with a vertical divider, in both the detail view overrides panel and the grid view overrides flyout.
- The Global update inclusion and Wiki exclusion row now uses equal-width columns so the vertical divider sits centered.

**Version number now from assembly**
- The version displayed in the Settings menu is now read from the assembly version rather than a hardcoded string, ensuring it always matches the build.

**Drag-and-drop game selection fallback**
- When dragging an addon or archive onto UPST and the filename doesn't match any game, the game picker now defaults to the currently selected game in the sidebar instead of showing an empty selection.

**False update detection fix**
- Fixed mods hosted on GitHub Pages falsely showing "Update Available" when the remote file hadn't changed. The update check now compares the remote hash against the stored install-time hash instead of re-hashing the local file, which could differ if the file was touched by the game or ReShade.

**Obsolete specs cleaned up**
- The `dc-legacy-toggle` and `dc-mode-redesign` spec directories have been removed.

---

## v1.5.5

### New Features

**DC Legacy Mode toggle**
- Display Commander is no longer available for new downloads. A new "DC Legacy Mode" toggle in Settings lets existing DC users restore full DC functionality. When off (the default), all DC-related UI, install operations, and update operations are hidden throughout the app. Existing DC installations are preserved — toggling off does not uninstall anything.

**Lilium shaders always included in global selection**
- The Lilium HDR shader pack is now always selected and locked (greyed out) in the global shader picker. It can still be deselected in per-game shader overrides. New installs and existing installs that were missing Lilium will have it added automatically.

### Changes

**DC UI hidden by default**
- The DC component row, DC status dot, DC install/uninstall buttons, DC progress indicators, DC Mode settings, DC per-game overrides, DC DLL override filename, DC references in the About section, and DC references in the Update button tooltip are all hidden when DC Legacy Mode is off.

**Refresh on DC Legacy Mode toggle**
- Toggling DC Legacy Mode now triggers a full UI refresh so all cards and panels update immediately.

---

## v1.5.4

### New Features

**Filter mode remembered across sessions**
- Your selected filter tab (e.g. Unity, Installed, Favourites) is now saved and automatically restored when you reopen the app, so you no longer have to reselect it every launch.

**Install button icons**
- The card install button and external download/redownload buttons now show action icons (⬇ install, ↺ reinstall/manage, ⬆ update) matching the per-component buttons in the detail panel.

**DC Mode redesigned — toggle + DLL picker**
- DC Mode has been redesigned from a 3-state integer cycle (Off / dxgi.dll / winmm.dll) to a simple On/Off toggle with a DLL filename picker. You can now select any proxy DLL name from a dropdown or type a custom filename.
- Per-game DC Mode overrides have been simplified to three options: Global, Off, and Custom. Custom lets you pick a per-game DLL filename independently of the global setting.
- Legacy settings from previous versions are automatically migrated on first launch.

**Toolbar redesign**
- All toolbar buttons now share a consistent teal accent style. Buttons are grouped into three sections separated by vertical dividers: Refresh and Global Shaders | Update | Help, View toggle, and Settings.
- The Update button is now dim by default and lights up purple when any game has an update available.

**Addon auto-detection**
- UPST now watches your Downloads folder (configurable in Settings) for new `renodx-*.addon64` / `.addon32` files and automatically prompts you to install them.
- Double-clicking an addon file in Explorer opens UPST and triggers the install flow. If UPST is already running, the file is forwarded to the existing instance via named pipe.
- Drag-and-drop, file association, and archive extraction all enforce the `renodx-` filename prefix to avoid triggering on unrelated addon files.

**AddonPath support**
- Addon installs (RenoDX and Display Commander) now respect the `AddonPath` setting in `reshade.ini`. If the `[ADDON]` section contains an `AddonPath=` line, addons are deployed to that folder instead of the game root. Relative paths are resolved against the game directory. Uninstall, update detection, and addon scanning all check the same path.

**Ko-fi link in Help menu**
- The Help flyout now includes a Ko-fi link.

### Bug Fixes

**Grid view install flyout not working**
- Fixed the install/manage flyout on grid view cards being empty when clicked. The flyout now correctly shows all component rows (RS, DC, RDX, Luma) with install, reinstall, and uninstall buttons.

**Grid view overrides flyout missing DC Custom DLL selector**
- The per-game overrides flyout in grid view was missing the DC Custom DLL filename picker, the vertical divider between DC Mode and Shaders, and the three-column layout. These now match the detail view's overrides panel.

**Grid view install flyout crash on external-only games**
- Fixed a crash (`FormatException: Could not find any recognizable digits`) when opening the install flyout on games without a RenoDX wiki mod. The color parser now handles the `"transparent"` keyword.

**ReShade version not shown in DC mode**
- The ReShade status label now shows the installed version number even when DC mode is active, matching the behavior outside DC mode.

**DC DLL picker not waiting for Enter before renaming**
- Fixed the editable DC DLL filename picker triggering file renames on every keystroke while typing a custom name. The picker now only commits the change when you press Enter, select from the dropdown, or leave the field. Applies to both the global and per-game pickers.

**Update detection for clshortfuse.github.io mods**
- Fixed mod update detection for all mods hosted on GitHub Pages (`*.github.io`), including the Generic Unity and Generic Unreal addons. These URLs were falling through to the HEAD Content-Length comparison path, which is unreliable on GitHub Pages because the CDN may return compressed transfer sizes. They are now routed through the download-based comparison path, matching the existing behavior for `marat569.github.io` mods.

**Same-size mod updates not detected**
- Fixed the download-based update check failing to detect updates when the new version of a mod has the same file size as the installed version. The check now compares SHA-256 hashes when file sizes match, so content changes are always detected regardless of size.

**Orphaned temp files in download cache**
- Fixed update check temp files (`.update_check_*.tmp`) accumulating in the download cache folder after crashes or interrupted checks. Stale temp files are now cleaned up automatically at the start of each update check.

---

## v1.5.2

### Bug Fixes

**Update All removing shaders from game folders**
- Fixed the Update All ReShade and Update All DC operations removing managed shaders from every game folder they touched. The batch update was not passing the per-game shader selection to the install methods, causing the shader sync to interpret a null selection as "remove all shaders". Shaders are now preserved correctly during batch updates using each game's global or per-game shader selection.

---

## v1.5.1

### New Features

**Vulkan ReShade lightweight install**
- When the global Vulkan implicit layer is already registered, clicking the ReShade install button on a Vulkan game now performs a fast lightweight deploy (INI + footprint + shaders only) without requiring administrator privileges or reinstalling the layer.
- The install flyout and detail panel show context-aware labels: "Vulkan RS" / "Install Vulkan ReShade" when the layer is present, "Reinstall" / "Reinstall Vulkan ReShade" when already active, and "Install" / "Install Vulkan Layer" when the layer is absent.

**Installed indicator for Nexus/Discord mods**
- External mods downloaded from Nexus Mods or Discord now show a green "Installed" status label next to the Redownload button in the detail panel when the mod is installed.

### Improvements

**Faster startup**
- The app now displays game cards significantly faster by deferring ReShade staging and shader deployment to the background. Previously, the ReShade download/staging task and shader pack sync blocked the UI until they completed. Cards now appear as soon as game detection and mod matching finish, while ReShade staging and shader deployment continue silently in the background.

**"No RenoDX mod available" moved to install button**
- Removed the large purple "No RenoDX mod available" box from game cards. The install button now displays "No RenoDX mod available for this game" inline when no mod exists, keeping the card layout cleaner. If a mod is manually installed, normal install/reinstall labels are shown instead.

### Bug Fixes

**Vulkan ReShade blocked by DC Mode when DC Mode is off**
- Fixed Vulkan games incorrectly showing "ReShade cannot be installed while DC Mode is active" when the global DC Mode was on but the game had no per-game override. The install handler now applies the same Vulkan DC Mode exemption used everywhere else in the app.

**Update All placing dxgi.dll in Vulkan game folders**
- Fixed the Update All ReShade batch operation incorrectly running the standard DX ReShade install path for Vulkan games, which copied a `dxgi.dll` into the game directory. Vulkan games are now excluded from Update All ReShade since they use the global implicit layer and don't have per-game DLLs.

**ReShade "unable to save configuration" error**
- Fixed ReShade showing "Unable to save configuration and/or current preset" errors for `reshade.ini` and `ReShadePreset.ini` in games where these files were deployed by UPST. The INI writer was emitting a UTF-8 BOM (byte order mark) that ReShade's native parser cannot handle. INI files are now written as plain UTF-8 without BOM.

---

## v1.5.0

### Bug Fixes

**Global shader selection not persisting across restarts**
- The global shader deploy mode was not being saved to the settings file, causing the shader selection to reset to empty every time the app was opened. The `SaveSettingsToDict` method now correctly writes the `ShaderDeployMode` key.

**Per-game shader mode overrides resetting on load**
- Per-game shader mode overrides (Off, Minimum, All, User) were being filtered out during settings load, causing them to reset. Only "Select" mode was being preserved. All valid shader modes are now loaded correctly.

**Shader selection not saved after choosing packs**
- Clicking Deploy in the global shader selection picker did not persist the selection to disk. Settings are now saved immediately after the picker closes.

---

## v1.4.9

### New Features

**Graphics API detection**
- UPST now scans game executables using PE header import table analysis to detect which graphics APIs a game uses: DirectX 11, DirectX 12, Vulkan, and OpenGL.
- API badges are displayed on game cards showing detected rendering paths (e.g. DX12, VLK).
- Multi-exe scanning — all `.exe` files in the install directory and common subdirectories (bin, binaries, x64, win64, etc.) are scanned, so games like Baldur's Gate 3 with multiple executables are detected correctly.
- Manifest API overrides — the remote manifest supports comma-separated API tags (e.g. `"DX12, VLK"`) for games like Red Dead Redemption 2 that load Vulkan dynamically and can't be detected via PE imports alone.

**Multi-API labels on game cards**
- Game cards now show all detected graphics APIs for dual-API games (e.g. "DX11/12 / VLK" for Red Dead Redemption 2) instead of only the primary API.
- Only valid multi-API combos are shown: DX11/12 + VLK. Legacy APIs (OGL, DX9, DX10) only appear alone.

**Vulkan ReShade support**
- Full Vulkan implicit layer support for ReShade. UPST can now install ReShade as a global Vulkan layer via the Windows registry (`HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers`), enabling ReShade injection for Vulkan-rendered games.
- Bundled `ReShade64.json` Vulkan layer manifest with correct `device_extensions` and `disable_environment` fields, deployed alongside the ReShade DLL.
- Vulkan layer install/uninstall buttons on game cards for games with detected Vulkan support.
- Dual-API game support — games detected with both DirectX and Vulkan show a rendering path toggle, allowing you to choose which path ReShade targets.

**Vulkan-specific ReShade INI**
- A dedicated `reshade.vulkan.ini` configuration file is now bundled and deployed for Vulkan ReShade installs.
- Includes depth buffer preprocessor definitions tuned for Vulkan rendering.
- The 📋 INI button deploys both `reshade.ini` and `reshade.vulkan.ini` when a game has Vulkan support.

**Vulkan ReShade footprint tracking**
- UPST now places a footprint file (`RDXC_VULKAN_FOOTPRINT`) in game folders when Vulkan ReShade is installed, enabling managed shader deployment to Vulkan games the same way it works for DLL-injected ReShade games.
- The footprint is automatically removed when Display Commander is installed and restored when DC is uninstalled from a Vulkan game.

**Per-game Vulkan ReShade uninstall**
- A ✕ uninstall button is now shown for Vulkan games that have `reshade.ini` deployed, allowing you to remove Vulkan ReShade artifacts from a specific game folder without affecting the global Vulkan layer.

**Vulkan ReShade status detection**
- Vulkan games now show the ReShade version number with "(Vulkan)" in the detail panel when `reshade.ini` is present, matching the green installed styling of DLL-based ReShade games.

**Vulkan DC Mode defaults**
- Vulkan games now default to DC Mode Off unless explicitly overridden by the user or manifest.
- The DC Mode dropdown in overrides shows "Exclude (Off)" for Vulkan games and updates automatically when switching rendering path to Vulkan.

**Rendering path switch cleanup**
- Switching a dual-API game from DirectX to Vulkan rendering path now automatically uninstalls DX ReShade, Display Commander, reshade.ini, and managed shaders from the game folder.

**Shader selection picker**
- A new shader selection picker lets you choose exactly which shader packs to deploy from all available packs.
- The selection is saved globally and restored across app restarts.
- Per-game shader overrides allow different games to use different subsets of shader packs.

**Auto-save overrides**
- All override controls now save immediately when changed — no more Save button. TextBoxes save on Enter, ComboBoxes on selection, ToggleSwitches on toggle.
- The Save Overrides button and hint text have been removed.
- Reset Overrides persists all defaults immediately.

**Per-game shader deploy on confirm**
- Selecting per-game shaders and clicking Deploy now immediately deploys the chosen shaders to the game folder without needing a refresh.
- The Confirm button has been renamed to Deploy to match the global shader workflow.

**Seamless refresh**
- Refresh is now invisible after the initial boot. Pressing Refresh updates everything in the background without showing a loading spinner or blanking the UI. Game cards stay visible throughout.
- Game renames and other actions that trigger a refresh also happen seamlessly.

**DLL name conflict prevention**
- The ReShade and DC filename dropdowns now cross-filter: selecting a name in one box removes it from the other's dropdown, preventing both from being set to the same DLL name.
- Saving is blocked if both names match.

**Startup shader deployment**
- On launch, UPST now ensures shader packs are fully downloaded before syncing shaders to all installed game folders. Games with ReShade or DC installed will have the correct global or per-game shaders deployed automatically, even if they were installed by an older version that didn't deploy shaders.

**Wiki exclusion toggle**
- A new per-game toggle in overrides lets you exclude a game from RenoDX wiki lookups, useful for games that share a name with an unrelated wiki entry.

### Bug Fixes

**Drag-and-drop not working**
- Fixed drag-and-drop of game executables and archives not functioning in the unpackaged WinUI 3 app. Drag-and-drop now uses Win32 shell `WM_DROPFILES` handling.
- Also added UIPI bypass so drag-and-drop works when UPST is running as administrator.

**Shaders not deployed to game folders after Display Commander removal**
- Games with ReShade installed but no Display Commander were left with an empty `reshade-shaders\` folder after DC was uninstalled. Refresh and Deploy Shaders now correctly detect this scenario and deploy shaders to the game folder.

**Name reset button not persisting**
- The ↩ Reset button next to the game name and wiki name fields now correctly persists the rename back to the original store name and clears the wiki mapping, instead of only resetting the text boxes visually.

### Changes

**DLL override dropdown auto-save**
- Selecting a DLL name from the dropdown now persists immediately, in addition to the existing Enter key save.

**Global update inclusion and wiki exclusion inline layout**
- The Global update inclusion toggles and Wiki exclusion toggle are now displayed in a single inline row with a vertical divider, saving vertical space in the overrides panel.

---

## v1.4.7

### Bug Fixes

**Global shader button not updating in Settings**
- Clicking the shader mode button in the Settings panel cycled the mode internally but the button label and colours did not update visually. The SettingsViewModel was raising PropertyChanged on its own properties, but the XAML bindings target MainViewModel which was not forwarding those notifications. MainViewModel now subscribes to SettingsViewModel.PropertyChanged and re-raises the shader button property changes so the UI reflects the current mode immediately.

**DC install/update overwriting shared shader folder with per-game mode**
- Installing or updating Display Commander for any game was syncing the DC AppData shader folder using that game's per-game shader mode override instead of the global shader mode. Because the DC shader folder is shared across all DC-mode games, a per-game override of "Off" would wipe shaders for every other DC-mode game, and a per-game override of "All" would deploy extra shaders globally. The DC folder sync now always uses the global shader deploy mode, while per-game overrides continue to apply only to standalone ReShade game folders.

### Changes

**Codebase optimisation**
- Shared UI helpers extracted (UIFactory, ResourceKeys) to eliminate duplicated brush/style creation across CardBuilder, DetailPanelBuilder, and DragDropHandler.
- Five new service interfaces introduced (IGameInitializationService, IUpdateOrchestrationService, IDllOverrideService, IGameNameService, ILiliumShaderService) to decouple MainViewModel from concrete implementations.
- Property notification deduplication — SetProperty guards added to prevent redundant UI updates.
- String comparisons standardised to OrdinalIgnoreCase across all filter and lookup paths.
- Async best practices applied — ConfigureAwait(false) on non-UI awaits, SafeFireAndForget extension for fire-and-forget tasks.
- Error handling normalised — ~180 CrashReporter.Log calls standardised with consistent tag format.
- Retry logic added to settings and library file writes to handle file contention.
- Per-platform exception isolation in game detection prevents one platform's failure from blocking others.
- ManifestService null-safety hardened for malformed remote JSON.
- GameDetectionService optimised with configurable max scan depth and engine detection caching.
- Memory management improvements — HttpClient lifetime audit, brush caching, PropertyChanged cleanup.
- WrapPanel measure/arrange optimised to reduce layout passes.
- DragDropHandler hardened with upfront extension validation.
- XML documentation added to all new public APIs.
- 11 property-based tests added covering filter correctness, batch collection, drag-drop validation, and property notification.

---

## v1.4.6

### New Features

**Per-component Update All toggles**
- The single "Exclude from Update All" toggle in the Overrides section has been replaced with three separate toggle switches for ReShade, Display Commander, and RenoDX.
- Each toggle independently controls whether the game is included in bulk updates for that component. All three default to On (included).
- Toggles are displayed horizontally under a "Global update inclusion" header, each in its own bordered card for clarity.
- Legacy settings are automatically migrated — if you previously excluded a game from Update All, all three toggles will start excluded.
- Applies to both Detail View and Grid View overrides.

**Reset Overrides button**
- A new "Reset Overrides" button in the Overrides section resets all per-game settings back to their defaults in one click: game name, wiki name, DC mode, shader mode, DLL override, all three update toggles, and wiki exclusion.
- Positioned on the left side opposite the Save Overrides button.

**Per-session logging**
- A new session log file is created every time UPST starts, named with a timestamp (e.g. `session_2025-03-14_12-30-00.txt`).
- All activity is logged to the session file automatically — no need to enable Verbose Logging first.
- Old session logs are automatically pruned to keep a maximum of 10 on disk.

**DLL override dropdown suggestions**
- The ReShade and DC filename text boxes in the DLL naming override section are now dropdown combo boxes with a clickable arrow that shows a predefined list of common DLL names: `dxgi.dll`, `d3d11.dll`, `dinput8.dll`, `version.dll`, `winmm.dll`, `d3d12.dll`, `xinput1_3.dll`, `msvcp140.dll`, `bink2w64.dll`, `d3d9.dll`.
- Select a DLL from the dropdown or type any custom filename directly.
- Applies to both Detail View and Grid View overrides.

**Mod author badges in Detail View**
- Named mods from the RenoDX wiki now display the mod author as a bordered badge on the detail panel info line, right-aligned next to the existing platform and status badges.
- Multiple authors (e.g. "oopydoopy & Voosh") each get their own badge.
- Generic Unreal Engine mods show "ShortFuse", UE-Extended mods show "Marat", and generic Unity mods show "Voosh".

**Update-available version display**
- The purple update indicator next to ReShade and Display Commander buttons now shows the currently installed version number (e.g. `6.7.3`) instead of just "Update", so you can see which version you're running before updating.
- The text remains purple to indicate an update is available, and switches to the new version number in green once updated.

### Bug Fixes

**ReShade not detected under non-standard filenames**
- ReShade installations using non-standard DLL filenames (e.g. `d3d11.dll`, `dinput8.dll`, `version.dll`) were not detected by UPST, showing the game as "Not Installed" and allowing a second ReShade DLL to be installed alongside the existing one. UPST now scans all DLL files in the game folder using binary signature detection (`IsReShadeFileStrict`) as a fallback when the standard filename checks don't find ReShade.

**Old ReShade DLL not removed on reinstall with non-standard filename**
- Clicking "Reinstall ReShade" on a game where ReShade was detected under a non-standard filename (e.g. `d3d11.dll`) installed a fresh `dxgi.dll` without removing the existing non-standard DLL, leaving two ReShade DLLs in the game folder. The reinstall flow now looks up the existing install record and deletes the old DLL when it differs from the new destination filename.

### Changes

**Code refactor**
- ViewModel partial classes reorganised and split into dedicated files for ReShade, Display Commander, RenoDX, Luma, and UI concerns.
- DetailPanelBuilder and CardBuilder extracted from MainWindow code-behind to reduce file size.
- DragDropHandler extracted into its own class.

---

## v1.4.5

### New Features

**Improved Unity engine detection**
- Unity games that don't have `UnityPlayer.dll` in the base folder are now detected correctly.
- Detection now also checks for `Mono` folder, `MonoBleedingEdge` folder, `il2cpp` folder, and `GameAssembly.dll` — all common markers of Unity IL2CPP and Mono builds.

**UE-Extended available for all generic Unreal Engine games**
- The UE-Extended toggle now appears for every Unreal Engine game that does not have a named mod on the RenoDX wiki, not just games explicitly listed in the manifest.
- A compatibility warning dialog now pops up when enabling UE-Extended, advising that not all games are compatible and to check the Notes section for any game-specific information.

**Manifest 32-bit / 64-bit flags**
- The `thirtyTwoBitGames` manifest flag now takes priority over automatic PE header detection, restoring the ability to force-flag a game as 32-bit from the manifest.
- A new `sixtyFourBitGames` manifest flag allows games incorrectly detected as 32-bit by the auto-detection to be force-flagged as 64-bit.

**Remember last view**
- The app now remembers whether it was last in Detail View or Grid View and opens in that same view on next launch.

**Installed filter**
- New filter button between All Games and Favourites that shows only games with RenoDX or Luma installed (DC and ReShade alone do not qualify).

**Manifest engine overrides**
- A new `engineOverrides` manifest field allows the engine for any game to be overridden.
- Setting a game to `"Unreal"` or `"Unity"` changes both its filter category and enables the correct generic mod/addon behaviour (UE-Extended eligibility, generic Unity addon, etc.).
- Setting a game to any other string (e.g. `"Silk"`, `"Source 2"`, `"Creation Engine"`) displays that label in the engine badge but keeps the game in the Other filter.
- Games with no known or overridden engine continue to show as Unknown and filter into Other.

**Manifest DLL name overrides**
- A new `dllNameOverrides` manifest field allows the ReShade and Display Commander install filenames to be set remotely per game.
- Example: `"Mirror's Edge": { "reshade": "d3d9.dll", "dc": "winmm.dll" }`. Either field may be empty to keep the default name.
- User-set per-game DLL overrides in the Manage panel always take priority over manifest values.

**ReShadePreset.ini auto-deploy**
- If a `ReShadePreset.ini` file is placed in `%LOCALAPPDATA%\RenoDXCommander\inis\`, it is automatically copied to the game folder alongside `reshade.ini` on every ReShade or Display Commander install.
- The 📋 INI button on the ReShade row also copies the preset file if present.

**ReShade and Display Commander version display**
- The status label next to the ReShade and Display Commander install buttons now shows the installed version number (e.g. `6.7.3`) instead of just `Installed`.
- Falls back to `Installed` if no version information is available.
- Applies to both Detail View and the grid card Manage popout.

**Custom engine icon**
- Games with a custom engine name set via `engineOverrides` in the manifest now show a dedicated engine icon in the engine badge, rather than no icon.

### Changes

**Filter layout**
- The Other filter has been moved from the top row to the second row, now sitting between Unity and RenoDX.

**Change install folder now opens game folder**
- The folder picker for changing a game's install path now opens directly in the game's current folder instead of the last-used location.

### Bug Fixes

**UE-Extended toggle not applying**
- Clicking the UE-Extended button was silently ignored for games that had not yet been flagged as `IsGenericMod`, even though the button was visible. The eligibility check now matches the same conditions used to show the button.

**Games showing as installed after manual file removal**
- After a full Refresh, games with ReShade, Display Commander, or RenoDX manually deleted from the game folder were still showing as installed in the UI.
- UPST now verifies that the installed file actually exists on disk when loading saved records. Stale records are automatically cleaned up and the correct status is shown immediately on the next Refresh.

**Manifest DLL name override not applying to existing installs**
- The `dllNameOverrides` manifest field was only used as the filename for new installs. Games already installed under a different filename were not renamed when the manifest override was applied.
- UPST now renames existing ReShade and Display Commander files to match the manifest override on every Refresh, matching the behaviour of user-set DLL overrides.

**Manifest DLL name override not visible in UI**
- Games flagged via `dllNameOverrides` in the manifest were silently installing with the correct filename but the DLL naming override toggle in the Overrides section remained off, giving no indication anything was different.
- Games with a manifest DLL override now have the toggle turned on automatically and the filenames pre-filled, identical to a user-set override. The override can be disabled per-game and that preference is remembered across refreshes.

**Change install folder picker opening in Documents**
- The Change Install Folder button was opening the file picker in the Documents folder instead of the game's current install directory.
- The picker now opens directly in the game's folder using the native `IFileOpenDialog` COM interface, which correctly supports arbitrary start paths in WinUI 3 unpackaged apps.

---

## v1.4.4


### New Features

**Drag-and-drop archive extraction**
- Archives (.zip, .7z, .rar, .tar, .gz, .bz2, .xz) can now be dragged directly onto the UPST window. The archive is extracted using the bundled 7-Zip, and any `.addon64` or `.addon32` files inside are automatically found and installed via the existing addon install flow.
- If multiple addon files are found inside an archive, a picker dialog lets you choose which one to install.
- If no addon files are found, a clear message is shown.

**32bit Mod**

- 32bit Mode has been replaced by automatic detection of 32bit game executables. Thanks to Lazorr for implementing this and Jon for the starting point.

### Changes

**Grid view wiki status icon**
- Each game card in grid view now displays the wiki status icon on the same row as the RDX/RS/DC installation dots, right-aligned.
- The wiki status shows only the icon, not the full text label. Hovering shows the full label as a tooltip.
- ✅ = Working (listed on RenoDX wiki). 🚧 = In Progress (listed on wiki). ⚠️ = May Work (not on wiki but Unreal/Unity engine detected). ❓ = Unknown (not on wiki, no known engine). 💬 = Discord-only.
- Games in Luma mode do not show a wiki status icon on the grid card.

### Bug Fixes

**Wiki parser now handles all table formats**
- The RenoDX wiki splits its game list across multiple tables with varying column layouts (3-column, 4-column, status in different positions). The parser previously only read the first 4-column table, missing ~40% of games. It now detects table structure by examining header text (Name/Maintainer/Links/Status) and parses every mod table on the page regardless of column count or order. This fixes games like Lies of P, Aragami 2, EVERSPACE 2, CODE VEIN, Avatar, Pacific Drive, and many others showing incorrect wiki status.

---

## v1.4.3

### New Features

**Grid View**

- Users now have the option of using a Grid View. Switch between Grid View and Detail View easily with the click of a button. Each game can now be managed from a smaller pop out while in Grid View.

### Changes

**Background Maintainance**

- Removed excess flyouts in Detail View.
- Cleaned up some code.

---

## v1.4.2

### Changes

**UI Tweaks**

- Layout change on game cards 

---

## v1.4.1

### Changes

**Code refinement**

- Additional UI cleanup and redundant code removal.

---

## v1.4.0

### Changes

**New UI design**

- Brand new UI designed by Lazorr as well as multiple background tweaks and fixes. 

---

## v1.3.7

### Changes

**ReShade INI deployed with DC Mode installs**
- Installing Display Commander in DC Mode now automatically deploys the template `reshade.ini` to the game folder using the same merge logic as standalone ReShade installs. If no INI exists, the template is copied; if one already exists, template keys are merged on top while preserving game-specific settings.

### Bug Fixes

**Foreign DLL backup not triggering for OptiScaler and similar tools**
- Fixed `dxgi.dll` files from OptiScaler (and other tools that mention "ReShade" in config comments) being misidentified as ReShade and overwritten instead of backed up to `.original`. The binary scan now only matches on `reshade.me` or `crosire` — strings unique to the actual ReShade binary — and rejects files over 15 MB as too large to be ReShade.

---

## v1.3.6

### New Features

**Battle.net game detection**
- UPST now automatically detects games installed via the Battle.net (Blizzard) launcher.
- Detection uses Windows Uninstall registry entries (filtering by Blizzard/Activision publisher), the Battle.net config file (`Battle.net.config`) for the default install path, and default folder scanning under `Program Files\Battle.net` and `Blizzard Entertainment`.
- Battle.net games appear with a dedicated platform icon on game cards and in the compact mode game list.
- Drag-and-drop exe detection now recognises Battle.net store markers (`.build.info`, `.product.db`).

**Rockstar Games Launcher detection**
- UPST now automatically detects games installed via the Rockstar Games Launcher.
- Detection uses Windows Uninstall registry entries (filtering by Rockstar publisher), the launcher's `titles.dat` file for install paths, and default folder scanning under `Program Files\Rockstar Games`.
- Rockstar games appear with a dedicated platform icon on game cards and in the compact mode game list.
- Drag-and-drop exe detection now recognises Rockstar store markers (`PlayGTAV.exe`, `socialclub*.dll`).

### Changes

**Compact UI layout rework**
- The top header bar (logo, title, search box) is now completely hidden in compact mode. The filter bar is the topmost bar.
- The search box has been moved to the right-hand toolbar, placed below the About button.
- The UPST logo is displayed below the search box on the right toolbar.
- The "UPST" title text is no longer shown in compact mode.
- The first game alphabetically is now auto-selected when entering compact mode, so the view is never empty on launch.

**About panel version**
- The About panel now correctly displays the current version number.

**Scroll and selection preservation**
- Favouriting or unfavouriting a game no longer resets the scroll position in full UI mode or deselects the game in compact mode.
- Refresh and Full Refresh now restore the previous scroll position in full UI and re-select the previously selected game in compact mode.

**ReShade INI merge**
- Installing ReShade or clicking the 📋 INI button now merges the template `reshade.ini` into the game's existing INI instead of overwriting it. Template keys always take precedence, but any game-specific settings not in the template (e.g. addon configs, effect toggles, custom keybinds) are preserved.

---

## v1.3.5

### Bug Fixes

**Drag-and-drop crash loop (source icon binding)**
- Fixed an infinite crash loop when dragging and dropping a game exe into the window. The platform source icon binding threw `ArgumentException` when the game had no known store source (e.g. manually added games), because WinUI's `ConvertValue` cannot convert `null` to an `ImageSource`. The icon is now bound via an explicit `BitmapImage` with a typed `Uri`, bypassing `ConvertValue` entirely.

**Added games appear in correct alphabetical position**
- Games added via drag-and-drop or the ➕ Add Game button now appear in their correct alphabetical position in the game list immediately, instead of being appended to the bottom.

---

## v1.3.4

### New Features

**Ubisoft Connect game detection**
- UPST now automatically detects games installed via Ubisoft Connect (formerly Uplay).
- Detection uses registry keys, the launcher's `settings.yml` configuration, and default install folder scanning.
- Ubisoft games appear with a dedicated platform icon on game cards and in the compact mode game list.
- Drag-and-drop exe detection now recognises Ubisoft store markers (`uplay_install.state`, `uplay_*.dll`).

### Changes

**DLL naming override — rename instead of delete**
- Enabling DLL naming override now renames existing ReShade/DC DLLs to the custom filenames instead of uninstalling them, keeping installs tracked without requiring a reinstall.
- When override filenames are changed while already enabled, existing custom-named files are renamed in place to the new names.
- Both Full UI and Compact UI now use the new rename path when DLL overrides are already active and only the filenames change.

**Compact view — selection preserved after save**
- After saving overrides in Compact mode, the previously selected game card is automatically re-selected once filtering finishes, preventing the selection from jumping unexpectedly.

**Deploy buttons — confirmation dialogs**
- The **🎨 Deploy Shaders** and **⚙ Deploy DC Mode** buttons now show a confirmation dialog asking to Continue or Cancel before executing bulk operations.

### Bug Fixes

**Search box clear button visibility**
- The search box now consistently shows the ✕ clear button as soon as you type the first character, instead of appearing only after further edits.

**Addon download and drag-and-drop — extension validation**
- Downloads and drag-and-drop addon installs now validate the resolved filename extension before any network or file activity, rejecting non-`.addon64` / `.addon32` files with a clear error message and skipping the download.

**Luma snapshot security — trusted source guard**
- Luma snapshot downloads are now restricted to GitHub URLs under `https://github.com/Filoppi/`. Any other URL is rejected with an error before any network request is made.

---

## v1.3.3

### New Features

**Compact UI Mode**
- Added an alternative "Compact" layout alongside the existing "Full" UI.
- Compact mode shows an alphabetical game list on the left, the selected game's card and overrides in the center, and all toolbar buttons vertically on the right.
- Toggle between modes with the 📐 button — in the header when in Full mode, or at the top of the right toolbar when in Compact mode.
- The UI mode preference is saved and persists across app restarts.

**Platform source icons**
- Game cards and the compact mode game list now display platform-specific icons (Steam, GOG, Epic, EA App, Xbox) instead of plain text badges.

**Remote manifest system**
- Game-specific overrides (blacklist, install path corrections, wiki status, game notes, shader packs, Luma defaults, native HDR list) are now driven by a remote manifest hosted on GitHub. This allows quick fixes and new game support without requiring an app update.
- The manifest is fetched from the GitHub API on launch with a raw.githubusercontent.com fallback, and cached locally for offline use.

**Wiki unlinks (manifest)**
- The remote manifest can now unlink games from false fuzzy wiki matches. Unlinked games fall through to their generic engine addon (Unreal or Unity) instead of being incorrectly associated with a named wiki mod.

**Luma always enabled**
- Luma Framework support is no longer hidden behind a settings toggle. Luma badges appear on all eligible game cards by default. The "Luma (Experimental)" setting has been removed from About → Settings.

**Luma auto-default for specific games**
- Games listed in the remote manifest automatically start in Luma mode on first detection, without requiring manual toggling.

**Luma-specific game notes**
- The ℹ info popup now shows custom Luma-specific notes (from the remote manifest) when a game is in Luma mode, providing tailored guidance beyond the standard wiki notes.

### Changes

**Filter bar rework**
- Removed the "Installed" and "Not Installed" filter tabs.
- Added a "RenoDX" tab that shows only games with RenoDX wiki mods available.
- The "Luma" tab is now always visible (previously required enabling Luma in Settings).

**Wiki status for unmatched Unity/Unreal games**
- Unity and Unreal Engine games that don't match any wiki entry now display a "🚧 Unknown" status badge with amber colouring instead of being left blank, indicating they may become supported in future.

**Compact list update highlight**
- Games in the compact mode list now show a highlighted border when an update is available.

**Per-mode window size persistence**
- Full UI and Compact UI each remember their own window size independently. Switching modes restores the last-used size for that mode.

**"Extended UE" tag support**
- The remote manifest can now tag Unreal Engine games as "Extended UE", which automatically assigns the UE-Extended addon and marks the game as native HDR.

**Game Info dialog enlarged**
- The ℹ info popup's maximum height increased from 400 to 440 pixels to reduce clipping of longer notes.

### Bug Fixes

**Nexus link icon not appearing**
- Fixed the 🌐 Nexus/external link button not appearing on game cards where a Nexus URL was available but no snapshot was present.

**Luma badge dimming**
- The Luma toggle badge now uses a dimmer green when active, making it easier to distinguish from the bright "available" state.

**UE-Extended button sizing**
- Fixed the ⚡ UE-Extended toggle button being taller and wider than adjacent buttons on game cards.
