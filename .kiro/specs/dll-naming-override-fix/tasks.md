# DLL Naming Override Fix — Tasks

## Task 1: Add name collision validation helper to DllOverrideService

- [x] 1.1 Add `IsNameOccupiedByOtherComponent` method to `DllOverrideService` that checks if a target filename is already in use by the other component (RS or DC) in the same game folder, considering both override config and physical files on disk
- [x] 1.2 Add `GetEffectiveRsName` helper that returns the current RS filename for a game — from override config if active, otherwise `AuxInstallService.RsNormalName`
- [x] 1.3 Add `GetEffectiveDcName` helper that returns the current DC filename for a game — from override config if active, otherwise the default addon name based on bitness

## Task 2: Fix dropdown display to show current filenames (Defects 1.1, 1.2)

- [x] 2.1 In `BuildOverridesPanel`, replace the `Loaded` event approach for `rsNameBox` with a reliable pattern — after constructing the ComboBox, if `existingRsName` is not in the dropdown list, add it as a temporary item and set `SelectedItem`, or use `DispatcherQueue.TryEnqueue` to set `.Text` after layout
- [x] 2.2 Apply the same fix for `dcNameBox` — ensure the existing DC filename from config is displayed as the selected value, not the placeholder text

## Task 3: Validate manual name entry against collisions (Defect 1.3)

- [x] 3.1 In the `dcNameBox.KeyDown` (Enter) handler, before calling `UpdateDllOverrideNames`, validate the typed DC name against the current RS name (from `rsNameBox` selection or `RsNormalName` if RS override is OFF). If collision detected, reject the entry and show a visual indicator (e.g., InfoBar or revert text)
- [x] 3.2 In the `rsNameBox.KeyDown` (Enter) handler, validate the typed RS name against the current DC name. If collision detected, reject the entry
- [x] 3.3 In the `dcNameBox.SelectionChanged` handler, add the same collision check before calling `UpdateDllOverrideNames` — reject if the selected DC name matches the current RS name

## Task 4: Fix toggle-on without selection (Defect 1.8)

- [x] 4.1 In the `dllOverrideToggle.Toggled` ON handler, when `rsText` is empty, revert `dllOverrideToggle.IsOn = false` instead of silently returning, ensuring toggle visual state matches persisted config
- [x] 4.2 Optionally auto-select the current RS filename (e.g., `card.RsInstalledFile` or `RsNormalName`) as the initial value when toggling ON, so the user always has a starting point

## Task 5: Add collision-safe revert to DisableDllOverride (Defects 1.5, 1.6)

- [x] 5.1 In `DllOverrideService.DisableDllOverride`, before renaming RS back to `dxgi.dll`, check if `dxgi.dll` is occupied by another file (DC or foreign). If occupied, keep RS under its current name or fall back to `ReShade64.dll`/`ReShade32.dll` based on bitness. Return a status indicating whether the revert succeeded
- [x] 5.2 In `DllOverrideService.DisableDllOverride`, before renaming DC back to the default addon name, check if that name is occupied. If occupied, keep DC under its current name
- [x] 5.3 Update the UI toggle-off handlers (`dllOverrideToggle.Toggled` OFF and `dcDllToggle.Toggled` OFF) to handle the case where revert was not possible — inform the user via InfoBar or tooltip

## Task 6: Add RS install fallback when target occupied by DC (Defect 1.4)

- [x] 6.1 In `AuxInstallService.InstallReShadeAsync`, after computing `destName`, check if `destName` is occupied by a DC file (by querying the DC install record for the same game/installPath). If occupied, fall back to `ReShade64.dll` or `ReShade32.dll` based on `use32Bit`
- [x] 6.2 Ensure the fallback name is reflected in the returned `AuxInstalledRecord.InstalledAs` and the card's `RsInstalledFile`

## Task 7: Add refresh reconciliation for default naming (Defect 1.7)

- [x] 7.1 In `MainViewModel.GameMatching.cs`, after `ApplyManifestDllRenames`, add a reconciliation pass over cards where `DllOverrideEnabled == false` and no manifest override exists. For each card, verify RS is installed as `RsNormalName` and DC is installed as the default addon name
- [x] 7.2 If files are not under default names, rename them back to defaults using the collision-safe logic from Task 5

## Task 8: Write exploration tests for bug conditions

- [x] 8.1 `DllOverrideCollisionExplorationTests.cs` — PBT: Generate random `(rsName, dcName)` pairs where `rsName == dcName` (case-insensitive) and verify the system rejects the collision. On unfixed code, this should FAIL (collision allowed)
  - Property: _For any_ RS/DC name pair where the names match (case-insensitive), attempting to set DC to the same name as RS SHALL be rejected
  - **Validates: Requirements 2.3**
- [x] 8.2 `DllOverrideRevertSafetyExplorationTests.cs` — PBT: Generate random override states where the default name is occupied by another file, toggle override OFF, and verify the occupied file is not overwritten. On unfixed code, this should FAIL (file overwritten)
  - Property: _For any_ toggle-off operation where the default filename is occupied by another file, the occupied file SHALL NOT be deleted or overwritten
  - **Validates: Requirements 2.5, 2.6**
- [x] 8.3 `RsInstallDcCollisionExplorationTests.cs` — PBT: Generate scenarios where DC is installed as `dxgi.dll` and RS is installed without an override, verify RS falls back to `ReShade64.dll`/`ReShade32.dll`. On unfixed code, this should FAIL (DC overwritten)
  - Property: _For any_ RS install where DC occupies the default RS target name, RS SHALL fall back to `ReShade64.dll` or `ReShade32.dll` instead of overwriting DC
  - **Validates: Requirements 2.4**

## Task 9: Write fix verification and preservation tests

- [x] 9.1 `DllOverrideCollisionPreservationTests.cs` — PBT: Generate random `(rsName, dcName)` pairs where names do NOT collide, verify `EnableDllOverride` and `UpdateDllOverrideNames` produce the same result as unfixed code (file renamed, config persisted)
  - Property: _For any_ valid non-colliding RS/DC name pair, the override system SHALL rename files and persist config identically to the original behavior
  - **Validates: Requirements 3.1, 3.2, 3.5, 3.7**
- [x] 9.2 `DllOverrideToggleConsistencyPropertyTests.cs` — PBT: Generate random toggle sequences (ON/OFF with various name selections) and verify toggle visual state always matches persisted config state
  - Property: _For any_ sequence of toggle operations, the `DllOverrideEnabled` flag on the card SHALL match whether a `DllOverrideConfig` exists for the game
  - **Validates: Requirements 2.8, 3.1**
- [x] 9.3 `RsInstallWithOverridePreservationTests.cs` — PBT: Generate random `filenameOverride` values (non-null, non-colliding with DC) and verify `InstallReShadeAsync` uses the override name identically to unfixed code
  - Property: _For any_ RS install with an explicit `filenameOverride` that doesn't collide with DC, the install SHALL use that override name and produce the same record as the original code
  - **Validates: Requirements 3.8**
