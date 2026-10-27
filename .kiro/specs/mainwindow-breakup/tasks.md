# Tasks

## Task 1: Extract OverridesFlyoutBuilder
- [x] 1.1 Create `RenoDXCommander/OverridesFlyoutBuilder.cs` with constructor accepting `MainWindow`
- [x] 1.2 Move `OpenOverridesFlyout` method and all its inline helpers (MakeSeparator, DLL sync logic) from MainWindow to OverridesFlyoutBuilder
- [x] 1.3 Replace `OpenOverridesFlyout` in MainWindow with one-line delegation to `_overridesFlyoutBuilder.OpenOverridesFlyout`
- [x] 1.4 Add `_overridesFlyoutBuilder` field and instantiation in MainWindow constructor
- [x] 1.5 Verify build compiles with `dotnet build -p:Platform=x64`

## Task 2: Extract DialogService
- [x] 2.1 Create `RenoDXCommander/DialogService.cs` with constructor accepting `MainWindow`
- [x] 2.2 Move `CheckForAppUpdateAsync`, `ShowUpdateDialogAsync`, `DownloadAndInstallUpdateAsync` from MainWindow to DialogService
- [x] 2.3 Move `ShowPatchNotesIfNewVersionAsync`, `ShowPatchNotesDialogAsync` from MainWindow to DialogService
- [x] 2.4 Move `ShowForeignDxgiConfirmDialogAsync`, `ShowForeignWinmmConfirmDialogAsync` from MainWindow to DialogService
- [x] 2.5 Move `NotesButton_Click` and `ShowUeExtendedWarningAsync` dialog logic from MainWindow to DialogService
- [x] 2.6 Replace moved methods in MainWindow with one-line delegations to `_dialogService`
- [x] 2.7 Wire ViewModel confirm delegates (`ConfirmForeignDxgiOverwrite`, `ConfirmForeignWinmmOverwrite`) to DialogService methods
- [x] 2.8 Add `_dialogService` field and instantiation in MainWindow constructor
- [x] 2.9 Verify build compiles with `dotnet build -p:Platform=x64`

## Task 3: Extract SettingsHandler
- [x] 3.1 Create `RenoDXCommander/SettingsHandler.cs` with constructor accepting `MainWindow`
- [x] 3.2 Move `SettingsButton_Click`, `SettingsBack_Click`, `SkipUpdateToggle_Toggled`, `BetaOptInToggle_Toggled`, `VerboseLoggingToggle_Toggled`, `OpenLogsFolder_Click`, `OpenDownloadsFolder_Click` from MainWindow to SettingsHandler
- [x] 3.3 Replace moved methods in MainWindow with one-line delegations to `_settingsHandler`
- [x] 3.4 Add `_settingsHandler` field and instantiation in MainWindow constructor
- [x] 3.5 Verify build compiles with `dotnet build -p:Platform=x64`

## Task 4: Extract InstallEventHandler
- [x] 4.1 Create `RenoDXCommander/InstallEventHandler.cs` with constructor accepting `MainWindow` and `Func<string?, Task<string?>>` folder-picker delegate
- [x] 4.2 Move detail-panel install handlers (`CombinedInstallButton_Click`, `InstallButton_Click`, `Install64Button_Click`, `Install32Button_Click`, `EnsurePathAndInstall`) from MainWindow to InstallEventHandler
- [x] 4.3 Move detail-panel uninstall handlers (`UninstallButton_Click`, `UninstallRsButton_Click`, `UninstallDcButton_Click`, `UninstallLumaButton_Click`) from MainWindow to InstallEventHandler
- [x] 4.4 Move component-specific install handlers (`InstallRsButton_Click`, `InstallDcButton_Click`, `InstallLumaButton_Click`) from MainWindow to InstallEventHandler
- [x] 4.5 Move `DeployDcModeButton_Click`, `ChooseShadersButton_Click`, `LumaToggle_Click`, `SwitchToLumaButton_Click`, `UeExtendedFlyoutItem_Click` from MainWindow to InstallEventHandler
- [x] 4.6 Replace moved methods in MainWindow with one-line delegations to `_installEventHandler`
- [x] 4.7 Add `_installEventHandler` field and instantiation in MainWindow constructor (passing `PickFolderAsync`)
- [x] 4.8 Verify build compiles with `dotnet build -p:Platform=x64`

## Task 5: Extract WindowStateManager
- [x] 5.1 Create `RenoDXCommander/WindowStateManager.cs` with constructor accepting `Window`, `IntPtr hwnd`, and `DragDropHandler`
- [x] 5.2 Move `WndProc`, `HandleWin32Drop` from MainWindow to WindowStateManager
- [x] 5.3 Move `TryRestoreWindowBounds`, `CaptureCurrentBounds`, `RestoreWindowBounds`, `SaveWindowBounds` from MainWindow to WindowStateManager
- [x] 5.4 Move `_hwnd`, `_origWndProc`, `_wndProcDelegate`, `_windowBounds`, `_windowSettingsPath` fields from MainWindow to WindowStateManager
- [x] 5.5 Add `InstallWndProcSubclass()` and `EnableDragAccept()` methods that encapsulate the Win32 setup currently in MainWindow constructor
- [x] 5.6 Update MainWindow constructor to delegate Win32 setup to `_windowStateManager.InstallWndProcSubclass()` and `_windowStateManager.EnableDragAccept()`
- [x] 5.7 Update `MainWindow_Activated` and `MainWindow_Closed` to delegate to WindowStateManager
- [x] 5.8 Add `_windowStateManager` field and instantiation in MainWindow constructor
- [x] 5.9 Verify build compiles with `dotnet build -p:Platform=x64`

## Task 6: Remove duplicate drag-drop code from MainWindow
- [x] 6.1 Delete `Grid_DragOver`, `Grid_Drop` methods from MainWindow
- [x] 6.2 Delete `ProcessDroppedExe`, `ProcessDroppedArchive`, `ProcessDroppedAddon` methods from MainWindow
- [x] 6.3 Delete `InferGameRoot`, `LooksLikeGameRoot`, `InferGameName`, `CleanUnrealExeName`, `CleanFolderName` methods from MainWindow
- [x] 6.4 Delete the duplicate `ArchiveExtensions` field from MainWindow
- [x] 6.5 Wire XAML drag-over/drop events to existing `DragDropHandler` instance (ensure `_dragDropHandler` field exists and is instantiated)
- [x] 6.6 Update `HandleWin32Drop` in WindowStateManager to route to `DragDropHandler` methods
- [x] 6.7 Verify build compiles with `dotnet build -p:Platform=x64`

## Task 7: Expose required XAML elements
- [x] 7.1 Audit all extracted classes for references to XAML-named elements (e.g. `DetailRsMessage`, `DetailGenericText`, `SettingsPanel`, etc.)
- [x] 7.2 Add `internal` properties on MainWindow for any XAML elements accessed by extracted classes (following the pattern already used by `DetailPanelBuilder`)
- [x] 7.3 Verify build compiles with `dotnet build -p:Platform=x64`

## Task 8: Write reflection-based verification tests
- [x] 8.1 Create test project (if not existing) with xUnit and FsCheck references, configured with `-p:Platform=x64`
- [x] 8.2 Write unit test verifying each extracted class exists in `RenoDXCommander` namespace with required methods (Property 1)
- [x] 8.3 Write unit test verifying MainWindow does NOT contain removed drag-drop methods (Property 2)
- [x] 8.4 Write unit test verifying MainWindow.xaml.cs is under 1200 lines (Property 4)
- [x] 8.5 Run all tests with `dotnet test -p:Platform=x64` and verify they pass
