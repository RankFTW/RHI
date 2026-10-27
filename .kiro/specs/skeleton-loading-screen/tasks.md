# Implementation Plan: Skeleton Loading Screen

## Overview

Replace the existing centered ProgressRing loading screen with a skeleton loading screen that mimics the GameViewPanel layout. Implementation proceeds bottom-up: theme resources → skeleton creation logic → XAML wiring → UISync integration → tests.

## Tasks

- [x] 1. Add skeleton theme resources and ResourceKeys constants
  - [x] 1.1 Add skeleton color and brush resources to DarkTheme.xaml
    - Add `SkeletonBase` Color (`#FF1A2028`), `SkeletonHighlight` Color (`#FF252D38`), `SkeletonBaseBrush` SolidColorBrush, and `SkeletonHighlightBrush` SolidColorBrush to `RenoDXCommander/Themes/DarkTheme.xaml`
    - _Requirements: 4.4_
  - [x] 1.2 Add skeleton resource key constants to ResourceKeys.cs
    - Add `SkeletonBase`, `SkeletonHighlight`, `SkeletonBaseBrush`, `SkeletonHighlightBrush` string constants to `RenoDXCommander/ResourceKeys.cs`
    - _Requirements: 4.4_

- [x] 2. Create MainWindow.Skeleton.cs with skeleton creation and cleanup logic
  - [x] 2.1 Create `RenoDXCommander/MainWindow.Skeleton.cs` partial class with `InitializeSkeletons()` and `RemoveSkeletons()` methods
    - Add `_shimmerStoryboard` field
    - `InitializeSkeletons()`: resolve `SkeletonBaseBrush` from resources (with hardcoded fallback), create 12 skeleton rows via `CreateSkeletonRow()`, populate `SkeletonRowPanel`, call `PopulateSkeletonDetailPanel()`, build shimmer storyboard via `CreateShimmerStoryboard()`, start storyboard (catch and log failures)
    - `RemoveSkeletons()`: stop storyboard (catch failures), null out `_shimmerStoryboard`, clear children of `SkeletonRowPanel` and `SkeletonDetailPanel`, collapse both panels. Must be idempotent.
    - _Requirements: 1.1, 1.3, 1.4, 5.1, 5.2, 5.3_
  - [x] 2.2 Implement `CreateSkeletonRow(SolidColorBrush fillBrush)` static method
    - Return a `Border` with `CornerRadius=6`, `Padding=8,6`, containing a `Grid` with `ColumnSpacing=6` and 3 columns: 14×14 icon `Border`, flexible-width name `Border` (Height=12, CornerRadius=4), 8×8 `Ellipse`
    - _Requirements: 2.1, 2.2_
  - [x] 2.3 Implement `PopulateSkeletonDetailPanel(SolidColorBrush fillBrush)` method
    - Add header placeholder (Width=280, Height=20, CornerRadius=4), badge row (3 borders, Width=60, Height=20, CornerRadius=5), component table Border (CornerRadius=12) with "Components" TextBlock header and 3 skeleton rows matching 5-column grid (120, 80, *, 36, 36)
    - Outer panel: Padding=(24,18,24,24), Spacing=16
    - _Requirements: 3.1, 3.2, 3.3, 3.4_
  - [x] 2.4 Implement `CreateShimmerStoryboard(List<Border> targets, Color fromColor, Color toColor)` method
    - Create one `ColorAnimation` per target with `AutoReverse=true`, `RepeatBehavior=Forever`, `Duration=1.5s`, targeting `(Border.Background).(SolidColorBrush.Color)`
    - _Requirements: 4.1, 4.2, 4.3_

- [x] 3. Modify XAML and code-behind for skeleton integration
  - [x] 3.1 Update `RenoDXCommander/MainWindow.xaml` — add skeleton overlay panels
    - Add `SkeletonRowPanel` StackPanel in sidebar Grid Row 3 (after GameList, so it overlays with higher z-order), with `Spacing="4"`, `Padding="4"`, `Visibility="Visible"`
    - Add `SkeletonDetailPanel` StackPanel in detail column Grid (Column 1), with `Spacing="16"`, `Padding="24,18,24,24"`, `Visibility="Visible"`
    - Change `GameViewPanel` from `Visibility="Collapsed"` to `Visibility="Visible"`
    - Change `LoadingPanel` from `Visibility="Visible"` to `Visibility="Collapsed"`
    - _Requirements: 1.1, 1.2, 6.3, 7.1, 7.2, 7.3_
  - [x] 3.2 Update `RenoDXCommander/MainWindow.xaml.cs` — call `InitializeSkeletons()` in constructor
    - Add `InitializeSkeletons()` call after `InitializeComponent()` in the `MainWindow` constructor
    - _Requirements: 1.1, 1.3_

- [x] 4. Update MainWindow.UISync.cs to use skeleton transition
  - [x] 4.1 Modify the `IsLoading` case in `OnViewModelChanged` in `RenoDXCommander/MainWindow.UISync.cs`
    - Replace `LoadingPanel`/`GameViewPanel` visibility toggling with a call to `RemoveSkeletons()` when `!loading && !silent`
    - Keep `GameViewPanel.Visibility = Visible` as a safety ensure
    - Remove or guard the `LoadingPanel.Visibility` assignment (LoadingPanel stays Collapsed)
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 6.3_

- [x] 5. Checkpoint — Build and verify skeleton loading screen
  - Ensure the project builds without errors. Ask the user to run the application and verify: skeleton rows appear on launch, shimmer animation plays, skeletons disappear when loading completes, status bar shows loading text. Ask the user if questions arise.

- [x] 6. Write property-based and unit tests
  - [x] 6.1 Write property test: Visibility state mapping is consistent (Property 1)
    - **Property 1: Visibility state mapping is consistent**
    - Extract a pure static method `ComputeVisibilityState(bool isLoading, bool hasInitialized, AppPage currentPage)` returning a record of visibility values, and test with FsCheck-generated `(bool, bool, AppPage)` tuples
    - **Validates: Requirements 1.1, 5.5, 6.3**
  - [x] 6.2 Write property test: Skeleton row structure matches game card template (Property 2)
    - **Property 2: Skeleton row structure matches game card template**
    - Generate arbitrary `SolidColorBrush` colors via FsCheck, call `CreateSkeletonRow()`, assert CornerRadius=6, Padding=(8,6,8,6), Grid with ColumnSpacing=6, 3 children (14×14, Height=12, 8×8 Ellipse)
    - **Validates: Requirements 2.1, 2.2**
  - [x] 6.3 Write property test: Skeleton detail panel contains all required sections (Property 3)
    - **Property 3: Skeleton detail panel contains all required sections**
    - Call `PopulateSkeletonDetailPanel()`, verify header (Height≥16), ≥3 badge placeholders, component table Border (CornerRadius=12) with ≥2 row Grids of 5 columns, outer panel Padding=(24,18,24,24) and Spacing=16
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
  - [x] 6.4 Write property test: Shimmer storyboard is correctly configured per target (Property 4)
    - **Property 4: Shimmer storyboard is correctly configured per target**
    - Generate lists of 1–50 Border targets via FsCheck, call `CreateShimmerStoryboard()`, verify animation count equals target count, each animation has AutoReverse=true, RepeatBehavior=Forever, Duration between 1.0–2.0s
    - **Validates: Requirements 4.1, 4.2, 4.3**
  - [x] 6.5 Write property test: Skeleton cleanup is complete (Property 5)
    - **Property 5: Skeleton cleanup is complete**
    - Create SkeletonRowPanel and SkeletonDetailPanel with FsCheck-generated child counts (N>0, M>0), set non-null storyboard, call `RemoveSkeletons()`, verify Children.Count=0, Visibility=Collapsed, _shimmerStoryboard=null
    - **Validates: Requirements 5.1, 5.2, 5.3**
  - [x] 6.6 Write unit tests for skeleton loading screen edge cases
    - Test skeleton row count constant is within [8, 15] range (Requirement 2.3)
    - Test shimmer colors `#FF1A2028` and `#FF252D38` are within expected dark theme range (Requirement 4.4)
    - Test `RemoveSkeletons()` is idempotent — calling twice does not throw
    - Test visibility state for specific case `(isLoading=true, hasInitialized=false, currentPage=Games)` produces skeleton-visible state
    - Create test file `RenoDXCommander.Tests/SkeletonLoadingScreenUnitTests.cs`
    - _Requirements: 2.3, 4.4, 5.1_

- [x] 7. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Property tests validate universal correctness properties from the design document
- The test project uses FsCheck 2.16.6 with FsCheck.Xunit and xUnit on net8.0-windows
- Skeleton logic is entirely code-behind; no new ViewModels or UserControls needed
