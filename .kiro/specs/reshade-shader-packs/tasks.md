# Implementation Plan: ReShade Installer Shader Packs

## Overview

Add 36 new `ShaderPack` entries to the `Packs` array in `ShaderPackService.cs` and update 2 existing pack URLs. All changes are data-driven — no new classes, interfaces, or architectural changes. Property-based tests validate correctness properties from the design document.

## Tasks

- [x] 1. Update existing pack URLs and add first batch of new packs (1–12)
  - [x] 1.1 Update `MaxG2DSimpleHDR` URL to `https://github.com/MaxG2D/ReshadeSimpleHDRShaders/archive/refs/heads/main.zip`
    - Change SourceKind from DirectUrl (unchanged) but update the Url from the specific release .7z to the main branch archive .zip
    - _Requirements: 1.1, 1.4_
  - [x] 1.2 Update `PotatoFX` URL and DisplayName to GimleLarpes upstream
    - Url: `https://github.com/GimleLarpes/potatoFX/archive/refs/heads/master.zip`
    - DisplayName: `potatoFX` (keep as-is or update to match upstream)
    - _Requirements: 1.1, 1.4_
  - [x] 1.3 Add new packs 1–12 to the `Packs` array
    - Append `SweetFX`, `CrosireLegacy`, `OtisFX`, `Depth3D`, `FXShaders`, `DaodanShaders`, `BrussellShaders`, `FubaxShaders`, `qUINT`, `AlucardDH`, `WarpFX`, `Prod80`
    - All use `SourceKind.DirectUrl`, `IsMinimum = false`, URLs as specified in design document
    - _Requirements: 1.1, 1.2, 1.3_

- [x] 2. Add remaining new packs (13–36)
  - [x] 2.1 Add new packs 13–24 to the `Packs` array
    - Append `CorgiFX`, `InsaneShaders`, `CobraFX`, `AstrayFX`, `CRTRoyale`, `RSRetroArch`, `VRToolkit`, `FGFX`, `CShade`, `iMMERSE`, `VortShaders`, `BXShade`
    - All use `SourceKind.DirectUrl`, `IsMinimum = false`, URLs as specified in design document
    - _Requirements: 1.1, 1.2, 1.3_
  - [x] 2.2 Add new packs 25–36 to the `Packs` array
    - Append `SHADERDECK`, `METEOR`, `AnnReShade`, `ZenteonFX`, `GShadeShaders`, `PthoFX`, `Anagrama`, `BarbatosShaders`, `BFBFX`, `Rendepth`, `CropAndResize`, `LumeniteFX`
    - All use `SourceKind.DirectUrl`, `IsMinimum = false`, URLs as specified in design document
    - _Requirements: 1.1, 1.2, 1.3_

- [x] 3. Checkpoint - Verify pack array compiles and basic structure
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Write property-based tests for pack data integrity
  - [x] 4.1 Write property test for pack array completeness
    - **Property 1: Pack array completeness**
    - Verify all 43 expected Ids exist in `Packs`, total count is 43, and `AvailablePacks` matches
    - **Validates: Requirements 1.1, 1.4, 4.1**
  - [x] 4.2 Write property test for pack definition validity
    - **Property 2: Pack definition validity**
    - Verify all Ids are unique, all DisplayNames are non-empty, all Urls are valid absolute URIs
    - **Validates: Requirements 1.2**
  - [x] 4.3 Write property test for DeployMode filtering correctness
    - **Property 3: DeployMode filtering correctness**
    - Generate random `DeployMode` values, verify `PacksForMode` returns correct subset per mode rules; verify Minimum returns exactly 1 pack (Lilium), All returns 43
    - **Validates: Requirements 1.3, 5.1, 5.2, 5.3, 5.4**
  - [x] 4.4 Write property test for settings key naming convention
    - **Property 4: Settings key naming convention**
    - Generate random non-empty alphanumeric pack Id strings, verify `FileListKey` produces `ShaderPack_{Id}_Files` and version key produces `ShaderPack_{Id}_Version`
    - **Validates: Requirements 2.3, 2.4, 2.5**

- [x] 5. Write property-based tests for deployment and round-trip correctness
  - [x] 5.1 Write property test for Select mode deploying only selected packs
    - **Property 5: Select mode deploys only selected packs**
    - Generate random subsets of pack Ids, call `SyncGameFolder` with Select mode and subset, verify only selected pack files are present
    - **Validates: Requirements 4.3, 4.4**
  - [x] 5.2 Write property test for pruning preserving user-added files
    - **Property 6: Pruning preserves user-added files**
    - Generate random user filenames, place in game reshade-shaders folder alongside managed files, call sync with different mode/selection, verify user files survive
    - **Validates: Requirements 7.2, 7.3**
  - [x] 5.3 Write property test for extracted file list round-trip
    - **Property 7: Extracted file list round-trip**
    - Generate random lists of relative file path strings, serialize to JSON, write to temp settings file, read back, deserialize, compare
    - **Validates: Requirements 7.4**
  - [x] 5.4 Write property test for selected shader packs round-trip
    - **Property 8: Selected shader packs round-trip**
    - Generate random lists of pack Id strings, save via `SaveSettingsToDict`, load via `LoadSettingsFromDict`, compare
    - **Validates: Requirements 4.5**

- [x] 6. Write unit tests for specific pack data and edge cases
  - [x] 6.1 Write unit tests for pack data correctness
    - Verify exact pack count is 43
    - Verify `MaxG2DSimpleHDR` URL updated to main branch archive
    - Verify `PotatoFX` URL updated to GimleLarpes upstream
    - Verify `CrosireSlim` and `CrosireLegacy` point to different branches
    - Verify `ClshortfuseShaders` is still present (RDXC-only pack)
    - Verify `PacksForMode(Minimum)` returns exactly 1 pack (Lilium)
    - Verify empty selection in Select mode behaves like Off mode
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 5.2_

- [x] 7. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The core implementation is tasks 1–3: purely data changes to `ShaderPackService.cs`
- All 36 new packs use `DirectUrl` (no GitHub API calls), avoiding rate limit concerns
- Property tests use FsCheck 2.16.6 with xUnit in `RenoDXCommander.Tests/`
- Tests should be added to `ShaderPackServicePropertyTests.cs` or a new file following existing naming conventions
