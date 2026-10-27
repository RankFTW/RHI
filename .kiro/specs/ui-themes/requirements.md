# Requirements Document

## Introduction

Add a "Themes" section to the RenoDXCommander settings panel that allows users to change the visual theme of the application. The section is positioned between the existing Preferences section and the Crash & Error Logs section. The app ships with a "Default" theme (the current dark theme) and supports additional themes defined as XAML ResourceDictionary files. The selected theme persists across application restarts via the existing JSON settings file.

## Glossary

- **Settings_Panel**: The scrollable settings view in MainWindow, toggled by the Settings toolbar button.
- **Themes_Section**: A new UI section within the Settings_Panel that displays available themes and allows the user to select one.
- **Theme**: A XAML ResourceDictionary file located in the `Themes/` folder that defines the full set of color and brush resources used by the application.
- **Default_Theme**: The built-in dark theme currently defined in `Themes/DarkTheme.xaml`, used when no other theme is selected.
- **Theme_Selector**: A ComboBox or equivalent control within the Themes_Section that lists all available themes.
- **Settings_File**: The JSON file at `%LocalAppData%\RenoDXCommander\settings.json` used to persist user preferences.
- **SettingsViewModel**: The view model responsible for loading and saving user settings to the Settings_File.

## Requirements

### Requirement 1: Themes Section Placement

**User Story:** As a user, I want to see a Themes section in the settings panel between Preferences and Crash Logs, so that I can find theme options in a logical location.

#### Acceptance Criteria

1. THE Settings_Panel SHALL display the Themes_Section after the Preferences section and before the Crash & Error Logs section.
2. THE Themes_Section SHALL display a heading with the text "Themes".
3. THE Themes_Section SHALL use the same visual card style (background, corner radius, border, padding) as other settings sections.

### Requirement 2: Theme Selection Control

**User Story:** As a user, I want to select a theme from a list of available options, so that I can change the visual appearance of the application.

#### Acceptance Criteria

1. THE Themes_Section SHALL display a Theme_Selector control listing all available themes.
2. THE Theme_Selector SHALL include "Default" as the first option in the list.
3. WHEN the user selects a theme from the Theme_Selector, THE Settings_Panel SHALL apply the selected Theme to the application immediately.
4. THE Theme_Selector SHALL display the currently active theme as the selected item when the Settings_Panel is opened.

### Requirement 3: Default Theme

**User Story:** As a user, I want the application to use the "Default" theme on first launch, so that I have a consistent initial experience.

#### Acceptance Criteria

1. WHEN no theme preference exists in the Settings_File, THE SettingsViewModel SHALL use "Default" as the active theme.
2. THE Default_Theme SHALL correspond to the existing `Themes/DarkTheme.xaml` resource dictionary.

### Requirement 4: Theme Persistence

**User Story:** As a user, I want my theme selection to be remembered across restarts, so that I do not have to reselect my preferred theme every time I launch the application.

#### Acceptance Criteria

1. WHEN the user selects a theme, THE SettingsViewModel SHALL save the theme name to the Settings_File.
2. WHEN the application starts, THE SettingsViewModel SHALL load the persisted theme name from the Settings_File.
3. WHEN the application starts with a persisted theme name, THE application SHALL apply the corresponding Theme before displaying the main window.
4. IF the persisted theme name does not match any available Theme file, THEN THE application SHALL fall back to the Default_Theme and update the Settings_File accordingly.

### Requirement 5: Theme Application at Runtime

**User Story:** As a user, I want the theme to take effect immediately when I select it, so that I can preview themes without restarting the application.

#### Acceptance Criteria

1. WHEN a new theme is selected, THE application SHALL replace the current theme ResourceDictionary in the merged dictionaries with the selected Theme ResourceDictionary.
2. WHEN a new theme is applied, THE application SHALL update all visible UI elements to reflect the new theme colors without requiring a restart.

### Requirement 6: Theme Discovery

**User Story:** As a user, I want the app to automatically find available themes, so that I can add new themes by placing files in the Themes folder.

#### Acceptance Criteria

1. THE application SHALL discover available themes by scanning for `*.xaml` ResourceDictionary files in the `Themes/` folder.
2. THE application SHALL derive the display name for each theme from the file name (e.g., `DarkTheme.xaml` maps to "Default", additional files use the filename without extension and "Theme" suffix).
3. WHEN a Theme file fails to load or parse, THE application SHALL skip that theme and log the error via the existing logging mechanism.
