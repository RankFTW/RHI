# Requirements Document

## Introduction

The RHI (ReShade HDR Installer) Settings page currently combines actual application settings with informational "About" content (app description, credits, disclaimers, and links). This feature separates those concerns by moving all About-related content to a dedicated About page, accessible from a new "About" menu item in the Help flyout. The Settings page retains only actionable configuration sections.

## Glossary

- **Settings_Page**: The existing panel (`SettingsPanel`) in `MainWindow.xaml` that displays application configuration options. After this feature, it contains only actionable settings sections (Add Game, Custom Shaders, Screenshots, Full Refresh, Addon Watch Folder, Preferences, Crash & Error Logs).
- **About_Page**: A new page/panel that displays read-only informational content about the application: version info, description, disclaimers, credits & acknowledgements, and external links.
- **Help_Flyout**: The `MenuFlyout` attached to the "Help" toolbar button (`SupportBtn`), currently containing Discord, Guide, and Ko-fi menu items.
- **AppPage_Enum**: The `AppPage` enum in `Models/AppPage.cs` that tracks which top-level page is currently displayed (currently `GameView` and `Settings`).
- **Toolbar**: The top-level action bar (Row 0 of the main grid) containing Refresh, Global Shaders, Update, Help, Layout Toggle, and Settings buttons.

## Requirements

### Requirement 1: Add About value to AppPage enum

**User Story:** As a developer, I want the navigation model to include an About page state, so that page visibility logic can distinguish between GameView, Settings, and About.

#### Acceptance Criteria

1. THE AppPage_Enum SHALL include a value named `About` in addition to the existing `GameView` and `Settings` values.

### Requirement 2: Create the About page panel

**User Story:** As a user, I want a dedicated About page that shows app information, credits, and links, so that I can find this content without scrolling through settings.

#### Acceptance Criteria

1. THE About_Page SHALL display the application icon, name ("ReShade HDR Installer"), and version text (currently bound to `AboutVersionText`).
2. THE About_Page SHALL display the application description text ("A desktop manager for HDR game mods on Windows...").
3. THE About_Page SHALL display the "What RHI does" informational block.
4. THE About_Page SHALL display the "Disclaimer" block (unofficial third-party tool notice).
5. THE About_Page SHALL display the "Single-player only" warning block.
6. THE About_Page SHALL display the full "Credits & Acknowledgements" section with all current entries (RenoDX, ReShade, Luma Framework, 7-Zip, HtmlAgilityPack, CommunityToolkit.Mvvm, UI Design by Lazorr, RHI by RankFTW).
7. THE About_Page SHALL display the "Links" section with all current external hyperlinks.
8. THE About_Page SHALL include a "← Back to Games" button that navigates back to the GameView page.
9. THE About_Page SHALL have a scrollable layout consistent with the existing Settings_Page styling (same padding, max width, card-style borders).

### Requirement 3: Add About menu item to the Help flyout

**User Story:** As a user, I want to access the About page from the Help menu, so that I can find app information alongside other help resources.

#### Acceptance Criteria

1. THE Help_Flyout SHALL contain an "About" menu item in addition to the existing Discord, Guide, and Ko-fi items.
2. WHEN the user clicks the "About" menu item, THE Help_Flyout SHALL navigate to the About_Page.
3. THE Help_Flyout SHALL display the "About" menu item as the last item in the flyout menu.

### Requirement 4: Remove About content from the Settings page

**User Story:** As a user, I want the Settings page to contain only actionable settings, so that I can configure the app without scrolling past informational content.

#### Acceptance Criteria

1. THE Settings_Page SHALL retain the following sections: Add Game, Custom Shaders, Screenshots, Full Refresh, Addon Watch Folder, Preferences, and Crash & Error Logs.
2. THE Settings_Page SHALL NOT display the "About" section (app icon, name, version, description, disclaimers).
3. THE Settings_Page SHALL NOT display the "Credits & Acknowledgements" section.
4. THE Settings_Page SHALL NOT display the "Links" section.

### Requirement 5: Update page visibility logic

**User Story:** As a developer, I want the page visibility system to handle three pages, so that only one page is visible at a time.

#### Acceptance Criteria

1. WHEN the current page is set to `About`, THE About_Page SHALL be visible and both the GameView panel and Settings_Page SHALL be collapsed.
2. WHEN the current page is set to `Settings`, THE Settings_Page SHALL be visible and both the GameView panel and About_Page SHALL be collapsed.
3. WHEN the current page is set to `GameView`, THE GameView panel SHALL be visible and both the Settings_Page and About_Page SHALL be collapsed.

### Requirement 6: Patch Notes link accessibility

**User Story:** As a user, I want to still be able to view patch notes, so that the About page separation does not remove any existing functionality.

#### Acceptance Criteria

1. IF the Settings_Page previously contained a Patch Notes link, THEN THE About_Page SHALL include the Patch Notes link in an appropriate location.
