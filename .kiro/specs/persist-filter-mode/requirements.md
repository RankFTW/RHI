# Requirements Document

## Introduction

RDXC (RenoDX Commander) is a WinUI 3 desktop application that manages game mods. The game list can be filtered by mode (Detected, Favourites, Hidden, Installed, Unreal, Unity, Other, RenoDX, Luma) and by combinable engine/framework filters. Currently, the selected filter mode resets to "Detected" on every application launch. This feature persists the user's last selected filter mode so it is automatically restored when the application starts.

## Glossary

- **RDXC**: The RenoDX Commander desktop application.
- **Filter_Mode**: A string representing the active filter selection in the game list (e.g. "Detected", "Favourites", "Installed", or a comma-separated combination of combinable filters like "Unreal,RenoDX").
- **Settings_File**: The JSON dictionary file at `%LocalAppData%/RenoDXCommander/settings.json` used to persist all user preferences.
- **FilterViewModel**: The view model that owns filter state, search query, and filter count logic.
- **GameNameService**: The service responsible for loading and saving the Settings_File.
- **Exclusive_Filter**: A filter that clears all other filters when selected (Detected, Favourites, Hidden, Installed).
- **Combinable_Filter**: A filter that can be active alongside other combinable filters (Unreal, Unity, Other, RenoDX, Luma).

## Requirements

### Requirement 1: Save Filter Mode on Change

**User Story:** As a user, I want my selected filter mode to be saved whenever I change it, so that the application remembers my preference.

#### Acceptance Criteria

1. WHEN the user selects a new Filter_Mode, THE RDXC SHALL persist the Filter_Mode value to the Settings_File.
2. WHEN the user selects a combinable filter combination, THE RDXC SHALL persist the full comma-separated Filter_Mode string to the Settings_File.
3. THE RDXC SHALL store the Filter_Mode under a dedicated key named "FilterMode" in the Settings_File.

### Requirement 2: Restore Filter Mode on Startup

**User Story:** As a user, I want my previously selected filter mode to be restored when I launch the application, so that I do not have to reselect it every time.

#### Acceptance Criteria

1. WHEN the application starts and a persisted Filter_Mode value exists in the Settings_File, THE FilterViewModel SHALL restore the Filter_Mode to the persisted value.
2. WHEN the application starts and a persisted Filter_Mode value exists in the Settings_File, THE FilterViewModel SHALL populate the active filters set to match the persisted value.
3. WHEN the application starts and no persisted Filter_Mode value exists in the Settings_File, THE FilterViewModel SHALL default to "Detected".
4. WHEN the application starts and the persisted Filter_Mode value is empty or whitespace, THE FilterViewModel SHALL default to "Detected".

### Requirement 3: Validate Restored Filter Mode

**User Story:** As a user, I want the application to handle invalid persisted filter values gracefully, so that a corrupted settings file does not break the filter.

#### Acceptance Criteria

1. IF the persisted Filter_Mode contains a value that is not a recognized Exclusive_Filter or Combinable_Filter, THEN THE FilterViewModel SHALL fall back to "Detected".
2. IF the persisted Filter_Mode contains a mix of Exclusive_Filter and Combinable_Filter values, THEN THE FilterViewModel SHALL fall back to "Detected".

### Requirement 4: Round-Trip Persistence

**User Story:** As a user, I want the saved filter mode to survive a full save-and-load cycle without data loss, so that my preference is reliably preserved.

#### Acceptance Criteria

1. FOR ALL valid Filter_Mode values, saving the Filter_Mode to the Settings_File and then loading the Settings_File SHALL produce an equivalent Filter_Mode value (round-trip property).
2. FOR ALL valid combinable filter combinations, saving and then loading SHALL restore the same set of active filters regardless of the order the filters were originally combined.
