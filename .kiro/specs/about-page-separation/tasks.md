# Implementation Plan: About Page Separation

## Overview

Move all About/Credits/Links content from the Settings panel to a dedicated About page. Add `About` to the `AppPage` enum, create the `AboutPanel` in XAML, wire navigation from the Help flyout, update visibility logic, and extend existing tests. Implementation proceeds bottom-up: enum → ViewModel command → XAML panel → visibility logic → event wiring → cleanup Settings → tests.

## Tasks

- [x] 1. Add About value to AppPage enum
  - [x] 1.1 Add `About` enum value to `RenoDXCommander/Models/AppPage.cs`
    - Add `About` after `Settings` in the `AppPage` enum
    - _Requirements: 1.1_
  - [x] 1.2 Add `NavigateToAbout` relay command to `RenoDXCommander/ViewModels/MainViewModel.Install.cs`
    - Add `[RelayCommand] public void NavigateToAbout() => CurrentPage = AppPage.About;` alongside existing `NavigateToSettings` and `NavigateToGameView`
    - _Requirements: 3.2_

- [x] 2. Update VisibilityState and ComputeVisibilityState for About page
  - [x] 2.1 Add `AboutPanel` field to the `VisibilityState` record in `RenoDXCommander/MainWindow.Skeleton.cs`
    - Add `Visibility AboutPanel` parameter to the record definition
    - _Requirements: 5.1, 5.2, 5.3_
  - [x] 2.2 Update `ComputeVisibilityState` in `RenoDXCommander/MainWindow.Skeleton.cs` to handle `AppPage.About`
    - Add a case for `AppPage.About` that returns all panels Collapsed except `AboutPanel=Visible`
    - Update the `AppPage.Settings` case to include `AboutPanel: Visibility.Collapsed`
    - Update the default/GameView cases to include `AboutPanel: Visibility.Collapsed`
    - _Requirements: 5.1, 5.2, 5.3_

- [x] 3. Create AboutPanel in MainWindow.xaml
  - [x] 3.1 Add `AboutPanel` grid to `RenoDXCommander/MainWindow.xaml` inside Row 1 content area
    - Add a `Grid x:Name="AboutPanel"` with `Visibility="Collapsed"` alongside `GameViewPanel` and `SettingsPanel`
    - Row 0: Back button bar with "← Back to Games" button (Click="AboutBack_Click") and "About" title text
    - Row 1: ScrollViewer containing a StackPanel with the About content (moved from Settings in task 4)
    - Use same styling pattern as SettingsPanel (Padding, MaxWidth=800, card-style borders)
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9_
  - [x] 3.2 Move About content from SettingsPanel to AboutPanel in `RenoDXCommander/MainWindow.xaml`
    - Move the "About" Border (icon, name, version, description, "What RHI does", Disclaimer, Single-player warning) from SettingsPanel to AboutPanel's ScrollViewer StackPanel
    - Move the "Credits & Acknowledgements" Border from SettingsPanel to AboutPanel
    - Move the "Links" Border from SettingsPanel to AboutPanel
    - Ensure `AboutVersionText` x:Name is preserved on the moved element
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 4.2, 4.3, 4.4_
  - [x] 3.3 Add "About" menu item to Help flyout in `RenoDXCommander/MainWindow.xaml`
    - Add `<MenuFlyoutItem Text="About" Click="AboutButton_Click"/>` as the last item in the SupportBtn MenuFlyout
    - _Requirements: 3.1, 3.3_

- [x] 4. Wire event handlers and update visibility logic
  - [x] 4.1 Add `AboutButton_Click` and `AboutBack_Click` handlers to `RenoDXCommander/MainWindow.Events.cs`
    - `AboutButton_Click`: call `ViewModel.NavigateToAboutCommand.Execute(null)`
    - `AboutBack_Click`: call `ViewModel.NavigateToGameViewCommand.Execute(null)`
    - _Requirements: 3.2, 2.8_
  - [x] 4.2 Update `UpdatePageVisibility()` in `RenoDXCommander/MainWindow.UISync.cs`
    - Replace the two-way if/else with three-way visibility: set `GameViewPanel`, `SettingsPanel`, and `AboutPanel` visibility based on `ViewModel.CurrentPage`
    - _Requirements: 5.1, 5.2, 5.3_
  - [x] 4.3 Update `OnViewModelChanged` IsLoading case in `RenoDXCommander/MainWindow.UISync.cs`
    - Change the condition `ViewModel.CurrentPage != AppPage.Settings` to also exclude `AppPage.About` (i.e., only show skeletons on GameView)
    - _Requirements: 5.1_

- [x] 5. Verify Settings page retains only actionable sections
  - [x] 5.1 Confirm SettingsPanel in `RenoDXCommander/MainWindow.xaml` retains only: Add Game, Custom Shaders, Screenshots, Full Refresh, Addon Watch Folder, Preferences, Crash & Error Logs
    - Verify the About, Credits, and Links borders have been removed from SettingsPanel (done in task 3.2)
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

- [x] 6. Checkpoint — Build and verify About page
  - Build the project and verify no compile errors. Ask the user to run the app and verify: Help flyout shows "About" item, clicking it shows the About page with all content, "← Back to Games" returns to GameView, Settings page no longer has About/Credits/Links sections.

- [x] 7. Write property-based and unit tests
  - [x] 7.1 Write property test: Page visibility exclusivity (Property 1)
    - **Property 1: Page visibility exclusivity**
    - Update the existing `SkeletonVisibilityStatePropertyTests` or create a new test class `AboutPageVisibilityPropertyTests` in `RenoDXCommander.Tests/`
    - Generate all `AppPage` values (GameView, Settings, About) via `Gen.Elements`, combined with `(bool isLoading, bool hasInitialized)`, call `ComputeVisibilityState`, assert that for each `AppPage` value exactly one of `GameViewPanel`/`SettingsPanel`/`AboutPanel` is Visible (or GameViewPanel is Visible for skeleton states) and the other page panels are Collapsed
    - Tag: `Feature: about-page-separation, Property 1: Page visibility exclusivity`
    - **Validates: Requirements 5.1, 5.2, 5.3**
  - [x] 7.2 Write property test: Navigation idempotence (Property 2)
    - **Property 2: Navigation idempotence**
    - Create test in `RenoDXCommander.Tests/AboutPageVisibilityPropertyTests.cs`
    - For any `AppPage` value, call `ComputeVisibilityState` twice with the same inputs and assert both results are identical
    - Tag: `Feature: about-page-separation, Property 2: Navigation idempotence`
    - **Validates: Requirements 5.1, 5.2, 5.3, 3.2**
  - [x] 7.3 Write unit tests for About page enum and navigation
    - Create `RenoDXCommander.Tests/AboutPageSeparationUnitTests.cs`
    - Test `AppPage` enum contains exactly three values: `GameView`, `Settings`, `About`
    - Test `ComputeVisibilityState` with `currentPage=About` returns `AboutPanel=Visible`, `GameViewPanel=Collapsed`, `SettingsPanel=Collapsed`
    - Test `ComputeVisibilityState` with `currentPage=Settings` returns `AboutPanel=Collapsed`
    - Test `ComputeVisibilityState` with `currentPage=GameView` returns `AboutPanel=Collapsed`
    - _Requirements: 1.1, 5.1, 5.2, 5.3_
  - [x] 7.4 Update existing `SkeletonVisibilityStatePropertyTests` to include `AppPage.About` in the generator
    - Change `Gen.Elements(AppPage.GameView, AppPage.Settings)` to `Gen.Elements(AppPage.GameView, AppPage.Settings, AppPage.About)`
    - Update assertions to handle the About case (all skeleton panels Collapsed, AboutPanel Visible)
    - _Requirements: 5.1, 5.2, 5.3_

- [x] 8. Final checkpoint — Ensure all tests pass
  - Run all tests and verify they pass. Ask the user if questions arise.

## Notes

- Each task references specific requirements for traceability
- Property tests validate universal correctness properties from the design document
- The test project uses FsCheck 2.16.6 with FsCheck.Xunit and xUnit on net8.0-windows
- The existing `VisibilityState` record and `ComputeVisibilityState` method are extended rather than replaced
- The `AboutVersionText` x:Name must be preserved when moving XAML to maintain code-behind bindings
- Patch Notes link is already in the status bar (Row 2), not in Settings, so Requirement 6.1 is a no-op
