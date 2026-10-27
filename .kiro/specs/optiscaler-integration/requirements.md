# Requirements Document

## Introduction

This feature integrates OptiScaler as a fully managed component in RHI (RenoDX HDR Installer). OptiScaler is a 64-bit middleware tool that intercepts upscaler calls (DLSS/FSR/XeSS) from games and redirects them to a different upscaling backend. It installs as a DLL replacement in the game folder (typically dxgi.dll). RHI will manage the full lifecycle: download, staging, install, uninstall, update detection, INI configuration, ReShade coexistence, DLL naming overrides, hotkey configuration, and per-game tracking. The integration follows the same patterns established by ReLimiter and Display Commander.

## Glossary

- **RHI**: RenoDX HDR Installer — the application (RenoDXCommander)
- **OptiScaler**: A middleware DLL that intercepts upscaler calls and redirects them to alternative backends
- **OptiScaler_Service**: The service responsible for downloading, staging, installing, uninstalling, and updating OptiScaler
- **OptiScaler_DLL**: The main OptiScaler.dll file, which must be renamed to a supported proxy DLL filename when deployed
- **Companion_Files**: The set of auxiliary files bundled with OptiScaler: fakenvapi.dll, dlssg_to_fsr3.dll, and FFX SDK runtime DLLs
- **Staging_Folder**: The local cache directory at %LOCALAPPDATA%\RHI\optiscaler\ where the downloaded OptiScaler release is extracted
- **INIs_Folder**: The %LOCALAPPDATA%\RHI\inis\ directory where user-configured INI templates are stored
- **Detail_Panel**: The right-hand game detail panel built by DetailPanelBuilder
- **Component_Row**: A horizontal row in the detail panel representing a managed component (label, version/status, install button, INI copy button, uninstall button)
- **GameCardViewModel**: The view-model backing each game card and detail panel
- **Manifest**: The manifest.json configuration file containing overrides and game metadata
- **DllOverrideService**: The existing service managing per-game DLL filename overrides for ReShade and Display Commander
- **AuxInstalledRecord**: The per-game tracking record for installed auxiliary components
- **NormalizeName**: The existing method that strips diacritics, trademark symbols, and non-alphanumeric characters for fuzzy name matching
- **Supported_DLL_Names**: The set of filenames OptiScaler.dll can be renamed to: dxgi.dll, winmm.dll, d3d12.dll, dbghelp.dll, version.dll, wininet.dll, winhttp.dll
- **ReShade64_DLL**: The filename ReShade must use when OptiScaler is installed (ReShade64.dll), loaded by OptiScaler via LoadReshade=true
- **First_Time_Warning**: A one-time dialog shown before the first OptiScaler install explaining its purpose and single-player-only recommendation

## Requirements

### Requirement 1: Download and Stage OptiScaler Release

**User Story:** As a user, I want RHI to download and cache the OptiScaler release archive, so that installs are fast and do not require repeated downloads.

#### Acceptance Criteria

1. WHEN RHI starts and no OptiScaler staging exists, THE OptiScaler_Service SHALL download the latest OptiScaler release archive from the OptiScaler GitHub releases page
2. WHEN the download succeeds, THE OptiScaler_Service SHALL extract the archive contents to the Staging_Folder at %LOCALAPPDATA%\RHI\optiscaler\
3. WHEN the download succeeds, THE OptiScaler_Service SHALL record the downloaded version tag in the Staging_Folder
4. WHEN the download fails, THE OptiScaler_Service SHALL log the failure via CrashReporter.Log and continue without OptiScaler availability
5. WHILE a valid staging exists in the Staging_Folder, THE OptiScaler_Service SHALL skip the download on subsequent startups
6. WHEN the user clears the downloads cache from Settings, THE OptiScaler_Service SHALL remove the Staging_Folder contents

### Requirement 2: Check for OptiScaler Updates

**User Story:** As a user, I want RHI to detect when a new OptiScaler version is available, so that I can keep OptiScaler up to date.

#### Acceptance Criteria

1. WHEN RHI starts, THE OptiScaler_Service SHALL check the OptiScaler GitHub releases API for the latest release tag
2. WHEN the latest release tag differs from the cached version tag in the Staging_Folder, THE OptiScaler_Service SHALL mark OptiScaler as having an update available
3. WHEN an update is available and the user triggers Update All, THE OptiScaler_Service SHALL download and stage the new release, then update all games that have OptiScaler installed
4. WHEN updating an installed game, THE OptiScaler_Service SHALL replace the renamed OptiScaler DLL and all Companion_Files in the game folder
5. WHEN updating an installed game, THE OptiScaler_Service SHALL preserve the existing OptiScaler.ini in the game folder
6. WHEN a game has the per-game OptiScaler update exclusion toggle enabled, THE OptiScaler_Service SHALL skip that game during Update All

### Requirement 3: Install OptiScaler to a Game Folder

**User Story:** As a user, I want to install OptiScaler to a game with a single click, so that I can enable upscaler redirection without manual file management.

#### Acceptance Criteria

1. WHEN the user clicks the OptiScaler install button for the first time ever, THE OptiScaler_Service SHALL display the First_Time_Warning dialog with "I Understand" and "Cancel" buttons
2. WHEN the user clicks "Cancel" on the First_Time_Warning dialog, THE OptiScaler_Service SHALL abort the install
3. WHEN the user clicks "I Understand" on the First_Time_Warning dialog, THE OptiScaler_Service SHALL record the acknowledgement and not show the dialog again on future installs
4. WHEN the install proceeds, THE OptiScaler_Service SHALL copy OptiScaler.dll from the Staging_Folder to the game folder, renamed to the effective OptiScaler DLL name (default: dxgi.dll, or the user-chosen override name)
5. WHEN the install proceeds, THE OptiScaler_Service SHALL copy OptiScaler.ini to the game folder, preserving the exact filename OptiScaler.ini
6. WHEN the install proceeds, THE OptiScaler_Service SHALL copy all Companion_Files (fakenvapi.dll, dlssg_to_fsr3.dll, FFX SDK DLLs) to the game folder
7. WHEN the install proceeds, THE OptiScaler_Service SHALL set LoadReshade=true in the deployed OptiScaler.ini regardless of whether ReShade is currently installed
8. WHEN a user-configured OptiScaler.ini exists in the INIs_Folder, THE OptiScaler_Service SHALL copy that INI to the game folder instead of the default one
9. WHEN no user-configured OptiScaler.ini exists in the INIs_Folder on first install, THE OptiScaler_Service SHALL seed a default OptiScaler.ini into the INIs_Folder
10. WHEN the user INI is copied from the INIs_Folder, THE OptiScaler_Service SHALL ensure LoadReshade=true is set in the deployed copy

### Requirement 4: ReShade Coexistence During OptiScaler Install

**User Story:** As a user, I want OptiScaler and ReShade to coexist correctly, so that both tools work together without manual file renaming.

#### Acceptance Criteria

1. WHEN OptiScaler is being installed and ReShade is already installed in the game folder (as dxgi.dll or any other DLL name), THE OptiScaler_Service SHALL rename the ReShade DLL to ReShade64.dll
2. WHEN OptiScaler is being installed and Luma mode is active with its own dxgi.dll for ReShade, THE OptiScaler_Service SHALL rename that ReShade DLL to ReShade64.dll
3. WHILE OptiScaler is installed in a game folder, THE DllOverrideService SHALL disable the ReShade DLL naming override dropdown for that game
4. WHILE OptiScaler is installed in a game folder and the ReShade DLL naming override dropdown is disabled, THE Detail_Panel SHALL display a tooltip explaining that OptiScaler controls the ReShade DLL name
5. WHEN ReShade is installed on a game that already has OptiScaler, THE ReShade install SHALL deploy the DLL as ReShade64.dll instead of the normal DLL name
6. WHEN ReShade is reinstalled on a game that already has OptiScaler, THE ReShade install SHALL deploy the DLL as ReShade64.dll
7. WHEN Update All updates ReShade on a game that has OptiScaler installed, THE ReShade update SHALL deploy the DLL as ReShade64.dll

### Requirement 5: Uninstall OptiScaler from a Game Folder

**User Story:** As a user, I want to cleanly remove OptiScaler from a game, so that the game folder is restored to its pre-OptiScaler state.

#### Acceptance Criteria

1. WHEN the user clicks the OptiScaler uninstall button, THE OptiScaler_Service SHALL remove the renamed OptiScaler DLL (whatever filename it was deployed as) from the game folder
2. WHEN the user clicks the OptiScaler uninstall button, THE OptiScaler_Service SHALL remove OptiScaler.ini from the game folder
3. WHEN the user clicks the OptiScaler uninstall button, THE OptiScaler_Service SHALL remove all Companion_Files (fakenvapi.dll, dlssg_to_fsr3.dll, FFX SDK DLLs) from the game folder
4. WHEN OptiScaler is uninstalled and ReShade64.dll exists in the game folder, THE OptiScaler_Service SHALL rename ReShade64.dll back to the correct ReShade filename using the priority: user DLL override, then manifest override, then auto-detected API name, then dxgi.dll default
5. WHEN OptiScaler is uninstalled, THE OptiScaler_Service SHALL update the per-game tracking record to reflect the removal

### Requirement 6: Per-Game OptiScaler Tracking

**User Story:** As a developer, I want OptiScaler install state persisted per game, so that the app can correctly manage installs, uninstalls, updates, and ReShade coexistence across restarts.

#### Acceptance Criteria

1. THE OptiScaler_Service SHALL persist an AuxInstalledRecord for each game with OptiScaler installed, storing the game name, install path, DLL filename used, and installed version
2. WHEN RHI starts, THE OptiScaler_Service SHALL load all persisted OptiScaler records from disk
3. WHEN OptiScaler is installed to a game, THE OptiScaler_Service SHALL create or update the tracking record for that game
4. WHEN OptiScaler is uninstalled from a game, THE OptiScaler_Service SHALL remove the tracking record for that game
5. THE tracking records SHALL survive application restarts

### Requirement 7: OptiScaler Component Row in Detail Panel

**User Story:** As a user, I want to see OptiScaler status and actions in the detail panel, so that I can manage OptiScaler alongside other components.

#### Acceptance Criteria

1. THE Detail_Panel SHALL display the OptiScaler Component_Row between the RenoDX row and the "Choose one from below" separator
2. THE Detail_Panel SHALL display an "── Optional ──" separator line above the OptiScaler Component_Row
3. THE OptiScaler Component_Row SHALL contain: an OptiScaler label, a version/status indicator, an install button, an INI copy button, and an uninstall button
4. WHEN OptiScaler is installed, THE version/status indicator SHALL display the installed version number
5. WHEN the version number is displayed, THE version number SHALL be clickable and open the OptiScaler GitHub wiki in the default browser
6. WHEN the game is detected as 32-bit, THE OptiScaler Component_Row SHALL be greyed out with reduced opacity and disabled interaction
7. WHEN the game is detected as 32-bit, THE OptiScaler label and status SHALL display strikethrough text decoration

### Requirement 8: OptiScaler GameCardViewModel State

**User Story:** As a developer, I want OptiScaler state tracked on the view-model, so that the UI can bind to install status, progress, and computed properties.

#### Acceptance Criteria

1. THE GameCardViewModel SHALL include observable properties for OptiScaler: OsStatus (GameStatus), OsIsInstalling (bool), OsProgress (double), OsActionMessage (string), OsInstalledFile (string?), OsInstalledVersion (string?)
2. THE GameCardViewModel SHALL expose computed properties: OsStatusDot, OsActionLabel, OsBtnBackground, OsBtnForeground, OsBtnBorderBrush, OsDeleteVisibility, OsStatusText, OsStatusColor, OsShortAction, IsOsInstalled, OsInstallEnabled, OsRowVisibility, OsProgressVisibility, IsOsNotInstalling
3. THE OsInstallEnabled computed property SHALL return false when the game is 32-bit or when OptiScaler is currently installing
4. THE GameCardViewModel SHALL expose OsIniExists computed property that returns true when OptiScaler.ini is present in the INIs_Folder
5. THE GameCardViewModel SHALL include card grid properties: CardOsStatusDot, CardOsInstallEnabled
6. THE GameCardViewModel SHALL notify all OptiScaler computed properties when OsStatus or OsIsInstalling changes

### Requirement 9: OptiScaler DLL Naming Overrides

**User Story:** As a user, I want to choose which DLL filename OptiScaler uses, so that I can avoid conflicts with other tools in the game folder.

#### Acceptance Criteria

1. THE DllOverrideService SHALL support a third dropdown in the DLL naming overrides section labeled "Select OptiScaler DLL name"
2. THE OptiScaler DLL name dropdown SHALL offer the Supported_DLL_Names: dxgi.dll, winmm.dll, d3d12.dll, dbghelp.dll, version.dll, wininet.dll, winhttp.dll
3. THE OptiScaler DLL name dropdown SHALL filter out filenames currently used by ReShade or Display Commander for the same game to prevent conflicts
4. WHEN the user selects a different OptiScaler DLL name while OptiScaler is installed, THE DllOverrideService SHALL rename the existing OptiScaler DLL in the game folder to the new filename
5. THE Manifest SHALL support an optiScalerDllOverrides field containing a dictionary of game name to OptiScaler DLL filename string pairs for per-game defaults
6. WHEN a manifest optiScalerDllOverrides entry exists for a game, THE DllOverrideService SHALL use the manifest-specified DLL name as the default for that game

### Requirement 10: Detect Existing OptiScaler Installations

**User Story:** As a user, I want RHI to detect OptiScaler already installed in game folders, so that the status display is accurate without requiring reinstallation through RHI.

#### Acceptance Criteria

1. WHEN scanning a game folder, THE OptiScaler_Service SHALL check DLLs in the folder for OptiScaler binary signatures (unique string markers that distinguish OptiScaler from ReShade and other DLLs)
2. WHEN OptiScaler.ini is present in the game folder, THE OptiScaler_Service SHALL treat the INI as a reliable secondary marker for OptiScaler presence
3. WHEN an existing OptiScaler installation is detected, THE OptiScaler_Service SHALL create a tracking record and display the correct status on the GameCardViewModel
4. WHEN the foreign DLL protection system scans a game folder, THE foreign DLL protection SHALL recognise OptiScaler DLLs and not flag them as foreign

### Requirement 11: OptiScaler Hotkey Configuration

**User Story:** As a user, I want to configure the OptiScaler overlay toggle hotkey from the Settings page, so that I can avoid key conflicts with other tools.

#### Acceptance Criteria

1. THE Settings page SHALL include an "OptiScaler overlay toggle key" section alongside the existing ReShade and ReLimiter hotkey sections
2. THE OptiScaler hotkey section SHALL default to the Insert key
3. WHEN the user sets a new hotkey, THE SettingsHandler SHALL write the ShortcutKey= value to the OptiScaler.ini in the INIs_Folder
4. WHEN the user clicks "Apply to All Games", THE SettingsHandler SHALL find all OptiScaler.ini files in game folders where OptiScaler is installed and update the ShortcutKey= value in each

### Requirement 12: Refresh and Scan Behaviour

**User Story:** As a user, I want the Refresh action to re-detect OptiScaler status per game, so that manual changes to game folders are reflected in the UI.

#### Acceptance Criteria

1. WHEN the user triggers a Refresh, THE OptiScaler_Service SHALL re-detect OptiScaler installation status for each game
2. WHEN OptiScaler was manually deleted from a game folder, THE OptiScaler_Service SHALL detect the removal and update the tracking record
3. WHEN OptiScaler was manually deleted and ReShade64.dll exists in the game folder, THE OptiScaler_Service SHALL rename ReShade64.dll back to the correct ReShade filename

### Requirement 13: Game Report Integration

**User Story:** As a user, I want the Copy Report diagnostic to include OptiScaler status, so that support requests contain complete component information.

#### Acceptance Criteria

1. WHEN the user copies a game report, THE GameReportEncoder SHALL include OptiScaler status in the components list: installed/not installed, version, and DLL filename used

### Requirement 14: Bottom Warning Text

**User Story:** As a user, I want the single-player warning to mention OptiScaler, so that I understand the recommendation applies to all injected components.

#### Acceptance Criteria

1. THE single-player warning text at the bottom of the application SHALL mention OptiScaler alongside ReShade

### Requirement 15: Skeleton Loading Screen

**User Story:** As a user, I want the skeleton loading screen to include an OptiScaler row placeholder, so that the loading state accurately previews the final layout.

#### Acceptance Criteria

1. THE skeleton loading screen SHALL include a placeholder row for the OptiScaler component in the correct position (between RenoDX and the separator)

### Requirement 16: Grid View Card Flyout

**User Story:** As a user, I want OptiScaler actions available in the card flyout for compact/grid view, so that I can manage OptiScaler without switching to the detail panel.

#### Acceptance Criteria

1. THE card flyout in grid/compact view SHALL include an OptiScaler component row with install, INI copy, and uninstall actions
2. WHEN the game is 32-bit, THE OptiScaler row in the card flyout SHALL be greyed out and disabled

### Requirement 17: Update All Integration

**User Story:** As a user, I want OptiScaler included in the Update All action, so that all components are updated together.

#### Acceptance Criteria

1. WHEN the user triggers Update All, THE UpdateOrchestrationService SHALL include OptiScaler in the update sweep for all games that have OptiScaler installed
2. WHEN updating OptiScaler during Update All, THE OptiScaler_Service SHALL follow the same update rules as Requirement 2 (replace DLL and companions, preserve INI)
3. THE overrides panel SHALL include a per-game OptiScaler update exclusion toggle alongside the existing RS, RDX, UL, DC toggles

### Requirement 18: OptiScaler INI Copy Button

**User Story:** As a user, I want to copy my configured OptiScaler.ini to a game folder, so that I can apply my preferred settings without editing files manually.

#### Acceptance Criteria

1. WHEN the user clicks the INI copy button on the OptiScaler Component_Row, THE OptiScaler_Service SHALL copy OptiScaler.ini from the INIs_Folder to the game folder, overwriting the existing one
2. WHEN the INI is copied, THE OptiScaler_Service SHALL ensure LoadReshade=true is set in the deployed copy
3. WHEN no OptiScaler.ini exists in the INIs_Folder, THE INI copy button SHALL be visually dimmed with reduced opacity and disabled

### Requirement 19: Manifest OptiScaler DLL Override Support

**User Story:** As a maintainer, I want to specify per-game OptiScaler DLL filenames in the manifest, so that games known to require a specific proxy DLL name work correctly by default.

#### Acceptance Criteria

1. THE Manifest SHALL support an optiScalerDllOverrides field containing a dictionary of game name to DLL filename string pairs
2. WHEN the optiScalerDllOverrides field is absent from the Manifest, THE OptiScaler_Service SHALL treat the overrides as an empty dictionary
3. WHEN an optiScalerDllOverrides entry exists for a detected game, THE OptiScaler_Service SHALL use the specified DLL filename as the default OptiScaler DLL name for that game

### Requirement 20: Documentation Updates

**User Story:** As a user, I want the documentation to describe OptiScaler integration, so that I can understand how to use it.

#### Acceptance Criteria

1. THE README.md SHALL include OptiScaler in the feature highlights and third-party components table
2. THE README_NexusMods.bbcode SHALL include OptiScaler in the features list and third-party components section
3. THE docs/DETAILED_GUIDE.md SHALL include a comprehensive OptiScaler section covering installation, configuration, ReShade coexistence, DLL naming, and hotkey setup
4. THE RHI_PatchNotes.md SHALL include OptiScaler integration in the patch notes for the release version

### Requirement 21: OptiScaler Availability Rules

**User Story:** As a user, I want OptiScaler shown as available on all 64-bit games regardless of upscaler detection, so that I can use it wherever it might help.

#### Acceptance Criteria

1. THE OptiScaler Component_Row SHALL be displayed for all detected games regardless of upscaler detection results
2. WHEN the game is 64-bit, THE OptiScaler install button SHALL be enabled (subject to other constraints like not currently installing)
3. WHEN the game is 32-bit, THE OptiScaler install button SHALL be disabled with the entire row greyed out
4. THE OptiScaler_Service SHALL NOT query PCGW or any external source for upscaler information to determine OptiScaler availability
