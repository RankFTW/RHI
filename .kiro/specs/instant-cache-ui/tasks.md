# Implementation Plan: Instant Cache UI

## Overview

Refactor `InitializeAsync` in `MainViewModel.Init.cs` into a two-phase approach: load cached library and display cards immediately (Phase 1), then run the full detection/network pipeline in the background and merge results silently (Phase 2). This eliminates the 2–5 second skeleton screen on subsequent launches.

## Tasks

- [x] 1. Add new observable properties to MainViewModel
  - [x] 1.1 Add `IsBackgroundScanning` and `BackgroundScanStatusText` properties
    - Add `[ObservableProperty] private bool _isBackgroundScanning;` to `MainViewModel.cs`
    - Add `[ObservableProperty] private string _backgroundScanStatusText = "";` to `MainViewModel.cs`
    - These drive status bar messaging during Phase 2
    - _Requirements: 5.1, 5.2_

- [x] 2. Implement cache-based card loading (Phase 1)
  - [x] 2.1 Create `LoadCacheAndBuildCardsAsync(SavedGameLibrary savedLib)` method in `MainViewModel.Init.cs`
    - Restore hidden/favourite sets, manual games, all caches (engine type, resolved path, addon file, bitness) from `savedLib`
    - Convert cached games to `DetectedGame` list via `_gameLibraryService.ToDetectedGames`
    - Apply game renames, folder overrides
    - Call `BuildCards` with cached data (no network, no filesystem traversal)
    - Apply card overrides, manifest card overrides (from cached manifest if available)
    - Push to `FilterViewModel`, apply filter
    - Set `IsLoading = false`, call `MarkInitialized()`
    - Restore selection from `LastSelectedGameName`
    - Set `StatusText` to show cached game count and scan age (e.g. "Library loaded (N games, scanned 2 min ago)")
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 5.1_

- [x] 3. Implement background scan and merge (Phase 2)
  - [x] 3.1 Create `RunBackgroundScanAndMergeAsync(SavedGameLibrary savedLib)` method in `MainViewModel.Init.cs`
    - Set `IsBackgroundScanning = true` and `BackgroundScanStatusText = "Scanning for changes..."`
    - Launch all background tasks in parallel: detection, wiki, manifest, luma, Nexus, PCGW, Lyall, OptiScaler, HDR DB, ReShade staging
    - Await detection + network tasks (same error handling as current code)
    - Rebuild cards from fresh data using `BuildCards`
    - Call `MergeCards(freshCards)` to reconcile with displayed cards
    - Save updated library
    - Set `IsBackgroundScanning = false`
    - Update `StatusText` with final game count and mod count
    - Launch deferred work (shader sync, addon deploy) — same as today
    - Wrap entire method in try/catch; on failure log and leave cached cards in place
    - _Requirements: 3.1, 3.2, 5.1, 5.2_

  - [x] 3.2 Create `MergeCards(List<GameCardViewModel> freshCards)` method in `MainViewModel.Init.cs`
    - Build lookup of existing `_allCards` by `GameName` (case-insensitive)
    - For each fresh card: if match exists → update mutable properties in-place; if no match → add to `_allCards`
    - For each existing card not in fresh set → remove from `_allCards` (stale game)
    - Implement zero-detection guard: if `freshCards.Count == 0 && _allCards.Count > 0`, log warning and skip merge
    - Preserve `SelectedGame`: if still in list keep it, if removed select first card
    - Re-sort `_allCards` by game name
    - Push to `FilterViewModel`, apply filter
    - _Requirements: 3.2, 3.3, 3.4, 3.5_

- [x] 4. Modify `InitializeAsync` to branch on cache availability
  - [x] 4.1 Add early branch in `InitializeAsync`
    - After loading `savedLib` and checking `!forceRescan`, if cache exists:
      - Call `await LoadCacheAndBuildCardsAsync(savedLib)`
      - Fire-and-forget `_ = RunBackgroundScanAndMergeAsync(savedLib)`
      - Return early (skip the rest of the existing pipeline)
    - When no cache exists or `forceRescan` is true: existing full-init path unchanged
    - _Requirements: 1.1, 1.3, 1.4, 4.1, 4.2_

- [x] 5. Checkpoint
  - Ensure the project builds cleanly (`dotnet build`), ask the user if questions arise.

- [ ]* 6. Write property tests for merge correctness
  - [ ]* 6.1 Write property test for SavedGameLibrary serialization round-trip
    - **Property 1: SavedGameLibrary serialization round-trip**
    - Generate random `SavedGameLibrary` instances with varied games, cache dictionaries, hidden/favourite sets, manual games, last-selected game
    - Verify `Serialize → Deserialize` produces equivalent object with all fields preserved
    - Use FsCheck or CsCheck with minimum 100 iterations
    - **Validates: Requirements 2.1, 2.2**

  - [ ]* 6.2 Write property test for merge correctness
    - **Property 2: Merge correctness — fresh results reconciled with cached cards**
    - Generate random cached card list + random fresh card list
    - Verify post-merge list contains exactly fresh games + manual games, sorted by name
    - **Validates: Requirements 3.2, 3.3**

  - [ ]* 6.3 Write property test for selection preservation during merge
    - **Property 3: Selection preservation during merge**
    - Generate random lists + selected game in both/only-cached
    - Verify selected game preserved or falls back to first game in merged result
    - **Validates: Requirements 3.4, 3.5**

- [ ]* 7. Write unit tests for cache and merge behaviour
  - [ ]* 7.1 Test cache hit displays cards and suppresses skeleton
    - Provide a `SavedGameLibrary`, verify `IsLoading == false` and `HasInitialized == true` after cache phase
    - _Requirements: 1.1, 1.2, 1.3_

  - [ ]* 7.2 Test cache miss falls back to full init with skeleton
    - Provide `null` library, verify skeleton screen shown (IsLoading stays true until pipeline completes)
    - _Requirements: 1.4_

  - [ ]* 7.3 Test zero-detection guard skips merge
    - Background scan returns 0 games, cache had 10 → verify merge is skipped and cached cards preserved
    - _Requirements: 3.2_

  - [ ]* 7.4 Test status text during and after background scan
    - Verify `StatusText` contains game count and scanning indicator during Phase 2
    - Verify `StatusText` contains final game count and mod count after Phase 2 completes
    - _Requirements: 5.1, 5.2_

- [x] 8. Final checkpoint
  - Ensure all tests pass and the project builds cleanly, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The existing `FullRefreshAsync` path is unchanged — it clears caches and calls `InitializeAsync(forceRescan: true)` which takes the else-branch
- `MergeCards` updates card properties in-place so WinUI bindings fire automatically; collection updates go through `FilterViewModel.ApplyFilter()` which uses `ReplaceAll` (single Reset notification)
- All code changes are in `MainViewModel.cs` and `MainViewModel.Init.cs` — no new files needed
