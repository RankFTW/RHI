# Implementation Plan: OptiScaler Integration

## Overview

Incrementally integrate OptiScaler as a fully managed component in RHI. Models are extended first, then the service interface and implementation are built, followed by DLL override extensions, install/uninstall logic, ReShade coexistence, detection of existing installs, UI changes (detail panel, overrides panel, settings hotkey, skeleton, card flyout), integration wiring (DI, startup, Update All, Refresh, Game Report), and documentation updates. Property-based tests validate correctness properties alongside each implementation step.

## Tasks

- [x] 1. Model changes
  - [x] 1.1 Add `OsFileName` to `DllOverrideConfig`
    - Add `public string OsFileName { get; set; } = "";` to `RenoDXCommander/Models/DllOverrideConfig.cs`
    - _Requirements: 9.1, 9.2_

  - [x] 1.2 Add `optiScalerDllOverrides` to `RemoteManifest`
    - Add `[JsonPropertyName("optiScalerDllOverrides")] public Dictionary<string, string>? OptiScalerDllOverrides { get; set; }` to `RenoDXCommander/Models/RemoteManifest.cs`
    - _Requirements: 19.1, 19.2, 19.3_

  - [x] 1.3 Create `GameCardViewModel.OptiScaler.cs` partial file
    - Create `RenoDXCommander/ViewModels/GameCardViewModel.OptiScaler.cs` with observable properties: `OsStatus` (GameStatus), `OsIsInstalling` (bool), `OsProgress` (double), `OsActionMessage` (string), `OsInstalledFile` (string?), `OsInstalledVersion` (string?), `ExcludeFromUpdateAllOs` (bool)
    - Add all computed properties: `OsStatusDot`, `OsActionLabel`, `OsBtnBackground`, `OsBtnForeground`, `OsBtnBorderBrush`, `OsProgressVisibility`, `OsMessageVisibility`, `OsDeleteVisibility`, `OsStatusText`, `OsStatusColor`, `OsShortAction`, `IsOsNotInstalling`, `IsOsInstalled`, `OsInstallEnabled`, `OsIniExists`, `OsRowVisibility`, `CardOsStatusDot`, `CardOsInstallEnabled`
    - Add `NotifyOsStatusDependents()` and `NotifyOsIsInstallingDependents()` methods
    - Add `partial void OnOsStatusChanged`, `OnOsIsInstallingChanged`, `OnOsInstalledVersionChanged`, `OnOsActionMessageChanged` handlers
    - Follow the same pattern as `GameCardViewModel.DisplayCommander.cs`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 21.1, 21.2, 21.3_

  - [x] 1.4 Register OptiScaler properties in `GameCardViewModel.NotifyAll()`
    - In `RenoDXCommander/ViewModels/GameCardViewModel.cs`, add all OptiScaler computed property names to the `NotifyAll()` method
    - _Requirements: 8.6_

  - [x] 1.5 Write property test for `OsInstallEnabled` boolean logic
    - **Property 11: OsInstallEnabled Boolean Logic**
    - Create `RenoDXCommander.Tests/OptiScalerInstallEnabledPropertyTests.cs`
    - For any combination of `Is32Bit` (bool) and `OsIsInstalling` (bool), assert `OsInstallEnabled` equals `!Is32Bit && !OsIsInstalling`
    - **Validates: Requirements 7.6, 8.3, 21.2, 21.3**

  - [x] 1.6 Write property test for `DllOverrideConfig` round-trip with `OsFileName`
    - **Property 10: Tracking Record Serialization Round-Trip** (extended for DllOverrideConfig)
    - Create `RenoDXCommander.Tests/OptiScalerDllOverrideRoundTripPropertyTests.cs`
    - For any valid `DllOverrideConfig` with arbitrary `ReShadeFileName`, `DcFileName`, and `OsFileName` strings, serializing to JSON and deserializing back shall produce an equivalent object
    - **Validates: Requirements 9.1, 9.2**

- [x] 2. Checkpoint — Verify model changes compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Create `IOptiScalerService` interface and `OptiScalerService` implementation
  - [x] 3.1 Create `IOptiScalerService` interface
    - Create `RenoDXCommander/Services/IOptiScalerService.cs` with all methods from the design: `IsStagingReady`, `HasUpdate`, `StagedVersion`, `EnsureStagingAsync()`, `CheckForUpdateAsync()`, `InstallAsync()`, `Uninstall()`, `UpdateAsync()`, `CopyIniToGame()`, `DetectInstallation()`, `IsOptiScalerFile()`, `LoadAllRecords()`, `FindRecord()`, `ClearStaging()`, `GetEffectiveOsDllName()`, `SetHotkey()`, `ApplyHotkeyToAllGames()`, `FirstTimeWarningAcknowledged`
    - Follow the same interface pattern as `IDllOverrideService`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 5.1, 5.2, 5.3, 5.4, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5, 10.1, 10.2, 10.3, 11.3, 11.4_

  - [x] 3.2 Create `OptiScalerService` class with constants and constructor
    - Create `RenoDXCommander/Services/OptiScalerService.cs` with constants (`DefaultDllName`, `IniFileName`, `ReShadeCoexistName`, `AddonType`, `SupportedDllNames`, `CompanionFiles`, `StagingDir`, `VersionFilePath`, `OsIniPath`, `GitHubReleasesApi`, `OptiScalerSignatures`), DI constructor accepting `HttpClient`, `IAuxInstallService`, `IDllOverrideService`
    - Implement `IsStagingReady`, `HasUpdate`, `StagedVersion` properties
    - _Requirements: 1.1, 1.5, 2.2_

  - [x] 3.3 Implement `EnsureStagingAsync` (download and extract)
    - Implement GitHub releases API fetch for latest release, download the 7z archive asset, extract to `StagingDir` using bundled 7z.exe, write version tag to `version.txt`
    - Skip download if staging is already valid and up to date
    - Log failures via `CrashReporter.Log()` and continue gracefully
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 3.4 Implement `CheckForUpdateAsync`
    - Fetch latest release tag from GitHub API, compare with cached version tag in `version.txt`
    - Set `HasUpdate = true` if tags differ
    - Log and handle network failures gracefully
    - _Requirements: 2.1, 2.2_

  - [x] 3.5 Implement `ClearStaging`
    - Delete all contents of the staging folder
    - _Requirements: 1.6_

  - [x] 3.6 Write property test for version tag round-trip
    - **Property 1: Version Tag Round-Trip**
    - Create `RenoDXCommander.Tests/OptiScalerVersionTagRoundTripPropertyTests.cs`
    - For any valid version tag string (non-empty, no path-separator characters), writing to `version.txt` and reading back produces the exact same string
    - **Validates: Requirements 1.3, 6.5**

  - [x] 3.7 Write property test for update detection by tag comparison
    - **Property 2: Update Detection by Tag Comparison**
    - Create `RenoDXCommander.Tests/OptiScalerUpdateDetectionPropertyTests.cs`
    - For any two version tag strings `cached` and `remote`, update detection returns `true` iff `cached` differs from `remote` (case-sensitive)
    - **Validates: Requirements 2.2**

- [x] 4. Implement DLL override extensions
  - [x] 4.1 Add OptiScaler methods to `IDllOverrideService`
    - Add `GetEffectiveOsName(string gameName)` and `GetAvailableOsDllNames(string gameName, bool is32Bit)` method signatures to `RenoDXCommander/Services/IDllOverrideService.cs`
    - _Requirements: 9.1, 9.2, 9.3, 9.6_

  - [x] 4.2 Implement OptiScaler methods in `DllOverrideService`
    - Implement `GetEffectiveOsName()` with priority chain: user override > manifest override > `dxgi.dll` default
    - Implement `GetAvailableOsDllNames()` filtering out RS and DC filenames from `SupportedDllNames`
    - Load `OptiScalerDllOverrides` from manifest during initialization
    - Update `SetDllOverride()` to accept an optional `osFileName` parameter (or add `SetOsDllOverride` method)
    - _Requirements: 9.1, 9.2, 9.3, 9.6, 19.3_

  - [x] 4.3 Write property test for DLL naming resolution priority chain
    - **Property 5: DLL Naming Resolution Priority Chain**
    - Create `RenoDXCommander.Tests/OptiScalerDllNamingPriorityPropertyTests.cs`
    - For any game name, given a user DLL override (string or null), a manifest override (string or null), and default `dxgi.dll`, the resolved name follows: user override if non-empty, else manifest override if non-empty, else `dxgi.dll`
    - **Validates: Requirements 3.4, 9.6, 19.3**

  - [x] 4.4 Write property test for DLL dropdown conflict filtering
    - **Property 12: OptiScaler DLL Dropdown Conflict Filtering**
    - Create `RenoDXCommander.Tests/OptiScalerDllDropdownFilterPropertyTests.cs`
    - For any pair of RS and DC effective filenames drawn from supported DLL names, `GetAvailableOsDllNames` returns a list excluding both RS and DC filenames and including all other supported names
    - **Validates: Requirements 9.3**

- [x] 5. Checkpoint — Verify service interface and DLL overrides compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement install logic
  - [x] 6.1 Implement `InstallAsync` in `OptiScalerService`
    - Implement first-time warning dialog check (`FirstTimeWarningAcknowledged` flag, abort on Cancel)
    - Resolve effective DLL name via `GetEffectiveOsDllName()` (user override > manifest override > dxgi.dll)
    - Copy `OptiScaler.dll` from staging to game folder renamed to effective DLL name
    - Copy all companion files (`fakenvapi.dll`, `dlssg_to_fsr3.dll`, FFX SDK DLLs) to game folder
    - Resolve INI source: user INI from `INIs_Folder` if exists, else default from staging
    - Seed default `OptiScaler.ini` to `INIs_Folder` on first install if not present
    - Copy `OptiScaler.ini` to game folder
    - Enforce `LoadReshade=true` in deployed INI via `EnforceLoadReshade()` helper
    - Create/update `AuxInstalledRecord` with `AddonType = "OptiScaler"`
    - Update card VM properties (`OsStatus`, `OsInstalledFile`, `OsInstalledVersion`)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 6.1, 6.3_

  - [x] 6.2 Implement `EnforceLoadReshade` static helper
    - Read INI lines, find `LoadReshade=` line (case-insensitive), replace with `LoadReshade=true`, or append if missing
    - Write lines back to file
    - _Requirements: 3.7, 3.10, 18.2_

  - [x] 6.3 Write property test for LoadReshade=true enforcement
    - **Property 6: LoadReshade=true Enforcement**
    - Create `RenoDXCommander.Tests/OptiScalerLoadReshadeEnforcementPropertyTests.cs`
    - For any OptiScaler.ini content (with or without an existing `LoadReshade` line, with any value), after `EnforceLoadReshade` the result contains exactly one line matching `LoadReshade=true`
    - **Validates: Requirements 3.7, 3.10, 18.2**

- [x] 7. Implement uninstall logic
  - [x] 7.1 Implement `Uninstall` in `OptiScalerService`
    - Delete the renamed OptiScaler DLL from game folder (using `OsInstalledFile` from tracking record)
    - Delete `OptiScaler.ini` from game folder
    - Delete all companion files from game folder
    - Remove `AuxInstalledRecord` for the game
    - Update card VM (`OsStatus = NotInstalled`)
    - _Requirements: 5.1, 5.2, 5.3, 5.5, 6.4_

  - [x] 7.2 Implement `UpdateAsync` in `OptiScalerService`
    - Replace renamed OptiScaler DLL and all companion files in game folder from staging
    - Preserve existing `OptiScaler.ini` in game folder (do not overwrite)
    - Update tracking record with new version
    - _Requirements: 2.4, 2.5_

  - [x] 7.3 Write property test for INI preservation during update
    - **Property 3: INI Preservation During Update**
    - Create `RenoDXCommander.Tests/OptiScalerIniPreservationPropertyTests.cs`
    - For any OptiScaler.ini content in a game folder, after an update operation the INI content is byte-identical to pre-update content
    - **Validates: Requirements 2.5**

- [x] 8. Implement ReShade coexistence logic
  - [x] 8.1 Add ReShade rename on OptiScaler install
    - In `InstallAsync`, after deploying OptiScaler files: detect if ReShade is installed in game folder (check tracking record or scan for ReShade DLL)
    - If ReShade DLL found, rename it to `ReShade64.dll`
    - Update ReShade `AuxInstalledRecord` (`InstalledAs = ReShade64.dll`)
    - _Requirements: 4.1, 4.2_

  - [x] 8.2 Add ReShade filename restoration on OptiScaler uninstall
    - In `Uninstall`, after removing OptiScaler files: if `ReShade64.dll` exists in game folder, resolve correct ReShade filename using priority chain (user DLL override > manifest override > auto-detected API > dxgi.dll default)
    - Rename `ReShade64.dll` back to resolved filename
    - Update ReShade `AuxInstalledRecord` (`InstalledAs = resolved filename`)
    - _Requirements: 5.4_

  - [x] 8.3 Modify ReShade install to deploy as `ReShade64.dll` when OptiScaler is present
    - In the existing ReShade install flow (likely in `AuxInstallService` or `InstallEventHandler`), check if OptiScaler is installed for the game
    - If OptiScaler is installed, deploy ReShade DLL as `ReShade64.dll` instead of the normal DLL name
    - This applies to fresh installs, reinstalls, and Update All updates of ReShade
    - _Requirements: 4.5, 4.6, 4.7_

  - [x] 8.4 Disable ReShade DLL naming override dropdown when OptiScaler is installed
    - In `DetailPanelBuilder.Overrides.cs`, when building the ReShade DLL name dropdown, check if OptiScaler is installed for the game
    - If installed, disable the dropdown and add a tooltip explaining OptiScaler controls the ReShade DLL name
    - _Requirements: 4.3, 4.4_

  - [x] 8.5 Write property test for ReShade rename on OptiScaler install
    - **Property 7: ReShade Rename on OptiScaler Install**
    - Create `RenoDXCommander.Tests/OptiScalerReShadeRenamePropertyTests.cs`
    - For any ReShade DLL filename from supported names, when OptiScaler is installed the ReShade file is renamed to `ReShade64.dll`
    - **Validates: Requirements 4.1**

  - [x] 8.6 Write property test for ReShade deploys as ReShade64.dll when OptiScaler present
    - **Property 8: ReShade Deploys as ReShade64.dll When OptiScaler Present**
    - Create `RenoDXCommander.Tests/OptiScalerReShadeCoexistPropertyTests.cs`
    - For any game with OptiScaler installed, a ReShade install/update deploys as `ReShade64.dll` regardless of overrides
    - **Validates: Requirements 4.5, 4.6, 4.7**

  - [x] 8.7 Write property test for ReShade filename restoration priority on uninstall
    - **Property 9: ReShade Filename Restoration Priority on Uninstall**
    - Create `RenoDXCommander.Tests/OptiScalerReShadeRestorePriorityPropertyTests.cs`
    - For any combination of user DLL override, manifest override, and detected graphics API, the restored ReShade filename follows: user override > manifest override > API-based default > `dxgi.dll`
    - **Validates: Requirements 5.4**

- [x] 9. Checkpoint — Verify install, uninstall, and ReShade coexistence compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Implement detection of existing OptiScaler installations
  - [x] 10.1 Implement `IsOptiScalerFile` binary signature detection
    - Read first ~2 MB of a DLL file, scan for `OptiScalerSignatures` byte patterns
    - Return `true` if any signature matches
    - Log errors via `CrashReporter.Log()`
    - _Requirements: 10.1_

  - [x] 10.2 Implement `DetectInstallation`
    - Scan DLLs in game folder for OptiScaler binary signatures via `IsOptiScalerFile()`
    - Check for `OptiScaler.ini` presence as secondary marker
    - Return detected DLL filename or null
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 10.3 Integrate OptiScaler detection into foreign DLL protection
    - In the foreign DLL protection system, call `IsOptiScalerFile()` to whitelist OptiScaler DLLs so they are not flagged as foreign
    - _Requirements: 10.4_

  - [x] 10.4 Implement `LoadAllRecords` and `FindRecord`
    - Load all persisted `AuxInstalledRecord` entries with `AddonType = "OptiScaler"` from the existing `aux_installed.json` database
    - Implement `FindRecord` to look up a specific game's OptiScaler record
    - _Requirements: 6.1, 6.2, 6.5_

  - [x] 10.5 Write property test for tracking record serialization round-trip
    - **Property 10: Tracking Record Serialization Round-Trip**
    - Create `RenoDXCommander.Tests/OptiScalerRecordRoundTripPropertyTests.cs`
    - For any valid `AuxInstalledRecord` with `AddonType = "OptiScaler"`, arbitrary `GameName`, `InstallPath`, `InstalledAs` from supported DLL names, and valid `InstalledAt` timestamp, serialize to JSON and deserialize back produces an equivalent record
    - **Validates: Requirements 6.1, 6.5**

- [x] 11. Checkpoint — Verify detection and tracking compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. UI changes — Detail panel OptiScaler component row
  - [x] 12.1 Add "── Optional ──" separator and OptiScaler component row to detail panel
    - In `RenoDXCommander/DetailPanelBuilder.Components.cs`, in `UpdateDetailComponentRows()`, add an "── Optional ──" separator line after the Display Commander / ReLimiter rows and before the "Choose one from below" separator
    - Add an OptiScaler component row below the separator containing: OptiScaler label, version/status indicator, install button, INI copy button, uninstall button
    - Follow the same `BuildComponentRow()` pattern used by DC and UL rows
    - Wire install button to `InstallOsButton_Click`, INI copy to `CopyOsIniButton_Click`, uninstall to `UninstallOsButton_Click`
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 21.1_

  - [x] 12.2 Add version number click handler to open OptiScaler GitHub wiki
    - When OptiScaler is installed and the version number is displayed, make it clickable to open the OptiScaler GitHub wiki in the default browser
    - _Requirements: 7.5_

  - [x] 12.3 Implement 32-bit game greyed-out state for OptiScaler row
    - When `Is32Bit` is true, set OptiScaler row `Opacity = 0.35`, `IsHitTestVisible = false`
    - Apply `TextDecorations.Strikethrough` to label and status text
    - _Requirements: 7.6, 7.7, 21.3_

  - [x] 12.4 Add OptiScaler property change handling in `DetailCard_PropertyChanged`
    - In `DetailPanelBuilder.Components.cs`, handle `OsStatus`, `OsIsInstalling`, `OsProgress`, `OsActionMessage`, `OsInstalledVersion` property changes to update the OptiScaler component row UI
    - _Requirements: 8.6_

  - [x] 12.5 Implement INI copy button behaviour
    - When clicked, call `OptiScalerService.CopyIniToGame(card)` which copies `OptiScaler.ini` from `INIs_Folder` to game folder with `LoadReshade=true` enforcement
    - When no `OptiScaler.ini` exists in `INIs_Folder`, dim the button with reduced opacity and disable it (bind to `OsIniExists`)
    - _Requirements: 18.1, 18.2, 18.3_

- [x] 13. UI changes — Overrides panel
  - [x] 13.1 Add OptiScaler DLL name dropdown to overrides panel
    - In `RenoDXCommander/DetailPanelBuilder.Overrides.cs`, add a "Select OptiScaler DLL name" dropdown below the DC dropdown
    - Populate with `GetAvailableOsDllNames()` (filtered to exclude RS and DC filenames)
    - On selection change, call `DllOverrideService.SetOsDllOverride()` and if OptiScaler is installed, rename the DLL in the game folder
    - _Requirements: 9.1, 9.2, 9.3, 9.4_

  - [x] 13.2 Add OptiScaler update exclusion toggle to overrides panel
    - Add an "OS" checkbox toggle in the Update Exclusions row alongside existing RS, RDX, UL, DC toggles
    - Bind to `card.ExcludeFromUpdateAllOs`
    - _Requirements: 17.3_

  - [x] 13.3 Write property test for update exclusion filtering
    - **Property 4: Update Exclusion Filtering**
    - Create `RenoDXCommander.Tests/OptiScalerUpdateExclusionPropertyTests.cs`
    - For any set of games with random `ExcludeFromUpdateAllOs` boolean flags, the Update All operation updates exactly the games where `ExcludeFromUpdateAllOs` is `false` and OptiScaler is installed
    - **Validates: Requirements 2.6**

- [x] 14. UI changes — Settings page hotkey
  - [x] 14.1 Add OptiScaler hotkey section to Settings page
    - Add "OptiScaler overlay toggle key" dropdown section alongside existing ReShade and ReLimiter hotkey sections
    - Default to `Insert` key
    - On change, call `OptiScalerService.SetHotkey()` to write `ShortcutKey=` to `OptiScaler.ini` in `INIs_Folder`
    - _Requirements: 11.1, 11.2, 11.3_

  - [x] 14.2 Wire "Apply to All Games" for OptiScaler hotkey
    - When "Apply to All Games" is clicked, call `OptiScalerService.ApplyHotkeyToAllGames()` to update `ShortcutKey=` in all game folders where OptiScaler is installed
    - _Requirements: 11.4_

  - [x] 14.3 Implement `SetHotkey` and `ApplyHotkeyToAllGames` in `OptiScalerService`
    - `SetHotkey`: Write `ShortcutKey=<value>` to `OptiScaler.ini` in `INIs_Folder`
    - `ApplyHotkeyToAllGames`: Iterate all games with OptiScaler installed, update `ShortcutKey=` in each game folder's `OptiScaler.ini`
    - _Requirements: 11.3, 11.4_

  - [x] 14.4 Write property test for hotkey INI round-trip
    - **Property 13: Hotkey INI Round-Trip**
    - Create `RenoDXCommander.Tests/OptiScalerHotkeyRoundTripPropertyTests.cs`
    - For any valid hotkey string (non-empty, printable ASCII), writing `ShortcutKey=<value>` and reading back produces the same value
    - **Validates: Requirements 11.3**

- [x] 15. UI changes — Skeleton loading screen
  - [x] 15.1 Add OptiScaler placeholder row to skeleton loading screen
    - In the skeleton loading screen builder, add a placeholder row for OptiScaler in the correct position (between RenoDX/DC rows and the "Choose one from below" separator)
    - Follow the same shimmer/placeholder pattern as existing component rows
    - _Requirements: 15.1_

- [x] 16. UI changes — Grid view card flyout
  - [x] 16.1 Add OptiScaler component row to card flyout
    - In `RenoDXCommander/CardBuilder.Flyout.cs`, in `BuildInstallFlyoutContent()`, add an OptiScaler row using `BuildComponentRow()` with install, INI copy, and uninstall actions
    - Position after existing component rows, before the separator
    - _Requirements: 16.1_

  - [x] 16.2 Implement 32-bit greyed-out state in card flyout
    - When `Is32Bit` is true, grey out and disable the OptiScaler row in the flyout
    - _Requirements: 16.2_

- [x] 17. Checkpoint — Verify all UI changes compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 18. Integration — DI registration and startup wiring
  - [x] 18.1 Register `IOptiScalerService` / `OptiScalerService` in DI
    - In `RenoDXCommander/App.xaml.cs`, register `IOptiScalerService` → `OptiScalerService` as a singleton in the DI container, following the same pattern as existing services
    - _Requirements: 1.1_

  - [x] 18.2 Wire OptiScaler into startup sequence
    - Call `OptiScalerService.EnsureStagingAsync()` during startup (alongside existing staging calls)
    - Call `OptiScalerService.CheckForUpdateAsync()` during startup
    - _Requirements: 1.1, 2.1_

  - [x] 18.3 Wire OptiScaler detection into card building
    - During the `BuildCards` loop, call `OptiScalerService.DetectInstallation()` for each game
    - If detected, create tracking record and set card VM properties (`OsStatus`, `OsInstalledFile`, `OsInstalledVersion`)
    - Also load existing tracking records via `LoadAllRecords()` and apply to cards
    - _Requirements: 10.3, 6.2_

- [x] 19. Integration — Install/Uninstall event handlers
  - [x] 19.1 Add `InstallOsButton_Click` and `UninstallOsButton_Click` to `InstallEventHandler`
    - In `RenoDXCommander/InstallEventHandler.cs`, add `InstallOsButton_Click` handler calling `OptiScalerService.InstallAsync(card)` with progress reporting
    - Add `UninstallOsButton_Click` handler calling `OptiScalerService.Uninstall(card)`
    - Follow the same pattern as `InstallDcButton_Click` / `UninstallDcButton_Click`
    - _Requirements: 3.1, 5.1_

  - [x] 19.2 Add `CopyOsIniButton_Click` to `InstallEventHandler`
    - Add handler calling `OptiScalerService.CopyIniToGame(card)` with `LoadReshade=true` enforcement
    - _Requirements: 18.1, 18.2_

- [x] 20. Integration — Update All
  - [x] 20.1 Include OptiScaler in Update All sweep
    - In the `UpdateOrchestrationService` (or equivalent Update All logic), add OptiScaler to the update sweep
    - For each game with OptiScaler installed and `ExcludeFromUpdateAllOs == false`, call `OptiScalerService.UpdateAsync(card)`
    - Follow the same update rules: replace DLL and companions, preserve INI
    - _Requirements: 17.1, 17.2, 2.3, 2.4, 2.5, 2.6_

- [x] 21. Integration — Refresh
  - [x] 21.1 Include OptiScaler in Refresh action
    - In the Refresh handler, call `OptiScalerService.DetectInstallation()` for each game to re-detect OptiScaler status
    - If OptiScaler was manually deleted, update tracking record and card VM
    - If OptiScaler was manually deleted and `ReShade64.dll` exists, rename it back to the correct ReShade filename
    - _Requirements: 12.1, 12.2, 12.3_

- [x] 22. Integration — Game Report
  - [x] 22.1 Include OptiScaler status in game report
    - In `RenoDXCommander/GameReportEncoder.cs`, in `BuildReport()`, add OptiScaler status to the components section: installed/not installed, version, and DLL filename used
    - _Requirements: 13.1_

- [x] 23. Integration — Bottom warning text
  - [x] 23.1 Update single-player warning text to mention OptiScaler
    - In the bottom warning text of the application, add OptiScaler alongside ReShade in the single-player recommendation
    - _Requirements: 14.1_

- [x] 24. Checkpoint — Verify all integration wiring compiles
  - Ensure all tests pass, ask the user if questions arise.

- [x] 25. Documentation updates
  - [x] 25.1 Update `README.md` with OptiScaler
    - Add OptiScaler to feature highlights and third-party components table
    - _Requirements: 20.1_

  - [x] 25.2 Update `README_NexusMods.bbcode` with OptiScaler
    - Add OptiScaler to features list and third-party components section
    - _Requirements: 20.2_

  - [x] 25.3 Update `docs/DETAILED_GUIDE.md` with OptiScaler section
    - Add comprehensive OptiScaler section covering installation, configuration, ReShade coexistence, DLL naming, and hotkey setup
    - _Requirements: 20.3_

- [x] 26. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- The design uses C# throughout, matching the existing codebase
- Services follow the existing interface + implementation + DI pattern (same as `DllOverrideService`, `AuxInstallService`)
- OptiScaler reuses the existing `AuxInstalledRecord` model with `AddonType = "OptiScaler"`
- The `GameCardViewModel.OptiScaler.cs` partial file follows the same pattern as `GameCardViewModel.DisplayCommander.cs`
- FsCheck with xUnit is used for property-based tests, consistent with the existing test suite
