# Requirements Document

## Introduction

The RHI application (ReShade HDR Installer) currently displays a centered ProgressRing spinner with "Loading..." text during initialization, which takes 10–30 seconds. The entire GameViewPanel (sidebar + detail panel) remains Collapsed until loading finishes, then appears all at once. This creates a jarring experience and makes the wait feel longer than necessary.

This feature replaces the spinner-based loading screen with a skeleton loading screen — a well-established UX pattern where placeholder shapes mimicking the real layout are shown with an animated pulsing effect. The sidebar structure, game list rows, and detail panel placeholders are visible immediately on launch, reducing perceived load time and giving users confidence the application is working.

## Glossary

- **Skeleton_Screen**: A placeholder UI that mimics the layout of real content using animated grey shapes while data loads in the background.
- **Shimmer_Animation**: A repeating color animation that pulses skeleton placeholder elements between two grey tones to indicate loading activity.
- **Skeleton_Row**: A single placeholder element in the sidebar game list that mimics the dimensions and layout of a real GameCardViewModel list item (icon area + text area + badge area).
- **Skeleton_Detail_Panel**: A set of placeholder shapes in the detail panel area that mimic the header, badge row, and component table of the real detail view.
- **GameViewPanel**: The main content Grid in MainWindow.xaml containing the sidebar (Column 0, 300px) and detail panel (Column 1).
- **LoadingPanel**: The existing centered StackPanel overlay containing the ProgressRing and status text, currently shown during initialization.
- **Sidebar**: The left column (300px) of GameViewPanel containing the search box, filter chips, game count area, and game ListView.
- **Detail_Panel**: The right column of GameViewPanel containing game header, badges, and component rows, built by DetailPanelBuilder.
- **Status_Bar**: The bottom row of the main layout Grid that displays StatusText and SubStatusText during all application states.

## Requirements

### Requirement 1: Show Skeleton Layout on Launch

**User Story:** As a user, I want to see the application layout structure immediately on launch, so that I perceive the app as responsive rather than staring at a blank spinner.

#### Acceptance Criteria

1. WHEN the application launches and IsLoading is true, THE Skeleton_Screen SHALL display the GameViewPanel structure with skeleton placeholders instead of the LoadingPanel spinner.
2. THE Skeleton_Screen SHALL display the Sidebar at its full 300px width with the real search box, filter chips, and game count area visible in their normal positions.
3. WHEN IsLoading is true, THE Skeleton_Screen SHALL display Skeleton_Row placeholders in the sidebar game list area to represent game entries that have not yet loaded.
4. WHEN IsLoading is true, THE Skeleton_Screen SHALL display Skeleton_Detail_Panel placeholders in the detail panel area to represent the header, badge row, and component table.

### Requirement 2: Skeleton Row Appearance

**User Story:** As a user, I want the skeleton rows to resemble the real game list items, so that the loading state feels like a preview of the actual content.

#### Acceptance Criteria

1. THE Skeleton_Row SHALL contain a rectangular placeholder matching the 14x14 pixel source icon area, a wider rectangular placeholder matching the game name text area, and a circular placeholder matching the 8px update badge area.
2. THE Skeleton_Row SHALL use the same padding (8,6), spacing (6px column spacing), and corner radius (6) as the real game card DataTemplate.
3. THE Skeleton_Screen SHALL display between 8 and 15 Skeleton_Row elements in the sidebar list area to fill the visible space.

### Requirement 3: Skeleton Detail Panel Appearance

**User Story:** As a user, I want the detail panel area to show placeholder shapes during loading, so that the right side of the screen does not appear empty.

#### Acceptance Criteria

1. THE Skeleton_Detail_Panel SHALL contain a wide rectangular placeholder representing the game name header area.
2. THE Skeleton_Detail_Panel SHALL contain a row of smaller rectangular placeholders representing the badge area (source, engine, bitness badges).
3. THE Skeleton_Detail_Panel SHALL contain rectangular placeholders representing at least two component table rows (matching the 5-column grid layout of the real component rows).
4. THE Skeleton_Detail_Panel SHALL use spacing and padding consistent with the real detail panel layout (24px horizontal padding, 16px vertical spacing between sections).

### Requirement 4: Shimmer Animation

**User Story:** As a user, I want the skeleton placeholders to pulse with a subtle animation, so that I can tell the application is actively loading and not frozen.

#### Acceptance Criteria

1. THE Shimmer_Animation SHALL animate the fill color of all skeleton placeholder elements between two dark grey tones using a WinUI 3 Storyboard with ColorAnimation.
2. THE Shimmer_Animation SHALL use AutoReverse set to true and RepeatBehavior set to Forever so the pulsing continues until loading completes.
3. THE Shimmer_Animation SHALL complete one full pulse cycle (dark to light and back) within 1.0 to 2.0 seconds.
4. THE Shimmer_Animation SHALL use colors consistent with the application dark theme (within the range of the existing surface brush palette, e.g., #1E242C to #2A3040).

### Requirement 5: Transition from Skeleton to Real Content

**User Story:** As a user, I want the skeleton placeholders to be replaced by real content when loading finishes, so that the transition feels smooth and natural.

#### Acceptance Criteria

1. WHEN IsLoading transitions from true to false for the first time (initial boot), THE Skeleton_Screen SHALL remove all Skeleton_Row elements from the sidebar list area.
2. WHEN IsLoading transitions from true to false for the first time, THE Skeleton_Screen SHALL remove all Skeleton_Detail_Panel placeholder elements from the detail panel area.
3. WHEN IsLoading transitions from true to false for the first time, THE Shimmer_Animation SHALL stop and release its Storyboard resources.
4. WHEN IsLoading transitions from true to false for the first time, THE GameViewPanel SHALL display the real game ListView and detail panel with actual data.
5. WHILE the application has already completed initial loading (HasInitialized is true), THE Skeleton_Screen SHALL remain hidden during subsequent refresh operations.

### Requirement 6: Status Text Visibility During Skeleton Loading

**User Story:** As a user, I want to see loading phase updates (e.g., "Scanning game library...", "Matching mods...") while the skeleton screen is displayed, so that I know what the application is doing.

#### Acceptance Criteria

1. WHILE IsLoading is true and the Skeleton_Screen is displayed, THE Status_Bar SHALL display the current StatusText and SubStatusText values.
2. WHEN StatusText or SubStatusText changes during loading, THE Status_Bar SHALL update its displayed text within the same dispatcher cycle.
3. THE Skeleton_Screen SHALL NOT display the ProgressRing spinner or the centered LoadingTitle and LoadingSubtitle TextBlocks from the old LoadingPanel.

### Requirement 7: Non-Data Controls During Skeleton State

**User Story:** As a user, I want the search box, filter chips, and count area to be visible during loading, so that the UI feels complete even before data arrives.

#### Acceptance Criteria

1. WHILE IsLoading is true and the Skeleton_Screen is displayed, THE Sidebar SHALL display the search box in its normal position and appearance.
2. WHILE IsLoading is true and the Skeleton_Screen is displayed, THE Sidebar SHALL display the filter chip buttons in their normal positions and appearance.
3. WHILE IsLoading is true and the Skeleton_Screen is displayed, THE Sidebar SHALL display the game count area (with "0 shown" and "0 installed" default text).
4. WHILE IsLoading is true, THE search box and filter chip buttons SHALL remain visually present but non-functional (input is accepted but filtering produces no results until data loads).
