# Bugfix Requirements Document

## Introduction

Two bugs exist in the grid view of RenoDXCommander. First, the install/manage flyout buttons are non-functional for the majority of games because the flyout's `Target` property is never set, causing the `CardInstallFlyout_Opening` handler to silently bail out. Second, the grid view override popup is missing the DC Mode custom DLL filename selector and the vertical divider separating the DC Mode section from the Shaders section, making it inconsistent with the detail view's overrides panel.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a user clicks the install button on a grid view card THEN the system opens a flyout whose `Target` property is null, causing `CardInstallFlyout_Opening` to fail the `flyout.Target is not FrameworkElement { Tag: GameCardViewModel card }` check and return without building any flyout content, making the install button appear non-functional.

1.2 WHEN a user opens the override popup in grid view and DC Mode is set to "Custom" THEN the system does not display a DC custom DLL filename selector (`dcCustomDllSelector` ComboBox), preventing the user from selecting or typing a custom DC DLL filename.

1.3 WHEN a user opens the override popup in grid view THEN the system displays the DC Mode combo and Shaders toggle in a simple two-column grid without a vertical divider between them, making the layout inconsistent with the detail view's overrides panel.

1.4 WHEN a user opens the override popup in grid view and DC Mode is set to "Custom" THEN the system does not provide the `RenameDcToCustom` helper function or its associated event handlers (`SelectionChanged`, `TextSubmitted`, `GotFocus`, `LostFocus`), so selecting or typing a custom DLL filename has no effect.

### Expected Behavior (Correct)

2.1 WHEN a user clicks the install button on a grid view card THEN the system SHALL set the flyout's `Target` property to the install button before the `Opening` event fires, so that `CardInstallFlyout_Opening` can extract the `GameCardViewModel` from `flyout.Target.Tag` and build the flyout content correctly.

2.2 WHEN a user opens the override popup in grid view and DC Mode is set to "Custom" THEN the system SHALL display a `dcCustomDllSelector` ComboBox (editable, populated with `DllOverrideConstants.DcDllPickerNames`) that allows the user to select or type a custom DC DLL filename, matching the detail view behavior.

2.3 WHEN a user opens the override popup in grid view THEN the system SHALL display the DC Mode section and Shaders section in a three-column grid layout with a vertical divider (1px `Border`) between them, matching the detail view's overrides panel layout.

2.4 WHEN a user opens the override popup in grid view and DC Mode is set to "Custom" THEN the system SHALL provide the `RenameDcToCustom` helper function and wire up `SelectionChanged`, `TextSubmitted`, `GotFocus`, and `LostFocus` event handlers on the `dcCustomDllSelector`, so that selecting or typing a custom DLL filename persists the setting and renames the installed DC file accordingly.

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a user clicks the install button on a grid view card that has a valid install path THEN the system SHALL CONTINUE TO show the per-component install flyout with RS, DC, RDX, and Luma rows as applicable.

3.2 WHEN a user opens the override popup in grid view THEN the system SHALL CONTINUE TO display the game name editor, wiki name editor, DC Mode combo, shader toggle, DLL naming override section, global update inclusion toggles, wiki exclusion toggle, and reset overrides button.

3.3 WHEN a user interacts with the detail view's overrides panel THEN the system SHALL CONTINUE TO display the DC custom DLL selector, vertical divider, and all existing functionality without any changes.

3.4 WHEN a user switches between grid view and detail view THEN the system SHALL CONTINUE TO correctly rebuild the card grid and populate the detail panel as before.

3.5 WHEN a user clicks install/manage buttons in the detail view THEN the system SHALL CONTINUE TO function correctly without any changes.
