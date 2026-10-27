# Implementation Plan: Compact View Mode

## Overview

Add a third "Compact" view mode to the RHI WinUI 3 application. The implementation replaces the `IsGridLayout` boolean with a `ViewLayout` enum, adds a `CompactViewBuilder` class following the existing builder pattern, extends `WindowStateManager` with size-locking, adds XAML elements for the compact container with navigation arrows, and updates settings persistence with backward compatibility. Property-based tests use FsCheck.Xunit to validate correctness properties from the design.

## Tasks

- [x] 1. Create ViewLayout enum and update MainViewModel state model
  - [x] 1.1 Create `RenoDXCommander/Models/ViewLayout.cs` with the `ViewLayout` enum (Detail=0, Grid=1, Compact=2)
    - _Requirements: 2.1_
  - [x] 1.2 Replace `[ObservableProperty] private bool _isGridLayout` with `[ObservableProperty] private ViewLayout _currentViewLayout` in `MainViewModel.cs`
    - Add `[ObservableProperty] private int _compactPageIndex = 0`
    - Add computed properties: `DetailPanelVisibility`, `CardGridVisibility`, `CompactViewVisibility`
    - Add `LayoutToggleLabel` returning "Grid View" / "Compact View" / "Detail View"
    - Add backward-compatible `IsGridLayout` property (`=> CurrentViewLayout == ViewLayout.Grid`)
    - Implement `OnCurrentViewLayoutChanged` to raise PropertyChanged for all dependent properties
    - Add `NextViewLayout()` method for cycling Detail → Grid → Compact → Detail
    - Add `NavigateCompactPage(int delta)` method using modular arithmetic `((index + delta) % 3 + 3) % 3`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 1.4, 1.5, 1.6, 4.3–4.8_
  - [x] 1.3 Update all existing references to `IsGridLayout` across the codebase
    - `MainWindow.UISync.cs`: update `OnViewModelChanged` to use `CurrentViewLayout` / `IsGridLayout` compat property
    - `MainWindow.xaml.cs`: update `GameList_SelectionChanged` to handle `ViewLayout.Compact` case
    - `MainWindow.Events.cs`: update `LayoutToggle_Click` to use `NextViewLayout()` cycling
    - `MainWindow.xaml`: update `LayoutToggleBtn` Content binding and visibility bindings
    - _Requirements: 1.1, 1.2, 1.3, 2.2_

- [x] 2. Update settings persistence for ViewLayout
  - [x] 2.1 Update `IGameNameService` interface: change `LoadNameMappings` parameter from `Action<bool> setIsGridLayout` to `Action<ViewLayout> setViewLayout`, and `SaveNameMappings` parameter from `bool isGridLayout` to `ViewLayout currentViewLayout`
    - _Requirements: 1.7, 9.1, 9.3_
  - [x] 2.2 Update `GameNameService.LoadNameMappings` to read `ViewLayout` key (int parse), with fallback to `GridLayout` key for backward compatibility; use `Enum.IsDefined` guard for invalid values defaulting to `ViewLayout.Detail`
    - _Requirements: 9.1, 9.3_
  - [x] 2.3 Update `GameNameService.SaveNameMappings` to write `ViewLayout` key as integer string instead of `GridLayout`
    - _Requirements: 1.7, 9.3_
  - [x] 2.4 Update `MainViewModel.Settings.cs` callers of `LoadNameMappings` and `SaveNameMappings` to pass `ViewLayout` enum instead of `bool`
    - _Requirements: 1.7, 9.1_

- [x] 3. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise. The existing `LayoutTogglePropertyTests` will need updating to use the new `ViewLayout` enum instead of `IsGridLayout` boolean.

- [x] 3.1 Write property tests for ViewLayout cycling and visibility (Properties 1, 2, 5)
    - **Property 1: View mode cycling is a 3-cycle** — For any ViewLayout value, calling NextViewLayout() three times returns the original value
    - **Property 2: Layout toggle label names the next mode** — For any ViewLayout value, LayoutToggleLabel contains the name of the next mode
    - **Property 5: Exactly one view container is visible per layout** — For any ViewLayout value, exactly one of DetailPanelVisibility, CardGridVisibility, CompactViewVisibility is Visible
    - Create `RenoDXCommander.Tests/ViewLayoutCyclePropertyTests.cs` and `ViewLayoutVisibilityPropertyTests.cs`
    - **Validates: Requirements 1.1–1.6, 2.3, 2.4, 2.5**

- [x] 3.2 Write property tests for ViewLayout persistence and notifications (Properties 3, 4)
    - **Property 3: ViewLayout persistence round-trip** — For any ViewLayout enum value, serializing to int string and parsing back produces the original value
    - **Property 4: ViewLayout change raises all dependent property notifications** — For any ViewLayout value, setting CurrentViewLayout raises PropertyChanged for DetailPanelVisibility, CardGridVisibility, CompactViewVisibility, LayoutToggleLabel, IsGridLayout
    - Create `RenoDXCommander.Tests/ViewLayoutPersistencePropertyTests.cs` and `ViewLayoutNotificationPropertyTests.cs`
    - **Validates: Requirements 1.7, 2.2, 9.1, 9.3**

- [x] 3.3 Write property tests for compact page navigation (Properties 6, 7)
    - **Property 6: Compact page navigation is modular arithmetic** — For any starting CompactPageIndex in {0,1,2} and delta of +1 or -1, NavigateCompactPage produces ((start+delta)%3+3)%3, always in [0,2]
    - **Property 7: Page index retained across game selection** — For any CompactPageIndex and any SelectedGame change, CompactPageIndex remains unchanged
    - Create `RenoDXCommander.Tests/CompactPageNavigationPropertyTests.cs` and `CompactPageRetentionPropertyTests.cs`
    - **Validates: Requirements 4.3–4.8, 10.4**

- [x] 4. Add XAML elements for Compact view container
  - [x] 4.1 Add `CompactViewContainer` Grid to `MainWindow.xaml` in Column 1 of `GameViewPanel`, alongside existing `DetailScrollViewer` and `CardGridScrollViewer`
    - Three-column Grid: left nav arrow button, ScrollViewer with `CompactPagePanel` StackPanel, right nav arrow button
    - Bind Visibility to `ViewModel.CompactViewVisibility`
    - Wire Click handlers: `CompactNavLeft_Click`, `CompactNavRight_Click`
    - _Requirements: 3.2, 4.1, 4.2, 4.9_
  - [x] 4.2 Add `CompactNavLeft_Click` and `CompactNavRight_Click` event handlers in `MainWindow.Events.cs`
    - Call `ViewModel.NavigateCompactPage(-1)` / `ViewModel.NavigateCompactPage(1)`
    - Rebuild current page via `_compactViewBuilder.RebuildCurrentPage()`
    - _Requirements: 4.3–4.8_
  - [x] 4.3 Update `LayoutToggle_Click` in `MainWindow.Events.cs` to cycle through three modes using `NextViewLayout()`
    - Handle window size locking transitions (capture bounds → apply compact size → lock; or unlock → restore bounds)
    - Rebuild content for the new layout (Grid: RebuildCardGrid, Detail: PopulateDetailPanel, Compact: RebuildCurrentPage)
    - _Requirements: 1.1, 1.2, 1.3, 8.1, 8.2, 8.4, 8.5_
  - [x] 4.4 Update `GameList_SelectionChanged` in `MainWindow.xaml.cs` to handle `ViewLayout.Compact` case
    - Rebuild current compact page for newly selected game, retaining page index
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

- [x] 5. Create CompactViewBuilder class
  - [x] 5.1 Create `RenoDXCommander/CompactViewBuilder.cs` with constructor taking `MainWindow` reference
    - Follow existing builder pattern (same as `CardBuilder`, `DetailPanelBuilder`)
    - Add `RebuildCurrentPage(StackPanel target, GameCardViewModel card, int pageIndex)` method that clears target and dispatches to page builders
    - _Requirements: 3.2, 5.1–5.6, 6.1–6.7, 7.1–7.8_
  - [x] 5.2 Implement `BuildGameCardPage` method
    - Display game name, install path header, action buttons row (Nexus, PCGW, Hide, Favourite), info badges, component rows (ReShade, RenoDX, ReLimiter, DC, OptiScaler, RE Framework) with install/update/remove buttons, author badges
    - Reuse existing `DetailPanelBuilder` logic where possible
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_
  - [x] 5.3 Implement `BuildOverridesPage` method
    - Display editable game name, wiki mod name, DLL override toggles, bitness override, graphics API overrides, shader mode, addon configuration
    - Delegate to existing `DetailPanelBuilder.BuildOverridesPanel` logic
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_
  - [x] 5.4 Implement `BuildManagementPage` method
    - Display four management buttons: Change install folder, Reset folder/Remove game, Reset Overrides, Copy Report
    - Wire click handlers to existing ViewModel commands
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8_
  - [x] 5.5 Wire `CompactViewBuilder` into `MainWindow.xaml.cs` constructor
    - Create `_compactViewBuilder` field, instantiate alongside other builders
    - _Requirements: 3.2_

- [x] 6. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Extend WindowStateManager with size locking
  - [x] 7.1 Add `_sizeLocked` field and `_compactSize` tuple to `WindowStateManager`
    - Add `SetSizeLocked(bool locked)` method
    - Add `ApplyCompactSize()` method that resizes window to fixed compact dimensions with DPI scaling
    - _Requirements: 8.2, 8.3, 8.5_
  - [x] 7.2 Update `WndProc` handler for `WM_GETMINMAXINFO` to enforce both min and max track size when `_sizeLocked` is true
    - When locked: set `ptMinTrackSize` and `ptMaxTrackSize` to the same compact dimensions
    - When unlocked: existing minimum-only enforcement
    - _Requirements: 8.3_
  - [x] 7.3 Wire size locking into layout transitions in `MainWindow.Events.cs`
    - On entering Compact: `CaptureCurrentBounds()` → `ApplyCompactSize()` → `SetSizeLocked(true)`
    - On leaving Compact: `SetSizeLocked(false)` → `RestoreWindowBounds()`
    - _Requirements: 8.1, 8.4, 8.5_

- [x] 8. Handle Compact mode on app startup
  - [x] 8.1 Update `MainWindow` initialization to apply Compact layout and size locking when persisted ViewLayout is Compact
    - After `LoadNameMappings` restores `ViewLayout.Compact`, apply `ApplyCompactSize()` and `SetSizeLocked(true)`
    - _Requirements: 9.1, 9.2_
  - [x] 8.2 Update `OnViewModelChanged` in `MainWindow.UISync.cs` to handle `TotalGames` change for Compact mode (rebuild compact page if needed, similar to grid rebuild)
    - _Requirements: 3.4, 10.1_

- [x] 9. Update existing tests for ViewLayout enum
  - [x] 9.1 Update `LayoutTogglePropertyTests.cs` to use `ViewLayout` enum and `CurrentViewLayout` property instead of `IsGridLayout` boolean
    - Update or replace existing property tests to validate three-state cycling
    - _Requirements: 1.1–1.3, 2.1_
  - [x] 9.2 Update `PeHeaderServiceEdgeCaseTests.cs` settings key list to reference `ViewLayout` instead of `GridLayout`
    - _Requirements: 9.3_

- [x] 10. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (Properties 1–7)
- The backward-compatible `IsGridLayout` property ensures existing code that reads grid state continues to work during the transition
- The `CompactViewBuilder` follows the same pattern as `CardBuilder` and `DetailPanelBuilder` — extracted UI construction with a `MainWindow` reference
- Settings migration is handled by reading `ViewLayout` first, falling back to `GridLayout` for older settings files
