# Requirements Document

## Introduction

The RenoDXCommander project has accumulated several large source files that have grown beyond maintainable sizes. This spec defines the requirements for splitting the 11 largest files into smaller, logically-grouped files. The refactoring is purely structural — no new features, no behaviour changes. The public API surface of each class remains the same. Partial classes are used where appropriate to split large classes without changing the type hierarchy.

The previous `mainwindow-breakup` spec already extracted helpers from `MainWindow.xaml.cs` (creating `DialogService`, `OverridesFlyoutBuilder`, `SettingsHandler`, `InstallEventHandler`, `WindowStateManager`, `DragDropHandler`). This spec continues that effort by splitting the remaining large files, including the extracted helpers themselves which have since grown large.

## Glossary

- **MainViewModel**: The primary ViewModel class (4417 lines) using CommunityToolkit.Mvvm, located at `ViewModels/MainViewModel.cs`.
- **GameDetectionService**: The service (1432 lines) responsible for detecting installed games across Steam, GOG, Epic, EA, Xbox, Ubisoft, Battle.net, and Rockstar platforms.
- **ShaderPackService**: The service (1329 lines) that downloads, extracts, and deploys HDR ReShade shader packs.
- **DetailPanelBuilder**: The helper class (1284 lines) that populates the game detail panel and component rows.
- **MainWindow**: The primary WinUI 3 Window code-behind (1335 lines) containing event handlers and UI sync.
- **DragDropHandler**: The handler class (1203 lines) for drag-and-drop processing of exe, addon, archive, and URL drops.
- **AuxInstallService**: The service (976 lines) that installs and manages ReShade, including INI parsing, DLL identification, and staging.
- **CardBuilder**: The helper class (824 lines) that builds game card UI elements and install flyout content.
- **DialogService**: The helper class (785 lines) encapsulating all dialog construction and async workflows.
- **ModInstallService**: The service (622 lines) that downloads, installs, and manages RenoDX addon files.
- **OverridesFlyoutBuilder**: The helper class (616 lines) that builds the per-game overrides flyout.
- **Partial_Class**: A C# feature allowing a single class to be split across multiple files while remaining one type at compile time.

## Requirements

### Requirement 1: Split MainViewModel into Partial Classes by Responsibility

**User Story:** As a developer, I want MainViewModel.cs (4417 lines) split into multiple partial class files grouped by responsibility, so that I can navigate and maintain each concern independently.

#### Acceptance Criteria

1. THE MainViewModel SHALL be declared as a `partial class` and split across multiple files in the `ViewModels/` folder.
2. WHEN the split is complete, THE MainViewModel SHALL have the following partial class files:
   - `MainViewModel.cs` — constructor, fields, observable properties, forwarding properties, and dispatcher setup (core scaffolding)
   - `MainViewModel.Init.cs` — `InitializeAsync`, `RefreshAsync`, `FullRefreshAsync`, `DetectAllGamesDedupedAsync`, `BuildCards`, `BuildNotes`, `ScanForInstalledAddon`, and related card-building logic
   - `MainViewModel.Install.cs` — all `[RelayCommand]` install/uninstall methods for RenoDX, ReShade, ReLimiter, RE Framework, and Luma, plus `InstallReShadeVulkanAsync` and helper methods like `ResolveAutoReShadeFilename`
   - `MainViewModel.Update.cs` — `UpdateAllRenoDxAsync`, `UpdateAllReShadeAsync`, `UpdateAllUlAsync`, `UpdateAllRefAsync`, `CheckForUpdatesAsync`, `CheckUlUpdateAsync`, UL caching/fetching methods, and update-related helpers
   - `MainViewModel.Settings.cs` — `LoadNameMappings`, `SaveNameMappings`, `LoadThemeAndDensity`, `RenameGame`, name mapping CRUD, wiki exclusion, update exclusion toggles, DLL override delegation, folder override, bitness/API override, shader mode, and all persisted-settings logic
   - `MainViewModel.GameMatching.cs` — `ApplyManifest`, `ApplyManifestStatusOverrides`, `ApplyManifestCardOverrides`, `ApplyManifestDllRenames`, `ApplyCardOverrides`, `ApplyGameRenames`, `ApplyFolderOverrides`, engine override resolution, manifest DLL name lookup, native HDR matching, UE-Extended matching, graphics API detection, and game-set matching
3. EACH partial class file SHALL contain a file-level comment indicating its responsibility.
4. THE application SHALL compile and behave identically after the split.

### Requirement 2: Split GameDetectionService by Platform

**User Story:** As a developer, I want GameDetectionService.cs (1432 lines) split into partial class files grouped by platform, so that each store's detection logic is isolated and independently maintainable.

#### Acceptance Criteria

1. THE GameDetectionService SHALL be declared as a `partial class` and split across multiple files in the `Services/` folder.
2. WHEN the split is complete, THE GameDetectionService SHALL have the following partial class files:
   - `GameDetectionService.cs` — class declaration, shared helpers (`ReadRegistry`, `ExtractVdfValue`, `ExtractJsonString`, `NormalizeName`, `MatchGame`), and engine detection (`DetectEngineAndPath`, `DetectEngineAndPathCore`, Unity/Unreal checks, file scanning helpers)
   - `GameDetectionService.Steam.cs` — `FindSteamGames`, `FindSteamLibraries`
   - `GameDetectionService.Platform.cs` — `FindGogGames`, `FindEpicGames`, `FindEaGames`, `FindUbisoftGames`, `FindBattleNetGames`, `FindRockstarGames`
   - `GameDetectionService.Xbox.cs` — `FindXboxGames`, `FindXboxGamesFallback`, `ParseGamingRootFile`, `ResolveXboxGameRoot`
3. EACH partial class file SHALL contain a file-level comment indicating which platforms it covers.
4. THE IGameDetectionService interface SHALL remain unchanged.
5. THE application SHALL compile and behave identically after the split.

### Requirement 3: Split ShaderPackService by Concern

**User Story:** As a developer, I want ShaderPackService.cs (1329 lines) split into partial class files separating pack definitions, download/extract logic, and deployment logic, so that each concern is independently navigable.

#### Acceptance Criteria

1. THE ShaderPackService SHALL be declared as a `partial class` and split across multiple files in the `Services/` folder.
2. WHEN the split is complete, THE ShaderPackService SHALL have the following partial class files:
   - `ShaderPackService.cs` — class declaration, constructor, public path constants, pack definitions array, enums (`SourceKind`, `PackCategory`), and the `ShaderPack` record
   - `ShaderPackService.Download.cs` — `EnsureLatestAsync`, `EnsurePackAsync`, `ResolveGhRelease`, `ResolveDirectUrlVersion`, extracted-file tracking methods (`PackHasExtractedFiles`, `RecordExtractedFiles`, `FileListKey`), and version persistence (`LoadStoredVersion`, `SaveStoredVersion`, `VersionKey`)
   - `ShaderPackService.Deploy.cs` — `DeployToGameFolder`, `RemoveFromGameFolder`, `SyncGameFolder`, `SyncShadersToAllLocations`, `DeployPacksIfAbsent`, `DeployAllPacksIfAbsent`, `DeployFileListIfAbsent`, `DeployFolderIfAbsent`, `PruneFiles`, `AllKnownPackFiles`, `FilesForIds`, `PacksForIds`, `IsFileStale`, `IsManagedByRdxc`, `WriteMarker`, `RestoreOriginalIfPresent`, `MigrateLegacyDcShaders`, `EnsureReShadeHeaders`, `GetPackDescription`
3. THE IShaderPackService interface SHALL remain unchanged.
4. THE application SHALL compile and behave identically after the split.

### Requirement 4: Split DetailPanelBuilder by Section

**User Story:** As a developer, I want DetailPanelBuilder.cs (1284 lines) split into partial class files separating the detail panel population from the overrides panel construction, so that each UI section is independently maintainable.

#### Acceptance Criteria

1. THE DetailPanelBuilder SHALL be declared as a `partial class` and split across multiple files.
2. WHEN the split is complete, THE DetailPanelBuilder SHALL have the following partial class files:
   - `DetailPanelBuilder.cs` — class declaration, constructor, `CurrentDetailCard` property, `PopulateDetailPanel` (header, badges, author badges, install path, utility buttons, Luma badge)
   - `DetailPanelBuilder.Components.cs` — `UpdateDetailComponentRows` (RE Framework, ReShade, ReLimiter, RenoDX, Luma rows, UE-Extended, progress bars, no-mod message), `DetailCard_PropertyChanged`, `GetMessageColor`
   - `DetailPanelBuilder.Overrides.cs` — `BuildOverridesPanel` and all its inline UI construction logic
3. THE application SHALL compile and behave identically after the split.

### Requirement 5: Split MainWindow Code-Behind by Concern

**User Story:** As a developer, I want MainWindow.xaml.cs (1335 lines) split into partial class files separating lifecycle, event handlers, and UI sync, so that the code-behind remains navigable.

#### Acceptance Criteria

1. THE MainWindow SHALL be declared as a `partial class` and split across multiple files.
2. WHEN the split is complete, THE MainWindow SHALL have the following partial class files:
   - `MainWindow.xaml.cs` — constructor, field declarations, `MainWindow_Activated`, `MainWindow_Closed`, `HandleAddonFile`, and XAML-generated partial class integration
   - `MainWindow.Events.cs` — all button click handlers (`RefreshButton_Click`, `FullRefreshButton_Click`, `LayoutToggle_Click`, `RsIniButton_Click`, `SupportDiscord_Click`, `SupportGuide_Click`, `SupportKofi_Click`, card action handlers, filter button handlers, game list selection handlers, search handlers)
   - `MainWindow.UISync.cs` — `OnViewModelChanged`, `UpdatePageVisibility`, `RefreshFilterButtonStyles`, `TryRestoreSelection`, `RestoreScrollAndSelection`, `RebuildCardGrid`, `BuildGameCard`, `ScrollToCard`, `PopulateDetailPanel`, `BuildOverridesPanel`, `OpenOverridesFlyout`, and other UI delegation methods
3. THE application SHALL compile and behave identically after the split.

### Requirement 6: Split DragDropHandler by Drop Type

**User Story:** As a developer, I want DragDropHandler.cs (1203 lines) split into partial class files separating the different drop-type processing paths, so that each handler is independently maintainable.

#### Acceptance Criteria

1. THE DragDropHandler SHALL be declared as a `partial class` and split across multiple files.
2. WHEN the split is complete, THE DragDropHandler SHALL have the following partial class files:
   - `DragDropHandler.cs` — class declaration, constructor, `AllowedExtensions`, `IsAllowedExtension`, `Grid_DragOver`, `Grid_Drop` (top-level routing), and URL-related helpers (`ExtractFileNameFromUrl`, `ParseUrlFromShortcutFile`)
   - `DragDropHandler.Exe.cs` — `ProcessDroppedExe`, `InferGameRoot`, `LooksLikeGameRoot`, `InferGameName`, `CleanUnrealExeName`, `CleanFolderName` (game inference heuristics)
   - `DragDropHandler.Addon.cs` — `ProcessDroppedAddon`, `ProcessDroppedArchive`, `ProcessDroppedUrl` (addon/archive/URL installation flows)
3. THE application SHALL compile and behave identically after the split.

### Requirement 7: Split AuxInstallService by Concern

**User Story:** As a developer, I want AuxInstallService.cs (976 lines) split into partial class files separating INI management, DLL identification, and install/uninstall logic, so that each concern is independently navigable.

#### Acceptance Criteria

1. THE AuxInstallService SHALL be declared as a `partial class` and split across multiple files in the `Services/` folder.
2. WHEN the split is complete, THE AuxInstallService SHALL have the following partial class files:
   - `AuxInstallService.cs` — class declaration, constructor, constants, staging paths, `EnsureReShadeStaging`, `EnsureInisDir`, `ReadInstalledVersion`, `SanitizeDirectoryName`, and the `IAuxFileService` explicit interface implementations
   - `AuxInstallService.Ini.cs` — `MergeRsIni`, `MergeRsVulkanIni`, `CopyRsIni`, `CopyRsPresetIniIfPresent`, `CopyUlIni`, `ApplyScreenshotPath`, `ParseIni`, `WriteIni`, `OrderedDict`, `IsRdr2`
   - `AuxInstallService.DllIdentification.cs` — `IdentifyDxgiFile`, `DxgiFileType` enum, `BackupForeignDll`, `RestoreForeignDll`, `IsReShadeFileStrict`, `IsReShadeFile`
   - `AuxInstallService.Install.cs` — `InstallReShadeAsync`, `CheckReShadeUpdateLocal`, `CheckForUpdateAsync`, `Uninstall`, `UninstallDllOnly`, and DB methods (`LoadAll`, `FindRecord`, `SaveRecord`, `SaveAuxRecord`, `RemoveRecord`, `LoadDb`, `SaveDb`)
3. THE IAuxInstallService and IAuxFileService interfaces SHALL remain unchanged.
4. THE application SHALL compile and behave identically after the split.

### Requirement 8: Split CardBuilder by Section

**User Story:** As a developer, I want CardBuilder.cs (824 lines) split into partial class files separating card construction from flyout content construction, so that each UI builder is independently maintainable.

#### Acceptance Criteria

1. THE CardBuilder SHALL be declared as a `partial class` and split across multiple files.
2. WHEN the split is complete, THE CardBuilder SHALL have the following partial class files:
   - `CardBuilder.cs` — class declaration, constructor, `BuildGameCard` (card border, header, status dots, action row, bottom row, PropertyChanged subscription)
   - `CardBuilder.Flyout.cs` — `BuildInstallFlyoutContent`, `BuildComponentRow`, `UpdateComponentRow`, and flyout-related PropertyChanged handler logic
3. THE application SHALL compile and behave identically after the split.

### Requirement 9: Split DialogService by Dialog Category

**User Story:** As a developer, I want DialogService.cs (785 lines) split into partial class files separating update dialogs, patch notes, and game-specific dialogs, so that each dialog category is independently maintainable.

#### Acceptance Criteria

1. THE DialogService SHALL be declared as a `partial class` and split across multiple files.
2. WHEN the split is complete, THE DialogService SHALL have the following partial class files:
   - `DialogService.cs` — class declaration, constructor, shared helpers (`Brush`, `ParseColor`, `GetCardFromSender`)
   - `DialogService.Update.cs` — `CheckForAppUpdateAsync`, `ShowUpdateDialogAsync`, `DownloadAndInstallUpdateAsync`, `ShowPatchNotesIfNewVersionAsync`, `ShowPatchNotesDialogAsync`, `ShowDcRemovalWarningAsync`, `ShowLegacyProgramFilesCleanupAsync`, `WriteCleanupMarker`
   - `DialogService.Game.cs` — `ShowForeignDxgiConfirmDialogAsync`, `ShowForeignWinmmConfirmDialogAsync`, `NotesButton_Click` (game notes dialog construction)
3. THE application SHALL compile and behave identically after the split.

### Requirement 10: ModInstallService and OverridesFlyoutBuilder Remain Single Files

**User Story:** As a developer, I want ModInstallService (622 lines) and OverridesFlyoutBuilder (616 lines) to remain as single files since they are under 650 lines and already have cohesive single responsibilities.

#### Acceptance Criteria

1. THE ModInstallService SHALL remain as a single file at `Services/ModInstallService.cs`.
2. THE OverridesFlyoutBuilder SHALL remain as a single file at `OverridesFlyoutBuilder.cs`.
3. IF either file grows beyond 800 lines in the future, THEN THE development team SHALL consider splitting it following the partial class pattern established by this spec.

### Requirement 11: Preserve Behavioural Equivalence

**User Story:** As a developer, I want the refactoring to produce zero user-visible changes, so that the split is a safe, purely structural improvement.

#### Acceptance Criteria

1. THE application SHALL produce identical UI output and user interactions after all splits are complete.
2. THE application SHALL compile without errors or new warnings introduced by the splits.
3. THE public API surface of each split class SHALL remain identical — all public and internal method signatures, properties, and fields SHALL be unchanged.
4. ALL existing interfaces (IGameDetectionService, IShaderPackService, IAuxInstallService, IAuxFileService, IModInstallService) SHALL remain unchanged.
5. EACH partial class file SHALL include the same `using` directives needed by its members.
6. EACH partial class file SHALL include the correct `namespace` declaration matching the original file.

### Requirement 12: Follow Consistent File Naming Convention

**User Story:** As a developer, I want all split files to follow a consistent naming convention, so that the codebase is predictable and navigable.

#### Acceptance Criteria

1. THE partial class files SHALL follow the naming pattern `{ClassName}.{Concern}.cs` (e.g., `MainViewModel.Install.cs`, `GameDetectionService.Steam.cs`).
2. THE primary partial class file SHALL retain the original filename (e.g., `MainViewModel.cs`, `GameDetectionService.cs`).
3. THE partial class files SHALL reside in the same directory as the original file.
4. EACH partial class file SHALL be under 600 lines after the split, with a target of 300–500 lines per file.

### Requirement 13: No File Exceeds 800 Lines After Refactoring

**User Story:** As a developer, I want every source file in the project to be under 800 lines after this refactoring, so that no single file is unwieldy to navigate.

#### Acceptance Criteria

1. WHEN all splits are complete, EACH source file in the `RenoDXCommander/` directory (excluding `obj/` and `bin/`) SHALL contain fewer than 800 lines.
2. IF a partial class file would exceed 800 lines after the initial split, THEN THE file SHALL be further subdivided until all files are under 800 lines.
