# Tasks: Persist Filter Mode

## Task 1: Add FilterModeChanged callback and save hook to FilterViewModel
- [x] 1.1 Add `Action? FilterModeChanged` property to `FilterViewModel`
- [x] 1.2 Invoke `FilterModeChanged?.Invoke()` at the end of `SetFilter` after `ApplyFilter()`
- [x] 1.3 Add `RestoreFilterMode(string? persisted)` method to `FilterViewModel` that validates the input, populates `_activeFilters`, sets `FilterMode`, and calls `ApplyFilter()`

## Task 2: Add FilterMode to settings persistence in GameNameService
- [x] 2.1 Add `Action<string> setFilterMode` parameter to `IGameNameService.LoadNameMappings` and `GameNameService.LoadNameMappings`; read `s["FilterMode"]` and invoke the callback
- [x] 2.2 Add `string filterMode` parameter to `IGameNameService.SaveNameMappings` and `GameNameService.SaveNameMappings`; write `s["FilterMode"] = filterMode`

## Task 3: Wire FilterViewModel persistence in MainViewModel
- [x] 3.1 In `MainViewModel` constructor, set `_filterViewModel.FilterModeChanged = () => SaveNameMappings()`
- [x] 3.2 Update `LoadNameMappings` call to pass `val => _filterViewModel.RestoreFilterMode(val)` callback
- [x] 3.3 Update `SaveNameMappings` call to pass `_filterViewModel.FilterMode`

## Task 4: Write property-based tests
- [x] 4.1 Create test file with FsCheck generators for valid/invalid filter modes
- [x] 4.2 Property 1: SetFilter produces correct FilterMode string (Feature: persist-filter-mode, Property 1)
- [x] 4.3 Property 2: RestoreFilterMode correctly restores valid state (Feature: persist-filter-mode, Property 2)
- [x] 4.4 Property 3: Invalid input falls back to Detected (Feature: persist-filter-mode, Property 3)
- [x] 4.5 Property 4: Round-trip persistence preserves filter set (Feature: persist-filter-mode, Property 4)

## Task 5: Write unit tests
- [x] 5.1 Test that settings key is literally "FilterMode"
- [x] 5.2 Test RestoreFilterMode with null, empty, and whitespace inputs defaults to "Detected"
- [x] 5.3 Test RestoreFilterMode with mixed exclusive+combinable input defaults to "Detected"
- [x] 5.4 Test first-launch default is "Detected" when no settings file exists
