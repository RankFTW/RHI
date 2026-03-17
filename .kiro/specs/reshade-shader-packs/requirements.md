# Requirements Document

## Introduction

RDXC (RenoDXCommander) currently ships with 7 hardcoded shader packs in `ShaderPackService`. The ReShade installer at reshade.me offers a broader set of community shader repositories during its installation wizard. This feature adds all ReShade installer shader packs to RDXC so they are automatically downloaded on first launch, kept up to date, and available for selection via the existing "Select" shader deploy mode.

## Glossary

- **RDXC**: RenoDXCommander — the WinUI 3 desktop application that manages ReShade and HDR shader installations for games.
- **Shader_Pack_Service**: The `ShaderPackService` class responsible for downloading, extracting, versioning, and deploying shader packs.
- **Staging_Directory**: The local folder `%LocalAppData%\RenoDXCommander\reshade\` where shader files (Shaders/ and Textures/) are extracted before deployment.
- **Download_Cache**: The local folder `%LocalAppData%\RenoDXCommander\downloads\` where downloaded archive files are stored.
- **Deploy_Mode**: The enum controlling shader behaviour — Off, Minimum, All, User, Select.
- **Select_Mode**: The Deploy_Mode value that allows users to pick a specific subset of shader packs for deployment.
- **Pack_Definition**: A `ShaderPack` record containing Id, DisplayName, SourceKind, Url, IsMinimum flag, and optional AssetExt.
- **ReShade_Installer_Packs**: The set of shader repositories offered for download through the ReShade installer at reshade.me.
- **Version_Token**: A per-pack string stored in settings.json used to detect whether a newer version is available (GitHub release asset name or HTTP ETag/Last-Modified).
- **Extracted_File_List**: A per-pack JSON array in settings.json recording which files were extracted, used to verify presence on disk.
- **Settings_File**: The `settings.json` file at `%LocalAppData%\RenoDXCommander\settings.json` that stores per-pack version tokens, extracted file lists, and user preferences.
- **Shader_Picker_UI**: The dialog shown when the user is in Select_Mode, listing all available packs with checkboxes.

## Requirements

### Requirement 1: Add ReShade Installer Shader Packs

**User Story:** As an RDXC user, I want all shader packs available through the ReShade installer to be available in RDXC, so that I have access to the same shader options without needing to run the ReShade installer separately.

#### Acceptance Criteria

1. THE Shader_Pack_Service SHALL include Pack_Definitions for all shader repositories offered by the ReShade installer, including but not limited to: crosire/reshade-shaders (standard effects), SweetFX by CeeJay.dk, AstrayFX by BlueSkyDefender, OtisFX by Otis_Inf, qUINT by Marty McFly, and other community packs listed in the ReShade installer configuration.
2. THE Shader_Pack_Service SHALL assign each new ReShade_Installer_Pack a unique Id, a human-readable DisplayName, the correct SourceKind (GhRelease or DirectUrl), and the appropriate download Url.
3. THE Shader_Pack_Service SHALL set the IsMinimum flag to false for all newly added ReShade_Installer_Packs, preserving the existing behaviour where only Lilium HDR Shaders is included in Minimum mode.
4. THE Shader_Pack_Service SHALL retain all 7 existing Pack_Definitions unchanged, preserving their Ids, Urls, and IsMinimum flags.

### Requirement 2: Automatic Download on First Launch

**User Story:** As an RDXC user, I want all shader packs to be automatically downloaded when I first launch RDXC, so that shaders are ready to use without manual intervention.

#### Acceptance Criteria

1. WHEN RDXC starts for the first time (no Version_Token exists for a pack), THE Shader_Pack_Service SHALL download and extract each Pack_Definition to the Staging_Directory.
2. WHEN a pack download fails, THE Shader_Pack_Service SHALL log the error and continue downloading remaining packs without interrupting the startup process.
3. WHEN a pack is downloaded, THE Shader_Pack_Service SHALL store the downloaded archive in the Download_Cache using the naming convention `shaders_{PackId}{extension}`.
4. WHEN a pack is extracted, THE Shader_Pack_Service SHALL record the Extracted_File_List in the Settings_File under the key `ShaderPack_{PackId}_Files`.
5. WHEN a pack is extracted, THE Shader_Pack_Service SHALL save the Version_Token in the Settings_File under the key `ShaderPack_{PackId}_Version`.

### Requirement 3: Automatic Update Detection and Download

**User Story:** As an RDXC user, I want shader packs to be automatically updated when new versions are released, so that I always have the latest shader effects.

#### Acceptance Criteria

1. WHEN RDXC starts and a Version_Token already exists for a pack, THE Shader_Pack_Service SHALL compare the stored Version_Token against the remote version.
2. WHEN the remote version differs from the stored Version_Token, THE Shader_Pack_Service SHALL download and re-extract the updated pack to the Staging_Directory.
3. WHEN the remote version matches the stored Version_Token and the cached archive exists and all Extracted_File_List entries are present on disk, THE Shader_Pack_Service SHALL skip downloading that pack.
4. WHEN the cached archive file is missing from the Download_Cache but the Version_Token matches, THE Shader_Pack_Service SHALL re-download the pack.
5. WHEN one or more files from the Extracted_File_List are missing from the Staging_Directory but the Version_Token matches, THE Shader_Pack_Service SHALL re-extract the pack from the cached archive.
6. WHEN a GhRelease pack is checked, THE Shader_Pack_Service SHALL resolve the download URL and version by querying the GitHub Releases API and selecting the first asset matching the configured AssetExt.
7. WHEN a DirectUrl pack is checked, THE Shader_Pack_Service SHALL resolve the version by sending an HTTP HEAD request and using the ETag or Last-Modified header as the Version_Token.

### Requirement 4: Select Mode Integration

**User Story:** As an RDXC user, I want to choose specific shader packs from the full list including the new ReShade installer packs, so that I can deploy only the shaders I want.

#### Acceptance Criteria

1. THE Shader_Pack_Service SHALL expose all Pack_Definitions (existing and new) through the AvailablePacks property as a list of (Id, DisplayName) tuples.
2. WHEN the user is in Select_Mode, THE Shader_Picker_UI SHALL display all packs from AvailablePacks with checkboxes for individual selection.
3. WHEN the user confirms a selection in Select_Mode, THE Shader_Pack_Service SHALL deploy only the files belonging to the selected pack Ids.
4. WHEN the user changes the selection in Select_Mode, THE Shader_Pack_Service SHALL prune files from previously selected packs that are no longer in the selection.
5. THE Shader_Pack_Service SHALL persist the user's selected pack Ids in the Settings_File under the key `SelectedShaderPacks`.

### Requirement 5: Deployment Compatibility

**User Story:** As an RDXC user, I want the new shader packs to work with all existing deployment modes and locations, so that my current workflow is not disrupted.

#### Acceptance Criteria

1. WHEN Deploy_Mode is All, THE Shader_Pack_Service SHALL deploy files from all Pack_Definitions including the newly added ReShade_Installer_Packs.
2. WHEN Deploy_Mode is Minimum, THE Shader_Pack_Service SHALL deploy only packs with IsMinimum set to true.
3. WHEN Deploy_Mode is Off, THE Shader_Pack_Service SHALL not deploy any pack files.
4. WHEN Deploy_Mode is User, THE Shader_Pack_Service SHALL deploy only files from the Custom folder, ignoring all Pack_Definitions.
5. WHEN deploying to the DC global Reshade folder, THE Shader_Pack_Service SHALL use the same pack filtering and pruning logic as game-local deployment.
6. WHEN deploying to a game-local reshade-shaders folder, THE Shader_Pack_Service SHALL use the same pack filtering and pruning logic as DC folder deployment.
7. WHEN a per-game shader mode override is set, THE Shader_Pack_Service SHALL respect the override instead of the global Deploy_Mode for that game's folder.

### Requirement 6: Archive Extraction

**User Story:** As an RDXC user, I want shader pack archives to be correctly extracted regardless of their format, so that all shader and texture files are available.

#### Acceptance Criteria

1. WHEN extracting an archive, THE Shader_Pack_Service SHALL place files from `Shaders/` paths into the Staging_Directory Shaders subfolder and files from `Textures/` paths into the Staging_Directory Textures subfolder.
2. WHEN an archive contains nested directory structures within Shaders/ or Textures/, THE Shader_Pack_Service SHALL preserve the full subdirectory hierarchy during extraction.
3. THE Shader_Pack_Service SHALL support extraction of .zip and .7z archive formats.
4. IF an archive extraction fails, THEN THE Shader_Pack_Service SHALL log the error and skip that pack without affecting other packs.

### Requirement 7: Per-Pack File Tracking Integrity

**User Story:** As an RDXC user, I want RDXC to track which files belong to which shader pack, so that switching modes or selections correctly adds and removes the right files.

#### Acceptance Criteria

1. WHEN a pack is extracted, THE Shader_Pack_Service SHALL record every extracted file path (relative to the Staging_Directory) in the Extracted_File_List for that pack.
2. WHEN pruning files during a mode or selection change, THE Shader_Pack_Service SHALL remove only files that belong to packs no longer eligible under the current mode or selection.
3. WHEN pruning files, THE Shader_Pack_Service SHALL not remove files that were not placed by any known Pack_Definition (user-added files remain untouched).
4. FOR ALL Pack_Definitions, saving the Extracted_File_List then reading the Extracted_File_List SHALL produce an equivalent list of file paths (round-trip property).
