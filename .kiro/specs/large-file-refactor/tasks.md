# Implementation Plan: Large File Refactor

## Overview

Split 9 large C# source files into smaller partial class files grouped by logical concern. Each split is a standalone task that must compile successfully before proceeding. MainViewModel is split first (largest/most complex), then services in parallel, then UI builders, and finally MainWindow. Property tests verify file structure, naming conventions, and size constraints using FsCheck + xUnit.

## Tasks

- [x] 1. Split MainViewModel into partial classes
  - [x] 1.1 Create `ViewModels/MainViewModel.Init.cs`
    - Add `partial class MainViewModel` with file-level responsibility comment
    - Move these methods from `MainViewModel.cs`: `InitializeAsync`, `DetectAllGamesDedupedAsync`, `BuildCards`, `BuildNotes`, `ScanForInstalledAddon`, `ScanAddonShallow`, `ScanForInstalledAddonQuick`, `MakeGenericUnreal`, `MakeGenericUnity`, `ResolveAddonUrl`, `SaveLibrary`, `SaveLibraryPublic`, `GetGenericNote`, `NormalizeForLookup`, `RefreshAsync`, `FullRefreshAsync`
    - Include all required `using` directives for the moved members
    - _Requirements: 1.2, 1.3, 11.5, 11.6, 12.1_

  - [x] 1.2 Create `ViewModels/MainViewModel.Install.cs`
    - Add `partial class MainViewModel` with file-level responsibility comment
    - Move all `[RelayCommand]` install/uninstall methods: `InstallModCommand`, `UninstallModCommand`, `InstallReShadeCommand`, `UninstallReShadeCommand`, `UninstallVulkanReShadeCommand`, `InstallREFrameworkCommand`, `UninstallREFrameworkCommand`, `InstallLumaAsync`, `UninstallLumaCommand`, `InstallReShadeVulkanAsync`, `ResolveAutoReShadeFilename`, `InstallUlAsync`, `UninstallUl`, `MatchLumaGame`, `IsLumaEnabled`, `ToggleLumaMode`, `ToggleUeExtended`, `AddManualGameCommand`, `ToggleHideGameCommand`, `ToggleFavouriteCommand`, `RemoveManualGameCommand`, `SetFilter`
    - Include all required `using` directives
    - _Requirements: 1.2, 1.3, 11.5, 11.6, 12.1_

  - [x] 1.3 Create `ViewModels/MainViewModel.Update.cs`
    - Add `partial class MainViewModel` with file-level responsibility comment
    - Move: `UpdateAllRenoDxAsync`, `UpdateAllReShadeAsync`, `UpdateAllUlAsync`, `UpdateAllRefAsync`, `CheckForUpdatesAsync`, `UpdateAllEligible`, `CheckUlUpdateAsync`, `EnsureUlCachedAsync`, `FetchLatestUlReleaseInfoAsync`, `SaveUlMeta`, `ReadUlInstalledVersion`, `IsNewerVersion`, `PreCacheRemoteUlAsync`, `PreCacheRemoteUlVariantAsync`, `GetUlFileName`, `GetUlCachePath`, `IsNewVersion`, `MarkVersionSeen`, `GetRecentPatchNotes`, `NotifyUpdateButtonChanged`
    - Include all required `using` directives
    - _Requirements: 1.2, 1.3, 11.5, 11.6, 12.1_

  - [x] 1.4 Create `ViewModels/MainViewModel.Settings.cs`
    - Add `partial class MainViewModel` with file-level responsibility comment
    - Move: `LoadNameMappings`, `SaveNameMappings`, `LoadThemeAndDensity`, `RenameGame`, `GetOriginalStoreName`, `RemoveGameRename`, `MigrateHashSet`, `MigrateDict`, `ApplyGameRenames`, `ApplyFolderOverrides`, `AddNameMapping`, `GetNameMapping`, `RemoveNameMapping`, `IsWikiExcluded`, `ToggleWikiExclusion`, `GetPerGameShaderMode`, `SetPerGameShaderMode`, `IsUpdateAllExcludedReShade/RenoDx/Ul`, `ToggleUpdateAllExclusionReShade/RenoDx/Ul`, `GetVulkanRenderingPath`, `SetVulkanRenderingPath`, `GetBitnessOverride`, `SetBitnessOverride`, `GetApiOverride`, `SetApiOverride`, `SetFolderOverride`, `ResetFolderOverride`, `GetFolderOverride`, `HasDllOverride`, `GetDllOverride`, `SetDllOverride`, `RemoveDllOverride`, `EnableDllOverride`, `UpdateDllOverrideNames`, `DisableDllOverride`, `SaveSettingsPublic`
    - Include all required `using` directives
    - _Requirements: 1.2, 1.3, 11.5, 11.6, 12.1_

  - [x] 1.5 Create `ViewModels/MainViewModel.GameMatching.cs`
    - Add `partial class MainViewModel` with file-level responsibility comment
    - Move: `ApplyManifest`, `ApplyManifestStatusOverrides`, `ApplyManifestCardOverrides`, `ApplyManifestDllRenames`, `ApplyCardOverrides`, `IsNativeHdrGameMatch`, `IsUeExtendedGameMatch`, `ResolveIs32Bit`, `DetectGraphicsApi`, `_DetectAllApisForCard`, `ScanAllExesInDir`, `IsSystemOrRuntimeDll`, `ResolveEngineOverride`, `GetManifestDllNames`, `MatchesGameSet`
    - Include all required `using` directives
    - _Requirements: 1.2, 1.3, 11.5, 11.6, 12.1_

  - [x] 1.6 Update primary `ViewModels/MainViewModel.cs`
    - Add `partial` keyword to the class declaration if not already present
    - Verify only constructor, fields, observable properties, forwarding properties, UI callback delegates, and shared helpers remain
    - Add file-level responsibility comment
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 1.7 Build verification for MainViewModel split
    - Run `dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental` and verify zero errors and zero new warnings
    - _Requirements: 1.4, 11.1, 11.2_


- [x] 2. Checkpoint — MainViewModel split complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Split GameDetectionService into partial classes
  - [x] 3.1 Create `Services/GameDetectionService.Steam.cs`
    - Add `partial class GameDetectionService` with file-level comment indicating Steam platform coverage
    - Move: `FindSteamGames`, `FindSteamLibraries`
    - Include all required `using` directives
    - _Requirements: 2.2, 2.3, 11.5, 11.6, 12.1_

  - [x] 3.2 Create `Services/GameDetectionService.Platform.cs`
    - Add `partial class GameDetectionService` with file-level comment indicating GOG/Epic/EA/Ubisoft/Battle.net/Rockstar coverage
    - Move: `FindGogGames`, `FindEpicGames`, `FindEaGames`, `FindUbisoftGames`, `FindBattleNetGames`, `FindRockstarGames`
    - Include all required `using` directives
    - _Requirements: 2.2, 2.3, 11.5, 11.6, 12.1_

  - [x] 3.3 Create `Services/GameDetectionService.Xbox.cs`
    - Add `partial class GameDetectionService` with file-level comment indicating Xbox platform coverage
    - Move: `FindXboxGames`, `FindXboxGamesFallback`, `ParseGamingRootFile`, `ResolveXboxGameRoot`
    - Include all required `using` directives
    - _Requirements: 2.2, 2.3, 11.5, 11.6, 12.1_

  - [x] 3.4 Update primary `Services/GameDetectionService.cs`
    - Add `partial` keyword to the class declaration if not already present
    - Verify only class declaration, shared helpers (`ReadRegistry`, `ExtractVdfValue`, `ExtractJsonString`, `NormalizeName`, `MatchGame`), engine detection methods, and `_engineCache` remain
    - Add file-level responsibility comment
    - _Requirements: 2.1, 2.2, 2.3, 2.5_

  - [x] 3.5 Build verification for GameDetectionService split
    - Run `dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental` and verify zero errors and zero new warnings
    - _Requirements: 2.5, 11.1, 11.2_

- [x] 4. Split ShaderPackService into partial classes
  - [x] 4.1 Create `Services/ShaderPackService.Download.cs`
    - Add `partial class ShaderPackService` with file-level responsibility comment
    - Move: `EnsureLatestAsync`, `EnsurePackAsync`, `ResolveGhRelease`, `ResolveDirectUrlVersion`, `PackHasExtractedFiles`, `RecordExtractedFiles`, `FileListKey`, `LoadStoredVersion`, `SaveStoredVersion`, `VersionKey`
    - Include all required `using` directives
    - _Requirements: 3.2, 3.3, 11.5, 11.6, 12.1_

  - [x] 4.2 Create `Services/ShaderPackService.Deploy.cs`
    - Add `partial class ShaderPackService` with file-level responsibility comment
    - Move: `DeployToGameFolder`, `RemoveFromGameFolder`, `SyncGameFolder`, `SyncShadersToAllLocations`, `DeployPacksIfAbsent`, `DeployAllPacksIfAbsent`, `DeployFileListIfAbsent`, `DeployFolderIfAbsent`, `PruneFiles`, `AllKnownPackFiles`, `FilesForIds`, `PacksForIds`, `IsFileStale`, `IsManagedByRdxc`, `WriteMarker`, `RestoreOriginalIfPresent`, `MigrateLegacyDcShaders`, `EnsureReShadeHeaders`
    - Include all required `using` directives
    - _Requirements: 3.2, 3.3, 11.5, 11.6, 12.1_

  - [x] 4.3 Update primary `Services/ShaderPackService.cs`
    - Add `partial` keyword to the class declaration if not already present
    - Verify only class declaration, constructor, path constants, pack definitions, enums, `ShaderPack` record, and `GetPackDescription` remain
    - Add file-level responsibility comment
    - _Requirements: 3.1, 3.2, 3.4_

  - [x] 4.4 Build verification for ShaderPackService split
    - Run `dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental` and verify zero errors and zero new warnings
    - _Requirements: 3.4, 11.1, 11.2_

- [x] 5. Split AuxInstallService into partial classes
  - [x] 5.1 Create `Services/AuxInstallService.Ini.cs`
    - Add `partial class AuxInstallService` with file-level responsibility comment
    - Move: `MergeRsIni`, `MergeRsVulkanIni`, `CopyRsIni`, `CopyRsPresetIniIfPresent`, `CopyUlIni`, `ApplyScreenshotPath`, `ParseIni`, `WriteIni`, `OrderedDict` class, `IsRdr2`
    - Include all required `using` directives
    - _Requirements: 7.2, 7.3, 11.5, 11.6, 12.1_

  - [x] 5.2 Create `Services/AuxInstallService.DllIdentification.cs`
    - Add `partial class AuxInstallService` with file-level responsibility comment
    - Move: `IdentifyDxgiFile`, `DxgiFileType` enum, `BackupForeignDll`, `RestoreForeignDll`, `IsReShadeFileStrict`, `IsReShadeFile`
    - Include all required `using` directives
    - _Requirements: 7.2, 7.3, 11.5, 11.6, 12.1_

  - [x] 5.3 Create `Services/AuxInstallService.Install.cs`
    - Add `partial class AuxInstallService` with file-level responsibility comment
    - Move: `InstallReShadeAsync`, `CheckReShadeUpdateLocal`, `CheckForUpdateAsync`, `Uninstall`, `UninstallDllOnly`, `LoadAll`, `FindRecord`, `SaveRecord`, `SaveAuxRecord`, `RemoveRecord`, `LoadDb`, `SaveDb`
    - Include all required `using` directives
    - _Requirements: 7.2, 7.3, 11.5, 11.6, 12.1_

  - [x] 5.4 Update primary `Services/AuxInstallService.cs`
    - Add `partial` keyword to the class declaration if not already present
    - Verify only class declaration, constructor, constants, staging paths, `EnsureReShadeStaging`, `EnsureInisDir`, `ReadInstalledVersion`, `SanitizeDirectoryName`, INI/DB path constants, and `IAuxFileService` explicit interface implementations remain
    - Add file-level responsibility comment
    - _Requirements: 7.1, 7.2, 7.4_

  - [x] 5.5 Build verification for AuxInstallService split
    - Run `dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental` and verify zero errors and zero new warnings
    - _Requirements: 7.4, 11.1, 11.2_

- [x] 6. Checkpoint — All services split complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Split CardBuilder into partial classes
  - [x] 7.1 Create `CardBuilder.Flyout.cs`
    - Add `partial class CardBuilder` with file-level responsibility comment
    - Move: `BuildInstallFlyoutContent`, `BuildComponentRow`, `UpdateComponentRow`, and flyout-related PropertyChanged handler logic
    - Include all required `using` directives
    - _Requirements: 8.2, 8.3, 11.5, 11.6, 12.1_

  - [x] 7.2 Update primary `CardBuilder.cs`
    - Add `partial` keyword to the class declaration if not already present
    - Verify only class declaration, constructor, `BuildGameCard` (card border, header, status dots, action row, bottom row, PropertyChanged subscription) remain
    - Add file-level responsibility comment
    - _Requirements: 8.1, 8.2, 8.3_

  - [x] 7.3 Build verification for CardBuilder split
    - Run `dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental` and verify zero errors and zero new warnings
    - _Requirements: 8.3, 11.1, 11.2_

- [x] 8. Split DetailPanelBuilder into partial classes
  - [x] 8.1 Create `DetailPanelBuilder.Components.cs`
    - Add `partial class DetailPanelBuilder` with file-level responsibility comment
    - Move: `UpdateDetailComponentRows`, `DetailCard_PropertyChanged`, `GetMessageColor`
    - Include all required `using` directives
    - _Requirements: 4.2, 4.3, 11.5, 11.6, 12.1_

  - [x] 8.2 Create `DetailPanelBuilder.Overrides.cs`
    - Add `partial class DetailPanelBuilder` with file-level responsibility comment
    - Move: `BuildOverridesPanel` and all its inline UI construction logic
    - Include all required `using` directives
    - _Requirements: 4.2, 4.3, 11.5, 11.6, 12.1_

  - [x] 8.3 Update primary `DetailPanelBuilder.cs`
    - Add `partial` keyword to the class declaration if not already present
    - Verify only class declaration, constructor, `_currentDetailCard` field, `CurrentDetailCard` property, and `PopulateDetailPanel` remain
    - Add file-level responsibility comment
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 8.4 Build verification for DetailPanelBuilder split
    - Run `dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental` and verify zero errors and zero new warnings
    - _Requirements: 4.3, 11.1, 11.2_

- [x] 9. Split DialogService into partial classes
  - [x] 9.1 Create `DialogService.Update.cs`
    - Add `partial class DialogService` with file-level responsibility comment
    - Move: `CheckForAppUpdateAsync`, `ShowUpdateDialogAsync`, `DownloadAndInstallUpdateAsync`, `ShowPatchNotesIfNewVersionAsync`, `ShowPatchNotesDialogAsync`, `ShowDcRemovalWarningAsync`, `ShowLegacyProgramFilesCleanupAsync`, `WriteCleanupMarker`, `DcWarningMarkerPath`, `LegacyCleanupMarkerPath`
    - Include all required `using` directives
    - _Requirements: 9.2, 9.3, 11.5, 11.6, 12.1_

  - [x] 9.2 Create `DialogService.Game.cs`
    - Add `partial class DialogService` with file-level responsibility comment
    - Move: `ShowForeignDxgiConfirmDialogAsync`, `ShowForeignWinmmConfirmDialogAsync`, `NotesButton_Click`
    - Include all required `using` directives
    - _Requirements: 9.2, 9.3, 11.5, 11.6, 12.1_

  - [x] 9.3 Update primary `DialogService.cs`
    - Add `partial` keyword to the class declaration if not already present
    - Verify only class declaration, constructor, `_window`, `_dispatcherQueue`, `PatchNotesDir`, `ViewModel` property, `Brush` helper, `ParseColor` helper, `GetCardFromSender` remain
    - Add file-level responsibility comment
    - _Requirements: 9.1, 9.2, 9.3_

  - [x] 9.4 Build verification for DialogService split
    - Run `dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental` and verify zero errors and zero new warnings
    - _Requirements: 9.3, 11.1, 11.2_

- [x] 10. Split DragDropHandler into partial classes
  - [x] 10.1 Create `DragDropHandler.Exe.cs`
    - Add `partial class DragDropHandler` with file-level responsibility comment
    - Move: `ProcessDroppedExe`, `InferGameRoot`, `LooksLikeGameRoot`, `InferGameName`, `CleanUnrealExeName`, `CleanFolderName`
    - Include all required `using` directives
    - _Requirements: 6.2, 6.3, 11.5, 11.6, 12.1_

  - [x] 10.2 Create `DragDropHandler.Addon.cs`
    - Add `partial class DragDropHandler` with file-level responsibility comment
    - Move: `ProcessDroppedAddon`, `ProcessDroppedArchive`, `ProcessDroppedUrl`
    - Include all required `using` directives
    - _Requirements: 6.2, 6.3, 11.5, 11.6, 12.1_

  - [x] 10.3 Update primary `DragDropHandler.cs`
    - Add `partial` keyword to the class declaration if not already present
    - Verify only class declaration, constructor, `_window`, `_crashReporter`, `ViewModel` property, `AllowedExtensions`, `IsAllowedExtension`, `Grid_DragOver`, `Grid_Drop` (top-level routing), `ExtractFileNameFromUrl`, `ParseUrlFromShortcutFile`, `HasPeSignature` remain
    - Add file-level responsibility comment
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 10.4 Build verification for DragDropHandler split
    - Run `dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental` and verify zero errors and zero new warnings
    - _Requirements: 6.3, 11.1, 11.2_

- [x] 11. Checkpoint — All UI builders split complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Split MainWindow code-behind into partial classes
  - [x] 12.1 Create `MainWindow.Events.cs`
    - Add `partial class MainWindow` with file-level responsibility comment
    - Move all button click and event handlers: `RefreshButton_Click`, `FullRefreshButton_Click`, `BrowseAddonWatchFolder_Click`, `ResetAddonWatchFolder_Click`, `RsIniButton_Click`, `SupportDiscord_Click`, `SupportGuide_Click`, `SupportKofi_Click`, `LayoutToggle_Click`, `CardInstallFlyout_Opening`, `CardComponentInstall_Click`, `CardComponentUninstall_Click`, `CardCopyRsIni_Click`, `CardCopyUlIni_Click`, `CardInstallButton_Click`, `CardFavouriteButton_Click`, `CardOpenFolder_Click`, `CardOverridesButton_Click`, `CardMoreMenu_Click`, `Card_PointerPressed`, `NotesButton_Click`, `CardInfoLink_Click`, `CardNotesButton_Click`, `ExternalLink_Click`, `NameLink_Click`, `SettingsButton_Click`, `SkipUpdateToggle_Toggled`, `BetaOptInToggle_Toggled`, `VerboseLoggingToggle_Toggled`, `PatchNotesLink_Click`, `OpenLogsFolder_Click`, `OpenDownloadsFolder_Click`, `CustomShadersToggle_Toggled`, `ApplyScreenshotPath_Click`, `SettingsBack_Click`, `DetailScrollViewer_SizeChanged`, `SearchBox_TextChanged`, `AddGameButton_Click`, `Filter_Click`, `FavouriteButton_Click`, `ExpandComponents_Click`, `CombinedInstallButton_Click`, `InstallButton_Click`, `Install64Button_Click`, `Install32Button_Click`, `EnsurePathAndInstall`, `UninstallButton_Click`, `InstallRsButton_Click`, `UninstallRsButton_Click`, `ChooseShadersButton_Click`, `UpdateAllButton_Click`, `UpdateAllRenoDx_Click`, `UpdateAllReShade_Click`, `InstallUlButton_Click`, `UninstallUlButton_Click`, `InstallRefButton_Click`, `UninstallRefButton_Click`, `UlIniButton_Click`, `DetailUlStatus_PointerPressed`, `DetailRsStatus_PointerPressed`, `DetailRefStatus_PointerPressed`, `DetailRdxStatus_PointerPressed`, `LumaToggle_Click`, `SwitchToLumaButton_Click`, `InstallLumaButton_Click`, `UninstallLumaButton_Click`, `UeExtendedFlyoutItem_Click`, `ShowUeExtendedWarningAsync`, `HideButton_Click`, `BrowseFolder_Click`, `OpenFolder_Click`, `RemoveManualGame_Click`, `RefreshWithScrollRestore`, `FullRefreshWithScrollRestore`, `RestoreScrollAndSelection`
    - Include all required `using` directives
    - _Requirements: 5.2, 5.3, 11.5, 11.6, 12.1_

  - [x] 12.2 Create `MainWindow.UISync.cs`
    - Add `partial class MainWindow` with file-level responsibility comment
    - Move: `OnViewModelChanged`, `UpdatePageVisibility`, `RefreshFilterButtonStyles`, `TryRestoreSelection`, `RebuildCardGrid`, `BuildGameCard`, `ScrollToCard`, `PopulateDetailPanel`, `UpdateDetailComponentRows`, `DetailCard_PropertyChanged`, `BuildOverridesPanel`, `OpenOverridesFlyout`, `UpdateLumaToggleStyle`, `RequestReselect`, `Brush`, `BuildScreenshotSavePath`, `GetCardFromSender`, `PickFolderAsync`, `Grid_DragOver`, `Grid_Drop`, and dialog delegation methods (`ShowForeignDxgiConfirmDialogAsync`, `CheckForAppUpdateAsync`, `ShowUpdateDialogAsync`, `DownloadAndInstallUpdateAsync`, `ShowPatchNotesIfNewVersionAsync`, `ShowDcRemovalWarningAsync`, `ShowLegacyProgramFilesCleanupAsync`, `ShowPatchNotesDialogAsync`)
    - Include all required `using` directives
    - _Requirements: 5.2, 5.3, 11.5, 11.6, 12.1_

  - [x] 12.3 Update primary `MainWindow.xaml.cs`
    - Add `partial` keyword to the class declaration if not already present (note: XAML code-behind is already partial)
    - Verify only constructor, field declarations, `MainWindow_Activated`, `MainWindow_Closed`, `HandleAddonFile`, `GameList_SelectionChanged`, and XAML-generated partial class integration remain
    - Add file-level responsibility comment
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 12.4 Build verification for MainWindow split
    - Run `dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental` and verify zero errors and zero new warnings
    - _Requirements: 5.3, 11.1, 11.2_

- [x] 13. Checkpoint — All splits complete, full build verification
  - Run `dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental` and verify zero errors and zero new warnings
  - Run all existing tests with `dotnet test` and verify no regressions
  - Ensure all tests pass, ask the user if questions arise.

- [x] 14. Write property tests for file structure and naming conventions
  - [x] 14.1 Write property test for file naming convention (Property 3)
    - **Property 3: File naming and location convention**
    - For all partial class files created by this refactoring, verify: (a) filename matches `{ClassName}.{Concern}.cs` pattern, (b) file resides in the same directory as the primary file, (c) primary file retains its original filename
    - Use FsCheck.Xunit `[Property]` attribute with the known list of expected partial files as test data
    - Add to `RenoDXCommander.Tests/LargeFileRefactorStructurePropertyTests.cs`
    - **Validates: Requirements 12.1, 12.2, 12.3**

  - [x] 14.2 Write property test for file size constraints (Property 4)
    - **Property 4: File size constraints**
    - For all `.cs` files in `RenoDXCommander/` (excluding `obj/` and `bin/`), verify line count < 800
    - For all partial class files created by this refactoring, verify line count < 600
    - Use FsCheck.Xunit `[Property]` attribute, enumerate files from disk
    - Add to `RenoDXCommander.Tests/LargeFileRefactorStructurePropertyTests.cs`
    - **Validates: Requirements 12.4, 13.1**

  - [x] 14.3 Write property test for partial keyword presence
    - For all partial class files created by this refactoring, verify each file contains the `partial class` keyword and the correct namespace declaration
    - Use FsCheck.Xunit `[Property]` attribute
    - Add to `RenoDXCommander.Tests/LargeFileRefactorStructurePropertyTests.cs`
    - **Validates: Requirements 11.5, 11.6**

  - [x] 14.4 Write unit tests for unsplit files
    - Verify `Services/ModInstallService.cs` remains a single file (no partial class files with `ModInstallService.*.cs` pattern exist)
    - Verify `OverridesFlyoutBuilder.cs` remains a single file (no partial class files with `OverridesFlyoutBuilder.*.cs` pattern exist)
    - Add to `RenoDXCommander.Tests/LargeFileRefactorStructurePropertyTests.cs`
    - **Validates: Requirements 10.1, 10.2**

- [x] 15. Final checkpoint — All tests pass
  - Run `dotnet test` and verify all existing and new tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each split task references specific requirements for traceability
- Build verification after each split catches issues early — the C# compiler is the primary correctness guarantee
- Property tests focus on file structure (existence, naming, size, partial keyword) — not API surface reflection
- The existing test project uses FsCheck 2.16.6 + xUnit
- MainViewModel is split first because it's the largest (4417 lines) and most complex
- Services (tasks 3–5) can be split in parallel after MainViewModel
- UI builders (tasks 7–10) can be split in parallel after services
- MainWindow (task 12) is split last to verify delegation methods compile correctly after helper classes are split
