# Requirements Document

## Introduction

Add a third view mode called "Compact" to the RHI (ReShade HDR Installer) WinUI 3 application. Currently the app supports two view modes (Detail and Grid) toggled by a toolbar button. The Compact view provides a space-efficient layout with the game list sidebar on the left and a paged content area on the right that cycles through three pages: Game Card, Overrides, and Management. The window is locked to a fixed size while in Compact mode and returns to free resizing when switching back to Detail or Grid.

## Glossary

- **RHI_App**: The ReShade HDR Installer WinUI 3 desktop application (MainWindow and its associated view models and builders)
- **View_Mode**: One of three layout modes for the main content area: Detail, Grid, or Compact
- **Layout_Toggle_Button**: The toolbar button that cycles through available view modes
- **Compact_View**: The third view mode featuring a sidebar game list and a paged content area with navigation arrows
- **Compact_Page**: One of three content pages displayed in the Compact view content area (Game_Card_Page, Overrides_Page, Management_Page)
- **Game_Card_Page**: Compact page 1 — displays the game info card with action buttons, badges, and component rows (ReShade, RenoDX, etc.)
- **Overrides_Page**: Compact page 2 — displays per-game overrides (game name, wiki name, DLL overrides, bitness, graphics API, shaders, addons)
- **Management_Page**: Compact page 3 — displays management actions (Change install folder, Reset folder/Remove game, Reset Overrides, Copy Report)
- **Navigation_Arrows**: Left and right arrow buttons on the sides of the Compact view content area that cycle through Compact pages
- **MainViewModel**: The primary view model that holds layout state, selected game, and settings persistence
- **WindowStateManager**: The Win32 interop class that enforces minimum window size and manages window bounds persistence
- **DetailPanelBuilder**: The helper class that constructs the detail panel, overrides panel, and management panel content

## Requirements

### Requirement 1: View Mode Cycling

**User Story:** As a user, I want the layout toggle button to cycle through three view modes (Detail → Grid → Compact → Detail), so that I can choose the most suitable layout for my workflow.

#### Acceptance Criteria

1. WHEN the Layout_Toggle_Button is clicked while in Detail View_Mode, THE RHI_App SHALL switch to Grid View_Mode
2. WHEN the Layout_Toggle_Button is clicked while in Grid View_Mode, THE RHI_App SHALL switch to Compact View_Mode
3. WHEN the Layout_Toggle_Button is clicked while in Compact View_Mode, THE RHI_App SHALL switch to Detail View_Mode
4. WHILE in Detail View_Mode, THE Layout_Toggle_Button SHALL display the label "Grid View"
5. WHILE in Grid View_Mode, THE Layout_Toggle_Button SHALL display the label "Compact View"
6. WHILE in Compact View_Mode, THE Layout_Toggle_Button SHALL display the label "Detail View"
7. WHEN the View_Mode changes, THE MainViewModel SHALL persist the selected View_Mode to settings

### Requirement 2: View Mode State Model

**User Story:** As a developer, I want the view mode represented as an enum rather than a boolean, so that the three-state layout is modeled correctly and extensible.

#### Acceptance Criteria

1. THE MainViewModel SHALL represent the current layout using a ViewLayout enum with values Detail, Grid, and Compact
2. WHEN the ViewLayout property changes, THE MainViewModel SHALL raise property-changed notifications for all layout-dependent visibility properties
3. THE MainViewModel SHALL expose a DetailPanelVisibility property that returns Visible only when ViewLayout is Detail
4. THE MainViewModel SHALL expose a CardGridVisibility property that returns Visible only when ViewLayout is Grid
5. THE MainViewModel SHALL expose a CompactViewVisibility property that returns Visible only when ViewLayout is Compact

### Requirement 3: Compact View Layout

**User Story:** As a user, I want the Compact view to show the game list sidebar on the left and a paged content area on the right, so that I can browse games and view their details in a compact layout.

#### Acceptance Criteria

1. WHILE in Compact View_Mode, THE RHI_App SHALL display the sidebar (game list, search box, filter chips, counts) in the left column identically to Detail View_Mode
2. WHILE in Compact View_Mode, THE RHI_App SHALL display the Compact content area in the right column
3. WHILE in Compact View_Mode, THE RHI_App SHALL display the toolbar row (Row 0) with all existing buttons including the Layout_Toggle_Button
4. WHEN a game is selected in the sidebar while in Compact View_Mode, THE RHI_App SHALL populate the current Compact_Page with data from the selected game
5. WHILE no game is selected in the sidebar, THE RHI_App SHALL hide the Compact content area

### Requirement 4: Compact Page Navigation

**User Story:** As a user, I want to cycle through the three Compact pages using left/right arrow buttons, so that I can view Game Card, Overrides, and Management information without switching views.

#### Acceptance Criteria

1. WHILE in Compact View_Mode, THE RHI_App SHALL display a left Navigation_Arrow on the left side of the content area
2. WHILE in Compact View_Mode, THE RHI_App SHALL display a right Navigation_Arrow on the right side of the content area
3. WHEN the right Navigation_Arrow is clicked while on Game_Card_Page, THE RHI_App SHALL navigate to Overrides_Page
4. WHEN the right Navigation_Arrow is clicked while on Overrides_Page, THE RHI_App SHALL navigate to Management_Page
5. WHEN the right Navigation_Arrow is clicked while on Management_Page, THE RHI_App SHALL navigate to Game_Card_Page
6. WHEN the left Navigation_Arrow is clicked while on Game_Card_Page, THE RHI_App SHALL navigate to Management_Page
7. WHEN the left Navigation_Arrow is clicked while on Overrides_Page, THE RHI_App SHALL navigate to Game_Card_Page
8. WHEN the left Navigation_Arrow is clicked while on Management_Page, THE RHI_App SHALL navigate to Overrides_Page
9. THE Navigation_Arrows SHALL remain visible and enabled at all times while a game is selected in Compact View_Mode

### Requirement 5: Game Card Page Content

**User Story:** As a user, I want the Game Card page in Compact view to show the same game information card as the Detail view, so that I can see game status, badges, and component rows.

#### Acceptance Criteria

1. WHILE on Game_Card_Page, THE RHI_App SHALL display the game name and install path header
2. WHILE on Game_Card_Page, THE RHI_App SHALL display the action buttons row (Nexus, PCGW, Hide, Favourite)
3. WHILE on Game_Card_Page, THE RHI_App SHALL display all info badges (installed file, generic mod, source, engine, graphics API, bitness, wiki status)
4. WHILE on Game_Card_Page, THE RHI_App SHALL display the component rows (ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, RE Framework) with their install/update/remove buttons
5. WHILE on Game_Card_Page, THE RHI_App SHALL display author badges when available
6. WHEN a game property changes (install status, update availability), THE Game_Card_Page SHALL reflect the updated state

### Requirement 6: Overrides Page Content

**User Story:** As a user, I want the Overrides page in Compact view to show all per-game override settings, so that I can configure game-specific options without switching to Detail view.

#### Acceptance Criteria

1. WHILE on Overrides_Page, THE RHI_App SHALL display the editable game name field
2. WHILE on Overrides_Page, THE RHI_App SHALL display the wiki mod name field
3. WHILE on Overrides_Page, THE RHI_App SHALL display DLL override toggles for all supported DLLs (dxgi.dll, d3d9.dll, d3d11.dll, d3d12.dll, ddraw.dll, hid.dll, version.dll, opengl32.dll, dbghelp.dll, vulkan-1.dll, winmm.dll)
4. WHILE on Overrides_Page, THE RHI_App SHALL display the bitness override selector (Auto, 32-bit, 64-bit)
5. WHILE on Overrides_Page, THE RHI_App SHALL display the graphics API override options
6. WHILE on Overrides_Page, THE RHI_App SHALL display shader mode and addon configuration options
7. WHEN an override value is changed on Overrides_Page, THE RHI_App SHALL persist the change to settings

### Requirement 7: Management Page Content

**User Story:** As a user, I want the Management page in Compact view to show game management actions, so that I can manage game folders and reset settings.

#### Acceptance Criteria

1. WHILE on Management_Page, THE RHI_App SHALL display a "Change install folder" button
2. WHILE on Management_Page, THE RHI_App SHALL display a "Reset folder / Remove game" button
3. WHILE on Management_Page, THE RHI_App SHALL display a "Reset Overrides" button
4. WHILE on Management_Page, THE RHI_App SHALL display a "Copy Report" button
5. WHEN the "Change install folder" button is clicked, THE RHI_App SHALL open a folder picker dialog for the selected game
6. WHEN the "Reset folder / Remove game" button is clicked, THE RHI_App SHALL execute the existing remove/reset game logic for the selected game
7. WHEN the "Reset Overrides" button is clicked, THE RHI_App SHALL reset all per-game overrides for the selected game to defaults
8. WHEN the "Copy Report" button is clicked, THE RHI_App SHALL generate and copy the game report to the clipboard

### Requirement 8: Compact View Window Size Locking

**User Story:** As a user, I want the window to be locked to a fixed size while in Compact view, so that the layout remains stable when cycling between pages.

#### Acceptance Criteria

1. WHEN switching to Compact View_Mode, THE WindowStateManager SHALL capture the current window bounds before resizing
2. WHEN switching to Compact View_Mode, THE RHI_App SHALL resize the window to a fixed size that accommodates the largest of the three Compact pages
3. WHILE in Compact View_Mode, THE WindowStateManager SHALL prevent the user from resizing the window by disabling resize handles
4. WHEN switching from Compact View_Mode to Detail or Grid View_Mode, THE WindowStateManager SHALL restore the previously captured window bounds
5. WHEN switching from Compact View_Mode to Detail or Grid View_Mode, THE WindowStateManager SHALL re-enable free window resizing

### Requirement 9: Compact View Persistence

**User Story:** As a user, I want my view mode preference to be remembered across app restarts, so that the app opens in the same view mode I last used.

#### Acceptance Criteria

1. WHEN the app starts, THE MainViewModel SHALL restore the previously saved View_Mode from persisted settings
2. IF the persisted View_Mode is Compact, THEN THE RHI_App SHALL apply Compact View_Mode layout and window size locking on startup
3. WHEN the View_Mode changes, THE MainViewModel SHALL persist the new View_Mode value to the settings file

### Requirement 10: Game Selection Synchronization

**User Story:** As a user, I want game selection in the sidebar to update the Compact view content, so that I can browse different games while staying in Compact view.

#### Acceptance Criteria

1. WHEN a different game is selected in the sidebar while in Compact View_Mode, THE RHI_App SHALL update the currently displayed Compact_Page with the newly selected game's data
2. WHEN a different game is selected while on Overrides_Page, THE RHI_App SHALL rebuild the overrides content for the newly selected game
3. WHEN a different game is selected while on Management_Page, THE RHI_App SHALL rebind the management buttons to the newly selected game
4. THE RHI_App SHALL retain the current Compact_Page index when switching between games (selecting a new game does not reset to Game_Card_Page)
