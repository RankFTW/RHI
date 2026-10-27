# DLL Naming Override Fix — Bugfix Design

## Overview

The DLL naming override system has eight interrelated defects spanning dropdown display, name collision prevention, toggle-off revert safety, refresh-time reconciliation, and toggle-on consistency. The fix targets three files: `DetailPanelBuilder.Overrides.cs` (UI layer), `DllOverrideService.cs` (CRUD and file rename logic), and `AuxInstallService.Install.cs` (RS deployment). The strategy is to harden each layer independently — UI displays correct state, service layer enforces collision invariants, and install logic respects occupied filenames.

## Glossary

- **Bug_Condition (C)**: Any operation on the DLL override system that produces an inconsistent, lossy, or collision-prone state — including dropdown display errors, unguarded name collisions, unsafe reverts, missing refresh reconciliation, and toggle-on without selection.
- **Property (P)**: The desired behavior: dropdowns reflect persisted config, RS and DC never share a filename in the same game folder, toggle-off reverts are collision-safe, refresh reconciles naming, and toggle state always matches persisted config.
- **Preservation**: Existing behaviors that must remain unchanged — valid override selection, manifest-driven overrides, opt-out persistence, cross-exclusion dropdown filtering, Reset Overrides, typed name acceptance, and RS install with explicit `filenameOverride`.
- **DllOverrideService**: The service in `Services/DllOverrideService.cs` that owns override CRUD, file renames on enable/disable/update, and the backing `_dllOverrides` dictionary.
- **DetailPanelBuilder.Overrides**: The UI builder in `DetailPanelBuilder.Overrides.cs` that constructs toggle switches, editable ComboBoxes, and wires event handlers for the overrides panel.
- **AuxInstallService.Install**: The RS deployment logic in `Services/AuxInstallService.Install.cs` that copies staged ReShade DLLs to game folders with optional `filenameOverride`.
- **DllOverrideConfig**: The data model (`Models/DllOverrideConfig.cs`) holding `ReShadeFileName` and `DcFileName` per game.
- **RsNormalName**: The default RS install filename `dxgi.dll` (constant in `AuxInstallService`).
- **Cross-exclusion filtering**: The dropdown filtering logic that removes the other component's current filename from each ComboBox's item list.

## Bug Details

### Bug Condition

The bug manifests across six categories of operations on the DLL override system. The common thread is that the system does not enforce naming invariants at every mutation point — UI display, manual text entry, toggle-off revert, RS install, and refresh.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type DllOverrideOperation
  OUTPUT: boolean

  // Category 1: Dropdown display (Defects 1.1, 1.2)
  IF input.type == "OpenOverridesPanel"
     AND overrideConfigExists(input.gameName)
     AND dropdownDisplaysPlaceholder(input.component)
    RETURN true

  // Category 2: Manual name collision (Defect 1.3)
  IF input.type == "ManualNameEntry"
     AND input.enteredName == otherComponentCurrentName(input.gameName, input.component)
    RETURN true

  // Category 3: RS install overwrites DC (Defect 1.4)
  IF input.type == "InstallReShade"
     AND NOT hasExplicitRsOverride(input.gameName)
     AND dcInstalledAs(input.gameName) == RsNormalName
    RETURN true

  // Category 4: Toggle-off revert collision (Defects 1.5, 1.6)
  IF input.type == "ToggleOffOverride"
     AND defaultNameIsOccupied(input.gameName, input.component)
    RETURN true

  // Category 5: Refresh doesn't reconcile (Defect 1.7)
  IF input.type == "Refresh"
     AND bothOverridesOff(input.gameName)
     AND filesNotUnderDefaultNames(input.gameName)
    RETURN true

  // Category 6: Toggle-on without selection (Defect 1.8)
  IF input.type == "ToggleOnOverride"
     AND noNameSelected(input.component)
     AND toggleVisuallyOn(input.component)
    RETURN true

  RETURN false
END FUNCTION
```

### Examples

- **Defect 1.1/1.2**: User has RS override set to `d3d11.dll`. Opens overrides panel → RS dropdown shows "Select ReShade DLL name" instead of `d3d11.dll`. Same for DC dropdown.
- **Defect 1.3**: RS is `dxgi.dll` (default, override OFF). User types `dxgi.dll` into DC ComboBox and presses Enter → both RS and DC are now `dxgi.dll` on disk.
- **Defect 1.4**: DC is installed as `dxgi.dll` via override. User installs RS without an RS override → RS deploys as `dxgi.dll`, overwriting DC.
- **Defect 1.5**: RS override is `d3d11.dll`. User toggles RS override OFF → system renames to `dxgi.dll` without checking if `dxgi.dll` is already occupied by a foreign DLL or DC.
- **Defect 1.6**: DC override is `winmm.dll`. User toggles DC override OFF → system renames to `zzz_display_commander_lite.addon64` without checking if that name is already occupied.
- **Defect 1.7**: Both overrides OFF. Files were manually renamed externally. User presses Refresh → no reconciliation occurs, files stay under wrong names.
- **Defect 1.8**: User toggles RS override ON but doesn't select a name → toggle is visually ON, but no config is persisted. Subsequent DC toggle reads stale state.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Selecting a valid RS override name from the dropdown that doesn't conflict with DC continues to rename the RS file and persist the config (Req 3.1)
- Selecting a valid DC override name from the dropdown that doesn't conflict with RS continues to rename the DC file and persist the config, including the foreign DLL conflict check dialog (Req 3.2)
- Manifest-driven DLL overrides continue to be injected by `ApplyManifestDllRenames` for games without user opt-out (Req 3.3)
- Manifest override opt-outs continue to be persisted in `_manifestDllOverrideOptOuts` (Req 3.4)
- Cross-exclusion dropdown filtering continues to remove the other component's name from each dropdown list (Req 3.5)
- "Reset Overrides" continues to disable both DLL overrides and revert settings (Req 3.6)
- Typed custom names in editable ComboBoxes continue to be accepted on Enter (Req 3.7)
- `InstallReShadeAsync` with explicit `filenameOverride` continues to use that name (Req 3.8)

**Scope:**
All operations that do NOT involve the six bug condition categories should be completely unaffected. This includes:
- All non-DLL-override settings (bitness, API, shader mode, addon mode, wiki mapping)
- Game card display, status badges, install/uninstall flows for non-override scenarios
- Update checking and Update All behavior

## Hypothesized Root Cause

Based on the bug description and code analysis, the most likely issues are:

1. **ComboBox display (Defects 1.1, 1.2)**: The `rsNameBox` and `dcNameBox` set `SelectedItem` when the existing name matches a dropdown item, or use a `Loaded` event to set `.Text` for custom names. The `Loaded` event fires inconsistently in WinUI 3 — the deferred `Text` assignment may be overwritten by the ComboBox's internal state reset. The placeholder remains visible because neither `SelectedItem` nor `Text` is reliably set before the control renders.

2. **Manual name collision (Defect 1.3)**: `UpdateDcDropdownItems()` filters the dropdown *list* but the editable ComboBox allows free-text entry. The `dcNameBox.KeyDown` handler (Enter key) and `rsNameBox.KeyDown` handler do not validate the typed name against the other component's current filename before calling `UpdateDllOverrideNames`.

3. **RS install overwrites DC (Defect 1.4)**: `InstallReShadeAsync` computes `destName` as `filenameOverride ?? RsNormalName`. When no RS override exists but DC is installed as `dxgi.dll`, the method blindly copies to `dxgi.dll`, overwriting DC. There is no check for DC occupancy at the target path.

4. **Toggle-off revert collision (Defects 1.5, 1.6)**: The `dllOverrideToggle.Toggled` handler (OFF branch) renames RS back to `dxgi.dll` with `if (File.Exists(newPath)) File.Delete(newPath); File.Move(oldPath, newPath)` — it deletes whatever is at the default path without checking if it belongs to another component. Same pattern in `dcDllToggle.Toggled` OFF branch and in `DllOverrideService.DisableDllOverride`.

5. **Refresh doesn't reconcile (Defect 1.7)**: `ApplyManifestDllRenames` only processes games with manifest entries. There is no corresponding pass that checks non-manifest games to confirm RS/DC are under their expected default names.

6. **Toggle-on without selection (Defect 1.8)**: The `dllOverrideToggle.Toggled` ON handler returns early when `rsText` is empty, leaving the toggle visually ON but no `EnableDllOverride` call made. The toggle's `IsOn` property is not reverted to `false`.

## Correctness Properties

Property 1: Bug Condition — Dropdown Display Reflects Persisted Config

_For any_ game where a DLL override config exists with a non-empty RS or DC filename, opening the overrides panel SHALL display that filename as the selected/displayed value in the corresponding ComboBox, not the placeholder text.

**Validates: Requirements 2.1, 2.2**

Property 2: Bug Condition — Name Collision Prevention for Manual Entry

_For any_ manually typed name entered into the RS or DC editable ComboBox, the system SHALL reject the name if it matches the other component's current filename (whether that component's override is ON or OFF), preventing RS and DC from sharing a filename in the same game folder.

**Validates: Requirements 2.3**

Property 3: Bug Condition — RS Install Fallback When Target Occupied by DC

_For any_ RS install where no explicit RS override exists and the default target name (`dxgi.dll`) is already occupied by a DC file, `InstallReShadeAsync` SHALL fall back to `ReShade64.dll` or `ReShade32.dll` (based on bitness) instead of overwriting DC, and the install record SHALL reflect the fallback name.

**Validates: Requirements 2.4**

Property 4: Bug Condition — Toggle-Off Revert Collision Safety

_For any_ toggle-off operation on RS or DC override, the system SHALL check whether the default name is already occupied before renaming. If occupied, the file SHALL remain under its current name (or fall back to a safe alternative), and the user SHALL be informed.

**Validates: Requirements 2.5, 2.6**

Property 5: Bug Condition — Refresh Reconciles Default Naming

_For any_ refresh when both overrides are OFF, the system SHALL confirm RS and DC are deployed under their default names. If files are not under default names, the system SHALL redeploy them under the correct defaults, respecting collision checks.

**Validates: Requirements 2.7**

Property 6: Bug Condition — Toggle-On Consistency

_For any_ toggle-on operation where no name is selected, the system SHALL either revert the toggle to OFF or auto-select the current filename, ensuring toggle visual state always matches persisted config.

**Validates: Requirements 2.8**

Property 7: Preservation — Valid Override Operations Unchanged

_For any_ valid override selection (RS or DC name that doesn't conflict with the other component), the system SHALL produce the same result as the original code — renaming the file on disk, persisting the config, and updating the card state.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `RenoDXCommander/DetailPanelBuilder.Overrides.cs`

**Functions**: `BuildOverridesPanel` (inline event handlers)

**Specific Changes**:

1. **Fix dropdown display (Defects 1.1, 1.2)**: Replace the `Loaded` event approach with a more reliable pattern. After constructing the ComboBox, use `DispatcherQueue.TryEnqueue` to set `.Text` on the next UI tick, or set `SelectedItem` to a programmatically-added item. Ensure the placeholder is cleared when a value is set.

2. **Validate manual entry against collision (Defect 1.3)**: In the `dcNameBox.KeyDown` (Enter) handler, before calling `UpdateDllOverrideNames`, check if the typed DC name matches the current RS name (from `rsNameBox` or `RsNormalName` if RS override is OFF). Reject with a visual indicator or revert. Apply the same check in `rsNameBox.KeyDown` for the reverse direction.

3. **Toggle-on without selection (Defect 1.8)**: In the `dllOverrideToggle.Toggled` ON handler, when `rsText` is empty, revert `dllOverrideToggle.IsOn = false` instead of silently returning. This keeps toggle state consistent with persisted config.

---

**File**: `RenoDXCommander/Services/DllOverrideService.cs`

**Functions**: `DisableDllOverride`, `EnableDllOverride`

**Specific Changes**:

4. **Toggle-off revert collision safety (Defects 1.5, 1.6)**: In `DisableDllOverride`, before renaming RS back to `dxgi.dll`, check if `dxgi.dll` is already occupied by a non-RS file (DC or foreign). If occupied, keep RS under its current custom name or fall back to `ReShade64.dll`/`ReShade32.dll`. Same for DC: before renaming back to the default addon name, check if that name is occupied. Return a result indicating whether the revert succeeded so the UI can inform the user.

5. **Add a name collision check helper**: Add a method `IsNameOccupiedByOtherComponent(string gameName, string targetName, string component)` that checks if the target filename is in use by the other component (RS or DC) in the same game folder.

---

**File**: `RenoDXCommander/Services/AuxInstallService.Install.cs`

**Function**: `InstallReShadeAsync`

**Specific Changes**:

6. **RS install fallback (Defect 1.4)**: After computing `destName`, check if `destName` is occupied by a DC file in the game folder (by checking the DC install record or scanning for known DC filenames). If occupied, fall back to `ReShade64.dll` or `ReShade32.dll` based on the `use32Bit` parameter.

---

**File**: `RenoDXCommander/ViewModels/MainViewModel.GameMatching.cs`

**Function**: `ApplyManifestDllRenames` (extend scope)

**Specific Changes**:

7. **Refresh reconciliation (Defect 1.7)**: After the existing `ApplyManifestDllRenames` pass, add a second pass over all cards where `DllOverrideEnabled == false` and no manifest override exists. For each such card, verify that RS is installed as `RsNormalName` and DC is installed as the default addon name. If not, rename them back to defaults (respecting collision checks from change #4).

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bugs on unfixed code, then verify the fixes work correctly and preserve existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bugs BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write unit tests that exercise each defect scenario against the unfixed code. These tests should fail on the current codebase, confirming the bugs exist.

**Test Cases**:
1. **Dropdown Display Test**: Construct a `DllOverrideConfig` with `ReShadeFileName = "d3d11.dll"`, build the overrides panel, and assert the ComboBox displays `d3d11.dll` (will fail on unfixed code — placeholder shown instead)
2. **Manual Name Collision Test**: Set RS to `dxgi.dll` (default), type `dxgi.dll` into DC ComboBox, press Enter, and assert the name is rejected (will fail on unfixed code — collision allowed)
3. **RS Install Overwrite Test**: Install DC as `dxgi.dll`, then call `InstallReShadeAsync` without `filenameOverride`, and assert RS does not overwrite DC (will fail on unfixed code — DC overwritten)
4. **Toggle-Off Revert Collision Test**: Set RS override to `d3d11.dll`, place a foreign `dxgi.dll` in the game folder, toggle RS override OFF, and assert `dxgi.dll` is not overwritten (will fail on unfixed code — foreign DLL deleted)
5. **Toggle-On No Selection Test**: Toggle RS override ON without selecting a name, and assert the toggle reverts to OFF (will fail on unfixed code — toggle stays ON with no config)

**Expected Counterexamples**:
- ComboBox `.Text` or `.SelectedItem` is empty/placeholder when config has a value
- `UpdateDllOverrideNames` called with colliding RS/DC names
- `InstallReShadeAsync` overwrites DC file at `dxgi.dll`
- `DisableDllOverride` deletes foreign file at default path

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed functions produce the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result := fixedSystem(input)
  ASSERT expectedBehavior(result)
  // Dropdown shows correct filename
  // Manual collision is rejected
  // RS install falls back to ReShade64/32.dll
  // Toggle-off revert is collision-safe
  // Refresh reconciles naming
  // Toggle-on without selection reverts toggle
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed functions produce the same result as the original functions.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT originalSystem(input) == fixedSystem(input)
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain (random game names, filenames, bitness, override states)
- It catches edge cases that manual unit tests might miss (e.g., case-insensitive filename comparisons, empty strings, whitespace)
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Observe behavior on UNFIXED code first for valid override operations, then write property-based tests capturing that behavior.

**Test Cases**:
1. **Valid RS Override Preservation**: For any valid RS name not conflicting with DC, verify `EnableDllOverride` renames the file and persists config identically to unfixed code
2. **Valid DC Override Preservation**: For any valid DC name not conflicting with RS, verify `EnableDllOverride` renames the file and persists config identically
3. **Manifest Override Preservation**: For any game with a manifest DLL override and no user opt-out, verify `ApplyManifestDllRenames` produces the same result
4. **RS Install With Override Preservation**: For any `InstallReShadeAsync` call with an explicit `filenameOverride`, verify the install uses that name identically

### Unit Tests

- Test `DllOverrideService.DisableDllOverride` with occupied default name — assert file not overwritten
- Test `DllOverrideService.DisableDllOverride` with free default name — assert normal revert
- Test `InstallReShadeAsync` with DC occupying `dxgi.dll` — assert fallback to `ReShade64.dll`
- Test `InstallReShadeAsync` with free `dxgi.dll` — assert normal install
- Test name collision validation rejects matching names
- Test name collision validation accepts non-matching names
- Test toggle-on revert when no name selected

### Property-Based Tests

- Generate random `(rsName, dcName)` pairs and verify the collision check correctly identifies conflicts (same name, case-insensitive) and allows non-conflicts
- Generate random game states (RS installed as X, DC installed as Y, override ON/OFF) and verify `DisableDllOverride` never overwrites a file belonging to the other component
- Generate random `filenameOverride` values and verify `InstallReShadeAsync` only falls back when the target is occupied by DC

### Integration Tests

- Test full flow: enable RS override → select name → enable DC override → select name → toggle RS off → verify DC untouched
- Test full flow: install DC as `dxgi.dll` → install RS without override → verify RS falls back → refresh → verify naming stable
- Test full flow: enable RS override → don't select name → verify toggle reverts → select name → verify toggle stays on
