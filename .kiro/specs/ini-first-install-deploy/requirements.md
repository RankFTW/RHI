# Requirements Document

## Introduction

When ReLimiter or Display Commander is installed to a game's directory for the first time, the user may already have a pre-configured INI file in the AppData `inis` folder (`%LocalAppData%\RHI\inis\`). Currently, these INI files are only deployed to the game folder via a manual "Copy INI" button click. This feature adds automatic deployment of `relimiter.ini` and `DisplayCommander.ini` from the AppData INI folder to the game directory on first install, but only when the INI file does not already exist in the game folder. This lets users maintain a single pre-configured INI that seeds every new game automatically.

## Glossary

- **INI_Deployer**: The component responsible for copying INI configuration files from the AppData INI folder to a game's addon deploy path during first install.
- **AppData_INI_Folder**: The directory `%LocalAppData%\RHI\inis\` where user-configured INI templates are stored.
- **Game_Deploy_Path**: The resolved addon deploy path for a game, determined by `ModInstallService.GetAddonDeployPath(installPath)`.
- **ReLimiter_INI**: The file `relimiter.ini` used to configure ReLimiter behaviour.
- **DisplayCommander_INI**: The file `DisplayCommander.ini` used to configure Display Commander behaviour.
- **First_Install**: An installation of ReLimiter or Display Commander to a game directory where the corresponding addon file was not previously present.

## Requirements

### Requirement 1: Deploy ReLimiter INI on First Install

**User Story:** As a user, I want my pre-configured `relimiter.ini` to be automatically copied to a game's folder when I install ReLimiter for the first time, so that I do not have to manually copy the INI for every game.

#### Acceptance Criteria

1. WHEN ReLimiter is installed to a game directory AND `relimiter.ini` does not exist in the Game_Deploy_Path, THE INI_Deployer SHALL copy `relimiter.ini` from the AppData_INI_Folder to the Game_Deploy_Path.
2. WHEN ReLimiter is installed to a game directory AND `relimiter.ini` already exists in the Game_Deploy_Path, THE INI_Deployer SHALL leave the existing `relimiter.ini` unchanged.
3. IF `relimiter.ini` does not exist in the AppData_INI_Folder, THEN THE INI_Deployer SHALL skip the copy without raising an error.

### Requirement 2: Deploy Display Commander INI on First Install

**User Story:** As a user, I want my pre-configured `DisplayCommander.ini` to be automatically copied to a game's folder when I install Display Commander for the first time, so that I do not have to manually copy the INI for every game.

#### Acceptance Criteria

1. WHEN Display Commander is installed to a game directory AND `DisplayCommander.ini` does not exist in the Game_Deploy_Path, THE INI_Deployer SHALL copy `DisplayCommander.ini` from the AppData_INI_Folder to the Game_Deploy_Path.
2. WHEN Display Commander is installed to a game directory AND `DisplayCommander.ini` already exists in the Game_Deploy_Path, THE INI_Deployer SHALL leave the existing `DisplayCommander.ini` unchanged.
3. IF `DisplayCommander.ini` does not exist in the AppData_INI_Folder, THEN THE INI_Deployer SHALL skip the copy without raising an error.

### Requirement 3: Preserve Existing Game-Specific Configuration

**User Story:** As a user, I want my per-game INI customisations to be preserved when I reinstall or update ReLimiter or Display Commander, so that I do not lose game-specific settings.

#### Acceptance Criteria

1. WHEN ReLimiter is reinstalled (addon file overwritten) to a game directory AND `relimiter.ini` already exists in the Game_Deploy_Path, THE INI_Deployer SHALL not overwrite the existing `relimiter.ini`.
2. WHEN Display Commander is reinstalled (addon file overwritten) to a game directory AND `DisplayCommander.ini` already exists in the Game_Deploy_Path, THE INI_Deployer SHALL not overwrite the existing `DisplayCommander.ini`.

### Requirement 4: INI Deploy Failure Handling

**User Story:** As a user, I want the install to succeed even if the INI copy fails, so that a file permission issue does not block the addon installation.

#### Acceptance Criteria

1. IF the INI file copy from the AppData_INI_Folder to the Game_Deploy_Path fails due to an I/O error, THEN THE INI_Deployer SHALL log the error via CrashReporter and continue the install without interruption.
2. IF the INI file copy fails, THEN THE INI_Deployer SHALL not alter the install status or success message of the addon installation.
