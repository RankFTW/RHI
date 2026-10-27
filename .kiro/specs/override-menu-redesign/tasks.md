# Implementation Plan: Override Menu Redesign

## Overview

Replace the two-toggle DLL naming override system (`dllOverrideToggle` + `dcDllToggle`) with a single unified toggle in both `DetailPanelBuilder.Overrides.cs` and `OverridesFlyoutBuilder.cs`. No service or model changes needed — all changes are in the UI layer. The existing `DllOverrideService.EnableDllOverride`, `DisableDllOverride`, and `UpdateDllOverrideNames` already handle both RS and DC atomically.

## Tasks

- [x] 1. Remove dcDllToggle and rewrite dllOverrideToggle handler in DetailPanelBuilder.Overrides.cs
  - [x] 1.1 Remove the dcDllToggle ToggleSwitch declaration, its Toggled handler, and the layout line that adds it to topRightColumn
    - Delete the `dcDllToggle` variable declaration (ToggleSwitch with Header = null, OffContent = "Override DC filename")
    - Delete the entire `dcDllToggle.Toggled += ...` handler block (~80 lines handling DC toggle ON/OFF with inline file renames)
    - Remove `topRightColumn.Children.Add(dcDllToggle)` from the layout section
    - Keep `dcNameBox` — it stays in the layout, just controlled by the unified toggle now
    - _Requirements: 1.1_

  - [x] 1.2 Rewrite the dllOverrideToggle.Toggled handler for unified ON/OFF behavior
    - **ON path (no prior config):** Enable both `rsNameBox` and `dcNameBox`. Auto-select `Safe_RS_Name` (`ReShade64.dll` / `ReShade32.dll` based on `card.Is32Bit`) in rsNameBox. Auto-select `Default_DC_Name` (`zzz_display_commander_lite.addon64` / `.addon32`) in dcNameBox. Call `EnableDllOverride(card, rsName, dcName)`.
    - **ON path (prior config exists):** Enable both dropdowns. Restore previously saved RS and DC filenames from `existingCfg`. Call `EnableDllOverride(card, rsName, dcName)`.
    - **OFF path:** Call `DisableDllOverride(card)` which returns `DllDisableResult`. Disable both `rsNameBox` and `dcNameBox`. Clear both (SelectedIndex = -1, Text = ""). If `!result.RsReverted`, set tooltip on toggle explaining RS fallback. If `!result.DcReverted`, set tooltip explaining DC kept under current name.
    - Remove the old partial-on logic that checked `dcDllToggle.IsOn` to decide whether to call `UpdateDllOverrideNames` vs `DisableDllOverride`
    - _Requirements: 2.1, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.4, 3.5_

- [x] 2. Update cross-exclusion filters and dropdown handlers in DetailPanelBuilder.Overrides.cs
  - [x] 2.1 Update UpdateRsDropdownItems() to reference dllOverrideToggle.IsOn instead of dcDllToggle.IsOn
    - Change `var dcCurrentName = dcDllToggle.IsOn ? ... : ""` to `var dcCurrentName = dllOverrideToggle.IsOn ? (dcNameBox.SelectedItem as string ?? "").Trim() : ""`
    - _Requirements: 4.1, 4.2_

  - [x] 2.2 Update RS and DC dropdown SelectionChanged and KeyDown handlers to reference dllOverrideToggle.IsOn
    - In `rsNameBox.KeyDown`: replace `dcDllToggle.IsOn ? (dcNameBox.SelectedItem ...) : ""` with reading dcNameBox directly when `dllOverrideToggle.IsOn`
    - In `rsNameBox.SelectionChanged`: same replacement for the dcName variable
    - In `dcNameBox.SelectionChanged`: replace `if (!dcDllToggle.IsOn) return;` with `if (!dllOverrideToggle.IsOn) return;`
    - In `dcNameBox.KeyDown`: replace `if (!dcDllToggle.IsOn) return;` with `if (!dllOverrideToggle.IsOn) return;`
    - In `dcNameBox.SelectionChanged` collision check: replace `dllOverrideToggle.IsOn` RS name resolution (already correct) but ensure the `else if` branch that checks `targetCard.RsRecord` still works when override is on (the RS name should come from rsNameBox, not RsNormalName)
    - _Requirements: 4.3, 4.4, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

- [x] 3. Update Reset Overrides handler in DetailPanelBuilder.Overrides.cs
  - Remove the line `dcDllToggle.IsOn = false;` from the resetOverridesBtn.Click handler
  - The existing `dllOverrideToggle.IsOn = false;` line stays — it will trigger the unified OFF handler which calls `DisableDllOverride`
  - _Requirements: 6.1, 6.2_

- [x] 4. Checkpoint — Verify DetailPanelBuilder changes compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Remove dcDllToggle and rewrite dllOverrideToggle handler in OverridesFlyoutBuilder.cs
  - [x] 5.1 Remove the dcDllToggle ToggleSwitch declaration, its Toggled handler, and the layout line in OverridesFlyoutBuilder
    - Delete the `dcDllToggle` variable declaration (ToggleSwitch with Header = null, OffContent = "Override DC filename")
    - Delete the entire `dcDllToggle.Toggled += ...` handler block
    - Remove `dllColumn.Children.Add(dcDllToggle)` from the layout section
    - Keep `dcNameBox` in the layout
    - _Requirements: 1.2_

  - [x] 5.2 Rewrite the dllOverrideToggle.Toggled handler in OverridesFlyoutBuilder for unified ON/OFF behavior
    - Same logic as task 1.2 but adapted for the flyout's variable names (`ViewModel` instead of `_window.ViewModel`, etc.)
    - **ON path (no prior config):** Enable both dropdowns. Auto-select `Safe_RS_Name` in rsNameBox, `Default_DC_Name` in dcNameBox. Call `ViewModel.EnableDllOverride(card, rsName, dcName)`.
    - **ON path (prior config exists):** Enable both dropdowns. Restore saved filenames. Call `ViewModel.EnableDllOverride(card, rsName, dcName)`.
    - **OFF path:** Call `ViewModel.DisableDllOverride(card)`. Disable and clear both dropdowns. Handle `DllDisableResult` tooltips.
    - Remove the old partial-on logic that checked `dcDllToggle.IsOn`
    - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.3, 3.4, 3.5_

  - [x] 5.3 Update cross-exclusion filters and dropdown handlers in OverridesFlyoutBuilder
    - Update `UpdateRsDropdownItems()`: replace `dcDllToggle.IsOn` with `dllOverrideToggle.IsOn`
    - Update `rsNameBox.KeyDown` and `rsNameBox.SelectionChanged`: replace `dcDllToggle.IsOn ? ... : ""` with reading dcNameBox when `dllOverrideToggle.IsOn`
    - Update `dcNameBox.SelectionChanged`: replace `if (!dcDllToggle.IsOn)` with `if (!dllOverrideToggle.IsOn)`
    - Update `dcNameBox.KeyDown`: replace `if (!dcDllToggle.IsOn)` with `if (!dllOverrideToggle.IsOn)`
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

- [x] 6. Update Reset Overrides handler in OverridesFlyoutBuilder.cs
  - Remove the line `dcDllToggle.IsOn = false;` from the resetOverridesBtn.Click handler
  - The existing `dllOverrideToggle.IsOn = false;` stays
  - _Requirements: 6.1, 6.2_

- [x] 7. Checkpoint — Verify all changes compile and existing tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Write property test for cross-exclusion dropdown filtering
  - [x] 8.1 Write property test: Cross-exclusion dropdown filtering
    - **Property 1: Cross-exclusion dropdown filtering**
    - **Validates: Requirements 4.1, 4.2**
    - Using FsCheck, generate random RS name from `DllOverrideConstants.CommonDllNames` and random DC name from `DetailPanelBuilder.DcDllOverrideNames`
    - Apply the filtering logic (exclude RS selection from DC list, exclude DC selection from RS list)
    - Assert: filtered DC list does not contain the RS name (case-insensitive), filtered RS list does not contain the DC name (case-insensitive)
    - Minimum 100 iterations

  - [x] 8.2 Write property test: Typed name collision rejection
    - **Property 2: Typed name collision rejection**
    - **Validates: Requirements 4.3, 4.4**
    - Using FsCheck, generate random strings including case variations of known DLL names
    - Simulate: if typed RS name matches current DC selection (case-insensitive), assert rejection
    - Simulate: if typed DC name matches current RS selection (case-insensitive), assert rejection
    - Minimum 100 iterations

- [x] 9. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The `DllOverrideService`, `IDllOverrideService`, and `DllOverrideConfig` model are unchanged — all changes are in the two UI builder files
- The key behavioral change: `dllOverrideToggle` ON now enables BOTH dropdowns and calls `EnableDllOverride` with both RS and DC names; OFF calls `DisableDllOverride` which reverts both
- Cross-exclusion filter algorithm is unchanged — only the toggle reference (`dcDllToggle.IsOn` → `dllOverrideToggle.IsOn`) changes
- Property tests use FsCheck (already in the project's test suite)
