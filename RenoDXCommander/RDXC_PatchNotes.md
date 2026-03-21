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
- RDXC now watches your Downloads folder (configurable in Settings) for new `renodx-*.addon64` / `.addon32` files and automatically prompts you to install them.
- Double-clicking an addon file in Explorer opens RDXC and triggers the install flow. If RDXC is already running, the file is forwarded to the existing instance via named pipe.
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
- Fixed ReShade showing "Unable to save configuration and/or current preset" errors for `reshade.ini` and `ReShadePreset.ini` in games where these files were deployed by RDXC. The INI writer was emitting a UTF-8 BOM (byte order mark) that ReShade's native parser cannot handle. INI files are now written as plain UTF-8 without BOM.

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
- RDXC now scans game executables using PE header import table analysis to detect which graphics APIs a game uses: DirectX 11, DirectX 12, Vulkan, and OpenGL.
- API badges are displayed on game cards showing detected rendering paths (e.g. DX12, VLK).
- Multi-exe scanning — all `.exe` files in the install directory and common subdirectories (bin, binaries, x64, win64, etc.) are scanned, so games like Baldur's Gate 3 with multiple executables are detected correctly.
- Manifest API overrides — the remote manifest supports comma-separated API tags (e.g. `"DX12, VLK"`) for games like Red Dead Redemption 2 that load Vulkan dynamically and can't be detected via PE imports alone.

**Multi-API labels on game cards**
- Game cards now show all detected graphics APIs for dual-API games (e.g. "DX11/12 / VLK" for Red Dead Redemption 2) instead of only the primary API.
- Only valid multi-API combos are shown: DX11/12 + VLK. Legacy APIs (OGL, DX9, DX10) only appear alone.

**Vulkan ReShade support**
- Full Vulkan implicit layer support for ReShade. RDXC can now install ReShade as a global Vulkan layer via the Windows registry (`HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers`), enabling ReShade injection for Vulkan-rendered games.
- Bundled `ReShade64.json` Vulkan layer manifest with correct `device_extensions` and `disable_environment` fields, deployed alongside the ReShade DLL.
- Vulkan layer install/uninstall buttons on game cards for games with detected Vulkan support.
- Dual-API game support — games detected with both DirectX and Vulkan show a rendering path toggle, allowing you to choose which path ReShade targets.

**Vulkan-specific ReShade INI**
- A dedicated `reshade.vulkan.ini` configuration file is now bundled and deployed for Vulkan ReShade installs.
- Includes depth buffer preprocessor definitions tuned for Vulkan rendering.
- The 📋 INI button deploys both `reshade.ini` and `reshade.vulkan.ini` when a game has Vulkan support.

**Vulkan ReShade footprint tracking**
- RDXC now places a footprint file (`RDXC_VULKAN_FOOTPRINT`) in game folders when Vulkan ReShade is installed, enabling managed shader deployment to Vulkan games the same way it works for DLL-injected ReShade games.
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
- On launch, RDXC now ensures shader packs are fully downloaded before syncing shaders to all installed game folders. Games with ReShade or DC installed will have the correct global or per-game shaders deployed automatically, even if they were installed by an older version that didn't deploy shaders.

**Wiki exclusion toggle**
- A new per-game toggle in overrides lets you exclude a game from RenoDX wiki lookups, useful for games that share a name with an unrelated wiki entry.

### Bug Fixes

**Drag-and-drop not working**
- Fixed drag-and-drop of game executables and archives not functioning in the unpackaged WinUI 3 app. Drag-and-drop now uses Win32 shell `WM_DROPFILES` handling.
- Also added UIPI bypass so drag-and-drop works when RDXC is running as administrator.

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
- A new session log file is created every time RDXC starts, named with a timestamp (e.g. `session_2025-03-14_12-30-00.txt`).
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
- ReShade installations using non-standard DLL filenames (e.g. `d3d11.dll`, `dinput8.dll`, `version.dll`) were not detected by RDXC, showing the game as "Not Installed" and allowing a second ReShade DLL to be installed alongside the existing one. RDXC now scans all DLL files in the game folder using binary signature detection (`IsReShadeFileStrict`) as a fallback when the standard filename checks don't find ReShade.

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
- RDXC now verifies that the installed file actually exists on disk when loading saved records. Stale records are automatically cleaned up and the correct status is shown immediately on the next Refresh.

**Manifest DLL name override not applying to existing installs**
- The `dllNameOverrides` manifest field was only used as the filename for new installs. Games already installed under a different filename were not renamed when the manifest override was applied.
- RDXC now renames existing ReShade and Display Commander files to match the manifest override on every Refresh, matching the behaviour of user-set DLL overrides.

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
- Archives (.zip, .7z, .rar, .tar, .gz, .bz2, .xz) can now be dragged directly onto the RDXC window. The archive is extracted using the bundled 7-Zip, and any `.addon64` or `.addon32` files inside are automatically found and installed via the existing addon install flow.
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
- RDXC now automatically detects games installed via the Battle.net (Blizzard) launcher.
- Detection uses Windows Uninstall registry entries (filtering by Blizzard/Activision publisher), the Battle.net config file (`Battle.net.config`) for the default install path, and default folder scanning under `Program Files\Battle.net` and `Blizzard Entertainment`.
- Battle.net games appear with a dedicated platform icon on game cards and in the compact mode game list.
- Drag-and-drop exe detection now recognises Battle.net store markers (`.build.info`, `.product.db`).

**Rockstar Games Launcher detection**
- RDXC now automatically detects games installed via the Rockstar Games Launcher.
- Detection uses Windows Uninstall registry entries (filtering by Rockstar publisher), the launcher's `titles.dat` file for install paths, and default folder scanning under `Program Files\Rockstar Games`.
- Rockstar games appear with a dedicated platform icon on game cards and in the compact mode game list.
- Drag-and-drop exe detection now recognises Rockstar store markers (`PlayGTAV.exe`, `socialclub*.dll`).

### Changes

**Compact UI layout rework**
- The top header bar (logo, title, search box) is now completely hidden in compact mode. The filter bar is the topmost bar.
- The search box has been moved to the right-hand toolbar, placed below the About button.
- The RDXC logo is displayed below the search box on the right toolbar.
- The "RDXC RenoDXCommander" title text is no longer shown in compact mode.
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
- RDXC now automatically detects games installed via Ubisoft Connect (formerly Uplay).
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
