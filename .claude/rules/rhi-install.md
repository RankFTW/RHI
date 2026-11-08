---
paths:
  - "**/*Install*"
  - "**/*Update*"
  - "**/*Orchestration*"
---

<!-- Copied from .kiro/steering/rhi-install.md. Keep both in sync while Kiro is still used. -->

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
- **OptiScaler versions = `OsVariants`** (`Services/OsVariants.cs`): built-ins Stable / Nightly / DlssNr / F5 plus custom builds `Custom:<slug>`. Use `OsVariants.All()` / `Label` / `FromLabel` / `IsKnown` / `IsOs10` / `NeedsNrDll` instead of new string lists or `isNightly || isDlssNrOrF5` checks; INI template suffix via `OptiScalerService.IniSuffix`. **Custom builds** (`OsCustomBuildService`, Settings card `SettingsHandler.OsCustomBuilds.cs`): user-added from a link (any host), zip / 7z / rar, folder or bare `OptiScaler.dll`; stored in `%LocalAppData%\RHI\optiscaler-custom\<slug>\build\` + `rhi_custom_build.json` (the folders are the list). Only check: an `OptiScaler.dll` within 3 folders (shallowest wins); a DLL whose ProductName isn't OptiScaler is added with a note. Never updated: no update check, skipped by Update All, `HasUpdate*` untouched; Replace + offer to redeploy (`UpdateAsync`). Install / update use the build folder as staging; a removed build aborts install with a message, and the Default version falls back to Stable. Plan: `docs/custom-optiscaler-builds-plan.md`. Archives dropped on RHI (file or link, both drop paths end in `ProcessDroppedArchive`) with an `OptiScaler.dll` and no RenoDX addon offer to add a custom build. **Recommended forks** (`OsForkService`, manifest `osForks`, variant `Fork:<id>`): listed releases newest by publish date (prereleases included by default), asset by `assetPattern` or `ScoreAsset` ranking; staged in `optiscaler-forks\<id>\build\` + `fork.json` (definition kept, so it works offline); install / update call `EnsureStagedAsync` (downloads when out of date), the update check calls `CheckForUpdateAsync` for forks installed games use. Definitions load next to `FeatureFlags.ApplyManifest` in all three init paths + the 4 h refresh; `SettingsViewModel` keeps a saved `Fork:` / `Custom:` default even before the manifest loads. 
- **Release pins and Check now** (OptiScaler ⚙, `MainWindow.Events.Components.OptiScaler.Release.cs`): `OsReleaseService` lists GitHub releases for Stable / DLSS NR (prefers the `-rtx40-mfg` zip, as the built-in download does) / F5 / forks. A per-game pin reuses `OsNightlyBuild` (one value per game: a nightly date for Nightly, a release tag otherwise; 8-digit values are never release pins; the ⚙ clears it when the version changes). Pinned installs stage into `optiscaler-pinned` via `EnsurePinnedAsync` and set `isPinned` in Install/Update (fixed dir, no update flags); the update check skips pinned games (`GetOsPinnedRelease`). The version line shows installed vs latest; Check now invalidates the ETag entry, refetches, sets the service's own update flag and marks the card; Update calls `InstallOsButton_Click` with the card in the button's Tag.
- **NR runtime for DLSS NR / F5**: install (5b) and update (4e) deploy `nvngx_dlssnr.dll` through `GetNrRuntimeForAsync`: the ⚙'s NR Runtime pick (`OptiScalerService.NrRuntimeProvider`, set by `MainViewModel` to `GetOsNrRuntime`), else the newest. Update skips it when `rhi_install.txt` has `NrMethod` (the Neural Rendering section owns the file). Variant lists that must include F5: version fallback switches (BuildCards / CacheLoad), startup staging (both init paths), the post-install Streamline step (`InstallEventHandler`). Plan to replace these lists: `docs/custom-optiscaler-builds-plan.md`.
- **Install checks for a newer release first** (`InstallAsync` step 3 calls the variant's `CheckFor*UpdateAsync`, ETag-cached; skipped for a pinned nightly build). The periodic check only covers variants an installed game uses, so without this a fresh install / reinstall deployed a stale staged build forever (issue #163, F5 stuck on 0.1.29).
- **Old nightly builds** (`Settings.OsNightlyCleanup`, Settings › Components › OptiScaler, default off; row built by `SettingsHandler.OsDefaultVariant.cs`): builds are staged in `%LocalAppData%\RHI\optiscaler-nightly\yyyyMMdd\` (200-300 MB each). `OptiScalerService.RemoveOldNightlyBuilds(keep)` deletes every dated folder except the newest complete one (has `OptiScaler.dll`), the pinned builds (`GameNameService.OsNightlyBuild` values, incl. Control UE's 20261004) and folders without `OptiScaler.dll` (download still unpacking). Runs after a new build stages (`NightlyCleanupKeepProvider`, set by `MainViewModel`), at startup in both init paths, and when the setting is turned on. Installs use `rhi_install.txt` for uninstall, so deleted builds aren't needed later.
- **Plugins folder depends on the build**: OptiScaler 10 builds (nightly, F5, DLSS NR) have an `OptiScaler\` subfolder and load plugins (`*.asi`, same-name DLLs) from `OptiScaler\plugins`; 0.9 stable (on hiatus until 0.9.5) loads from `<game>\plugins`. Always use `OptiScalerService.GetPluginsDir(gameDir)`. `LoadAsiPlugins=true` is enforced on install and ini deploy.
- **Presets** (top of the OptiScaler cog, all variants; `MainWindow.Events.Components.OptiScaler.Presets.cs`): ready-made presets from `optiscaler_presets.json` (repo root, live data like `dlss_manifest.json`: pushing to main deploys it) via `OsPresetService.ManifestPresets` (fetched in both init paths + the 4 h refresh, cached in `%LocalAppData%\RHI\optiscaler_presets.json`, built-in copies in `OsPresetService.BuiltInPresets` as fallback; keep the two in sync). Entries are `OsPreset` fields in camelCase (null = leave alone) plus `id`, `description`, `streamlineVersion` (`latest` = first of `StreamlineVersions`) and `installUniversalMfg` (`MainViewModel.EnsureUniversalMfgForPresetAsync`: plugin with OptiScaler 10, else first free of version/winmm/dinput8). Applied by the same `ApplyOsPreset` as the 4 user slots (`os_presets.json`, one dropdown). The ticked preset = `IsOsPresetActive` (FG fields only).
- `UpdateAsync` clears the game's OptiScaler subfolders before redeploying but must keep `OptiScaler\plugins` (`DeleteDirKeepingPlugins`); uninstall skips that folder in its generic subfolder pass so DXVK coexistence DLLs can be moved back first (step 4c), then 4d deletes `OptiScaler\`.

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

### Pushing component updates into game folders
- `EnsureStagingAsync()` sets `HasUpdate = false` after a successful download. Never gate the "copy into games" step on `HasUpdate` after staging has run — it is always false by then (this broke RTX 40 MFG, NR Cost Scaler and DLSSG 20/30 startup deploys). Capture the flag before staging, or better, compare the game copy with staging.
- Prefer content/size+mtime comparison over "update happened this session": a copy that fails (game running) is then retried on the next launch. `File.Copy` preserves the source's modified time, so size + `LastWriteTimeUtc` identifies an outdated RHI-made copy cheaply.
- Never re-create a tracked file that no longer exists in the game folder — the user removed it.
- Components that copy addons directly must call `AddonPackService.TrackAddonDeployment` / `UntrackAddonDeployment`.
- Upstream release asset/file names change (RTX 40 MFG: `RTXMFG.dll` → `MFGUnlocker.dll`). Never record a new staged version unless the expected file was actually extracted.
- Universal MFG Unlocker (formerly RTX 40 MFG; code still `Rtx40MfgService`, repo `dashdogy/MFG-Unlocker`): its zip has only `MFGUnlocker.dll`, but standalone XeMFG needs Intel's `libxess_fg.dll` + `libxell.dll` in `<game>\RTXMFG-XeSS\`. RHI stages them from `intel/xess` (pinned tag `v3.0.2`, raw `bin/` files) into `rtx40mfg\xess\`, deploys them on every Install, and lists what it copied in `RTXMFG-XeSS\rhi_installed.txt` (uninstall removes only those; user-supplied copies are never overwritten). Existing installs are caught up once from `UpdateInstalledGames`; once the marker exists, missing files count as user-removed and aren't re-created.
- **Universal MFG Unlocker as an OptiScaler plugin**: when RHI installed an OptiScaler 10 build (nightly, F5, DLSS NR; `MainViewModel.IsOptiScalerPluginHost` = OS record + `OptiScaler\` folder) the mod lives at `OptiScaler\plugins\MFGUnlocker.asi` and the per-game installed-as value is that relative path (`Rtx40MfgService.PluginRelPath`; every `Path.Combine(installPath, installedAs)` check keeps working). No proxy copy alongside it, no RTXMFG-XeSS (XeMFG goes through OptiScaler's FG Output = XeFG and its own XeSS files). Moves: `MoveToOptiScalerPlugin` (writes `MFGUnlocker.rhi-proxy.txt` with the old proxy name, SentinelRestores the proxy) and `MoveOutOfOptiScalerPlugin` (old name → version.dll → first free name; XeSS folder redeployed). Triggered by `OptiScalerService` hooks wired in `MainViewModel.MfgPlacement.cs`: `BeforeV10DeployHook` (before OptiScaler's files are copied, so its DLL can't overwrite the proxy), `V10InstallFailedHook`, `BeforeOptiScalerFolderRemovedHook` (uninstall, before `OptiScaler\` is deleted); plus `ReconcileMfgPlacementForAllGames` at startup (skips hidden games). Stable 0.9.x is unsupported by the mod and left as a proxy. FG Output is the user's choice (OptiScaler ⚙); RHI never writes it for the mod. XeMFG standalone (no OptiScaler) refuses to start while ReShade hooks DXGI (`d3d12_takeover_failed reason=menu_factory_not_dxgi`; the mod cancels the takeover when its menu can't attach to XeFG's output chain, d3d12_host.cpp ~L603), whether ReShade is a DLL or an ASI. The install button shows a Continue/Cancel warning for non-plugin installs when ReShade is installed (not Vulkan), dismissible via `UmfgReShadeWarningDismissed`.
- 20/30 FG Unlock (dlssg_for_sm86) is a package since 0.4.0: proxy DLL + `dlssg_sm86.ini` + the whole `dlssg_sm86\` folder (backend, kernel packs, bundled runtime). Each proxy name (version, winmm, dbghelp, dinput8, dxgi, d3d12) is a separate build that forwards to its own system DLL: never rename one proxy to another name. Staged from the release zip; deployed files tracked in `dlssg_sm86\rhi_install.txt`; installed games are refreshed by file comparison (`NeedsUpdate`).

- **MFG Ada Unlock** (ReShade addon `renodx-mfgunlock.addon64`, Extras row in `DetailPanelBuilder.Extras.Unlocks.cs`): keeps its settings in `MFGAdaUnlock.ini` in the **game exe folder** (`GetModuleFileName(nullptr)`), not the addon folder. Frame generation method: DLSS-G = `[Backend] Selected=DLSSG` + `[DLSSG] XmxGenerator=0`; XeMFG (CUDA) = `Selected=XeMFG` + `XmxGenerator=0`; XeMFG via DLSS-G (XMX) = `Selected=DLSSG` + `XmxGenerator=1`; missing/unknown = DLSS-G. `[Adapters] DlssSr=1` = DLSS-SR input (experimental): XeMFG in games with DLSS SR but no DLSS FG to hook; only meaningful with XeMFG (CUDA), so the ⚙ enables it only then. The ⚙ writes these (restart needed). API support as of 1.8.0 (README comparison table): DLSS-G = D3D12 (Vulkan experimental); XeMFG (CUDA) = D3D12 (inputs: Streamline DLSS-G, the game's own XeSS-FG, FSR 3.1 FG, or DLSS-SR input) and Vulkan experimental (DLSS SR/RR input only); XMX = D3D12 with native DLSS FG only; no DX11 or older. The ⚙ warns for Vulkan / DX11-and-older games (dual-API games with DX12 get no warning); the text says 'as of 1.8.0', so recheck when the mod updates. Both XeMFG modes need `libxess_fg.dll` 1.3.1.78 + `libxell.dll` 1.3.2.10 in the exe folder (the mod checks exact SHA-256s; XMX refuses any other `libxess_fg` build): RHI's pinned `rtx40mfg\xess` staging (intel/xess v3.0.2) matches. `Rtx40MfgService.DeployAdaXessFiles` / `RemoveAdaXessFiles` copy/remove them, listed in `MFGAdaUnlock.rhi-xess.txt`. Games often ship their own older XeSS FG files there (Of Ash and Steel: libxess_fg 1.2.2.118, libxell 1.2.1.13), which the mod rejects, so for MFG Ada a different version is moved to `<file>.mfgada-original` (first backup never overwritten) and replaced; identical copies are left unclaimed. Removal deletes only files still matching the staged copy and restores the `.mfgada-original` backups. `ReconcileMfgAdaXessFiles` (background, launch + 4 h refresh, skips hidden games) deploys where a file is missing (before RHI set it up) or a different version (also after a game update), and removes RHI's copies where the mod is gone (checks the disk too). The ⚙ shows Ready / Missing / wrong version by FileVersion (no hashing on the UI thread). The UMFG `RTXMFG-XeSS` folder keeps the old never-replace rule.

- **RTX Encore** (`RtxEncoreService`, row in `DetailPanelBuilder.Extras.AsiLoaders.cs`, ⚙ in `DetailPanelBuilder.Extras.RtxEncoreSettings.cs`): settings live in `rtx-encore.jsonc` beside the mod DLL (`svc.ModDir`: game folder, or `plugins\` for the `.asi`). It's JSON with a `//` comment on almost every key, written by the mod: **never round-trip it through a JSON serializer**. `RtxEncoreConfig` replaces only the value token on the key's line (top-level section scoped), inserts a missing key at the top of its section / a missing section at the top of the file, and creates a small file (`configVersion` 4 + only RHI's keys) when absent; the mod fills in defaults on first launch. Unit tests: `RtxEncoreConfigTests`. Nested sections are addressed as `"parent/child"` (e.g. `neuralRendering/optimizations`); a missing nested section is inserted at the top of its deepest existing parent. The ⚙ (compact grid, `CogDialog.AddCompactGrid`) shows the settings worth choosing before launch: frame generation (engine, mode, multiplier, FG preset, dynamic target / 5-6x, flip metering, `reflexFrameLimit`), `xefg.*` (enabled only when engine = xefg), `imageQuality.*`, `hudUi.*`, upscaling (`reconstructionEngine`, `xess.xessTensorCores`, `dlssSuperResolution.*` incl. `fastSuperResolution`, SR/RR presets, `superResolutionInputs.*`), `smoothMotion.*` (API defaults to the game's), NR (`neuralRendering/core.nrEnabled`, `dlssNrPreset`, `neuralRendering/optimizations.*`) and compatibility (`general.gpuSeries`, `compatibility.forceOTA/disableInGameMenu/disableMenuDetection`, `nativeMenuFeatures.hairEnabled`). Left to the in-game menu: overlay, NR appearance/per-pass/experimental, sharpening, Reflex mode, V-Sync, hotkeys, diagnostics. XeFG doesn't add FG to games without DLSS FG.
  - **Intel runtimes** (`Rtx40MfgService.EncoreIntel.cs`) copy what RTX Encore Hub 1.6.0 does (read from the Hub binary's catalog and strings; its source isn't public): XeSS SDK 3.0.2 `libxess.dll` 2.0.2.68, `libxess_fg.dll` 1.3.1.78, `libxell.dll` 1.3.2.10 and `LICENSE.txt`, SHA-256 pinned (the mod checks exact hashes), from raw.githubusercontent intel/xess v3.0.2. Copies the game already ships (anywhere under `DlssStreamlineService.ResolveSearchRoot`, e.g. UE `Engine\Plugins\...\XeSS\Binaries\ThirdParty\Win64`; CompanyName Intel; bounded scan that skips links, `rtx-encore-runtimes`, `RTXMFG-XeSS`, OptiScaler folders and WindowsApps) are **replaced in place** with backup `<file>.encore-original`. Missing files and `license.txt` go in `<modDir>\rtx-encore-runtimes\intel\`. Never leave a private duplicate of a file the game ships (two versions in one process). Marker `rhi_installed.txt` there: plain names = private files, `native:<path relative to game root>` = game copies (restored on removal unless the game changed them since). `ReconcileRtxEncoreFiles` (launch / 4 h) redoes it when the marker is missing, has no `license.txt` (pre-2.9.0 installs), or a replaced game copy no longer matches.
  - **Proxies**: renaming the main `rtx-encore.dll` is the documented method; the `.asi` uses the release's separate `alternative-proxies\rtx-encore.asi` build (staged as `rtx-encore.asi`). Bink names (`binkw64`/`bink2w64`): the game's file must stay beside the mod as `<name>Hooked.dll`, not `.original` (a 0-byte `.original` marks "game had none").
  - **NR DLL** is 310.9.16 only (`NrManifestVersion` "310.9.16 (20/30/40/50)") and goes **beside the mod** (the mod checks its neighbour): `NrRelPath` gives `plugins\nvngx_dlssnr.dll` for the `.asi`, which is also the shared-file key. `MoveNrDllBesideMod` moves old game-folder copies; `UpgradeNrDllIfOlder` moves RHI's old 310.8.x copies when RtxEncore is the only shared-file owner.

### Periodic Timer
- `System.Threading.Timer` after the initial scan completes: every hour with a GitHub token, every 4 hours without (`MainViewModel.PeriodicUpdateCheckInterval`, from `GitHubTokenHandler.HasToken`; a full check uses most of the 60 requests an hour GitHub allows without a token). The launch-check cooldown uses the same interval; the timer re-arms itself if a token is added or removed, and saving / clearing the PAT in Settings restarts the countdown.
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

## Hidden games
- **Hidden games are left alone**: every automatic update or bulk "apply to all" loop over `_allCards` / `AllCards` must skip `card.IsHidden` (Update All, AutoUpdateService, DLSS/Streamline auto-update, RTX 40 MFG, 20/30 FG, RTX Encore, NR Cost Scaler, ASI Loader, DLSS Enabler, custom ReShade redeploy, startup shader sync, Streamline redeploy, Settings "apply to all" buttons). Detection and update checks (read-only) still include hidden games. Exception: Vulkan per-game override propagation (shared global layer state). Decided Oct 2026; before that only Update All skipped them.

## Removed games
- Management › **Remove from RHI** (`RemoveManualGame_Click`): a manually added game is deleted as before; a detected game's `"GameName|Store"` key goes into `GameNameService.RemovedGames` (settings.json `RemovedGames`) and the card leaves `_allCards` (`MainViewModel.RemovedGames.cs`). Optional "also uninstall" checkbox when components are installed.
- Removed games are filtered out wherever the list is built: after `BuildCards` (Init), before the sort in `LoadCacheAndBuildCardsAsync`, and from `cardsToAdd` in the background merge (`WithoutRemovedGames`). Being out of `_allCards`, every bulk/auto loop skips them with no extra checks. Any new path that builds or adds cards must call `WithoutRemovedGames` too.
- Filtered cards are kept in `_removedCards` so Restore (Settings › Tools & Maintenance › Troubleshooting › Removed games, `SettingsHandler.RemovedGames.cs`) puts them straight back; if a game wasn't seen this session, Restore triggers a refresh.
- "Reset install folder" is now its own Management button (detected games only, `ResetInstallFolder_Click`).

## Install folder migration
- When a game's folder changes (manifest `installPathMigrations` / `installPathOverrides`, Change install folder, Reset install folder) `InstallFolderMigrationService` moves RHI's installs to the new folder (`InstallFolderMover`: all-or-nothing, preflight for running game / locked files, `.original` sentinels restored in the old folder and recreated in the new one; a component RHI already has in the new folder wins). Automatic pass: `MainViewModel.FolderMigration.cs`, both init paths, before records are loaded for `BuildCards`; manual: prompt in `MainWindow.FolderMigration.cs`. Details and pitfalls: "Install folder migration" in `rhi-knowledge.md`.
- **New component that writes into the game folder: add it to the inventory** (`InstallFolderMigrationService.Inventory.cs`), or a folder move leaves it behind.
- `SetFolderOverride` must be given the detected folder before the card changes (`originalPath`), or Reset install folder returns to the picked folder.
