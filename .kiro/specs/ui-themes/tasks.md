# Implementation Plan: UI Themes

## Overview

Add a theme selection system to RenoDXCommander: an `IThemeService`/`ThemeService` for discovering and swapping XAML theme dictionaries at runtime, a `SelectedTheme` property on `SettingsViewModel` for persistence, and a "Themes" section in the settings panel with a ComboBox. Implementation uses C# / .NET 8 / WinUI 3 with CommunityToolkit.Mvvm and FsCheck.Xunit for property tests.

## Tasks

- [x] 1. Create IThemeService interface and ThemeService implementation
  - [x] 1.1 Create `RenoDXCommander/Services/IThemeService.cs` with the `IThemeService` interface
    - Define `AvailableThemes` (IReadOnlyList\<string\>), `CurrentTheme` (string), and `ApplyTheme(string)` (bool) members
    - _Requirements: 2.1, 5.1, 6.1_

  - [x] 1.2 Create `RenoDXCommander/Services/ThemeService.cs` implementing `IThemeService`
    - Implement `DiscoverThemes()` to scan `Themes/*.xaml` and build a display-name-to-path mapping
    - Map `DarkTheme.xaml` → `"Default"`, strip `.xaml` extension and trailing `"Theme"` suffix for other files
    - Ensure `"Default"` is always the first entry in `AvailableThemes`
    - Implement `ApplyTheme(string)` to load the target `ResourceDictionary` and replace `MergedDictionaries[1]`
    - On failure or invalid name, fall back to `"Default"` and log via `CrashReporter.Log`
    - Handle null/empty theme name as invalid → fall back to Default
    - _Requirements: 2.1, 2.2, 2.3, 3.1, 4.4, 5.1, 5.2, 6.1, 6.2, 6.3_

  - [ ]* 1.3 Write property test: Theme name derivation (Property 3)
    - **Property 3: Theme name derivation**
    - Generate random filename strings ending in `.xaml`, apply the name derivation logic, assert `.xaml` stripped, trailing `"Theme"` stripped, `DarkTheme.xaml` → `"Default"`
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 6.2**

  - [ ]* 1.4 Write property test: Default is always first (Property 2)
    - **Property 2: Default is always first**
    - Generate random sets of theme filenames (always including `DarkTheme.xaml`), run discovery, assert first element is `"Default"`
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 2.2**

  - [ ]* 1.5 Write property test: Theme discovery returns all valid themes (Property 1)
    - **Property 1: Theme discovery returns all valid themes**
    - Generate random sets of `.xaml` filenames, feed to discovery logic, assert output count and names match expectations
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 2.1, 6.1**

- [x] 2. Add theme persistence to SettingsViewModel
  - [x] 2.1 Add `SelectedTheme` property to `SettingsViewModel`
    - Add `[ObservableProperty] private string _selectedTheme = "Default";`
    - Update `LoadSettingsFromDict` to read `"Theme"` key from settings dictionary, defaulting to `"Default"` if missing
    - Update `SaveSettingsToDict` to write `"Theme"` key
    - _Requirements: 3.1, 4.1, 4.2_

  - [ ]* 2.2 Write property test: Theme setting persistence round-trip (Property 4)
    - **Property 4: Theme setting persistence round-trip**
    - Generate random non-empty theme name strings, write via `SaveSettingsToDict`, read back via `LoadSettingsFromDict`, assert equality
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 4.1, 4.2**

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Register ThemeService in DI and apply theme on startup
  - [x] 4.1 Register `IThemeService` / `ThemeService` as singleton in `App.xaml.cs` DI container
    - Add `services.AddSingleton<IThemeService, ThemeService>();` alongside existing service registrations
    - _Requirements: 5.1_

  - [x] 4.2 Apply persisted theme in `App.OnLaunched` before window activation
    - After creating `MainWindow`, resolve `IThemeService` and `SettingsViewModel`
    - Load settings, then call `ThemeService.ApplyTheme(settings.SelectedTheme)` before `_window.Activate()`
    - _Requirements: 4.3_

  - [ ]* 4.3 Write property test: Invalid theme name falls back to Default (Property 5)
    - **Property 5: Invalid theme name falls back to Default**
    - Generate random strings not in the available themes list (including empty/null), call `ApplyTheme`, assert `CurrentTheme` is `"Default"`
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 3.1, 4.4**

- [x] 5. Add Themes section to settings panel UI
  - [x] 5.1 Add Themes section XAML to `MainWindow.xaml`
    - Insert a new `Border` section between the Preferences section and the Crash & Error Logs section
    - Include a `TextBlock` heading with text "Themes"
    - Include a `ComboBox` with `x:Name="ThemeSelector"` and a `SelectionChanged` handler
    - Use the same card style (background, corner radius, border, padding) as other settings sections
    - _Requirements: 1.1, 1.2, 1.3, 2.1_

  - [x] 5.2 Wire up ThemeSelector in `MainWindow.xaml.cs`
    - In `SettingsButton_Click`, populate `ThemeSelector.ItemsSource` from `IThemeService.AvailableThemes`
    - Set `ThemeSelector.SelectedItem` to `SettingsViewModel.SelectedTheme`
    - Add `ThemeSelector_SelectionChanged` handler that calls `ThemeService.ApplyTheme(name)` and sets `SettingsViewModel.SelectedTheme`, then calls `SaveSettingsPublic()`
    - _Requirements: 2.3, 2.4, 4.1, 5.1, 5.2_

  - [ ]* 5.3 Write property test: Applying a valid theme swaps the ResourceDictionary (Property 6)
    - **Property 6: Applying a valid theme swaps the ResourceDictionary**
    - For each valid theme name from the available list, call `ApplyTheme`, assert `MergedDictionaries` contains the expected `ResourceDictionary`
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 2.3, 5.1**

- [x] 6. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Property tests use FsCheck.Xunit with `[Property(MaxTest = 100)]` per design spec
- The theme ResourceDictionary is at index 1 in `MergedDictionaries` (after `XamlControlsResources`)
- Settings panel sections are built programmatically in `MainWindow.xaml.cs`; the Themes section follows the same pattern
