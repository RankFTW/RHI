# Grid View Bugs — Bugfix Design

## Overview

Two bugs in the grid view of RenoDXCommander need fixing. First, the install flyout on grid view cards is non-functional because `CardBuilder.BuildGameCard` never sets `installFlyout.Target`, so the `CardInstallFlyout_Opening` handler cannot extract the `GameCardViewModel` from `flyout.Target.Tag` and silently returns. Second, the override popup built by `OverridesFlyoutBuilder` is missing the DC custom DLL filename selector ComboBox, the `RenameDcToCustom` helper with its event handlers, a vertical divider between the DC Mode and Shaders sections, and the proper three-column layout — all of which exist in the reference implementation in `DetailPanelBuilder.BuildOverridesPanel`.

## Glossary

- **Bug_Condition (C)**: The conditions that trigger each bug — (1) clicking the install button on a grid card where `flyout.Target` is null; (2) opening the overrides flyout when DC Mode is "Custom" and the DC custom DLL selector is absent.
- **Property (P)**: The desired behavior — (1) the flyout opens with full component content; (2) the overrides flyout displays the DC custom DLL selector, vertical divider, and three-column layout matching the detail view.
- **Preservation**: Existing detail view behavior, mouse clicks, card grid rendering, and all non-affected overrides flyout sections must remain unchanged.
- **CardBuilder**: The class in `RenoDXCommander/CardBuilder.cs` that builds grid view game cards including the install flyout.
- **OverridesFlyoutBuilder**: The class in `RenoDXCommander/OverridesFlyoutBuilder.cs` that builds the per-game overrides flyout for grid view.
- **DetailPanelBuilder**: The class in `RenoDXCommander/DetailPanelBuilder.cs` that builds the detail view overrides panel — the reference implementation for correct behavior.
- **installFlyout.Target**: The `Flyout.Target` property that must reference the install button so `CardInstallFlyout_Opening` can extract the `GameCardViewModel` from `Target.Tag`.
- **dcCustomDllSelector**: An editable `ComboBox` populated with `DllOverrideConstants.DcDllPickerNames` that lets users select or type a custom DC DLL filename.
- **RenameDcToCustom**: A helper function that renames the installed DC file on disk to match the chosen custom DLL filename.

## Bug Details

### Bug Condition

**Bug 1 — Install Flyout Target Not Set:**
The bug manifests when a user clicks the install button on any grid view card. The `Flyout` object created in `CardBuilder.BuildGameCard` has its `Opening` event wired to `CardInstallFlyout_Opening`, but `installFlyout.Target` is never assigned. The handler checks `flyout.Target is not FrameworkElement { Tag: GameCardViewModel card }` and returns immediately, producing an empty flyout.

**Bug 2 — Overrides Flyout Missing DC Custom DLL Selector and Layout:**
The bug manifests when a user opens the overrides flyout in grid view. `OverridesFlyoutBuilder.OpenOverridesFlyout` builds the DC Mode + Shaders section as a simple two-column grid without a vertical divider, and omits the `dcCustomDllSelector` ComboBox, the `RenameDcToCustom` helper, and all associated event handlers (`SelectionChanged`, `TextSubmitted`, `GotFocus`, `LostFocus`). The detail view's `DetailPanelBuilder.BuildOverridesPanel` has all of these.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type UserInteraction
  OUTPUT: boolean

  // Bug 1: Install flyout on grid card
  IF input.action == "ClickInstallButton" AND input.viewMode == "Grid"
    RETURN installFlyout.Target == null

  // Bug 2: Overrides flyout missing DC custom DLL selector
  IF input.action == "OpenOverridesFlyout" AND input.viewMode == "Grid"
    RETURN dcCustomDllSelector NOT IN overridesFlyoutPanel.Children
           OR verticalDivider NOT IN modeGrid.Children
           OR modeGrid.ColumnCount != 3

  RETURN false
END FUNCTION
```

### Examples

- **Bug 1 — Install button click**: User clicks "Install" on a grid card for "Cyberpunk 2077". Expected: flyout opens showing RS, DC, RDX component rows. Actual: flyout opens empty because `Target` is null.
- **Bug 2 — DC Mode Custom selected**: User opens overrides flyout, sets DC Mode to "Custom". Expected: a `dcCustomDllSelector` ComboBox appears below the DC Mode combo, allowing selection from `DcDllPickerNames`. Actual: no selector appears; the user cannot set a custom DLL filename.
- **Bug 2 — Missing divider**: User opens overrides flyout. Expected: a 1px vertical divider separates DC Mode (left) from Shaders (right) in a three-column grid. Actual: DC Mode and Shaders are in a two-column grid with no divider.
- **Bug 2 — RenameDcToCustom missing**: User selects "winmm.dll" in the DC custom DLL selector. Expected: the installed DC file is renamed on disk and the record is updated. Actual: nothing happens because the helper and event handlers don't exist.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Detail view overrides panel (`DetailPanelBuilder.BuildOverridesPanel`) must continue to function identically with its DC custom DLL selector, vertical divider, and three-column layout.
- Mouse clicks on grid card install buttons must continue to open the flyout (the flyout mechanism itself is not broken, only the content population).
- All other overrides flyout sections (game name editor, wiki name editor, DLL naming override, global update inclusion toggles, wiki exclusion toggle, reset overrides button) must remain unchanged.
- Card grid rendering, detail panel population, and view switching must remain unchanged.
- The `CardInstallFlyout_Opening` handler logic must not be modified — only the missing `Target` assignment is added.

**Scope:**
All inputs that do NOT involve (1) the grid view install flyout opening or (2) the grid view overrides flyout DC Mode/Shaders section should be completely unaffected by this fix. This includes:
- Detail view install buttons and flyouts
- Detail view overrides panel
- Settings page interactions
- Search, filter, and sort operations
- Card favourite, hide, and more-menu actions

## Hypothesized Root Cause

Based on the bug description and code analysis, the root causes are:

1. **Missing `installFlyout.Target` assignment (Bug 1)**: In `CardBuilder.BuildGameCard`, the `installFlyout` is created and its `Opening` event is wired, but the line `installFlyout.Target = installBtn` is never executed. The `Flyout.Target` property is only automatically set when using XAML `Button.Flyout` in some scenarios, but in this imperative code-behind construction, it must be set explicitly. The `CardInstallFlyout_Opening` handler relies on `flyout.Target` to extract the card's `GameCardViewModel`.

2. **Missing `dcCustomDllSelector` ComboBox (Bug 2)**: `OverridesFlyoutBuilder.OpenOverridesFlyout` was likely created by copying from `DetailPanelBuilder.BuildOverridesPanel` but the DC custom DLL selector section was omitted. The `dcCustomDllSelector` ComboBox, its pre-population logic, and the `RenameDcToCustom` helper with all four event handlers (`SelectionChanged`, `TextSubmitted`, `GotFocus`, `LostFocus`) are missing.

3. **Missing vertical divider and wrong column count (Bug 2)**: The `modeGrid` in `OverridesFlyoutBuilder` uses a two-column layout (`Star, Star`) instead of the three-column layout (`Star, Auto, Star`) used in `DetailPanelBuilder`. The vertical `Border` divider element is absent.

4. **Missing DC custom DLL reset in Reset Overrides (Bug 2)**: The reset overrides button handler in `OverridesFlyoutBuilder` does not clear the DC custom DLL filename or reset the `dcCustomDllSelector` visibility, unlike the detail view's reset handler.

## Correctness Properties

Property 1: Bug Condition — Install Flyout Populates Content

_For any_ grid view card where the user clicks the install button, the fixed `CardBuilder.BuildGameCard` SHALL set `installFlyout.Target = installBtn` so that `CardInstallFlyout_Opening` can extract the `GameCardViewModel` from `flyout.Target.Tag` and build the flyout content with component rows.

**Validates: Requirements 2.1**

Property 2: Bug Condition — Overrides Flyout DC Custom DLL Selector

_For any_ grid view card where the user opens the overrides flyout and sets DC Mode to "Custom", the fixed `OverridesFlyoutBuilder.OpenOverridesFlyout` SHALL display a `dcCustomDllSelector` ComboBox (editable, populated with `DllOverrideConstants.DcDllPickerNames`) with the `RenameDcToCustom` helper and `SelectionChanged`, `TextSubmitted`, `GotFocus`, `LostFocus` event handlers, matching the detail view behavior.

**Validates: Requirements 2.2, 2.4**

Property 3: Bug Condition — Overrides Flyout Three-Column Layout with Divider

_For any_ grid view card where the user opens the overrides flyout, the fixed `OverridesFlyoutBuilder.OpenOverridesFlyout` SHALL display the DC Mode section and Shaders section in a three-column grid layout (Star, Auto, Star) with a 1px vertical `Border` divider between them, matching the detail view's overrides panel layout.

**Validates: Requirements 2.3**

Property 4: Preservation — Detail View and Non-Affected Functionality

_For any_ interaction that does NOT involve the grid view install flyout or the grid view overrides flyout DC Mode/Shaders section, the fixed code SHALL produce exactly the same behavior as the original code, preserving all existing functionality for the detail view, card rendering, and all other overrides flyout sections.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `RenoDXCommander/CardBuilder.cs`

**Function**: `BuildGameCard`

**Specific Changes**:
1. **Set `installFlyout.Target`**: After creating `installBtn` (around line 230), add `installFlyout.Target = installBtn;` before `installBtn` is added to the root panel. This ensures the `Target` property is set before the flyout's `Opening` event can fire.

---

**File**: `RenoDXCommander/OverridesFlyoutBuilder.cs`

**Function**: `OpenOverridesFlyout`

**Specific Changes**:
1. **Add `dcCustomDllSelector` ComboBox**: After creating `dcModeCombo`, add an editable `ComboBox` with `ItemsSource = DllOverrideConstants.DcDllPickerNames`, `PlaceholderText = "Select or type DLL filename"`, `Header = "DC Custom DLL filename"`, initially collapsed (visible only when DC Mode index == 2). Pre-populate with saved filename via `ViewModel.GetDcCustomDllFileName(gameName)`.

2. **Add `RenameDcToCustom` helper**: Add a local helper function matching `DetailPanelBuilder`'s implementation that renames the installed DC file on disk when the custom DLL filename changes.

3. **Add event handlers**: Wire up `SelectionChanged`, `TextSubmitted`, `GotFocus`, and `LostFocus` on `dcCustomDllSelector` with the same logic as `DetailPanelBuilder`, using `dcCustomDllPickerIsTyping` and `dcCustomDllPickerJustCommitted` tracking booleans.

4. **Change `modeGrid` to three-column layout**: Replace the two-column `modeGrid` (`Star, Star`) with a three-column grid (`Star, Auto, Star`). Column 0 gets a `StackPanel` containing `dcModeCombo` and `dcCustomDllSelector`. Column 1 gets a 1px vertical `Border` divider. Column 2 gets a `StackPanel` containing `shaderToggle` and `selectShadersBtn` (moved from being a separate panel child).

5. **Remove standalone `selectShadersBtn`**: Since `selectShadersBtn` moves into the shaders column of the three-column grid, remove the standalone `panel.Children.Add(selectShadersBtn)` call.

6. **Update DC Mode `SelectionChanged` handler**: Add logic to toggle `dcCustomDllSelector.Visibility` based on `dcModeCombo.SelectedIndex == 2`, and trigger `RenameDcToCustom` when switching to Custom mode with a filename already set.

7. **Update Reset Overrides handler**: Add lines to clear the DC custom DLL filename (`ViewModel.SetDcCustomDllFileName(capturedName, null)`), reset the `dcCustomDllSelector` state, and collapse its visibility.

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bugs on unfixed code, then verify the fixes work correctly and preserve existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bugs BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write tests that verify the install flyout's `Target` property and the overrides flyout's panel structure. Run these tests on the UNFIXED code to observe failures.

**Test Cases**:
1. **Install Flyout Target Test**: Call `CardBuilder.BuildGameCard(card)` and inspect the `Flyout` attached to the install button — verify `flyout.Target` is null (will fail on unfixed code, confirming the bug).
2. **Overrides Flyout DC Custom DLL Selector Test**: Call `OpenOverridesFlyout` with DC Mode "Custom" and inspect the panel for a `dcCustomDllSelector` ComboBox (will fail on unfixed code — no such ComboBox exists).
3. **Overrides Flyout Divider Test**: Call `OpenOverridesFlyout` and inspect the mode grid for a vertical `Border` divider (will fail on unfixed code — only two columns, no divider).
4. **Overrides Flyout Column Count Test**: Inspect `modeGrid.ColumnDefinitions.Count` — expect 3 (will fail on unfixed code — returns 2).

**Expected Counterexamples**:
- `installFlyout.Target` is null after `BuildGameCard` returns
- No `ComboBox` with `Header == "DC Custom DLL filename"` in the overrides flyout panel
- `modeGrid` has 2 column definitions instead of 3
- Possible causes: omitted code during extraction from `DetailPanelBuilder` to `OverridesFlyoutBuilder`

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed functions produce the expected behavior.

**Pseudocode:**
```
FOR ALL card IN gameCards DO
  border := CardBuilder.BuildGameCard(card)
  installBtn := FindInstallButton(border)
  flyout := installBtn.Flyout
  ASSERT flyout.Target == installBtn
END FOR

FOR ALL card IN gameCards DO
  panel := OpenOverridesFlyout(card)
  modeGrid := FindModeGrid(panel)
  ASSERT modeGrid.ColumnDefinitions.Count == 3
  ASSERT FindDivider(modeGrid) != null
  SetDcModeToCustom(panel)
  ASSERT FindDcCustomDllSelector(panel).Visibility == Visible
  ASSERT FindDcCustomDllSelector(panel).ItemsSource == DcDllPickerNames
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed functions produce the same result as the original functions.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT originalFunction(input) = fixedFunction(input)
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain
- It catches edge cases that manual unit tests might miss
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Observe behavior on UNFIXED code first for detail view interactions and non-DC-Mode overrides sections, then write property-based tests capturing that behavior.

**Test Cases**:
1. **Detail View Overrides Preservation**: Verify that `DetailPanelBuilder.BuildOverridesPanel` produces identical output before and after the fix — no changes should affect this file's behavior.
2. **Card Grid Rendering Preservation**: Verify that `CardBuilder.BuildGameCard` produces cards with the same visual structure (header, status dots, bottom row) before and after the fix, with only the `Target` property added.
3. **Overrides Flyout Non-DC Sections Preservation**: Verify that game name editor, wiki name editor, DLL naming override, global update inclusion, wiki exclusion, and reset overrides sections remain unchanged.
4. **Install Flyout Content Preservation**: Verify that `BuildInstallFlyoutContent` produces the same component rows (RS, DC, RDX, Luma) before and after the fix.

### Unit Tests

- Test that `BuildGameCard` sets `installFlyout.Target` to the install button
- Test that `OpenOverridesFlyout` creates a three-column mode grid with a vertical divider
- Test that `dcCustomDllSelector` is visible when DC Mode is "Custom" and collapsed otherwise
- Test that `RenameDcToCustom` renames the DC file on disk correctly
- Test that Reset Overrides clears the DC custom DLL filename and collapses the selector

### Property-Based Tests

- Generate random `GameCardViewModel` configurations and verify `BuildGameCard` always sets `flyout.Target` to the install button
- Generate random DC Mode selections (Global/Off/Custom) and verify `dcCustomDllSelector` visibility matches `SelectedIndex == 2`
- Generate random game states and verify all non-affected overrides flyout sections remain structurally identical

### Integration Tests

- Test full grid view flow: render card grid, click install button, verify flyout content populates
- Test overrides flyout flow: open flyout, switch DC Mode to Custom, select a DLL filename, verify persistence
- Test view switching: open overrides in grid view, switch to detail view, verify detail panel shows same DC custom DLL setting
