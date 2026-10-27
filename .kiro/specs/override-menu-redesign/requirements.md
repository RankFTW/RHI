# Requirements Document

## Introduction

The DLL naming override system in RenoDXCommander currently uses two independent toggles — one for ReShade (RS) and one for Display Commander (DC) — each with its own editable ComboBox dropdown. This creates complex cross-toggle interaction scenarios: users must reason about which toggle controls which component, and the system must handle partial-on states (RS on / DC off, RS off / DC on, both on, both off). The redesign simplifies this to a single toggle that controls both RS and DC overrides together, reducing cognitive load and eliminating partial-on edge cases.

The underlying `DllOverrideConfig` model (which already stores both `ReShadeFileName` and `DcFileName` per game) does not need to change. The changes are purely in the UI layer (`DetailPanelBuilder.Overrides.cs` and `OverridesFlyoutBuilder.cs`) and the toggle-on/toggle-off orchestration logic that calls into `DllOverrideService`.

## Glossary

- **Override_Toggle**: The single ToggleSwitch control that enables or disables DLL naming overrides for both RS and DC simultaneously. Replaces the current two-toggle system (`dllOverrideToggle` + `dcDllToggle`).
- **RS_Dropdown**: The editable ComboBox for selecting the ReShade DLL filename. Populated from `DllOverrideConstants.CommonDllNames`.
- **DC_Dropdown**: The editable ComboBox for selecting the Display Commander filename. Populated from `DetailPanelBuilder.DcDllOverrideNames`.
- **DllOverrideService**: The service in `Services/DllOverrideService.cs` that owns override CRUD, file renames on enable/disable/update, and the backing `_dllOverrides` dictionary.
- **DllOverrideConfig**: The data model (`Models/DllOverrideConfig.cs`) holding `ReShadeFileName` and `DcFileName` per game.
- **DetailPanelBuilder_Overrides**: The UI builder in `DetailPanelBuilder.Overrides.cs` that constructs the overrides panel for the detail view.
- **OverridesFlyoutBuilder**: The UI builder in `OverridesFlyoutBuilder.cs` that constructs the overrides flyout popup.
- **RsNormalName**: The default RS install filename `dxgi.dll` (constant in `AuxInstallService`).
- **Default_DC_Name**: The default DC addon filename `zzz_display_commander_lite.addon64` (or `.addon32` for 32-bit games).
- **Safe_RS_Name**: The non-conflicting initial RS name when the override is toggled ON: `ReShade64.dll` (or `ReShade32.dll` for 32-bit games).
- **Cross_Exclusion_Filter**: The existing dropdown filtering logic that removes the other component's current filename from each ComboBox's item list to prevent name collisions.

## Requirements

### Requirement 1: Remove the Separate DC Toggle

**User Story:** As a user, I want a single toggle for DLL naming overrides, so that I do not have to manage two independent toggles with confusing cross-toggle interactions.

#### Acceptance Criteria

1. THE DetailPanelBuilder_Overrides SHALL render a single Override_Toggle labeled "DLL naming overrides" in the overrides panel, with no separate DC toggle visible.
2. THE OverridesFlyoutBuilder SHALL render a single Override_Toggle labeled "DLL naming overrides" in the overrides flyout, with no separate DC toggle visible.
3. WHEN the Override_Toggle is OFF, THE DetailPanelBuilder_Overrides SHALL display both RS_Dropdown and DC_Dropdown in a disabled state.
4. WHEN the Override_Toggle is OFF, THE OverridesFlyoutBuilder SHALL display both RS_Dropdown and DC_Dropdown in a disabled state.

### Requirement 2: Single Toggle ON Behavior

**User Story:** As a user, I want toggling ON the override to automatically set safe initial names for both RS and DC, so that I start in a non-conflicting state without manual configuration.

#### Acceptance Criteria

1. WHEN the Override_Toggle is switched from OFF to ON, THE DetailPanelBuilder_Overrides SHALL enable both RS_Dropdown and DC_Dropdown.
2. WHEN the Override_Toggle is switched from OFF to ON, THE OverridesFlyoutBuilder SHALL enable both RS_Dropdown and DC_Dropdown.
3. WHEN the Override_Toggle is switched from OFF to ON and no prior override config exists for the game, THE Override_Toggle handler SHALL auto-select Safe_RS_Name in the RS_Dropdown.
4. WHEN the Override_Toggle is switched from OFF to ON and no prior override config exists for the game, THE Override_Toggle handler SHALL keep DC under Default_DC_Name in the DC_Dropdown.
5. WHEN the Override_Toggle is switched from OFF to ON, THE Override_Toggle handler SHALL call DllOverrideService.EnableDllOverride with the selected RS and DC filenames.
6. WHEN the Override_Toggle is switched from OFF to ON and a prior override config exists for the game, THE Override_Toggle handler SHALL restore the previously saved RS and DC filenames into the dropdowns.

### Requirement 3: Single Toggle OFF Behavior

**User Story:** As a user, I want toggling OFF the override to revert both RS and DC to their default filenames, so that I can return to the standard configuration in one action.

#### Acceptance Criteria

1. WHEN the Override_Toggle is switched from ON to OFF, THE Override_Toggle handler SHALL call DllOverrideService.DisableDllOverride to revert both RS and DC files to their default names.
2. WHEN the Override_Toggle is switched from ON to OFF, THE DetailPanelBuilder_Overrides SHALL disable both RS_Dropdown and DC_Dropdown and clear their displayed values.
3. WHEN the Override_Toggle is switched from ON to OFF, THE OverridesFlyoutBuilder SHALL disable both RS_Dropdown and DC_Dropdown and clear their displayed values.
4. IF DllOverrideService.DisableDllOverride reports that RS revert failed (default name occupied), THEN THE Override_Toggle handler SHALL display a tooltip on the Override_Toggle explaining that RS was renamed to a fallback name.
5. IF DllOverrideService.DisableDllOverride reports that DC revert failed (default name occupied), THEN THE Override_Toggle handler SHALL display a tooltip on the Override_Toggle explaining that DC was kept under its current name.

### Requirement 4: Cross-Exclusion Filtering Preservation

**User Story:** As a user, I want the system to continue preventing me from picking the same filename for both RS and DC, so that I do not accidentally create a name collision.

#### Acceptance Criteria

1. WHILE the Override_Toggle is ON, THE Cross_Exclusion_Filter SHALL remove the RS_Dropdown's current selection from the DC_Dropdown's item list.
2. WHILE the Override_Toggle is ON, THE Cross_Exclusion_Filter SHALL remove the DC_Dropdown's current selection from the RS_Dropdown's item list.
3. WHEN the user types a name into the RS_Dropdown that matches the DC_Dropdown's current selection (case-insensitive), THE RS_Dropdown handler SHALL reject the typed name and revert to the previous valid RS selection.
4. WHEN the user types a name into the DC_Dropdown that matches the RS_Dropdown's current selection (case-insensitive), THE DC_Dropdown handler SHALL reject the typed name and revert to the previous valid DC selection.

### Requirement 5: Dropdown Selection Auto-Save

**User Story:** As a user, I want my dropdown selections to be persisted immediately when I change them, so that I do not lose my configuration.

#### Acceptance Criteria

1. WHEN the user selects a new RS filename from the RS_Dropdown while the Override_Toggle is ON, THE RS_Dropdown handler SHALL call DllOverrideService.UpdateDllOverrideNames with the new RS name and the current DC name.
2. WHEN the user selects a new DC filename from the DC_Dropdown while the Override_Toggle is ON, THE DC_Dropdown handler SHALL call DllOverrideService.UpdateDllOverrideNames with the current RS name and the new DC name.
3. WHEN the user types a custom RS filename and presses Enter while the Override_Toggle is ON, THE RS_Dropdown handler SHALL call DllOverrideService.UpdateDllOverrideNames with the typed RS name and the current DC name.
4. WHEN the user types a custom DC filename and presses Enter while the Override_Toggle is ON, THE DC_Dropdown handler SHALL call DllOverrideService.UpdateDllOverrideNames with the current RS name and the typed DC name.
5. WHEN the user selects a new DC filename from the DC_Dropdown, THE DC_Dropdown handler SHALL check for foreign DLL conflicts via DllOverrideService.CheckDcForeignDllConflictAsync before persisting.
6. IF DllOverrideService.CheckDcForeignDllConflictAsync returns false (user cancelled), THEN THE DC_Dropdown handler SHALL revert the DC_Dropdown to its previous selection.

### Requirement 6: Reset Overrides Integration

**User Story:** As a user, I want the "Reset Overrides" button to disable the single override toggle and revert both components, so that the reset behavior is consistent with the new single-toggle design.

#### Acceptance Criteria

1. WHEN the user clicks "Reset Overrides", THE reset handler SHALL set the Override_Toggle to OFF.
2. WHEN the user clicks "Reset Overrides" and a DLL override config exists, THE reset handler SHALL call DllOverrideService.DisableDllOverride to revert both RS and DC files to their default names.

### Requirement 7: Manifest-Driven Override Compatibility

**User Story:** As a user, I want manifest-driven DLL overrides to continue working with the single-toggle design, so that remote manifest entries still apply correctly.

#### Acceptance Criteria

1. WHEN a manifest-driven DLL override is injected for a game, THE Override_Toggle SHALL reflect the enabled state (IsOn = true).
2. WHEN a manifest-driven DLL override is injected for a game, THE RS_Dropdown and DC_Dropdown SHALL display the manifest-provided filenames.
3. WHEN the user toggles the Override_Toggle OFF for a manifest-injected override, THE DllOverrideService SHALL record the opt-out in ManifestDllOverrideOptOuts so the override is not re-injected on refresh.

### Requirement 8: Luma Mode Interaction

**User Story:** As a user, I want the override toggle to be disabled when Luma mode is active, so that I cannot create conflicting configurations.

#### Acceptance Criteria

1. WHILE Luma mode is active for a game, THE Override_Toggle SHALL be disabled (IsEnabled = false).
2. WHILE Luma mode is active for a game, THE RS_Dropdown and DC_Dropdown SHALL remain disabled regardless of the Override_Toggle state.
