# Requirements Document

## Introduction

This feature adds a ReShade UI overlay hotkey configuration option to the RHI Settings page. The existing Screenshots section is restructured into two side-by-side sub-sections within the same container box: Screenshots on the left and ReShade UI Hotkey on the right. Users can capture a hotkey (with optional Ctrl/Shift/Alt modifiers), persist it across restarts, and apply it to all managed reshade.ini files. The hotkey is stored in ReShade's native `KeyOverlay=<vk_code>,<shift>,<ctrl>,<alt>` format under the `[INPUT]` section.

## Glossary

- **RHI**: ReShade HDR Installer — the WinUI 3 desktop application that manages ReShade, RenoDX, and Luma mods for detected games.
- **Settings_Page**: The settings panel in RHI, toggled via the Settings button in the toolbar.
- **Screenshots_Section**: The existing UI sub-section within the Settings_Page for configuring the screenshot save path, folder picker, Open button, per-game subfolder toggle, and its own Apply to All Games button.
- **Hotkey_Section**: A new UI sub-section within the same container box as Screenshots_Section, positioned to the right, for configuring the ReShade overlay hotkey.
- **Hotkey_Capture_Box**: A text input control that captures a single key press with optional modifier keys (Ctrl, Shift, Alt) and displays the resulting hotkey combination.
- **Hotkey_Apply_Button**: A button within the Hotkey_Section that writes the configured hotkey to all managed reshade.ini files.
- **Managed_Ini**: A reshade.ini (or reshade2.ini, reshade3.ini, or any reshade*.ini variant) file located in a game's install directory that RHI has previously deployed or merged.
- **KeyOverlay_Format**: The ReShade INI format for the overlay hotkey: `KeyOverlay=<vk_code>,<shift>,<ctrl>,<alt>` where vk_code is a Windows virtual key code integer and shift/ctrl/alt are 0 or 1.
- **Default_Hotkey**: The Home key (VK code 36) with no modifiers, represented as `KeyOverlay=36,0,0,0`. This is ReShade's standard default overlay toggle key.
- **SettingsViewModel**: The view model that owns settings persistence (load/save to settings.json).
- **AuxInstallService**: The service responsible for deploying and merging reshade.ini files into game directories.

## Requirements

### Requirement 1: Side-by-Side Layout for Screenshots and Hotkey Sections

**User Story:** As a user, I want the Screenshots and ReShade UI Hotkey settings grouped together in one container box with a side-by-side layout, so that the Settings page remains compact and organized.

#### Acceptance Criteria

1. WHEN the user navigates to the Settings_Page, THE Settings_Page SHALL display a single container box containing the Screenshots_Section on the left and the Hotkey_Section on the right.
2. THE Screenshots_Section SHALL retain all existing functionality: screenshot path text box, folder picker button, Open button, per-game subfolder toggle, and its own Apply to All Games button.
3. THE Hotkey_Section SHALL contain the Hotkey_Capture_Box and its own Hotkey_Apply_Button labeled "Apply to All Games".
4. THE container box SHALL NOT have a single combined heading — each sub-section SHALL display its own heading ("Screenshots" and "ReShade UI Hotkey" respectively).

### Requirement 2: Hotkey Capture Input

**User Story:** As a user, I want to press a key combination in a capture box to set my ReShade overlay hotkey, so that I can configure the hotkey without manually editing INI files.

#### Acceptance Criteria

1. WHEN the Hotkey_Capture_Box has focus and the user presses a non-modifier key (with or without Ctrl, Shift, or Alt held), THE Hotkey_Capture_Box SHALL display the captured key combination in human-readable format (e.g. "Home", "F2", "Ctrl + F2", "Ctrl + Shift + F12").
2. THE Hotkey_Capture_Box SHALL store the captured key as a virtual key code integer and three boolean modifier flags (shift, ctrl, alt).
3. WHEN the user clears the Hotkey_Capture_Box (empties the text), THE Hotkey_Capture_Box SHALL revert to the Default_Hotkey (Home, VK 36, no modifiers).
4. WHEN the Settings_Page is opened, THE Hotkey_Capture_Box SHALL display the previously saved hotkey from SettingsViewModel in human-readable format.

### Requirement 3: Hotkey Persistence

**User Story:** As a user, I want my chosen overlay hotkey to be remembered between application restarts, so that I do not have to reconfigure it each session.

#### Acceptance Criteria

1. THE SettingsViewModel SHALL persist the hotkey as a single string in KeyOverlay_Format (e.g. "36,0,0,0") to the settings.json file.
2. WHEN RHI starts and loads settings, THE SettingsViewModel SHALL restore the hotkey value from settings.json.
3. WHEN no hotkey value exists in settings.json, THE SettingsViewModel SHALL default to the Default_Hotkey value "36,0,0,0".
4. WHEN the user saves a hotkey, THE SettingsViewModel SHALL write the value via SaveSettingsToDict following the existing settings persistence pattern.

### Requirement 4: Apply Hotkey to All Games

**User Story:** As a user, I want to click "Apply to All Games" to push my hotkey into all managed reshade.ini files, so that every game uses the same overlay toggle key.

#### Acceptance Criteria

1. WHEN the user clicks the Hotkey_Apply_Button, THE RHI SHALL iterate over all game cards that have a non-empty InstallPath.
2. THE RHI SHALL locate all reshade*.ini files (reshade.ini, reshade2.ini, reshade3.ini, and any other reshade*.ini variants) in each game's install directory, following the same file enumeration pattern as the screenshot path Apply to All Games flow.
3. THE RHI SHALL write the `KeyOverlay` key with the configured value in KeyOverlay_Format to the `[INPUT]` section of each located reshade*.ini file.
4. THE RHI SHALL use the existing INI parse/merge/write logic in AuxInstallService (ParseIni/WriteIni) to modify only the `[INPUT]` section without altering other sections or keys in each Managed_Ini.
5. WHEN the Hotkey_Apply_Button operation completes, THE RHI SHALL display a confirmation message indicating how many reshade.ini files were updated.
6. THE RHI SHALL persist the hotkey to SettingsViewModel before iterating game cards.

### Requirement 5: Deploy Behavior for New INI Files

**User Story:** As a user, I want newly deployed reshade.ini files to receive my custom hotkey automatically, so that new game installs are consistent with my configuration.

#### Acceptance Criteria

1. WHEN a hotkey is configured and the hotkey is not the Default_Hotkey, and RHI deploys a new reshade.ini via MergeRsIni or MergeRsVulkanIni, THE RHI SHALL write the `KeyOverlay` value to the `[INPUT]` section of the deployed file.
2. WHEN the configured hotkey is the Default_Hotkey (Home, VK 36, no modifiers), THE RHI SHALL NOT write or overwrite the `KeyOverlay` value in newly deployed reshade.ini files, preserving any existing KeyOverlay value in the template (e.g. the RDR2 template's END key).
3. WHEN the configured hotkey is not the Default_Hotkey, THE RHI SHALL overwrite any existing KeyOverlay value in the deployed file, including template-specific values such as the RDR2 template's END key.

### Requirement 6: INI Section Handling for Hotkey

**User Story:** As a user, I want the hotkey to be written correctly into the reshade.ini format, so that ReShade reads the overlay toggle key without issues.

#### Acceptance Criteria

1. THE RHI SHALL write the hotkey under an `[INPUT]` section header with the key `KeyOverlay` in the format `<vk_code>,<shift>,<ctrl>,<alt>` (e.g. `KeyOverlay=36,0,0,0`).
2. IF a Managed_Ini already contains an `[INPUT]` section, THEN THE RHI SHALL update the existing `KeyOverlay` key value without duplicating the section.
3. IF a Managed_Ini does not contain an `[INPUT]` section, THEN THE RHI SHALL append a new `[INPUT]` section with the `KeyOverlay` key.
4. THE RHI SHALL preserve all other existing sections and keys in the Managed_Ini when writing the hotkey.
5. THE RHI SHALL preserve all other existing keys within the `[INPUT]` section (other than `KeyOverlay`) when writing the hotkey.

### Requirement 7: Hotkey Display Format

**User Story:** As a user, I want the hotkey capture box to show a readable key name, so that I can verify which key combination is configured.

#### Acceptance Criteria

1. THE Hotkey_Capture_Box SHALL display modifier keys before the main key, separated by " + " (e.g. "Ctrl + Shift + F2").
2. THE Hotkey_Capture_Box SHALL display modifier keys in the order: Ctrl, Shift, Alt.
3. THE Hotkey_Capture_Box SHALL display the main key using its standard name (e.g. "Home", "End", "F2", "F12", "A", "1").
4. WHEN only a modifier key is pressed without a main key, THE Hotkey_Capture_Box SHALL NOT update the displayed value.
