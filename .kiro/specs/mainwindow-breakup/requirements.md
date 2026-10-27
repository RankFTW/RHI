# Requirements Document

## Introduction

MainWindow.xaml.cs is the largest file in the RenoDXCommander codebase at ~3400 lines. It contains event handlers, flyout construction, dialog logic, update workflows, settings handlers, drag-and-drop processing, game inference heuristics, window persistence, and navigation — all in a single code-behind file. This spec defines the requirements for breaking it up into focused builder/service classes, following the extraction pattern already established by `CardBuilder` and `DetailPanelBuilder`.

The goal is improved maintainability, testability, and developer navigation without changing any user-visible behaviour.

## Glossary

- **MainWindow**: The primary WinUI 3 `Window` subclass (`MainWindow.xaml.cs`) that serves as the application shell.
- **Builder**: A helper class that receives a reference to `MainWindow` (or its ViewModel/DispatcherQueue) and encapsulates imperative UI construction or event-handler logic. Examples: `CardBuilder`, `DetailPanelBuilder`.
- **OverridesFlyoutBuilder**: The new builder responsible for constructing the per-game overrides flyout (currently the `OpenOverridesFlyout` method, ~700 lines).
- **DialogService**: The new service responsible for app-update dialogs, patch-notes dialogs, confirmation dialogs, and the game-notes dialog.
- **SettingsHandler**: The new handler class responsible for settings-page event handlers and toggle logic.
- **InstallEventHandler**: The new handler class responsible for detail-panel install/uninstall button click handlers and related path-picking logic.
- **WindowStateManager**: The new helper responsible for window bounds persistence (save/restore), minimum-size enforcement via WndProc subclass, and Win32 drag-accept setup.
- **DragDropHandler**: The existing `DragDropHandler.cs` class that already contains extracted drag-and-drop processing, game-root inference, and game-name inference logic.
- **MainViewModel**: The primary ViewModel (`MainViewModel.cs`) using CommunityToolkit.Mvvm.
- **GameCardViewModel**: The per-game card ViewModel displayed in the game list and grid.

## Requirements

### Requirement 1: Extract Overrides Flyout Construction

**User Story:** As a developer, I want the overrides flyout construction logic extracted into a dedicated `OverridesFlyoutBuilder` class, so that the ~700-line `OpenOverridesFlyout` method no longer lives in MainWindow.xaml.cs.

#### Acceptance Criteria

1. WHEN `OpenOverridesFlyout` is called, THE MainWindow SHALL delegate to `OverridesFlyoutBuilder` to construct and show the flyout.
2. THE OverridesFlyoutBuilder SHALL follow the same constructor-injection pattern as `CardBuilder` (receiving `MainWindow` or the required dependencies).
3. THE OverridesFlyoutBuilder SHALL produce a flyout that is visually and functionally identical to the current implementation.
4. WHEN the extraction is complete, THE MainWindow SHALL contain only a one-line delegation call for overrides flyout display.

### Requirement 2: Extract Dialog and Update Logic

**User Story:** As a developer, I want update dialogs, patch-notes dialogs, confirmation dialogs, and the game-notes dialog extracted into a `DialogService`, so that MainWindow no longer contains ~400 lines of dialog construction and async update workflow.

#### Acceptance Criteria

1. THE DialogService SHALL encapsulate `CheckForAppUpdateAsync`, `ShowUpdateDialogAsync`, `DownloadAndInstallUpdateAsync`, `ShowPatchNotesIfNewVersionAsync`, `ShowPatchNotesDialogAsync`, `ShowForeignDxgiConfirmDialogAsync`, `ShowForeignWinmmConfirmDialogAsync`, and `NotesButton_Click` dialog logic.
2. WHEN MainWindow needs to show a dialog, THE MainWindow SHALL delegate to the DialogService.
3. THE DialogService SHALL accept `XamlRoot` and `DispatcherQueue` as dependencies so that dialog display remains on the UI thread.
4. THE DialogService SHALL preserve all existing dialog behaviour including progress reporting during update downloads.

### Requirement 3: Extract Settings Event Handlers

**User Story:** As a developer, I want settings-page event handlers extracted into a `SettingsHandler` class, so that toggle and navigation handlers are grouped separately from the main window logic.

#### Acceptance Criteria

1. THE SettingsHandler SHALL encapsulate `SettingsButton_Click`, `SettingsBack_Click`, `SkipUpdateToggle_Toggled`, `BetaOptInToggle_Toggled`, `VerboseLoggingToggle_Toggled`, `OpenLogsFolder_Click`, and `OpenDownloadsFolder_Click`.
2. WHEN a settings event fires, THE MainWindow SHALL delegate to the SettingsHandler.
3. THE SettingsHandler SHALL interact with MainViewModel for state changes and persistence.
4. WHEN the extraction is complete, THE MainWindow SHALL contain only one-line delegation calls for each settings event handler.

### Requirement 4: Extract Detail-Panel Install/Uninstall Event Handlers

**User Story:** As a developer, I want the detail-panel install, uninstall, and component-action button handlers extracted into an `InstallEventHandler` class, so that the ~250 lines of install/uninstall click handlers are separated from MainWindow.

#### Acceptance Criteria

1. THE InstallEventHandler SHALL encapsulate `CombinedInstallButton_Click`, `InstallButton_Click`, `Install64Button_Click`, `Install32Button_Click`, `EnsurePathAndInstall`, `UninstallButton_Click`, `InstallRsButton_Click`, `UninstallRsButton_Click`, `InstallDcButton_Click`, `UninstallDcButton_Click`, `InstallLumaButton_Click`, `UninstallLumaButton_Click`, `DeployDcModeButton_Click`, `ChooseShadersButton_Click`, `LumaToggle_Click`, `SwitchToLumaButton_Click`, and `UeExtendedFlyoutItem_Click`.
2. THE InstallEventHandler SHALL accept a folder-picker delegate so that path selection remains tied to the window handle.
3. WHEN an install or uninstall button is clicked, THE MainWindow SHALL delegate to the InstallEventHandler.
4. THE InstallEventHandler SHALL interact with MainViewModel commands for all install and uninstall operations.

### Requirement 5: Extract Window State Management

**User Story:** As a developer, I want window bounds persistence, WndProc subclassing, and Win32 drag-accept setup extracted into a `WindowStateManager` class, so that low-level Win32 interop is isolated from UI logic.

#### Acceptance Criteria

1. THE WindowStateManager SHALL encapsulate `TryRestoreWindowBounds`, `CaptureCurrentBounds`, `RestoreWindowBounds`, `SaveWindowBounds`, `WndProc`, and `HandleWin32Drop`.
2. THE WindowStateManager SHALL manage the `_hwnd`, `_origWndProc`, `_wndProcDelegate`, and `_windowBounds` fields.
3. WHEN MainWindow is constructed, THE MainWindow SHALL delegate Win32 subclass setup to the WindowStateManager.
4. WHEN MainWindow is closed, THE MainWindow SHALL delegate window bounds saving to the WindowStateManager.
5. WHEN a WM_DROPFILES message is received, THE WindowStateManager SHALL route dropped files to the existing `DragDropHandler` for processing.

### Requirement 6: Remove Duplicate Drag-Drop Code from MainWindow

**User Story:** As a developer, I want the duplicated drag-and-drop methods removed from MainWindow.xaml.cs, so that `DragDropHandler.cs` is the single source of truth for drag-drop processing and game inference.

#### Acceptance Criteria

1. WHEN the extraction is complete, THE MainWindow SHALL NOT contain `Grid_DragOver`, `Grid_Drop`, `ProcessDroppedExe`, `ProcessDroppedArchive`, `ProcessDroppedAddon`, `InferGameRoot`, `LooksLikeGameRoot`, `InferGameName`, `CleanUnrealExeName`, or `CleanFolderName` methods.
2. THE MainWindow SHALL delegate all XAML drag-over and drop events to the existing `DragDropHandler` instance.
3. THE DragDropHandler SHALL remain the sole implementation of drag-drop processing and game-inference heuristics.

### Requirement 7: Preserve Behavioural Equivalence

**User Story:** As a developer, I want the refactoring to produce zero user-visible changes, so that the extraction is a safe, purely structural improvement.

#### Acceptance Criteria

1. THE application SHALL produce identical UI output and user interactions after the extraction.
2. THE application SHALL compile without errors or new warnings introduced by the extraction.
3. IF an extracted class needs access to XAML-named elements, THEN THE MainWindow SHALL expose those elements via `internal` properties or pass them as method parameters.
4. THE MainWindow constructor SHALL instantiate all new builder and handler classes following the same pattern used for `CardBuilder` and `DetailPanelBuilder`.

### Requirement 8: Maintain Consistent Extraction Pattern

**User Story:** As a developer, I want all new extracted classes to follow the same structural pattern as `CardBuilder` and `DetailPanelBuilder`, so that the codebase remains consistent and predictable.

#### Acceptance Criteria

1. THE new builder and handler classes SHALL reside in the `RenoDXCommander` namespace at the project root level (same location as `CardBuilder.cs` and `DetailPanelBuilder.cs`).
2. THE new classes SHALL accept dependencies via constructor injection.
3. THE new classes SHALL use `internal` or `public` visibility for methods called by MainWindow event wiring.
4. THE MainWindow code-behind SHALL be reduced to under 1200 lines after all extractions are complete.
