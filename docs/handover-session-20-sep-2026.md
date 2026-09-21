# Handover Report — 20 September 2026

## Session Summary

Long session covering a mix of bug fixes, UI cleanup, and new features. App is currently at v2.7.6 (code changes staged but version not yet bumped — see below). All changes pushed to main.

---

## What Was Done

### v2.7.5 Bug Fixes (all pushed, version tag exists)

- **Luma reinstall crash** — `InstallLumaAsync` now returns a friendly message for bespoke drag-drop mods with no download URL instead of throwing.
- **DLSS5 Feeder host64 not deployed** — host64 folder setup was incorrectly gated on `isDx9`. Now runs for all 32-bit Feeder installs regardless of API detection. dgVoodoo2 deploy remains DX9-only.
- **Feeder DX9 detection via `DetectedApis`** — all five DX9 checks in the NeuralRendering file now fall back to `card.GraphicsApi` when `DetectedApis` is empty (e.g. Gothic II, Diablo GOG).
- **32-bit Feeder gets 64-bit addon** — versioned staging only stores `.addon64`. 32-bit games now always source from AddonPackService which has the correct `.addon32`.
- **Feeder shaders (LumeniteFX + Feed.fx) not persisting** — shader mode/selection was written inside a `TryEnqueue` which raced with concurrent saves. Now written synchronously on the install thread.
- **`PerGameShaderSelection` load guard** — removed incorrect filter that dropped entries without a matching `PerGameShaderMode` key.
- **NVAPI panel freeze after sleep/wake** — `BuildDriverProfileSection` and `BuildNvidiaProfileSection` NVAPI reads now wrapped in `Task.WhenAny` + 5-second timeout (matching the Settings page fix in v2.7.4).

### v2.7.6 Work (pushed, version NOT bumped yet)

#### Features
- **Luma GitHub releases API merge** — `LumaService.FetchReleasesModsAsync()` fetches the latest Luma release assets and `MergeLumaMods()` merges with wiki results using wiki-wins priority:
  1. Wiki entry with any URL → keep as-is
  2. Wiki entry with no URL → fill in GitHub URL from release asset
  3. No wiki entry → add as new LumaMod from release asset
  Both `MainViewModel.Init.cs` and `MainViewModel.BackgroundScan.cs` launch the releases fetch in parallel with the wiki fetch.
- **HDR Mods separator** — the separator between ReShade and RenoDX in the Components panel now always shows (previously only when Luma was present) and reads "HDR Mods" instead of "HDR Mods — Install one or both".

#### Bug Fixes
- **Spurious `renodx-dlss5.addon64` cleanup** — one-time migration in `AddonPackService.EnsureLatestAsync` removes `renodx-dlss5.addon64` from game folders tracked by AddonPackService where `rhi_install.txt` shows `NrMethod = ShortFuse` or `Feeder`. These were deployed when the global addon picker still included DLSS5 Tool.
- **DLSS NR OptiScaler link** — version number click and Info button now open the DLSS NR fork's releases page (`github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases`) when that variant is installed, instead of the main OptiScaler wiki.

#### UI Changes
- **Simple View removed** — `CompactViewBuilder`, all three event handlers (`LayoutToggle_Click`, `CompactNavLeft_Click`, `CompactNavRight_Click`), and all Compact branches in `GameList_SelectionChanged`, `MainWindow_Activated`, skeleton builder, and UISync have been removed. `ViewLayout.Compact` still exists in the enum but is never set. `CurrentViewLayout` always returns `ViewLayout.Detail`.

#### Addon Picker Cleanup
- **5 NR addons hidden from picker** — `HideFromPicker = true` on `DLSS5 Tool`, `DLSS Tool (ShortFuse)`, `MFG Ada Unlock`, `DLSS5 Feeder`, `DLSS5 DX11 Bridge`. Both `AddonManagerDialog` and `AddonPopupHelper` filter these out. Startup migrations in `SettingsViewModel.LoadSettingsFromDict` and `GameNameService.LoadNameMappings` remove them from `EnabledGlobalAddons` and `PerGameAddonSelection` respectively.

#### Manifest Changes
- `engineIniPathOverrides` — added `"Clive Barker's Hellraiser: Revival Demo": "Hellraiser_Demo"`
- `lumaNameOverrides` — added `"L.A. Noire": "L.A. Noire"` for release asset matching
- `installWarnings` — added `luma` warnings for 21 WIP/release-only Luma mods (Batman Arkham Knight, Beyond Two Souls, Blue Reflection Second Light, Dying Light 2, Far Cry 5, Final Fantasy XV, Halo MCC, Heaven Burns Red, Kingdom Come Deliverance, L.A. Noire, Mirror's Edge Catalyst, Monster Hunter World, Mortal Kombat 11, NieR Automata, Hatsune Miku PDMM+, Saints Row Third Remastered, Sekiro, Shenmue I&II, Snowbreak, The Evil Within 2, Titanfall 2)

---

## Pending / Next Steps

### Version bump needed
The csproj is still at `2.7.5.0`. Before releasing v2.7.6, bump `AssemblyVersion` and `FileVersion` to `2.7.6.0`.

### Luma name matching may need tuning
`FetchReleasesModsAsync` extracts game names from asset filenames using `NormalizeForMerge` (strips all non-alphanumeric, lowercases, collapses spaces). The install warning game name keys in `installWarnings` use best-guess Steam detected names — some may not match exactly (e.g. trademark symbols in `"NieR:Automata™"`, `"Sekiro™: Shadows Die Twice"`, `"Titanfall® 2"`, `"Batman™: Arkham Knight"`). Test by detecting those games and checking if the warning appears on Luma install click.

### Luma WIP mods need lumaNameOverrides in manifest
The 21 WIP mods added to `installWarnings` will need matching entries in `lumaNameOverrides` if their Steam folder name doesn't normalize-match the release asset filename stem. The `MatchLumaGame` function uses `_gameDetectionService.NormalizeName` (strips trademarks, lowercases, removes non-alphanumeric) so most should auto-match — but verify with actual installed games.

### publish.bat not run
No publish was done this session. The user runs from `C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI\RHI.exe` — run `publish.bat` to deploy when ready.

---

## Key Files Changed This Session

| File | Change |
|---|---|
| `Services/LumaService.cs` | Added `FetchReleasesModsAsync()`, `MergeLumaMods()`, `NormalizeForMerge()` |
| `Services/ILumaService.cs` | Added `FetchReleasesModsAsync()` to interface |
| `Services/AddonPackService.cs` | Added DLSS5 Tool spurious addon cleanup migration; `HideFromPicker` post-processing in `ApplyManifestOverrides` |
| `Models/AddonEntry.cs` | Added `HideFromPicker = false` parameter |
| `AddonManagerDialog.cs` | Filter `!e.HideFromPicker` |
| `AddonPopupHelper.cs` | Filter `!a.HideFromPicker` |
| `ViewModels/SettingsViewModel.cs` | Migration: remove 5 NR packages from `EnabledGlobalAddons` on load |
| `Services/GameNameService.cs` | Migration: remove 5 NR packages from `PerGameAddonSelection` on load; always force `ViewLayout.Detail` |
| `ViewModels/MainViewModel.cs` | Default `_currentViewLayout = ViewLayout.Detail`; removed `CompactViewVisibility`, `LayoutToggleLabel`, `NextViewLayout()`, `NavigateCompactPage()`, `CompactPageIndex` |
| `ViewModels/MainViewModel.Init.cs` | Added `lumaRelTask`; merge wiki + releases |
| `ViewModels/MainViewModel.BackgroundScan.cs` | Same |
| `MainWindow.xaml` | Removed `LayoutToggleBtn`, separator, `CompactNavLeft`, `CompactNavRight`; updated HDR Mods separator text |
| `MainWindow.xaml.cs` | Removed `_compactViewBuilder`; simplified compact branches |
| `MainWindow.Events.cs` | Removed `LayoutToggle_Click`, `CompactNavLeft_Click`, `CompactNavRight_Click` |
| `MainWindow.UISync.cs` | Removed Compact rebuild on `TotalGames` change |
| `MainWindow.Skeleton.cs` | Removed compact skeleton hide block |
| `MainWindow.Events.Install.cs` | DLSS NR link routing |
| `DetailPanelBuilder.Components.cs` | HDR Mods separator always visible |
| `DetailPanelBuilder.Extras.cs` | DLSS NR link routing |
| `DetailPanelBuilder.NeuralRendering.cs` | Feeder fixes (host64 gate, DX9 fallback, shader persistence, 32-bit versioned staging) |
| `manifest.json` | Engine.ini path, lumaNameOverrides, installWarnings |
| `RHI_PatchNotes.md` | v2.7.5 and v2.7.6 sections complete |
