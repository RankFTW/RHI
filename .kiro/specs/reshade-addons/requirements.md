# Requirements Document

## Introduction

RHI (RenoDX Commander) currently manages game mods, ReShade installations, shader packs, and frame limiters. This feature adds ReShade addon management — a curated list of addons sourced from the official ReShade Addons.ini, with global download/storage, per-game deployment, and per-game override controls. The feature mirrors the existing Global Shaders pattern for consistency.

## Glossary

- **RHI**: The RenoDX Commander application (WPF/WinUI 3, C#).
- **Addon**: A ReShade addon file (`.addon32` or `.addon64`) that extends ReShade functionality.
- **Addons_Ini**: The official ReShade addon list hosted at `https://raw.githubusercontent.com/crosire/reshade-shaders/list/Addons.ini`, containing metadata for curated addons.
- **Addon_Entry**: A single addon record parsed from the Addons_Ini, containing PackageName, PackageDescription, DownloadUrl, DownloadUrl32, DownloadUrl64, RepositoryUrl, and optional EffectInstallPath.
- **Addon_Exclusion_List**: A hardcoded list of Addons.ini section IDs that RHI filters out because they are already managed by RHI or not applicable. Currently excludes: `[00]` (Swap chain override — managed by ReShade itself), the commented-out Framerate Limiter, `[21]` (RenoDX — already managed by RHI), and `[26]` (Display Commander — already managed by RHI).
- **Addon_Manager_Window**: The dialog window for browsing and downloading curated ReShade addons globally.
- **Addon_Storage**: The local directory under `%LocalAppData%\RHI\addons\` where downloaded addon files are staged.
- **Addon_Deploy_Path**: The target directory alongside ReShade in a game's folder where addon files are placed for that game.
- **Override_Panel**: The per-game overrides UI section in the detail panel.
- **Global_Addon_Set**: The set of addons downloaded and available globally via the Addon_Manager_Window.
- **Per_Game_Addon_Selection**: A per-game override that specifies which addons from the Global_Addon_Set to deploy for a specific game.
- **Bitness**: Whether a game executable is 32-bit or 64-bit, determining which addon file variant to use.
- **RenoDX_DevKit_Addon**: A supplementary addon (`renodx-devkit.addon32`/`.addon64`) sourced from the RenoDX GitHub releases at `https://github.com/clshortfuse/renodx/releases/download/snapshot/`. This addon is managed separately from the Addons.ini list and is always included in the available addons.
- **First_Time_Warning**: A one-time warning dialog shown the first time a user opens the Addon_Manager_Window, informing them that addons are an advanced feature.

## Requirements

### Requirement 1: Parse Addons.ini

**User Story:** As a user, I want RHI to fetch and parse the official ReShade Addons.ini, so that I can see a curated list of available addons.

#### Acceptance Criteria

1. WHEN RHI starts or the user triggers a refresh, THE Addons_Ini_Parser SHALL fetch the Addons.ini file from `https://raw.githubusercontent.com/crosire/reshade-shaders/list/Addons.ini`.
2. WHEN the Addons.ini file is fetched successfully, THE Addons_Ini_Parser SHALL parse each section into an Addon_Entry containing PackageName, PackageDescription, DownloadUrl, DownloadUrl32, DownloadUrl64, RepositoryUrl, and EffectInstallPath fields.
3. THE Addons_Ini_Parser SHALL exclude any entries whose section IDs appear in the Addon_Exclusion_List (currently sections `00`, `21`, and `26`) and SHALL skip commented-out sections.
4. IF the Addons.ini file cannot be fetched, THEN THE Addons_Ini_Parser SHALL log the error and use the previously cached addon list if available.
5. IF an Addon_Entry section contains malformed or missing required fields, THEN THE Addons_Ini_Parser SHALL skip that entry and log a warning.
5. THE Addons_Ini_Pretty_Printer SHALL format a list of Addon_Entry objects back into valid Addons.ini format.
6. FOR ALL valid Addon_Entry lists, parsing then pretty-printing then parsing SHALL produce an equivalent list of Addon_Entry objects (round-trip property).

### Requirement 9: RenoDX DevKit Addon

**User Story:** As a user, I want the RenoDX DevKit addon available alongside the Addons.ini addons, so that I can use RenoDX development tools with ReShade.

#### Acceptance Criteria

1. THE AddonPackService SHALL include the RenoDX DevKit addon in the AvailablePacks list, with PackageName "RenoDX DevKit", DownloadUrl32 `https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-devkit.addon32`, and DownloadUrl64 `https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-devkit.addon64`.
2. THE RenoDX DevKit addon SHALL appear in the Addon_Manager_Window alongside the Addons.ini entries.
3. THE Addon_Update_Service SHALL check the RenoDX DevKit addon for updates using the same GitHub release checking mechanism, monitoring the `snapshot` tag at `https://github.com/clshortfuse/renodx/releases`.
4. THE RenoDX DevKit addon SHALL follow the same download, storage, deployment, and per-game selection rules as all other addons.

### Requirement 10: First-Time Warning Dialog

**User Story:** As a user, I want to see a warning the first time I open the ReShade Addons manager, so that I understand these are advanced features.

#### Acceptance Criteria

1. WHEN the user clicks the "ReShade Addons" box for the first time ever, THE RHI SHALL display a warning dialog before opening the Addon_Manager_Window.
2. THE warning dialog SHALL explain that ReShade addons are advanced features intended for users who understand what they are.
3. THE warning dialog SHALL provide a "Continue" button to proceed to the Addon_Manager_Window and a "Cancel" button to close without opening it.
4. WHEN the user clicks "Continue", THE RHI SHALL persist a flag so the warning is not shown again on subsequent opens.
5. WHEN the user clicks "Cancel", THE RHI SHALL not open the Addon_Manager_Window and SHALL not persist the dismissal flag.

### Requirement 2: Global Addon Manager Window

**User Story:** As a user, I want a "ReShade Addons" box in the main UI header that opens an addon manager window, so that I can browse and download addons globally.

#### Acceptance Criteria

1. THE Main_UI SHALL display a "ReShade Addons" box to the right of the existing "Global Shaders" box in the top section.
2. WHEN the user clicks the "ReShade Addons" box, THE RHI SHALL open the Addon_Manager_Window.
3. THE Addon_Manager_Window SHALL display a scrollable list of all Addon_Entry items parsed from the Addons.ini.
4. THE Addon_Manager_Window SHALL display each addon's PackageName and PackageDescription.
5. WHEN an addon has a DownloadUrl, DownloadUrl32, or DownloadUrl64, THE Addon_Manager_Window SHALL show a download/install button for that addon.
6. WHEN an addon has only a RepositoryUrl and no download URLs, THE Addon_Manager_Window SHALL show a link to the repository instead of a download button.
7. THE Addon_Manager_Window SHALL indicate which addons are already downloaded in the Addon_Storage.

### Requirement 3: Addon Download and Storage

**User Story:** As a user, I want to download addons to a local staging area, so that they are available for deployment to games.

#### Acceptance Criteria

1. WHEN the user initiates an addon download, THE Addon_Download_Service SHALL download the addon file to the Addon_Storage directory.
2. WHEN the addon download URL points to a `.addon32` or `.addon64` file, THE Addon_Download_Service SHALL save the file directly to Addon_Storage.
3. WHEN the addon download URL points to a `.zip` archive, THE Addon_Download_Service SHALL extract addon files from the archive and place them in Addon_Storage.
4. WHEN an Addon_Entry provides both DownloadUrl32 and DownloadUrl64, THE Addon_Download_Service SHALL download both variants and store them with their respective extensions.
5. WHEN an Addon_Entry provides only a single DownloadUrl, THE Addon_Download_Service SHALL download that file and store it in Addon_Storage.
6. IF a download fails, THEN THE Addon_Download_Service SHALL display an error message to the user and log the failure.
7. THE Addon_Download_Service SHALL track the installed version of each addon using the same GitHub release checking mechanism used by the shader pack service.

### Requirement 4: Addon Auto-Update

**User Story:** As a user, I want addons to be automatically updated when new versions are available, so that I always have the latest addon versions.

#### Acceptance Criteria

1. WHEN RHI starts, THE Addon_Update_Service SHALL check each downloaded addon for available updates using the same version-checking mechanism as the shader pack service.
2. WHEN a newer version of a downloaded addon is available, THE Addon_Update_Service SHALL download and replace the addon file in Addon_Storage.
3. WHILE an addon update is in progress, THE Addon_Manager_Window SHALL display a progress indicator for that addon.
4. IF an addon update fails, THEN THE Addon_Update_Service SHALL retain the existing addon version and log the error.

### Requirement 5: Override Panel Layout Change

**User Story:** As a user, I want the override panel reorganized so that Shaders and Addons are grouped together on the bottom row, so that related controls are visually adjacent.

#### Acceptance Criteria

1. THE Override_Panel SHALL display the Shaders section on the bottom row, left-hand side (moved from its current position in the middle row).
2. THE Override_Panel SHALL display the Bitness and Graphics API section in the position previously occupied by the Shaders section (middle row).
3. THE Override_Panel SHALL display the Addons section on the bottom row, right-hand side, adjacent to the Shaders section.

### Requirement 6: Per-Game Addon Control

**User Story:** As a user, I want to control which addons are deployed per game, so that I can customize addon usage for each game.

#### Acceptance Criteria

1. THE Addons_Override_Section SHALL include a "Global" toggle switch.
2. WHEN the "Global" toggle is ON, THE RHI SHALL use the Global_Addon_Set for that game.
3. WHEN the "Global" toggle is OFF, THE Addons_Override_Section SHALL display a "Select Addons" button.
4. WHEN the user clicks "Select Addons", THE RHI SHALL open a selection dialog showing all addons in the Global_Addon_Set with checkboxes.
5. THE RHI SHALL persist the Per_Game_Addon_Selection so it survives application restarts.
6. WHEN the "Global" toggle is switched from OFF to ON, THE RHI SHALL clear the Per_Game_Addon_Selection for that game and use the Global_Addon_Set.

### Requirement 7: Addon Deployment to Games

**User Story:** As a user, I want addons deployed alongside ReShade in the game's folder, so that ReShade loads them when the game starts.

#### Acceptance Criteria

1. WHEN a game has ReShade installed and the "Global" addon toggle is ON, THE Addon_Deploy_Service SHALL copy all addons from the Global_Addon_Set to the Addon_Deploy_Path for that game.
2. WHEN a game has ReShade installed and the "Global" addon toggle is OFF, THE Addon_Deploy_Service SHALL copy only the selected addons from the Per_Game_Addon_Selection to the Addon_Deploy_Path.
3. THE Addon_Deploy_Service SHALL deploy the correct bitness variant (`.addon32` or `.addon64`) based on the game's detected or overridden bitness.
4. IF the required bitness variant (`.addon32` for 32-bit games, `.addon64` for 64-bit games) does not exist for an addon, THEN THE Addon_Deploy_Service SHALL skip that addon for that game and SHALL NOT deploy any fallback variant.
5. WHEN an Addon_Entry specifies an EffectInstallPath, THE Addon_Deploy_Service SHALL check the game's reshade.ini for that path and place addon files accordingly.
6. IF the Addon_Deploy_Path does not exist, THEN THE Addon_Deploy_Service SHALL create the directory before deploying addon files.
7. WHEN addons are deployed, THE Addon_Deploy_Service SHALL remove any previously deployed addon files that are no longer in the active selection.

### Requirement 8: Addon Selection Dialog

**User Story:** As a user, I want a selection dialog for choosing per-game addons, so that I can pick exactly which addons to enable for a specific game.

#### Acceptance Criteria

1. THE Addon_Selection_Dialog SHALL display a scrollable list of all addons present in the Global_Addon_Set.
2. THE Addon_Selection_Dialog SHALL show each addon's PackageName and PackageDescription.
3. THE Addon_Selection_Dialog SHALL pre-check addons that are currently in the Per_Game_Addon_Selection for the game.
4. WHEN the user confirms the selection, THE Addon_Selection_Dialog SHALL return the list of selected addon identifiers.
5. WHEN the user cancels the dialog, THE Addon_Selection_Dialog SHALL preserve the existing Per_Game_Addon_Selection.
6. IF no addons are available in the Global_Addon_Set, THEN THE Addon_Selection_Dialog SHALL display a message indicating no addons are downloaded.
