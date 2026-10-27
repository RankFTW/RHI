# Implementation Plan: Custom Shaders

## Overview

Implement a custom shader system for RHI that allows users to source shaders/textures from user-managed directories instead of built-in shader packs. The feature adds a global toggle in Settings, a per-game toggle in overrides panels, and extends the shader resolution pipeline with a priority chain: per-game custom → per-game packs → global custom → global packs.

## Tasks

- [x] 1. Add UseCustomShaders property to SettingsViewModel and persistence
  - [x] 1.1 Add `UseCustomShaders` observable property to `SettingsViewModel`
    - Add `[ObservableProperty] private bool _useCustomShaders;` to `SettingsViewModel`
    - In `LoadSettingsFromDict`, load `"UseCustomShaders"` key, defaulting to `false` when absent
    - In `SaveSettingsToDict`, persist `"UseCustomShaders"` as `"true"` / `"false"`
    - _Requirements: 1.3, 1.4, 1.5, 7.2_

  - [x] 1.2 Write property test for settings round-trip (Property 1)
    - **Property 1: Settings round-trip preserves custom shader state**
    - Generate random `UseCustomShaders` bool and random `Dictionary<string, string>` of per-game shader modes (values from `{"Custom", "Select", "Global"}`)
    - Save via `SaveSettingsToDict`, load via `LoadSettingsFromDict`, assert equivalence
    - **Validates: Requirements 1.3, 1.4, 1.5, 4.2, 4.3, 7.1, 7.3**

- [x] 2. Add custom shader sentinel and extend ShaderPackService
  - [x] 2.1 Add custom shader constants and sentinel to `ShaderPackService`
    - Add `public const string CustomShaderSentinel = "__custom__";`
    - Add `public static readonly string CustomShadersDir` and `CustomTexturesDir` path constants pointing to `%LOCALAPPDATA%\RHI\reshade\Custom\shaders` and `textures`
    - _Requirements: 2.1, 2.2_

  - [x] 2.2 Extend `SyncGameFolder` to handle custom shader sentinel
    - When `selectedPackIds` contains `CustomShaderSentinel`, ensure `CustomShadersDir` and `CustomTexturesDir` exist (create if missing)
    - Copy/sync files from custom directories into the game's `reshade-shaders` folder instead of pack-based sources
    - Add verbose logging of effective shader source when `CrashReporter.VerboseLogging` is enabled
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 6.3_

- [x] 3. Extend ResolveShaderSelection in MainViewModel
  - [x] 3.1 Update `ResolveShaderSelection` to implement the full priority chain
    - Check per-game `"Custom"` mode → return `new[] { ShaderPackService.CustomShaderSentinel }`
    - Check per-game `"Select"` mode → return per-game pack selection (existing behavior)
    - Check global `UseCustomShaders` enabled → return custom shader sentinel
    - Fallback → return global pack selection (existing behavior)
    - _Requirements: 2.1, 2.2, 4.4, 5.1, 5.2, 5.3, 6.1_

  - [x] 3.2 Write property test for shader resolution priority chain (Property 3)
    - **Property 3: Shader resolution follows priority chain**
    - Generate random tuples of (globalCustomEnabled, perGameMode, perGamePacks, globalPacks)
    - Call `ResolveShaderSelection`, assert result matches expected priority chain output
    - **Validates: Requirements 2.1, 2.2, 4.4, 5.1, 5.2, 5.3, 6.1**

- [x] 4. Add IsGlobalShaderButtonEnabled and wire to Global Shader Button
  - [x] 4.1 Add `IsGlobalShaderButtonEnabled` computed property to `MainViewModel`
    - Return `!Settings.UseCustomShaders`
    - Subscribe to `Settings.PropertyChanged` for `UseCustomShaders` to raise `OnPropertyChanged(nameof(IsGlobalShaderButtonEnabled))`
    - _Requirements: 3.1, 3.2, 3.3_

  - [x] 4.2 Bind `ChooseShadersBtn` in `MainWindow.xaml` to `IsGlobalShaderButtonEnabled`
    - Set `IsEnabled="{x:Bind ViewModel.IsGlobalShaderButtonEnabled, Mode=OneWay}"` on the Global Shader Button
    - Ensure the button transitions between enabled/disabled states without restart
    - _Requirements: 3.4, 3.5_

  - [x] 4.3 Write property test for global shader button enabled state (Property 2)
    - **Property 2: Global shader button enabled state is the inverse of custom shaders**
    - Generate random bool for `UseCustomShaders`, assert `IsGlobalShaderButtonEnabled == !UseCustomShaders`
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Add Custom Shaders toggle to Settings menu UI
  - [x] 6.1 Extend `SettingsHandler` to add the Custom Shaders toggle box
    - Add a new toggle box between the "Add Game" box and the "Full Refresh" box in the Settings panel
    - Follow the same visual format: `Border` with `SurfaceRaisedBrush` background, `CornerRadius="10"`, `Padding="20,16"`, `BorderSubtleBrush` border, containing title TextBlock, description TextBlock, and `ToggleSwitch`
    - Wire toggle to `ViewModel.Settings.UseCustomShaders` with auto-save via `ViewModel.SaveSettingsPublic()`
    - _Requirements: 1.1, 1.2_

- [x] 7. Add Per-Game Custom Shaders toggle to overrides panels
  - [x] 7.1 Add "Use Custom Shaders" toggle to `DetailPanelBuilder.BuildOverridesPanel`
    - Add a `ToggleSwitch` next to the existing "Global Shaders" toggle in the shaders section
    - When toggled ON: set `PerGameShaderMode[gameName] = "Custom"`, trigger `DeployShadersForCard`
    - When toggled OFF: remove the custom override (revert to `"Global"`), trigger `DeployShadersForCard`
    - Default to ON when global custom shaders is enabled and no per-game override exists
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 5.1, 5.3_

  - [x] 7.2 Add "Use Custom Shaders" toggle to `OverridesFlyoutBuilder.OpenOverridesFlyout`
    - Mirror the same toggle behavior from the detail panel in the grid-view flyout
    - _Requirements: 4.5_

- [x] 8. Wire toggle changes to trigger shader re-deployment
  - [x] 8.1 Trigger re-deployment on global custom shaders toggle change
    - When `UseCustomShaders` changes in `SettingsViewModel`, call `DeployAllShaders()` to re-sync all installed games
    - _Requirements: 6.2_

  - [x] 8.2 Ensure per-game toggle changes trigger `DeployShadersForCard`
    - Verify that toggling the per-game custom shaders toggle in both detail and grid overrides panels calls `DeployShadersForCard` for the affected game
    - _Requirements: 6.2_

  - [x] 8.3 Write property test for toggle change triggers re-deployment (Property 4)
    - **Property 4: Toggle change triggers shader re-deployment**
    - Generate random game configurations with ReShade installed
    - Change the custom shaders toggle (global or per-game), assert `DeployShadersForCard` was invoked via a tracking stub
    - **Validates: Requirements 6.2**

- [x] 9. Persist per-game custom shader overrides in GameNameService
  - [x] 9.1 Ensure `PerGameShaderMode` accepts `"Custom"` as a valid value
    - Verify that `GameNameService` load/save handles `"Custom"` in `PerGameShaderMode` dictionary alongside `"Global"` and `"Select"`
    - No new dictionaries needed — reuse existing `PerGameShaderMode` key
    - _Requirements: 4.2, 4.3, 7.3_

- [x] 10. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- The design uses C# with the existing WinUI 3 / CommunityToolkit.Mvvm stack — all implementation follows existing patterns
