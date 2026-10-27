# Requirements Document

## Introduction

The RHI application currently limits its search bar to matching game names and maintainer fields only. This feature transforms the search bar into a universal keyword search that matches across all game card properties (name, source, engine, graphics API, bitness, mod details, etc.). Additionally, users can save any search query as a named custom filter chip that appears alongside the existing built-in filter chips, persists across sessions, and can be removed via right-click.

## Glossary

- **Search_Bar**: The text input field in the sidebar used to enter keyword queries for filtering the game list.
- **Filter_Chip**: A clickable button in the sidebar representing a predefined or custom filter category.
- **Custom_Filter**: A user-created filter chip that stores a saved search query string and a display name.
- **Game_Card**: A `GameCardViewModel` instance representing a single game entry with all its associated properties.
- **Filter_ViewModel**: The `FilterViewModel` class responsible for search, filter state, and applying combined filter logic to the game list.
- **Settings_File**: The JSON settings file managed by `GameNameService.SaveNameMappings` used to persist application state across sessions.
- **Save_Button**: A "+" icon button inside the Search_Bar area that opens the naming dialog to save the current search query as a Custom_Filter.
- **Naming_Dialog**: A modal dialog that prompts the user to enter a display name for a new Custom_Filter.

## Requirements

### Requirement 1: Universal Keyword Search

**User Story:** As a user, I want the search bar to match keywords across all game properties, so that I can find games by any attribute such as store, engine, graphics API, bitness, or author.

#### Acceptance Criteria

1. WHEN a user enters a query into the Search_Bar, THE Filter_ViewModel SHALL match the query as a case-insensitive substring against the following Game_Card properties: GameName, Maintainer, Source, EngineHint, the string representation of GraphicsApi, the string "32-bit" when Is32Bit is true and "64-bit" when Is32Bit is false, Mod.Name, Mod.Maintainer, LumaMod.Name, LumaMod.Author, and VulkanRenderingPath.
2. WHEN a query matches any one or more of the searchable properties on a Game_Card, THE Filter_ViewModel SHALL include that Game_Card in the filtered results.
3. WHEN a query does not match any searchable property on a Game_Card, THE Filter_ViewModel SHALL exclude that Game_Card from the filtered results.
4. WHEN the Search_Bar query is empty or contains only whitespace, THE Filter_ViewModel SHALL treat the search as matching all Game_Card entries (no search restriction applied).
5. WHEN a user types "dx11" into the Search_Bar, THE Filter_ViewModel SHALL display all Game_Card entries where GraphicsApi equals DirectX11.
6. WHEN a user types "steam" into the Search_Bar, THE Filter_ViewModel SHALL display all Game_Card entries where Source contains "steam".
7. WHEN a user types a partial name into the Search_Bar, THE Filter_ViewModel SHALL display Game_Card entries where any searchable property contains that substring.

### Requirement 2: Search Combined with Active Filters

**User Story:** As a user, I want the universal search to work together with the active filter chips, so that I can narrow results by both keyword and category simultaneously.

#### Acceptance Criteria

1. WHEN a search query is active and one or more Filter_Chip selections are active, THE Filter_ViewModel SHALL display only Game_Card entries that satisfy both the keyword search match and the active filter criteria.
2. WHEN the user clears the Search_Bar while filter chips remain active, THE Filter_ViewModel SHALL display all Game_Card entries matching the active filter criteria without any search restriction.
3. WHEN the user deactivates all filter chips while a search query is active, THE Filter_ViewModel SHALL fall back to the "Detected" filter and apply the search query against that set.

### Requirement 3: Save Search as Custom Filter

**User Story:** As a user, I want to save a search query as a named custom filter chip, so that I can quickly re-apply frequent searches without retyping them.

#### Acceptance Criteria

1. THE Search_Bar area SHALL display a Save_Button ("+") icon that is visible when the Search_Bar contains a non-empty, non-whitespace query.
2. WHEN the Save_Button is not applicable (the Search_Bar is empty or whitespace-only), THE Save_Button SHALL be hidden or disabled.
3. WHEN the user clicks the Save_Button, THE application SHALL open the Naming_Dialog pre-populated with an empty name field and a confirmation button.
4. WHEN the user enters a non-empty name in the Naming_Dialog and confirms, THE Filter_ViewModel SHALL create a new Custom_Filter storing the entered name and the current Search_Bar query text.
5. WHEN the user cancels the Naming_Dialog or submits an empty name, THE Filter_ViewModel SHALL not create a Custom_Filter and SHALL close the dialog without side effects.
6. IF the user attempts to save a Custom_Filter with a name that duplicates an existing Custom_Filter name, THEN THE Naming_Dialog SHALL display a validation message indicating the name is already in use.

### Requirement 4: Display Custom Filter Chips

**User Story:** As a user, I want my saved custom filters to appear as chips alongside the built-in filter chips, so that I can activate them with a single click.

#### Acceptance Criteria

1. THE sidebar SHALL display all Custom_Filter entries as Filter_Chip elements in a dedicated row or area below the existing built-in filter chip rows.
2. WHEN a Custom_Filter chip is clicked, THE Filter_ViewModel SHALL populate the Search_Bar with the stored query text and apply the search.
3. WHEN a Custom_Filter chip is active, THE Filter_Chip SHALL use the active visual style (ChipActiveBrush background, white foreground).
4. WHEN a Custom_Filter chip is not active, THE Filter_Chip SHALL use the default visual style (ChipDefaultBrush background, ChipTextBrush foreground).
5. WHEN the user manually changes the Search_Bar text after activating a Custom_Filter chip, THE Custom_Filter chip SHALL revert to the inactive visual style.

### Requirement 5: Delete Custom Filter via Right-Click

**User Story:** As a user, I want to delete a custom filter by right-clicking its chip, so that I can remove filters I no longer need.

#### Acceptance Criteria

1. WHEN the user right-clicks a Custom_Filter chip, THE application SHALL display a context menu with a "Delete" option.
2. WHEN the user selects "Delete" from the context menu, THE Filter_ViewModel SHALL remove the Custom_Filter from the list of saved filters.
3. WHEN a Custom_Filter is deleted, THE sidebar SHALL immediately remove the corresponding Filter_Chip from the UI.
4. WHEN the deleted Custom_Filter was the currently active filter, THE Filter_ViewModel SHALL fall back to the "Detected" filter and clear the Search_Bar.
5. THE context menu SHALL not appear when right-clicking a built-in Filter_Chip (Detected, Installed, Favourites, Hidden, Unreal, Unity, Other, RenoDX, Luma).

### Requirement 6: Persist Custom Filters Across Sessions

**User Story:** As a user, I want my custom filters to be saved and restored when I reopen the application, so that I do not lose my saved searches.

#### Acceptance Criteria

1. WHEN a Custom_Filter is created or deleted, THE application SHALL persist the current list of Custom_Filters (each with its name and query) to the Settings_File.
2. WHEN the application starts, THE Filter_ViewModel SHALL load all previously saved Custom_Filters from the Settings_File and display them as Filter_Chip elements.
3. IF the Settings_File contains no Custom_Filter data or the data is malformed, THEN THE Filter_ViewModel SHALL initialize with an empty Custom_Filter list and log a warning without crashing.
4. FOR ALL valid Custom_Filter lists, saving to the Settings_File and then loading from the Settings_File SHALL produce an equivalent list of Custom_Filters (round-trip property).
