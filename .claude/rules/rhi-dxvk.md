---
paths:
  - "**/*Dxvk*"
  - "**/*dxvk*"
---

<!-- Copied from .kiro/steering/rhi-dxvk.md. Keep both in sync while Kiro is still used. -->

# DXVK Knowledge

## Variants & Staging
- 3 variants: Development (nightly.link), Stable (GitHub releases), LiliumHdr (EndlesslyFlowering/dxvk)
- 3 staging folders: `dxvk-development/`, `dxvk-stable/`, `dxvk-lilium/`
- `StagingDir` is an instance property resolving based on `_selectedVariant`
- Temp extraction uses fixed path `%TEMP%\RHI_dxvk_extract` (not random GUID) — prevents Windows Defender re-flagging
- Lilium archive structure: `normal/x64/*.dll` and `normal/x32/*.dll`
- `FormatVersionForDisplay()` trims Lilium tags to just the mod version (e.g. "0.3.3")

## Direct Mode (All Variants, DX8/DX9)
- ALL variants use direct mode: DXVK deployed as `d3d9.dll` directly, ReShade uses Vulkan implicit layer
- Proxy mode (`dxgi_dxvk.dll` + `[PROXY]` section) eliminated for new installs
- DX10/DX11: standard mode, DXVK as `dxgi.dll`, ReShade switches to Vulkan layer
- Legacy `IsProxyMode = true` records still handled on uninstall (cleanup of proxy DLL + `[PROXY]` section)

### Install Flow (DX8/DX9)
1. Remove existing legacy proxy + `[PROXY]` section if present (migration)
2. Remove local ReShade `d3d9.dll` (backed up as `.reshade_backup`)
3. Deploy DXVK as `d3d9.dll`
4. Deploy `dxvk.conf` (Lilium: HDR preset content, Dev/Stable: `DefaultDxvkConfContent`)
5. Ensure Vulkan layer ReShade installed (`VulkanLayerService.InstallLayer()`)
6. Deploy `reshade.ini` (Vulkan variant, NO `[PROXY]` section) + Vulkan footprint
7. Deploy shaders
8. Lilium only: Set NVIDIA profile settings (Present Method + DirectFlip)

### Uninstall Flow (DX9)
1. Delete DXVK `d3d9.dll` + `dxvk.conf`
2. Reinstall ReShade as local `d3d9.dll` (from `.reshade_backup` or staging)
3. Delete reshade.ini entirely, then recreate via `MergeRsIni` (standard DX mode)
4. Remove Vulkan footprint
5. Lilium only: Clear NVIDIA profile settings
6. Re-resolve `GraphicsApi` back to DirectX9

## Persistence on Restart
- Uses `dxvkRec.InstalledDlls.Contains("d3d9.dll")` to force `GraphicsApi = Vulkan` and `VulkanRenderingPath = "Vulkan"` for ALL DX9 DXVK installs
- `IsDxvkToggleVisible` uses `DxvkRecord?.InstalledDlls.Contains("d3d9.dll")` to stay visible
- DxvkRecord IS persisted (`dxvk_installed.json`) — source of truth for Lilium HDR mode across restarts
- `RsStatus` must be derived from `reshade.ini` presence when `RequiresVulkanInstall` is true and no aux RS record exists

## Lilium HDR Conf Tiers
- 6 presets for DX9 (0=safest → 5=experimental), 7 for DX10/DX11 (0=safest → 6=fully experimental)
- DX9 uses `d3d9.*` prefix + `enforceWindowModeInternally = disabled`
- DX10/DX11 uses `d3d11.*` prefix, includes `_SRGB` and `_TYPELESS` render target variants
- RHI deploys Level 0 (safest) by default. Per-game via `LiliumPresetOverrides`
- `DxvkService.LiliumPresetIndex` must be set before `InstallAsync`
- Lilium presets are the COMPLETE dxvk.conf — `DefaultDxvkConfContent` is NOT prepended

## Per-Game Variant Override
- Temporarily switch `_selectedVariant` before install, restore after
- UI: Single ComboBox (Off, Development, Stable, Lilium HDR). "Off" = `HandleDxvkToggleAsync(false)`
- Global DXVK Variant selector removed from Settings page — variant selected per-game directly

## Pitfalls

### card.GraphicsApi = Vulkan breaks API checks
Any code checking `card.GraphicsApi` for DX9 vs DX11 on a Lilium HDR game must use the DLL-based check instead: `DxvkRecord.InstalledDlls.Contains("d3d9.dll")` → DX9, else DX10/DX11.

### BuildCards ordering
Vulkan RS detection runs BEFORE the DXVK block. Fix: second RS Vulkan check immediately AFTER the DXVK block.

### Update fails with "does not support Vulkan"
`UpdateAsync` uses `existingRecord.IsLiliumHdrMode ? GraphicsApiType.DirectX9 : card.GraphicsApi` for staging folder resolution.

### UpdateAsync must rewrite dxvk.conf
If `existingRecord.IsLiliumHdrMode`, rewrite `dxvk.conf` with current preset after updating DLLs.

### Standard DX10/DX11 uninstall leaves Vulkan footprint
Delete `RDXC_VULKAN_FOOTPRINT` unconditionally BEFORE calling `SwitchReShadeForDxvkAsync`.

### Lilium HDR uninstall must re-resolve GraphicsApi
`HandleDxvkToggleAsync` must re-resolve API after uninstall using `_DetectAllApisForCard` + `DetectGraphicsApi`.

### _selectedVariant must be synced on startup
Defaults to `DxvkVariant.Development` but settings.json may have "LiliumHdr". Sync in dxvk staging task before `EnsureStagingAsync` + `IsLoadingSettings` guard.

### EnsureStagingAsync skip guard
Guard must be `if (IsStagingReady && !HasUpdate) return;` — allows re-staging when update detected.

### Update All per-game variant
`UpdateAllDxvkAsync` must call `ResolveDxvkVariant(card.GameName)` per game, switch variant, ensure staging, then update. Same for single-game update button.

### Update check ignores per-game variants
Must discover all distinct variants in use, check each independently, flag only matching cards.

### CheckForUpdateGitHubAsync wrong repo
Uses `LiliumGitHubApi` when `_selectedVariant == LiliumHdr`, not always `StandardGitHubApi`.

## Lilium HDR Required NVIDIA Profile Settings
- "Present Method - (Vulkan/OpenGL)" (`0x20D690F8`) → `0x00000001`
- "Present Method - (OpenGL/DX)" (`0x20324987`) → `0x00080004`
- Set on install, cleared on uninstall

## Lilium HDR Shaders Limitation
Most Lilium HDR shaders don't work on DX9 (require SM4+, DX9 ReShade compiles SM3). Only `lilium__sdr_trc_fix.fx`, `lilium__cas_hdr.fx`, `lilium__filmgrain.fx`, `lilium__rcas_hdr.fx` work. Use PumboAutoHDR for DX9 HDR shaders.
