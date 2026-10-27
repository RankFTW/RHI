# Requirements Document

## Introduction

Add first-class RE Framework support to RenoDX Commander (RHI). RE Engine games (Resident Evil, Monster Hunter Wilds, Devil May Cry 5, Street Fighter 6, Dragon's Dogma 2, etc.) require RE Framework's `dinput8.dll` to be present alongside the game executable for ReShade and RenoDX to function. This feature automates detection of RE Engine games, installation of RE Framework from GitHub nightly releases, version tracking, auto-updating, and uninstallation — following the same patterns established by the existing ReShade and ReLimiter auxiliary tool integrations.

## Glossary

- **Commander**: The RenoDX Commander (RHI) desktop application
- **RE_Engine_Game**: A game built on Capcom's RE Engine, identified by the presence of `re_chunk_000.pak` adjacent to the game executable
- **RE_Framework**: The open-source modding framework by praydog (REFramework) that injects via `dinput8.dll`, required for ReShade/RenoDX to work in RE Engine games
- **RE_Framework_Service**: The service component within Commander responsible for downloading, installing, updating, and uninstalling RE Framework
- **Nightly_Releases_API**: The GitHub API endpoint for the REFramework-nightly repository releases (`https://api.github.com/repos/praydog/REFramework-nightly/releases`)
- **Game_Card**: The UI element representing a detected game in Commander's game list, containing status indicators and action buttons
- **Detail_Panel**: The right-side panel in Commander that shows full details and install controls for the selected game
- **Install_Flyout**: The popup panel that appears when clicking the install button on a Game_Card, showing per-component install rows
- **Downloads_Folder**: The local cache directory (`%LocalAppData%\RHI\downloads\`) where Commander stores downloaded files
- **Engine_Badge**: The UI badge on a Game_Card and Detail_Panel that displays the detected game engine name and icon

## Requirements

### Requirement 1: RE Engine Game Detection

**User Story:** As a user, I want Commander to automatically detect RE Engine games so that I can see which games need RE Framework and manage the installation from the app.

#### Acceptance Criteria

1. WHEN Commander scans a detected game's install path, THE Game_Detection_Service SHALL check for the presence of `re_chunk_000.pak` in the same directory as the game executable
2. WHEN `re_chunk_000.pak` is found adjacent to the game executable, THE Game_Detection_Service SHALL classify the game as an RE_Engine_Game
3. WHEN a game is classified as an RE_Engine_Game, THE Commander SHALL display the Engine_Badge with the text "RE Engine" and the custom engine icon (`engine.ico`)
4. THE Game_Detection_Service SHALL add `REEngine` as a new variant to the `EngineType` enum
5. WHEN a manifest engine override is set for a game, THE Commander SHALL use the manifest override instead of the auto-detected RE Engine classification

### Requirement 2: RE Framework Install Button Placement

**User Story:** As a user, I want an RE Framework install button to appear for RE Engine games so that I can install the framework directly from Commander.

#### Acceptance Criteria

1. WHEN a game is classified as an RE_Engine_Game, THE Detail_Panel SHALL display an RE Framework install row
2. THE Detail_Panel SHALL place the RE Framework install row above the ReShade install row, making the order: RE Framework, ReShade, ReLimiter, RenoDX (top to bottom)
3. THE Install_Flyout SHALL display the RE Framework component row above the ReShade component row for RE_Engine_Games
4. WHEN a game is not classified as an RE_Engine_Game, THE Detail_Panel SHALL hide the RE Framework install row
5. WHEN a game is not classified as an RE_Engine_Game, THE Install_Flyout SHALL hide the RE Framework component row

### Requirement 3: RE Framework Installation

**User Story:** As a user, I want to install RE Framework with one click so that I do not have to manually download and extract files.

#### Acceptance Criteria

1. WHEN the user clicks the RE Framework install button, THE RE_Framework_Service SHALL download `MHWILDS.zip` from the latest release at `https://github.com/praydog/REFramework-nightly/releases`
2. WHEN the download completes, THE RE_Framework_Service SHALL extract only `dinput8.dll` from `MHWILDS.zip` using the bundled 7-Zip extractor
3. WHEN extraction completes, THE RE_Framework_Service SHALL store `dinput8.dll` in the Downloads_Folder for caching
4. WHEN the cached `dinput8.dll` is available, THE RE_Framework_Service SHALL copy `dinput8.dll` to the same directory as the game executable
5. WHEN installation completes, THE RE_Framework_Service SHALL delete the downloaded `MHWILDS.zip` from the Downloads_Folder, retaining only the extracted `dinput8.dll`
6. WHILE the RE Framework installation is in progress, THE Game_Card SHALL display a progress indicator
7. IF the download or extraction fails, THEN THE RE_Framework_Service SHALL display an error message on the Game_Card and revert to the pre-install state

### Requirement 4: RE Framework Version Tracking

**User Story:** As a user, I want to see which version of RE Framework is installed so that I know whether I am up to date.

#### Acceptance Criteria

1. WHEN RE Framework is installed for a game, THE Commander SHALL display the version number (e.g. "01302") in the RE Framework status text on the Detail_Panel and Install_Flyout
2. THE RE_Framework_Service SHALL obtain the version number from the GitHub release tag name via the Nightly_Releases_API
3. WHEN RE Framework is not installed for a game, THE Commander SHALL display "Ready" as the RE Framework status text
4. THE RE_Framework_Service SHALL persist the installed version number in the install metadata so the version survives application restarts

### Requirement 5: RE Framework Auto-Update

**User Story:** As a user, I want RE Framework to auto-update so that I always have the latest nightly build without manual intervention.

#### Acceptance Criteria

1. WHEN Commander launches and performs update checks, THE RE_Framework_Service SHALL check the Nightly_Releases_API for a newer release than the installed version
2. WHEN a newer version is available, THE Commander SHALL set the RE Framework status to UpdateAvailable on all RE_Engine_Games that have RE Framework installed
3. WHEN a newer version is available, THE Commander SHALL display "Update" as the RE Framework status text with the update color styling
4. THE Commander SHALL provide no user toggle to disable RE Framework auto-update checking
5. WHEN the "Update All" action is triggered, THE Commander SHALL include RE Framework in the batch update alongside ReShade and ReLimiter updates

### Requirement 6: RE Framework Install Button States

**User Story:** As a user, I want the RE Framework button to reflect the current state so that I know what action will be performed.

#### Acceptance Criteria

1. WHEN RE Framework is not installed for a game, THE install button SHALL display "Install RE Framework"
2. WHEN RE Framework is installed and up to date, THE install button SHALL display "Reinstall RE Framework"
3. WHEN an RE Framework update is available, THE install button SHALL display "Update RE Framework"
4. WHILE RE Framework is being installed, THE install button SHALL display "Installing..." and be disabled
5. THE RE Framework status dot on the Game_Card SHALL display green when installed, amber when an update is available, and grey when not installed

### Requirement 7: RE Framework Uninstallation

**User Story:** As a user, I want to uninstall RE Framework so that I can remove it from a game if I no longer need it.

#### Acceptance Criteria

1. WHEN the user clicks the RE Framework delete button, THE RE_Framework_Service SHALL delete `dinput8.dll` from the game's executable directory
2. WHEN uninstallation completes, THE Commander SHALL reset the RE Framework status to NotInstalled for that game
3. WHEN uninstallation completes, THE Commander SHALL remove the RE Framework install metadata for that game
4. THE delete button SHALL be visible only when RE Framework is installed or has an update available
5. IF the deletion fails (e.g. file is locked), THEN THE RE_Framework_Service SHALL display an error message on the Game_Card
