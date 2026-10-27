---
inclusion: fileMatch
fileMatchPattern: "*Install*,*Update*,*Orchestration*"
---

# Install, Update & Orchestration Knowledge

## Update All
- Respects per-game exclusions (Update Inclusion dialog)
- `AnyUpdateAvailable` property drives the purple button state
- Must call `NotifyUpdateButtonChanged()` after any status change
- Cache phase restores saved update statuses from `game_library.json` (`UpdateAvailableSnapshot`)
- `SaveLibrary()` must be called after `CheckForUpdatesAsync` completes
- Background scan merge must preserve `UpdateAvailable` (fresh cards always have `Status = Installed`)
- Foreign DLLs silently skipped. DLL override games ARE eligible (resolve via `dllOverrideService`).
- **After batch completion**: must call `NotifyUpdateButtonChanged()` + reset Nexus baselines by name

### Update Status Persistence
- REF/Luma: restore guard `if (status != NotInstalled)` fails during cache phase (detection only on background scan). Fix: remove guard, set unconditionally from snapshot.
- Nexus updates for non-external games: snapshot may not persist if `SaveLibrary()` timing is wrong.
- RDX cooldown blocks re-check: if not saved after first detection, lost on restart.

### Stale Update Indicators
- Luma false update: check must reset cards from `UpdateAvailable` to `Installed` when no update exists.
- Nexus false positives: `updatedAt` changes for any page edit. Reset baseline on button click + install.
- Update exclusion not respected on restore: check exclusion flags before restoring status.
- Nexus badge on uninstalled games: `UpdateBadgeVisibility` requires installed evidence.

## Install Flows

### ReShade
- 3 channels (Stable/Nightly/Legacy/Custom). Per-game override.
- Vulkan games: global layer at `C:\ProgramData\ReShade\` + elevation
- Legacy: on-demand download from manifest URL list
- Custom: per-game picker from `Custom\ReShade\` folder

### RenoDX
- Standard: download addon from wiki snapshot URL, deploy to game folder
- UE-Extended: toggle visible on ALL UE games. Toggle ON uninstalls named mod. No auto-install.
- `nativeHdrGames` supersedes `ueExtendedGames` (both use UE-Extended, native HDR has Set_Path=0)

### Engine.ini Deployment
- Auto for UE5 UE-Extended games on install/Update All. **UE4 UE-Extended: Engine.ini HDR keys skipped by default** (no native HDR pipeline). User can still enable manually via cog dialog.
- `ApplyEngineIniHdrSettings`: resolves project name from install path, finds platform subfolder
- Platform priority: WinGDK → Windows → WindowsNoEditor → create Windows
- **Never use ParseIni/WriteIni** (destroys duplicate keys). Use append-based approach.
- Set read-only after writing. Remove before writing.
- `InstalledModRecord.EngineIniHdr` boolean controls whether it fires (preserved across reinstall).
- `r.LUT.UpdateEveryFrame=1` unconditional for all UE games regardless of version (separate toggle).
- Alternate locations: Documents\My Games, in-game directory `{Root}\{Project}\Saved\Config\`
- Generic "Project" folder: check grandparent if candidate has no matching AppData folder.
- Pipe-separated paths in override for multi-store games.

### Native HDR / UE-Extended reshade.ini — UE4 vs UE5 Differentiation
- `ApplyRenoDxNativeHdrSettings(gameDir, usesSdrPath)`: UE5 gets `Set_Path=0` (native HDR), UE4 gets `Set_Path=1` (SDR upgrade)
- UE4 detection: `card.EngineHint?.Contains("Unreal Engine 4")` — requires accurate `EngineHint` (from auto-detect or `engineHintOverrides` manifest field)
- Only affects FRESH installs — method only adds missing keys, never overwrites existing user values
- Deploy ReShade.ini button callers use default (`usesSdrPath: false`) — safe for already-installed games
- **Record field must reflect skipped deployment**: When Engine.ini HDR is skipped for UE4 games, `InstalledModRecord.EngineIniHdr` must be explicitly set to `false` and saved. Otherwise the cog dialog reads the default (`true`) and shows "On" despite nothing being deployed. Same pattern applies to any conditional feature skip — the record must always reflect the actual deployed state.
- **Use `record` (local variable from `InstallAsync`), NOT `card.InstalledRecord`**: On fresh installs, `card.InstalledRecord` is still null when post-install logic runs — it's only assigned later in the dispatcher. The local `record` variable IS the correct target for modifications. Same issue doesn't affect reinstalls (card already has a record from the previous install).
- **Update All must NOT override user's explicit toggle state for UE4 games**: The UE4 `EngineIniHdr = false` logic only belongs in the fresh install path (`InstallModAsync`). The Update All path must respect the existing record value — if the user manually enabled HDR via cog dialog (`EngineIniHdr = true`), Update All should honour that and deploy HDR keys. Only fresh installs (no previous record) get the UE4 default of "Off".

### engineHintOverrides (Manifest Field)
- Maps game name → full engine hint string (e.g. `"Unreal Engine 4.27.2"`, `"Unreal Engine 5.4.3"`)
- Applied in `BuildCards` + `LoadCacheAndBuildCardsAsync` AFTER auto-detection, BEFORE user badge override
- Priority: user badge override > manifest override > auto-detection
- Use cases: Game Pass games (ACLs prevent version detection), accurate DOF Fix eligibility, UE4 vs UE5 differentiation
- **Engine badge click blocked** when `EngineHint` contains a specific version number (regex `Unreal Engine \d`). Only toggleable when hint is vague "Unreal Engine" or "Unreal Engine 5". Works regardless of version source (manifest override, auto-detection, or user override). Does NOT depend on `ViewModel.Manifest` being loaded — safe during cache phase.
- **Badge visual styling must match click handler**: `DetailPanelBuilder.cs` determines `isClickable` (underline, tooltip, hand cursor) separately from the `EngineBadge_PointerPressed` handler guard. Both use the same regex check. If they disagree, the badge looks clickable but does nothing (confusing UX). The `isClickable` logic: `hasSpecificVersion` (regex) → false. Only vague "Unreal Engine"/"Unreal Engine 5" with a user override makes it clickable again.
- **Stale user EngineVersionOverrides conflict**: If a user previously clicked the badge (setting e.g. `"Palworld": "Unreal Engine 5"` in settings.json) and a manifest `engineHintOverrides` entry is later added for the same game, the stale user override won't fire (guard requires `EngineHint == "Unreal Engine"` which is false after manifest applies). BUT if the user's override was set when no manifest override existed, it persists in settings.json. On the FIRST launch after adding the manifest override, the manifest applies first → user override guard fails → correct behavior. However if somehow the manifest loads AFTER the user override is checked, the stale value could stick. The priority chain (manifest > user override check) protects against this in normal flow.

### renodxIniOverrides
- Manifest field: game → { key: value } for `[renodx]` section
- `forceOverwrite=false` on install (only adds missing), `true` on redeploy button
- Called from `InstallModAsync` and `UpdateAllRenoDxAsync`

### RenoDX [renodx] Key Placeholders
- `ApplyRenodxKeyPlaceholders`: pre-writes empty keys for Unreal/Unity games
- Triggers for `!UseUeExtended` generic UE/Unity games only
- Uninstall cleanup: removes entire `[renodx]` section

### Native HDR Settings
- `ApplyRenoDxNativeHdrSettings`: inserts `[renodx]` section for UE-Extended
- Upgrade keys MUST be empty (not "0") — addon populates game-specific defaults
- Only fires when `card.UseUeExtended` is true during install

### ReLimiter / Display Commander
- Standard addon deployment
- Auto-disable driver FPS cap on install. Delete setting on uninstall.

### OptiScaler
- Installs as `dxgi.dll`, renames existing ReShade → `ReShade64.dll`
- Uninstall: only delete DLSS DLLs if `.original` backup exists (OptiScaler placed them)
- `EnsureStagingAsync` skip guard: `if (IsStagingReady && !HasUpdate) return;`

### RE Framework
- Standard component install
- REF status only detected during background scan — snapshot restore guard must be removed

### DOF Fix
- Addon: `renodx-universal_ue_dof_fix.addon64`
- Eligibility: `EngineHint` contains "Unreal Engine 5" + minor 0–6 + 64-bit + not in skip list
- `dofFixForceGames` bypasses version check (requires 64-bit still)
- Game Pass: UE version detection fails → add to `dofFixForceGames` manually
- Excluded from `AddonFileWatcher` and "existing addon" mod replace check
- Engine badge toggle OFF must uninstall DOF Fix

### Global Addon Exclusion from Mod Detection
- Addons that start with `renodx-` but are NOT game-specific mods must be excluded from `ScanForInstalledAddon`:
  - `renodx-devkit` (development tools)
  - `renodx-dlssfix` (DLSS frame gen fix)
  - `renodx-upgrade` (ITM/resource upgrades — global utility)
  - `renodx-universal_ue` (DOF Fix)
- Helper: `IsGameSpecificRenodxAddon(fileName)` in `MainViewModel.CacheLoad.cs`
- Same exclusion needed in: `ScanForInstalledAddon`, `ScanForInstalledAddonQuick`, `DragDropHandler.Addon.cs` (warning check + removal loop), `MainViewModel.Install.Luma.cs` (Luma toggle cleanup)
- Without exclusion: these global addons are detected as the game's installed RenoDX mod, showing wrong filename badge and blocking actual mod detection

### Luma
- `LumaService.InstallFromArchiveAsync` — handles zip/7z
- If no `dxgi.dll` in archive → deploy cached stable ReShade
- Shaders deployed via `SyncGameFolder` (respects shader selection)
- `skipWarning` parameter: no `[RelayCommand]` (generator doesn't support optional params)

### DLSS Fix
- `ApplyOrRemoveDlssFixIni`: writes `[ADDON]` LoadFromDllMain + `[RENODX-DLSSFIX]` paths
- Falls back to `TryFastDetect` when `card.DlssDetection` is null (cached startup)
- Early return paths in `DeployAddonsForCard` must also call the INI method

## Update Check Logic

### RenoDX Updates
- Content-Length comparison for standard addons
- SHA256 hash comparison for rolling `snapshot`/`latest` tags (full download, not HEAD)
- URL deduplication: group by resolved snapshot URL, download once per unique URL
- GitHub Pages CDN caching can serve stale versions
- `ShouldUseDownloadCheck` returns true for `/snapshot/` or `/latest/` in path

### Addon Rolling Tag Fix
- `ResolveVersionToken` skips tag extraction for `snapshot`/`latest` tags
- Falls through to Content-Length as version token (ETag/Last-Modified fallback)

### DXVK Updates
- Per-game variant check: discover all variants in use, check each independently
- Lilium uses different GitHub repo (`EndlesslyFlowering/dxvk`)
- `EnsureStagingAsync` guard includes `!HasUpdate` check

### Vulkan ReShade False Update
- Add `.Where(c => !c.RequiresVulkanInstall)` to `auxInstalled` filter

### Periodic Timer
- `System.Threading.Timer` every 4 hours after initial scan completes
- Re-fetches manifests, forces update check, triggers app version check

## Install Warnings
- Manifest `installWarnings` field: per-game, per-component messages
- `CheckInstallWarningAsync(gameName, component)` — Continue/Cancel dialog

## Bitness Override Auto-Uninstall
- When effective `Is32Bit` flips: uninstall ALL components (clean slate)
- Vulkan games excluded (global layer includes both bitness)
- Uninstalls run BEFORE updating `card.Is32Bit` (need old value for filename resolution)

## AddManualGame Missing Component Detection
- `ScanManualGameComponentsAsync` — background `Task.Run` for DC, OptiScaler, DXVK, DLSS/Streamline
- Results merged to card on UI thread via `DispatcherQueue.TryEnqueue`

## Emulator (Ryubing) Install
- ONE card, installs bundle of 9 addons. Manifest defines addon list with wiki game names.
- `card.IsEmulator` check routes to `InstallEmulatorAddonsAsync`
- Each addon tracked individually (size-based). ANY update → card purple.
- Three paths must handle emulator: `AddManualGame`, `BuildCards`, `LoadCacheAndBuildCardsAsync`
- Manifest timing on cached path: resolve addon names at click time as fallback

## Concurrency Issues
- `AddonPackService`: `SemaphoreSlim` download lock for concurrent `EnsureLatestAsync`/`CheckAndUpdateAll`
- `addon_deployments.json`: static `lock` around Load/Save
- `ShaderPackService.EnsurePackAsync`: needs per-pack lock (concurrent cache/background phases)
- `settings.json` contention: causes spurious shader re-downloads

## Purge Cache — Metadata Preservation
- Preserve `.json` files in download subfolders (version tracking)
- Safe to purge completely: DLSS/DLSS-D/DLSS-G/Streamline (version tracked by folder name)

## InstalledModRecord Replacement Pattern
After `InstallAsync` creates a fresh record (all defaults), preserve user toggles from old record AND re-save:
```csharp
if (card.InstalledRecord != null) {
    record.EngineIniHdr = card.InstalledRecord.EngineIniHdr;
    record.EngineIniLut = card.InstalledRecord.EngineIniLut;
    _installer.SaveRecordPublic(record);
}
```
Any future per-game toggle fields must also be preserved here.
