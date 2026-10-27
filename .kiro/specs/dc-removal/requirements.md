# Requirements Document

## Introduction

Complete removal of all Display Commander (DC) related functionality from the UPST (Ultra Plus Support Tools) codebase. Display Commander was an optional addon that proxied ReShade loading alongside games. The feature is being retired — all DC code paths, UI elements, settings, services, models, and tests must be stripped out so the application has no awareness of DC.

## Glossary

- **UPST**: Ultra Plus Support Tools — the WinUI 3 / C# desktop application this codebase implements.
- **DC**: Display Commander — the addon being removed. A DLL proxy that loaded ReShade alongside games.
- **ReShade**: A post-processing injector for games; remains in the application after DC removal.
- **GameCardViewModel**: The view model representing a single game entry in the UI, with per-component install state.
- **AuxInstallService**: The service responsible for installing/uninstalling auxiliary components (ReShade, DC) into game directories.
- **UpdateOrchestrationService**: The service that coordinates batch update operations across all game cards.
- **GameNameService**: The service that persists per-game settings, name mappings, and overrides.
- **ShaderPackService**: The service that manages HDR shader pack downloads, extraction, and deployment to game folders and the DC AppData folder.
- **DetailPanelBuilder**: The code-behind class that populates the right-side detail panel for a selected game card.
- **CardBuilder**: The code-behind class that constructs game card UI elements for the grid/list view.
- **InstallEventHandler**: The code-behind class that routes install/uninstall button clicks to ViewModel commands.
- **MainViewModel**: The primary view model for the application window.
- **SettingsViewModel**: The view model that manages persisted user settings.
- **ManifestDllNames**: A model class carrying per-game DLL filename overrides from the remote manifest.
- **DllOverrideConstants**: A constants class holding known DLL filenames for override pickers.
- **IShaderPackService**: The interface for ShaderPackService, consumed by other services and tests.
- **IAuxInstallService**: The interface for AuxInstallService, consumed by other services and tests.
- **IAuxFileService**: The interface for AuxInstallService file-level operations.

## Requirements

### Requirement 1: Remove DC State from GameCardViewModel

**User Story:** As a developer, I want all DC-related state removed from GameCardViewModel, so that game cards have no concept of Display Commander.

#### Acceptance Criteria

1. THE GameCardViewModel SHALL NOT contain the fields DcStatus, DcIsInstalling, DcProgress, DcActionMessage, DcInstalledFile, DcInstalledVersion, DcCustomDllFileName, DcRecord, PerGameDcMode, DcModeExcluded, ExcludeFromUpdateAllDc, DcLegacyMode, or RsBlockedByDcMode.
2. THE GameCardViewModel.DisplayCommander.cs partial class file SHALL be deleted in its entirety.
3. WHEN the application compiles after removal, THE GameCardViewModel SHALL have zero references to Display Commander types or properties.

### Requirement 2: Remove DC Constants and Model Fields

**User Story:** As a developer, I want DC-related constants and model fields removed, so that the data layer has no DC awareness.

#### Acceptance Criteria

1. THE DllOverrideConstants class SHALL NOT contain the DcDllPickerNames array.
2. THE AuxInstallService class SHALL NOT contain the constants DcUrl, DcUrl32, DcCacheFile, DcCacheFile32, DcNormalName, DcNormalName32, DcDxgiName, DcWinmmName, or DcReShadeFolderPath.
3. THE AuxInstallService class SHALL NOT contain the methods InstallDcAsync, CopyDcIni, SyncReShadeToDisplayCommander, or IsDcFileStrict.
4. THE AuxInstallService class SHALL NOT contain the DcIniPath property.
5. THE AuxInstallService class SHALL NOT contain the TypeDc constant.
6. THE ManifestDllNames class SHALL NOT contain the Dc property.
7. THE IAuxInstallService interface SHALL NOT declare InstallDcAsync.
8. THE IAuxFileService interface SHALL NOT declare CopyDcIni.

### Requirement 3: Remove DC Logic from UpdateOrchestrationService

**User Story:** As a developer, I want DC batch-update logic removed from UpdateOrchestrationService, so that the update pipeline has no DC code paths.

#### Acceptance Criteria

1. THE UpdateOrchestrationService SHALL NOT contain the UpdateAllDcAsync method.
2. THE UpdateOrchestrationService.UpdateAllReShadeAsync method SHALL NOT accept a dcModeEnabled parameter.
3. THE UpdateOrchestrationService.UpdateAllReShadeAsync method SHALL NOT filter cards by RsBlockedByDcMode.
4. THE UpdateOrchestrationService.UpdateAllReShadeAsync method SHALL NOT compute an effectiveDcMode variable or pass dcIsInstalled to InstallReShadeAsync.
5. WHEN UpdateAllReShadeAsync is called, THE UpdateOrchestrationService SHALL pass false for the dcMode parameter and false for dcIsInstalled when calling InstallReShadeAsync.

### Requirement 4: Remove DC Logic from GameNameService

**User Story:** As a developer, I want DC persistence and migration logic removed from GameNameService, so that settings files no longer store or load DC state.

#### Acceptance Criteria

1. THE GameNameService SHALL NOT contain the fields _perGameDcModeOverride, _dcCustomDllFileNames, or _updateAllExcludedDc.
2. THE GameNameService SHALL NOT expose the properties PerGameDcModeOverride, DcCustomDllFileNames, or UpdateAllExcludedDc.
3. THE GameNameService.LoadNameMappings method SHALL NOT accept setDcModeEnabled or setDcDllFileName callbacks.
4. THE GameNameService.LoadNameMappings method SHALL NOT contain DcModeLevel migration logic, DcModeEnabled loading, DcDllFileName loading, or PerGameDcModeOverride migration logic.
5. THE GameNameService.SaveNameMappings method SHALL NOT persist DcModeEnabled, DcDllFileName, PerGameDcModeOverride, DcCustomDllFileNames, or UpdateAllExcludedDc keys.

### Requirement 5: Remove DC Logic from ShaderPackService

**User Story:** As a developer, I want DC shader folder sync removed from ShaderPackService, so that shaders are only deployed to game folders.

#### Acceptance Criteria

1. THE ShaderPackService SHALL NOT contain the DcShadersDir or DcTexturesDir properties.
2. THE ShaderPackService SHALL NOT contain the SyncDcFolder method.
3. THE IShaderPackService interface SHALL NOT declare SyncDcFolder or DeployToDcFolder.
4. THE ShaderPackService.SyncShadersToAllLocations method SHALL NOT reference DC folders or DC-related parameters.

### Requirement 6: Remove DC from MainViewModel

**User Story:** As a developer, I want all DC commands, properties, and mode-switching logic removed from MainViewModel, so that the primary view model has no DC awareness.

#### Acceptance Criteria

1. THE MainViewModel SHALL NOT contain the properties DcModeEnabled, DcDllFileName, DcLegacyMode, DcLegacySettingsVisibility, DcLegacyHiddenVisibility, or DcDllPickerVisibility.
2. THE MainViewModel SHALL NOT contain the commands InstallDcCommand or UninstallDcCommand.
3. THE MainViewModel SHALL NOT contain the methods InstallDcAsync, UninstallDc, ApplyDcModeSwitch, ApplyDcModeSwitchForCard, ResolveEffectiveDcMode, UpdateAllDcAsync, OnDcModeEnabledChanged, OnDcLegacyModeChanged, or OnDcDllFileNameChanged.
4. THE MainViewModel SHALL NOT contain the methods GetPerGameDcModeOverride, SetPerGameDcModeOverride, GetDcCustomDllFileName, or SetDcCustomDllFileName.
5. THE MainViewModel.UpdateButtonTooltip property SHALL NOT reference Display Commander in its text.
6. WHEN UpdateAllReShadeAsync is called from MainViewModel, THE MainViewModel SHALL NOT pass dcModeEnabled or check DcLegacyMode.

### Requirement 7: Remove DC from SettingsViewModel

**User Story:** As a developer, I want DC settings removed from SettingsViewModel, so that persisted settings have no DC fields.

#### Acceptance Criteria

1. THE SettingsViewModel SHALL NOT contain the DcLegacyMode property.
2. THE SettingsViewModel.LoadSettingsFromDict method SHALL NOT load a DcLegacyMode key.
3. THE SettingsViewModel.SaveSettingsToDict method (or equivalent save path) SHALL NOT persist a DcLegacyMode key.

### Requirement 8: Remove DC from UI (XAML and Code-Behind)

**User Story:** As a developer, I want all DC-related UI elements removed from the application window, so that users see no DC controls or status indicators.

#### Acceptance Criteria

1. THE MainWindow.xaml SHALL NOT contain DC Legacy Mode toggle sections, DC Mode Settings sections (toggle, DLL picker, Deploy button), DC rows in the detail panel, DC progress bars or messages, DC-related About section text variants, DC credits entries, or DC links.
2. THE MainWindow.xaml.cs SHALL NOT contain the DcDllPickerNames field, SyncDcDllPickerText method, DcDllPicker_SelectionChanged handler, DcDllPicker_TextSubmitted handler, UpdateAllDc_Click handler, DcIniButton_Click handler, or DeployDcModeButton_Click handler.
3. THE InstallEventHandler SHALL NOT contain InstallDcButton_Click, UninstallDcButton_Click, DeployDcModeButton_Click, or CardCopyDcToml_Click methods.
4. THE InstallEventHandler.CardComponentInstall_Click SHALL NOT contain a "DC" case.
5. THE InstallEventHandler.CardComponentUninstall_Click SHALL NOT contain a "DC" case.
6. THE DetailPanelBuilder SHALL NOT render a DC row, DC status link, or DC mode override UI in the overrides panel.
7. THE CardBuilder SHALL NOT render a DC status dot or DC component row in the install flyout.
8. WHEN the UpdateAllButton_Click handler fires, THE MainWindow.xaml.cs SHALL NOT call UpdateAllDcAsync.

### Requirement 9: Remove DC from DLL Identification

**User Story:** As a developer, I want DC-specific DLL identification logic removed, so that file classification only recognizes ReShade and unknown DLLs.

#### Acceptance Criteria

1. THE AuxInstallService.IdentifyDxgiFile method SHALL NOT return DxgiFileType.DisplayCommander.
2. THE AuxInstallService.IdentifyWinmmFile method and WinmmFileType enum SHALL be removed if their sole purpose was DC identification; IF they serve other purposes, THE methods SHALL NOT reference DC.
3. THE AuxInstallService.IsDcFileStrict method SHALL be removed.
4. THE AuxInstallService.DxgiFileType enum SHALL NOT contain a DisplayCommander member.

### Requirement 10: Remove DC-Related Tests

**User Story:** As a developer, I want all DC-specific test classes and DC test references removed, so that the test suite validates only remaining functionality.

#### Acceptance Criteria

1. THE SyncAllLocationsDcNeverCalledPropertyTests test class SHALL be deleted in its entirety.
2. THE ShaderPackServicePropertyTests SHALL NOT reference DC folder state or DC folder snapshots.
3. THE VersionDisplayPropertyTests SHALL NOT reference DC properties.
4. THE NotifyAllPropertyTests SHALL NOT reference DC properties.
5. WHEN test helper stubs implement IAuxInstallService or IAuxFileService, THE stubs SHALL NOT contain InstallDcAsync or CopyDcIni methods.
6. WHEN test helper stubs implement IShaderPackService, THE stubs SHALL NOT contain SyncDcFolder methods.
7. THE InstallDcShaderPreservationTests, DcUninstallShaderPropertyTests, DcShaderDeployPropertyTests, DcModeSwitchShaderPropertyTests, DcModeSwitchNoShaderTests, and DcFileRenameOnModeSwitchTests test classes SHALL be deleted in their entirety.
8. THE InstallReShadeLocalDeployPropertyTests SHALL NOT reference SyncDcFolder or dcMode/dcIsInstalled parameters that exist solely for DC.

### Requirement 11: Remove Obsolete DC Spec Directories

**User Story:** As a developer, I want the obsolete DC-related spec directories removed, so that the spec folder reflects only active features.

#### Acceptance Criteria

1. THE .kiro/specs/dc-legacy-toggle/ directory SHALL be deleted in its entirety.
2. THE .kiro/specs/dc-mode-redesign/ directory SHALL be deleted in its entirety.

### Requirement 12: Clean Up ReShade Install Path After DC Removal

**User Story:** As a developer, I want the ReShade install path simplified now that DC Mode no longer exists, so that ReShade always installs as its standard filename without DC-conditional branching.

#### Acceptance Criteria

1. THE AuxInstallService.InstallReShadeAsync method SHALL NOT accept a dcMode or dcIsInstalled parameter.
2. THE AuxInstallService.InstallReShadeAsync method SHALL always install ReShade as the standard filename (dxgi.dll or the DLL override filename) without DC-conditional renaming to ReShade64.dll or ReShade32.dll.
3. THE AuxInstallService constants RsDcModeName and RsDcModeName32 SHALL be removed if they exist solely for DC Mode renaming.
4. WHEN ReShade is installed for any game, THE AuxInstallService SHALL NOT check whether DC is installed in that game folder.
