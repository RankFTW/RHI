# Requirements Document

## Introduction

Add per-addon "Info" buttons to every component row in the RHI application. Each addon (RE Framework, ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, Luma) gets a small "Info" button placed to the left of its install/reinstall button. The button opens a flyout/dialog showing contextual information resolved from a three-tier priority system: per-game manifest entries, wiki scraping (RenoDX and OptiScaler only), and generic fallback text. This feature applies to all three view modes (Detail, Grid/Card flyout, Compact). The existing header-level "ℹ" (DetailNotesBtn) and "💬" (DetailDiscussionBtn) buttons are removed, with their RenoDX wiki/notes functionality migrated to the new per-addon RenoDX Info button.

## Glossary

- **RHI**: ReShade HDR Installer — the WinUI 3 desktop application this feature is built into.
- **Info_Button**: A small button labeled "Info" placed in each component row, which opens a content flyout showing addon-specific information for the selected game.
- **Info_Resolver**: The service responsible for determining which content to display for a given game + addon combination, following the three-tier priority system.
- **Manifest**: The remote JSON configuration file (manifest.json) that provides per-game overrides, notes, and metadata.
- **Detail_View**: The primary game detail panel (DetailPanelBuilder.cs, DetailPanelBuilder.Components.cs) showing full component rows with install buttons.
- **Card_View**: The grid/card view (CardBuilder.cs, CardBuilder.Flyout.cs) showing game cards with install flyouts containing component rows.
- **Compact_View**: The compact view (CompactViewBuilder.cs) that reuses the Detail_View panel with page-based navigation.
- **Content_Dialog**: A WinUI 3 ContentDialog styled with dark theme and SurfaceToolbarBrush background, used to display info content.
- **Wiki_Scraper**: The existing WikiService that fetches and parses mod information from the RenoDX GitHub wiki.
- **OptiScaler_Wiki_Scraper**: A new scraping component that fetches compatibility information from the OptiScaler wiki, matched by game name.
- **Generic_Fallback**: Static placeholder text describing an addon, used when no per-game manifest entry or wiki content exists.
- **Addon_Info_Entry**: A new per-game manifest field that stores custom info text for a specific addon.
- **Luma**: An alternative HDR mod framework that provides native HDR output for supported games, shown as a component row when Luma mode is active.

## Requirements

### Requirement 1: Info Button Placement in Detail View

**User Story:** As a user, I want to see an Info button next to each addon's install button in the detail panel, so that I can quickly access addon-specific information for the selected game.

#### Acceptance Criteria

1. WHEN a game is displayed in the Detail_View, THE RHI SHALL render an Info_Button to the left of the install button in each component row (RE Framework, ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, Luma).
2. THE Info_Button SHALL display the text "Info" with a font size of 11 and padding of (6, 2, 6, 2).
3. THE Info_Button SHALL use the same visual style as existing small buttons in the application: SurfaceOverlayBrush background, TextSecondaryBrush foreground, BorderStrongBrush border, corner radius of 8, width of 36, and height of 32.
4. THE Info_Button SHALL be placed in a new column between the status text column and the install button column in each component row Grid.
5. WHEN the component row is collapsed or hidden, THE Info_Button SHALL also be hidden along with the rest of the row.

### Requirement 2: Info Button Placement in Card Flyout View

**User Story:** As a user, I want to see Info buttons in the card flyout component rows, so that I can access addon information from the grid view.

#### Acceptance Criteria

1. WHEN a game's install flyout is opened in the Card_View, THE CardBuilder SHALL render an Info_Button in each component row built by BuildComponentRow.
2. THE Info_Button in the Card_View SHALL be placed between the status text column and the install button column.
3. THE Info_Button in the Card_View SHALL use the same visual styling as the Detail_View Info_Button, scaled to match the card flyout's smaller dimensions (font size 11, padding (4, 2, 4, 2)).

### Requirement 3: Info Button Placement in Compact View

**User Story:** As a user, I want Info buttons to appear in compact view, so that the feature is consistent across all view modes.

#### Acceptance Criteria

1. WHEN a game is displayed in the Compact_View, THE RHI SHALL display Info_Buttons in the component rows, because the Compact_View reuses the Detail_View panel.
2. THE Info_Buttons in the Compact_View SHALL behave identically to the Detail_View Info_Buttons.

### Requirement 4: Info Content Resolution Priority

**User Story:** As a user, I want the Info button to show the most relevant information available, so that I get per-game custom notes when they exist, wiki-scraped content when available, or a generic addon description as a fallback.

#### Acceptance Criteria

1. WHEN an Info_Button is clicked, THE Info_Resolver SHALL check for a per-game Addon_Info_Entry in the Manifest for the selected game and addon combination.
2. WHEN a per-game Addon_Info_Entry exists in the Manifest, THE Info_Resolver SHALL return the manifest entry content as the highest priority source.
3. WHEN no per-game Addon_Info_Entry exists and the addon is RenoDX, THE Info_Resolver SHALL return content from the Wiki_Scraper (the existing RenoDX wiki notes and status information).
4. WHEN no per-game Addon_Info_Entry exists and the addon is OptiScaler, THE Info_Resolver SHALL return content from the OptiScaler_Wiki_Scraper matched by game name.
5. WHEN no per-game Addon_Info_Entry exists and no wiki content is available, THE Info_Resolver SHALL return the Generic_Fallback text for the addon.
6. THE Info_Resolver SHALL resolve content independently for each addon — one addon having manifest content SHALL NOT affect the resolution for other addons.
7. WHEN no per-game Addon_Info_Entry exists and the addon is Luma, THE Info_Resolver SHALL check for existing Luma notes from the lumaGameNotes manifest section and LumaMod wiki data before falling back to Generic_Fallback text.

### Requirement 5: Info Content Dialog Display

**User Story:** As a user, I want the info popup to match the existing dark-themed dialog style, so that the UI feels consistent.

#### Acceptance Criteria

1. WHEN an Info_Button is clicked, THE RHI SHALL display a Content_Dialog with the addon name and game name in the title.
2. THE Content_Dialog SHALL use SurfaceToolbarBrush as its background, ElementTheme.Dark as its requested theme, and a "Close" button to dismiss.
3. THE Content_Dialog SHALL contain a ScrollViewer with a maximum height of 440 pixels wrapping the info content.
4. WHEN the info content includes a URL (from manifest notesUrl or wiki link), THE Content_Dialog SHALL render the URL as a clickable hyperlink styled with AccentBlueBrush.
5. THE Content_Dialog text content SHALL use TextSecondaryBrush foreground, font size 13, and line height 22.

### Requirement 6: Manifest Schema for Per-Addon Info Entries

**User Story:** As a manifest maintainer, I want to add per-game info text for each addon, so that I can provide custom instructions or notes specific to a game and addon combination.

#### Acceptance Criteria

1. THE Manifest SHALL support new top-level dictionary sections for per-addon info entries: "reshadeGameInfo", "relimiterGameInfo", "displayCommanderGameInfo", "reframeworkGameInfo", "optiScalerGameInfo", and "lumaGameInfo".
2. EACH per-addon info section SHALL use the same schema as the existing GameNoteEntry model: a JSON object with "notes" (string), "notesUrl" (string, nullable), and "notesUrlLabel" (string, nullable) fields.
3. THE RemoteManifest model SHALL include new nullable dictionary properties for each addon info section, keyed by game name.
4. THE Manifest SHALL support a new "optiScalerWikiNames" dictionary section that maps game names (as detected by RHI) to the corresponding game name used in the OptiScaler wiki compatibility list.

### Requirement 7: OptiScaler Wiki Scraping

**User Story:** As a user, I want the OptiScaler Info button to show compatibility information from the OptiScaler wiki, so that I can see whether OptiScaler works with my game.

#### Acceptance Criteria

1. WHEN the OptiScaler Info_Button is clicked and no per-game Addon_Info_Entry exists, THE OptiScaler_Wiki_Scraper SHALL fetch compatibility data from the OptiScaler wiki Compatibility List page.
2. THE OptiScaler_Wiki_Scraper SHALL match the current game to a wiki entry using the game name, with an optional override from the "optiScalerWikiNames" manifest section.
3. WHEN a matching OptiScaler wiki entry is found, THE Info_Resolver SHALL display the compatibility status (working/not working), supported upscaler types (DLSS, FSR, XeSS), and any notes text from the wiki entry.
4. WHEN a matching OptiScaler wiki entry contains links to a dedicated game wiki page, THE Info_Resolver SHALL include those links as clickable hyperlinks in the Content_Dialog so the user can access detailed per-game instructions.
5. IF the OptiScaler_Wiki_Scraper fails to fetch or parse the wiki page, THEN THE Info_Resolver SHALL fall back to the Generic_Fallback text for OptiScaler.
6. THE OptiScaler_Wiki_Scraper SHALL cache fetched wiki data for the duration of the application session to avoid repeated network requests.

### Requirement 8: RenoDX Info Button Replaces Header Notes Button

**User Story:** As a user, I want the RenoDX-specific notes to appear in the RenoDX Info button instead of the header bar, so that information is organized per-addon rather than per-game.

#### Acceptance Criteria

1. THE RHI SHALL remove the DetailNotesBtn ("ℹ" button) from the game header bar in MainWindow.xaml.
2. THE RHI SHALL remove the DetailDiscussionBtn ("💬" button) from the game header bar in MainWindow.xaml.
3. WHEN the RenoDX Info_Button is clicked, THE Info_Resolver SHALL reuse the existing wiki notes and manifest gameNotes content that was previously shown by the DetailNotesBtn click handler (NotesButton_Click in DialogService.Game.cs).
4. THE RenoDX Info_Button Content_Dialog SHALL display the wiki status badge, wiki notes, and manifest gameNotes content in the same format as the previous NotesButton_Click dialog.
5. WHEN the game has a wiki discussion/instructions URL (NameUrl), THE RenoDX Info_Button Content_Dialog SHALL include the wiki page link as a clickable hyperlink within the dialog content, preserving the functionality previously provided by the DetailDiscussionBtn.
6. WHEN the game is in Luma mode, THE Luma Info_Button SHALL display the Luma-specific notes (LumaMod wiki data and lumaGameNotes manifest content) that were previously shown in the NotesButton_Click dialog under Luma mode.

### Requirement 9: Generic Fallback Text for Addons

**User Story:** As a user, I want to see a brief description of each addon when no game-specific information is available, so that I understand what the addon does.

#### Acceptance Criteria

1. THE Generic_Fallback for RE Framework SHALL describe RE Framework as a modding framework for RE Engine games that enables ReShade and other mods.
2. THE Generic_Fallback for ReShade SHALL describe ReShade as a post-processing injector that adds visual effects and is required by RenoDX.
3. THE Generic_Fallback for RenoDX SHALL describe RenoDX as an HDR mod framework that upgrades SDR games to HDR using ReShade.
4. THE Generic_Fallback for ReLimiter SHALL describe ReLimiter as a frame limiter that works alongside ReShade to reduce input lag and improve frame pacing.
5. THE Generic_Fallback for Display Commander SHALL describe Display Commander as a display management tool that works alongside ReShade to control HDR output settings.
6. THE Generic_Fallback for OptiScaler SHALL describe OptiScaler as an upscaling compatibility layer that enables FSR, XeSS, or DLSS across different GPU vendors.
7. THE Generic_Fallback for Luma SHALL describe Luma as an alternative HDR mod framework that provides native HDR output for supported games.
8. EACH Generic_Fallback text SHALL be a static string defined in the Info_Resolver, not fetched from any external source.

### Requirement 10: Info Button State Synchronization

**User Story:** As a user, I want the Info button to stay in sync with the component row state, so that disabled or struck-through rows also affect the Info button appropriately.

#### Acceptance Criteria

1. WHEN a component row is greyed out (opacity 0.35) due to mutual exclusion or missing prerequisites, THE Info_Button in that row SHALL remain clickable and at full opacity, because info content is useful regardless of install state.
2. WHEN a component row visibility changes due to PropertyChanged events, THE Info_Button SHALL update its visibility to match the row.
3. WHEN the install flyout in Card_View is open and a PropertyChanged event fires, THE Info_Button state SHALL be preserved during the component row update cycle.

### Requirement 11: Visual Differentiation for Info Buttons with Real Content

**User Story:** As a user, I want to visually distinguish Info buttons that have game-specific or wiki-scraped content from those that only show generic fallback text, so that I can quickly identify which addons have useful per-game information.

#### Acceptance Criteria

1. WHEN the Info_Resolver determines that an addon has per-game manifest content or wiki-scraped content for the current game, THE Info_Button SHALL use a highlighted visual style: AccentBlueBgBrush background, AccentBlueBrush foreground, and AccentBlueBorderBrush border.
2. WHEN the Info_Resolver determines that an addon only has Generic_Fallback text for the current game, THE Info_Button SHALL use the default muted visual style: SurfaceOverlayBrush background, TextSecondaryBrush foreground, and BorderStrongBrush border.
3. THE Info_Button visual style SHALL update when the game selection changes, reflecting the content availability for the newly selected game.
4. THE Info_Button visual style in the Card_View flyout SHALL follow the same differentiation rules as the Detail_View.
5. THE Info_Button visual style SHALL update when manifest data or wiki data is refreshed during the application session.

### Requirement 12: Tooltip Preview on Info Button Hover

**User Story:** As a user, I want to see a brief tooltip when hovering over an Info button, so that I can quickly tell what kind of content is available without opening the dialog.

#### Acceptance Criteria

1. WHEN the user hovers over an Info_Button that has per-game manifest content, THE RHI SHALL display a tooltip reading "Per-game notes available".
2. WHEN the user hovers over an Info_Button that has wiki-scraped content (RenoDX or OptiScaler), THE RHI SHALL display a tooltip reading "Wiki info available".
3. WHEN the user hovers over an Info_Button that only has Generic_Fallback content, THE RHI SHALL display a tooltip reading "General addon info".
4. THE tooltip text SHALL reflect the highest-priority content source for the current game and addon combination.

### Requirement 13: OptiScaler Wiki Data Fetched at Startup

**User Story:** As a user, I want OptiScaler wiki data to be available immediately when I open a game, so that Info button styling is correct from the start without waiting for a lazy fetch.

#### Acceptance Criteria

1. WHEN the application starts and fetches the RenoDX wiki data, THE RHI SHALL also fetch the OptiScaler Compatibility List wiki page in parallel.
2. WHEN the application starts, THE RHI SHALL also fetch the OptiScaler FSR4 Compatibility List wiki page in parallel.
3. THE OptiScaler_Wiki_Scraper SHALL merge data from both the standard Compatibility List and the FSR4 Compatibility List into a single cached dataset per game.
4. IF the OptiScaler wiki fetch fails at startup, THEN THE RHI SHALL log the failure and continue without OptiScaler wiki data, falling back to Generic_Fallback text for all OptiScaler Info_Buttons.
5. THE fetched OptiScaler wiki data SHALL be used immediately to determine Info_Button visual styling (highlighted vs muted) for all games.

### Requirement 14: Greyed-Out Info Button When No Content Available

**User Story:** As a user, I want Info buttons to be greyed out and unclickable when there is no content available for that addon, so that I don't waste time clicking buttons with nothing to show.

#### Acceptance Criteria

1. WHEN the Info_Resolver determines that an addon has no content at all for the current game (no manifest entry, no wiki data, and no Generic_Fallback text), THE Info_Button SHALL be rendered with opacity 0.3 and IsHitTestVisible set to false.
2. WHEN the Info_Resolver determines that an addon has at least Generic_Fallback content, THE Info_Button SHALL remain clickable at full opacity.
3. THE greyed-out state SHALL apply to addons whose component row is visible but whose Info_Button has no content to display.

### Requirement 15: FSR4 Compatibility List Scraping

**User Story:** As a user, I want the OptiScaler Info button to include FSR4 compatibility information, so that I get a complete picture of OptiScaler support for my game.

#### Acceptance Criteria

1. THE OptiScaler_Wiki_Scraper SHALL fetch and parse the FSR4 Compatibility List page from the OptiScaler wiki in addition to the standard Compatibility List.
2. WHEN a game appears in both the standard Compatibility List and the FSR4 Compatibility List, THE Info_Resolver SHALL display information from both lists in the Content_Dialog, clearly labeled as "OptiScaler Compatibility" and "FSR4 Compatibility" sections.
3. WHEN a game appears only in the FSR4 Compatibility List, THE Info_Resolver SHALL display the FSR4 data and note that no standard compatibility entry was found.
4. THE FSR4 Compatibility List data SHALL follow the same caching and game-name matching rules as the standard Compatibility List.
