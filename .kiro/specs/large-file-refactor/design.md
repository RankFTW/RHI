# Design Document — Large File Refactor

## Overview

This design describes a purely structural refactoring of 9 large C# source files in the RenoDXCommander project using C# partial classes. The goal is to split each file into smaller, logically-grouped partial class files without changing any runtime behaviour, public API surface, or user-visible functionality.

The project is a WinUI 3 / .NET 8 desktop application that manages HDR mod installations for PC games. The codebase uses CommunityToolkit.Mvvm for the ViewModel layer (`[ObservableProperty]`, `[RelayCommand]` attributes) and a service-oriented architecture with dependency injection.

### Key Design Decisions

1. **Partial classes over inheritance**: C# partial classes allow splitting a single type across multiple files while remaining one type at compile time. No new types, interfaces, or inheritance hierarchies are introduced.
2. **Shared state stays in the primary file**: Fields, observable properties, constructor, and DI wiring remain in the primary `{ClassName}.cs` file. Partial files access these members directly since they are the same class.
3. **File naming convention**: `{ClassName}.{Concern}.cs` — e.g., `MainViewModel.Install.cs`, `GameDetectionService.Steam.cs`.
4. **Target file size**: 300–500 lines per file, hard cap at 800 lines.
5. **No behavioural changes**: Every split must produce identical compilation output (same IL) modulo file/line metadata.

## Architecture

The refactoring does not change the architecture. The existing layered structure remains:

```
┌─────────────────────────────────────────────────┐
│  MainWindow.xaml.cs (code-behind)               │
│  ├── CardBuilder          (card UI)             │
│  ├── DetailPanelBuilder   (detail panel UI)     │
│  ├── OverridesFlyoutBuilder (overrides flyout)  │
│  ├── DialogService        (dialogs)             │
│  ├── DragDropHandler      (drag-drop)           │
│  ├── InstallEventHandler  (install events)      │
│  ├── SettingsHandler      (settings UI)         │
│  └── WindowStateManager   (window state)        │
├─────────────────────────────────────────────────┤
│  MainViewModel (CommunityToolkit.Mvvm)          │
│  ├── .Init.cs       (initialization, refresh)   │
│  ├── .Install.cs    (install/uninstall commands) │
│  ├── .Update.cs     (update-all, UL caching)   │
│  ├── .Settings.cs   (persistence, name mapping) │
│  └── .GameMatching.cs (manifest, engine, API)   │
├─────────────────────────────────────────────────┤
│  Services                                       │
│  ├── GameDetectionService (.Steam, .Platform,   │
│  │                         .Xbox)               │
│  ├── ShaderPackService (.Download, .Deploy)     │
│  ├── AuxInstallService (.Ini, .DllId, .Install) │
│  └── ModInstallService (unchanged)              │
└─────────────────────────────────────────────────┘
```

Each partial class file shares the same namespace, class declaration, and has full access to all private/internal members defined in sibling partial files.

## Components and Interfaces

No new components or interfaces are introduced. All existing interfaces remain unchanged:

- `IGameDetectionService` — unchanged
- `IShaderPackService` — unchanged
- `IAuxInstallService` / `IAuxFileService` — unchanged
- `IModInstallService` — unchanged

### Split 1: MainViewModel (4417 lines → 6 files)

**Primary file: `ViewModels/MainViewModel.cs`** (~700 lines)
Contains: constructor, all DI field declarations, observable properties (`[ObservableProperty]`), forwarding properties (SearchQuery, FilterMode, etc.), UI callback delegates (ConfirmForeignDxgiOverwrite, ShowVulkanAdminRequiredDialog, etc.), Vulkan testable seams, `DeployAllShaders`, `DeployShadersForCard`, `ResolveShaderSelection`, `BuildScreenshotSavePath`, `OnHasUpdatesAvailableChanged`, `OnIsGridLayoutChanged`, `OnLumaFeatureEnabledChanged`, `OnSelectedGameChanged`, `SetDispatcher`, `SetShaderPackReadyTask`, `MarkInitialized`, `DisplayedGames`, `UnrealWarnings` constant, `FormatAge`.


**`ViewModels/MainViewModel.Init.cs`** (~600 lines)
Contains: `InitializeAsync`, `DetectAllGamesDedupedAsync`, `BuildCards`, `BuildNotes`, `ScanForInstalledAddon`, `ScanAddonShallow`, `ScanForInstalledAddonQuick`, `MakeGenericUnreal`, `MakeGenericUnity`, `ResolveAddonUrl`, `SaveLibrary`, `SaveLibraryPublic`, `GetGenericNote`, `NormalizeForLookup`, `RefreshAsync` (if present), `FullRefreshAsync` (if present).

**`ViewModels/MainViewModel.Install.cs`** (~700 lines)
Contains: All `[RelayCommand]` install/uninstall methods — `InstallModCommand`, `UninstallModCommand`, `InstallReShadeCommand`, `UninstallReShadeCommand`, `UninstallVulkanReShadeCommand`, `InstallREFrameworkCommand`, `UninstallREFrameworkCommand`, `InstallLumaAsync`, `UninstallLumaCommand`, `InstallReShadeVulkanAsync`, `ResolveAutoReShadeFilename`, `InstallUlAsync`, `UninstallUl`, `MatchLumaGame`, `IsLumaEnabled`, `ToggleLumaMode`, `ToggleUeExtended`, `AddManualGameCommand`, `ToggleHideGameCommand`, `ToggleFavouriteCommand`, `RemoveManualGameCommand`, `SetFilter`.

**`ViewModels/MainViewModel.Update.cs`** (~600 lines)
Contains: `UpdateAllRenoDxAsync`, `UpdateAllReShadeAsync`, `UpdateAllUlAsync`, `UpdateAllRefAsync`, `CheckForUpdatesAsync`, `UpdateAllEligible`, `CheckUlUpdateAsync`, `EnsureUlCachedAsync`, `FetchLatestUlReleaseInfoAsync`, `SaveUlMeta`, `ReadUlInstalledVersion`, `IsNewerVersion`, `PreCacheRemoteUlAsync`, `PreCacheRemoteUlVariantAsync`, `GetUlFileName`, `GetUlCachePath`, `IsNewVersion`, `MarkVersionSeen`, `GetRecentPatchNotes`, `NotifyUpdateButtonChanged`.

**`ViewModels/MainViewModel.Settings.cs`** (~500 lines)
Contains: `LoadNameMappings`, `SaveNameMappings`, `LoadThemeAndDensity`, `RenameGame`, `GetOriginalStoreName`, `RemoveGameRename`, `MigrateHashSet`, `MigrateDict`, `ApplyGameRenames`, `ApplyFolderOverrides`, `AddNameMapping`, `GetNameMapping`, `RemoveNameMapping`, `IsWikiExcluded`, `ToggleWikiExclusion`, `GetPerGameShaderMode`, `SetPerGameShaderMode`, `IsUpdateAllExcludedReShade/RenoDx/Ul`, `ToggleUpdateAllExclusionReShade/RenoDx/Ul`, `GetVulkanRenderingPath`, `SetVulkanRenderingPath`, `GetBitnessOverride`, `SetBitnessOverride`, `GetApiOverride`, `SetApiOverride`, `SetFolderOverride`, `ResetFolderOverride`, `GetFolderOverride`, `HasDllOverride`, `GetDllOverride`, `SetDllOverride`, `RemoveDllOverride`, `EnableDllOverride`, `UpdateDllOverrideNames`, `DisableDllOverride`, `SaveSettingsPublic`.

**`ViewModels/MainViewModel.GameMatching.cs`** (~500 lines)
Contains: `ApplyManifest`, `ApplyManifestStatusOverrides`, `ApplyManifestCardOverrides`, `ApplyManifestDllRenames`, `ApplyCardOverrides`, `IsNativeHdrGameMatch`, `IsUeExtendedGameMatch`, `ResolveIs32Bit`, `DetectGraphicsApi`, `_DetectAllApisForCard`, `ScanAllExesInDir`, `IsSystemOrRuntimeDll`, `ResolveEngineOverride`, `GetManifestDllNames`, `MatchesGameSet`.

### Split 2: GameDetectionService (1432 lines → 4 files)

**Primary file: `Services/GameDetectionService.cs`** (~500 lines)
Contains: class declaration, `_engineCache`, `MaxScanDepth`, shared helpers (`ReadRegistry`, `ExtractVdfValue`, `ExtractJsonString`, `NormalizeName`, `MatchGame`), engine detection (`DetectEngineAndPath`, `DetectEngineAndPathCore`, `IsUnityGame`, `IsUnrealLegacy`, `HasFileShallow`, `FindUEBinariesFolder`, `CollectUEBinaries`, `IsSkippedFolder`, `FindFileShallow`, `FindShallowExeFolder`).

**`Services/GameDetectionService.Steam.cs`** (~150 lines)
Contains: `FindSteamGames`, `FindSteamLibraries`.

**`Services/GameDetectionService.Platform.cs`** (~500 lines)
Contains: `FindGogGames`, `FindEpicGames`, `FindEaGames`, `FindUbisoftGames`, `FindBattleNetGames`, `FindRockstarGames`.

**`Services/GameDetectionService.Xbox.cs`** (~250 lines)
Contains: `FindXboxGames`, `FindXboxGamesFallback`, `ParseGamingRootFile`, `ResolveXboxGameRoot`.

### Split 3: ShaderPackService (1329 lines → 3 files)

**Primary file: `Services/ShaderPackService.cs`** (~450 lines)
Contains: class declaration, constructor, public path constants (`ShadersDir`, `TexturesDir`, `CustomShaderSentinel`, `CustomDir`, etc.), `GameReShadeShaders`, `GameReShadeOriginal`, `ManagedMarkerFile`, `ManagedMarkerContent`, `SourceKind` enum, `PackCategory` enum, `ShaderPack` record, `Packs` array, `ExcludedShaderFiles`, `ReShadeHeaders`, `SettingsPath`, `GetPackDescription`.

**`Services/ShaderPackService.Download.cs`** (~350 lines)
Contains: `EnsureLatestAsync`, `EnsurePackAsync`, `ResolveGhRelease`, `ResolveDirectUrlVersion`, `PackHasExtractedFiles`, `RecordExtractedFiles`, `FileListKey`, `LoadStoredVersion`, `SaveStoredVersion`, `VersionKey`.

**`Services/ShaderPackService.Deploy.cs`** (~550 lines)
Contains: `DeployToGameFolder`, `RemoveFromGameFolder`, `SyncGameFolder`, `SyncShadersToAllLocations`, `DeployPacksIfAbsent`, `DeployAllPacksIfAbsent`, `DeployFileListIfAbsent`, `DeployFolderIfAbsent`, `PruneFiles`, `AllKnownPackFiles`, `FilesForIds`, `PacksForIds`, `IsFileStale`, `IsManagedByRdxc`, `WriteMarker`, `RestoreOriginalIfPresent`, `MigrateLegacyDcShaders`, `EnsureReShadeHeaders`.

### Split 4: DetailPanelBuilder (1284 lines → 3 files)

**Primary file: `DetailPanelBuilder.cs`** (~450 lines)
Contains: class declaration, constructor, `_currentDetailCard` field, `CurrentDetailCard` property, `PopulateDetailPanel` (header, badges, author badges, install path, utility buttons, Luma badge).

**`DetailPanelBuilder.Components.cs`** (~350 lines)
Contains: `UpdateDetailComponentRows`, `DetailCard_PropertyChanged`, `GetMessageColor`.

**`DetailPanelBuilder.Overrides.cs`** (~500 lines)
Contains: `BuildOverridesPanel` and all its inline UI construction logic.

### Split 5: MainWindow (1335 lines → 3 files)

**Primary file: `MainWindow.xaml.cs`** (~450 lines)
Contains: constructor, field declarations (`_cardBuilder`, `_detailPanelBuilder`, `_overridesFlyoutBuilder`, `_dialogService`, `_settingsHandler`, `_installEventHandler`, `_windowStateManager`, `_dragDropHandler`, `_addonFileWatcher`), `MainWindow_Activated`, `MainWindow_Closed`, `HandleAddonFile`, `OnViewModelChanged`, `UpdatePageVisibility`, `GameList_SelectionChanged`, delegation methods (`PopulateDetailPanel`, `UpdateDetailComponentRows`, `DetailCard_PropertyChanged`, `BuildOverridesPanel`), `UpdateLumaToggleStyle`, `RequestReselect`, `TryRestoreSelection`, `Brush`, `BuildScreenshotSavePath`, `GetCardFromSender`, `PickFolderAsync`.

**`MainWindow.Events.cs`** (~500 lines)
Contains: `RefreshButton_Click`, `FullRefreshButton_Click`, `BrowseAddonWatchFolder_Click`, `ResetAddonWatchFolder_Click`, `RefreshWithScrollRestore`, `FullRefreshWithScrollRestore`, `RestoreScrollAndSelection`, `RsIniButton_Click`, `SupportDiscord_Click`, `SupportGuide_Click`, `SupportKofi_Click`, `LayoutToggle_Click`, `CardInstallFlyout_Opening`, `CardComponentInstall_Click`, `CardComponentUninstall_Click`, `CardCopyRsIni_Click`, `CardCopyUlIni_Click`, `CardInstallButton_Click`, `CardFavouriteButton_Click`, `CardOpenFolder_Click`, `CardOverridesButton_Click`, `CardMoreMenu_Click`, `Card_PointerPressed`, `NotesButton_Click`, `CardInfoLink_Click`, `CardNotesButton_Click`, `ExternalLink_Click`, `NameLink_Click`, `SettingsButton_Click`, `SkipUpdateToggle_Toggled`, `BetaOptInToggle_Toggled`, `VerboseLoggingToggle_Toggled`, `PatchNotesLink_Click`, `OpenLogsFolder_Click`, `OpenDownloadsFolder_Click`, `CustomShadersToggle_Toggled`, `ApplyScreenshotPath_Click`, `SettingsBack_Click`, `DetailScrollViewer_SizeChanged`, `SearchBox_TextChanged`, `AddGameButton_Click`, `Filter_Click`, `FavouriteButton_Click`, `ExpandComponents_Click`, `CombinedInstallButton_Click`, `InstallButton_Click`, `Install64Button_Click`, `Install32Button_Click`, `EnsurePathAndInstall`, `UninstallButton_Click`, `InstallRsButton_Click`, `UninstallRsButton_Click`, `ChooseShadersButton_Click`, `UpdateAllButton_Click`, `UpdateAllRenoDx_Click`, `UpdateAllReShade_Click`, `InstallUlButton_Click`, `UninstallUlButton_Click`, `InstallRefButton_Click`, `UninstallRefButton_Click`, `UlIniButton_Click`, `DetailUlStatus_PointerPressed`, `DetailRsStatus_PointerPressed`, `DetailRefStatus_PointerPressed`, `DetailRdxStatus_PointerPressed`, `LumaToggle_Click`, `SwitchToLumaButton_Click`, `InstallLumaButton_Click`, `UninstallLumaButton_Click`, `UeExtendedFlyoutItem_Click`, `ShowUeExtendedWarningAsync`, `HideButton_Click`, `BrowseFolder_Click`, `OpenFolder_Click`, `RemoveManualGame_Click`.

**`MainWindow.UISync.cs`** (~400 lines)
Contains: `RebuildCardGrid`, `BuildGameCard`, `ScrollToCard`, `OpenOverridesFlyout`, `RefreshFilterButtonStyles`, `Grid_DragOver`, `Grid_Drop`, dialog delegation methods (`ShowForeignDxgiConfirmDialogAsync`, `CheckForAppUpdateAsync`, `ShowUpdateDialogAsync`, `DownloadAndInstallUpdateAsync`, `ShowPatchNotesIfNewVersionAsync`, `ShowDcRemovalWarningAsync`, `ShowLegacyProgramFilesCleanupAsync`, `ShowPatchNotesDialogAsync`).

### Split 6: DragDropHandler (1203 lines → 3 files)

**Primary file: `DragDropHandler.cs`** (~300 lines)
Contains: class declaration, constructor, `_window`, `_crashReporter`, `ViewModel` property, `AllowedExtensions`, `IsAllowedExtension`, `Grid_DragOver`, `Grid_Drop` (top-level routing), `ExtractFileNameFromUrl`, `ParseUrlFromShortcutFile`, `HasPeSignature`.

**`DragDropHandler.Exe.cs`** (~250 lines)
Contains: `ProcessDroppedExe`, `InferGameRoot`, `LooksLikeGameRoot`, `InferGameName`, `CleanUnrealExeName`, `CleanFolderName`.

**`DragDropHandler.Addon.cs`** (~650 lines)
Contains: `ProcessDroppedAddon`, `ProcessDroppedArchive`, `ProcessDroppedUrl`.

### Split 7: AuxInstallService (976 lines → 4 files)

**Primary file: `Services/AuxInstallService.cs`** (~300 lines)
Contains: class declaration, constructor, `_http`, `_shaderPackService`, constants (`RsNormalName`, `RsStaged64`, `RsStaged32`, `TypeReShade`), staging paths (`RsStagingDir`, `RsStagedPath64`, `RsStagedPath32`), `EnsureReShadeStaging`, `EnsureInisDir`, `ReadInstalledVersion`, `SanitizeDirectoryName`, `DownloadCacheDir`, INI path constants (`InisDir`, `RsIniPath`, `RsVulkanIniPath`, `RsPresetIniPath`, `RsRdr2IniPath`, `UlIniPath`), `DbPath`, `IAuxFileService` explicit interface implementations.

**`Services/AuxInstallService.Ini.cs`** (~250 lines)
Contains: `MergeRsIni`, `MergeRsVulkanIni`, `CopyRsIni`, `CopyRsPresetIniIfPresent`, `CopyUlIni`, `ApplyScreenshotPath`, `ParseIni`, `WriteIni`, `OrderedDict` class, `IsRdr2`.

**`Services/AuxInstallService.DllIdentification.cs`** (~150 lines)
Contains: `IdentifyDxgiFile`, `DxgiFileType` enum, `BackupForeignDll`, `RestoreForeignDll`, `IsReShadeFileStrict`, `IsReShadeFile`.

**`Services/AuxInstallService.Install.cs`** (~300 lines)
Contains: `InstallReShadeAsync`, `CheckReShadeUpdateLocal`, `CheckForUpdateAsync`, `Uninstall`, `UninstallDllOnly`, `LoadAll`, `FindRecord`, `SaveRecord`, `SaveAuxRecord`, `RemoveRecord`, `LoadDb`, `SaveDb`.

### Split 8: CardBuilder (824 lines → 2 files)

**Primary file: `CardBuilder.cs`** (~400 lines)
Contains: class declaration, constructor, `BuildGameCard` (card border, header, status dots, action row, bottom row, PropertyChanged subscription).

**`CardBuilder.Flyout.cs`** (~430 lines)
Contains: `BuildInstallFlyoutContent`, `BuildComponentRow`, `UpdateComponentRow`, and flyout-related PropertyChanged handler logic.

### Split 9: DialogService (785 lines → 3 files)

**Primary file: `DialogService.cs`** (~100 lines)
Contains: class declaration, constructor, `_window`, `_dispatcherQueue`, `PatchNotesDir`, `ViewModel` property, `Brush` helper, `ParseColor` helper, `GetCardFromSender`.

**`DialogService.Update.cs`** (~400 lines)
Contains: `CheckForAppUpdateAsync`, `ShowUpdateDialogAsync`, `DownloadAndInstallUpdateAsync`, `ShowPatchNotesIfNewVersionAsync`, `ShowPatchNotesDialogAsync`, `ShowDcRemovalWarningAsync`, `ShowLegacyProgramFilesCleanupAsync`, `WriteCleanupMarker`, `DcWarningMarkerPath`, `LegacyCleanupMarkerPath`.

**`DialogService.Game.cs`** (~300 lines)
Contains: `ShowForeignDxgiConfirmDialogAsync`, `ShowForeignWinmmConfirmDialogAsync`, `NotesButton_Click`.

## Data Models

No data models are changed. All existing model classes (`DetectedGame`, `GameMod`, `GameCardViewModel`, `InstalledModRecord`, `AuxInstalledRecord`, `DllOverrideConfig`, `RemoteManifest`, etc.) remain unchanged.

### Shared State Handling Across Partial Files

C# partial classes are a single type at compile time. All fields, properties, and methods declared in any partial file are accessible from all other partial files of the same class. Therefore:

1. **Fields and DI dependencies** declared in the primary file (e.g., `_installer`, `_http`, `_crashReporter` in `MainViewModel.cs`) are directly accessible from `MainViewModel.Install.cs`, `MainViewModel.Update.cs`, etc.
2. **Observable properties** generated by `[ObservableProperty]` attributes (e.g., `StatusText`, `IsLoading`) are accessible from all partial files.
3. **Private helper methods** can be called across partial files without any access modifier changes.
4. **No `internal` or `public` promotions needed** — the split is purely file-level, not type-level.

### Ordering Considerations

1. **MainViewModel must be split first** — it is the largest file (4417 lines) and the most complex. Other files reference it but don't need to change.
2. **Services can be split in parallel** — `GameDetectionService`, `ShaderPackService`, and `AuxInstallService` are independent of each other.
3. **UI builders depend on MainWindow** — `CardBuilder`, `DetailPanelBuilder`, `DialogService`, and `DragDropHandler` all take a `MainWindow` reference, but since we're only splitting (not changing signatures), order doesn't matter.
4. **MainWindow should be split after its helper classes** — to verify that delegation methods still compile correctly.
5. **Each split should be followed by a build verification** — `dotnet build` after each class split to catch issues early.


## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Compilation equivalence after split

*For any* class that was split into partial class files, the project SHALL compile with zero errors and zero new warnings. This is the fundamental invariant of a structural refactoring: the compiled output is functionally identical before and after.

**Validates: Requirements 1.4, 2.5, 3.4, 4.3, 5.3, 6.3, 7.4, 8.3, 9.3, 11.1, 11.2**

### Property 2: Public API surface preservation

*For any* public or internal member (method, property, field, event) of a split class, the member SHALL exist with an identical signature (name, return type, parameter types, access modifier) after the split. The set of public/internal members before the split SHALL equal the set after the split — no members added, removed, or signature-changed.

**Validates: Requirements 11.3, 11.4**

### Property 3: File naming and location convention

*For any* partial class file created by this refactoring, the file SHALL (a) follow the naming pattern `{ClassName}.{Concern}.cs`, (b) reside in the same directory as the original primary file, and (c) the primary file SHALL retain its original filename.

**Validates: Requirements 12.1, 12.2, 12.3**

### Property 4: File size constraints

*For any* source file in the `RenoDXCommander/` directory (excluding `obj/` and `bin/`), the file SHALL contain fewer than 800 lines. Additionally, *for any* partial class file created by this refactoring, the file SHALL contain fewer than 600 lines.

**Validates: Requirements 12.4, 13.1**

## Error Handling

No new error handling is introduced. This is a structural refactoring — all existing error handling (try/catch blocks, null checks, logging via `_crashReporter`) remains in place within the methods that are moved to partial files.

The only error scenario specific to this refactoring is a failed build after a split. The mitigation is:

1. **Split one class at a time** — verify compilation after each split before proceeding.
2. **Missing `using` directives** — each partial file must include the `using` directives needed by its members. A missing `using` will cause a build error that is immediately obvious and trivially fixable.
3. **Missing `partial` keyword** — forgetting to add `partial` to the class declaration in a new file will cause a "duplicate type" build error. Also immediately obvious.

## Testing Strategy

### Dual Testing Approach

This refactoring is unusual in that the primary correctness guarantee comes from the C# compiler itself. If the project compiles after the split, the runtime behaviour is guaranteed to be identical (partial classes produce the same IL as a single file).

**Unit tests**: Verify specific structural examples:
- Each expected partial class file exists
- Each file contains the `partial class` keyword
- Each file has the correct namespace
- Each file has a file-level responsibility comment
- Interface files are unmodified
- ModInstallService and OverridesFlyoutBuilder remain single files

**Property tests**: Verify universal properties across all split files:
- Property 2 (API surface preservation): Use reflection to enumerate all public/internal members of each split class and compare against a baseline snapshot taken before the refactoring. *For all* members in the baseline, the member must exist with the same signature in the refactored code.
- Property 3 (naming convention): *For all* created partial class files, verify the filename matches `{ClassName}.{Concern}.cs` and resides in the correct directory.
- Property 4 (file size): *For all* `.cs` files in `RenoDXCommander/` (excluding `obj/`, `bin/`), verify line count < 800. *For all* partial class files created by this spec, verify line count < 600.

### Property-Based Testing Configuration

- Library: **FsCheck** (via `FsCheck.Xunit`) or **CsCheck** for C# property-based testing
- Minimum 10 iterations per property test (though for file-based properties, the iteration count equals the number of files)
- Each test tagged with: **Feature: large-file-refactor, Property {number}: {property_text}**
- Each correctness property implemented by a single property-based test

### Build Verification

The most critical test is simply:
```bash
dotnet build RenoDXCommander/RenoDXCommander.csproj --no-incremental
```
A successful build with zero errors and zero new warnings validates Property 1 completely. This should be run after each individual class split.
