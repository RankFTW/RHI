# Handover — 29 September 2026 (Session B)

## Overview

Long session covering: component update log feature, UI freeze root-cause fix (WinUI star column infinite layout loop), NVIDIA panel split into two independent sections, DXVK DX11 install/uninstall fixes, OptiScaler CET/plugins folder deletion bug, archive watch-folder cancel deletion bug, Driver Settings summary label corrections, and various patch notes / steering updates.

---

## Current State

- **Branch**: `main`
- **Latest commit**: `2dea5a3` — Fix: Driver Settings summary shows correct ReBAR state
- **Version**: `2.7.9.0` in csproj (not bumped — user bumps manually before release)
- **Build**: clean, published to `C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI\RHI.exe`
- **Active release**: v2.8.0 Beta (unreleased, actively being tested by users)
- **v2.7.9 status**: released

---

## What Was Done This Session

### Component Update Log (v2.7.9)
- New `UpdateLogEntry` model, `IUpdateLogService` / `UpdateLogService` — persists to `%LocalAppData%\RHI\update_log.json`, 200-entry cap, 1s debounced save
- "Updates" button in bottom bar opens `UpdateLogWindow` — dated groups (Today/Yesterday/date), coloured category badges, Clear History
- Instrumented: ShaderPackService, AddonPackService, Renodx5AddonService, OptiScalerService.Staging (nightly + patcher), DgVoodooService, ReShadeUpdateService, NormalReShadeUpdateService, MainViewModel.Update (DC + UL)
- RenoDX mod installs/updates: subscribed to `_installer.InstallCompleted` event in MainViewModel constructor — captures both user-initiated and AutoUpdateService batch updates
- Dedup logic: skips recording if the most recent entry for the same component already has the same NewVersion
- Shader pack hash versions display as "Updated" (hashes are unreadable)
- OldVersion for OptiScaler Nightly: captured before staging is cleared via `previousStagedVersion` at top of `EnsureNightlyStagingAsync`

### NVIDIA Profile Panel Split (v2.8.0 Beta)
Split "Nvidia Profile Overrides" into two independent sections:
- **DLSS / Streamline** (section key `"NvidiaProfileDlss"`) — SR, RR, FG, SL columns, driver version in header
- **Driver Settings** (section key `"NvidiaProfileDriver"`) — VSync, Low Latency, Smooth Motion, Power/G-Sync, ReBAR, driver version in header

Each has: own XAML container (`NvidiaProfileDlssContainer`/`Panel`, `NvidiaProfileDriverContainer`/`Panel`), own collapse/expand toggle, own drag handle, own collapsed summary line, own `CollapsedDetailSections` key.

Migration: old `"NvidiaProfile"` key in `DetailSectionOrder` and `CollapsedDetailSections` is automatically expanded into both new keys in `SettingsViewModel.LoadSettings()`.

Collapsed summary:
- DLSS: `SR 310.9.1 · RR … · FG … · SL …`
- Driver: `VSync Force Off · ReBAR On` (Smooth Motion shown only when On; ReBAR shown only when not Auto)

Driver Settings summary bug fixed: `ReBarEnableMode` values are `0=Off, 1=Auto, 2=On` — original code checked `== 1` for On (wrong), now correctly checks `== 2`.

**Key change**: `BuildDriverProfileSection` is now fully independent — it no longer appends to `_nvBodyPanel` from the DLSS section. Both sections are called sequentially in `DetailPanelBuilder.Overrides.cs`. `BuildNvidiaProfileBody` no longer calls `BuildDriverProfileSection` at the end.

### UI Freeze Root Cause Fix (v2.8.0 Beta)
**Root cause confirmed**: WinUI 3 enters an infinite measurement loop when a `Grid` with star (`*`) columns is nested inside a `StackPanel` inside a `ScrollViewer` with no height constraint. The driver grid (4 star cols) and DLSS grid (4 star cols) were triggering this.

**Fix**: replaced all `GridLength.Star` column definitions in both grids with calculated fixed-pixel widths:
- `colW = (containerWidth - overhead) / ColCount` where overhead = (numCols + numDividers - 1) × ColumnSpacing + numDividers × dividerWidth
- `containerWidth` is read from `driverContainer.ActualWidth` or `NvidiaProfileDriverPanel.ActualWidth` inside `TryEnqueue(Low)` where it's already rendered

Additionally added `MaxDropDownHeight = 300` to all ComboBoxes in the NVIDIA/DLSS grids — `ComboBox` inside `StackPanel` inside `ScrollViewer` also causes infinite popup measurement.

Additional guards added: skip rebuild when Settings panel is open OR any dialog is open (`DialogService.IsDialogOpen` — uses `_dialogGate.CurrentCount == 0`).

### DXVK DX11 Install/Uninstall Fixes (v2.8.0 Beta)
DX11 DXVK now behaves the same as DX9 for all user-visible behaviour. Key insight: **DX11 must NOT flip `GraphicsApi` to Vulkan** (unlike DX9 which genuinely runs Vulkan) — this would break `SwitchReShadeForDxvk`, uninstall guards, and `IsDxvkToggleVisible`.

What DX11 DXVK does differently from DX9:
- Keeps `GraphicsApi = DirectX11` (intentional — DX11+DXVK is a translation layer, not a native Vulkan game)
- Does NOT add Vulkan to `DetectedApis` (intentional)
- Uses `VulkanRenderingPath = "Vulkan"` (same as DX9) to signal Vulkan RS mode
- `RequiresVulkanInstall` returns true via `DxvkEnabled && GraphicsApi is not DX8/DX9` clause

Three files changed:
1. **`DxvkService.Install.cs` step 11** — sets `VulkanRenderingPath = "Vulkan"`, clears stale RS record, reads Vulkan RS version, `RefreshBackupState()`, `NotifyAll()` for DX10/DX11 path
2. **`MainViewModel.Dxvk.cs`** — `SetVulkanRenderingPath` condition changed from `InstalledDlls.Contains("d3d9.dll")` to `card.DxvkStatus == Installed && card.VulkanRenderingPath == "Vulkan"`; `InstallDxvkAsync` sets `card.DxvkEnabled = true`; `UninstallDxvkAsync` sets `card.DxvkEnabled = false`, deploys shaders after DX proxy restore
3. **`BuildCards`/`CacheLoad`** — `isDxvkVulkan` condition extended to also fire when `VulkanRenderingPath == "Vulkan"` (catches DX11 installs on restart); DX9 sub-path (`isDx9Dxvk`) still flips GraphicsApi to Vulkan, DX11 sub-path only sets `VulkanRenderingPath`

`IsDxvkToggleVisible` escape hatch extended: now also returns true when `InstalledDlls.Contains("d3d11.dll")` or `Contains("dxgi.dll")` — keeps the DXVK row visible after DX11 install.

**Stale library fix**: one user had `TUNIC|Steam` stuck in `DxvkEnabledGames` from a pre-fix uninstall. Manually removed from `game_library.json` via PowerShell. Future uninstalls are safe — `card.DxvkEnabled = false` is now set before `SaveLibrary()`.

### OptiScaler Uninstall CET/Plugins Fix (v2.8.0 Beta)
**Bug**: `UninstallAsync` step 3b iterated `deployedFolderNames` which unconditionally included `"plugins"`, then did `GetFiles(..., SearchOption.AllDirectories)` on every folder and deleted everything — wiping Cyber Engine Tweaks, RED4ext, etc. on Cyberpunk 2077.

**Fix**: added `"plugins"` to the skip list in step 3b (alongside `"Licenses"` and `"docs"`). Step 4b already handles `OptiPatcher.asi` cleanup safely (only deletes that one file, only removes the folder if empty after).

### Archive Watch-Folder Cancel Bug Fix (v2.8.0 Beta)
**Bug**: `HandleArchiveFile` called `DeleteFromWatchFolder(filePath)` unconditionally after `ProcessDroppedArchive` returned — whether the user installed or cancelled inside the dialog.

**Fix**: removed `DeleteFromWatchFolder` call from `HandleArchiveFile` entirely. Archives are never auto-deleted (they're large files the user may want to keep). Direct `.addon` files from the watch folder still auto-delete after successful install (unchanged).

`ProcessDroppedArchive` return type changed from `Task` to `Task<bool>` for future use, though the return value isn't currently consumed by `HandleArchiveFile`.

### Other Fixes
- **Driver Settings collapsed summary ReBAR label**: `ReBarEnableMode` values are `0=Off, 1=Auto, 2=On` — original code checked `== 1` for "On" (wrong). Fixed to `== 2`. Auto is now omitted from summary (same treatment as Smooth Motion Off).
- **Minimum window width**: set to 1220px in `NativeInterop.MinWindowWidth` (enforced via `WM_GETMINMAXINFO`).
- **Steering doc** (`rhi-knowledge.md`): updated with entries from 29-sep-2026.md handover.
- **ROADMAP.md**: added Known Issues section with NVIDIA panel AddToTree freeze (now fixed) and Windows Defender slow first launch.

---

## Open Investigation: NVIDIA Panel AddToTree Freeze

**RESOLVED** — root cause was WinUI 3 infinite layout loop with star columns in ScrollViewer. Fixed by:
1. Fixed-pixel column widths (eliminates the layout loop entirely)
2. `MaxDropDownHeight = 300` on all combos (prevents ComboBox popup measurement loop)
3. Settings-open / dialog-open guards (defensive, skips rebuild when panel not visible)

No further action needed. If freeze recurs, check for any new star-column grids added to the NVIDIA/DLSS panels.

---

## Steering Doc Updates Needed

Add to `rhi-knowledge.md`:

- **DXVK DX11 install**: `DxvkService.InstallAsync` step 11 now sets `VulkanRenderingPath = "Vulkan"` for DX10/DX11 games (NOT `GraphicsApi = Vulkan` — that breaks everything). `InstallDxvkAsync` sets `DxvkEnabled = true`. `UninstallDxvkAsync` sets `DxvkEnabled = false` and deploys shaders. `IsDxvkToggleVisible` escape hatch covers `d3d11.dll` and `dxgi.dll` in InstalledDlls. BuildCards/CacheLoad detect DX11 DXVK via `VulkanRenderingPath == "Vulkan"` persisted key.

- **NVIDIA panel is now two sections**: `"NvidiaProfileDlss"` and `"NvidiaProfileDriver"`. XAML containers: `NvidiaProfileDlssContainer`/`Panel` and `NvidiaProfileDriverContainer`/`Panel`. Old `"NvidiaProfile"` key is migrated in `SettingsViewModel.LoadSettings()`. `BuildNvidiaProfileSection` owns the DLSS section only; `BuildDriverProfileSection` is now fully independent with its own header/drag handle/collapse. Both called from `DetailPanelBuilder.Overrides.cs`.

- **WinUI infinite layout loop**: Any `Grid` with star columns nested inside `StackPanel` inside `ScrollViewer` causes a permanent UI freeze. Fix: use calculated fixed-pixel column widths. Also add `MaxDropDownHeight = 300` to all `ComboBox` controls in such grids.

- **UpdateLogService**: `IUpdateLogService` singleton. Persists to `%LocalAppData%\RHI\update_log.json`. Dedup: skips if most recent entry for same component already has same `NewVersion`. Categories: "ReShade", "Addon", "Shader Pack", "RenoDX DLSS5", "DLSS Tool", "OptiScaler", "OptiScaler Nightly", "OptiPatcher", "DLSS", "Streamline", "dgVoodoo2", "Component", "RenoDX". RenoDX installs captured via `_installer.InstallCompleted` event in `MainViewModel` constructor.

- **OptiScaler uninstall step 3b**: The `deployedFolderNames` loop now skips `"plugins"` to avoid deleting CET/RED4ext. Step 4b handles `OptiPatcher.asi` cleanup independently.

- **Archive watch-folder**: Archives (zip/7z) from the watch folder are never auto-deleted — `DeleteFromWatchFolder` was removed from `HandleArchiveFile`. Only `.addon` files auto-delete after successful install.
