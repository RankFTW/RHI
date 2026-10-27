# Requirements Document

## Introduction

RenoDXCommander currently installs ReShade with addon support for all games. Some users need to run normal ReShade (without addon support) for specific games — for example, when addons cause compatibility issues or are unnecessary. This feature adds a per-game toggle called "ReShade Without Addon Support" that switches a game from the addon-enabled ReShade variant to the standard ReShade variant, manages addon removal, and maintains a separate download/staging pipeline for the non-addon ReShade installer.

## Glossary

- **RHI**: RenoDXCommander application (also the AppData folder name `%LocalAppData%\RHI\`)
- **Addon_ReShade**: The ReShade variant downloaded from `reshade.me` with the `_Addon` suffix in the installer filename. This is the current default variant managed by `ReShadeUpdateService`.
- **Normal_ReShade**: The ReShade variant downloaded from `reshade.me` without the `_Addon` suffix. URL pattern: `/downloads/ReShade_Setup_X.Y.Z.exe`.
- **Toggle**: The per-game "ReShade Without Addon Support" boolean setting stored in `settings.json`.
- **Staging_Directory**: A folder under `%LocalAppData%\RHI\` where downloaded and extracted ReShade DLLs are cached before deployment to game folders.
- **Addon_Staging**: The existing staging directory at `%LocalAppData%\RHI\reshade\` used for Addon_ReShade DLLs.
- **Normal_Staging**: A new staging directory at `%LocalAppData%\RHI\reshade-normal\` used for Normal_ReShade DLLs.
- **Managed_Addon**: An addon file (`.addon32` / `.addon64`) deployed to a game folder by AddonPackService (e.g. renodx, relimiter, display commander addons).
- **DLL_Override**: A per-game custom DLL filename configured via `DllOverrideService`, overriding the default `dxgi.dll` name.
- **GameCardViewModel**: The per-game view model holding all observable state for a game card in the UI.
- **SevenZipExtractor**: The existing `ReShadeExtractor` service that extracts DLLs from ReShade installer executables using ZIP or 7-Zip fallback.

## Requirements

### Requirement 1: Per-Game Toggle Persistence

**User Story:** As a user, I want to enable "ReShade Without Addon Support" for specific games, so that those games use normal ReShade instead of the addon-enabled variant.

#### Acceptance Criteria

1. THE GameCardViewModel SHALL expose an observable boolean property `UseNormalReShade` that defaults to `false`.
2. WHEN the user enables the Toggle for a game, THE RHI SHALL persist the `UseNormalReShade` value for that game in `settings.json`.
3. WHEN RHI loads settings from `settings.json`, THE RHI SHALL restore the `UseNormalReShade` value for each game that has the Toggle enabled.
4. WHEN the user disables the Toggle for a game, THE RHI SHALL remove the game from the persisted set of normal-ReShade games in `settings.json`.

### Requirement 2: Addon Removal on Toggle Enable

**User Story:** As a user, I want all managed addons removed from a game folder when I enable the Toggle, so that the game runs cleanly with normal ReShade.

#### Acceptance Criteria

1. WHEN the user enables the Toggle for a game that has Managed_Addons deployed, THE RHI SHALL remove all Managed_Addon files (`.addon32` and `.addon64`) from the game's install folder.
2. WHEN the user enables the Toggle for a game that has Addon_ReShade installed, THE RHI SHALL uninstall the Addon_ReShade DLL from the game folder.
3. WHEN the user enables the Toggle for a game that has Addon_ReShade installed, THE RHI SHALL install Normal_ReShade in its place using the same DLL filename (respecting any active DLL_Override).
4. IF the Normal_ReShade DLLs are not available in Normal_Staging when the Toggle is enabled, THEN THE RHI SHALL display an error message indicating that Normal_ReShade must be downloaded first.

### Requirement 3: Global Addon Deployment Exclusion

**User Story:** As a user, I want games with the Toggle enabled to be excluded from global addon deployment, so that addons are not re-deployed to those games during bulk operations.

#### Acceptance Criteria

1. WHEN the RHI performs global addon deployment (Deploy All Addons), THE AddonPackService SHALL skip any game that has `UseNormalReShade` set to `true`.
2. WHEN the RHI performs per-game addon deployment for a game with `UseNormalReShade` set to `true`, THE AddonPackService SHALL deploy zero addons and remove any existing Managed_Addon files from the game folder.
3. WHEN the user disables the Toggle for a game, THE RHI SHALL re-deploy addons to that game according to the game's current addon selection (global or per-game).

### Requirement 4: UI Addon Greying Out

**User Story:** As a user, I want addon UI elements to appear greyed out and struck through for games with the Toggle enabled, so that I can see at a glance that addons are unavailable for that game.

#### Acceptance Criteria

1. WHILE a game has `UseNormalReShade` set to `true`, THE GameCardViewModel SHALL report all addon install buttons (renodx addon, relimiter, display commander) as disabled.
2. WHILE a game has `UseNormalReShade` set to `true`, THE GameCardViewModel SHALL expose a computed property indicating that addon rows should display with strikethrough styling.
3. WHEN the user disables the Toggle for a game, THE GameCardViewModel SHALL re-enable addon install buttons according to existing mutual-exclusivity rules (ReLimiter vs Display Commander).

### Requirement 5: Normal ReShade Download and Version Check

**User Story:** As a user, I want RHI to download normal ReShade from reshade.me and check for newer versions, so that I always have the latest non-addon ReShade available.

#### Acceptance Criteria

1. THE RHI SHALL provide a `NormalReShadeUpdateService` that scrapes `https://reshade.me` to find the latest Normal_ReShade installer URL matching the pattern `/downloads/ReShade_Setup_X.Y.Z.exe` (without the `_Addon` suffix).
2. WHEN a newer Normal_ReShade version is detected, THE NormalReShadeUpdateService SHALL download the installer from `https://reshade.me/downloads/ReShade_Setup_X.Y.Z.exe`, including a `Referer` header set to `https://reshade.me/`.
3. WHEN the Normal_ReShade installer is downloaded, THE NormalReShadeUpdateService SHALL validate that the downloaded file is a valid PE executable (starts with `MZ` header bytes).
4. IF the downloaded Normal_ReShade installer is smaller than 500,000 bytes, THEN THE NormalReShadeUpdateService SHALL discard the file and report a download failure.
5. THE NormalReShadeUpdateService SHALL store the currently staged Normal_ReShade version in a `reshade_version.txt` file inside Normal_Staging.

### Requirement 6: Separate Storage Directories

**User Story:** As a user, I want addon-enabled and normal ReShade DLLs stored in separate directories, so that the two variants do not overwrite or conflict with each other.

#### Acceptance Criteria

1. THE RHI SHALL stage Addon_ReShade DLLs in Addon_Staging (`%LocalAppData%\RHI\reshade\`).
2. THE RHI SHALL stage Normal_ReShade DLLs in Normal_Staging (`%LocalAppData%\RHI\reshade-normal\`).
3. THE Normal_Staging directory SHALL contain `ReShade64.dll`, `ReShade32.dll`, and `reshade_version.txt`.
4. WHEN the RHI installs ReShade for a game with `UseNormalReShade` set to `true`, THE AuxInstallService SHALL copy the DLL from Normal_Staging instead of Addon_Staging.
5. WHEN the RHI installs ReShade for a game with `UseNormalReShade` set to `false`, THE AuxInstallService SHALL copy the DLL from Addon_Staging (existing behaviour).

### Requirement 7: DLL Extraction from Installer

**User Story:** As a user, I want ReShade64.dll and ReShade32.dll extracted from the normal ReShade installer, so that the DLLs are ready for deployment.

#### Acceptance Criteria

1. WHEN the Normal_ReShade installer has been downloaded, THE NormalReShadeUpdateService SHALL extract `ReShade64.dll` and `ReShade32.dll` from the installer using the SevenZipExtractor.
2. THE NormalReShadeUpdateService SHALL store the extracted DLLs in Normal_Staging.
3. IF extraction fails, THEN THE NormalReShadeUpdateService SHALL log the failure and report the error to the caller.

### Requirement 8: DLL Naming on Deployment

**User Story:** As a user, I want the deployed Normal_ReShade DLL to follow the same naming rules as Addon_ReShade, so that DLL override configurations work consistently.

#### Acceptance Criteria

1. WHEN deploying Normal_ReShade to a game folder, THE AuxInstallService SHALL name the DLL `dxgi.dll` by default.
2. WHEN a DLL_Override is active for a game (via manifest or per-game override), THE AuxInstallService SHALL use the overridden filename instead of `dxgi.dll`.
3. WHEN deploying Normal_ReShade, THE AuxInstallService SHALL perform the same DC occupancy check and foreign DLL backup as the existing Addon_ReShade deployment.

### Requirement 9: ReShade Install State Tracking

**User Story:** As a user, I want the app to track whether a game has normal or addon-enabled ReShade installed, so that update detection and reinstall work correctly.

#### Acceptance Criteria

1. WHEN Normal_ReShade is installed for a game, THE AuxInstallService SHALL save an AuxInstalledRecord with a distinguishing marker indicating the installed variant is Normal_ReShade (e.g. AddonType = "ReShadeNormal").
2. WHEN checking for ReShade updates for a game with Normal_ReShade installed, THE RHI SHALL compare against the Normal_Staging DLLs rather than the Addon_Staging DLLs.
3. WHEN the user toggles from Normal_ReShade back to Addon_ReShade, THE RHI SHALL uninstall the Normal_ReShade DLL and install the Addon_ReShade DLL in its place.

### Requirement 10: Shader Deployment Unchanged

**User Story:** As a user, I want shader deployment to work identically regardless of the ReShade variant, so that my shader setup is not affected by the Toggle.

#### Acceptance Criteria

1. WHEN Normal_ReShade is installed for a game, THE ShaderPackService SHALL deploy shaders to the game folder using the same logic as for Addon_ReShade.
2. THE Toggle SHALL have no effect on shader selection, shader sync, or shader file deployment.
