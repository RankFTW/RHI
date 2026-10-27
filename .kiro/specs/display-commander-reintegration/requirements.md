# Requirements Document

## Introduction

Display Commander (DC) is a frame rate limiter addon that was removed from RHI in v1.6.0. This feature reintegrates DC back into RHI as a second limiter option alongside ReLimiter. DC and ReLimiter are mutually exclusive — only one can be installed per game at a time. DC is fetched from GitHub, supports both 32-bit and 64-bit games via the LITE variant, and follows the same update, status, and override patterns as existing RHI components.

## Glossary

- **RHI**: ReShade HDR Installer — the application being modified
- **DC**: Display Commander — a frame rate limiter addon distributed as `.addon64` / `.addon32` files
- **ReLimiter**: The existing frame rate limiter addon already integrated into RHI
- **Detail_Panel**: The right-side panel in RHI that shows component install rows and game info
- **Overrides_Panel**: The expandable section in the Detail_Panel for per-game configuration overrides
- **Remote_Manifest**: A JSON manifest fetched from a remote URL that provides per-game configuration overrides
- **Update_All**: The batch operation that updates all eligible components across all games
- **Sidebar_Badge**: The green dot indicator on sidebar game cards that signals available updates
- **LITE_Variant**: The lightweight version of DC distributed as `zzz_display_commander_lite.addon32` and `zzz_display_commander_lite.addon64`
- **GameCardViewModel**: The view model representing a single game's state in the UI
- **AuxInstalledRecord**: The data model tracking auxiliary component installations (ReShade, DC)

## Requirements

### Requirement 1: DC Download and Version Fetching

**User Story:** As a user, I want RHI to download the latest DC LITE addon from GitHub, so that I always get the current version without manual downloads.

#### Acceptance Criteria

1. WHEN RHI fetches DC, THE RHI SHALL download from `https://github.com/pmnoxx/display-commander/releases/tag/latest_build`
2. WHEN the game is 64-bit, THE RHI SHALL use the file `zzz_display_commander_lite.addon64`
3. WHEN the game is 32-bit, THE RHI SHALL use the file `zzz_display_commander_lite.addon32`
4. WHEN the GitHub endpoint is unreachable, THE RHI SHALL log the failure and display an error message on the DC component row
5. WHEN a previously cached DC file exists locally, THE RHI SHALL use the cached file for install operations when the remote is unavailable

### Requirement 2: Mutual Exclusion of DC and ReLimiter

**User Story:** As a user, I want only one frame rate limiter installed at a time, so that the two limiters do not conflict with each other.

#### Acceptance Criteria

1. WHILE DC is installed for a game, THE Detail_Panel SHALL display the ReLimiter install button as greyed out and unclickable
2. WHILE ReLimiter is installed for a game, THE Detail_Panel SHALL display the DC install button as greyed out and unclickable
3. WHEN the user removes DC via the red X button, THE Detail_Panel SHALL re-enable the ReLimiter install button
4. WHEN the user removes ReLimiter via the red X button, THE Detail_Panel SHALL re-enable the DC install button
5. THE RHI SHALL prevent programmatic installation of DC when ReLimiter is already installed for the same game
6. THE RHI SHALL prevent programmatic installation of ReLimiter when DC is already installed for the same game
7. WHEN the app launches and a game already has ReLimiter installed, THE Detail_Panel SHALL display the DC install button as greyed out and unclickable immediately upon selecting that game
8. WHEN the app launches and a game already has DC installed, THE Detail_Panel SHALL display the ReLimiter install button as greyed out and unclickable immediately upon selecting that game

### Requirement 3: DC Install Row Placement and Visual Grouping

**User Story:** As a user, I want the DC and ReLimiter rows visually grouped together in the detail panel, so that I can see they are related limiter options.

#### Acceptance Criteria

1. THE Detail_Panel SHALL display the DC install row between the ReLimiter row and the RenoDX row
2. THE Detail_Panel SHALL render a visible border or box around the ReLimiter and DC rows to visually group them as limiter options
3. WHEN a game is in Luma mode, THE Detail_Panel SHALL hide the DC row (matching ReLimiter visibility behavior in Luma mode is not required — ReLimiter is always visible, but DC follows the same pattern as RenoDX: hidden in Luma mode)

### Requirement 4: DC Install, Reinstall, and Uninstall

**User Story:** As a user, I want to install, reinstall, and uninstall DC with one click, so that managing DC is as easy as managing other RHI components.

#### Acceptance Criteria

1. WHEN the user clicks the DC install button, THE RHI SHALL download the correct bitness LITE variant and deploy it to the game folder
2. WHEN DC is already installed, THE Detail_Panel SHALL show a reinstall button with the ↺ icon
3. WHEN the user clicks the DC red X button, THE RHI SHALL remove the DC addon file from the game folder
4. WHEN DC is installed, THE RHI SHALL create an AuxInstalledRecord with AddonType "DisplayCommander"
5. WHEN DC is uninstalled, THE RHI SHALL remove the corresponding AuxInstalledRecord
6. WHEN DC installation fails, THE RHI SHALL display an error message on the DC component row and leave the game folder unchanged

### Requirement 5: DC Status Display

**User Story:** As a user, I want to see the DC installed status and version number, so that I know which version is deployed.

#### Acceptance Criteria

1. WHEN DC is installed, THE Detail_Panel SHALL display the DC version number read from the PE file version info
2. WHEN DC is installed, THE Detail_Panel SHALL display the status text in green (#5ECB7D), matching the color scheme of other installed components
3. WHEN DC is not installed, THE Detail_Panel SHALL display "Ready" in the muted color (#A0AABB)
4. WHEN DC is being installed, THE Detail_Panel SHALL display "Installing…" in amber (#D4A856)
5. WHEN a DC update is available, THE Detail_Panel SHALL display "Update" in purple (#B898E8)
6. THE Detail_Panel SHALL display a status dot for DC following the same dot color rules as ReLimiter (green when installed, white when not installed)
7. WHEN DC is successfully installed, THE Detail_Panel SHALL display a green ✅ confirmation message that auto-fades after the same timeout as other components (4 seconds)
8. WHEN DC is successfully removed, THE Detail_Panel SHALL display a red ✖ removal message that auto-fades after the same timeout as other components (4 seconds)
9. WHEN DC installation or removal fails, THE Detail_Panel SHALL display an error message that does NOT auto-fade (remains visible until the next action)

### Requirement 6: DC DLL Naming Override

**User Story:** As a user, I want to rename the DC DLL file for specific games, so that I can resolve compatibility issues with games that require specific DLL names.

#### Acceptance Criteria

1. THE Overrides_Panel SHALL include a toggle for DC DLL naming override in the DLL naming override section
2. WHEN the DC DLL naming toggle is enabled, THE Overrides_Panel SHALL display a dropdown with the following options: dxgi.dll, d3d9.dll, d3d11.dll, d3d12.dll, ddraw.dll, hid.dll, version.dll, opengl32.dll, dbghelp.dll, vulkan-1.dll, winmm.dll
3. WHEN a DC DLL override is set, THE RHI SHALL install DC using the selected filename instead of the default addon filename
4. WHEN the DC DLL override is disabled, THE RHI SHALL install DC using the default LITE variant filename
5. THE RHI SHALL persist the DC DLL override in the DllOverrideConfig alongside the existing ReShade override
6. WHEN DC is already installed and the user changes the DLL naming override, THE RHI SHALL immediately rename the DC file in the game folder to the new name without requiring a reinstall
7. WHEN the user selects a DC DLL name that matches an existing file in the game folder (not owned by DC), THE RHI SHALL display a warning popup informing the user of the conflict, matching the existing foreign DLL detection dialog pattern
8. WHEN the user confirms the warning popup, THE RHI SHALL proceed with the rename, overwriting the existing file
9. WHEN the user cancels the warning popup, THE RHI SHALL revert the dropdown selection to the previous value and leave the game folder unchanged

### Requirement 7: DC Global Update Exclusion Toggle

**User Story:** As a user, I want to exempt specific games from DC global updates, so that I can keep a specific DC version on certain games.

#### Acceptance Criteria

1. THE Overrides_Panel SHALL include a "DC" toggle in the Global update inclusion section alongside the existing ReShade, ReLimiter, and RenoDX toggles
2. WHEN the DC toggle is set to "No", THE RHI SHALL exclude the game from Update All operations for DC
3. WHEN the DC toggle is set to "No", THE Sidebar_Badge SHALL not count DC updates for the excluded game
4. WHEN Reset Overrides is clicked, THE RHI SHALL reset the DC exclusion toggle back to included

### Requirement 8: DC Update Detection and Update All

**User Story:** As a user, I want DC checked for updates alongside other components, so that I stay current without manual checking.

#### Acceptance Criteria

1. WHEN RHI performs update checks on startup, THE RHI SHALL check for DC updates by comparing the remote release against the locally installed version
2. WHEN a DC update is available, THE Sidebar_Badge SHALL display the green update dot on affected game cards
3. WHEN the user clicks Update All, THE RHI SHALL update DC for all eligible games (not excluded, not hidden)
4. WHEN a DC update is available, THE Detail_Panel SHALL display the DC install button with the purple update styling and "⬆ Update DC" label
5. WHEN all DC updates are completed, THE RHI SHALL refresh the Update All button state

### Requirement 9: Remote Manifest DC Addon Filename Override

**User Story:** As a user, I want the remote manifest to be able to override the DC addon filename per game, so that game-specific compatibility issues can be resolved remotely without an app update.

#### Acceptance Criteria

1. THE ManifestDllNames model SHALL include a `Dc` property with `[JsonPropertyName("dc")]` to deserialize the existing "dc" field from the manifest's `dllNameOverrides` section (e.g. `"Mirror's Edge": { "reshade": "d3d9.dll", "dc": "winmm.dll" }`)
2. WHEN a game has a manifest DC filename override, THE RHI SHALL install DC using the manifest-specified filename
3. WHEN both a user DLL override and a manifest override exist for DC, THE RHI SHALL prioritize the user override over the manifest override
4. WHEN only a manifest override exists (no user override), THE RHI SHALL use the manifest value as the DC filename

### Requirement 10: DC GameCardViewModel State

**User Story:** As a developer, I want DC state tracked on the GameCardViewModel, so that the UI can reactively display DC status, progress, and actions.

#### Acceptance Criteria

1. THE GameCardViewModel SHALL include observable properties for DC status, installing flag, progress, action message, installed version, and installed file name
2. THE GameCardViewModel SHALL include computed properties for DC status text, status color, action label, button colors, delete visibility, and row visibility
3. WHEN DC status changes, THE GameCardViewModel SHALL notify all dependent computed properties
4. WHEN DC installing state changes, THE GameCardViewModel SHALL notify all dependent computed properties
5. THE GameCardViewModel SHALL include DC properties in the NotifyAll method for bulk state refresh

### Requirement 11: DC Detection on Game Scan

**User Story:** As a user, I want RHI to detect existing DC installations when scanning game folders, so that previously installed DC shows up without reinstalling.

#### Acceptance Criteria

1. WHEN RHI scans a game folder, THE RHI SHALL detect the presence of DC LITE addon files (`zzz_display_commander_lite.addon64` or `zzz_display_commander_lite.addon32`)
2. WHEN a DC file is detected, THE RHI SHALL set the DC status to Installed and read the version from PE file info
3. WHEN a DC file with a custom DLL override name is present, THE RHI SHALL detect it using the AuxInstalledRecord tracking data
