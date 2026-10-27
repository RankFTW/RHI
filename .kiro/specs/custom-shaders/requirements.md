# Requirements Document

## Introduction

This feature adds a "Custom Shaders" mode to RHI (ReShade HDR Installer). When enabled, the application sources shaders and textures from user-managed directories (`%LOCALAPPDATA%\RHI\reshade\Custom\shaders` and `%LOCALAPPDATA%\RHI\reshade\Custom\textures`) instead of the built-in shader pack system. The feature operates at two levels: a global toggle in the Settings menu and a per-game override toggle in the overrides panel. Per-game overrides allow individual games to opt in or out of custom shaders independently of the global setting.

## Glossary

- **RHI**: ReShade HDR Installer — the WinUI 3 desktop application this feature belongs to.
- **Settings_Menu**: The settings page of RHI, accessed via the settings button, containing configuration boxes for Add Game, Full Refresh, and other options.
- **Custom_Shaders_Directory**: The directory `%LOCALAPPDATA%\RHI\reshade\Custom\shaders` from which custom shader files are sourced.
- **Custom_Textures_Directory**: The directory `%LOCALAPPDATA%\RHI\reshade\Custom\textures` from which custom texture files are sourced.
- **Global_Shader_Button**: The "Global Shaders" button (`ChooseShadersBtn`) in the toolbar at the top of the main window, used to open the shader pack selection picker.
- **Shader_Pack_Service**: The service (`IShaderPackService`) responsible for deploying shader files to game folders via `SyncGameFolder`.
- **Per_Game_Overrides_Panel**: The overrides UI (built by `DetailPanelBuilder` in detail view and `OverridesFlyoutBuilder` in grid view) that allows per-game configuration of shader modes, DLL overrides, and other settings.
- **Settings_ViewModel**: The view model (`SettingsViewModel`) responsible for persisting application settings to the JSON settings file.
- **Game_Name_Service**: The service (`IGameNameService`) responsible for persisting per-game settings including shader mode and shader selection.
- **Custom_Shaders_Toggle**: The new toggle switch in the Settings menu that enables or disables global custom shaders mode.
- **Per_Game_Custom_Toggle**: The new toggle switch in the per-game overrides panel that enables or disables custom shaders for a specific game.

## Requirements

### Requirement 1: Global Custom Shaders Toggle in Settings Menu

**User Story:** As a user, I want a toggle in the Settings menu to enable custom shaders globally, so that I can use my own shader files instead of the built-in shader packs.

#### Acceptance Criteria

1. THE Settings_Menu SHALL display a Custom_Shaders_Toggle box between the "Add Game" box and the "Full Refresh" box.
2. THE Custom_Shaders_Toggle box SHALL follow the same visual format as the "Add Game" and "Full Refresh" boxes (Border with `SurfaceRaisedBrush` background, `CornerRadius="10"`, `Padding="20,16"`, `BorderSubtleBrush` border, containing a title TextBlock, a description TextBlock, and a ToggleSwitch).
3. WHEN the Custom_Shaders_Toggle is toggled ON, THE Settings_ViewModel SHALL persist the custom shaders enabled state as `"true"` under the key `"UseCustomShaders"` in the settings file.
4. WHEN the Custom_Shaders_Toggle is toggled OFF, THE Settings_ViewModel SHALL persist the custom shaders enabled state as `"false"` under the key `"UseCustomShaders"` in the settings file.
5. WHEN the Settings_Menu is opened, THE Custom_Shaders_Toggle SHALL reflect the persisted custom shaders enabled state from the settings file.

### Requirement 2: Custom Shader Directory Sourcing

**User Story:** As a user, I want RHI to source shaders from my custom directories when custom shaders mode is active, so that my personal shader files are deployed to games.

#### Acceptance Criteria

1. WHILE global custom shaders is enabled and no per-game override disables custom shaders for a specific game, THE Shader_Pack_Service SHALL deploy shader files from the Custom_Shaders_Directory to that game folder.
2. WHILE global custom shaders is enabled and no per-game override disables custom shaders for a specific game, THE Shader_Pack_Service SHALL deploy texture files from the Custom_Textures_Directory to that game folder.
3. IF the Custom_Shaders_Directory does not exist when custom shaders deployment is triggered, THEN THE RHI SHALL create the Custom_Shaders_Directory before attempting deployment.
4. IF the Custom_Textures_Directory does not exist when custom shaders deployment is triggered, THEN THE RHI SHALL create the Custom_Textures_Directory before attempting deployment.

### Requirement 3: Global Shader Button Disabled State

**User Story:** As a user, I want the global shader picker button to be greyed out when custom shaders is enabled, so that I understand the built-in shader pack selection is not in use.

#### Acceptance Criteria

1. WHILE global custom shaders is enabled, THE Global_Shader_Button SHALL be visually greyed out (disabled appearance).
2. WHILE global custom shaders is enabled, THE Global_Shader_Button SHALL not respond to click events.
3. WHILE global custom shaders is disabled, THE Global_Shader_Button SHALL be enabled and respond to click events as normal.
4. WHEN the Custom_Shaders_Toggle is toggled ON from the Settings_Menu, THE Global_Shader_Button SHALL transition to the disabled state without requiring an application restart.
5. WHEN the Custom_Shaders_Toggle is toggled OFF from the Settings_Menu, THE Global_Shader_Button SHALL transition to the enabled state without requiring an application restart.

### Requirement 4: Per-Game Custom Shaders Toggle in Overrides Panel

**User Story:** As a user, I want a per-game "Use Custom Shaders" toggle in the overrides panel, so that I can enable custom shaders for individual games independently of the global setting.

#### Acceptance Criteria

1. THE Per_Game_Overrides_Panel SHALL display a Per_Game_Custom_Toggle next to the existing "Use Global Shaders" toggle in the shaders section.
2. WHEN the Per_Game_Custom_Toggle is toggled ON for a specific game, THE Game_Name_Service SHALL persist the per-game custom shaders state for that game.
3. WHEN the Per_Game_Custom_Toggle is toggled OFF for a specific game, THE Game_Name_Service SHALL remove the per-game custom shaders state for that game.
4. WHEN the Per_Game_Custom_Toggle is toggled ON for a specific game, THE Shader_Pack_Service SHALL deploy shaders from the Custom_Shaders_Directory and textures from the Custom_Textures_Directory to that game folder, regardless of the global custom shaders setting.
5. THE Per_Game_Custom_Toggle SHALL appear in both the detail view overrides panel (built by `DetailPanelBuilder`) and the grid view overrides flyout (built by `OverridesFlyoutBuilder`).

### Requirement 5: Per-Game Override Opt-Out from Global Custom Shaders

**User Story:** As a user, I want to opt individual games out of global custom shaders and back to selected shader packs, so that I can use custom shaders globally while still using specific shader packs for certain games.

#### Acceptance Criteria

1. WHILE global custom shaders is enabled, THE Per_Game_Overrides_Panel SHALL allow the user to set a game to use selected shader packs instead of custom shaders by toggling the Per_Game_Custom_Toggle OFF and selecting shader packs via the existing per-game shader selection mechanism.
2. WHILE global custom shaders is enabled and a game has the Per_Game_Custom_Toggle set to OFF with a per-game shader selection, THE Shader_Pack_Service SHALL deploy the per-game selected shader packs to that game folder instead of custom shaders.
3. WHILE global custom shaders is enabled, THE Per_Game_Custom_Toggle SHALL default to ON (following the global setting) for games that have no per-game custom shader override set.

### Requirement 6: Shader Deployment Resolution Logic

**User Story:** As a user, I want the shader deployment to correctly resolve which shaders to use based on the combination of global and per-game settings, so that each game gets the correct shaders.

#### Acceptance Criteria

1. WHEN deploying shaders for a game, THE RHI SHALL resolve the effective shader source using the following priority order: (a) per-game custom shaders ON uses custom shader directories, (b) per-game custom shaders OFF with per-game shader selection uses the per-game selected packs, (c) global custom shaders ON uses custom shader directories, (d) global custom shaders OFF uses the global selected shader packs.
2. WHEN the Custom_Shaders_Toggle or Per_Game_Custom_Toggle is changed, THE RHI SHALL re-deploy shaders to all affected game folders immediately without requiring a manual refresh.
3. THE RHI SHALL log the effective shader source (custom or pack-based) for each game during shader deployment when verbose logging is enabled.

### Requirement 7: Settings Persistence Round-Trip

**User Story:** As a developer, I want the custom shaders settings to survive application restarts, so that users do not lose their configuration.

#### Acceptance Criteria

1. FOR ALL valid combinations of global custom shaders state (true/false) and per-game custom shaders overrides, saving settings via `SaveSettingsToDict` and loading via `LoadSettingsFromDict` SHALL produce equivalent state (round-trip property).
2. THE Settings_ViewModel SHALL load the `UseCustomShaders` key from the settings dictionary during `LoadSettingsFromDict`, defaulting to `false` when the key is absent.
3. THE Game_Name_Service SHALL persist per-game custom shader overrides alongside existing per-game shader mode data.
