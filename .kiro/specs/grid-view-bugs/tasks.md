# Implementation Plan

- [x] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - Grid View Install Flyout Target Null & Overrides Flyout Missing DC Custom DLL Selector
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bugs exist
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate both bugs exist
  - **Scoped PBT Approach**: Scope the property to concrete failing cases:
    - Bug 1: For any `GameCardViewModel`, `CardBuilder.BuildGameCard(card)` produces a card where the install button's `Flyout.Target` is null
    - Bug 2: For any `GameCardViewModel`, `OverridesFlyoutBuilder.OpenOverridesFlyout` produces a panel where `modeGrid.ColumnDefinitions.Count == 2` (not 3), no vertical `Border` divider exists, and no `ComboBox` with `Header == "DC Custom DLL filename"` is present
  - Test that `BuildGameCard(card)` returns a card where the install `Flyout.Target` is null (from Bug Condition in design: `installFlyout.Target == null`)
  - Test that the overrides flyout panel has only 2 column definitions in the mode grid instead of 3 (from Bug Condition in design: `modeGrid.ColumnCount != 3`)
  - Test that no `dcCustomDllSelector` ComboBox exists in the overrides flyout panel (from Bug Condition in design: `dcCustomDllSelector NOT IN overridesFlyoutPanel.Children`)
  - Test that no vertical `Border` divider exists between DC Mode and Shaders sections (from Bug Condition in design: `verticalDivider NOT IN modeGrid.Children`)
  - Run test on UNFIXED code - expect FAILURE (this confirms the bugs exist)
  - **EXPECTED OUTCOME**: Test FAILS (this is correct - it proves the bugs exist)
  - Document counterexamples found (e.g., "`installFlyout.Target` is null after `BuildGameCard` returns", "mode grid has 2 columns instead of 3", "no DC custom DLL selector ComboBox found")
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Existing Card Structure and Non-DC Overrides Sections Unchanged
  - **IMPORTANT**: Follow observation-first methodology
  - **IMPORTANT**: Write and run these tests BEFORE implementing the fix
  - Observe on UNFIXED code:
    - `CardBuilder.BuildGameCard(card)` produces a card with header row (source icon, game name, fav button, more button), status dots (RDX, RS, DC, optionally Luma), install button with flyout, and bottom row (info buttons, overrides gear)
    - `OverridesFlyoutBuilder.OpenOverridesFlyout` produces a panel with game name editor, wiki name editor, DC Mode combo, shader toggle, DLL naming override section, global update inclusion toggles, wiki exclusion toggle, and reset overrides button
    - `CardBuilder.BuildInstallFlyoutContent(card)` produces component rows (RS, DC, RDX, optionally Luma) with status text, install button, copy config button, and uninstall button
  - Write property-based tests:
    - For all `GameCardViewModel` configurations, `BuildGameCard` produces a card with the same structural elements (header, status dots, bottom row) regardless of the fix
    - For all `GameCardViewModel` configurations, the overrides flyout contains game name editor, wiki name editor, DLL naming override, global update inclusion toggles, wiki exclusion toggle, and reset overrides button
    - For all `GameCardViewModel` configurations, `BuildInstallFlyoutContent` produces the same component rows with correct structure
  - Verify tests PASS on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (this confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

- [x] 3. Fix for grid view install flyout and overrides flyout bugs

  - [x] 3.1 Set `installFlyout.Target` in `CardBuilder.BuildGameCard`
    - In `RenoDXCommander/CardBuilder.cs`, in the `BuildGameCard` method, after creating `installBtn` and before adding it to `root`, add `installFlyout.Target = installBtn;`
    - This ensures `CardInstallFlyout_Opening` can extract the `GameCardViewModel` from `flyout.Target.Tag`
    - _Bug_Condition: isBugCondition(input) where input.action == "ClickInstallButton" AND input.viewMode == "Grid" AND installFlyout.Target == null_
    - _Expected_Behavior: installFlyout.Target == installBtn so CardInstallFlyout_Opening builds flyout content_
    - _Preservation: Card structure (header, status dots, bottom row) unchanged; detail view install buttons unaffected_
    - _Requirements: 2.1, 3.1, 3.5_

  - [x] 3.2 Add `dcCustomDllSelector` ComboBox to `OverridesFlyoutBuilder.OpenOverridesFlyout`
    - In `RenoDXCommander/OverridesFlyoutBuilder.cs`, after creating `dcModeCombo`, add an editable `ComboBox` with `ItemsSource = DllOverrideConstants.DcDllPickerNames`, `PlaceholderText = "Select or type DLL filename"`, `Header = "DC Custom DLL filename"`, initially collapsed (visible only when `dcModeCombo.SelectedIndex == 2`)
    - Pre-populate with saved filename via `ViewModel.GetDcCustomDllFileName(gameName)` matching `DetailPanelBuilder`'s logic
    - _Bug_Condition: isBugCondition(input) where input.action == "OpenOverridesFlyout" AND dcCustomDllSelector NOT IN panel_
    - _Expected_Behavior: dcCustomDllSelector visible when DC Mode == "Custom", populated with DcDllPickerNames_
    - _Requirements: 2.2_

  - [x] 3.3 Add `RenameDcToCustom` helper and wire event handlers
    - Add a local `RenameDcToCustom(string dllFileName)` helper function matching `DetailPanelBuilder`'s implementation (renames installed DC file on disk)
    - Add `dcCustomDllPickerIsTyping` and `dcCustomDllPickerJustCommitted` tracking booleans
    - Wire up `SelectionChanged` handler: on dropdown selection (not during typing), persist filename and call `RenameDcToCustom`
    - Wire up `TextSubmitted` handler: on Enter key, persist typed filename and call `RenameDcToCustom`
    - Wire up `GotFocus` handler: set `dcCustomDllPickerIsTyping = true`
    - Wire up `LostFocus` handler: commit on focus loss if filename changed
    - _Bug_Condition: isBugCondition(input) where RenameDcToCustom helper and event handlers are missing_
    - _Expected_Behavior: selecting or typing a custom DLL filename persists the setting and renames the DC file_
    - _Requirements: 2.4_

  - [x] 3.4 Change `modeGrid` to three-column layout with vertical divider
    - Replace the two-column `modeGrid` (`Star, Star`) with a three-column grid (`Star, Auto, Star`)
    - Column 0: `StackPanel` containing `dcModeCombo` and `dcCustomDllSelector`
    - Column 1: 1px vertical `Border` divider with `Margin = new Thickness(12, 0, 12, 0)`
    - Column 2: `StackPanel` containing `shaderToggle` and `selectShadersBtn`
    - Remove the standalone `panel.Children.Add(selectShadersBtn)` since it moves into the shaders column
    - _Bug_Condition: isBugCondition(input) where modeGrid.ColumnCount != 3 OR verticalDivider NOT IN modeGrid_
    - _Expected_Behavior: three-column layout (Star, Auto, Star) with 1px vertical Border divider_
    - _Requirements: 2.3_

  - [x] 3.5 Update DC Mode `SelectionChanged` to toggle `dcCustomDllSelector` visibility
    - In the existing `dcModeCombo.SelectionChanged` handler, add logic to set `dcCustomDllSelector.Visibility` based on `dcModeCombo.SelectedIndex == 2`
    - When switching to Custom mode with a filename already set, call `RenameDcToCustom`
    - _Requirements: 2.2, 2.4_

  - [x] 3.6 Update Reset Overrides handler to clear DC custom DLL state
    - In the `resetOverridesBtn.Click` handler, add: `ViewModel.SetDcCustomDllFileName(capturedName, null)` to clear the saved filename
    - Reset `dcCustomDllSelector` text/selection and collapse its visibility
    - _Requirements: 2.2_

  - [x] 3.7 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - Grid View Install Flyout Target Set & Overrides Flyout Has DC Custom DLL Selector
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - The test from task 1 encodes the expected behavior
    - When this test passes, it confirms the expected behavior is satisfied
    - Run bug condition exploration test from step 1
    - **EXPECTED OUTCOME**: Test PASSES (confirms bugs are fixed)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.8 Verify preservation tests still pass
    - **Property 2: Preservation** - Existing Card Structure and Non-DC Overrides Sections Unchanged
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions)
    - Confirm all tests still pass after fix (no regressions)

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.
