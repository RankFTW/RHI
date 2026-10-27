# Implementation Plan: Universal Search Filters

## Overview

Extend the RHI application's search and filtering system with universal keyword search across all game card properties and user-saveable custom filter chips. Implementation proceeds bottom-up: model → ViewModel logic → persistence → UI → wiring → tests.

## Tasks

- [x] 1. Create CustomFilter model and extend FilterViewModel core logic
  - [x] 1.1 Create `RenoDXCommander/Models/CustomFilter.cs` with `Name` and `Query` string properties
    - Simple POCO class in the Models namespace
    - _Requirements: 3.4, 6.4_

  - [x] 1.2 Add `MatchesUniversalSearch` static method to `FilterViewModel`
    - Add `internal static bool MatchesUniversalSearch(GameCardViewModel card, string query)` that checks: GameName, Maintainer, Source, EngineHint, GraphicsApi.ToString(), "32-bit"/"64-bit" based on Is32Bit, Mod?.Name, Mod?.Maintainer, LumaMod?.Name, LumaMod?.Author, VulkanRenderingPath
    - Use null-conditional operators for nullable Mod and LumaMod properties
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 1.3 Update `ApplyFilter()` to use `MatchesUniversalSearch`
    - Replace the existing two-field search match (`GameName` + `Maintainer`) with `MatchesUniversalSearch(c, query)` call
    - Keep the `string.IsNullOrEmpty(query)` short-circuit
    - _Requirements: 1.1, 1.4, 1.7, 2.1_

  - [x] 1.4 Write property test: Universal search produces correct subset (Property 1)
    - **Property 1: Universal search produces correct subset**
    - Create `RenoDXCommander.Tests/FilterUniversalSearchPropertyTests.cs`
    - Generate random GameCardViewModels with varied Source, EngineHint, GraphicsApi, Is32Bit, Mod, LumaMod, VulkanRenderingPath values
    - Assert: with "Detected" filter, ApplyFilter returns exactly the non-hidden cards where MatchesUniversalSearch is true
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.7**

  - [x] 1.5 Write property test: Search and filter intersection (Property 2)
    - **Property 2: Search and filter intersection**
    - Add to `FilterUniversalSearchPropertyTests.cs`
    - Generate random cards, search queries, and valid filter combinations
    - Assert: result equals intersection of "cards matching search" and "cards matching filter"
    - **Validates: Requirements 2.1**

  - [x] 1.6 Write unit tests for specific search examples
    - Create `RenoDXCommander.Tests/UniversalSearchUnitTests.cs`
    - Test: "dx11" returns cards with GraphicsApi == DirectX11 (Req 1.5)
    - Test: "steam" returns cards with Source containing "steam" (Req 1.6)
    - Test: empty/whitespace search returns all non-hidden cards (Req 1.4)
    - Test: clearing search while filters active shows filter-only results (Req 2.2)
    - _Requirements: 1.4, 1.5, 1.6, 2.2_

- [x] 2. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Implement custom filter management on FilterViewModel
  - [x] 3.1 Add custom filter properties and methods to `FilterViewModel`
    - Add `ObservableCollection<CustomFilter> CustomFilters` observable property
    - Add `string? ActiveCustomFilterName` observable property
    - Add `Action? CustomFiltersChanged` callback property (same pattern as `FilterModeChanged`)
    - Implement `bool AddCustomFilter(string name, string query)` — returns false if name is empty or duplicate (case-insensitive), otherwise adds and invokes `CustomFiltersChanged`
    - Implement `void RemoveCustomFilter(string name)` — removes by name, if it was active resets to "Detected" filter and clears SearchQuery, invokes `CustomFiltersChanged`
    - Implement `void ActivateCustomFilter(string name)` — sets SearchQuery to stored query, sets ActiveCustomFilterName
    - Implement `void DeactivateCustomFilter()` — sets ActiveCustomFilterName to null
    - Implement `bool CustomFilterNameExists(string name)` — case-insensitive check
    - _Requirements: 3.4, 3.5, 3.6, 4.2, 4.5, 5.2, 5.4_

  - [x] 3.2 Hook `DeactivateCustomFilter` into `OnSearchQueryChanged`
    - In the existing `OnSearchQueryChanged` partial method, check if the new value differs from the active custom filter's query; if so, call `DeactivateCustomFilter()`
    - _Requirements: 4.5_

  - [x] 3.3 Write property test: AddCustomFilter correctness (Property 4)
    - **Property 4: AddCustomFilter correctness**
    - Create `RenoDXCommander.Tests/CustomFilterManagementPropertyTests.cs`
    - Assert: AddCustomFilter succeeds iff name is non-empty and no duplicate exists; list unchanged on failure
    - **Validates: Requirements 3.4, 3.5, 3.6**

  - [x] 3.4 Write property test: ActivateCustomFilter sets SearchQuery (Property 5)
    - **Property 5: ActivateCustomFilter sets SearchQuery**
    - Add to `CustomFilterManagementPropertyTests.cs`
    - Assert: after ActivateCustomFilter, SearchQuery equals stored query and ActiveCustomFilterName equals filter name
    - **Validates: Requirements 4.2**

  - [x] 3.5 Write property test: Manual search edit deactivates custom filter (Property 6)
    - **Property 6: Manual search edit deactivates custom filter**
    - Add to `CustomFilterManagementPropertyTests.cs`
    - Assert: changing SearchQuery to a different value sets ActiveCustomFilterName to null
    - **Validates: Requirements 4.5**

  - [x] 3.6 Write property test: RemoveCustomFilter correctness (Property 7)
    - **Property 7: RemoveCustomFilter correctness**
    - Add to `CustomFilterManagementPropertyTests.cs`
    - Assert: after removal, filter no longer in list; other filters unchanged
    - **Validates: Requirements 5.2**

  - [x] 3.7 Write property test: Delete active custom filter falls back to Detected (Property 8)
    - **Property 8: Delete active custom filter falls back to Detected**
    - Add to `CustomFilterManagementPropertyTests.cs`
    - Assert: after deleting active filter, SearchQuery is empty, ActiveCustomFilterName is null, active filters contain "Detected"
    - **Validates: Requirements 5.4**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement persistence for custom filters
  - [x] 5.1 Extend `GameNameService.SaveNameMappings` to serialize custom filters
    - Add a `List<CustomFilter>` parameter (or access via FilterViewModel reference) to `SaveNameMappings`
    - Serialize the custom filters list as JSON under the `"CustomFilters"` key in the settings dictionary
    - Update `IGameNameService` interface if the signature changes
    - _Requirements: 6.1_

  - [x] 5.2 Extend `GameNameService.LoadNameMappings` to deserialize custom filters
    - Read the `"CustomFilters"` key from the settings dictionary
    - Deserialize into `List<CustomFilter>`, defaulting to empty list on missing key or malformed JSON
    - Log a warning via CrashReporter on malformed JSON
    - Pass loaded filters to FilterViewModel (add a callback parameter or return value)
    - _Requirements: 6.2, 6.3_

  - [x] 5.3 Wire `CustomFiltersChanged` callback in `MainViewModel`
    - In `MainViewModel` initialization (where `FilterModeChanged` is set), also set `FilterViewModel.CustomFiltersChanged` to trigger `SaveNameMappings()`
    - Update `MainViewModel.Settings.cs` `SaveNameMappings` and `LoadNameMappings` calls to pass/receive custom filter data
    - _Requirements: 6.1, 6.2_

  - [x] 5.4 Write property test: Custom filter serialization round-trip (Property 9)
    - **Property 9: Custom filter serialization round-trip**
    - Create `RenoDXCommander.Tests/CustomFilterRoundTripPropertyTests.cs`
    - Generate random lists of CustomFilter objects with non-empty names and query strings
    - Assert: serializing to JSON and deserializing back produces equivalent list
    - **Validates: Requirements 6.4**

  - [x] 5.5 Write unit test for malformed JSON handling
    - Add to `UniversalSearchUnitTests.cs`
    - Test: malformed JSON in settings results in empty custom filter list without crash (Req 6.3)
    - Test: loading settings with valid custom filters populates FilterViewModel correctly (Req 6.2)
    - _Requirements: 6.2, 6.3_

- [x] 6. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Add UI elements for save button and custom filter chips
  - [x] 7.1 Add Save button ("+") to the search box area in `MainWindow.xaml`
    - Add a Button with "+" content inside or adjacent to the SearchBox Border in the sidebar (Grid.Row="0")
    - Bind visibility to whether SearchBox text is non-empty/non-whitespace
    - Wire Click handler to `SaveFilterButton_Click`
    - _Requirements: 3.1, 3.2_

  - [x] 7.2 Add custom filter chip area in `MainWindow.xaml`
    - Add a `StackPanel` (Orientation="Horizontal", wrapping) or `ItemsRepeater` below the existing built-in chip rows (inside the Grid.Row="1" filter chips Border)
    - Name it `CustomFilterChipPanel` for programmatic population
    - _Requirements: 4.1_

  - [x] 7.3 Write property test: Save button visibility matches query state (Property 3)
    - **Property 3: Save button visibility matches query state**
    - Add to `CustomFilterManagementPropertyTests.cs` or a new file
    - Test the logic function (not UI): for any string, save should be enabled iff trimmed string is non-empty
    - **Validates: Requirements 3.1, 3.2**

- [x] 8. Implement event handlers and UI wiring
  - [x] 8.1 Add `SaveFilterButton_Click` handler in `MainWindow.Events.cs`
    - Open a `ContentDialog` (naming dialog) with a TextBox for the filter name
    - On confirm: validate non-empty name, check for duplicates via `FilterViewModel.CustomFilterNameExists()`, call `FilterViewModel.AddCustomFilter(name, query)`
    - Show validation message if name is duplicate (Req 3.6)
    - On cancel or empty name: close dialog, no side effects
    - _Requirements: 3.3, 3.4, 3.5, 3.6_

  - [x] 8.2 Implement custom chip rendering and click handler
    - Create a method `RebuildCustomFilterChips()` in `MainWindow.xaml.cs` or `MainWindow.UISync.cs` that clears and rebuilds the `CustomFilterChipPanel` children from `FilterViewModel.CustomFilters`
    - Each chip: Button with filter name as Content, styled with active/inactive chip brushes based on `ActiveCustomFilterName`
    - Click handler calls `FilterViewModel.ActivateCustomFilter(name)` and refreshes chip styles
    - Subscribe to `FilterViewModel.CustomFilters.CollectionChanged` to trigger rebuild
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 8.3 Implement right-click delete on custom filter chips
    - Attach `RightTapped` handler to each custom chip Button
    - Show a `MenuFlyout` with a "Delete" item
    - Delete handler calls `FilterViewModel.RemoveCustomFilter(name)` and rebuilds chips
    - Do NOT attach context menu to built-in filter chips
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

  - [x] 8.4 Extend `SearchBox_TextChanged` to deactivate custom filter
    - In the existing `SearchBox_TextChanged` handler, after setting `ViewModel.SearchQuery`, the `OnSearchQueryChanged` partial method in FilterViewModel will handle deactivation automatically (from task 3.2)
    - Refresh custom chip styles when active filter changes
    - _Requirements: 4.5_

- [x] 9. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The implementation language is C# (WinUI 3 / CommunityToolkit.Mvvm / FsCheck)
