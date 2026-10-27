# Requirements Document

## Introduction

This feature adds a "Screenshots" section to the RHI Settings page that lets users configure a global screenshot save path for all managed reshade.ini files. Users type a directory path into a text box and click Apply to write a `[SCREENSHOT]` section with `SavePath=<directory>` into every deployed reshade.ini. An optional toggle appends the game name (as detected by RHI) as a subfolder, giving each game its own screenshot directory.

## Glossary

- **RHI**: ReShade HDR Installer — the WinUI 3 desktop application that manages ReShade, RenoDX, and Luma mods for detected games.
- **Settings_Page**: The settings panel in RHI, toggled via the Settings button in the toolbar.
- **Screenshots_Section**: A new UI section within the Settings_Page for configuring the screenshot save path.
- **Screenshot_Path_TextBox**: A text input field where the user types an absolute directory path (e.g. `D:\Screenshots`).
- **Per_Game_Folder_Toggle**: A toggle switch that, when enabled, appends the game name as a subfolder to the screenshot path.
- **Apply_Button**: A button that triggers writing the configured screenshot path into all managed reshade.ini files.
- **Managed_Ini**: A reshade.ini file located in a game's install directory that RHI has previously deployed or merged via `AuxInstallService.MergeRsIni` or `AuxInstallService.MergeRsVulkanIni`.
- **Game_Name**: The display name of a game as resolved by RHI (the `GameName` property on `GameCardViewModel`), which may reflect user renames or manifest overrides.
- **SettingsViewModel**: The view model that owns settings persistence (load/save to `settings.json`).
- **AuxInstallService**: The service responsible for deploying and merging reshade.ini files into game directories.

## Requirements

### Requirement 1: Screenshots Section in Settings Page

**User Story:** As a user, I want a dedicated Screenshots section on the Settings page, so that I can configure where ReShade saves screenshots.

#### Acceptance Criteria

1. WHEN the user navigates to the Settings_Page, THE Screenshots_Section SHALL be visible as a distinct bordered section containing the Screenshot_Path_TextBox, the Per_Game_Folder_Toggle, and the Apply_Button.
2. THE Screenshots_Section SHALL display a heading with the text "Screenshots".
3. THE Screenshots_Section SHALL display a brief description explaining that the configured path will be written to all managed reshade.ini files.

### Requirement 2: Screenshot Path Text Input

**User Story:** As a user, I want to type a directory path into a text box, so that I can specify where screenshots are saved.

#### Acceptance Criteria

1. THE Screenshot_Path_TextBox SHALL accept free-text input representing an absolute directory path.
2. THE Screenshot_Path_TextBox SHALL display placeholder text indicating the expected format (e.g. `D:\Screenshots`).
3. WHEN the Settings_Page is opened, THE Screenshot_Path_TextBox SHALL display the previously saved screenshot path from SettingsViewModel.

### Requirement 3: Per-Game Folder Toggle

**User Story:** As a user, I want a toggle to give each game its own screenshot subfolder, so that screenshots are organized by game.

#### Acceptance Criteria

1. THE Per_Game_Folder_Toggle SHALL default to off.
2. WHEN the Settings_Page is opened, THE Per_Game_Folder_Toggle SHALL reflect the previously saved toggle state from SettingsViewModel.
3. THE Per_Game_Folder_Toggle SHALL display descriptive on/off content labels (e.g. "Enabled — each game gets its own subfolder" / "Disabled — all screenshots in one folder").

### Requirement 4: Apply Button Writes Screenshot Path to All Managed INI Files

**User Story:** As a user, I want to click Apply to push my screenshot path into all managed reshade.ini files, so that every game uses the configured screenshot directory.

#### Acceptance Criteria

1. WHEN the user clicks the Apply_Button and the Screenshot_Path_TextBox contains a non-empty path, THE RHI SHALL iterate over all game cards that have a non-empty InstallPath and a reshade.ini file present in that directory.
2. WHEN the Per_Game_Folder_Toggle is off, THE RHI SHALL set the `[SCREENSHOT]` section's `SavePath` key to the exact text entered in the Screenshot_Path_TextBox for each Managed_Ini.
3. WHEN the Per_Game_Folder_Toggle is on, THE RHI SHALL set the `[SCREENSHOT]` section's `SavePath` key to the Screenshot_Path_TextBox value followed by a backslash and the Game_Name for each Managed_Ini.
4. WHEN the user clicks the Apply_Button and the Screenshot_Path_TextBox is empty, THE RHI SHALL take no action on any Managed_Ini and SHALL not modify any reshade.ini file.
5. WHEN the Per_Game_Folder_Toggle is on and the Screenshot_Path_TextBox is empty, THE RHI SHALL take no action on any Managed_Ini.
6. THE RHI SHALL use the existing INI parse/merge/write logic in AuxInstallService (ParseIni/WriteIni) to modify only the `[SCREENSHOT]` section without altering other sections or keys in each Managed_Ini.
7. WHEN the Apply_Button operation completes successfully, THE RHI SHALL display a confirmation message indicating how many reshade.ini files were updated.

### Requirement 5: Settings Persistence

**User Story:** As a user, I want my screenshot path and per-game toggle state to be remembered between sessions, so that I do not have to re-enter them.

#### Acceptance Criteria

1. WHEN the user clicks the Apply_Button with a non-empty path, THE SettingsViewModel SHALL persist the screenshot path and the Per_Game_Folder_Toggle state to the settings.json file.
2. WHEN the user clicks the Apply_Button with an empty path, THE SettingsViewModel SHALL clear the persisted screenshot path and Per_Game_Folder_Toggle state from the settings.json file.
3. WHEN RHI starts and loads settings, THE SettingsViewModel SHALL restore the screenshot path and Per_Game_Folder_Toggle state from settings.json.

### Requirement 6: INI Section Handling

**User Story:** As a user, I want the screenshot path to be written correctly into the reshade.ini format, so that ReShade reads the path without issues.

#### Acceptance Criteria

1. THE RHI SHALL write the screenshot path under a `[SCREENSHOT]` section header with the key `SavePath` (e.g. `SavePath=D:\Screenshots`).
2. IF a Managed_Ini already contains a `[SCREENSHOT]` section, THEN THE RHI SHALL update the existing `SavePath` key value without duplicating the section.
3. IF a Managed_Ini does not contain a `[SCREENSHOT]` section, THEN THE RHI SHALL append a new `[SCREENSHOT]` section with the `SavePath` key.
4. THE RHI SHALL preserve all other existing sections and keys in the Managed_Ini when writing the screenshot path.

### Requirement 7: Per-Game Folder Path Construction

**User Story:** As a user, I want the per-game subfolder to use the game name as detected by RHI, so that folder names match what I see in the app.

#### Acceptance Criteria

1. WHEN the Per_Game_Folder_Toggle is on, THE RHI SHALL construct the SavePath by combining the base screenshot path with the Game_Name using a single backslash separator.
2. THE RHI SHALL use the Game_Name exactly as displayed in the game card sidebar (the `GameName` property on `GameCardViewModel`), preserving original casing and spacing.
3. WHEN the Game_Name contains characters that are invalid in Windows directory names (e.g. `<`, `>`, `:`, `"`, `/`, `|`, `?`, `*`), THE RHI SHALL remove those characters from the subfolder name.

### Requirement 8: Future INI Deployments Include Screenshot Path

**User Story:** As a user, I want newly deployed reshade.ini files to also include my screenshot path, so that new installs are consistent with existing ones.

#### Acceptance Criteria

1. WHEN a screenshot path is configured and RHI deploys a new reshade.ini via MergeRsIni or MergeRsVulkanIni, THE RHI SHALL include the `[SCREENSHOT]` section with the configured SavePath in the merged result.
2. WHEN the Per_Game_Folder_Toggle is on and a screenshot path is configured, THE RHI SHALL use the game name of the target game when constructing the SavePath for the newly deployed Managed_Ini.
