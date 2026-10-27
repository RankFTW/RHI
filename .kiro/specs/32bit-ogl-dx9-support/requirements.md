# Requirements Document

## Introduction

This feature introduces three enhancements to RHI (ReShade HDR Installer): 32-bit ReLimiter support, automatic OpenGL DLL naming for ReShade, and automatic DirectX 9 DLL naming for ReShade. It also includes a version bump to 1.6.4, patch notes for the release, and documentation updates to README.md and the Detailed Guide to reflect all current functionality.

## Glossary

- **RHI**: ReShade HDR Installer — the desktop application that manages HDR game mod installations
- **ReLimiter**: A frame pacing addon downloaded from GitHub, currently only available as a 64-bit addon (`relimiter.addon64`)
- **ReShade**: A post-processing injector DLL installed into game folders, supporting both 32-bit and 64-bit variants
- **Graphics_API**: The rendering API detected via PE header import table scanning (DirectX 8/9/10/11/12, Vulkan, OpenGL)
- **Install_Service**: The component responsible for deploying ReShade and ReLimiter files to game folders
- **Game_Card**: The UI view model representing a detected game, including its `Is32Bit` property and detected `GraphicsApiType`
- **Manifest**: The remote JSON manifest providing per-game overrides including `dllNameOverrides`, `thirtyTwoBitGames`, and `sixtyFourBitGames`
- **Patch_Notes**: The `RHI_PatchNotes.md` file bundled with the app and displayed on first launch after an update

## Requirements

### Requirement 1: 32-bit ReLimiter Support

**User Story:** As a user with 32-bit games, I want RHI to install a 32-bit version of ReLimiter, so that 32-bit games can benefit from ReLimiter frame pacing.

#### Acceptance Criteria

1. WHEN a game is detected as 32-bit, THE Install_Service SHALL download and install the 32-bit ReLimiter addon file (`relimiter.addon32`) instead of the 64-bit variant (`relimiter.addon64`)
2. WHEN a game is detected as 64-bit, THE Install_Service SHALL continue to install the 64-bit ReLimiter addon file (`relimiter.addon64`)
3. THE Install_Service SHALL download the 32-bit ReLimiter addon from the same GitHub releases endpoint and release URL used for the 64-bit addon (`https://api.github.com/repos/RankFTW/Ultra-Limiter/releases/latest`), selecting the asset by filename (`relimiter.addon32`)
4. THE Install_Service SHALL cache the 32-bit ReLimiter addon separately from the 64-bit addon in the downloads cache directory
5. THE Install_Service SHALL detect updates for the 32-bit ReLimiter addon independently using the same file size and SHA-256 hash comparison used for the 64-bit addon
6. WHEN a 32-bit game has ReLimiter installed, THE Game_Card SHALL display the same ReLimiter status indicators (installed, update available) as 64-bit games, including the clickable "Installed" link that opens the ReLimiter feature guide and the clickable "Update" status
7. WHEN uninstalling ReLimiter from a 32-bit game, THE Install_Service SHALL remove the `relimiter.addon32` file from the game folder and any AddonPath location

### Requirement 2: Automatic OpenGL ReShade DLL Naming

**User Story:** As a user with OpenGL games, I want RHI to automatically install ReShade as `opengl32.dll` when a game is detected as OpenGL-only, so that ReShade loads correctly without manual DLL override configuration.

#### Acceptance Criteria

1. WHEN a game is detected with OpenGL as the only graphics API and no user-set DLL override exists, THE Install_Service SHALL install ReShade as `opengl32.dll` instead of the default `dxgi.dll`
2. WHEN a game is detected with OpenGL as the only graphics API, THE Install_Service SHALL apply the `opengl32.dll` naming for both 32-bit and 64-bit ReShade installs
3. WHEN a game has a user-set DLL override, THE Install_Service SHALL use the user-set override instead of the automatic OpenGL naming
4. WHEN a game has a manifest `dllNameOverrides` entry for ReShade, THE Install_Service SHALL use the manifest override instead of the automatic OpenGL naming
5. WHEN a game has OpenGL detected alongside other graphics APIs (multi-API), THE Install_Service SHALL use the default `dxgi.dll` naming
6. WHEN uninstalling ReShade from an OpenGL game where ReShade was installed as `opengl32.dll`, THE Install_Service SHALL remove the `opengl32.dll` file

### Requirement 3: Automatic DirectX 9 ReShade DLL Naming

**User Story:** As a user with DirectX 9 games, I want RHI to automatically install ReShade as `d3d9.dll` when a game is detected as DX9, so that ReShade loads correctly without manual DLL override configuration.

#### Acceptance Criteria

1. WHEN a game is detected with DirectX 9 as the graphics API and no user-set DLL override exists, THE Install_Service SHALL install ReShade as `d3d9.dll` instead of the default `dxgi.dll`
2. WHEN a game is detected with DirectX 9 as the graphics API, THE Install_Service SHALL apply the `d3d9.dll` naming for both 32-bit and 64-bit ReShade installs
3. WHEN a game has a user-set DLL override, THE Install_Service SHALL use the user-set override instead of the automatic DX9 naming
4. WHEN a game has a manifest `dllNameOverrides` entry for ReShade, THE Install_Service SHALL use the manifest override instead of the automatic DX9 naming
5. WHEN uninstalling ReShade from a DX9 game where ReShade was installed as `d3d9.dll`, THE Install_Service SHALL remove the `d3d9.dll` file

### Requirement 4: Version Bump to 1.6.4

**User Story:** As a developer, I want the app version bumped to 1.6.4, so that the release is properly versioned.

#### Acceptance Criteria

1. THE RHI SHALL set `AssemblyVersion` in `RenoDXCommander.csproj` to `1.6.4.0`
2. THE RHI SHALL set `FileVersion` in `RenoDXCommander.csproj` to `1.6.4.0`

### Requirement 5: Patch Notes for v1.6.4

**User Story:** As a user, I want to see patch notes for v1.6.4, so that I know what changed in the new release.

#### Acceptance Criteria

1. THE Patch_Notes SHALL contain a `## v1.6.4` section prepended before the existing `## v1.6.3` section in `RHI_PatchNotes.md`
2. THE Patch_Notes SHALL document the 32-bit ReLimiter support feature
3. THE Patch_Notes SHALL document the automatic OpenGL ReShade DLL naming feature
4. THE Patch_Notes SHALL document the automatic DirectX 9 ReShade DLL naming feature

### Requirement 6: Documentation Updates

**User Story:** As a user, I want the README and Detailed Guide to accurately reflect all current features, so that I can find correct information about RHI capabilities.

#### Acceptance Criteria

1. THE README.md SHALL document 32-bit ReLimiter support in the features list
2. THE README.md SHALL document automatic OpenGL and DX9 ReShade DLL naming in the features list or relevant section
3. THE docs/DETAILED_GUIDE.md SHALL update the ReLimiter section to describe 32-bit addon support and the automatic bitness-based file selection
4. THE docs/DETAILED_GUIDE.md SHALL update the Components section to describe automatic DLL naming for OpenGL and DX9 games
5. THE docs/DETAILED_GUIDE.md SHALL update the Graphics API Detection section to note that OpenGL-only and DX9 detections now drive automatic ReShade DLL naming
6. THE docs/DETAILED_GUIDE.md SHALL correct any outdated information found during the update (e.g. stale references to removed features, incorrect descriptions of current behaviour)
7. THE README.md SHALL correct any outdated information found during the update
