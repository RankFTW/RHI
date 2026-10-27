# Requirements Document: DXVK Integration for RHI

## Introduction

DXVK is an open-source translation layer that converts DirectX 8/9/10/11 API calls into Vulkan. This feature integrates DXVK into RHI (ReShade HDR Installer) as a managed per-game component, enabling users to translate older DirectX games to Vulkan for ReShade compute shader support, improved performance, reduced shader stutter, and HDR output. The integration covers the full lifecycle: staging (download and cache), per-game install/uninstall, ReShade mode switching, OptiScaler coexistence, dxvk.conf configuration management, binary signature detection, update detection, UI controls, safety guards, and manifest-driven overrides. DXVK touches nearly every subsystem in RHI and follows the established patterns used by OptiScaler, ReShade, ReLimiter, and other managed components.

## Glossary

- **RHI**: ReShade HDR Installer — the WinUI 3 desktop application (.NET 8, Windows 10/11 x64) that manages HDR mods across PC game libraries.
- **DXVK**: An open-source DirectX 8/9/10/11 to Vulkan translation layer (github.com/doitsujin/dxvk).
- **DXVK_Service**: The RHI service responsible for DXVK staging, install, uninstall, update detection, and configuration management.
- **Staging_Cache**: The local directory `%LocalAppData%\RHI\dxvk\` where downloaded DXVK release archives are extracted and cached.
- **Game_Directory**: The resolved install path for a game's executable, as determined by RHI's engine and path detection logic.
- **DxvkInstalledRecord**: The per-game tracking record that persists DXVK install state (version, deployed DLLs, backups, conf deployment, OptiScaler plugin placement).
- **OptiScaler**: A DLL-based upscaler redirection tool managed by RHI that can conflict with DXVK when both need the same DLL filename (dxgi.dll).
- **OptiScaler_Plugins_Folder**: The `<Game_Directory>\OptiScaler\plugins\` subfolder where OptiScaler automatically loads same-filename proxy DLLs.
- **dxvk.conf**: DXVK's per-game configuration file placed in the Game_Directory.
- **Vulkan_Layer**: The ReShade Vulkan implicit layer installed via the Windows registry (`HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers`), used when a game presents as Vulkan.
- **DX_Proxy**: A ReShade installation method where ReShade is deployed as a DirectX proxy DLL (e.g., dxgi.dll, d3d9.dll) in the Game_Directory.
- **Binary_Signature_Scanner**: RHI's existing mechanism that reads the first ~2 MB of a DLL to identify known tools (ReShade, OptiScaler) by embedded string patterns.
- **Foreign_DLL_Protection**: RHI's system that detects unrecognised DLLs in game directories and warns users before overwriting them.
- **Anti_Cheat_Blacklist**: A manifest-driven list of games where DXVK installation is blocked because anti-cheat software would reject or ban the modified DLLs.
- **GameCardViewModel**: The per-game view model that holds all component states, install statuses, and UI-bound properties for a single game card.
- **MergeCards**: The method that copies fresh card data into existing displayed cards during background scan refresh without rebuilding the UI.
- **Manifest**: The remote JSON configuration file (`manifest.json`) that provides per-game overrides, blacklists, notes, and classification data.
- **Update_All**: The batch operation that updates all installed components (ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler) across all games in a single action.
- **gplasync**: The dxvk-gplasync variant — a community fork that adds additional async pipeline compilation beyond standard DXVK's built-in graphics pipeline library feature.
- **Variant_Selector**: A Settings page control that lets advanced users choose between standard DXVK and the gplasync variant.
- **Original_Backup**: A copy of a game's original DLL file saved with a `.original` extension before RHI replaces it with a managed DLL.

## Requirements


### Requirement 1: DXVK Staging and Download

**User Story:** As a user, I want RHI to automatically download and cache DXVK releases from GitHub, so that DXVK DLLs are available for per-game installation without manual downloads.

#### Acceptance Criteria

1. WHEN RHI starts and the Staging_Cache does not contain a valid DXVK release, THE DXVK_Service SHALL download the latest release archive from the GitHub Releases API (`https://api.github.com/repos/doitsujin/dxvk/releases/latest`) and extract it to the Staging_Cache directory (`%LocalAppData%\RHI\dxvk\`).
2. WHEN the DXVK release archive has been downloaded, THE DXVK_Service SHALL extract the `.tar.gz` archive using the bundled 7-Zip executable and preserve the `x64/` and `x32/` subdirectory structure containing the DXVK DLLs.
3. WHEN extraction completes successfully, THE DXVK_Service SHALL write the release version tag to a `version.txt` file in the Staging_Cache directory.
4. THE DXVK_Service SHALL expose an `IsStagingReady` property that returns true only when the Staging_Cache directory exists, the `version.txt` file exists, and at least one DXVK DLL (`d3d9.dll`) is present in the `x64/` subdirectory.
5. THE DXVK_Service SHALL expose a `StagedVersion` property that reads and returns the version string from the `version.txt` file in the Staging_Cache, or null if the file does not exist or cannot be read.
6. IF the GitHub API request fails due to network error or rate limiting, THEN THE DXVK_Service SHALL log the failure and continue without blocking RHI startup or other component staging.
7. WHEN the Staging_Cache already contains a valid DXVK release, THE DXVK_Service SHALL skip the download and use the cached files.
8. THE DXVK_Service SHALL use the existing `GitHubETagCache` for conditional HTTP requests to avoid redundant downloads when the cached version is already current.

### Requirement 2: DXVK Update Detection

**User Story:** As a user, I want RHI to detect when a newer DXVK version is available, so that I can keep DXVK up to date across my games.

#### Acceptance Criteria

1. WHEN RHI performs background update checks, THE DXVK_Service SHALL compare the cached version tag in `version.txt` against the `tag_name` field from the latest GitHub release.
2. WHEN the remote version tag differs from the cached version tag, THE DXVK_Service SHALL set a `HasUpdate` property to true.
3. WHEN `HasUpdate` is true and the user triggers an update (via the DXVK component row or Update All), THE DXVK_Service SHALL download the new release, replace the Staging_Cache contents, update `version.txt`, and re-deploy DXVK DLLs to all games that have DXVK installed.
4. WHEN re-deploying updated DLLs to a game, THE DXVK_Service SHALL update the `DxvkVersion` field in that game's DxvkInstalledRecord.
5. WHEN a game has the per-game DXVK update exclusion override enabled, THE DXVK_Service SHALL skip re-deployment for that game during Update All.

### Requirement 3: Per-Game DXVK Installation

**User Story:** As a user, I want to enable DXVK for individual games with a single toggle, so that RHI handles all DLL deployment, backup, and configuration automatically.

#### Acceptance Criteria

1. WHEN the user enables the DXVK toggle for a game, THE DXVK_Service SHALL determine the correct DXVK DLLs to deploy based on the game's detected primary `GraphicsApiType` (DirectX8, DirectX9, DirectX10, or DirectX11) and the game's bitness (32-bit or 64-bit).
2. WHEN the game's primary API is DirectX8, THE DXVK_Service SHALL deploy `d3d8.dll` from the matching architecture folder (`x32/` for 32-bit, `x64/` for 64-bit).
3. WHEN the game's primary API is DirectX9, THE DXVK_Service SHALL deploy `d3d9.dll` from the matching architecture folder.
4. WHEN the game's primary API is DirectX10, THE DXVK_Service SHALL deploy both `d3d10core.dll` and `dxgi.dll` from the matching architecture folder.
5. WHEN the game's primary API is DirectX11, THE DXVK_Service SHALL deploy both `d3d11.dll` and `dxgi.dll` from the matching architecture folder.
6. WHEN deploying a DXVK DLL to a Game_Directory that already contains a file with the same name, THE DXVK_Service SHALL back up the existing file using the `.original` extension (e.g., `d3d9.dll` becomes `d3d9.dll.original`) before overwriting, following the same backup pattern used by OptiScaler.
7. THE DXVK_Service SHALL only back up files that are genuine game files or other non-RHI-managed files, and SHALL skip backup if the existing file is already a known RHI-managed DLL (ReShade, OptiScaler, or a previous DXVK install).
8. WHEN installation completes, THE DXVK_Service SHALL create a DxvkInstalledRecord containing the game name, install path, DXVK version, list of deployed DLLs, list of backed-up files, conf deployment flag, OptiScaler plugin placement flag, and installation timestamp.
9. THE DXVK_Service SHALL persist the DxvkInstalledRecord to a JSON tracking file in the RHI data directory (`%LocalAppData%\RHI\`).
10. IF the Staging_Cache is not ready when the user enables the DXVK toggle, THEN THE DXVK_Service SHALL download and stage DXVK first, then proceed with installation.
11. THE DXVK_Service SHALL validate that the staged DLL architecture (32-bit or 64-bit) matches the game's bitness before deployment, and SHALL refuse to deploy mismatched DLLs with a descriptive error message.

### Requirement 4: DXVK Uninstallation

**User Story:** As a user, I want to disable DXVK for a game and have RHI cleanly restore the original state, so that the game returns to its pre-DXVK configuration.

#### Acceptance Criteria

1. WHEN the user disables the DXVK toggle for a game, THE DXVK_Service SHALL delete all DXVK DLLs listed in the game's DxvkInstalledRecord from both the Game_Directory root and the OptiScaler_Plugins_Folder (if any DLLs were placed there).
2. WHEN DXVK DLLs have been removed, THE DXVK_Service SHALL restore all `.original` backup files listed in the DxvkInstalledRecord by removing the `.original` extension.
3. WHEN the DxvkInstalledRecord contains entries in `PluginFolderDlls`, THE DXVK_Service SHALL delete those DLLs from the OptiScaler_Plugins_Folder.
4. WHEN the DxvkInstalledRecord indicates that RHI deployed `dxvk.conf`, THE DXVK_Service SHALL delete `dxvk.conf` from the Game_Directory.
5. WHEN uninstallation completes, THE DXVK_Service SHALL remove the game's DxvkInstalledRecord from the tracking file.
6. IF a backup file listed in the DxvkInstalledRecord no longer exists on disk, THEN THE DXVK_Service SHALL log a warning and continue with the remaining uninstallation steps without failing.

### Requirement 5: ReShade Mode Switching

**User Story:** As a user, I want RHI to automatically switch ReShade between DX proxy mode and Vulkan layer mode when I toggle DXVK, so that ReShade continues to work correctly with the translated API.

#### Acceptance Criteria

1. WHEN DXVK is enabled for a game that has ReShade installed as a DX_Proxy, THE DXVK_Service SHALL coordinate with the ReShade install service to uninstall the DX_Proxy ReShade and reinstall ReShade via the Vulkan_Layer path.
2. WHEN switching ReShade to Vulkan_Layer mode, THE DXVK_Service SHALL ensure that `reshade.vulkan.ini` is deployed with Vulkan-tuned depth buffer settings and the `RDXC_VULKAN_FOOTPRINT` marker is placed in the Game_Directory.
3. WHEN DXVK is disabled for a game that has ReShade installed via the Vulkan_Layer, THE DXVK_Service SHALL coordinate with the ReShade install service to remove the Vulkan footprint and `reshade.vulkan.ini`, and reinstall ReShade as a DX_Proxy using the game's original API.
4. WHEN the game is a native Vulkan game or a dual-API game already using the Vulkan rendering path, THE DXVK_Service SHALL skip ReShade mode switching because ReShade is already in Vulkan_Layer mode.
5. IF ReShade is not installed for the game when DXVK is enabled, THEN THE DXVK_Service SHALL not install ReShade automatically, but SHALL record that any future ReShade install for this game must use the Vulkan_Layer path.

### Requirement 6: OptiScaler Coexistence

**User Story:** As a user, I want DXVK and OptiScaler to work together on any game where both are installed, so that I get both Vulkan translation and upscaler redirection without manual configuration.

#### Acceptance Criteria

1. WHEN DXVK is being installed for any game that already has OptiScaler installed, AND any DXVK DLL filename conflicts with OptiScaler's installed DLL filename (typically `dxgi.dll`), THE DXVK_Service SHALL place the conflicting DXVK DLL in the OptiScaler_Plugins_Folder (`<Game_Directory>\OptiScaler\plugins\<filename>`) instead of the Game_Directory root. Non-conflicting DXVK DLLs (e.g. `d3d9.dll` when OptiScaler is `dxgi.dll`) SHALL be deployed to the Game_Directory root as normal.
2. WHEN any DXVK DLL is placed in the OptiScaler_Plugins_Folder, THE DXVK_Service SHALL set the `InOptiScalerPlugins` flag to true in the DxvkInstalledRecord and record which specific DLLs were placed in the plugins folder.
3. WHEN OptiScaler is installed after DXVK on a game where DXVK already has a DLL in the Game_Directory root that conflicts with OptiScaler's target filename, THE OptiScaler install flow SHALL move the conflicting DXVK DLL to the OptiScaler_Plugins_Folder and update the DxvkInstalledRecord accordingly.
4. WHEN OptiScaler is uninstalled from a game where DXVK DLLs are in the OptiScaler_Plugins_Folder, THE OptiScaler uninstall flow SHALL move those DXVK DLLs back to the Game_Directory root and update the DxvkInstalledRecord.
5. WHEN DXVK is installed for a DX8 or DX9 game that has OptiScaler installed, THE DXVK_Service SHALL check whether any DXVK DLL filenames conflict with OptiScaler's installed filename. If no conflict exists (e.g. DXVK deploys `d3d9.dll` while OptiScaler is `dxgi.dll`), DXVK DLLs SHALL be deployed to the Game_Directory root. If a conflict exists, the conflicting DLL SHALL be placed in the OptiScaler_Plugins_Folder.
6. WHEN DXVK is installed for a DX10 or DX11 game that has OptiScaler installed as `dxgi.dll`, THE DXVK_Service SHALL place DXVK's `dxgi.dll` in the OptiScaler_Plugins_Folder and deploy any other required DXVK DLLs (e.g. `d3d11.dll`, `d3d10core.dll`) to the Game_Directory root.
7. THE DxvkInstalledRecord SHALL track which DLLs were placed in the OptiScaler_Plugins_Folder separately from those placed in the Game_Directory root, so that uninstall can correctly remove DLLs from both locations.

### Requirement 7: dxvk.conf Configuration Management

**User Story:** As a user, I want RHI to deploy a sensible default DXVK configuration file and let me customise it, so that DXVK works well out of the box with options for tuning.

#### Acceptance Criteria

1. WHEN DXVK is installed for a game and no `dxvk.conf` file exists in the Game_Directory, THE DXVK_Service SHALL deploy a default `dxvk.conf` containing: `dxgi.enableHDR = True`, `dxvk.allowFse = False`, `dxvk.latencySleep = Auto`, and `d3d9.dpiAware = True`.
2. WHEN the default `dxvk.conf` is deployed, THE DXVK_Service SHALL set the `DeployedConf` flag to true in the DxvkInstalledRecord.
3. WHEN a `dxvk.conf` file already exists in the Game_Directory (placed by the user or a previous install), THE DXVK_Service SHALL preserve the existing file and not overwrite it.
4. THE DXVK_Service SHALL provide a method to copy a user-placed `dxvk.conf` template from the RHI INI presets directory (`%LocalAppData%\RHI\inis\`) to a game's directory, following the same pattern as the OptiScaler INI copy button.
5. WHEN the user clicks the dxvk.conf copy button on the DXVK component row, THE DXVK_Service SHALL copy the template `dxvk.conf` from the INI presets directory to the Game_Directory, overwriting any existing `dxvk.conf` only after user confirmation.

### Requirement 8: Binary Signature Detection

**User Story:** As a user, I want RHI to detect existing DXVK installations in game directories, so that foreign DLL protection does not flag them as unknown and so that RHI can display accurate install status.

#### Acceptance Criteria

1. THE Binary_Signature_Scanner SHALL include DXVK-specific byte signatures: the ASCII strings `"dxvk"` and `"DXVK_"` scanned within the first 2 MB of DLL files.
2. WHEN the Binary_Signature_Scanner detects a DXVK signature in a DLL file in a Game_Directory, THE Foreign_DLL_Protection system SHALL classify that DLL as a known DXVK DLL and not flag it as an unknown foreign DLL.
3. WHEN a game has DXVK DLLs detected via binary signature but no DxvkInstalledRecord exists, THE DXVK_Service SHALL report the game's DXVK status as "Detected (unmanaged)" to distinguish it from RHI-managed installs.
4. WHEN the user enables the DXVK toggle for a game that already has unmanaged DXVK DLLs detected, THE DXVK_Service SHALL offer to adopt the existing installation by creating a DxvkInstalledRecord without re-deploying DLLs, or to overwrite with the RHI-managed version.

### Requirement 9: Safety Guards

**User Story:** As a user, I want RHI to prevent DXVK installation in situations where it would cause problems, so that I do not accidentally break games or trigger anti-cheat bans.

#### Acceptance Criteria

1. WHEN a game is on the Anti_Cheat_Blacklist in the Manifest, THE GameCardViewModel SHALL disable the DXVK toggle and display a tooltip explaining that DXVK is blocked for this game due to anti-cheat software.
2. WHEN a game's primary `GraphicsApiType` is DirectX12, Vulkan, or OpenGL, THE GameCardViewModel SHALL hide the DXVK toggle because DXVK does not translate those APIs.
3. WHEN the user enables DXVK for the first time in the current RHI session, THE DXVK_Service SHALL display a warning dialog explaining: DXVK is an unofficial Windows translation layer, anti-cheat games may ban players, game overlays (Steam, Nvidia, RTSS) may conflict, and borderless fullscreen is recommended over exclusive fullscreen.
4. WHEN the user acknowledges the first-time warning, THE DXVK_Service SHALL set a `FirstTimeWarningAcknowledged` flag and not show the warning again during the same session.
5. THE DXVK_Service SHALL never deploy DXVK DLLs to any path under `%SystemRoot%\System32` or `%SystemRoot%\SysWOW64`.
6. THE DXVK_Service SHALL validate that the target deployment path is within a game directory and not a system directory before writing any files.
7. WHEN a game's `GraphicsApiType` is Unknown and no API override exists in the Manifest, THE GameCardViewModel SHALL disable the DXVK toggle with a tooltip explaining that the game's DirectX version could not be determined.

### Requirement 10: UI — Per-Game DXVK Toggle

**User Story:** As a user, I want a per-game toggle in the overrides panel to enable or disable DXVK, so that I have granular control over which games use the Vulkan translation layer.

#### Acceptance Criteria

1. THE GameCardViewModel SHALL expose a `DxvkEnabled` boolean property that represents whether DXVK is enabled for the game.
2. WHEN the user toggles `DxvkEnabled` to true, THE MainViewModel SHALL invoke the DXVK install flow (Requirement 3) for that game, and THE DXVK component row SHALL become visible in the Components section.
3. WHEN the user toggles `DxvkEnabled` to false, THE MainViewModel SHALL invoke the DXVK uninstall flow (Requirement 4) for that game, and THE DXVK component row SHALL be hidden from the Components section.
4. WHILE DXVK installation or uninstallation is in progress, THE GameCardViewModel SHALL disable the DXVK toggle to prevent concurrent operations.
5. THE DXVK toggle SHALL be visible in the Overrides panel only for games whose primary `GraphicsApiType` is DirectX8, DirectX9, DirectX10, or DirectX11.

### Requirement 11: UI — DXVK Component Row

**User Story:** As a user, I want a DXVK component row in the game detail view showing install status, version, and action buttons once DXVK is enabled, so that I can see and manage DXVK state at a glance without cluttering the view for games where I don't use it.

#### Acceptance Criteria

1. THE GameCardViewModel SHALL expose DXVK component state properties following the existing component pattern: `DxvkStatus` (GameStatus), `DxvkIsInstalling` (bool), `DxvkProgress` (double), `DxvkActionMessage` (string), `DxvkInstalledVersion` (string).
2. THE DXVK component row SHALL only be visible in the Components section when `DxvkEnabled` is true for the game. When `DxvkEnabled` is false, the row SHALL be hidden entirely.
3. WHEN DXVK is installed for a game, THE DXVK component row SHALL display the installed DXVK version number and an "Installed" status indicator.
4. WHEN DXVK is enabled but installation is still in progress, THE DXVK component row SHALL display an "Installing…" status with a progress indicator.
5. THE DXVK component row SHALL include an Info button that displays a dialog explaining what DXVK does, game-specific notes from the Manifest, and known issues for the game's API version.
6. THE DXVK component row SHALL include a dxvk.conf copy button (clipboard icon) that deploys or copies the dxvk.conf template to the Game_Directory per Requirement 7.
7. WHEN a DXVK update is available, THE DXVK component row SHALL display an update badge consistent with other component update indicators, with a Reinstall/Update button.
8. THE DXVK component row SHALL appear below the "── Optional ──" separator, after the OptiScaler row.

### Requirement 12: UI — Settings Page Variant Selector

**User Story:** As an advanced user, I want to choose between standard DXVK and the gplasync variant in the Settings page, so that I can use the variant that best suits my needs.

#### Acceptance Criteria

1. THE Settings page SHALL include a DXVK variant selector with two options: "Standard DXVK" (default, recommended) and "dxvk-gplasync".
2. WHEN the user selects a different DXVK variant, THE DXVK_Service SHALL clear the Staging_Cache and re-download the selected variant's release from its respective GitHub repository.
3. WHEN the variant is changed and games have DXVK installed, THE DXVK_Service SHALL prompt the user to re-deploy the new variant's DLLs to all installed games or keep the current version until the next update.
4. THE Settings page SHALL persist the selected variant preference across RHI sessions.

### Requirement 13: GameCardViewModel State Properties

**User Story:** As a developer, I want DXVK state properties on GameCardViewModel following the established component pattern, so that the UI can bind to DXVK state consistently with other components.

#### Acceptance Criteria

1. THE GameCardViewModel SHALL include the following observable properties for DXVK state: `DxvkStatus` (GameStatus, default NotInstalled), `DxvkIsInstalling` (bool), `DxvkProgress` (double), `DxvkActionMessage` (string), `DxvkInstalledVersion` (string nullable).
2. THE GameCardViewModel SHALL include a `DxvkEnabled` observable boolean property for the per-game override toggle.
3. THE GameCardViewModel SHALL include an `ExcludeFromUpdateAllDxvk` boolean property for per-game update exclusion.
4. THE GameCardViewModel SHALL include a `DxvkRecord` property of type `DxvkInstalledRecord` for tracking the install record.
5. THE `NotifyAll` method SHALL include notification calls for all DXVK-dependent computed properties (status dot, action label, button styles, row visibility, card status dot, install enabled).

### Requirement 14: MergeCards Integration

**User Story:** As a developer, I want DXVK fields to be correctly copied during the MergeCards background scan merge, so that DXVK state is preserved when the UI refreshes without a full rebuild.

#### Acceptance Criteria

1. WHEN MergeCards updates an existing card from a fresh card, THE MergeCards method SHALL copy all DXVK-related properties: `DxvkStatus`, `DxvkInstalledVersion`, `DxvkRecord`, `DxvkEnabled`, and `ExcludeFromUpdateAllDxvk`.
2. WHEN a new game card is added during MergeCards, THE new card SHALL include DXVK state populated from the DxvkInstalledRecord tracking file and the game's API detection results.

### Requirement 15: Cache Phase Integration

**User Story:** As a user, I want to see DXVK install status immediately when RHI launches from cache, so that I do not have to wait for the background scan to see which games have DXVK installed.

#### Acceptance Criteria

1. WHEN RHI loads cached cards during the instant-launch cache phase, THE card builder SHALL populate DXVK state (DxvkStatus, DxvkInstalledVersion, DxvkEnabled) from the persisted DxvkInstalledRecord tracking file and the saved game library data.
2. WHEN the background scan completes and MergeCards runs, THE DXVK state on cached cards SHALL be updated with fresh detection results without losing user-set overrides (DxvkEnabled, ExcludeFromUpdateAllDxvk).

### Requirement 16: Update All Integration

**User Story:** As a user, I want DXVK to be included in the Update All batch operation, so that all my managed components stay up to date with a single action.

#### Acceptance Criteria

1. WHEN the user triggers Update All and a DXVK update is available, THE Update All flow SHALL re-stage the latest DXVK release and re-deploy updated DLLs to all games that have DXVK installed.
2. WHEN a game has `ExcludeFromUpdateAllDxvk` set to true, THE Update All flow SHALL skip DXVK re-deployment for that game.
3. WHEN DXVK re-deployment fails for a specific game during Update All, THE Update All flow SHALL log the error, report the failure for that game, and continue processing remaining games.

### Requirement 17: Manifest Integration

**User Story:** As a maintainer, I want to control DXVK behavior per-game via the remote manifest, so that I can block DXVK for problematic games, provide game-specific notes, and override detection results.

#### Acceptance Criteria

1. THE Manifest SHALL support a `dxvkBlacklist` array of game names where the DXVK toggle is blocked, separate from the existing general blacklist.
2. THE Manifest SHALL support a `dxvkGameNotes` object mapping game names to note entries (notes text, notes URL, notes URL label) displayed in the DXVK Info dialog.
3. THE Manifest SHALL support a `dxvkApiOverrides` object mapping game names to DirectX version strings (e.g., "DX9", "DX11") to override auto-detected API for DXVK DLL selection.
4. WHEN the Manifest contains a `dxvkBlacklist` entry for a game, THE GameCardViewModel SHALL disable the DXVK toggle with a tooltip explaining the block reason.
5. WHEN the Manifest contains a `dxvkApiOverrides` entry for a game, THE DXVK_Service SHALL use the overridden API version for DLL selection instead of the auto-detected value.

### Requirement 18: DxvkInstalledRecord Data Model

**User Story:** As a developer, I want a well-defined data model for tracking DXVK installations, so that install, uninstall, update, and detection operations have reliable state to work with.

#### Acceptance Criteria

1. THE DxvkInstalledRecord SHALL contain the following fields: `GameName` (string), `InstallPath` (string), `DxvkVersion` (string), `InstalledDlls` (list of strings — DLLs in Game_Directory root), `PluginFolderDlls` (list of strings — DLLs in OptiScaler_Plugins_Folder), `BackedUpFiles` (list of strings), `DeployedConf` (bool), `InOptiScalerPlugins` (bool), `InstalledAt` (DateTime).
2. THE DXVK_Service SHALL persist all DxvkInstalledRecords to a JSON file at `%LocalAppData%\RHI\dxvk_installed.json`.
3. THE DXVK_Service SHALL load DxvkInstalledRecords on startup and use them to populate DXVK state on game cards.
4. FOR ALL valid DxvkInstalledRecords, serializing to JSON then deserializing SHALL produce an equivalent record (round-trip property).

### Requirement 19: DXVK Service Architecture

**User Story:** As a developer, I want the DXVK service to follow the established service patterns in RHI, so that the codebase remains consistent and maintainable.

#### Acceptance Criteria

1. THE DXVK_Service SHALL be implemented as a class `DxvkService` implementing an `IDxvkService` interface, registered in the DI container.
2. THE DXVK_Service SHALL accept `HttpClient`, `IAuxInstallService`, `IDllOverrideService`, and `GitHubETagCache` as constructor-injected dependencies, following the OptiScalerService pattern.
3. THE DXVK_Service SHALL use the existing `CrashReporter` for all error logging, following the `[ClassName.MethodName]` log prefix convention.
4. THE DXVK_Service SHALL expose staging, install, uninstall, update check, and configuration methods as async Task-returning methods with `IProgress<(string msg, double pct)>` parameters for UI progress reporting.

### Requirement 20: Saved Game Library Persistence

**User Story:** As a developer, I want DXVK state to be included in the saved game library, so that DXVK install status and user overrides survive RHI restarts.

#### Acceptance Criteria

1. THE SavedGameLibrary model SHALL include DXVK-related fields for each saved game entry: `DxvkEnabled` (bool), `DxvkInstalledVersion` (string nullable), `ExcludeFromUpdateAllDxvk` (bool).
2. WHEN the game library is saved to disk, THE save logic SHALL include all DXVK state fields for each game.
3. WHEN the game library is loaded from disk, THE load logic SHALL restore DXVK state fields and apply them to the corresponding game cards.

### Requirement 21: DXVK DLL Deployment Path Safety

**User Story:** As a user, I want RHI to deploy DXVK DLLs only to the correct game directory, so that system files are never modified and DLLs go to the right location.

#### Acceptance Criteria

1. THE DXVK_Service SHALL use the same resolved install path logic as other components (engine detection + install path overrides from the Manifest) to determine the Game_Directory for DLL deployment.
2. THE DXVK_Service SHALL verify that the resolved Game_Directory is not under `%SystemRoot%`, `%ProgramFiles%\WindowsApps`, or any other protected system path before deploying DLLs.
3. IF the resolved Game_Directory does not exist on disk, THEN THE DXVK_Service SHALL report an error and refuse to deploy DLLs.
4. THE DXVK_Service SHALL deploy DXVK DLLs to the same directory where the game's primary executable resides, consistent with where ReShade and OptiScaler DLLs are deployed.

### Requirement 22: Concurrent Operation Safety

**User Story:** As a user, I want DXVK operations to be safe when multiple actions happen simultaneously, so that file corruption and race conditions are prevented.

#### Acceptance Criteria

1. WHILE a DXVK install or uninstall operation is in progress for a game, THE GameCardViewModel SHALL disable all DXVK-related controls (toggle, update button, conf copy button) for that game.
2. WHILE a DXVK staging download is in progress, THE DXVK_Service SHALL queue subsequent install requests and process them after staging completes, rather than starting parallel downloads.
3. THE DXVK_Service SHALL use file-level locking or sequential processing to prevent concurrent writes to the DxvkInstalledRecord tracking file.

### Requirement 23: Error Handling and Recovery

**User Story:** As a user, I want DXVK operations to handle errors gracefully and provide clear feedback, so that I understand what went wrong and can recover.

#### Acceptance Criteria

1. IF a DXVK DLL file copy fails due to the file being locked by a running game, THEN THE DXVK_Service SHALL display an error message indicating the file is in use and suggest closing the game before retrying.
2. IF the DXVK uninstall process fails to delete a deployed DLL, THEN THE DXVK_Service SHALL log the error, report it to the user, and mark the DxvkInstalledRecord as partially uninstalled.
3. IF the `.original` backup file is missing during uninstall, THEN THE DXVK_Service SHALL log a warning, skip the restore step for that file, and continue with the remaining uninstall steps.
4. IF the DXVK staging download is interrupted, THEN THE DXVK_Service SHALL clean up any partial download files and allow the user to retry.
5. THE DXVK_Service SHALL write crash reports via `CrashReporter.WriteCrashReport` for all unhandled exceptions during install, uninstall, and update operations, including the game name in the report note.