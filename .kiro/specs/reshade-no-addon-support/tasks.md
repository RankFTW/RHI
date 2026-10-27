# Implementation Plan: ReShade Without Addon Support

## Overview

Implement a per-game toggle that switches individual games from addon-enabled ReShade to standard (non-addon) ReShade. The implementation adds a new download/staging service, modifies the install pipeline, persists the toggle per-game, and updates UI computed properties. All code is C# targeting .NET with WinUI 3, CommunityToolkit.Mvvm, and xUnit + FsCheck for testing.

## Tasks

- [x] 1. Create INormalReShadeUpdateService interface and NormalReShadeUpdateService
  - [x] 1.1 Create `INormalReShadeUpdateService` interface in `RenoDXCommander/Services/`
    - Define `CheckLatestVersionAsync`, `EnsureLatestAsync`, and static `GetStagedVersion` matching the design
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - [x] 1.2 Create `NormalReShadeUpdateService` implementing `INormalReShadeUpdateService`
    - Mirror `ReShadeUpdateService` structure with regex `/downloads/ReShade_Setup_([\d.]+)\.exe` (no `_Addon` suffix)
    - Use staging dir `%LocalAppData%\RHI\reshade-normal\`
    - Exe pattern `ReShade_Setup_{ver}.exe`, cleanup glob excludes `*_Addon.exe`
    - Constructor takes `HttpClient` and `ISevenZipExtractor`
    - Include PE header validation (MZ check) and minimum size check (500KB)
    - Write version to `reshade_version.txt` in Normal_Staging
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 7.1, 7.2, 7.3_
  - [x] 1.3 Write property test: Normal ReShade version regex extraction
    - **Property 4: Normal ReShade version regex extraction**
    - Test file: `RenoDXCommander.Tests/NormalReShadeVersionRegexPropertyTests.cs`
    - For any valid version string embedded in HTML as `/downloads/ReShade_Setup_{version}.exe`, the regex extracts the correct version and URL; must NOT match `_Addon` URLs
    - **Validates: Requirements 5.1**
  - [x] 1.4 Write property test: PE header and size validation
    - **Property 5: PE header and size validation**
    - Test file: `RenoDXCommander.Tests/NormalReShadeValidationPropertyTests.cs`
    - For any byte array ≥500,000 starting with MZ, validation passes; otherwise fails
    - **Validates: Requirements 5.3, 5.4**
  - [x] 1.5 Write property test: Version file round-trip
    - **Property 6: Version file round-trip**
    - Test file: `RenoDXCommander.Tests/NormalReShadeVersionFileRoundTripPropertyTests.cs`
    - For any non-empty version string, writing to `reshade_version.txt` and reading back via `GetStagedVersion()` produces the same trimmed string
    - **Validates: Requirements 5.5**

- [x] 2. Extend AuxInstallService with Normal ReShade staging and install support
  - [x] 2.1 Add constants and staging paths to `AuxInstallService`
    - Add `TypeReShadeNormal = "ReShadeNormal"`, `RsNormalStagingDir`, `RsNormalStagedPath64`, `RsNormalStagedPath32`
    - _Requirements: 6.1, 6.2, 6.3_
  - [x] 2.2 Add `useNormalReShade` parameter to `InstallReShadeAsync`
    - Update `IAuxInstallService` interface signature with `bool useNormalReShade = false`
    - When true: copy from `RsNormalStagedPath64/32`, save record with `AddonType = TypeReShadeNormal`
    - All other logic (DC occupancy, foreign DLL backup, INI merge, shader deploy) unchanged
    - _Requirements: 6.4, 6.5, 8.1, 8.2, 8.3, 9.1, 10.1, 10.2_
  - [x] 2.3 Extend `CheckReShadeUpdateLocal` for `TypeReShadeNormal`
    - When record has `AddonType == TypeReShadeNormal`, compare against Normal_Staging DLLs
    - _Requirements: 9.2_
  - [x] 2.4 Write property test: Staging path and AddonType selection
    - **Property 7: Staging path and AddonType selection**
    - Test file: `RenoDXCommander.Tests/NormalReShadeStagingPathPropertyTests.cs`
    - For any combination of `useNormalReShade` and `is32Bit`, verify correct staging dir and AddonType
    - **Validates: Requirements 6.4, 6.5, 9.1**
  - [x] 2.5 Write property test: Update check uses correct staging DLLs
    - **Property 8: Update check uses correct staging DLLs**
    - Test file: `RenoDXCommander.Tests/NormalReShadeUpdateCheckStagingPropertyTests.cs`
    - For `AddonType = "ReShadeNormal"`, compare against Normal_Staging; for `"ReShade"`, compare against Addon_Staging
    - **Validates: Requirements 9.2**

- [x] 3. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Add UseNormalReShade property and computed properties to GameCardViewModel
  - [x] 4.1 Add `[ObservableProperty] private bool _useNormalReShade` to `GameCardViewModel.cs`
    - Default value `false`
    - _Requirements: 1.1_
  - [x] 4.2 Create computed properties `AddonsDisabled` and `AddonStrikethrough`
    - `AddonsDisabled => UseNormalReShade`
    - `AddonStrikethrough => UseNormalReShade`
    - Can be added in a new partial file `GameCardViewModel.NormalReShade.cs` or inline
    - _Requirements: 4.1, 4.2_
  - [x] 4.3 Modify `DcInstallEnabled` and `UlInstallEnabled` to add `&& !UseNormalReShade`
    - In `GameCardViewModel.DisplayCommander.cs`: `DcInstallEnabled => !DcIsInstalling && !IsUlInstalled && !UseNormalReShade`
    - In `GameCardViewModel.UltraLimiter.cs`: `UlInstallEnabled => !UlIsInstalling && !IsDcInstalled && !Is32Bit && !UseNormalReShade`
    - _Requirements: 4.1, 4.3_
  - [x] 4.4 Add `OnUseNormalReShadeChanged` partial method
    - Trigger notifications for `AddonsDisabled`, `AddonStrikethrough`, `DcInstallEnabled`, `UlInstallEnabled`, and other addon-related dependents
    - Update `NotifyAll()` to include new properties
    - _Requirements: 4.1, 4.3_
  - [x] 4.5 Write property test: Addon UI disabled when UseNormalReShade is active
    - **Property 3: Addon UI disabled when UseNormalReShade is active**
    - Test file: `RenoDXCommander.Tests/NormalReShadeAddonUiDisabledPropertyTests.cs`
    - For any card state with `UseNormalReShade=true`, `DcInstallEnabled`, `UlInstallEnabled` are false and `AddonsDisabled` is true; when false, existing mutual-exclusivity rules apply
    - **Validates: Requirements 4.1, 4.3**

- [x] 5. Implement settings persistence for NormalReShadeGames
  - [x] 5.1 Add `HashSet<string> _normalReShadeGames` to MainViewModel / SettingsViewModel
    - Use `StringComparer.OrdinalIgnoreCase`
    - Persist as JSON array under key `"NormalReShadeGames"` in `settings.json`
    - Load at startup, restore `UseNormalReShade` on each matching card
    - _Requirements: 1.2, 1.3, 1.4_
  - [x] 5.2 Write property test: Toggle persistence round-trip
    - **Property 1: Toggle persistence round-trip**
    - Test file: `RenoDXCommander.Tests/NormalReShadeTogglePersistenceRoundTripPropertyTests.cs`
    - For any set of game names (special chars, mixed case, Unicode), serialize → deserialize produces equivalent set; removing a game and re-serializing excludes it
    - **Validates: Requirements 1.2, 1.3, 1.4**

- [x] 6. Implement toggle orchestration in MainViewModel
  - [x] 6.1 Create `SetUseNormalReShade(GameCardViewModel card, bool enable)` method
    - Enable path: check Normal_Staging DLLs exist → uninstall addon RS → remove managed addons → install normal RS → add to `_normalReShadeGames` → persist → update card
    - Disable path: uninstall normal RS → install addon RS → re-deploy addons → remove from `_normalReShadeGames` → persist → update card
    - Show error if Normal_Staging DLLs missing on enable
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 3.3, 9.3_

- [x] 7. Implement addon deployment exclusion
  - [x] 7.1 Add guard check in `MainViewModel.DeployAddonsForCard` and `DeployAllAddons`
    - If `card.UseNormalReShade`, deploy empty selection (removes all managed addons)
    - Otherwise use game's actual addon selection
    - _Requirements: 3.1, 3.2_
  - [x] 7.2 Write property test: Addon deployment exclusion
    - **Property 2: Addon deployment exclusion**
    - Test file: `RenoDXCommander.Tests/NormalReShadeAddonExclusionPropertyTests.cs`
    - For any card with `UseNormalReShade=true`, zero addons deployed regardless of selection; for `false`, actual selection used
    - **Validates: Requirements 3.1, 3.2**

- [x] 8. Register NormalReShadeUpdateService in DI and wire startup
  - [x] 8.1 Register `INormalReShadeUpdateService` / `NormalReShadeUpdateService` in the DI container
    - Follow existing `IReShadeUpdateService` registration pattern
    - _Requirements: 5.1, 5.2_
  - [x] 8.2 Call `NormalReShadeUpdateService.EnsureLatestAsync` during app startup alongside existing `ReShadeUpdateService.EnsureLatestAsync`
    - _Requirements: 5.1, 5.2, 6.2, 6.3_

- [x] 9. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
