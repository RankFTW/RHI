# Implementation Plan: DC Removal

## Overview

Complete removal of all Display Commander (DC) functionality from the UPST codebase. Follows the bottom-up removal strategy from the design: models/constants → interfaces → services → viewmodels → UI → tests → specs. Each task builds on the previous to minimize intermediate compile errors.

## Tasks

- [x] 1. Remove DC constants, model fields, and interfaces
  - [x] 1.1 Remove DcDllPickerNames from DllOverrideConstants.cs
    - Delete the `DcDllPickerNames` array from `RenoDXCommander/Models/DllOverrideConstants.cs`
    - Remove any imports or references that become unused
    - _Requirements: 2.1_

  - [x] 1.2 Remove Dc property from ManifestDllNames.cs
    - Delete the `Dc` property from `RenoDXCommander/Models/ManifestDllNames.cs`
    - _Requirements: 2.6_

  - [x] 1.3 Remove DC method declarations from IAuxInstallService and IAuxFileService
    - Remove `InstallDcAsync` from `IAuxInstallService` interface
    - Remove `CopyDcIni` from `IAuxFileService` interface (if declared there)
    - Remove `dcMode` and `dcIsInstalled` parameters from `InstallReShadeAsync` in the interface
    - _Requirements: 2.7, 2.8, 12.1_

  - [x] 1.4 Remove DC declarations from IShaderPackService
    - Remove `SyncDcFolder` and `DeployToDcFolder` method declarations
    - _Requirements: 5.3_

  - [x] 1.5 Remove DC declarations from IUpdateOrchestrationService
    - Remove `UpdateAllDcAsync` method declaration from the interface
    - Remove `dcModeEnabled` parameter from `UpdateAllReShadeAsync` declaration
    - _Requirements: 3.1, 3.2_


- [x] 2. Remove DC from services
  - [x] 2.1 Remove DC constants, methods, and enums from AuxInstallService.cs
    - Remove constants: `DcUrl`, `DcUrl32`, `DcCacheFile`, `DcCacheFile32`, `DcNormalName`, `DcNormalName32`, `DcDxgiName`, `DcWinmmName`, `DcReShadeFolderPath`, `DcIniPath`, `TypeDc`, `RsDcModeName`, `RsDcModeName32`
    - Remove methods: `InstallDcAsync`, `CopyDcIni`, `SyncReShadeToDisplayCommander`, `IsDcFileStrict`
    - Remove `DisplayCommander` from `DxgiFileType` enum
    - Remove DC branch from `IdentifyDxgiFile`
    - Remove `WinmmFileType` enum and `IdentifyWinmmFile` if DC-only
    - Simplify `InstallReShadeAsync` to remove `dcMode`/`dcIsInstalled` parameters — always install as standard filename (dxgi.dll or override)
    - _Requirements: 2.2, 2.3, 2.4, 2.5, 9.1, 9.2, 9.3, 9.4, 12.1, 12.2, 12.3, 12.4_

  - [x] 2.2 Remove DC from UpdateOrchestrationService.cs
    - Delete `UpdateAllDcAsync` method entirely
    - Remove `dcModeEnabled` parameter from `UpdateAllReShadeAsync`
    - Remove `RsBlockedByDcMode` filter from `UpdateAllReShadeAsync`
    - Remove `effectiveDcMode` computation and pass `false` for dcMode/dcIsInstalled to `InstallReShadeAsync`
    - Remove DC update checks from `CheckForUpdatesAsync` and `CheckAuxUpdate`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 2.3 Remove DC from GameNameService.cs
    - Remove fields: `_perGameDcModeOverride`, `_dcCustomDllFileNames`, `_updateAllExcludedDc`
    - Remove properties: `PerGameDcModeOverride`, `DcCustomDllFileNames`, `UpdateAllExcludedDc`
    - Remove `setDcModeEnabled`/`setDcDllFileName` callbacks from `LoadNameMappings`
    - Remove DcModeLevel migration, DcModeEnabled/DcDllFileName loading, PerGameDcModeOverride migration from `LoadNameMappings`
    - Remove DC keys from `SaveNameMappings` (DcModeEnabled, DcDllFileName, PerGameDcModeOverride, DcCustomDllFileNames, UpdateAllExcludedDc)
    - Remove DC migration from `RenameGame` (DcRecord rename, _perGameDcModeOverride, _dcCustomDllFileNames migration)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 2.4 Remove DC from ShaderPackService
    - Remove `DcShadersDir` and `DcTexturesDir` properties
    - Remove `SyncDcFolder` method
    - Remove DC folder references from `SyncShadersToAllLocations` (or equivalent)
    - _Requirements: 5.1, 5.2, 5.4_

- [x] 3. Checkpoint — Ensure services compile
  - Ensure all tests pass, ask the user if questions arise.


- [x] 4. Remove DC from ViewModels
  - [x] 4.1 Delete GameCardViewModel.DisplayCommander.cs
    - Delete `RenoDXCommander/ViewModels/GameCardViewModel.DisplayCommander.cs` entirely
    - _Requirements: 1.2_

  - [x] 4.2 Remove DC fields and NotifyAll entries from GameCardViewModel.cs
    - Remove fields: `_dcStatus`, `_dcIsInstalling`, `_dcProgress`, `_dcActionMessage`, `_dcInstalledFile`, `_dcInstalledVersion`, `_dcCustomDllFileName`, `DcRecord`, `PerGameDcMode`, `DcModeExcluded`, `ExcludeFromUpdateAllDc`, `DcLegacyMode`, `_rsBlockedByDcMode`
    - Remove all DC-related entries from `NotifyAll()` method (DcStatusDot, DcActionLabel, DcBtnBackground, etc.)
    - _Requirements: 1.1, 1.3_

  - [x] 4.3 Write property test: GameCardViewModel has no DC members (Property 1)
    - **Property 1: GameCardViewModel has no DC members**
    - Use reflection to verify no DC member names exist on GameCardViewModel
    - Test all names: DcStatus, DcIsInstalling, DcProgress, DcActionMessage, DcInstalledFile, DcInstalledVersion, DcCustomDllFileName, DcRecord, PerGameDcMode, DcModeExcluded, ExcludeFromUpdateAllDc, DcLegacyMode, RsBlockedByDcMode, plus all computed properties from the deleted partial class
    - Use FsCheck with minimum 100 iterations
    - **Validates: Requirements 1.1, 1.3**

  - [x] 4.4 Remove DC from MainViewModel.cs
    - Remove properties: `DcModeEnabled`, `DcDllFileName`, `DcLegacyMode`, `DcLegacySettingsVisibility`, `DcLegacyHiddenVisibility`, `DcDllPickerVisibility`
    - Remove commands: `InstallDcCommand`, `UninstallDcCommand`
    - Remove methods: `InstallDcAsync`, `UninstallDc`, `ApplyDcModeSwitch`, `ApplyDcModeSwitchForCard`, `ResolveEffectiveDcMode`, `OnDcModeEnabledChanged`, `OnDcLegacyModeChanged`, `OnDcDllFileNameChanged`, `GetPerGameDcModeOverride`, `SetPerGameDcModeOverride`, `GetDcCustomDllFileName`, `SetDcCustomDllFileName`
    - Simplify `UpdateButtonTooltip` to not mention DC
    - Remove DC from `ApplyManifestDllRenames`
    - Remove DC from `ConfirmForeignWinmmOverwrite` if DC-only
    - Remove DC from any batch update calls (UpdateAllDcAsync, dcModeEnabled params)
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_

  - [x] 4.5 Write property test: MainViewModel has no DC members (Property 3)
    - **Property 3: MainViewModel has no DC members**
    - Use reflection to verify no DC member names exist on MainViewModel
    - Test all names: DcModeEnabled, DcDllFileName, DcLegacyMode, DcLegacySettingsVisibility, DcLegacyHiddenVisibility, DcDllPickerVisibility, InstallDcCommand, UninstallDcCommand, InstallDcAsync, UninstallDc, ApplyDcModeSwitch, ApplyDcModeSwitchForCard, ResolveEffectiveDcMode, UpdateAllDcAsync, OnDcModeEnabledChanged, OnDcLegacyModeChanged, OnDcDllFileNameChanged, GetPerGameDcModeOverride, SetPerGameDcModeOverride, GetDcCustomDllFileName, SetDcCustomDllFileName
    - Use FsCheck with minimum 100 iterations
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4**

  - [x] 4.6 Remove DC from SettingsViewModel.cs
    - Remove `_dcLegacyMode` field and `DcLegacyMode` property
    - Remove DcLegacyMode from `LoadSettingsFromDict`
    - Remove DcLegacyMode from `SaveSettingsToDict`
    - _Requirements: 7.1, 7.2, 7.3_


- [x] 5. Checkpoint — Ensure ViewModels compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Remove DC from UI layer
  - [x] 6.1 Remove DC elements from MainWindow.xaml
    - Remove DC Legacy Mode toggle sections
    - Remove DC Mode Settings sections (toggle, DLL picker, Deploy button)
    - Remove DC rows in the detail panel (DetailDcRow, DetailDcStatus, DetailDcInstallBtn, DetailDcIniBtn, DetailDcDeleteBtn, DetailDcProgress, DetailDcMessage)
    - Remove DC-related About section text variants
    - Remove DC credits entries and DC links
    - _Requirements: 8.1_

  - [x] 6.2 Remove DC handlers from MainWindow.xaml.cs
    - Remove `DcDllPickerNames` field
    - Remove `SyncDcDllPickerText` method
    - Remove `DcDllPicker_SelectionChanged` handler
    - Remove `DcDllPicker_TextSubmitted` handler
    - Remove `UpdateAllDc_Click` handler
    - Remove `DcIniButton_Click` handler
    - Remove `DeployDcModeButton_Click` handler
    - Remove `CardCopyDcToml_Click` handler
    - Remove DC from `UpdateAllButton_Click` (remove UpdateAllDcAsync call)
    - Remove DC from `CardInstallButton_Click` (remove DC install chain step)
    - Remove DC from `CardComponentInstall_Click` (remove "DC" case)
    - Remove DC from `CardComponentUninstall_Click` (remove "DC" case)
    - Remove `ConfirmForeignWinmmOverwrite` callback setup if DC-only
    - _Requirements: 8.2, 8.4, 8.5, 8.8_

  - [x] 6.3 Remove DC from InstallEventHandler.cs
    - Remove `InstallDcButton_Click` method
    - Remove `UninstallDcButton_Click` method
    - Remove `DeployDcModeButton_Click` method
    - Remove DC from `CombinedInstallButton_Click` (remove DC install chain step)
    - _Requirements: 8.3, 8.4, 8.5_

  - [x] 6.4 Remove DC from DetailPanelBuilder.cs
    - Remove DC row rendering from `UpdateDetailComponentRows`
    - Remove DC status link handler (`DcStatusLink_PointerPressed`)
    - Remove DC mode override UI from `BuildOverridesPanel`
    - Remove DC INI button references
    - _Requirements: 8.6_

  - [x] 6.5 Remove DC from CardBuilder.cs
    - Remove DC status dot from `BuildGameCard`
    - Remove DC component row from `BuildInstallFlyoutContent`
    - Remove DC status dot update from PropertyChanged handler
    - Remove `ApplyDcStatusLink` calls
    - Remove DC row visibility updates from flyout PropertyChanged handler
    - _Requirements: 8.7_

  - [x] 6.6 Write property test: UI handler classes have no DC methods (Property 6)
    - **Property 6: UI handler classes have no DC methods**
    - Use reflection to verify InstallEventHandler does not contain: InstallDcButton_Click, UninstallDcButton_Click, DeployDcModeButton_Click
    - Use reflection to verify MainWindow does not contain: SyncDcDllPickerText, DcDllPicker_SelectionChanged, DcDllPicker_TextSubmitted, UpdateAllDc_Click, DcIniButton_Click
    - Use FsCheck with minimum 100 iterations
    - **Validates: Requirements 8.2, 8.3**


- [x] 7. Checkpoint — Ensure UI compiles
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Remove DC test files and update test helpers
  - [x] 8.1 Delete DC-only test files
    - Delete all DC-only test files from `RenoDXCommander.Tests/`:
      - `DcCustomDllPersistencePropertyTests.cs`
      - `DcFileRenameOnModeSwitchTests.cs`
      - `DcLegacyTogglePropertyTests.cs`
      - `DcModeCustomSelectorVisibilityPropertyTests.cs`
      - `DcModeOverrideStaleLabelExplorationTests.cs`
      - `DcModeOverrideStaleLabelPreservationTests.cs`
      - `DcModeSwitchNoShaderTests.cs`
      - `DcModeSwitchShaderPropertyTests.cs`
      - `DcModeUiStatusPropertyTests.cs`
      - `DcShaderDeployExplorationTests.cs`
      - `DcShaderDeployPropertyTests.cs`
      - `DcUninstallShaderPropertyTests.cs`
      - `InstallDcShaderPreservationTests.cs`
      - `SyncAllLocationsDcNeverCalledPropertyTests.cs`
      - `MigrateLegacyDcShadersTests.cs`
    - _Requirements: 10.1, 10.7_

  - [x] 8.2 Remove DC references from TestHelpers.cs
    - Remove `InstallDcAsync` from IAuxInstallService stub implementations
    - Remove `CopyDcIni` from IAuxFileService stub implementations
    - Remove `SyncDcFolder` from IShaderPackService stub implementations
    - Remove `dcMode`/`dcIsInstalled` parameters from `InstallReShadeAsync` stubs
    - _Requirements: 10.5, 10.6_

  - [x] 8.3 Remove DC references from shared test files
    - Remove DC folder references from `ShaderPackServicePropertyTests.cs`
    - Remove DC property references from `VersionDisplayPropertyTests.cs`
    - Remove DC property references from `NotifyAllPropertyTests.cs`
    - Remove DC parameters from `InstallReShadeLocalDeployPropertyTests.cs`
    - _Requirements: 10.2, 10.3, 10.4, 10.8_

  - [x] 8.4 Write property test: AuxInstallService has no DC members (Property 2)
    - **Property 2: AuxInstallService has no DC members**
    - Use reflection to verify no DC member names exist on AuxInstallService
    - Test all names: DcUrl, DcUrl32, DcCacheFile, DcCacheFile32, DcNormalName, DcNormalName32, DcDxgiName, DcWinmmName, DcReShadeFolderPath, DcIniPath, TypeDc, RsDcModeName, RsDcModeName32, InstallDcAsync, CopyDcIni, SyncReShadeToDisplayCommander, IsDcFileStrict
    - Use FsCheck with minimum 100 iterations
    - **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 9.3, 12.3**

  - [x] 8.5 Write property test: GameNameService has no DC members (Property 4)
    - **Property 4: GameNameService has no DC members**
    - Use reflection to verify no DC member names exist on GameNameService
    - Test all names: _perGameDcModeOverride, _dcCustomDllFileNames, _updateAllExcludedDc, PerGameDcModeOverride, DcCustomDllFileNames, UpdateAllExcludedDc
    - Use FsCheck with minimum 100 iterations
    - **Validates: Requirements 4.1, 4.2**

  - [x] 8.6 Write property test: Settings persistence excludes DC keys (Property 5)
    - **Property 5: Settings persistence excludes DC keys**
    - Verify that when SaveNameMappings is called, the resulting settings dictionary does not contain DC keys: DcModeEnabled, DcDllFileName, PerGameDcModeOverride, DcCustomDllFileNames, UpdateAllExcludedDc, DcLegacyMode
    - Use FsCheck with minimum 100 iterations
    - **Validates: Requirements 4.5, 7.3**

  - [x] 8.7 Write property test: ReShade installs as standard filename (Property 7)
    - **Property 7: ReShade installs as standard filename**
    - For any game configuration (32-bit or 64-bit, with or without DLL override), verify that InstallReShadeAsync produces an AuxInstalledRecord.InstalledAs that is the standard filename (dxgi.dll or override) and never ReShade64.dll or ReShade32.dll
    - Use FsCheck with minimum 100 iterations
    - **Validates: Requirements 12.2**

- [x] 9. Delete obsolete DC spec directories
  - Delete `.kiro/specs/dc-legacy-toggle/` directory entirely
  - Delete `.kiro/specs/dc-mode-redesign/` directory entirely
  - _Requirements: 11.1, 11.2_

- [x] 10. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each major layer
- Property tests validate universal correctness properties from the design document using FsCheck
- The bottom-up removal order (models → interfaces → services → viewmodels → UI → tests) minimizes cascading compile errors
