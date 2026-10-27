# Implementation Plan: ReShade Addons

## Overview

Implements curated ReShade addon management for RHI, mirroring the existing Global Shaders pattern. Covers INI parsing, global addon manager window, download/storage, auto-update, per-game addon control with override panel integration, and deployment to game folders. All new services follow the established ShaderPackService / ShaderPopupHelper patterns. Property-based tests use FsCheck 2.16.6 with xUnit.

## Tasks

- [x] 1. Create AddonEntry model and AddonsIniParser
  - [x] 1.1 Create `RenoDXCommander/Models/AddonEntry.cs` with the `AddonEntry` record
    - Fields: SectionId, PackageName, PackageDescription?, DownloadUrl?, DownloadUrl32?, DownloadUrl64?, RepositoryUrl?, EffectInstallPath?
    - _Requirements: 1.2_

  - [x] 1.2 Create `RenoDXCommander/Services/AddonsIniParser.cs` with static `Parse` and `PrettyPrint` methods
    - `ExcludedSections` HashSet containing "00", "21", "26"
    - `Parse(string iniContent, Action<string>? logWarning)` — parses INI sections into `List<AddonEntry>`, skips excluded/commented/malformed sections
    - `PrettyPrint(List<AddonEntry> entries)` — formats entries back to valid Addons.ini format
    - _Requirements: 1.2, 1.3, 1.5, 1.6, 1.7_

  - [x] 1.3 Write property test: Addons.ini parse/print round-trip
    - Create `RenoDXCommander.Tests/AddonsIniParserRoundTripPropertyTests.cs`
    - Generate random lists of `AddonEntry`, pretty-print, parse back, verify equivalence
    - **Property 1: Addons.ini parse/print round-trip**
    - **Validates: Requirements 1.2, 1.6, 1.7**

  - [x] 1.4 Write property test: Parse filtering excludes invalid and excluded entries
    - Create `RenoDXCommander.Tests/AddonsIniParserFilteringPropertyTests.cs`
    - Generate INI content with mixed valid/excluded/commented/malformed sections, verify output contains only valid non-excluded entries
    - **Property 2: Parse filtering excludes invalid and excluded entries**
    - **Validates: Requirements 1.3, 1.5**

- [x] 2. Create IAddonPackService interface and AddonPackService implementation
  - [x] 2.1 Create `RenoDXCommander/Services/IAddonPackService.cs` with the interface
    - Properties: AvailablePacks, DownloadedAddonNames
    - Methods: EnsureLatestAsync, DownloadAddonAsync, IsDownloaded, CheckAndUpdateAllAsync, DeployAddonsForGame
    - _Requirements: 1.1, 2.5, 2.6, 2.7, 3.1, 3.7, 4.1, 7.1, 7.2, 7.3, 7.6_

  - [x] 2.2 Create `RenoDXCommander/Services/AddonPackService.cs` — core fetch, parse, and cache logic
    - Fetch Addons.ini from URL, parse via AddonsIniParser, cache to `addons_cache.ini`
    - Append RenoDX DevKit addon entry (PackageName: "RenoDX DevKit", DownloadUrl32: `https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-devkit.addon32`, DownloadUrl64: `https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-devkit.addon64`)
    - Fall back to cached file on fetch failure
    - Mirror ShaderPackService constructor/field patterns (HttpClient, staging dir, cache path)
    - _Requirements: 1.1, 1.4, 9.1, 9.2, 9.4_

  - [x] 2.3 Implement download logic in AddonPackService
    - `DownloadAddonAsync`: handle direct `.addon32`/`.addon64` saves and `.zip` extraction
    - Download both 32/64 variants when both URLs provided; single file when only one URL
    - Track versions in `versions.json` using the same GitHub release checking mechanism as ShaderPackService
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 2.4 Implement auto-update logic in AddonPackService
    - `CheckAndUpdateAllAsync`: check each downloaded addon for updates, download newer versions
    - For RenoDX DevKit, monitor the `snapshot` tag at `https://github.com/clshortfuse/renodx/releases`
    - Retain existing version on failure
    - _Requirements: 4.1, 4.2, 4.4, 9.3_

  - [x] 2.5 Implement deployment logic in AddonPackService
    - `DeployAddonsForGame`: copy correct bitness variant to game's addon deploy path
    - Handle global vs per-game selection
    - Skip addons that don't have the required bitness variant (no fallback to other variant)
    - Remove stale addon files not in active selection
    - Create deploy directory if missing
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6_

  - [x] 2.6 Write property test: Addon action type determined by URL availability
    - Create `RenoDXCommander.Tests/AddonActionTypePropertyTests.cs`
    - **Property 3: Addon action type determined by URL availability**
    - **Validates: Requirements 2.5, 2.6**

  - [x] 2.7 Write property test: URL classification — direct save vs zip extract
    - Create `RenoDXCommander.Tests/AddonUrlClassificationPropertyTests.cs`
    - **Property 5: URL classification — direct save vs zip extract**
    - **Validates: Requirements 3.2, 3.3**

  - [x] 2.8 Write property test: Download file count matches provided URLs
    - Create `RenoDXCommander.Tests/AddonDownloadFileCountPropertyTests.cs`
    - **Property 6: Download file count matches provided URLs**
    - **Validates: Requirements 3.4, 3.5**

  - [x] 2.9 Write property test: Addon version tracking round-trip
    - Create `RenoDXCommander.Tests/AddonVersionTrackingRoundTripPropertyTests.cs`
    - **Property 7: Addon version tracking round-trip**
    - **Validates: Requirements 3.7**

  - [x] 2.10 Write property test: Download status reflects storage state
    - Create `RenoDXCommander.Tests/AddonDownloadStatusPropertyTests.cs`
    - **Property 4: Download status reflects storage state**
    - **Validates: Requirements 2.7**

  - [x] 2.11 Write property test: Deployment matches active selection with correct bitness
    - Create `RenoDXCommander.Tests/AddonDeploymentBitnessPropertyTests.cs`
    - **Property 10: Deployment matches active selection with correct bitness**
    - Verify addons without the required bitness variant are skipped (not deployed)
    - **Validates: Requirements 6.2, 7.1, 7.2, 7.3, 7.4**

  - [x] 2.12 Write property test: Deployment removes stale addon files
    - Create `RenoDXCommander.Tests/AddonDeploymentStaleRemovalPropertyTests.cs`
    - **Property 11: Deployment removes stale addon files**
    - **Validates: Requirements 7.6**

  - [x] 2.13 Write property test: RenoDX DevKit addon always present in available packs
    - Create `RenoDXCommander.Tests/AddonRenoDxDevKitPresencePropertyTests.cs`
    - **Property 14: RenoDX DevKit addon always present in available packs**
    - **Validates: Requirements 9.1, 9.2**

  - [x] 2.14 Write property test: Missing bitness variant skips addon deployment
    - Create `RenoDXCommander.Tests/AddonMissingBitnessSkipPropertyTests.cs`
    - Generate random addons with missing 32/64 variants and random game bitness, verify skipped addons are not deployed
    - **Property 16: Missing bitness variant skips addon deployment**
    - **Validates: Requirements 7.3, 7.4**

- [x] 3. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Add per-game addon persistence to GameNameService
  - [x] 4.1 Add `PerGameAddonSelection` and `PerGameAddonMode` dictionaries to `IGameNameService` and `GameNameService`
    - Mirror the existing `PerGameShaderSelection` and `PerGameShaderMode` patterns
    - Include in `LoadNameMappings` / `SaveNameMappings` serialization
    - Update `RenameGame` to migrate addon keys when a game is renamed
    - _Requirements: 6.1, 6.2, 6.3, 6.5, 6.6_

  - [x] 4.2 Write property test: Per-game addon selection persistence round-trip
    - Create `RenoDXCommander.Tests/AddonSelectionPersistenceRoundTripPropertyTests.cs`
    - **Property 8: Per-game addon selection persistence round-trip**
    - **Validates: Requirements 6.5**

  - [x] 4.3 Write property test: Global toggle ON clears per-game selection
    - Create `RenoDXCommander.Tests/AddonGlobalToggleClearsSelectionPropertyTests.cs`
    - **Property 9: Global toggle ON clears per-game selection**
    - **Validates: Requirements 6.6**

- [x] 5. Create AddonPopupHelper for addon selection dialogs
  - [x] 5.1 Create `RenoDXCommander/AddonPopupHelper.cs` mirroring ShaderPopupHelper
    - `PopupContext` enum (Global, PerGame)
    - `ShowAsync` method: displays ContentDialog with checkboxes for available addons, returns selected package names or null on cancel
    - `ComputeCheckboxModel` internal pure method for testability
    - Show "no addons downloaded" message when Global_Addon_Set is empty
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

  - [x] 5.2 Write property test: Addon selection checkbox model
    - Create `RenoDXCommander.Tests/AddonCheckboxModelPropertyTests.cs`
    - **Property 12: Addon selection checkbox model**
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4**

  - [x] 5.3 Write property test: Cancel preserves existing selection
    - Create `RenoDXCommander.Tests/AddonCancelPreservesSelectionPropertyTests.cs`
    - **Property 13: Cancel preserves existing selection**
    - **Validates: Requirements 8.5**

- [x] 6. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Integrate Addon Manager Window into MainWindow header
  - [x] 7.1 Add "ReShade Addons" button to the main UI header in `MainWindow.xaml`, positioned to the right of "Global Shaders"
    - _Requirements: 2.1_

  - [x] 7.2 Create the Addon Manager Window ContentDialog (opened on button click)
    - Scrollable list of AddonEntry items with PackageName and PackageDescription
    - Download button for entries with download URLs; repository link for entries with only RepositoryUrl
    - Indicate already-downloaded addons
    - Progress indicator during downloads/updates
    - Wire click handler in `MainWindow.Events.cs`
    - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 4.3_

  - [x] 7.3 Implement first-time warning dialog
    - On first click of "ReShade Addons" button, show a ContentDialog warning that addons are advanced features for experienced users
    - "Continue" button: persist `AddonWarningDismissed = true` in settings, then open the Addon Manager Window
    - "Cancel" button: close dialog without persisting the flag and without opening the manager
    - On subsequent clicks (flag already true), skip the warning and open the manager directly
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

  - [x] 7.4 Write property test: First-time warning shown exactly once
    - Create `RenoDXCommander.Tests/AddonFirstTimeWarningPropertyTests.cs`
    - **Property 15: First-time warning shown exactly once**
    - **Validates: Requirements 10.1, 10.4, 10.5**

- [x] 8. Restructure Override Panel and add Addons section
  - [x] 8.1 Restructure `DetailPanelBuilder.Overrides.cs` layout
    - Move Bitness/API to the middle row (where Shaders currently is)
    - Move Shaders to the bottom row, left side
    - _Requirements: 5.1, 5.2_

  - [x] 8.2 Add Addons override section to the bottom row, right side
    - "Global" toggle switch (mirrors Shaders toggle pattern)
    - "Select Addons" button (enabled when Global is OFF, mirrors "Select Shaders" button)
    - Wire toggle to persist `PerGameAddonMode` and clear per-game selection when switching to Global
    - Wire "Select Addons" button to open AddonPopupHelper
    - Trigger addon deployment on selection change
    - _Requirements: 5.3, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_

- [x] 9. Wire addon lifecycle into MainViewModel and startup
  - [x] 9.1 Add AddonPackService to MainViewModel
    - Instantiate and expose `IAddonPackService`
    - Call `EnsureLatestAsync` on startup (fire-and-forget, same pattern as ShaderPackService)
    - Call `CheckAndUpdateAllAsync` after initial fetch
    - _Requirements: 1.1, 4.1_

  - [x] 9.2 Add addon deployment orchestration to MainViewModel
    - `DeployAddonsForCard(string gameName)` method mirroring `DeployShadersForCard`
    - Call deployment after install/uninstall of ReShade, after addon selection changes, and after global addon set changes
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6_

  - [x] 9.3 Wire global addon selection in Settings / InstallEventHandler
    - Add `ChooseAddonsButton_Click` handler in `InstallEventHandler.cs` mirroring `ChooseShadersButton_Click`
    - Persist global addon selection and trigger deployment to all games
    - _Requirements: 2.2, 6.1, 6.2_

- [x] 10. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All new services follow the established ShaderPackService / ShaderPopupHelper patterns
- FsCheck 2.16.6 with xUnit integration (`[Property(MaxTest = 20)]` attribute pattern)
