# Implementation Plan: 32-bit OGL/DX9 Support

## Overview

Implement 32-bit ReLimiter support, automatic OpenGL/DX9 ReShade DLL naming, version bump to 1.6.4, patch notes, and documentation updates. All changes are in C# within the existing `MainViewModel`, `UpdateOrchestrationService`, and supporting files. Property-based tests use FsCheck + xUnit in the existing test project.

## Tasks

- [x] 1. ReLimiter bitness support
  - [x] 1.1 Split ReLimiter constants and add helper methods in MainViewModel
    - Replace `UltraLimiterFileName` constant with `UltraLimiterFileName64` and `UltraLimiterFileName32`
    - Replace `UlCachePath` static field with `GetUlCachePath(bool is32Bit)` method
    - Add `GetUlFileName(bool is32Bit)` helper method
    - Add `LegacyUltraLimiterFileName32 = "ultra_limiter.addon32"` constant
    - _Requirements: 1.1, 1.2, 1.4_

  - [x] 1.2 Write property test: ReLimiter filename matches game bitness
    - **Property 1: ReLimiter filename matches game bitness**
    - Generate random booleans for `is32Bit`, assert `GetUlFileName` returns correct addon filename and `GetUlCachePath(true)` ≠ `GetUlCachePath(false)`
    - **Validates: Requirements 1.1, 1.2, 1.4, 1.7**

  - [x] 1.3 Update InstallUlAsync for bitness-aware install
    - Pass `card.Is32Bit` to `GetUlFileName` and `GetUlCachePath` throughout `InstallUlAsync`
    - Update `EnsureUlCachedAsync` to accept `bool is32Bit` parameter and cache the correct asset
    - Update `FetchLatestUlReleaseInfoAsync` to accept `bool is32Bit` and match the correct asset filename
    - Update legacy cleanup to also handle `LegacyUltraLimiterFileName32`
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 1.4 Update UninstallUl for bitness-aware uninstall
    - Use `GetUlFileName(card.Is32Bit)` for file deletion in `UninstallUl`
    - Add legacy `ultra_limiter.addon32` cleanup alongside existing `ultra_limiter.addon64` cleanup
    - _Requirements: 1.7_

  - [x] 1.5 Update CheckUlUpdateAsync and PreCacheRemoteUlAsync for both bitness variants
    - Update `CheckUlUpdateAsync` to check both 32-bit and 64-bit cache files when games of both types are installed
    - Update `PreCacheRemoteUlAsync` to handle bitness
    - _Requirements: 1.5_

  - [x] 1.6 Update card building for bitness-aware ReLimiter detection
    - In `BuildCards`, check for both `relimiter.addon32` and `relimiter.addon64` based on `newCard.Is32Bit`
    - Set `UlInstalledFile` to the correct bitness-specific filename
    - _Requirements: 1.6_

- [x] 2. Checkpoint — ReLimiter bitness
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Automatic ReShade DLL naming
  - [x] 3.1 Add ResolveAutoReShadeFilename method to MainViewModel
    - Add `internal static string? ResolveAutoReShadeFilename(HashSet<GraphicsApiType> detectedApis)` method
    - DX9 detected → return `"d3d9.dll"`; OpenGL-only → return `"opengl32.dll"`; otherwise → return `null`
    - _Requirements: 2.1, 2.5, 3.1_

  - [x] 3.2 Write property test: Automatic ReShade DLL naming by detected graphics APIs
    - **Property 2: Automatic ReShade DLL naming by detected graphics APIs**
    - Generate random subsets of `GraphicsApiType` enum values, assert return value matches DX9/OpenGL/null rules
    - **Validates: Requirements 2.1, 2.2, 2.5, 3.1, 3.2**

  - [x] 3.3 Update InstallReShadeAsync filenameOverride resolution in MainViewModel
    - Change the `filenameOverride` argument in `InstallReShadeAsync` to fall through to `ResolveAutoReShadeFilename(card.DetectedApis)` when no user or manifest override exists
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 3.4 Update UpdateOrchestrationService ReShade filename resolution
    - In `UpdateAllReShadeAsync`, update the `filenameOverride` argument to also fall through to `ResolveAutoReShadeFilename(card.DetectedApis)` when no manifest override applies
    - Currently only passes user override; add manifest check and auto-naming fallback to match MainViewModel logic
    - _Requirements: 2.1, 2.3, 2.4, 3.1, 3.3, 3.4_

  - [x] 3.5 Write property test: User DLL override takes priority over automatic naming
    - **Property 3: User DLL override takes priority over automatic naming**
    - Generate random `DetectedApis` sets and random non-empty `ReShadeFileName` strings, simulate resolution chain with `DllOverrideEnabled = true`, assert user override always wins
    - **Validates: Requirements 2.3, 3.3**

  - [x] 3.6 Write property test: Manifest DLL override takes priority over automatic naming
    - **Property 4: Manifest DLL override takes priority over automatic naming**
    - Generate random `DetectedApis` sets and random non-empty manifest `ReShade` strings, simulate resolution chain with `DllOverrideEnabled = false` and manifest entry, assert manifest value always wins
    - **Validates: Requirements 2.4, 3.4**

- [x] 4. Checkpoint — ReShade auto-naming
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Version bump
  - [x] 5.1 Update AssemblyVersion and FileVersion in RenoDXCommander.csproj
    - Change `AssemblyVersion` from `1.6.3.0` to `1.6.4.0`
    - Change `FileVersion` from `1.6.3.0` to `1.6.4.0`
    - _Requirements: 4.1, 4.2_

- [x] 6. Patch notes
  - [x] 6.1 Prepend v1.6.4 section to RHI_PatchNotes.md
    - Add `## v1.6.4` section before the existing `## v1.6.3` section
    - Document 32-bit ReLimiter support, automatic OpenGL DLL naming, and automatic DX9 DLL naming
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [x] 7. Documentation updates
  - [x] 7.1 Update README.md features list
    - Add 32-bit ReLimiter support to the features list
    - Add automatic OpenGL and DX9 ReShade DLL naming to the features list
    - Correct any outdated information found during the update
    - _Requirements: 6.1, 6.2, 6.7_

  - [x] 7.2 Update docs/DETAILED_GUIDE.md
    - Update the ReLimiter section to describe 32-bit addon support and automatic bitness-based file selection
    - Update the Components section to describe automatic DLL naming for OpenGL and DX9 games
    - Update the Graphics API Detection section to note that OpenGL-only and DX9 detections now drive automatic ReShade DLL naming
    - Correct any outdated information found during the update
    - _Requirements: 6.3, 6.4, 6.5, 6.6_

- [x] 8. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The design uses C# throughout; all implementation is in the existing `RenoDXCommander` project
- Property tests use FsCheck + xUnit in `RenoDXCommander.Tests`
- `AuxInstallService.InstallReShadeAsync` already accepts `filenameOverride` and needs no modification
- `GameCardViewModel.Is32Bit` and `DetectedApis` already exist and are populated during card building
- Checkpoints ensure incremental validation after each major feature area
