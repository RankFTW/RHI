# Implementation Plan: Codebase Refactor

## Overview

Structural refactoring of the RenoDX Commander (RHI) WinUI3 C# codebase. Six work items that split large files, extract helpers, deduplicate logic, and remove dead code — all without changing observable behavior. Each major task ends with a build + test verification checkpoint to catch breakage immediately.

## Tasks

- [x] 1. Split OptiScalerService into partial class files
  - [x] 1.1 Create `OptiScalerService.Staging.cs` partial class file
    - Create `RenoDXCommander/Services/OptiScalerService.Staging.cs` with `namespace RenoDXCommander.Services;` and `public partial class OptiScalerService`
    - Move staging/download methods into it: `EnsureStagingAsync`, `CheckForUpdateAsync`, `ClearStaging`, `EnsureOptiPatcherStagingAsync`, `CheckOptiPatcherUpdateAsync`, `EnsureDlssStagingAsync`, `CheckDlssUpdateAsync`, `GetStagedDlssPath`, `GetStagedDlssdPath`, `GetStagedDlssgPath`, `GetStagedDlssVersion`, `Find7ZipExe`
    - Add only the `using` directives referenced by the moved methods
    - Remove the moved methods from the original `OptiScalerService.cs`
    - _Requirements: 1.1, 1.5_

  - [x] 1.2 Create `OptiScalerService.Install.cs` partial class file
    - Create `RenoDXCommander/Services/OptiScalerService.Install.cs` with `namespace RenoDXCommander.Services;` and `public partial class OptiScalerService`
    - Move install/uninstall/INI/detection methods into it: `GetBundledIniPath`, `InstallAsync`, `Uninstall`, `UpdateAsync`, `CopyIniToGame`, `BackupOriginalIfExists`, `RestoreOriginalIfExists`, `EnforceLoadReshade`, `EnforceLoadAsiPlugins`, `EnforceNvngxDlssPath`, `DetectInstallation`, `IsOptiScalerFile`, `IsOptiScalerFileStatic`, `ContainsSequence`, `LoadAllRecords`, `FindRecord`, `GetEffectiveOsDllName`, `SetHotkey`, `ApplyHotkeyToAllGames`, `WriteShortcutKey`, `ReadShortcutKey`
    - Add only the `using` directives referenced by the moved methods
    - Remove the moved methods from the original `OptiScalerService.cs`
    - _Requirements: 1.1, 1.5_

  - [x] 1.3 Create `OptiScalerService.Coexist.cs` partial class file
    - Create `RenoDXCommander/Services/OptiScalerService.Coexist.cs` with `namespace RenoDXCommander.Services;` and `public partial class OptiScalerService`
    - Move ReShade coexistence methods into it: both `ResolveReShadeFilename` overloads
    - Add only the `using` directives referenced by the moved methods
    - Remove the moved methods from the original `OptiScalerService.cs`
    - _Requirements: 1.1, 1.5_

  - [x] 1.4 Clean up the root `OptiScalerService.cs`
    - Verify the root file retains: constructor, constants, public properties, DI fields, `HotkeyNameToVkCode` dictionary, `ResolveHotkeyToVkCode`
    - Remove any `using` directives that are no longer referenced in the root file
    - Confirm `IOptiScalerService` interface is unchanged
    - _Requirements: 1.2, 1.3, 1.4, 1.6_

- [x] 2. Checkpoint — Build and test after OptiScaler split
  - Run `dotnet build` and verify zero new errors and zero new warnings attributable to the split
  - Run `dotnet test` on `RenoDXCommander.Tests` and verify all existing tests pass unchanged
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 1.6, 7.3, 7.6_

- [x] 3. Extract HotkeyManager from SettingsHandler
  - [x] 3.1 Create `HotkeyManager.cs` static class
    - Create `RenoDXCommander/HotkeyManager.cs` with `namespace RenoDXCommander;` and `public static class HotkeyManager`
    - Move from `SettingsHandler.cs`: `ParseHotkeyString`, `BuildHotkeyString`, `FormatHotkeyDisplay` (both overloads), `IsDefaultHotkey`, `BuildUlHotkeyString`, and the `VkNames` dictionary
    - Preserve all method signatures exactly (same parameter types, return types, accessibility)
    - _Requirements: 2.1, 2.2, 2.5_

  - [x] 3.2 Update SettingsHandler to delegate to HotkeyManager
    - Replace all internal calls in `SettingsHandler.cs` from `ParseHotkeyString(...)` to `HotkeyManager.ParseHotkeyString(...)`
    - Replace all `BuildHotkeyString(...)` calls with `HotkeyManager.BuildHotkeyString(...)`
    - Replace all `FormatHotkeyDisplay(...)` calls with `HotkeyManager.FormatHotkeyDisplay(...)`
    - Replace all `IsDefaultHotkey(...)` calls with `HotkeyManager.IsDefaultHotkey(...)`
    - Replace all `BuildUlHotkeyString(...)` calls with `HotkeyManager.BuildUlHotkeyString(...)`
    - Remove the now-empty method stubs and `VkNames` dictionary from `SettingsHandler.cs`
    - _Requirements: 2.3, 2.4_

  - [x] 3.3 Update existing hotkey tests to reference HotkeyManager
    - In `HotkeyStringRoundTripPropertyTests.cs`: change `SettingsHandler.ParseHotkeyString` / `SettingsHandler.BuildHotkeyString` references to `HotkeyManager.ParseHotkeyString` / `HotkeyManager.BuildHotkeyString`
    - In `HotkeyDisplayFormatPropertyTests.cs`: change `SettingsHandler.FormatHotkeyDisplay` references to `HotkeyManager.FormatHotkeyDisplay`
    - In `HotkeyHelperUnitTests.cs`: change all `SettingsHandler.` hotkey method references to `HotkeyManager.`
    - In `ApplyOverlayHotkeyPropertyTests.cs`: change `SettingsHandler.` hotkey method references to `HotkeyManager.`
    - In `HotkeyIniIdempotencePropertyTests.cs`: change `SettingsHandler.` hotkey method references to `HotkeyManager.`
    - In `HotkeyIniPreservationPropertyTests.cs`: change `SettingsHandler.` hotkey method references to `HotkeyManager.`
    - In `HotkeySettingsRoundTripPropertyTests.cs`: change `SettingsHandler.` hotkey method references to `HotkeyManager.`
    - _Requirements: 2.6, 2.7, 7.3_

- [x] 4. Checkpoint — Build and test after HotkeyManager extraction
  - Run `dotnet build` and verify zero new errors and zero new warnings attributable to the extraction
  - Run `dotnet test` on `RenoDXCommander.Tests` and verify all existing hotkey tests pass with updated references
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 2.6, 2.7, 7.3, 7.6_

- [x] 5. Extract MassDeployHandler from SettingsHandler
  - [x] 5.1 Create `MassDeployHandler.cs` class
    - Create `RenoDXCommander/MassDeployHandler.cs` with `namespace RenoDXCommander;` and `public class MassDeployHandler`
    - Add constructor accepting `MainWindow window` and store as `private readonly MainWindow _window`
    - Move from `SettingsHandler.cs`: `MassDeployRsIni_Click`, `MassDeployUlIni_Click`, `MassDeployDcIni_Click`, `MassDeployOsIni_Click`, `MassPresetInstall_ClickAsync`, and the private `ShowDeployResult` helper
    - Preserve all method signatures exactly
    - _Requirements: 3.1, 3.2_

  - [x] 5.2 Update SettingsHandler to delegate to MassDeployHandler
    - Create a `MassDeployHandler` instance in `SettingsHandler` (or wherever mass deploy buttons are wired), passing the `MainWindow` reference
    - Wire mass deploy button click handlers to the `MassDeployHandler` instance methods
    - Remove the mass deployment methods from `SettingsHandler.cs`
    - _Requirements: 3.3, 3.4_

- [x] 6. Checkpoint — Build and test after MassDeployHandler extraction
  - Run `dotnet build` and verify zero new errors and zero new warnings attributable to the extraction
  - Run `dotnet test` on `RenoDXCommander.Tests` and verify all existing tests pass unchanged
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 3.5, 7.3, 7.6_

- [x] 7. Deduplicate MainViewModel.Init.cs initialization branches
  - [x] 7.1 Merge the two initialization branches into a single flow
    - In `RenoDXCommander/ViewModels/MainViewModel.Init.cs`, refactor `InitializeAsync` to replace the two near-identical `if/else` branches with a single unified flow
    - Set `hasCachedLibrary` boolean based on `savedLib != null && !forceRescan`
    - Move status message assignment and `addonCache` initialization into a small `if/else` block at the top (cached vs fresh messages and cache source)
    - Keep the 10 background task launches (`wikiTask`, `lumaTask`, `manifestTask`, `detectTask`, `osWikiTask`, `hdrDbTask`, `rsTask`, `normalRsTask`, `osTask`, `dlssTask`) as a single shared block — no duplication
    - Keep the `await detectTask` and individual network task awaits with identical error handling as a single shared block
    - Keep the wiki/luma result extraction as a single shared block
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 7.2 Implement the cache merge conditional at the end
    - After `ApplyGameRenames(freshGames)`, add a single `if (hasCachedLibrary)` block that performs the deduplication merge (fresh wins, cached added if no match by normalized name or install path)
    - In the `else` branch, simply assign `detectedGames = freshGames`
    - Preserve the different log messages for each path
    - _Requirements: 4.4, 4.5, 4.6, 4.8, 4.9_

  - [x] 7.3 Write property test for cache merge deduplication
    - **Property 1: Cache merge deduplication preserves fresh games and excludes duplicates**
    - Create `RenoDXCommander.Tests/CacheMergeDeduplicationPropertyTests.cs`
    - Generate random lists of fresh and cached `DetectedGame` objects
    - Verify: (a) all fresh games are present in the merged result, (b) cached games appear only if they don't match any fresh game by normalized name or install path, (c) total count equals fresh count plus non-duplicate cached count
    - Use FsCheck.Xunit with `[Property(MaxTest = 100)]`
    - **Validates: Requirements 4.4**

- [x] 8. Checkpoint — Build and test after init deduplication
  - Run `dotnet build` and verify zero new errors and zero new warnings attributable to the merge
  - Run `dotnet test` on `RenoDXCommander.Tests` and verify all existing tests pass unchanged
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 4.7, 7.3, 7.6_

- [x] 9. Extract UpdateInclusionHelper from duplicated dialog code
  - [x] 9.1 Create `UpdateInclusionHelper.cs` static class
    - Create `RenoDXCommander/UpdateInclusionHelper.cs` with `namespace RenoDXCommander;` and `public static class UpdateInclusionHelper`
    - Implement `CreateUpdateInclusionControls(MainViewModel viewModel, string gameName, bool isREEngineGame, XamlRoot xamlRoot)` returning `(Button button, TextBlock summary)`
    - Move the shared logic into the helper: `RefreshUpdateSummary` local function, button creation with standard blue styling, the `Click` handler that builds the checkbox dialog via `DialogService.ShowSafeAsync`, and the save handler that toggles exclusions
    - Include the conditional REF checkbox for RE Engine games
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

  - [x] 9.2 Update DetailPanelBuilder.Overrides.cs to use UpdateInclusionHelper
    - Replace the inline Update Inclusion dialog code in `DetailPanelBuilder.Overrides.cs` with a call to `UpdateInclusionHelper.CreateUpdateInclusionControls(_window.ViewModel, capturedName, card.IsREEngineGame, _window.Content.XamlRoot)`
    - Wire the returned `(button, summaryTextBlock)` into the existing layout
    - Remove the now-redundant inline dialog code
    - _Requirements: 5.6_

  - [x] 9.3 Update OverridesFlyoutBuilder.cs to use UpdateInclusionHelper
    - Replace the inline Update Inclusion dialog code in `OverridesFlyoutBuilder.cs` with a call to `UpdateInclusionHelper.CreateUpdateInclusionControls(ViewModel, capturedName, card.IsREEngineGame, _window.Content.XamlRoot)`
    - Wire the returned `(button, summaryTextBlock)` into the existing layout
    - Remove the now-redundant inline dialog code
    - _Requirements: 5.7_

- [x] 10. Checkpoint — Build and test after UpdateInclusionHelper extraction
  - Run `dotnet build` and verify zero new errors and zero new warnings attributable to the consolidation
  - Run `dotnet test` on `RenoDXCommander.Tests` and verify all existing tests pass unchanged
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 5.8, 7.3, 7.6_

- [x] 11. Remove dead MigrateLegacyDcShaders code
  - [x] 11.1 Remove MigrateLegacyDcShaders from ShaderPackService.Deploy.cs
    - Delete the `public static void MigrateLegacyDcShaders()` method and its `[Obsolete]` attribute from `RenoDXCommander/Services/ShaderPackService.Deploy.cs`
    - Delete the `internal static void MigrateLegacyDcShaders(string shadersDir, string texturesDir)` overload
    - Remove any `using` directives in `ShaderPackService.Deploy.cs` that become unused after the removal
    - _Requirements: 6.1, 6.2, 6.3, 6.5_

- [x] 12. Final checkpoint — Full build and test verification
  - Run `dotnet build` and verify zero errors and zero new warnings across the entire solution
  - Run `dotnet test` on `RenoDXCommander.Tests` and verify all 190+ tests pass
  - Verify no public API surface has changed (interfaces, public methods, public properties) except for the dead code removal in Requirement 6
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 6.4, 7.1, 7.2, 7.3, 7.5, 7.6_

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each major refactoring step is followed by a build + test checkpoint to catch breakage immediately
- The design uses C# throughout — all code in this project is C# targeting .NET 8 / WinUI3
- Property tests use FsCheck.Xunit (already configured in the test project)
- Each task references specific requirements for traceability
- The OptiScaler split (Task 1) is the largest single change; the checkpoint at Task 2 is critical
- Hotkey test updates (Task 3.3) are required because existing tests reference `SettingsHandler` methods that move to `HotkeyManager`
