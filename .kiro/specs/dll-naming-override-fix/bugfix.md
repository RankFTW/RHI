# Bugfix Requirements Document

## Introduction

The DLL naming override system in RenoDXCommander allows users to rename ReShade (RS) and Display Commander (DC) files to custom filenames on a per-game basis. The system has multiple interrelated bugs affecting dropdown display, name conflict resolution, toggle-off revert behavior, and refresh-time naming confirmation. These bugs collectively make the override system unreliable for end-to-end use.

The system involves:
- Two independent toggles: one for RS override, one for DC override
- Each toggle has an editable ComboBox dropdown for selecting/typing a custom filename
- RS default install name is `dxgi.dll`; DC default is `zzz_display_commander_lite.addon64/32`
- Cross-exclusion filtering exists between RS and DC dropdowns to prevent duplicate names
- Manifest-driven overrides can inject DLL names from a remote manifest
- `DllOverrideService.cs` handles all CRUD and file rename operations
- `DetailPanelBuilder.Overrides.cs` builds the UI and wires event handlers
- `AuxInstallService.Install.cs` deploys RS with an optional `filenameOverride` parameter

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a DLL override is active and the user opens the overrides panel THEN the RS dropdown shows the placeholder text "Select ReShade DLL name" instead of the current installed RS filename, even though `existingRsName` is correctly loaded from the config — the ComboBox only sets `SelectedItem` or deferred `Text` but the placeholder remains visible when the value doesn't match a dropdown item exactly or the Loaded event fires inconsistently.

1.2 WHEN a DC override is active and the user opens the overrides panel THEN the DC dropdown shows the placeholder text "Select DC DLL name" instead of the current installed DC filename, with the same root cause as 1.1.

1.3 WHEN RS is installed as `dxgi.dll` (default) and the user attempts to set DC's override name to `dxgi.dll` THEN the cross-exclusion filtering in `UpdateDcDropdownItems()` removes `dxgi.dll` from the DC dropdown only when the RS override toggle is ON — but if the RS override toggle is OFF (RS still physically named `dxgi.dll`), the filter uses `AuxInstallService.RsNormalName` which is `dxgi.dll`, so the filtering works for the dropdown list but does NOT prevent a user from typing `dxgi.dll` manually into the editable DC ComboBox and pressing Enter, creating a name collision on disk.

1.4 WHEN DC is installed with a custom name (e.g., `dxgi.dll`) and RS is subsequently installed or refreshed to the same game folder THEN `InstallReShadeAsync` uses `filenameOverride` if a DLL override config exists, but if no RS override is active, RS installs as `dxgi.dll` (the default `RsNormalName`), overwriting the DC file without checking for conflicts — RS should fall back to `ReShade64.dll` or `ReShade32.dll` when its target name is already occupied by DC.

1.5 WHEN the RS override toggle is turned OFF THEN the `dllOverrideToggle.Toggled` handler in the UI renames RS back to `dxgi.dll` (the default) but does NOT check whether `dxgi.dll` is already occupied by another DLL (e.g., a foreign game DLL or a DC file that was renamed to `dxgi.dll`), potentially overwriting an existing file.

1.6 WHEN the DC override toggle is turned OFF THEN the `dcDllToggle.Toggled` handler renames DC back to `zzz_display_commander_lite.addon64/32` but does NOT check whether that default name is already occupied by another file in the addon deploy path.

1.7 WHEN both overrides are OFF and the user presses Refresh THEN `ApplyManifestDllRenames` only processes manifest-driven overrides and skips games without manifest entries — there is no mechanism to confirm that RS and DC are deployed under their original/default names, so if files were manually moved or corrupted, the naming state is not reconciled on refresh.

1.8 WHEN the RS override toggle is turned ON but no name is selected from the dropdown THEN the handler returns early without calling `EnableDllOverride`, leaving the toggle visually ON but no override config persisted — subsequent interactions (like toggling DC) may read stale or missing config state.

### Expected Behavior (Correct)

2.1 WHEN a DLL override is active for RS and the user opens the overrides panel THEN the RS dropdown SHALL display the current RS filename from the override config (e.g., `d3d11.dll`) as its selected/displayed value, not the placeholder text.

2.2 WHEN a DLL override is active for DC and the user opens the overrides panel THEN the DC dropdown SHALL display the current DC filename from the override config as its selected/displayed value, not the placeholder text.

2.3 WHEN the user attempts to set DC's override name to the same filename that RS currently uses (whether RS override is ON or OFF) THEN the system SHALL prevent the name collision — either by filtering the name from the DC dropdown, rejecting the manually-typed name, or showing a validation error — so that RS and DC never share the same filename in the same game folder.

2.4 WHEN DC is installed with a name that matches RS's target install name (e.g., DC is `dxgi.dll`) and RS is installed or refreshed without an explicit RS override THEN RS SHALL fall back to `ReShade64.dll` or `ReShade32.dll` (based on game bitness) instead of overwriting the DC file, and the RS install record SHALL reflect the fallback name.

2.5 WHEN the RS override toggle is turned OFF THEN the system SHALL check whether the default RS name (`dxgi.dll`) is already occupied by another file before renaming — if occupied, RS SHALL remain under its current custom name or fall back to `ReShade64.dll`/`ReShade32.dll`, and the user SHALL be informed that the revert was not possible.

2.6 WHEN the DC override toggle is turned OFF THEN the system SHALL check whether the default DC name (`zzz_display_commander_lite.addon64/32`) is already occupied by another file before renaming — if occupied, DC SHALL remain under its current custom name, and the user SHALL be informed that the revert was not possible.

2.7 WHEN both overrides are OFF and the user presses Refresh THEN the system SHALL confirm that RS and DC are deployed under their original/default names, and if they are not (e.g., files were renamed externally), the system SHALL redeploy them under the correct default names, respecting conflict checks from 2.5 and 2.6.

2.8 WHEN the RS override toggle is turned ON but no name is selected THEN the system SHALL either keep the toggle OFF until a name is selected, or auto-select the current RS filename as the initial override value, so that the toggle state and persisted config are always consistent.

### Unchanged Behavior (Regression Prevention)

3.1 WHEN the user selects a valid RS override name from the dropdown that does not conflict with DC THEN the system SHALL CONTINUE TO rename the RS file on disk and persist the override config as it does today.

3.2 WHEN the user selects a valid DC override name from the dropdown that does not conflict with RS THEN the system SHALL CONTINUE TO rename the DC file on disk and persist the override config, including the foreign DLL conflict check dialog.

3.3 WHEN a manifest-driven DLL override exists for a game and the user has not opted out THEN `ApplyManifestDllRenames` SHALL CONTINUE TO inject the manifest override, rename the RS file, and set `DllOverrideEnabled` on the card.

3.4 WHEN the user opts out of a manifest-driven override by toggling it OFF THEN the opt-out SHALL CONTINUE TO be persisted in `_manifestDllOverrideOptOuts` and survive refreshes.

3.5 WHEN the cross-exclusion filtering updates the DC dropdown items based on the RS selection THEN the system SHALL CONTINUE TO filter out the RS name from the DC dropdown list and vice versa.

3.6 WHEN the user clicks "Reset Overrides" THEN the system SHALL CONTINUE TO disable both DLL overrides, revert all other per-game settings, and restore default naming (subject to the new conflict checks in 2.5/2.6).

3.7 WHEN the user types a custom name into the RS or DC editable ComboBox and presses Enter THEN the system SHALL CONTINUE TO accept the typed name and persist it as the override filename, provided it passes the new conflict validation from 2.3.

3.8 WHEN RS is installed via `InstallReShadeAsync` with an explicit `filenameOverride` parameter THEN the system SHALL CONTINUE TO use that override name for deployment, cleaning up any old non-standard DLL as it does today.
