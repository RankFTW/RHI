# Refactoring Roadmap

## ✅ 1. Extract duplicated file-write retry logic into a shared helper
- **Status:** Done
- **Risk:** Low
- **Spec:** `.kiro/specs/file-write-retry-helper/`
- Extracted `FileHelper.WriteAllTextWithRetry` and replaced 6 call sites

## ✅ 2. Fix blocking async patterns (`.Wait()`, `.Result`)
- **Status:** Done
- **Risk:** Medium
- **Impact:** High — prevents potential deadlocks, improves responsiveness
- Converted `GameInitializationService.DetectAllGamesDeduped()` to `DetectAllGamesDedupedAsync()` using `await Task.WhenAll()` instead of `.Wait()`
- Replaced all `.Result` accesses in `MainViewModel.InitializeAsync` with proper `await` on already-awaited tasks
- Updated `IGameInitializationService` interface to match

## ✅ 3. Add logging to bare catch blocks
- **Status:** Done
- **Risk:** Low
- **Impact:** Medium — improves debuggability
- Added `CrashReporter.Log` calls to all silent catch blocks across 9 production files
- `GameDetectionService` — 7 bare catches in EA/Ubisoft/Battle.net/Rockstar scan methods
- `MainViewModel` — 13 bare catches in DetectGraphicsApi, ScanAllExesInDir, game notes lookup, ToggleLumaMode, InitializeAsync lumaMods, SaveLibrary fire-and-forget, addon scan methods
- `SettingsViewModel` — 2 bare catches in LoadSettingsFile and shader pack deserialization
- `MainWindow.xaml.cs` — 2 bare catches in Activated handler and Card_PointerPressed
- `DragDropHandler` — 4 bare catches in archive cleanup, existing addon check, LooksLikeGameRoot, InferGameName
- `DialogService` — 3 bare catches in patch notes marker cleanup and UE warning dialog
- `DetailPanelBuilder` — 1 bare catch in reshade.ini delete
- `CardBuilder` — 2 bare catches in PropertyChanged handlers
- `ValueConverters` — 1 bare catch in HexColorToBrush
- Intentionally skipped: `CrashReporter.cs` (must never crash), `WindowStateManager.cs`/`WindowStateService.cs` (best-effort persistence), test files (cleanup code)

## ✅ 4. Break up MainWindow.xaml.cs (~3400 → ~1187 lines)
- **Status:** Done
- **Risk:** Medium-High
- **Impact:** High — most impactful for maintainability
- **Spec:** `.kiro/specs/mainwindow-breakup/`
- Extracted 5 new classes following the `CardBuilder`/`DetailPanelBuilder` pattern:
  - `OverridesFlyoutBuilder` — per-game overrides flyout (~730 lines)
  - `DialogService` — update dialogs, patch notes, confirmation dialogs, game notes (~400 lines)
  - `SettingsHandler` — settings page event handlers and toggle logic (~100 lines)
  - `InstallEventHandler` — install/uninstall button handlers (~250 lines)
  - `WindowStateManager` — Win32 WndProc subclass, window bounds persistence, drag-accept (~200 lines)
- Removed ~700 lines of duplicate drag-drop code (already in `DragDropHandler`)
- MainWindow retains one-line delegation stubs, constructor/lifecycle, ViewModel sync, navigation, and `PickFolderAsync`
- 7 reflection-based verification tests confirm structural correctness

## ✅ 5. Wrap static services behind interfaces
- **Status:** Done
- **Risk:** Medium
- **Impact:** Medium — cleaner DI, better testability
- **Spec:** `.kiro/specs/static-service-interfaces/`
- Created `ICrashReporter` interface and `CrashReporterService` wrapper delegating to the static `CrashReporter` class
- Created `IAuxFileService` interface for 17 static file-identification/INI-management methods on `AuxInstallService`; implemented via explicit interface delegation on the same class
- Registered both interfaces as singletons in the DI container (`IAuxFileService` reuses the existing `AuxInstallService` singleton)
- Migrated 8 consumer classes to constructor injection: `MainViewModel`, `UpdateOrchestrationService`, `MainWindow`, `DragDropHandler`, `WindowStateManager`, `OverridesFlyoutBuilder`, `LumaService`
- Static constants/paths (`TypeDc`, `RsStagingDir`, etc.) intentionally left as static per design
- 3 property-based test classes (FsCheck) + 1 structural/DI reflection test class

## 6. Minor cleanup
- **Status:** Not started
- **Risk:** Low
- **Impact:** Low
- Hardcoded title bar colors in `MainWindow` constructor → reference theme resources
- Unused `using` directives scattered across files
