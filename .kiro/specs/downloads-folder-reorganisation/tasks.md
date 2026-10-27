# Tasks: Downloads Folder Reorganisation

## Task 1: Create DownloadPaths static class
- [x] 1.1 Create `RenoDXCommander/Services/DownloadPaths.cs` with `Root`, `Shaders`, `RenoDX`, `FrameLimiter`, `Misc`, `Luma`, and `AllSubdirectories` static fields
- [x] 1.2 Write property test: `DownloadPathsResolveUnderRootPropertyTests.cs` — for any path in AllSubdirectories, it starts with Root and ends with the expected folder name (Property 2)

## Task 2: Create DownloadsMigrationService
- [x] 2.1 Create `RenoDXCommander/Services/DownloadsMigrationService.cs` with `RunOnce()` and `ClassifyFile(string fileName)` methods
- [x] 2.2 Implement `ClassifyFile` with priority-ordered pattern matching (renodx addons → renodx, relimiter/dc_lite/meta → framelimiter, luma_ → luma, .7z/.zip → shaders, else → misc)
- [x] 2.3 Implement `RunOnce`: check marker, create subdirectories, move files by classification, write marker, log each move
- [x] 2.4 Write property test: `FileClassificationPropertyTests.cs` — for any filename, ClassifyFile returns the correct category per priority rules (Property 1)
- [x] 2.5 Write property test: `MigrationIdempotencePropertyTests.cs` — for any set of files where some already exist at destination, migration skips existing files without error (Property 3)
- [x] 2.6 Write unit tests: `DownloadsMigrationServiceTests.cs` — marker-absent triggers migration, marker-present skips, marker written after completion, continues after individual move failure, handles subdirectory creation failure, handles marker write failure

## Task 3: Update service path references
- [x] 3.1 Update `ShaderPackService.Download.cs`: replace `AuxInstallService.DownloadCacheDir` with `DownloadPaths.Shaders`
- [x] 3.2 Update `LiliumShaderService.cs`: replace `AuxInstallService.DownloadCacheDir` with `DownloadPaths.Shaders`
- [x] 3.3 Update `ModInstallService.cs`: replace local `DownloadCacheDir` field with `DownloadPaths.RenoDX`, keep `DownloadCacheDir` as a deprecated alias pointing to `DownloadPaths.Root` for any external references
- [x] 3.4 Update `AuxInstallService.cs`: replace `DownloadCacheDir` alias with `DownloadPaths.FrameLimiter` for relimiter/dc_lite paths and `DownloadPaths.Misc` for ReShade paths
- [x] 3.5 Update `AuxInstallService.Install.cs`: replace `DownloadCacheDir` references with appropriate `DownloadPaths` constants
- [x] 3.6 Update `LumaService.cs`: replace local `DownloadCacheDir` field with `DownloadPaths.Luma`
- [x] 3.7 Update `MainViewModel.Install.cs`: replace `ModInstallService.DownloadCacheDir` in `GetUlCachePath`, `GetDcCachePath`, and `UlMetaPath`/`DcMetaPath` with `DownloadPaths.FrameLimiter`
- [x] 3.8 Update `SettingsHandler.cs`: replace `ModInstallService.DownloadCacheDir` with `DownloadPaths.Root`

## Task 4: Integrate migration into app startup
- [x] 4.1 Add `DownloadsMigrationService.RunOnce()` call in `App.OnLaunched` after `MigrateLegacyAppData()` and before service resolution

## Task 5: Clean up deprecated path fields
- [x] 5.1 Remove or deprecate `ModInstallService.DownloadCacheDir` static field (update any remaining references)
- [x] 5.2 Remove or deprecate `AuxInstallService.DownloadCacheDir` static field
- [x] 5.3 Remove `LumaService.DownloadCacheDir` private field
- [x] 5.4 Verify no remaining direct path constructions to `RHI/downloads` exist outside `DownloadPaths`
