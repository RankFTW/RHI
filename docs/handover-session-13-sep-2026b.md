# Handover Report — Session 13 September 2026 (Evening)

**Branch:** main  
**App version:** 2.7.1 (in progress, not released)  
**Published:** RHI.exe  
**HEAD:** b744b81

---

## What Was Done This Session

### 1. `rhi_install.txt` Game-Folder Manifest

A new JSON file written to every game folder on OptiScaler install/update. Solves two persistent bugs:

**Version confusion:** OptiScaler was reading the installed version from the staging `version.txt`, which changes when a new version downloads. If you installed nightly `20260912`, then a new nightly downloaded, the card showed the new version instead of what's actually installed. `rhi_install.txt` records the version at install time and is read preferentially on startup.

**Stale uninstall cleanup:** Uninstall previously scanned the staging folder to know which files to delete. If staging changed (new nightly downloaded between install and uninstall), wrong files got deleted. Now uninstall reads the game-folder manifest for the authoritative file list.

**Model:** `Models/RhiInstallManifest.cs`  
Fields: `component`, `variant`, `version`, `installedAs`, `installedAt`, `files`, `folders`, `sharedFiles`, `components`, `nrMethod`  
Static helpers: `Write`, `Read`, `Delete`, `UpdateInstalledAs`, `SetNrMethod`, `AddSharedFileOwner`, `RemoveSharedFileOwner`, `SetComponent`, `GetComponentFiles`, `RemoveComponent`

**Files changed:** `OptiScalerService.Install.cs`, `MainViewModel.BuildCards.cs`, `MainViewModel.CacheLoad.cs`, `DetailPanelBuilder.Overrides.cs`

---

### 2. Shared DLSS File Ownership (`sharedFiles` in manifest)

Multiple components (OptiScaler, ShortFuse NR, DLSS5 Tool, Feeder) all deploy the same `nvngx_dlss*.dll` files to the game folder. Previously they trampled each other's sentinel files and deleted files the other component still needed.

**Solution:** `sharedFiles` dict in `rhi_install.txt` maps each filename to a list of component owners. A file is only deleted/restored when the last owner uninstalls.

**Components wired:**
- OptiScaler (all variants): registers `nvngx_dlss.dll`, `nvngx_dlssd.dll`, `nvngx_dlssg.dll` always; `nvngx_dlssnr.dll` for DlssNr variant only
- ShortFuse: registers all 4 DLLs in `DeploySfDllsAsync`; deregisters in `UninstallSf`
- DLSS5 Tool: registers all 4 in `UpgradeDlssDllsAsync`; deregisters in `Renodx5AddonService.Uninstall`
- Bridge: uses DLSS5 Tool's ownership
- Feeder: registers `nvngx_dlssnr.dll` in `DeployNrDllIfAbsentAsync("Feeder")`; deregisters in `RemoveNrDll("Feeder")`

**Edge case fixed:** When DLSS5 Tool deploys to a detected plugin subfolder (e.g. `Engine\Plugins\Runtime\Nvidia\DLSS\...`) but OptiScaler deployed to the game root, `RestoreDlssDllsWithSentinel` now also cleans up the root copies if the paths differ.

**Files changed:** `Renodx5AddonService.cs`, `DetailPanelBuilder.NeuralRendering.cs`, `OptiScalerService.Install.cs`

---

### 3. Per-Component File Records (`components` in manifest)

Each NR component now records its complete deployed file list in `rhi_install.txt` under a `components` dict. This provides redundancy alongside sentinels — if sentinels are missing or wrong, RHI still knows what to clean up.

**What each component records:**
- `ShortFuse`: `renodx-dlss.addon64` + 4 nvngx DLLs + 11 sl.*.dll files
- `Dlss5Tool`: `renodx-dlss5.addon64` + 4 nvngx DLLs; Bridge adds `dlss5-bridge.addon64`
- `Feeder`: feeder addon + nvngx_dlssnr + nvngx_dlss + renodx-dlss5 + shaders + `ReShadePreset.ini`; 32-bit adds `host64\*`; DX9 adds `D3D9.dll` + `dgVoodoo.conf`

**Important:** OptiScaler's `InstallAsync` and `UpdateAsync` both preserve `sharedFiles`, `components`, and `nrMethod` when rewriting the manifest. Earlier bug: it was creating a fresh `RhiInstallManifest` object which wiped those fields.

---

### 4. GitHub API Rate Limit Fixes

**Token support:** `DevUnlockService.GitHubApiToken` now reads from `%LocalAppData%\RHI\github_api.txt` first (any user), falls back to `github_api=` line in `unlock.txt` (dev only). Token is applied to all `GitHubETagCache.GetWithETagAsync` calls.

**403 handling:** Previously any 403 set `_rateLimited = true` for the entire session. Now only sets it when `X-RateLimit-Remaining: 0` is confirmed — transient 403s no longer kill all API calls for the session. Log message now includes the URL and whether the request was authenticated.

**Files changed:** `DevUnlockService.cs`, `GitHubETagCache.cs`

---

### 5. DB Refresh Fix

RHI database (rhi-repo) changes were not reflecting after standard Refresh — required a full restart. Root cause: `GitHubETagCache` is in-memory and session-scoped. On Refresh, the ETag was still cached so GitHub returned 304 with the old data.

Fix: `InitializeAsync` calls `_renoDxDbService.InvalidateCache()` when `forceRescan=true`, clearing the DB URLs from the ETag cache before fetching.

**Files changed:** `MainViewModel.Init.cs`, `RenoDXDbService.cs`, `IRenoDXDbService.cs`, `GitHubETagCache.cs`

---

### 6. Detail Panel Refresh After Refresh

After a Refresh, `ViewModel.SelectedGame` gets set to null (new card objects don't match old references). `GameList.SelectedItem` still holds the old object so `SelectionChanged` never fires and `PopulateDetailPanel` never runs. Fixed in `MainWindow.UISync.cs`: when `IsLoading` goes false on a silent (Refresh) transition, capture the current `GameList.SelectedItem`, find the equivalent new card in `DisplayedGames`, and force `PopulateDetailPanel`.

**File changed:** `MainWindow.UISync.cs`

---

### 7. UE-Extended DB Notes in Info Dialog

`RenoDXDbUnrealEntry.Comments` was never reaching the info dialog for NativeHDR/UE-Extended games due to three separate blocks:
1. `MergeDbSources` only wrote Comments to `_genericNotes` in DbOnly/Hybrid mode — now always writes regardless of source mode
2. `BuildNotes` early-returned for NativeHDR games before calling `GetGenericNote` — now includes DB comment after the HDR warning
3. `AddonInfoResolver` never read `card.Notes` — now appends DB comment (stripped of the HDR warning line) to the fallback text

---

### 8. MFG Ada Unlock Reinstall Fix

When installed via Extras section, the addon picker toggle is greyed (extrasInstalledConflict). Users couldn't deselect it in the picker. Remove button in Extras now also removes the entry from `EnabledGlobalAddons` and `PerGameAddonSelection`.

**File changed:** `DetailPanelBuilder.Extras.cs`

---

### 9. Other Fixes

- **OptiScaler Stable/Nightly uninstall removed NR-owned `nvngx_dlssnr.dll`** — step 2e now only runs for DlssNr variant
- **DLSS files deleted twice on uninstall** — the generic step 3 loop was re-deleting files that steps 2b/2c/2d had already handled. Fixed by adding `explicitlyHandled` set
- **Batch DLSS deploy stuck** — `MassDlssDeployDialog` now checks `Directory.Exists` before processing each game
- **Shader pack folder renamed to `reshade-shaders-original`** — race condition in `ShaderPackService.Deploy.cs`, fixed by writing the managed marker before rename/deploy
- **Solasta 2 Engine.ini path** — added to `engineIniPathOverrides` in manifest (`"Solasta 2": "Solasta 2"`)
- **Stale OptiScaler sentinels** — `CleanOrphanedOptiScalerSentinels()` runs on startup, removes `.original` files whose base filename doesn't match `InstalledAs`

---

## Active/Pending Items

### Nexus appid Registration
Mark is in correspondence with Nexus support. Update `AppId` const in `NexusSsoService.cs` (~line 20) when they assign an official slug.

### `rhi_install.txt` Limitations
- Existing installs (before this session) have no manifest — uninstall falls back to staging scan. The sentinel-guard fix (only delete root files if `.original` exists in staging scan fallback) helps but isn't perfect for all edge cases.
- The `sharedFiles` tracking only starts working once both components have been installed/reinstalled with the new code. Users who installed OptiScaler + ShortFuse before this update won't have ownership tracking until they reinstall.

---

## Key Files Changed This Session

| File | Change |
|------|--------|
| `Models/RhiInstallManifest.cs` | **New** — full model with all fields and static helpers |
| `Services/OptiScalerService.Install.cs` | Manifest write/read/delete in Install/Update/Uninstall; shared file ownership; preserve fields on write |
| `Services/Renodx5AddonService.cs` | Shared file ownership for ShortFuse/DLSS5Tool; component records; RemoveNrDll with component param |
| `DetailPanelBuilder.NeuralRendering.cs` | Component records for Dlss5Tool/Bridge/Feeder; RestoreDlssDllsWithSentinel root cleanup; ownership calls |
| `DetailPanelBuilder.Overrides.cs` | UpdateInstalledAs called on OS DLL rename (fixes sentinel and manifest on override change) |
| `DetailPanelBuilder.Extras.cs` | MFG Ada Unlock remove also clears addon selection |
| `Services/GitHubETagCache.cs` | Token auth on all requests; proper 403 handling (only rate-limit on Remaining=0) |
| `Services/DevUnlockService.cs` | github_api.txt support for all users |
| `Services/RenoDXDbService.cs` | InvalidateCache() method |
| `Services/IRenoDXDbService.cs` | InvalidateCache() in interface |
| `Services/AddonInfoResolver.cs` | DB Comments appended to NativeHDR fallback text |
| `ViewModels/MainViewModel.cs` | MergeDbSources always writes Comments to _genericNotes |
| `ViewModels/MainViewModel.Init.cs` | DB cache invalidate on Refresh; CleanOrphanedOptiScalerSentinels |
| `ViewModels/MainViewModel.CacheLoad.cs` | BuildNotes includes DB comment for NativeHDR games |
| `ViewModels/MainViewModel.BackgroundScan.cs` | CleanOrphanedOptiScalerSentinels method |
| `ViewModels/MainViewModel.BuildCards.cs` | OsInstalledVersion from manifest first |
| `MainWindow.UISync.cs` | Force panel rebuild for selected game after Refresh |
| `MassDlssDeployDialog.cs` | Directory.Exists guard |
| `manifest.json` | Solasta 2 engineIniPathOverrides |
| `RHI_PatchNotes.md` | v2.7.1 patch notes |

---

## Notes for Next Agent

- Never bump version — Mark does it manually
- Always ask before committing or pushing
- Build: `dotnet build g:\RDXC\RenoDXCommander\RenoDXCommander.csproj --no-restore -v q -p:Platform=x64`
- Publish: close RHI first, then `& "g:\RDXC\publish.bat"` — if RHI is running, the exe is locked and publish silently fails
- Log access: `Copy-Item "$env:LOCALAPPDATA\RHI\Logs\*" "g:\RDXC\Logs\" -Force` then read from `g:\RDXC\Logs\`
