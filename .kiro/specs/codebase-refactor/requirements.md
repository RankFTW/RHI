# Requirements Document

## Introduction

This specification covers a structural refactoring of the RHI (RenoDX Commander) WinUI3 C# codebase. The goal is to reduce file sizes, eliminate code duplication, and improve maintainability without changing any observable behavior. The application must behave identically before and after the refactoring. All existing interfaces, public APIs, and test suites must continue to pass unchanged.

## Glossary

- **RHI**: RenoDX Commander — the WinUI3 desktop application being refactored
- **OptiScaler_Service**: The `OptiScalerService` class (2,462 lines) that manages OptiScaler lifecycle including download, staging, install, uninstall, update detection, INI management, and ReShade coexistence
- **IOptiScalerService**: The public interface contract for OptiScaler_Service that must be preserved unchanged
- **Settings_Handler**: The `SettingsHandler` class (1,117 lines) that encapsulates settings-page event handlers, hotkey capture/apply/reset logic, and mass INI deployment
- **Hotkey_Manager**: A new helper class to be extracted from Settings_Handler, encapsulating shared hotkey parse, format, capture, and apply logic
- **Mass_Deploy_Handler**: A new helper class to be extracted from Settings_Handler, encapsulating the mass INI deployment methods
- **Init_ViewModel**: The `MainViewModel.Init.cs` partial class (1,708 lines) containing the `InitializeAsync` method with two near-identical initialization branches
- **Detail_Panel_Builder**: The `DetailPanelBuilder.Overrides.cs` partial class (1,591 lines) that builds the detail panel override UI including the Update Inclusion dialog
- **Overrides_Flyout_Builder**: The `OverridesFlyoutBuilder.cs` class (1,389 lines) that builds the flyout override UI including an identical Update Inclusion dialog
- **Update_Inclusion_Helper**: A new shared helper class to be extracted, encapsulating the duplicated Update Inclusion dialog logic
- **Shader_Pack_Service**: The `ShaderPackService` class split across partial files including `ShaderPackService.Deploy.cs`
- **Partial_Class**: A C# language feature allowing a single class definition to be split across multiple source files, used extensively in this project
- **EARS_Pattern**: Easy Approach to Requirements Syntax — a structured pattern for writing unambiguous requirements
- **KeyOverlay_Format**: The hotkey string format used by ReShade: "vk,shift,ctrl,alt" (e.g. "36,0,0,0" for the Home key)
- **ReLimiter_Format**: The hotkey string format used by ReLimiter: "[Ctrl+][Alt+][Shift+]KeyName" (e.g. "Ctrl+F12")

## Requirements

### Requirement 1: Split OptiScaler_Service into Partial Classes

**User Story:** As a developer, I want OptiScaler_Service split into focused partial class files, so that I can navigate and maintain each concern independently, consistent with how other large services (AuxInstallService, ShaderPackService, GameDetectionService) are already organized.

#### Acceptance Criteria

1. WHEN the refactoring is complete, THE OptiScaler_Service SHALL be split into four partial class files: `OptiScalerService.cs` (constructor, constants, public properties), `OptiScalerService.Staging.cs` (EnsureStagingAsync, EnsureDlssStagingAsync, DLL management), `OptiScalerService.Install.cs` (Install, Uninstall, Update, OptiPatcher deployment), and `OptiScalerService.Coexist.cs` (ReShade coexistence logic, DLL renaming)
2. THE OptiScaler_Service SHALL continue to implement IOptiScalerService with no changes to the interface
3. THE OptiScaler_Service SHALL preserve all existing public and internal method signatures across the partial class files
4. WHEN any partial class file is compiled, THE OptiScaler_Service SHALL produce identical IL for all public members as the original single-file implementation
5. THE OptiScaler_Service partial class files SHALL each contain a `partial class OptiScalerService` declaration within the `RenoDXCommander.Services` namespace
6. WHEN the project is built after splitting, THE Compiler SHALL report zero new errors and zero new warnings attributable to the split

### Requirement 2: Extract Hotkey Management from Settings_Handler

**User Story:** As a developer, I want the duplicated hotkey capture, parse, format, and apply logic extracted into a shared Hotkey_Manager class, so that the four identical hotkey systems (overlay, screenshot, ReLimiter OSD, OptiScaler) use a single implementation.

#### Acceptance Criteria

1. WHEN the refactoring is complete, THE Hotkey_Manager SHALL contain the static methods `ParseHotkeyString`, `BuildHotkeyString`, `FormatHotkeyDisplay`, `IsDefaultHotkey`, and `BuildUlHotkeyString` currently in Settings_Handler
2. WHEN the refactoring is complete, THE Hotkey_Manager SHALL contain the `VkNames` dictionary currently in Settings_Handler
3. THE Settings_Handler SHALL reference Hotkey_Manager for all hotkey parsing, formatting, and building operations instead of containing its own implementations
4. WHEN a hotkey is captured via any PreviewKeyDown handler, THE Settings_Handler SHALL delegate to Hotkey_Manager for string building and display formatting
5. THE Hotkey_Manager SHALL be a static class located in the `RenoDXCommander` namespace
6. WHEN the project is built after extraction, THE Compiler SHALL report zero new errors and zero new warnings attributable to the extraction
7. WHEN any existing hotkey-related test is executed, THE Test_Suite SHALL produce the same pass/fail results as before the extraction

### Requirement 3: Extract Mass INI Deployment from Settings_Handler

**User Story:** As a developer, I want the mass INI deployment methods extracted into a dedicated Mass_Deploy_Handler class, so that Settings_Handler focuses on settings UI logic and the deployment concern is isolated.

#### Acceptance Criteria

1. WHEN the refactoring is complete, THE Mass_Deploy_Handler SHALL contain the methods `MassDeployRsIni_Click`, `MassDeployUlIni_Click`, `MassDeployDcIni_Click`, `MassDeployOsIni_Click`, `MassPresetInstall_ClickAsync`, and the private `ShowDeployResult` helper
2. THE Mass_Deploy_Handler SHALL accept the same `MainWindow` dependency as Settings_Handler to access ViewModel and UI elements
3. THE Settings_Handler SHALL no longer contain any mass deployment methods after extraction
4. WHEN any mass deploy button is clicked, THE Mass_Deploy_Handler SHALL produce identical behavior to the original Settings_Handler implementation
5. WHEN the project is built after extraction, THE Compiler SHALL report zero new errors and zero new warnings attributable to the extraction

### Requirement 4: Deduplicate Init_ViewModel Initialization Branches

**User Story:** As a developer, I want the two near-identical initialization branches in Init_ViewModel merged into a single flow, so that future changes to the task setup only need to be made in one place.

#### Acceptance Criteria

1. WHEN the refactoring is complete, THE Init_ViewModel SHALL contain a single initialization flow that handles both the cached-library path and the fresh-scan path
2. THE Init_ViewModel SHALL launch the same set of background tasks (wikiTask, lumaTask, manifestTask, detectTask, osWikiTask, hdrDbTask, rsTask, normalRsTask, osTask, dlssTask) regardless of whether a cached library exists
3. THE Init_ViewModel SHALL await tasks in the same order and with the same individual error handling as the original implementation
4. WHEN a cached library exists, THE Init_ViewModel SHALL merge fresh scan results with cached games using the same deduplication logic (normalized name and install path)
5. WHEN no cached library exists, THE Init_ViewModel SHALL skip the cache merge step and use fresh scan results directly
6. THE Init_ViewModel SHALL set `StatusText` and `SubStatusText` to the appropriate messages for each path (cached vs fresh) before launching tasks
7. WHEN the project is built after deduplication, THE Compiler SHALL report zero new errors and zero new warnings attributable to the merge
8. WHEN the application starts with a cached library, THE Init_ViewModel SHALL produce identical runtime behavior to the original cached-library branch
9. WHEN the application starts without a cached library, THE Init_ViewModel SHALL produce identical runtime behavior to the original fresh-scan branch

### Requirement 5: Consolidate Update Inclusion Dialog

**User Story:** As a developer, I want the duplicated Update Inclusion dialog logic extracted into a shared Update_Inclusion_Helper, so that Detail_Panel_Builder and Overrides_Flyout_Builder both call the same implementation.

#### Acceptance Criteria

1. WHEN the refactoring is complete, THE Update_Inclusion_Helper SHALL contain the `RefreshUpdateSummary` method, the Update Inclusion button creation, the checkbox dialog construction, and the save handler logic
2. THE Update_Inclusion_Helper SHALL accept a ViewModel accessor (or equivalent delegate) to abstract the difference between `_window.ViewModel` (Detail_Panel_Builder) and direct `ViewModel` access (Overrides_Flyout_Builder)
3. THE Update_Inclusion_Helper SHALL accept the game name, XamlRoot, and whether the game is an RE Engine game as parameters
4. WHEN the Update Inclusion button is clicked in either Detail_Panel_Builder or Overrides_Flyout_Builder, THE Update_Inclusion_Helper SHALL display the same checkbox dialog with the same component list (RS, RDX, UL, DC, OS, and conditionally REF)
5. WHEN the user saves the dialog, THE Update_Inclusion_Helper SHALL toggle exclusions and refresh the summary text identically to the original implementations
6. THE Detail_Panel_Builder SHALL call Update_Inclusion_Helper instead of containing its own dialog implementation
7. THE Overrides_Flyout_Builder SHALL call Update_Inclusion_Helper instead of containing its own dialog implementation
8. WHEN the project is built after consolidation, THE Compiler SHALL report zero new errors and zero new warnings attributable to the consolidation

### Requirement 6: Remove Dead Code

**User Story:** As a developer, I want dead code removed from the codebase, so that the project is cleaner and does not contain misleading obsolete methods.

#### Acceptance Criteria

1. WHEN the refactoring is complete, THE Shader_Pack_Service SHALL no longer contain the `MigrateLegacyDcShaders()` public method or its `MigrateLegacyDcShaders(string, string)` internal overload
2. WHEN the refactoring is complete, THE Shader_Pack_Service SHALL no longer contain the `[Obsolete]` attribute associated with the removed method
3. IF any caller references `MigrateLegacyDcShaders` exist in the codebase, THEN THE Refactoring SHALL not remove the method (confirmed: zero external callers exist)
4. WHEN the dead code is removed, THE Compiler SHALL report zero new errors caused by the removal
5. WHEN the refactoring is complete, THE Compiler SHALL report fewer unused `using` directive warnings than before the cleanup

### Requirement 7: Preserve Behavioral Equivalence

**User Story:** As a developer, I want confidence that the refactoring introduces zero functional changes, so that users experience identical application behavior.

#### Acceptance Criteria

1. THE Refactoring SHALL not add, remove, or modify any public API surface (interfaces, public methods, public properties) except for the dead code removal specified in Requirement 6
2. THE Refactoring SHALL not change any observable behavior including UI rendering, file I/O operations, network requests, error handling, or logging output
3. WHEN the existing test suite in `RenoDXCommander.Tests/` is executed after the refactoring, THE Test_Suite SHALL produce the same pass/fail results as before the refactoring
4. THE Refactoring SHALL not remove any Display Commander (DC) related code, as DC is an active component
5. THE Refactoring SHALL preserve all existing `using` directives that are actively referenced, removing only those flagged as unused by the compiler
6. WHEN the project is built after all refactoring steps, THE Compiler SHALL produce a successful build with zero new errors
