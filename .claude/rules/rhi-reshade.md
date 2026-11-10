---
paths:
  - "**/*ReShade*"
  - "**/*AuxInstall*"
  - "**/*Vulkan*"
  - "**/*reshade*"
---

<!-- Copied from .kiro/steering/rhi-reshade.md. Keep both in sync while Kiro is still used. -->

# ReShade Knowledge

## Channels & Staging
- 3 staging folders: `reshade/` (stable addon), `reshade-nightly/`, `reshade-normal/`
- Per-game channel override: "Stable" or "Nightly" (null = defaults to Stable)
- Global channel always resolves to "Stable" (global selector removed from Settings page)
- `AddonType` field: "ReShade" (addon) or "ReShadeNormal" (non-addon)

## Vulkan Games
- Share global layer in `C:\ProgramData\ReShade\` — requires `CopyFileWithElevation()`
- Vulkan ReShade update exclusion is all-or-nothing (shared DLL)
- `RequiresVulkanInstall` drives Vulkan-specific UI and install paths
- **One layer, one channel**: `VulkanLayerService.SyncLayerToChannel` copies the staged build over `ReShade64.dll` (and `ReShade32.dll` if present) when the contents differ (SHA-256, never size: Stable and Nightly 6.8.0 are both 5,592,064 bytes). The channel comes from `MainViewModel.ResolveVulkanLayerChannel` / `VulkanLayerService.ResolveLayerChannel` (first non-Stable override among Vulkan games). Every channel change on a Vulkan game (Stable, Nightly, Legacy, Custom) is applied to ALL Vulkan games.
- Both install paths (`InstallReShadeVulkanAsync`, layer present or not) call `SyncVulkanLayerAsync`: before Nov 2026 the light path only wrote reshade.ini, so the layer kept whatever version was first installed (users stuck on 6.7.3).
- Update check: `CheckForUpdatesAsync` flags installed Vulkan cards `UpdateAvailable` when the layer differs from staged Stable/Nightly (`IsLayerOutdated`); legacy/Custom are never flagged. Vulkan cards count in `IsUpdateAllCandidate`.

## Nightly
- Update detection uses Content-Length comparison (Last-Modified unreliable from nightly.link)
- One-time Nightly migration: `__nightly_migration_pending` flag → all games without override get "Nightly"

## Legacy ReShade
- RS Channel dropdown: Stable, Nightly, Legacy... (opens version picker), Custom
- Version list from manifest `legacyReShadeAvailable` array (zero app maintenance)
- URL pattern: `https://reshade.me/downloads/ReShade_Setup_X.Y.Z_Addon.exe`
- Game excluded from RS update checks when on legacy version
- Staging: `%LocalAppData%\RHI\LegacyReshade\{version}\` (on-demand download)
- Manifest `legacyReShadeVersions`: forced per-game versions
- Addon compatibility warning shown for legacy versions
- `InstallReShadeAsync` must download on-demand if staged DLLs don't exist

## Custom ReShade
- Folder: `%LocalAppData%\RHI\Custom\ReShade\` — user places any `.dll` files
- Per-game selection: `GameNameService.CustomReShadeSelection` dictionary
- RS Channel "Custom": picker dialog with radio-button checkboxes, then deploys selected DLL
- `IsLegacyVersion()` excludes "Custom" (returns false)
- Vulkan: shows "Apply to All Vulkan Games", propagates to all, copies to ProgramData with elevation

## INI Template Selection
- `IsRdr2(gameName)` selects template (includes RDR2 AND Max Payne 3)
- RDR2 template: special depth buffer settings, END key for overlay
- DX games with RDR2 template: `StripPreprocessorDefinitions` removes Vulkan-only line
- Hotkey deployment skips these games when hotkey is default (Home)

## ReShade Hotkey Format
- KeyOverlay INI format: `vk,ctrl,shift,alt` (NOT `vk,shift,ctrl,alt`)
- Second position is Ctrl, third is Shift

## Shader Packs
- 46 packs in `ShaderPackService.cs` `DefaultPacks` (hardcoded fallback)
- Manifest-driven: `shaderPacks` field can add/override/disable without app updates
- Merge: `disabled: true` removes, matching ID overrides, new ID adds
- Categories: Essential, Recommended, Extra. Source: `GhRelease` or `DirectUrl`
- Extraction expects `Shaders/` or `Textures/` subdirs. Fallback: root `.fx`/`.fxh` treated as shaders.
- Direct `.fx`/`.fxh` file support: copied to `Shaders/{packId}/` directly (no archive open)
- Pack update cleanup: after a complete extraction, `RemoveFilesNoLongerInPack` deletes files in `Shaders\{packId}` / `Textures\{packId}` that the new version didn't write (renamed/removed shaders). Never before extraction, and never with an empty keep-set. Game folders are already cleaned by the mirror, which deploys only recorded files.
- Deploy-time dependency expansion: `ExpandDependencies` (BFS) transitively resolves `Requires`
- Three expansion points: (1) UI picker, (2) deploy-time, (3) `ApplyPresetShadersAsync`
- `Blending.fxh` cross-pack: `CrosireLegacy` requires `CrosireMaster`

## Luma Framework
- Toggle per game — swaps between RenoDX/standard ReShade and Luma mode
- `lumaDefaultGames` in manifest auto-enables on first detection
- Uninstall must remove `Luma/` folder from game directories
- Game names with special chars (®) need both variants in manifest lists

### Luma Drag-Drop / File Watcher
- Detection: `DragDropHandler.IsLumaArchive(path)` — peeks for `Luma/d3dcompiler_47.dll`
- Drag-drop runs BEFORE generic addon handler. File watcher fuzzy-matches filename.
- `LumaService.InstallFromArchiveAsync` handles zip (native) and 7z (bundled exe)
- Multi-folder archives: folders starting with `(` filtered out. Multiple valid → folder picker dialog.
- If no `dxgi.dll` in archive → deploy cached stable addon ReShade as fallback
- `AddonFileWatcher` only triggers for archives with "renodx" or "luma" in filename
- Post-install: enable Luma mode, set `RsStatus = Installed`, save settings, delete source from watch folder

### Luma + RenoDX Coexistence
- Manifest field `lumaRenodxCompat`: games where RenoDX can coexist with Luma
- Multiple computed properties check `!EffectiveLumaMode` — ALL must add `|| LumaRenodxCompatible`

### Luma Uninstall Order
- `RemoveFromGameFolder` BEFORE deleting individual tracked files (marker check)
- Also delete `reshade-shaders-original` after uninstall

### Luma Install reshade.ini Ordering
- Skip extracting `reshade.ini` from zip. Pass screenshot/hotkey/game params to `MergeRsIni`.
- Cannot move `MergeRsIni` after extraction — `AddonPath` must be present before extraction.

## Load ReShade as ASI (per game)
- ReShade ⚙ › Loading › "Load ReShade as ASI" (Off/On). Stored in `GameNameService.RsAsiGames` (composite key, saved as `RsAsiGames`). `MainViewModel.ReShadeAsi.cs`: `GetRsAsiMode` / `SetRsAsiModeAsync` (renames the installed file; returns an error message and leaves the setting unchanged if it can't).
- Name: `AuxInstallService.RsAsiName(is32Bit)` = `ReShade64.asi` / `ReShade32.asi`. RHI only renames; it does NOT install an ASI loader (user's choice).
- `AuxInstallService.RsAsiModeResolver` (wired in `MainViewModel.Init`) makes `InstallReShadeAsync` deploy as the .asi for every install/update/Update All path, ahead of DLL overrides.
- While on: ReShade's DLL name dropdown in Game Overrides is disabled (reason on the "ReShade" label), and `ApplyManifestDllRenames` / `ReconcileDefaultNaming` skip the ReShade rename. Off: name = user DLL override → manifest name → API default; `ReShade64.dll` if OptiScaler holds that name.
- Hidden for Vulkan games (global layer). Separate from the ShortFuse auto-config, which renames to `Reshade64.asi` on its own.

## GAC Symlink Games (XNA Framework)
- Games load DLLs from GAC (`C:\Windows\Microsoft.NET\assembly\GAC_32\...`)
- Manifest field `gacSymlinkGames`: game name → GAC directory path
- Install creates symlinks from GAC to staged files. Both install AND uninstall require admin.
- No RsRecord created — uninstall checks `GetGacSymlinkPath` instead.

## ReShadePreset.ini Convention
- ONLY called from dedicated "Deploy ReShadePreset.ini" button in RS cog dialog
- Full `File.Copy(..., overwrite: true)` — no merge
- NOT called during install, Update All, Mass INI Deploy, or other paths

## Addon Manager
- Deploy name priority: `DeployFileName` → `OriginalName` (from zip) → URL filename → sanitized name
- Staging always uses sanitized name. Original name only for deployment.
- Stale removal must track ALL naming variants.
- `addon_deployments.json` tracker: only deletes tracked files; user-placed untouched.
- Auto-backfill migration: re-downloads zip addons with missing `OriginalName` on startup.
- `FileSystemWatcher` only fires `Created` — overwrites require delete-then-create pattern.

## Peak Nits (ToneMapPeakNits)
- Global setting in `[renodx-preset*]` sections. Static cache on `AuxInstallService`.
- Preset filter: only checked preset numbers receive the value.
- Auto-creates missing `[renodx-preset{N}]` sections for checked presets not in INI.
- **Key spelling depends on the mod**: Unity / generic RenoDX mods save `toneMapPeakNits` / `toneMapGameNits` / `toneMapUINits`, others (e.g. UE-Extended, Lies of P) `ToneMapPeakNits`. ReShade's ini is case-sensitive. `ApplyPeakNits` (global peak nits) and the RenoDX ⚙ nits row add `ToneMapPeakNits` (capital T) to sections without the key, so a lower-case mod's file ends up with both spellings (AI Limit, Nov 2026) and RHI's value is ignored by that mod. `ParseIni` is case-insensitive and merges them (keeps the first spelling, last value), so any ParseIni/WriteIni pass over reshade.ini can silently change the mod's real value. Display modes (`DisplayModeItems.ReadRdx` / `WriteRdx`) edit line by line instead: read the lower-case spelling first when both exist (RHI only ever wrote capital T), write every spelling present, add both spellings where neither exists.
