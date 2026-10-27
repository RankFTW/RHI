# Requirements Document

## Introduction

The RHI application currently stores all downloaded files (shader packs, RenoDX addons, frame limiter caches, installers) in a single flat `%LOCALAPPDATA%\RHI\downloads\` directory. As the number of cached files grows, this becomes difficult to reason about and maintain. This feature reorganises the downloads folder into categorised subdirectories and provides a one-time migration for existing users so no cached files are lost.

## Glossary

- **RHI_App**: The RenoDX Commander (RHI) desktop application
- **Downloads_Root**: The directory `%LOCALAPPDATA%\RHI\downloads\`
- **Shader_Cache**: Zip or 7z archives containing HDR ReShade shader packs, stored in `Downloads_Root\shaders\`
- **RenoDX_Cache**: RenoDX mod addon files (`renodx-*.addon64`, `renodx-*.addon32`), stored in `Downloads_Root\renodx\`
- **FrameLimiter_Cache**: ReLimiter and Display Commander addon files and metadata, stored in `Downloads_Root\framelimiter\`
- **Misc_Cache**: ReShade installer executables, RHI update installers, and any other downloaded files, stored in `Downloads_Root\misc\`
- **Luma_Cache**: Luma mod zip files, stored in `Downloads_Root\luma\`
- **REFramework_Cache**: RE Framework zip files, stored in `Downloads_Root\reframework\` (already uses a subdirectory)
- **Migration_Marker**: A sentinel file at `Downloads_Root\.reorganised` indicating that the one-time migration has already been performed
- **Migration_Service**: The component responsible for executing the one-time file migration from the flat layout to the categorised layout
- **ShaderPackService**: The service that downloads, extracts, and deploys HDR ReShade shader packs
- **ModInstallService**: The service that downloads, installs, updates, and uninstalls RenoDX addon files
- **AuxInstallService**: The service that manages ReShade, ReLimiter, and Display Commander auxiliary files
- **LumaService**: The service that downloads and manages Luma mod files
- **UpdateService**: The service that checks for and downloads RHI application updates

## Requirements

### Requirement 1: Subdirectory Structure

**User Story:** As a developer, I want downloaded files organised into categorised subdirectories, so that the downloads folder is easier to understand and maintain.

#### Acceptance Criteria

1. THE RHI_App SHALL organise Downloads_Root into the following subdirectories: `shaders/`, `renodx/`, `framelimiter/`, `misc/`, and `luma/`
2. WHEN a subdirectory does not exist at the time a service needs to write a file, THE service SHALL create the subdirectory before writing

### Requirement 2: ShaderPackService Path Update

**User Story:** As a user, I want shader pack downloads stored in a dedicated `shaders/` subfolder, so that shader caches are separated from other downloads.

#### Acceptance Criteria

1. THE ShaderPackService SHALL resolve all cache zip and 7z file paths against `Downloads_Root\shaders\` instead of Downloads_Root
2. WHEN the ShaderPackService downloads a shader pack, THE ShaderPackService SHALL write the file to `Downloads_Root\shaders\`
3. WHEN the ShaderPackService checks cached file hashes for version comparison, THE ShaderPackService SHALL read from `Downloads_Root\shaders\`

### Requirement 3: ModInstallService Path Update

**User Story:** As a user, I want RenoDX addon downloads stored in a dedicated `renodx/` subfolder, so that mod files are separated from other downloads.

#### Acceptance Criteria

1. THE ModInstallService SHALL resolve all addon cache file paths against `Downloads_Root\renodx\` instead of Downloads_Root
2. WHEN the ModInstallService downloads an addon file, THE ModInstallService SHALL write the file to `Downloads_Root\renodx\`
3. WHEN the ModInstallService performs an update check by downloading a remote file, THE ModInstallService SHALL write temporary files to `Downloads_Root\renodx\`
4. WHEN the ModInstallService cleans up orphaned temporary files, THE ModInstallService SHALL enumerate files in `Downloads_Root\renodx\`

### Requirement 4: AuxInstallService Path Update

**User Story:** As a user, I want auxiliary component downloads stored in appropriate subfolders, so that frame limiter files and miscellaneous installers are separated.

#### Acceptance Criteria

1. THE AuxInstallService SHALL resolve ReLimiter cache file paths (`relimiter.addon64`, `relimiter.addon32`) against `Downloads_Root\framelimiter\`
2. THE AuxInstallService SHALL resolve Display Commander cache file paths (`dc_lite.addon64`, `dc_lite.addon32`) against `Downloads_Root\framelimiter\`
3. THE AuxInstallService SHALL resolve metadata files (`ul_meta.json`, `dc_meta.json`) against `Downloads_Root\framelimiter\`
4. THE AuxInstallService SHALL resolve ReShade installer cache paths against `Downloads_Root\misc\`

### Requirement 5: LumaService Path Update

**User Story:** As a user, I want Luma mod downloads stored in a dedicated `luma/` subfolder, so that Luma caches are separated from other downloads.

#### Acceptance Criteria

1. THE LumaService SHALL resolve all Luma mod cache file paths against `Downloads_Root\luma\` instead of Downloads_Root
2. WHEN the LumaService downloads a Luma mod zip, THE LumaService SHALL write the file to `Downloads_Root\luma\`

### Requirement 6: UpdateService Path Update

**User Story:** As a user, I want RHI update installers stored in the `misc/` subfolder, so that app update files are separated from other downloads.

#### Acceptance Criteria

1. WHEN the UpdateService downloads an RHI installer, THE UpdateService SHALL write the file to `Downloads_Root\misc\`

### Requirement 7: Centralised Download Path Constants

**User Story:** As a developer, I want all download subdirectory paths defined in a single location, so that path references are consistent and easy to update.

#### Acceptance Criteria

1. THE RHI_App SHALL define all download subdirectory paths (shaders, renodx, framelimiter, misc, luma) in a single shared location
2. THE ModInstallService, AuxInstallService, ShaderPackService, LumaService, and UpdateService SHALL reference the shared path constants instead of constructing paths independently
3. THE shared path constants SHALL each resolve to the correct subdirectory under Downloads_Root

### Requirement 8: One-Time Migration

**User Story:** As an existing user upgrading to the new version, I want my cached downloads automatically moved into the new subdirectories, so that I do not need to re-download files after the update.

#### Acceptance Criteria

1. WHEN the RHI_App starts and the Migration_Marker does not exist, THE Migration_Service SHALL execute the file migration
2. WHEN the Migration_Marker exists, THE Migration_Service SHALL skip the migration entirely
3. THE Migration_Service SHALL run before any download service accesses Downloads_Root
4. THE Migration_Service SHALL create all subdirectories (`shaders/`, `renodx/`, `framelimiter/`, `misc/`, `luma/`) before moving files
5. WHEN the Migration_Service encounters files matching `*.7z` or `*.zip` in Downloads_Root, THE Migration_Service SHALL move those files to `Downloads_Root\shaders\`
6. WHEN the Migration_Service encounters files matching `renodx-*.addon64` or `renodx-*.addon32` in Downloads_Root, THE Migration_Service SHALL move those files to `Downloads_Root\renodx\`
7. WHEN the Migration_Service encounters files matching `relimiter.*`, `dc_lite.*`, `ul_meta.json`, or `dc_meta.json` in Downloads_Root, THE Migration_Service SHALL move those files to `Downloads_Root\framelimiter\`
8. WHEN the Migration_Service encounters files matching `luma_*` in Downloads_Root, THE Migration_Service SHALL move those files to `Downloads_Root\luma\`
9. WHEN the Migration_Service encounters any remaining unmatched files in Downloads_Root (excluding subdirectories and the Migration_Marker), THE Migration_Service SHALL move those files to `Downloads_Root\misc\`
10. WHEN the Migration_Service completes all file moves, THE Migration_Service SHALL write the Migration_Marker file
11. THE Migration_Service SHALL log each file move operation for debugging purposes

### Requirement 9: Migration Error Handling

**User Story:** As a user, I want the migration to handle errors gracefully, so that a failed file move does not prevent the application from starting.

#### Acceptance Criteria

1. IF a file move fails during migration, THEN THE Migration_Service SHALL log the error and continue processing remaining files
2. IF the Migration_Service fails to create a subdirectory, THEN THE Migration_Service SHALL log the error and skip files destined for that subdirectory
3. IF the migration completes with one or more file move failures, THEN THE Migration_Service SHALL still write the Migration_Marker file
4. IF the Migration_Marker file cannot be written, THEN THE Migration_Service SHALL log the error and allow the application to continue starting

### Requirement 10: Migration Idempotence

**User Story:** As a developer, I want the migration to be safe to re-run, so that partially completed migrations do not cause data loss or duplication.

#### Acceptance Criteria

1. WHEN a destination file already exists in the target subdirectory, THE Migration_Service SHALL skip that file move without error
2. THE Migration_Service SHALL use move operations (not copy-then-delete) to avoid temporary disk space doubling
3. WHEN the Migration_Service moves a file, THE Migration_Service SHALL preserve the original filename
