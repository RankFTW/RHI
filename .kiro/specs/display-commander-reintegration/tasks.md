# Implementation Plan: Display Commander Reintegration

## Overview

Reintegrate Display Commander (DC) as a second frame rate limiter option in RHI, following the established ReLimiter pattern. Implementation proceeds bottom-up: models → ViewModel → services → UI → wiring → tests.

## Tasks

- [x] 1. Extend data models for DC support
  - [x] 1.1 Add `Dc` property to `ManifestDllNames.cs`
    - Add `[JsonPropertyName("dc")] public string? Dc { get; set; }` to `RenoDXCommander/Models/ManifestDllNames.cs`
    - _Requirements: 9.1_

  - [x] 1.2 Add DC observable properties to `GameCardViewModel.cs`
    - Add `[ObservableProperty]` fields: `_dcStatus` (GameStatus.NotInstalled), `_dcIsInstalling` (bool), `_dcProgress` (double), `_dcActionMessage` (string), `_dcInstalledFile` (string?), `_dcInstalledVersion` (string?)
    - Add `ExcludeFromUpdateAllDc` property
    - Add DC property names to `NotifyAll()` method
    - _Requirements: 10.1, 10.2, 10.5_

  - [x] 1.3 Create `GameCardViewModel.DisplayCommander.cs` partial class
    - Create new file `RenoDXCommander/ViewModels/GameCardViewModel.DisplayCommander.cs`
    - Implement computed properties mirroring `GameCardViewModel.UltraLimiter.cs`: `DcStatusDot`, `DcActionLabel`, `DcBtnBackground`, `DcBtnForeground`, `DcBtnBorderBrush`, `DcProgressVisibility`, `DcMessageVisibility`, `DcDeleteVisibility`, `DcStatusText`, `DcStatusColor`, `IsDcNotInstalling`, `IsDcInstalled`, `DcRowVisibility`
    - Implement mutual exclusion: `DcInstallEnabled` returns false when `IsUlInstalled` is true
    - Add `partial void OnDcStatusChanged()` and `partial void OnDcIsInstallingChanged()` to fire dependent property notifications
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 10.3, 10.4_

  - [x] 1.4 Modify `GameCardViewModel.UltraLimiter.cs` for mutual exclusion
    - Modify `UlInstallEnabled` (or equivalent) to also check `!IsDcInstalled`
    - Ensure `OnDcStatusChanged` triggers `UlInstallEnabled` notification
    - _Requirements: 2.1, 2.2, 2.5, 2.6, 2.7, 2.8_

  - [x] 1.5 Extend `GameCardViewModel.UI.cs` for DC
    - Add `DcStatus` to `UpdateBadgeVisibility` logic (respecting `ExcludeFromUpdateAllDc`)
    - Add `DcIsInstalling` to `CanCardInstall` check
    - Add DC to `IsManaged` property
    - _Requirements: 7.3, 8.2_

- [x] 2. Checkpoint - Ensure all model changes compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Implement DC install, uninstall, and update logic in ViewModel
  - [x] 3.1 Add DC constants and helper methods to `MainViewModel.Install.cs`
    - Add `DcFileName64`, `DcFileName32`, `DcReleasesUrl`, `DcReleasesApiUrl` constants
    - Add `GetDcFileName(bool is32Bit)`, `GetDcCachePath(bool is32Bit)`, `EnsureDcCachedAsync()`, `FetchLatestDcReleaseInfoAsync()`
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 3.2 Implement `InstallDcAsync()` in `MainViewModel.Install.cs`
    - Download correct bitness LITE variant, deploy to game folder
    - Create `AuxInstalledRecord` with `AddonType = "DisplayCommander"`
    - Add mutual exclusion guard: return early if `IsUlInstalled`
    - Set card properties: `DcStatus`, `DcInstalledFile`, `DcInstalledVersion`, `DcActionMessage`
    - Use `FadeMessage` for success (✅), no fade for errors (❌)
    - Handle download failure: log error, set error message, leave game folder unchanged
    - Handle cache fallback when remote is unavailable
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 2.5, 4.1, 4.4, 4.6, 5.7, 5.9_

  - [x] 3.3 Implement `UninstallDc()` in `MainViewModel.Install.cs`
    - Remove DC addon file from game folder
    - Remove corresponding `AuxInstalledRecord`
    - Reset card DC properties
    - Use `FadeMessage` for removal message (✖), no fade for errors
    - _Requirements: 4.3, 4.5, 5.8, 5.9_

  - [x] 3.4 Implement DC DLL filename resolution with priority chain
    - Add `ResolveDcFileName(GameCardViewModel card)` method
    - Priority: user DLL override (`DllOverrideConfig.DcFileName`) > manifest override (`ManifestDllNames.Dc`) > default (`GetDcFileName(is32Bit)`)
    - Wire into `InstallDcAsync` to use resolved filename
    - _Requirements: 6.3, 6.4, 9.2, 9.3, 9.4_

  - [x] 3.5 Add mutual exclusion guard to existing `InstallUlAsync`
    - Add early return if `IsDcInstalled` is true at the start of `InstallUlAsync`
    - _Requirements: 2.6_

- [x] 4. Implement DC update detection and Update All
  - [x] 4.1 Add DC update methods to `MainViewModel.Update.cs`
    - Add `CheckDcUpdateAsync()` comparing remote release against locally installed version
    - Add `ReadDcInstalledVersion()` and `SaveDcMeta()` with DC meta path
    - Extend `CheckForUpdatesAsync()` to include DC checks
    - _Requirements: 8.1_

  - [x] 4.2 Implement `UpdateAllDcAsync()` in update orchestration
    - Update DC for all eligible games (not excluded via `ExcludeFromUpdateAllDc`, not hidden)
    - Extend `UpdateOrchestrationService` or `MainViewModel.Update.cs` to include DC in Update All flow
    - Refresh Update All button state after completion
    - _Requirements: 8.3, 8.5_

  - [x] 4.3 Extend `AnyUpdateAvailable` to include DC
    - Include `DcStatus == UpdateAvailable` in the aggregate update check
    - _Requirements: 8.2_

- [x] 5. Implement DC persistence in services
  - [x] 5.1 Add DC exclusion toggle to `GameNameService.cs`
    - Add `UpdateAllExcludedDc` HashSet
    - Include in `Load()`, `Save()`, and `MigrateRename()` methods
    - Add to `IGameNameService` interface
    - _Requirements: 7.1, 7.2, 7.4_

  - [x] 5.2 Add DC exclusion methods to `MainViewModel.Settings.cs`
    - Add `IsUpdateAllExcludedDc()` and `ToggleUpdateAllExclusionDc()` methods
    - _Requirements: 7.2_

  - [x] 5.3 Extend `DllOverrideService.cs` for DC file renames
    - Extend `EnableDllOverride()`, `UpdateDllOverrideNames()`, `DisableDllOverride()` to handle DC file alongside ReShade
    - Handle foreign DLL conflict detection and warning dialog for DC
    - Handle immediate rename when DC is already installed and override changes
    - _Requirements: 6.5, 6.6, 6.7, 6.8, 6.9_

- [x] 6. Checkpoint - Ensure all ViewModel and service changes compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Implement DC detection on game scan
  - [x] 7.1 Add DC file detection in `MainViewModel.Init.cs`
    - Detect `zzz_display_commander_lite.addon64` / `.addon32` in game folders during scan
    - Set `DcStatus = Installed` and read version from PE file info when found
    - Detect DC with custom DLL override name using `AuxInstalledRecord` tracking data
    - Set `ExcludeFromUpdateAllDc` from `GameNameService.UpdateAllExcludedDc`
    - _Requirements: 11.1, 11.2, 11.3_

- [x] 8. Implement UI layer changes
  - [x] 8.1 Add DC row to `MainWindow.xaml`
    - Add `DetailDcRow` Grid with same 5-column layout as `DetailUlRow` (label, status, install button, empty column, delete button)
    - Add DC progress bar (`DetailDcProgress`) and message TextBlock (`DetailDcMessage`)
    - Wrap `DetailUlRow` + `DetailDcRow` in a grouping `Border` with `BorderSubtleBrush`
    - Place DC row between ReLimiter row and RenoDX row
    - _Requirements: 3.1, 3.2_

  - [x] 8.2 Add DC row update logic to `DetailPanelBuilder.Components.cs`
    - Add DC section in `UpdateDetailComponentRows()` mirroring the ReLimiter section
    - Set DC row visibility (hidden in Luma mode)
    - Update status text, colors, button content, enabled state (including mutual exclusion), delete button visibility
    - Add DC progress bar and message updates
    - Apply mutual exclusion: grey out DC install button when ReLimiter is installed, grey out ReLimiter install button when DC is installed
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.7, 2.8, 3.3, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 8.4_

  - [x] 8.3 Add DC event handlers to `InstallEventHandler.cs` and `MainWindow.Events.cs`
    - Add `InstallDcButton_Click` and `UninstallDcButton_Click` in `InstallEventHandler.cs` (following `InstallUlButton_Click` / `UninstallUlButton_Click` pattern)
    - Add `InstallDcButton_Click`, `UninstallDcButton_Click`, `DetailDcStatus_PointerPressed` event routing in `MainWindow.Events.cs`
    - _Requirements: 4.1, 4.3_

  - [x] 8.4 Add DC DLL naming override to `DetailPanelBuilder.Overrides.cs`
    - Add DC DLL naming toggle and dropdown in the DLL naming override section (alongside existing ReShade override)
    - Dropdown options: dxgi.dll, d3d9.dll, d3d11.dll, d3d12.dll, ddraw.dll, hid.dll, version.dll, opengl32.dll, dbghelp.dll, vulkan-1.dll, winmm.dll
    - Wire auto-save on toggle and dropdown selection change
    - _Requirements: 6.1, 6.2_

  - [x] 8.5 Add DC toggle to Global update inclusion section in `DetailPanelBuilder.Overrides.cs`
    - Add "DC" toggle alongside existing ReShade, ReLimiter, and RenoDX toggles
    - Wire auto-save on toggle change
    - Include DC exclusion in Reset Overrides handler
    - _Requirements: 7.1, 7.4_

  - [x] 8.6 Add DC to `CardComponentInstall_Click` / `CardComponentUninstall_Click` in `MainWindow.Events.cs`
    - Add `"DC"` case to the component switch statements for flyout install/uninstall
    - _Requirements: 4.1, 4.3_

- [x] 9. Checkpoint - Ensure full UI integration compiles and renders
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Write property-based tests for DC
  - [x] 10.1 Write property test for DC bitness filename resolution
    - **Property 1: Bitness selects correct DC filename**
    - Generate random booleans, verify `GetDcFileName` returns correct addon32/addon64 filename and `GetDcCachePath` returns distinct paths
    - **Validates: Requirements 1.2, 1.3**

  - [x] 10.2 Write property test for mutual exclusion invariant
    - **Property 2: Mutual exclusion of DC and ReLimiter**
    - Generate random `(GameStatus dcStatus, GameStatus ulStatus, bool dcInstalling, bool ulInstalling)` tuples, verify `DcInstallEnabled` and UL install enabled are mutually exclusive when one limiter is installed
    - **Validates: Requirements 2.1, 2.2, 2.5, 2.6, 2.7, 2.8**

  - [x] 10.3 Write property test for DC status display consistency
    - **Property 3: DC status display consistency**
    - Generate random `(GameStatus, bool isInstalling, string? version, bool isLumaMode)` tuples, verify all computed properties match status mapping rules
    - **Validates: Requirements 3.3, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 8.4**

  - [x] 10.4 Write property test for DllOverrideConfig round-trip
    - **Property 4: DllOverrideConfig and ManifestDllNames round-trip**
    - Generate random `(string rsName, string dcName)` pairs, serialize/deserialize `DllOverrideConfig`, verify equality
    - **Validates: Requirements 6.5, 9.1**

  - [x] 10.5 Write property test for ManifestDllNames round-trip
    - **Property 4: DllOverrideConfig and ManifestDllNames round-trip**
    - Generate random `(string? reshade, string? dc)` pairs, serialize/deserialize `ManifestDllNames`, verify both fields preserved
    - **Validates: Requirements 6.5, 9.1**

  - [x] 10.6 Write property test for DC DLL override resolution priority
    - **Property 5: DC DLL override resolution priority**
    - Generate random `(string? userOverride, string? manifestOverride, bool is32Bit)` tuples, verify priority chain: user > manifest > default
    - **Validates: Requirements 6.3, 6.4, 9.2, 9.3, 9.4**

  - [x] 10.7 Write property test for DC update exclusion
    - **Property 6: DC update exclusion from badge and Update All**
    - Generate random `(bool excludeDc, GameStatus dcStatus, GameStatus ulStatus, GameStatus rdxStatus, GameStatus rsStatus)` tuples, verify badge visibility and Update All eligibility
    - **Validates: Requirements 7.2, 7.3, 8.2**

  - [x] 10.8 Write property test for DC state change notifications
    - **Property 7: DC state change notifications**
    - Change `DcStatus` on a `GameCardViewModel`, capture `PropertyChanged` events, verify all expected dependent property names are fired
    - **Validates: Requirements 10.3, 10.4**

- [x] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests use FsCheck (already in the test project) with minimum 10 iterations
- DC follows the exact same patterns as ReLimiter throughout — install, uninstall, update, overrides, and UI
