# RHI Manifest Field Reference

Complete reference for every field in `RemoteManifest`. For each field: what it does, where in the code it's read, and what it controls.

---

## Game Detection Fields

### `blacklist` — `List<string>`
Completely suppresses detected games from appearing in RHI (e.g. DLC launchers, anti-cheat helpers).
- `GameInitializationService.ApplyManifest()` → populates `_manifestBlacklist`
- `MainViewModel.Init`, `BackgroundScan`, `CacheLoad` → filters `allGames` before card build

### `wikiNameOverrides` — `Dict<string,string>`
Maps detected folder/store name → RenoDX wiki mod name. Fixes cases where Steam folder names don't match the wiki.
- `GameInitializationService.ApplyManifest()` → adds to `_nameMappings` (user overrides win)
- Marked as manifest-origin: invisible in UI, excluded from settings.json saves

### `lumaNameOverrides` — `Dict<string,string>`
Maps detected game names → Luma completed-mods list entry names. Separate from wiki name overrides.
- `MainViewModel.Install.Luma.MatchLumaGame()` → highest priority, before fuzzy matching

### `installPathOverrides` — `Dict<string,string>`
Defines the subpath within the detected install folder where mods should be deployed (e.g. `"bin\\x64"`).
Supports pipe-separated candidates: `"Win64|WinGDK"` — tried in order, first existing wins.
- `GameInitializationService.ApplyManifest()` → merges into shared `_installPathOverrides` dict
- `BuildCards`, `CacheLoad`, `AddManualGame` → applies path resolution before card creation

### `splitGames` — `Dict<string,List<SplitGameEntry>>`
Splits a single detected store entry into multiple cards, each pointing to a subfolder.
- `MainViewModel.BuildCards` → very early, replaces one `DetectedGame` entry with N sub-entries
- `SplitGameEntry` has `name` (card display name) and `subPath` (relative path from bundle root)
- Entry is only created if the subPath directory actually exists on disk

### `engineOverrides` — `Dict<string,string>`
Forces a specific engine type and label, overriding auto-detection.
Special values: `"Unreal"`, `"Unreal (Legacy)"`, `"Unity"`, `"RE Engine"` → map to `EngineType` enum.
Any other string (e.g. `"Silk"`) is stored as-is and shown in the engine badge but doesn't affect mod fallback logic.
- `GameInitializationService.ApplyManifest()` → populates `_manifestEngineOverrides`
- `MainViewModel.GameMatching.ResolveEngineOverride()` → called during card build
- When present: engine badge `isClickable = false` (no user toggling)

### `engineHintOverrides` — `Dict<string,string>`
Sets the `EngineHint` display string on a card (e.g. `"4.27.2"`, `"5.3.2"`). Unlike `engineOverrides`, display/hint only — doesn't affect `EngineType` enum.
- `BuildCards`, `CacheLoad` → `card.EngineHint = manifestEngineHint` after card construction
- Used by Engine.ini deployment to decide UE4 vs UE5 HDR key behaviour

### `engineIniPathOverrides` — `Dict<string,string>`
Overrides the Unreal project name used to locate `Engine.ini` in `%LocalAppData%`. Supports pipe-separated candidates and absolute paths.
- `BuildCards`, `CacheLoad`, `AddManualGame` → `card.EngineIniProjectOverride`
- Consumed by all `AuxInstallService` Engine.ini methods, the 📋 INI button, and the cog dialog HDR/LUT handlers

### `emulatorGames` — `Dict<string,EmulatorConfig>`
Drives the Ryubing emulator bundle. Defines which addons to download and their fallback URLs.
- `BuildCards`, `CacheLoad`, `AddManualGame` → sets `card.EmulatorAddonNames` and synthetic `GameMod`
- `InstallEmulatorAddonsAsync()` → reads `AddonUrls` per wiki name (manifest priority > wiki-scraped)
- `EmulatorConfig` fields: `addons` (wiki game name list), `addonUrls` (per-game URL overrides)

### `thirtyTwoBitGames` / `sixtyFourBitGames` — `List<string>`
Forces 32-bit or 64-bit mode independent of PE header detection. Determines which DLL variant is deployed.
- `GameInitializationService.ApplyManifest()` → populates `_manifest32BitGames` / `_manifest64BitGames`
- `MainViewModel.GameMatching.ResolveIs32Bit()` → checked after user override, before PE-header fallback

### `dlssSkipGames` — `List<string>`
Suppresses the DLSS/Streamline file scan for specific games (false positives, known irrelevant, or slow).
- `MainViewModel.BuildCards` → inline check, skips the entire DLSS detection block when matched

### `steamAppIdOverrides` — `Dict<string,int>`
Forces a specific Steam AppID regardless of ACF manifest or `steam_appid.txt`.
- `SteamAppIdResolver.ResolveAsync()` → highest priority (step 1 of 5)
- Used for PCGW lookups (HDR database, DLSS data) and Steam launch commands

---

## UE-Extended / HDR Feature Flags

### `ueExtendedGames` — `List<string>`
Games that should use `renodx-ue-extended.addon64` instead of the standard generic UE addon.
- `GameInitializationService.ApplyManifest()` → adds to `gameNameService.UeExtendedGames`
- Lowest priority UE-Extended signal — overridden by `ueExtendedCompatibility`, blocked by `noUeExtendedGames`

### `nativeHdrGames` — `List<string>`
Games that force UE-Extended on AND bypass the `hasNamedMod` block (named wiki mod won't prevent UE-Extended).
- `GameInitializationService.ApplyManifest()` → populates `_manifestNativeHdrGames`
- `IsNativeHdrGameMatch()` → drives `isNativeHdr = true` in BuildCards/Install
- Effect: forces UE-Extended, hides toggle, sets `card.IsNativeHdrGame = true`

### `noUeExtendedGames` — `List<string>`
Highest-priority block on UE-Extended. Overrides everything including `nativeHdrGames` and user opt-in.
- `GameInitializationService.ApplyManifest()` → populates `_manifestNoUeExtendedGames`
- BuildCards/AddManualGame → `noUeExtended` gate is the first check in `useUeExt` decision tree

### `ueExtendedCompatibility` — `Dict<string,UeExtendedCompatEntry>`
Highest-priority UE-Extended config. Replaces both `nativeHdrGames` and `ueExtendedGames` (v2+ feature).
Presence in the dict = forced UE-Extended. Entry controls Engine.ini deployment:
- `hdr: false` → skip HDR keys (game has its own in-engine HDR option); default: deploy for UE5, skip for UE4
- `lut: false` → skip LUT key; default: always deploy
- `GameInitializationService.ApplyManifest()` → populates `_manifestUeExtendedCompat` AND adds keys to `_manifestNativeHdrGames`
- `MainViewModel.Install.InstallModAsync()`, `UpdateOrchestrationService` → read `deployHdr`/`deployLut` from entry

### `lumaRenodxCompat` — `List<string>`
Games where RenoDX and Luma can coexist. Normally enabling Luma removes the RenoDX mod.
- `BuildCards`, `CacheLoad`, `AddManualGame` → `card.LumaRenodxCompatible = true`
- **Important:** uses direct `Contains(game.Name)` — exact match, no normalization
- Effect: keeps RenoDX row visible in Luma mode, skips RenoDX removal on Luma install

### `lumaDefaultGames` — `List<string>`
Games that auto-enable Luma mode on first detection without user action. Respects prior toggles.
- `MainViewModel.BuildCards` → auto-adds composite key to `_lumaEnabledGames` if no prior user toggle
- NOT applied during CacheLoad phase (only fires in Phase 2)

---

## Install Behaviour Fields

### `forceExternalOnly` — `Dict<string,ForceExternalEntry>`
Forces a card into redirect-only mode. Install button becomes an external link instead of a direct download.
- `GameInitializationService.ApplyManifestCardOverrides()` → sets `card.IsExternalOnly = true`, `card.ExternalUrl`, `card.ExternalLabel`, `card.WikiStatus`
- Entry has `url` (download link) and `label` (button text)
- Key must match the **detected** game name, NOT the wiki-mapped name

### `snapshotOverrides` — `Dict<string,string>`
Injects or overrides the addon download URL for a game when wiki scraping fails or captures the wrong URL.
- `BuildCards`, `Install`, `AddManualGame` → sets `effectiveMod.SnapshotUrl`
- **Warning:** any non-null `SnapshotUrl` makes `hasNamedMod = true`, blocking UE-Extended. Never add NativeHDR generic UE games here.

### `installWarnings` — `Dict<string,Dict<string,string>>`
Per-game, per-component blocking confirm dialogs before install. User can cancel to abort.
Structure: `{ "Game Name": { "reshade": "warning text", "renodx": "...", ... } }`
- `MainViewModel.Install.Luma.CheckInstallWarningAsync(gameName, component)` → shows ContentDialog
- Wired for all 8 components: `reshade`, `renodx`, `relimiter`, `dc`, `optiscaler`, `luma`, `reframework`, `dxvk`

### `dllNameOverrides` — `Dict<string,ManifestDllNames>`
Forces specific DLL proxy filenames (ReShade and/or DC) per game. Example: `{ "reshade": "winmm.dll", "dc": "" }`
- `GameInitializationService.ApplyManifest()` → populates `_manifestDllNameOverrides`
- `GetManifestDllNames()` → exact → trademark-stripped → normalized lookup
- `InstallReShadeAsync()`, `ApplyManifestDllRenames()` → use `.ReShade` filename

### `gacSymlinkGames` — `Dict<string,string>`
Routes XNA Framework games (e.g. Terraria) through a GAC symlink install instead of a normal DLL copy.
- `GetGacSymlinkPath()` → checks this dict; if found, `InstallReShadeAsync` routes to `InstallReShadeGacAsync`
- Dict value = absolute GAC directory path. Requires admin privileges.

### `legacyReShadeVersions` — `Dict<string,string>`
Auto-assigns a locked legacy ReShade channel per game (e.g. `"Max Payne 3": "6.4.1"`). Never overwrites existing user overrides.
- `MainViewModel.Init`, `BackgroundScan` → calls `SetReShadeChannelOverride(gameName, version)` if no existing override (checks both name-only and composite keys)

### `legacyReShadeAvailable` — `List<string>`
List of version strings shown in the legacy ReShade version picker dialog. Server-managed.
- `DetailPanelBuilder.Overrides.RsChannel` → RS channel picker, populates `RadioButtons` in the `"Legacy..."` selection dialog

### `launchExeOverrides` — `Dict<string,string>`
Relative exe path from InstallPath. Used for two purposes:
1. **Game launch** (priority 2, after user override): `MainWindow.Events.Install.LaunchGame()` → `Path.Combine(card.InstallPath, manifestExe)`
2. **NVIDIA profile matching**: `DlssPresetService.FindProfileUncached()` → matches the exe filename against NVIDIA driver profile application entries

### `renodxIniOverrides` — `Dict<string,Dict<string,string>>`
Per-game `[renodx]` INI keys written to `reshade.ini` on install/update. Only adds/updates — never removes user values (unless `forceOverwrite: true`).
- Read via `AuxInstallService.GlobalManifest?.RenodxIniOverrides` in: `InstallModAsync`, `UpdateAllRenoDxAsync`, `MergeRsIni` button, `RdxCogButton_Click` redeploy

### `renodxExtraSettings` — `List<RenodxExtraSetting>`
Adds extra ComboBox rows to the RenoDX ⚙ cog Compatibility Settings grid without a client update.
- `MainWindow.Events.Components.RdxCogButton_Click` → appends rows; `SelectionChanged` writes chosen value to `[renodx]` INI section
- Each entry: `key`, `label`, `default`, `options` (array of `{value, name}` pairs)

### `pdUpscalerGames` — `Dict<string,string>`
RE Engine games that need the PD-Upscaler REFramework build when OptiScaler is installed.
- `InstallEventHandler.InstallOsButton_Click` → after OptiScaler install, if `dinput8.dll` exists, swaps to PD-Upscaler REFramework
- `UninstallOsButton_Click` → restores standard REFramework on uninstall
- Dict value = nightly.link artifact name (e.g. `"RE2"`, `"RE7"`, `"RE8"`)

---

## DXVK Fields

### `dxvkBlacklist` — `List<string>`
Prevents the DXVK toggle from being enabled (greyed out with anti-cheat tooltip).
- `GameInitializationService.ApplyManifestCardOverrides()` → `card.IsDxvkBlacklisted = true`
- `GameCardViewModel.Dxvk.IsDxvkToggleEnabled` → returns `false`; tooltip shows anti-cheat warning

### `dxvkApiOverrides` — `Dict<string,string>`
Intended per-game DXVK DLL selection override (`"DX8"`, `"DX9"`, etc.). **The value string is not currently consumed** — only existence is checked.
- `ApplyManifestCardOverrides()` → `card.HasDxvkApiOverride = true` (presence only)
- Effect: unlocks DXVK toggle for games where GraphicsApi is Unknown

### `dxvkGameNotes` — `Dict<string,GameNoteEntry>`
Per-game notes shown in the DXVK Info dialog. Appended after the generic DXVK description.
- `MainWindow.Events.Install.DxvkInfoButton_Click()` → direct inline read (bypasses `AddonInfoResolver`)

---

## Info Button Content Fields

All `*GameInfo` and `gameNotes` fields are routed through `AddonInfoResolver.GetManifestDict(AddonType)` for the component info button dialogs. `DxvkGameNotes` is the exception — read directly in the DXVK info button handler.

| Field | Component info button | Notes |
|-------|----------------------|-------|
| `gameNotes` | RenoDX | Also has a secondary path via `ApplyManifestCardOverrides` → `card.Notes` |
| `reshadeGameInfo` | ReShade | — |
| `relimiterGameInfo` | ReLimiter | — |
| `displayCommanderGameInfo` | Display Commander | — |
| `reframeworkGameInfo` | RE Framework | — |
| `optiScalerGameInfo` | OptiScaler | — |
| `lumaGameInfo` | Luma (primary) | — |
| `lumaGameNotes` | Luma (supplementary) | Also has secondary path via `ApplyManifestCardOverrides` → `card.LumaNotes`; also read directly in `AddonInfoResolver.TryResolveLumaWiki()` as a supplement after LumaMod wiki notes |
| `dxvkGameNotes` | DXVK | Read inline in `DxvkInfoButton_Click`, NOT via `AddonInfoResolver` |

All use `GameNoteEntry` schema: `{ notes, notesUrl, notesUrlLabel }`.

---

## Wiki Status / Author Fields

### `wikiStatusOverrides` — `Dict<string,string>`
Overrides the status emoji on wiki `GameMod` entries (e.g. `"✅"`, `"🚧"`) without requiring a wiki edit.
- `GameInitializationService.ApplyManifestStatusOverrides()` → iterates `_allMods`, sets `mod.Status`
- Called after wiki fetch in both Init and BackgroundScan

### `wikiUnlinks` — `List<string>`
Completely severs a game from the wiki/mod system. No RenoDX row, no generic engine fallback.
Unlike `blacklist`, the card still appears — it just has no mod options.
- `GameInitializationService.ApplyManifest()` → populates `_manifestWikiUnlinks`
- `BuildCards`, `InstallModAsync`, `AddManualGame` → if `_manifestWikiUnlinks.Contains(game.Name)` → `mod = null`, `fallback = null`

### `donationUrls` — `Dict<string,string>`
Donation page URLs keyed by author display name. Merged into the hardcoded dictionary; manifest entries take priority.
- `MainViewModel.Init`, `BackgroundScan` → `GameCardViewModel.MergeManifestAuthorData(DonationUrls, AuthorDisplayNames)`

### `authorDisplayNames` — `Dict<string,string>`
Display-name overrides for wiki maintainer handles (e.g. `"oopydoopy": "Jon"`). Merged into hardcoded dict.
- `GameCardViewModel.MergeManifestAuthorData()` → same call as `donationUrls`

### `authorOverrides` — `Dict<string,string>`
Sets the mod author for games with no wiki entry (Discord/Nexus-only mods).
- `GameInitializationService.ApplyManifestCardOverrides()` → `card.Maintainer = author` (only if `card.Maintainer` is empty)

---

## URL Override Fields

All follow the same pattern in their respective services — checked first before any scraped/cached source.

| Field | Used in | What it overrides |
|-------|---------|-------------------|
| `nexusUrlOverrides` | `NexusModsService` | Game → Nexus Mods page URL |
| `pcgwUrlOverrides` | `PcgwService` | Game → PCGamingWiki page URL |
| `uwFixUrlOverrides` | `UltraWideFixService` | Game → ultrawide fix URL |
| `ultraPlusUrlOverrides` | `UltraPlusService` | Game → Ultra+ URL |
| `steamAppIdOverrides` | `SteamAppIdResolver.ResolveAsync()` | Forces Steam AppID (highest priority, before ACF/file detection) |

### `optiScalerWikiNames` — `Dict<string,string>`
Maps RHI game names to their OptiScaler wiki compatibility list names (when they differ).
- `AddonInfoResolver.ResolveOptiScalerWikiName()` → used before all OptiScaler wiki lookups

---

## NVIDIA Profile / DLSS Fields

### `profileExeExclusions` — `List<string>`
Additional exe names excluded from NVIDIA profile matching (extends the hardcoded defaults).
- `DlssPresetService.ApplyManifestProfileConfig()` → merged into `_excludedProfileExeNames`
- `FindProfileUncached()` → `exeNames.ExceptWith(_excludedProfileExeNames)`

### `profileNameOverrides` — `Dict<string,string>`
Redirects a game name to a different NVIDIA driver profile name (first lookup attempted, before exe scanning).
- `DlssPresetService.ApplyManifestProfileConfig()` → stored as `_profileNameOverrides`
- `FindProfileUncached()` → checked before exact title match and exe scanning

### `dlssPresets` — `ManifestDlssPresets`
Injects new DLSS preset options (SR/RR/FG) into the detail panel dropdowns without a client update.
- `DlssPresetService.ApplyManifestPresets()` → merges `.Sr`, `.Rr`, `.Fg` into static preset arrays
- Called at Init and BackgroundScan. `disabled: true` entries remove existing presets by name.

### `rtxHdrInfoUrl` — `string`
URL for the RTX HDR Calibration Guide hyperlink in the RenoDX info dialog. Falls back to a hardcoded Reddit post.
- Read in `DialogService.Game` info dialog builder

---

## DOF Fix Fields

### `dofFixSkipGames` — `List<string>`
Suppresses DOF Fix eligibility for specific games (no DOF issue, or known incompatible).
- `MainViewModel.Init`, `BackgroundScan` → `DofFixService.SetSkipGames()`
- `DofFixService.IsEligible()` → returns `false` if game is on the list

### `dofFixForceGames` — `List<string>`
Force-enables DOF Fix eligibility for games where UE engine detection fails. 64-bit requirement still applies.
- `MainViewModel.Init`, `BackgroundScan` → `DofFixService.SetForceGames()`
- `DofFixService.IsForceEligible()` → returns `true`

---

## Graphics API Fields

### `graphicsApiOverrides` — `Dict<string,string>`
Forces a specific graphics API badge, overriding all PE import scanning.
Supports comma-separated multi-API: `"DX12, VLK"` marks a game as dual-API.
Valid tokens: `DX8`, `DX9`, `DX10`, `DX11`, `DX12`, `Vulkan`/`VLK`, `OpenGL`/`OGL`.
- `MainViewModel.GameMatching.DetectGraphicsApi()` and `_DetectAllApisForCard()` → checked after user API override, before all filesystem scanning
- Affects: API badge on card, auto-selected ReShade DLL filename, DXVK toggle visibility, DXVK DLL deployment

---

## Pack / Preset Override Fields

### `shaderPacks` — `Dict<string,ManifestShaderPack>`
Add, override, or disable shader packs without a client update.
- `ShaderPackService.ApplyManifestOverrides()` → called at Init and BackgroundScan
- `disabled: true` removes the pack from the active list
- New packs require at minimum a `url` and `kind` (`"GhRelease"` or `"DirectUrl"`)

### `addonPacks` — `Dict<string,ManifestAddonPack>`
Add, override, or disable addon entries without a client update. Keyed by `SectionId`.
- `AddonPackService.ApplyManifestOverrides()` → called at Init and BackgroundScan; also re-applied when the Addon Manager dialog opens
- `disabled: true` removes the addon

### `componentUrls` — `Dict<string,string>`
Override base download URLs for components. Active keys:
- `"ueExtended"` → UE-Extended addon download URL (`MainViewModel.Install.UeExtendedUrl`)
- `"ueDofFix"` → DOF Fix URL override (`DofFixService.ManifestUrlOverride`, set at Init/BackgroundScan)

---

## Other Fields

### `version` — `int`
Manifest version integer. Logged on every `ApplyManifest` call for diagnostics.

### `gacSymlinkGames` — see [Install Behaviour Fields](#gacSymlinkGames)

---

## Key Architectural Notes

1. **Two apply paths**: Most fields are processed by `GameInitializationService.ApplyManifest()` (populates shared sets/dicts), then read by BuildCards/CacheLoad. Card-level fields (`forceExternalOnly`, `gameNotes`, `authorOverrides`, etc.) are applied by `ApplyManifestCardOverrides()` after cards are built.

2. **Three init paths**: Fields that affect game display must be applied in ALL three: `InitializeAsync` (full scan), `LoadCacheAndBuildCardsAsync` (Phase 1 cache display), AND `RunBackgroundScanAndMergeAsync` (Phase 2 update). Missing one causes inconsistent state between phases.

3. **Case sensitivity**: `ManifestService.Normalize()` rebuilds most dicts with `StringComparer.OrdinalIgnoreCase`. Fields NOT normalized: `dxvkApiOverrides` (lookups are case-sensitive as deserialized). `lumaRenodxCompat` uses direct `Contains(game.Name)` — exact match.

4. **Name matching**: `GetManifestDllNames()`, `GetGacSymlinkPath()`, `ResolveEngineOverride()` all try: (1) exact match, (2) trademark-stripped (™®©), (3) fully normalized. Most other fields use exact match only.

5. **User override priority**: Manifest `WikiNameOverrides` only adds if key not already in `_nameMappings` (user wins). Manifest `DllNameOverrides` are blocked by `DllOverrideService` per-game opt-outs. Manifest `LegacyReShadeVersions` doesn't overwrite existing RS channel overrides.

6. **`AuxInstallService.GlobalManifest`**: A static reference to the live manifest, accessible from services that don't receive the manifest via DI. Used by `RenodxIniOverrides`, `RenodxExtraSettings`, `UeExtendedCompatibility` in Update flow.
