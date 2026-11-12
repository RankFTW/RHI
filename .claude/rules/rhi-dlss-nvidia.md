---
paths:
  - "**/*Dlss*"
  - "**/*Streamline*"
  - "**/*DlssPreset*"
  - "**/*NvidiaProfile*"
  - "**/*Nvidia*"
---

<!-- Copied from .kiro/steering/rhi-dlss-nvidia.md. Keep both in sync while Kiro is still used. -->

# DLSS, Streamline & NVIDIA Profile Knowledge

## DLSS & Streamline Management
- **Components**: DLSS SR (`nvngx_dlss.dll`), DLSS RR (`nvngx_dlssd.dll`), DLSS FG (`nvngx_dlssg.dll`), Streamline (`sl.*.dll`)
- **Detection**: Recursive scan of game folders during `BuildCards` (background thread only). Results cached in `dlss_trusted_paths.json`.
- **Storage**: `%LocalAppData%\RHI\DLSS\{version}\`, `DLSS-D\{version}\`, `DLSS-G\{version}\`, `Streamline\{version}\`
- **Manifest**: `dlss_manifest.json` (separate from main manifest). Fetched with ETag caching.
- **Backup**: `.original` extension per game. Never overwrite existing `.original` — first backup is always the true game original.
- **Streamline deployment rule**: Only replace `sl.*.dll` files that already exist in the game folder.

### Service & ViewModel
- `DlssStreamlineService` (partial class: `.cs`, `.Detection.cs`, `.Swap.cs`). Singleton DI.
- `GameCardViewModel.DlssStreamline.cs` — stores `DlssDetectionResult`, version strings, computed properties.
- Detection NOT in `LoadCacheAndBuildCardsAsync` — only background scan.

### DLL Location & Scanning
- DLLs can be anywhere in game install tree. Detection recursively searches.
- WindowsApps: manual per-directory walk with try/catch (AllDirectories throws on first restricted folder).
- `ResolveSearchRoot()`: `Binaries` → grandparent (UE), `Bin` → parent (CryEngine). Guard: don't go to grandparent if parent is known store folder.
- No `Binaries`/`Bin` in the path → `FindUnrealGameRootAbove` climbs up to 2 levels to a folder with `Engine\Plugins` or `Engine\Binaries` (PBZ manual/override at `platform\steam`, DLLs in `{root}\Engine\Plugins`). Never climbs into a store library folder or a drive root.
- OptiScaler detection: skip DLLs in directories containing `OptiScaler.ini`.

### Mod-owned DLSS copies
- `SearchDirectory` skips folders in `DlssStreamlineService.ModOwnedFolderNames` (`HSF_streamline`): HSF's bundled Streamline 2.14 / DLSS-G aren't the game's. Counting them showed DLSS-G for games without it and let swaps / auto-update replace HSF's files. `PathsAreWithin` rejects trusted-cache entries inside such a folder (forces a rescan). Add other mods' private DLSS folders to that list.

### Caching
- **Scan skip cache** (`dlss_scan_cache.json`): Games scanned 3× with no DLSS → auto-exempted.
- **Trusted path cache** (`dlss_trusted_paths.json`): Games found at same paths 3× → fast `File.Exists` + `FileVersionInfo` (~1ms).
- `TryFastDetect` validates paths within resolved search root via `PathsAreWithin`.
- Full Refresh selectively invalidates trusted entries with null components.
- `dlssSkipGames` manifest field skips expensive scans immediately.

### Version Display Rules
- "None": component absent, ComboBox disabled
- v1.x SR/RR/SL: greyed out, disabled (incompatible with v2.x+)
- v1.x FG: enabled (can upgrade)
- `(D)` suffix = original/default version. Selecting it restores `.original` file.
- "Custom" deploys from custom folder. Marker file `_rhi_custom_streamline`.

### Download Timeouts
- 120-second `CancellationTokenSource` timeout on all swap downloads.

## DLSS Presets (NVIDIA Driver Profile)
- **Setting IDs**: SR `0x10E41DF3`, RR `0x10E41DF7`, FG `0x10E41DF1`
- **Values**: 0x00=Default, 0x06=F, 0x0A=J, 0x0B=K, 0x0C=L, 0x0D=M, 0x00FFFFFF=Latest Recommended
- SR Preset Override companion (`0x00634291`): N/A=0x00, Latest=0x01, Custom=0x02. Only for SR.
- "Default" must DELETE settings, not write zero (blocks inheritance). Use `DeletePreset`.
- **`DeleteSetting` fails for non-standard IDs**: catches exception, falls back to writing 0 via raw NVAPI.

### Profile Matching (`FindProfile`)
1. Exact title match
2. Fuzzy title (strip ™, ®, colons)
3. Manifest `launchExeOverrides` exe match
4. Recursive exe scan → profile Applications match
5. Profile NAME == exe filename
- **Cache**: `_profileLookupCache` per session. Invalidated on session reload + `ApplyManifestProfileConfig`.
- **Auto-create**: `CreateProfileForGame` if all passes fail. Commas stripped from name.
- **`EnsureExeRegistered`**: adds exe to existing profile's Applications list (only when `AutoCreateProfiles`).

### Manifest-Driven Presets
- `dlssPresets` field with `sr`, `rr`, `fg` arrays. `ApplyManifestPresets(manifest)` merges entries.
- `disabled: true` removes by name (except "Default").
- Called after manifest fetch in both init paths. `Initialize()` must run in BOTH cached and non-cached paths.

## DLSS Render Scale Override
- SR mode: `0x10AFB768`, SR custom: `0x10E41DF5`
- RR mode: `0x10BD9423`, RR custom: `0x10C7D4A2`
- Mode values: 0x03=Default, 0x04=DLAA, 0x02=Quality, 0x01=Balanced, 0x00=Performance, 0x05=UltraPerf, 0x06=Custom
- Custom values: percentage as uint DWORD (33=0x21, 50=0x32, 67=0x43, 100=0x64). 0x00=Off.
- Reset: mode to 0x03, custom to 0x00.
- **DeletePreset fallback**: render scale mode default is `0x03` (NOT 0x00 which = Performance).

## DLSS "Latest DLL" Driver Override
- SR: `0x10E41E01`, RR: `0x10E41E02`, FG: `0x10E41E03` — Off(0)/On(1)
- When On, driver injects its own DLSS DLL. UI greys out version ComboBox.
- Quick Apply skips components with driver override active.

## Multi Frame Generation (MFG)
- FG Mode Override (`0x10308298`): Off=0x00, Fixed=0x02, Dynamic=0x04
- MFG Generation Factor (`0x104D6667`): App-controlled=0x00, 2x=0x01, 3x=0x02, 4x=0x03, 5x=0x04, 6x=0x05
- MFG Dynamic Max Frame Count (`0x10562D0F`): Off=0x00, 3x=0x02 through 6x=0x05
- MFG Dynamic Target FPS (`0x10CF4125`): Off=0x00, Max Refresh=0x01000000, or FPS as uint
- FG Prioritization (`0x10E41DF6`): Quality=0x00, Smoothness=0x01, Unspecified=0x02
- Writing 0 to MFG Dynamic Max/Target blocks global inheritance — use `DeletePreset` instead.

## FPS Limiter V3
- Setting ID: `0x10835002`. Value: 0=Off, any uint=FPS cap.
- Background variant: `0x10835005`.
- RL/DC auto-disable driver FPS cap on install, delete on uninstall.
- Per-game UI: Driver Settings › Other › FPS Limit. "Global" = no value on the game profile (`ClearPerGameFpsLimit`, raw delete); "Off" writes 0 (overrides a global cap).
- `GetPerGameFpsLimit` uses `GetOwnSettingRawNvApi`: NvAPI_DRS_GetSetting also returns values inherited from the base profile, so check settingLocation (offset 4108, 0 = this profile) to tell "own value" from "inherited".

## Driver Profile Settings (VSync, Latency, Smooth Motion, Power, ReBAR)

### Setting IDs
- VSync Mode: `0x00A879CF`, Tear Control: `0x005A375C`
- Power Management: `0x1057EB71`
- Latency ULL Enable: `0x10835000`, ULL Mode: `0x0005F543`, Pre-Rendered Frames: `0x007BA09E`
- Smooth Motion Enable: `0xB0D384C0`, APIs: `0xB0CC0875`, Flip Pacing FS: `0xB03A4546`, Win: `0xB03A4547`
- ReBAR Feature: `0x000F00BA`, Expr Modes: `0x00C09D09`, Size Limit: `0x000F00FF` (BINARY/QWORD)
- G-Sync Mode: `0x1194F158` (App) + `0x1094F1F7` (Global)
- Preferred Refresh Rate: `0x0064B541`

### Elevation Requirements
- **Requires admin**: ReBAR (Feature, Mode, Size), CPU Expr Modes, Smooth Motion APIs/Flip Pacing
- **Does NOT require admin**: VSync, Power Management, Smooth Motion Enable, Pre-Rendered Frames, DLSS presets/render scale

### ReBAR
- Feature+Mode use `SetReBarElevated` fallback. Size Limit is BINARY type — use PS helper.
- `NvAPIWrapper` crashes on BINARY settings during enumeration. Never enumerate `profile.Settings` on profiles with binary settings.
- In-memory `_rebarSizeLimitCache` for reads (newer drivers throw on GetSetting for BINARY).
- Global+Per-Game pattern: "Global (On/Off)" only on Enable dropdown.

### Low Latency
- Off: PreRendered=0, Enable=0, Mode=0. On: PreRendered=1, Enable=0, Mode=1. Ultra: PreRendered=1, Enable=1, Mode=2.
- Newer function ID `0x8A2CF5F5` needed for ULL Enable/Mode (legacy `0x577DD202` returns SETTING_NOT_FOUND).

### Raw NVAPI
- SetSetting primary: `0x8A2CF5F5` (5 params), legacy: `0x577DD202` (3 params)
- GetSetting primary: `0xEA99498D`, legacy: `0x73BF8338`
- NVDRS_SETTING struct: 12320 bytes, Pack=8. settingType at offset 4104, currentValue union at offset 8220.
- BINARY type: settingType=1, data at union offset = `[int32 length][data bytes]`
- `QueryInterface` requires `NvAPI_Initialize` (`0x0150E828`) first.

### PowerShell Helpers
- Hex values > `0x7FFFFFFF` must use unsigned decimal equivalents.
- `GetDelegateForFunctionPointer` requires non-generic delegate types (define in `Add-Type`).
- PS helper for BINARY writes uses `$profile.SetSetting([uint32]0x000F00FF, $bytes)` — works in fresh process.
- `SetReBarSizeLimitViaPs`: pass actual NVIDIA profile name (from `FindProfile`), not RHI game name.

### Export/Import
- Export only checks profiles for games in RHI (~80), not all ~7800 cached profiles.
- Do NOT filter by `!= 0` — many settings use 0 as meaningful value.
- Base profile import must use raw API FIRST (NvAPIWrapper `SetSetting` silently fails for many settings).
- `profile.Applications` and `profile.Settings` can throw — wrap in try-catch.

### Profile Restore to Defaults
- `profile.DeleteSetting(settingId)` per setting — removes user overrides while keeping profile intact.
- Invisible settings (written via newer API) need `SetSettingRawNvApi(id, 0)` + raw save.
- Warning before restore (irreversible).

### Shader Cache Global Settings
- Cache Size: `0x00AC8497`, Pre-Compile: `0x00EAD189` — set on `_session.BaseProfile`.
- Base profile reads: raw API first, NvAPIWrapper fallback. Global defaults when at 0: Cache=16GB, PreCompile=Low.
- **Compile now** (`NvShaderCompilerService`, Settings row from `SettingsHandler.ShaderCompile.cs`): runs the driver's `NvOSC.exe -runTool -resUtil=MIN|MED|MAX` (no `-resUtil` = the Pre-Compile DRS level), found via the NVIDIA display adapter's `UserModeDriverName` folder in the display Class registry key. **Working folder must be the driver folder** (it loads `nvgpucomp64.dll` / `NvMemMapStoragex.dll` by relative name; otherwise exit 3). No admin needed; compiles the user's DXCache (Local + LocalLow). Stop = kill NvOSC.exe (the NVIDIA App does `taskkill /F`). The idle task is `NVIDIA Shader Compiler` (`NvOSC -register` / `-deregister`).
- **NvOSC logging** (found by disassembly, tested on driver 621.02): off by default and controlled by two DRS settings on the **Base Profile** (read with `NvAPI_DRS_GetSetting`, not the registry): `0x00EAD177` (DWORD, `1` = on; anything else or missing = off) and `0x00B7B8AC` (**BINARY**: length at offset 8220, ANSI folder path at 8224; default `c:`, i.e. the C:\ root, which a normal user can't write to). **Writing either needs admin** (`-137` INVALID_USER_PRIVILEGE otherwise); NvOSC itself still runs unelevated. Log file = `<folder>\logs_<NvOSC pid>.txt`, written live, one set of lines per app: `<id> proccessing started.` / `<id> cache exe: pbz.exe` / `<id> proccessing completed.`, then `Summary (local dir): applications=N`, done for Local then LocalLow, ending `OscTool ran successfully`. No percent or time estimate. The source caches look like the `0002*.nvph` files in each DXCache folder (4 files = `applications=4` in one test), so N can probably be counted before the run starts. It prints nothing to stdout/stderr or OutputDebugString. **NvOSC locks its log (no read sharing) until it exits, so it can't be used for live progress.** Compile now gets progress another way, no admin needed (`NvShaderCompilerService.Progress.cs`): NvOSC processes the `0002<id>.nvph` files (Local DXCache, then LocalLow; folder order) one at a time and keeps the current one open; Restart Manager (`RmGetList`) tells which one, everything before it is done. Each cache file's header = `nvph` magic, exe name (ASCII, null-terminated) at byte 17. Each Restart Manager check takes ~50 ms, so only the current file and up to 8 after it are checked, about once a second.

### Quick Apply v1.x Guard
- SR/RR: skip if `StartsWith("1.")`. FG: NO guard (can upgrade). Streamline: skip if v1.x.
- Also skips components with driver override active.

## DLSS/Streamline Defaults (Quick Apply)
- 9 settings on `SettingsViewModel`. Empty/"Default" = don't change.
- `DlssDefaultsDialog.cs` for configuration. Quick Apply column in panel.

## DLSS/Streamline Auto-Update
- Compares manifest newest against `LastKnownNewest*`. Games at old baseline → swapped to new.
- First run seeds baseline without applying. Runs after `CheckForUpdatesAsync`.

## Batch Deploy
- `MassDlssDeployDialog.cs`. Eligible: `card.HasAnyDlssStreamline`. v1.x games greyed out.
- Skips games already at selected version. Downloads on-demand.
- Presets deployed independently of DLL versions.
- Panel rebuild needed after batch (code-behind, not data-bound).

## Digital Vibrance
- Service: `DigitalVibranceService.cs` — raw NVAPI P/Invoke.
- Function IDs: EnumDisplayHandle `0x9ABDD40D`, GetDVCInfoEx `0x0E45002D`, SetDVCLevelEx `0x4A82C2B1`
- Non-Ex fallbacks: Get `0x4085DE45`, Set `0x172409B4`
- Persistence: `DigitalVibrance` key in settings.json, restored on startup.

## profileNameOverrides
- Manifest field forces `FindProfile` to use specific NVIDIA profile name.
- Checked FIRST in `FindProfile`, before the persistent `nvidia_profile_cache.json` (and again in `FindProfileUncached`). Previously the cache won, so an override added after a game was first matched was ignored until Full Refresh (Metro 2033 Redux stuck on "Metro: Last Light Redux" via shared `metro.exe`).

## NVIDIA App override limits
- The NVIDIA App marks games it hasn't approved with `Disable_{FG,RR,SR,RR_Model,SR_Model}_Override` flags (`ApplicationStorage.json` as JSON bools, every `fingerprint.db` under `%LOCALAPPDATA%\NVIDIA Corporation\NVIDIA app\NvBackend\` as XML-ish `<...>1</...>` text) and undoes driver-profile DLSS overrides (RHI, Profile Inspector) on them. `NvAppOverrideLimitService.RemoveLimits` (Settings › Driver Settings › Profiles button, `SettingsHandler.NvAppLimits.cs`) clears them like JPersson77's nVAppAppApp.ps1: backup to `%LOCALAPPDATA%\RHI\NvAppBackup\<timestamp>\` (last 5 kept), regex replace (non-text files skipped), `fingerprint.db` set read-only, then restarts `NVDisplay.ContainerLocalSystem` + `NvContainerLocalSystem` via an elevated (`runas`) PowerShell. Driver / NVIDIA App updates can restore the flags; users re-run it.

## DLSS5 Feeder (Neural Rendering method, 2.0.0+)
- Install: `DetailPanelBuilder.NeuralRendering.Feeder.cs`. Add-on + `DLSS5_Feed.fx` from the Feeder zip; 32-bit games also get `host64\` (`dlss5-feed-host64.exe`, 64-bit ReShade, consumer, NR / SR DLLs).
- **Motion vectors / preset** (`Services/FeederPreset.cs`, tests `FeederPresetTests`; mirrors Feeder's `Install-DLSS5Feeder.ps1`): `DLSS5_MV_PROVIDER` is a per-effect key in the preset's `[DLSS5_Feed.fx] PreprocessorDefinitions` (ReShade.ini's global list is ignored for it; RHI's old global `=3` is removed). New install = 5 (built-in estimator, detected by `float4 BaseLevel(` in the .fx), else 3 for an older shader; a preset that already names a provider keeps it. LumeniteFX is only downloaded / deployed / selected for 3 or 4. The preset is **merged** (Techniques / TechniqueSorting + that one definition), never overwritten; it's only in the Feeder install record (removed on uninstall) when RHI created it. Status tag: Built-in motion vectors vs LumeniteFX via `FeederPreset.ReadGameProvider`.
- **Add-on and host must be from the same release** (2.0.0 IPC v12; they refuse each other otherwise). The shared `DLSS5 Feeder_host64.exe` in addon staging is written only by `AddonPackService` together with `.addon32`; versioned staging (`Renodx5AddonService.EnsureVersionStagedAsync`) keeps its host in its own folder. `FeederHostMatchesAddon32` re-downloads when the two staged files are >5 min apart.
- Feeder in a game is not auto-updated (not tracked by `AddonPackService` deployments); it changes on reinstall or the version combo. The author's NR fork is manifest `osForks.feeder-nr` (live menu changes from v0.8.93).
