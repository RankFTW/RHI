# Implementation Plan: Bitness & API Override

## Overview

Add per-game bitness and API override support across three layers: persistence (GameNameService dictionaries), ViewModel (accessor/mutator methods + modified resolution logic), and UI (new Bitness & API row in the overrides panel). Uses FsCheck 2.16.6 + xUnit for property-based tests.

## Tasks

- [x] 1. Add persistence layer for bitness and API overrides
  - [x] 1.1 Add `BitnessOverrides` and `ApiOverrides` properties to `IGameNameService`
    - Add `Dictionary<string, string> BitnessOverrides { get; }` to the interface
    - Add `Dictionary<string, List<string>> ApiOverrides { get; }` to the interface
    - _Requirements: 4.1, 4.2_

  - [x] 1.2 Implement `BitnessOverrides` and `ApiOverrides` in `GameNameService`
    - Add backing fields with `StringComparer.OrdinalIgnoreCase`
    - Expose as public properties matching the interface
    - Deserialize `"BitnessOverrides"` and `"ApiOverrides"` in `LoadNameMappings`
    - Serialize both dictionaries in `SaveNameMappings`
    - Add `MigrateDict` calls for both dictionaries in `RenameGame`
    - _Requirements: 4.1, 4.2, 4.5_

  - [x] 1.3 Write property test for override persistence round-trip
    - **Property 6: Override persistence round-trip**
    - Generate random game names and override values; set via dictionary, get back, verify equality
    - **Validates: Requirements 4.1, 4.2**

- [x] 2. Add ViewModel accessor/mutator methods and override resolution logic
  - [x] 2.1 Add bitness override accessor/mutator methods to `MainViewModel`
    - Add `GetBitnessOverride(string gameName)` returning `string?`
    - Add `SetBitnessOverride(string gameName, string? value)` — null or "Auto" removes the entry, "32"/"64" sets it
    - Both delegate to `GameNameServiceInstance.BitnessOverrides` dictionary and call `SaveNameMappings`
    - _Requirements: 2.3, 2.4, 2.5, 4.1_

  - [x] 2.2 Add API override accessor/mutator methods to `MainViewModel`
    - Add `GetApiOverride(string gameName)` returning `List<string>?`
    - Add `SetApiOverride(string gameName, List<string>? apis)` — null removes the entry, otherwise stores the list
    - Both delegate to `GameNameServiceInstance.ApiOverrides` dictionary and call `SaveNameMappings`
    - _Requirements: 3.4, 3.5, 4.2_

  - [x] 2.3 Modify `ResolveIs32Bit` to check bitness overrides first
    - Before manifest 32-bit/64-bit checks, look up `BitnessOverrides[gameName]`
    - If "32" → return true; if "64" → return false; otherwise fall through to existing logic
    - _Requirements: 2.3, 2.4, 2.5, 2.6, 4.3_

  - [x] 2.4 Write property test for bitness resolution priority
    - **Property 1: Bitness resolution respects override priority**
    - Generate random game names, MachineType values, and override values ("Auto"/"32"/"64"); verify resolved Is32Bit matches expected
    - **Validates: Requirements 2.3, 2.4, 2.5, 2.6, 4.3**

  - [x] 2.5 Modify `_DetectAllApisForCard` to check API overrides first
    - If `ApiOverrides` contains the game name, parse the stored list via `Enum.TryParse<GraphicsApiType>` and return the override set instead of scanning
    - Skip unrecognized values silently
    - _Requirements: 3.3, 3.4, 3.5, 3.6, 4.4_

  - [x] 2.6 Modify `DetectGraphicsApi` to check API overrides first
    - If `ApiOverrides` contains the game name, derive the primary API from the override set (prefer highest DX version when both DX and Vulkan present, or single API, or Unknown for empty set)
    - _Requirements: 3.8, 4.4_

  - [x] 2.7 Write property test for API resolution priority
    - **Property 3: API resolution respects override priority**
    - Generate random game names, auto-detected API sets, and override sets (or null); verify effective DetectedApis matches expected
    - **Validates: Requirements 3.3, 3.4, 3.5, 3.6, 4.4**

  - [x] 2.8 Write property test for IsDualApiGame derivation
    - **Property 4: IsDualApiGame is correctly derived from effective APIs**
    - Generate random subsets of GraphicsApiType; verify IsDualApiGame equals (set contains any DX API) && (set contains Vulkan)
    - **Validates: Requirements 3.7**

  - [x] 2.9 Write property test for primary GraphicsApi derivation
    - **Property 5: Primary GraphicsApi is deterministically derived from effective APIs**
    - Generate random non-empty subsets of GraphicsApiType; verify primary API follows the deterministic selection rule
    - **Validates: Requirements 3.8**

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Build the Bitness & API Override UI section
  - [x] 4.1 Add the Bitness ComboBox (left column) in `DetailPanelBuilder.BuildOverridesPanel`
    - Create a "Bitness" TextBlock label (FontSize 12, TextPrimaryBrush)
    - Create a ComboBox with items `["Auto", "32-bit", "64-bit"]`, defaulting to the current override or "Auto"
    - Wire `SelectionChanged` handler to call `SetBitnessOverride`, update `card.Is32Bit`, update DLL naming section `PlaceholderText` ("ReShade32.dll" / "ReShade64.dll"), and call `card.NotifyAll()`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8_

  - [x] 4.2 Write property test for DLL placeholder text matching bitness
    - **Property 2: DLL placeholder text matches effective bitness**
    - Generate random boolean Is32Bit values; verify placeholder is "ReShade32.dll" when true, "ReShade64.dll" when false
    - **Validates: Requirements 2.7**

  - [x] 4.3 Add the API Override ToggleSwitches (right column) in `DetailPanelBuilder.BuildOverridesPanel`
    - Create a "Graphics API" TextBlock label (FontSize 12, TextPrimaryBrush)
    - Create one ToggleSwitch per non-Unknown GraphicsApiType value (7 toggles: DirectX8, DirectX9, DirectX10, DirectX11, DirectX12, Vulkan, OpenGL)
    - Use same ToggleSwitch styling as existing Global update inclusion toggles (FontSize 11, TextSecondaryBrush, OnContent "On", OffContent "Off")
    - Initialize toggle state from `GetApiOverride` if override exists, otherwise from `card.DetectedApis`
    - Wire `Toggled` handler to collect all toggle states, call `SetApiOverride`, update `card.DetectedApis`, recompute `card.IsDualApiGame` and `card.GraphicsApi`, and call `card.NotifyAll()`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 7.1, 7.2_

  - [x] 4.4 Assemble the Bitness & API Row Grid and insert into the panel
    - Create a 3-column Grid (Star | Auto divider | Star) matching existing row patterns
    - Place Bitness StackPanel in column 0, vertical divider in column 1, API StackPanel in column 2
    - Insert the row between the Middle Row separator and the DLL naming section (Bottom Row)
    - Add `UIFactory.MakeSeparator()` above and below the new row
    - _Requirements: 1.1, 1.2, 1.3, 7.1, 7.3_

  - [x] 4.5 Extend the Reset Overrides handler to clear bitness and API overrides
    - Reset the Bitness ComboBox to "Auto"
    - Reset all API ToggleSwitches to reflect auto-detected state
    - Call `SetBitnessOverride(capturedName, null)` and `SetApiOverride(capturedName, null)`
    - Revert `card.Is32Bit`, `card.DetectedApis`, `card.IsDualApiGame`, `card.GraphicsApi` to auto-detected values and call `card.NotifyAll()`
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 4.6 Write property test for reset removing all overrides
    - **Property 7: Reset removes all overrides and restores auto-detected values**
    - Generate random game names with random bitness and API overrides; perform reset; verify dictionaries no longer contain entries
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4**

- [x] 5. Downstream effects and integration
  - [x] 5.1 Ensure bitness changes update the 32-bit badge visibility
    - After `card.Is32Bit` is updated by the Bitness ComboBox handler, `card.NotifyAll()` triggers `Is32BitBadgeVisibility` refresh
    - Verify `DetailPanelBuilder.UpdateDetailComponentRows` picks up the new `Is32Bit` value via `Detail32BitBadge.Visibility`
    - _Requirements: 6.1_

  - [x] 5.2 Ensure API changes update Rendering Path ComboBox and Graphics API badge
    - After API toggles update `card.IsDualApiGame`, the overrides panel should be rebuilt (or the Rendering Path ComboBox conditionally shown/hidden)
    - After API toggles update `card.GraphicsApi`, `card.NotifyAll()` triggers `HasGraphicsApiBadge` and `GraphicsApiLabel` refresh
    - _Requirements: 6.2, 6.3, 6.4, 6.5_

- [x] 6. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (Properties 1-7)
- The implementation language is C# (WinUI 3 / .NET 8), matching the existing codebase
- FsCheck 2.16.6 + xUnit is the existing PBT infrastructure
