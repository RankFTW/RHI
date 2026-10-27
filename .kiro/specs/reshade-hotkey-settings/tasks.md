# Implementation Plan: ReShade Hotkey Settings

## Overview

Add a ReShade UI overlay hotkey configuration section to the Settings page. Restructure the Screenshots container into a two-column layout, implement hotkey capture/persistence/display helpers, add `ApplyOverlayHotkey` to `AuxInstallService`, integrate into `MergeRsIni`/`MergeRsVulkanIni`, and wire the UI Apply button to push the hotkey to all managed reshade*.ini files.

## Tasks

- [x] 1. Add hotkey helper methods to SettingsHandler
  - [x] 1.1 Implement `ParseHotkeyString`, `BuildHotkeyString`, `FormatHotkeyDisplay`, and `IsDefaultHotkey` as static methods in `RenoDXCommander/SettingsHandler.cs`
    - `ParseHotkeyString(string value)` returns `(int vk, bool shift, bool ctrl, bool alt)`, defaulting to `(36, false, false, false)` on invalid input
    - `BuildHotkeyString(int vk, bool shift, bool ctrl, bool alt)` returns `"vk,shift,ctrl,alt"` format string
    - `FormatHotkeyDisplay(int vk, bool shift, bool ctrl, bool alt)` returns human-readable string with modifiers in Ctrl, Shift, Alt order separated by " + "
    - `FormatHotkeyDisplay(string keyOverlayValue)` overload that parses then formats
    - `IsDefaultHotkey(string keyOverlayValue)` returns true for `"36,0,0,0"`
    - _Requirements: 2.1, 2.2, 7.1, 7.2, 7.3_

  - [x] 1.2 Write property test for hotkey string round-trip (Property 2)
    - **Property 2: Hotkey string round-trip**
    - For any VK code (1–254) and any combination of modifier booleans, `ParseHotkeyString(BuildHotkeyString(vk, shift, ctrl, alt))` returns the original tuple
    - Create `RenoDXCommander.Tests/HotkeyStringRoundTripPropertyTests.cs`
    - **Validates: Requirements 2.2**

  - [x] 1.3 Write property test for FormatHotkeyDisplay ordered format (Property 3)
    - **Property 3: FormatHotkeyDisplay produces correct ordered format**
    - For any VK code (1–254) and modifier flags, output is non-empty, modifiers appear in Ctrl/Shift/Alt order before the main key, separated by " + "
    - Create `RenoDXCommander.Tests/HotkeyDisplayFormatPropertyTests.cs`
    - **Validates: Requirements 2.1, 7.1, 7.2, 7.3**

  - [x] 1.4 Write unit tests for hotkey helpers
    - `ParseHotkeyString` with invalid input returns default `(36, false, false, false)`
    - `FormatHotkeyDisplay("36,0,0,0")` returns `"Home"`
    - `FormatHotkeyDisplay("113,0,1,0")` returns `"Ctrl + F2"`
    - `IsDefaultHotkey("36,0,0,0")` returns true; `IsDefaultHotkey("113,0,1,0")` returns false
    - Create `RenoDXCommander.Tests/HotkeyHelperUnitTests.cs`
    - _Requirements: 2.1, 2.2, 7.1, 7.2, 7.3_

- [x] 2. Add OverlayHotkey property to SettingsViewModel
  - [x] 2.1 Add `[ObservableProperty] private string _overlayHotkey = "36,0,0,0";` to `RenoDXCommander/ViewModels/SettingsViewModel.cs`
    - Add `OverlayHotkey` read in `LoadSettingsFromDict` (default `"36,0,0,0"` if absent)
    - Add `s["OverlayHotkey"] = OverlayHotkey;` in `SaveSettingsToDict`
    - Follow the existing `ScreenshotPath` persistence pattern
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 2.2 Write property test for hotkey settings round-trip (Property 1)
    - **Property 1: Hotkey settings round-trip**
    - For any valid KeyOverlay format string, setting `OverlayHotkey`, calling `SaveSettingsToDict`, then `LoadSettingsFromDict` on a fresh SettingsViewModel produces the same value
    - Create `RenoDXCommander.Tests/HotkeySettingsRoundTripPropertyTests.cs`
    - **Validates: Requirements 3.1, 3.2, 3.4**

- [x] 3. Implement ApplyOverlayHotkey in AuxInstallService
  - [x] 3.1 Add `public static void ApplyOverlayHotkey(string iniFilePath, string keyOverlayValue)` to `RenoDXCommander/Services/AuxInstallService.Ini.cs`
    - Follow the `ApplyScreenshotPath` pattern: `ParseIni` → ensure `[INPUT]` section exists → set `KeyOverlay=<value>` → `WriteIni`
    - _Requirements: 4.3, 4.4, 6.1, 6.2, 6.3, 6.4, 6.5_

  - [x] 3.2 Write property test for ApplyOverlayHotkey writes correct KeyOverlay (Property 4)
    - **Property 4: ApplyOverlayHotkey writes correct KeyOverlay**
    - For any valid INI content and any non-empty KeyOverlay string, after calling `ApplyOverlayHotkey`, the parsed result has `[INPUT]` with `KeyOverlay` equal to the provided value
    - Create `RenoDXCommander.Tests/ApplyOverlayHotkeyPropertyTests.cs`
    - **Validates: Requirements 4.3, 6.1, 6.3**

  - [x] 3.3 Write property test for ApplyOverlayHotkey preserves other sections (Property 5)
    - **Property 5: ApplyOverlayHotkey preserves other sections and keys**
    - All sections other than `[INPUT]` are unchanged; keys within `[INPUT]` other than `KeyOverlay` are preserved
    - Create `RenoDXCommander.Tests/HotkeyIniPreservationPropertyTests.cs`
    - **Validates: Requirements 4.4, 6.4, 6.5**

  - [x] 3.4 Write property test for ApplyOverlayHotkey idempotence (Property 6)
    - **Property 6: ApplyOverlayHotkey idempotence**
    - After calling `ApplyOverlayHotkey`, exactly one `[INPUT]` section exists; calling again with a different value still yields exactly one `[INPUT]` section with the new value
    - Create `RenoDXCommander.Tests/HotkeyIniIdempotencePropertyTests.cs`
    - **Validates: Requirements 6.2**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Integrate hotkey into MergeRsIni and MergeRsVulkanIni
  - [x] 5.1 Add optional `string? overlayHotkey = null` parameter to `MergeRsIni` and `MergeRsVulkanIni` in `RenoDXCommander/Services/AuxInstallService.Ini.cs`
    - After existing merge + screenshot path logic, if `overlayHotkey` is non-null and `!IsDefaultHotkey(overlayHotkey)`, call `ApplyOverlayHotkey`
    - When hotkey is default (`"36,0,0,0"`) or null, do NOT write `KeyOverlay` — preserve template values
    - Update all call sites of `MergeRsIni`/`MergeRsVulkanIni` to pass the hotkey from `SettingsViewModel.OverlayHotkey`
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 5.2 Write property test for MergeRsIni writes non-default hotkey (Property 7)
    - **Property 7: MergeRsIni writes non-default hotkey**
    - For any non-default KeyOverlay string, after `MergeRsIni` with that value, the resulting INI has `[INPUT]` with `KeyOverlay` equal to the value
    - Create `RenoDXCommander.Tests/MergeRsIniHotkeyPropertyTests.cs`
    - **Validates: Requirements 5.1, 5.3**

  - [x] 5.3 Write property test for MergeRsIni skips default hotkey (Property 8)
    - **Property 8: MergeRsIni skips default hotkey**
    - When `overlayHotkey` is `"36,0,0,0"` or null, existing `KeyOverlay` values in the template-merged result are unchanged
    - Create `RenoDXCommander.Tests/MergeRsIniDefaultHotkeySkipPropertyTests.cs`
    - **Validates: Requirements 5.2**

- [x] 6. Build the Settings page UI for the hotkey section
  - [x] 6.1 Restructure the Screenshots `<Border>` in the Settings page XAML into a two-column `<Grid>` with Screenshots on the left and ReShade UI Hotkey on the right
    - Add `HotkeyBox` (read-only TextBox with `PreviewKeyDown` handler) and `ApplyHotkeyBtn` (Button with `Click` handler) to the right column
    - Each sub-section has its own heading ("Screenshots" and "ReShade UI Hotkey")
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 6.2 Implement `HotkeyBox_PreviewKeyDown` handler in `SettingsHandler`
    - Ignore modifier-only keys (Ctrl, Shift, Alt, Windows)
    - Extract VK code and modifier state, build hotkey string, update display text, mark event handled
    - _Requirements: 2.1, 2.2, 2.3, 7.4_

  - [x] 6.3 Implement `ApplyOverlayHotkey_Click` handler in `SettingsHandler`
    - Persist hotkey via `SettingsViewModel.OverlayHotkey` → `SaveSettingsPublic()`
    - Iterate `ViewModel.AllCards`, enumerate `reshade*.ini` files per game (same pattern as `ApplyScreenshotPath_Click`)
    - Call `AuxInstallService.ApplyOverlayHotkey` for each file, catch `IOException`, log failures
    - Show confirmation dialog with count of updated files
    - _Requirements: 4.1, 4.2, 4.3, 4.5, 4.6_

  - [x] 6.4 Initialize `HotkeyBox` display on Settings page open
    - In `SettingsButton_Click`, read `ViewModel.Settings.OverlayHotkey` and set `HotkeyBox.Text` to the formatted display string using `FormatHotkeyDisplay`
    - _Requirements: 2.4, 3.2_

- [x] 7. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The project uses C# with WinUI 3, FsCheck.Xunit for property-based tests
