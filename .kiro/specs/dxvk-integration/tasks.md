# Implementation Plan: DXVK Integration for RHI

## Overview

This plan implements DXVK as a managed per-game component in RHI, following the established lifecycle pattern used by OptiScaler, ReShade, ReLimiter, and Display Commander. Implementation proceeds bottom-up: data models and enums first, then the service interface and core logic, then ViewModel integration, then UI wiring, and finally cross-cutting concerns (Update All, MergeCards, cache phase, manifest). Each task builds on the previous ones so there is no orphaned code.

## Tasks

- [x] 1. Create data models and enums
  - [x] 1.1 Create `DxvkVariant` enum in `RenoDXCommander/Models/DxvkVariant.cs`
    - Define `Standard` and `Gplasync` members matching the design
    - _Requirements: 12.1_

  - [x] 1.2 Create `DxvkInstalledRecord` class in `RenoDXCommander/Models/DxvkInstalledRecord.cs`
    - Implement all fields from the design: `GameName`, `InstallPath`, `DxvkVersion`, `InstalledDlls`, `PluginFolderDlls`, `BackedUpFiles`, `DeployedConf`, `InOptiScalerPlugins`, `InstalledAt`
    - Use `System.Text.Json` serialization attributes consistent with existing models (e.g. `AuxInstalledRecord`)
    - _Requirements: 18.1, 18.2, 18.4_

  - [x] 1.3 Write property test for DxvkInstalledRecord serialization round-trip
    - **Property 1: DxvkInstalledRecord serialization round-trip**
    - Generate random records with arbitrary strings, DLL name lists, boolean flags, and DateTime values; serialize to JSON then deserialize and assert all fields match
    - **Validates: Requirements 18.4**

  - [x] 1.4 Add DXVK fields to `RemoteManifest` in `RenoDXCommander/Models/RemoteManifest.cs`
    - Add `DxvkBlacklist` (`List<string>?`), `DxvkGameNotes` (`Dictionary<string, GameNoteEntry>?`), `DxvkApiOverrides` (`Dictionary<string, string>?`) with `[JsonPropertyName]` attributes
    - _Requirements: 17.1, 17.2, 17.3_

  - [x] 1.5 Add DXVK fields to `SavedGameLibrary` persistence
    - Add DXVK state dictionaries to `SavedGameLibrary` (or equivalent saved-game entry pattern): `DxvkEnabled` map, `DxvkInstalledVersion` map, `ExcludeFromUpdateAllDxvk` map — following the same pattern as `HiddenGames`/`FavouriteGames`
    - _Requirements: 20.1, 20.2, 20.3_

- [x] 2. Implement IDxvkService interface and DxvkService core
  - [x] 2.1 Create `IDxvkService` interface in `RenoDXCommander/Services/IDxvkService.cs`
    - Define all members from the design: staging properties (`IsStagingReady`, `HasUpdate`, `StagedVersion`, `FirstTimeWarningAcknowledged`), staging methods (`EnsureStagingAsync`, `CheckForUpdateAsync`, `ClearStaging`), install/uninstall/update methods, `CopyConfToGame`, detection methods (`IsDxvkFile`, `DetectInstallation`), record methods (`LoadAllRecords`, `FindRecord`), and `SelectedVariant` property
    - _Requirements: 19.1, 19.4_

  - [x] 2.2 Create `DxvkService.cs` with constructor, constants, and DI wiring
    - Implement the class skeleton in `RenoDXCommander/Services/DxvkService.cs` as a partial class
    - Define constants: `StagingDir`, `VersionFilePath`, `DbPath`, `ConfTemplatePath`, GitHub API URLs for both variants, binary signature byte arrays
    - Inject `HttpClient`, `IAuxInstallService`, `IOptiScalerService`, `GitHubETagCache` via constructor
    - Implement `IsStagingReady`, `StagedVersion`, `HasUpdate`, `FirstTimeWarningAcknowledged`, `SelectedVariant` properties
    - _Requirements: 19.1, 19.2, 19.3, 1.4, 1.5_

  - [x] 2.3 Implement `DetermineRequiredDlls` static method
    - Pure function: given `GraphicsApiType` and `bool is32Bit`, return `(archFolder, dllNames)` per the design's switch expression
    - Throw `InvalidOperationException` for unsupported APIs (DX12, Vulkan, OpenGL, Unknown)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 2.4 Write property test for DLL selection mapping
    - **Property 2: DLL selection mapping**
    - Generate random `(GraphicsApiType ∈ {DX8, DX9, DX10, DX11}, bool is32Bit)` pairs; assert correct arch folder and DLL names
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

  - [x] 2.5 Implement `ResolveDeploymentPaths` method for OptiScaler coexistence
    - Given required DLL names and the install path, query `IOptiScalerService.DetectInstallation` for the OptiScaler filename
    - Route conflicting DLLs to `pluginDlls`, non-conflicting to `rootDlls` per the design
    - _Requirements: 6.1, 6.5, 6.6, 6.7_

  - [x] 2.6 Write property test for OptiScaler coexistence routing
    - **Property 4: OptiScaler coexistence routing**
    - Generate random DLL name lists and optional OptiScaler filenames; assert correct partitioning into root vs plugin DLLs
    - **Validates: Requirements 6.1, 6.5, 6.6, 6.7**

  - [x] 2.7 Implement `IsValidDeploymentPath` path safety validation
    - Reject paths under `%SystemRoot%`, `%SystemRoot%\System32`, `%SystemRoot%\SysWOW64`, `%ProgramFiles%\WindowsApps`
    - Accept valid game directory paths outside protected locations
    - _Requirements: 9.5, 9.6, 21.2_

  - [x] 2.8 Write property test for path safety validation
    - **Property 6: Path safety validation**
    - Generate random path strings including system paths; assert correct accept/reject decisions
    - **Validates: Requirements 9.5, 9.6, 21.2**

  - [x] 2.9 Implement `IsDxvkFile` binary signature detection
    - Read first 2 MB of a DLL file and scan for ASCII `"dxvk"` and `"DXVK_"` byte patterns
    - Follow the same scanning pattern as `OptiScalerService.IsOptiScalerFile`
    - _Requirements: 8.1, 8.2_

  - [x] 2.10 Write property test for binary signature detection
    - **Property 8: Binary signature detection**
    - Generate random byte arrays with and without DXVK signature strings; assert `IsDxvkFile` returns correct boolean
    - **Validates: Requirements 8.1, 8.2**

- [x] 3. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement DxvkService staging and tracking
  - [x] 4.1 Create `DxvkService.Staging.cs` partial class with staging methods
    - Implement `EnsureStagingAsync`: download from GitHub API (variant-aware URL), extract `.tar.gz` via 7-Zip, write `version.txt`, preserve `x64/`/`x32/` structure
    - Implement `CheckForUpdateAsync`: compare cached version tag against remote `tag_name`, set `HasUpdate`
    - Implement `ClearStaging`: delete staging directory contents
    - Use `GitHubETagCache` for conditional HTTP requests
    - Report progress via `IProgress<(string, double)>`
    - Log errors via `CrashReporter`
    - _Requirements: 1.1, 1.2, 1.3, 1.6, 1.7, 1.8, 2.1, 2.2_

  - [x] 4.2 Write property test for IsStagingReady correctness
    - **Property 10: IsStagingReady correctness**
    - Generate random `(dirExists, versionExists, dllExists)` boolean triples; assert `IsStagingReady` returns true only when all three are true
    - **Validates: Requirements 1.4**

  - [x] 4.3 Write property test for update detection
    - **Property 11: Update detection**
    - Generate random version string pairs; assert `HasUpdate` is true if and only if remote differs from cached
    - **Validates: Requirements 2.1, 2.2**

  - [x] 4.4 Implement tracking record persistence (`LoadAllRecords`, `FindRecord`, save/remove)
    - Read/write `dxvk_installed.json` using `System.Text.Json`
    - Implement `LoadAllRecords`, `FindRecord(gameName, installPath)`, internal `SaveRecord`, `RemoveRecord`
    - Handle corrupted/missing file gracefully (log, start with empty set)
    - _Requirements: 18.2, 18.3, 23.5_

- [x] 5. Implement DxvkService install, uninstall, and update flows
  - [x] 5.1 Create `DxvkService.Install.cs` partial class with `InstallAsync`
    - Call `EnsureStagingAsync` if not ready
    - Call `DetermineRequiredDlls` with game API and bitness
    - Validate deployment path via `IsValidDeploymentPath`
    - Validate DLL architecture matches game bitness
    - Call `ResolveDeploymentPaths` for OptiScaler coexistence
    - Back up existing non-RHI-managed files with `.original` extension (skip backup for known RHI DLLs)
    - Copy DLLs to game root and/or OptiScaler plugins folder (create plugins folder if needed)
    - Deploy default `dxvk.conf` if none exists (template: `dxgi.enableHDR = True`, `dxvk.allowFse = False`, `dxvk.latencySleep = Auto`, `d3d9.dpiAware = True`)
    - Coordinate ReShade mode switching (DX proxy → Vulkan layer) via `IAuxInstallService`
    - Create and persist `DxvkInstalledRecord`
    - Update card properties (`DxvkStatus`, `DxvkInstalledVersion`, `DxvkRecord`)
    - Report progress via `IProgress<(string, double)>`
    - Handle file-locked errors with descriptive messages
    - _Requirements: 3.1–3.11, 5.1, 5.2, 5.4, 5.5, 6.1–6.7, 7.1, 7.2, 7.3, 9.5, 9.6, 21.1–21.4, 22.1, 23.1_

  - [x] 5.2 Write property test for architecture validation
    - **Property 3: Architecture validation rejects mismatches**
    - Generate random `(API, gameBitness, dllBitness)` triples; assert deployment is refused when bitness mismatches
    - **Validates: Requirements 3.11**

  - [x] 5.3 Write property test for backup skip logic
    - **Property 12: Backup skip for managed DLLs**
    - Generate random DLL files with/without RHI signatures; assert backup is created only for non-RHI-managed files
    - **Validates: Requirements 3.7**

  - [x] 5.4 Implement `Uninstall` method
    - Delete all DLLs in `InstalledDlls` from game root
    - Delete all DLLs in `PluginFolderDlls` from OptiScaler plugins folder
    - Restore all `.original` backups (log warning if missing, continue)
    - Delete `dxvk.conf` if `DeployedConf` is true
    - Coordinate ReShade mode switching (Vulkan layer → DX proxy) via `IAuxInstallService`
    - Remove `DxvkInstalledRecord` from tracking file
    - Handle partial failures gracefully (log, continue, mark as partially uninstalled if DLL deletion fails)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.3, 23.2, 23.3_

  - [x] 5.5 Write property test for uninstall restores original state
    - **Property 9: Uninstall restores original state**
    - Generate random `DxvkInstalledRecord` with various DLL placements; assert all DLLs removed and backups restored
    - **Validates: Requirements 4.1, 4.2, 4.3**

  - [x] 5.6 Implement `UpdateAsync` method
    - Re-stage latest DXVK if needed
    - Re-deploy DLLs to game directory (overwrite existing DXVK DLLs, no backup needed for managed DLLs)
    - Update `DxvkVersion` in the tracking record
    - Update card properties
    - _Requirements: 2.3, 2.4_

  - [x] 5.7 Implement `CopyConfToGame` method
    - Copy `dxvk.conf` template from `AuxInstallService.InisDir` to game directory
    - Follow the same pattern as `OptiScalerService.CopyIniToGame`
    - _Requirements: 7.4, 7.5_

  - [x] 5.8 Implement `DetectInstallation` method
    - Scan game directory for DLLs matching DXVK binary signatures
    - Return detected version string or null
    - Distinguish managed vs unmanaged installs by checking for existing `DxvkInstalledRecord`
    - _Requirements: 8.3, 8.4_

- [x] 6. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Register DxvkService in DI container
  - [x] 7.1 Register `IDxvkService` → `DxvkService` in `App.xaml.cs` DI setup
    - Follow the same registration pattern as `IOptiScalerService` → `OptiScalerService`
    - Inject `IDxvkService` into `MainViewModel` constructor
    - _Requirements: 19.1, 19.2_

- [x] 8. Implement GameCardViewModel DXVK properties
  - [x] 8.1 Create `GameCardViewModel.Dxvk.cs` partial class in `RenoDXCommander/ViewModels/`
    - Add observable properties: `DxvkStatus` (GameStatus), `DxvkIsInstalling` (bool), `DxvkProgress` (double), `DxvkActionMessage` (string), `DxvkInstalledVersion` (string?)
    - Add `DxvkEnabled` observable property for per-game toggle
    - Add `ExcludeFromUpdateAllDxvk` property
    - Add `DxvkRecord` property of type `DxvkInstalledRecord?`
    - Add computed properties following the OptiScaler pattern: `DxvkStatusDot`, `DxvkActionLabel`, `DxvkBtnBackground`, `DxvkBtnForeground`, `DxvkBtnBorderBrush`, `DxvkProgressVisibility`, `DxvkDeleteVisibility`, `DxvkStatusText`, `DxvkStatusColor`, `DxvkShortAction`, `IsDxvkInstalled`, `DxvkInstallEnabled`, `DxvkRowVisibility` (Visible when `DxvkEnabled`, Collapsed otherwise)
    - Add card grid properties: `CardDxvkStatusDot`, `CardDxvkInstallEnabled`
    - Add `NotifyDxvkStatusDependents` and `NotifyDxvkIsInstallingDependents` methods
    - Add `OnDxvkStatusChanged`, `OnDxvkIsInstallingChanged`, `OnDxvkInstalledVersionChanged`, `OnDxvkActionMessageChanged`, `OnDxvkEnabledChanged` partial methods
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 11.1, 11.2_

  - [x] 8.2 Implement DXVK toggle eligibility logic
    - Add `IsDxvkToggleVisible` computed property: true when API is DX8/DX9/DX10/DX11
    - Add `IsDxvkToggleEnabled` computed property: true when visible AND not blacklisted AND API is not Unknown (unless override exists)
    - Add `DxvkToggleTooltip` computed property for disabled state explanations
    - _Requirements: 9.1, 9.2, 9.7, 10.5_

  - [x] 8.3 Write property test for DXVK toggle eligibility
    - **Property 5: DXVK toggle eligibility**
    - Generate random `(GraphicsApiType, blacklist set, hasOverride)` combinations; assert toggle enabled/disabled correctly
    - **Validates: Requirements 9.1, 9.2, 9.7, 10.5, 17.4**

  - [x] 8.4 Write property test for API override precedence
    - **Property 13: API override precedence**
    - Generate random `(autoApi, overrideApi)` pairs; assert DXVK DLL selection uses the override when present
    - **Validates: Requirements 17.5**

- [x] 9. Implement MainViewModel DXVK command handlers
  - [x] 9.1 Add DXVK install/uninstall/update command handlers in `MainViewModel.Install.cs` (or new `MainViewModel.Dxvk.cs` partial)
    - `InstallDxvkAsync(card)`: show first-time warning if not acknowledged, call `IDxvkService.InstallAsync`, update card state, save library
    - `UninstallDxvk(card)`: call `IDxvkService.Uninstall`, update card state, save library
    - `UpdateDxvkAsync(card)`: call `IDxvkService.UpdateAsync`, update card state, save library
    - `CopyDxvkConf(card)`: call `IDxvkService.CopyConfToGame`
    - Wire `DxvkEnabled` toggle change to install/uninstall flow
    - Disable controls during operations (set `DxvkIsInstalling`)
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 9.3, 9.4, 22.1_

- [x] 10. Integrate DXVK into MergeCards and cache phase
  - [x] 10.1 Update MergeCards to copy DXVK fields
    - In the MergeCards method (likely in `MainViewModel.GameMatching.cs`), copy `DxvkStatus`, `DxvkInstalledVersion`, `DxvkRecord`, `DxvkEnabled`, `ExcludeFromUpdateAllDxvk` from fresh card to existing card
    - _Requirements: 14.1, 14.2_

  - [x] 10.2 Write property test for MergeCards DXVK field copy
    - **Property 14: MergeCards DXVK field copy**
    - Generate random card pairs with arbitrary DXVK field values; assert all fields propagated correctly after merge
    - **Validates: Requirements 14.1**

  - [x] 10.3 Update cache phase card builder to populate DXVK state
    - In `CardBuilder.cs` (or `GameInitializationService.cs`), load `DxvkInstalledRecord` tracking file on startup
    - Populate `DxvkStatus`, `DxvkInstalledVersion`, `DxvkEnabled` from saved records and saved game library data
    - _Requirements: 15.1, 15.2, 18.3_

  - [x] 10.4 Update save/load logic for DXVK state in `SavedGameLibrary`
    - Ensure DXVK fields (`DxvkEnabled`, `DxvkInstalledVersion`, `ExcludeFromUpdateAllDxvk`) are written during save and restored during load
    - Apply restored values to game cards without losing user-set overrides
    - _Requirements: 20.1, 20.2, 20.3_

- [x] 11. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Integrate DXVK into Update All flow
  - [x] 12.1 Add `UpdateAllDxvkAsync` method in `MainViewModel.Update.cs`
    - Filter cards: `DxvkStatus == UpdateAvailable && !ExcludeFromUpdateAllDxvk && !IsHidden`
    - Ensure staging is ready before iterating
    - Call `IDxvkService.UpdateAsync` for each eligible card
    - Log failures per game, continue processing remaining games
    - Refresh UI state after completion
    - _Requirements: 16.1, 16.2, 16.3_

  - [x] 12.2 Write property test for Update All exclusion filtering
    - **Property 7: Update All exclusion filtering**
    - Generate random card sets with varying `DxvkStatus`, `ExcludeFromUpdateAllDxvk`, and `IsHidden` values; assert only eligible cards are selected
    - **Validates: Requirements 2.5, 16.2**

  - [x] 12.3 Wire `UpdateAllDxvkAsync` into the existing Update All orchestration
    - Add DXVK to the `AnyUpdateAvailable` computed property
    - Call `UpdateAllDxvkAsync` alongside existing `UpdateAllOsAsync`, `UpdateAllUlAsync`, `UpdateAllDcAsync`
    - Include DXVK update check in the background update check flow (`CheckForUpdateAsync`)
    - _Requirements: 16.1_

- [x] 13. Implement DXVK staging trigger and update check on startup
  - [x] 13.1 Add DXVK staging to the startup initialization flow
    - In `MainViewModel.Init.cs` (or `GameInitializationService`), call `IDxvkService.EnsureStagingAsync` alongside other component staging
    - Call `IDxvkService.CheckForUpdateAsync` during background update checks
    - Mark cards with `DxvkStatus = UpdateAvailable` when `HasUpdate` is true and card has DXVK installed
    - _Requirements: 1.1, 1.7, 2.1, 2.2, 2.3_

- [x] 14. Implement binary signature detection integration
  - [x] 14.1 Register DXVK signatures in the foreign DLL protection system
    - In `AuxInstallService.DllIdentification.cs` (or equivalent), add DXVK binary signature check using `IDxvkService.IsDxvkFile`
    - Classify detected DXVK DLLs as known (not flagged as foreign)
    - _Requirements: 8.1, 8.2_

  - [x] 14.2 Populate DXVK detection status during game scan
    - During game initialization, call `IDxvkService.DetectInstallation` for each game
    - Set `DxvkStatus` to appropriate value: `Installed` (managed), or display "Detected (unmanaged)" for unmanaged installs
    - _Requirements: 8.3, 8.4_

- [x] 15. Implement OptiScaler coexistence coordination
  - [x] 15.1 Handle OptiScaler installed after DXVK
    - In `OptiScalerService.Install.cs`, after OptiScaler is deployed, check if a DXVK DLL in the game root conflicts with OptiScaler's filename
    - If conflict: move the DXVK DLL to `OptiScaler/plugins/`, update the `DxvkInstalledRecord`
    - _Requirements: 6.3_

  - [x] 15.2 Handle OptiScaler uninstalled when DXVK is in plugins folder
    - In `OptiScalerService` uninstall flow, check if DXVK DLLs exist in the plugins folder
    - If so: move them back to game root, update the `DxvkInstalledRecord`
    - _Requirements: 6.4_

- [x] 16. Build UI — DXVK toggle in Overrides panel
  - [x] 16.1 Add DXVK toggle to the Overrides flyout
    - In `OverridesFlyoutBuilder.cs` (or `DetailPanelBuilder.Overrides.cs`), add a toggle switch bound to `DxvkEnabled`
    - Show only when `IsDxvkToggleVisible` is true
    - Disable when `IsDxvkToggleEnabled` is false, with tooltip from `DxvkToggleTooltip`
    - Add per-game DXVK update exclusion toggle bound to `ExcludeFromUpdateAllDxvk`
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

- [x] 17. Build UI — DXVK component row
  - [x] 17.1 Add DXVK component row in `DetailPanelBuilder.Components.cs`
    - Place below OptiScaler row, under the "── Optional ──" separator
    - Bind visibility to `DxvkRowVisibility` (hidden when `DxvkEnabled` is false)
    - Show status dot (`DxvkStatusDot`), version (`DxvkInstalledVersion`), status text (`DxvkStatusText`)
    - Add Install/Reinstall/Update button bound to `DxvkActionLabel` with appropriate styling
    - Add progress bar bound to `DxvkProgress` with visibility bound to `DxvkProgressVisibility`
    - Add delete button bound to `DxvkDeleteVisibility`
    - Add Info button that shows DXVK explanation dialog with game-specific notes from `DxvkGameNotes` manifest
    - Add dxvk.conf copy button (📋) that calls `CopyDxvkConf`
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8_

- [x] 18. Build UI — Settings page variant selector
  - [x] 18.1 Add DXVK variant selector to `SettingsHandler.cs` (or Settings page builder)
    - Add a dropdown/radio with "Standard DXVK" (default, recommended) and "dxvk-gplasync"
    - Bind to `IDxvkService.SelectedVariant`
    - On variant change: clear staging cache, prompt user about re-deployment to installed games
    - Persist variant preference across sessions (save/load in settings)
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

- [x] 19. Add manifest.json DXVK entries
  - [x] 19.1 Add initial `dxvkBlacklist`, `dxvkGameNotes`, and `dxvkApiOverrides` entries to `manifest.json`
    - Populate `dxvkBlacklist` with known anti-cheat games (Fortnite, Apex Legends, Dead by Daylight, PUBG, Rainbow Six Siege, Escape from Tarkov, Genshin Impact, Valorant)
    - Add sample `dxvkGameNotes` entries for games that benefit from DXVK (e.g. FFXIV)
    - Add sample `dxvkApiOverrides` if any games need them
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5_

- [x] 20. Add dxvk.conf template file
  - [x] 20.1 Create default `dxvk.conf` template in the RHI INI presets directory
    - Content: `dxgi.enableHDR = True`, `dxvk.allowFse = False`, `dxvk.latencySleep = Auto`, `d3d9.dpiAware = True`
    - Place alongside existing INI templates (e.g. `OptiScaler.ini`, `relimiter.ini`)
    - _Requirements: 7.1, 7.4_

- [x] 21. Implement first-time warning and safety guards
  - [x] 21.1 Add first-time DXVK warning dialog
    - Show warning on first DXVK enable in session: unofficial Windows support, anti-cheat risks, overlay conflicts, borderless fullscreen recommendation
    - Set `FirstTimeWarningAcknowledged` after user acknowledges
    - Integrate into the install flow in `MainViewModel` (check before calling `InstallAsync`)
    - _Requirements: 9.3, 9.4_

  - [x] 21.2 Wire blacklist and DX12 guard into toggle eligibility
    - Ensure `IsDxvkToggleEnabled` checks `DxvkBlacklist` from manifest
    - Ensure `IsDxvkToggleVisible` hides for DX12/Vulkan/OpenGL games
    - Ensure Unknown API disables toggle unless API override exists
    - _Requirements: 9.1, 9.2, 9.7_

- [x] 22. Implement concurrent operation safety
  - [x] 22.1 Add operation locking for DXVK install/uninstall
    - Disable DXVK toggle, update button, and conf copy button while `DxvkIsInstalling` is true
    - Queue install requests during staging download (sequential processing)
    - Use file-level locking or sequential processing for `dxvk_installed.json` writes
    - _Requirements: 22.1, 22.2, 22.3_

- [x] 23. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (14 properties total)
- Unit tests validate specific examples and edge cases
- The implementation follows the established OptiScaler pattern throughout: same DI structure, same partial class organization, same progress reporting, same UI component row pattern
- All C# code targets .NET 8 with WinUI 3, using CommunityToolkit.Mvvm for observable properties
