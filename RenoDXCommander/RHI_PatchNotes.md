
## v2.6.1

### Changes

- Neural Rendering auto-select now defaults to ShortFuse (was DLSS5 Tool) for DX12 games with native DLSS.
- ReShade Settings cog: Overlay Key and Screenshot Key fields are now side by side.

### Bug Fixes

- Fixed UI hang when rapidly clicking through the game list — all synchronous NVAPI driver profile reads (DLSS presets, driver overrides, VSync/ReBAR/Smooth Motion) are now fetched off the UI thread.
- Fixed UI hang when rapidly changing DLSS/Streamline version dropdowns.
- Fixed toggling the DLL naming overrides switch hanging the UI.
- Fixed games using the D3D12 Agility SDK (e.g. Onimusha: Way of the Sword) being detected as DX11.
- Fixed Neural Rendering method incorrectly defaulting to DLSS5 Tool for games that have a backed-up NR DLL but nothing actively installed.
- Fixed DLSS5 Feeder deploying a 0-byte nvngx_dlss.dll when the cached file was unavailable.

### Manifest Updates

- Onimusha: Way of the Sword and PRAGMATA forced to DX12 detection.
- RoboCop: Rogue City — Unfinished Business Engine.ini config path corrected.

---

## v2.6.0

### New

- Detail view sections (Components, Game Overrides, Neural Rendering, Nvidia Profile Overrides, Management) are now collapsible. Click the section heading to toggle it open or closed. Collapsed state persists across restarts.
- Detail view sections can be reordered by dragging the ≡ handle on the left of each section header. Order persists across restarts.
- New Extras section in detail view. Contains Ultimate ASI Loader — install the UAL proxy DLL into any game folder to enable .asi plugin loading. Choose from the full list of supported DLL names (bitness-filtered, with Recommended badges and conflict warnings). Keeps itself up to date automatically. Hooked chaining handled automatically when the chosen DLL name is already in use by a game file.
- ShortFuse DLSS Tool now auto-configures ReShade for FrameGen on install: renames ReShade to Reshade64.asi, installs ASI Loader automatically (winmm → version → dinput8 priority), and writes HookStreamline=1 and HookDirectX=1 to reshade.ini. Controlled via the ⚙ cog next to the Neural Rendering install button — enabled by default, can be turned off per-game.

### Changes

- RenoDX renamed to RenoDX HDR in the detail view component list.
- Version number now shown next to MFG Ada Unlock, DLSS5 Feeder, and DX11 Bridge in the addon panel (same as DLSS5 Tool).
- ASI Loader status now appears in the ShortFuse Neural Rendering status line alongside ReShade and DLSS versions.

### Bug Fixes

- Fixed ShortFuse DLSS Tool addon (renodx-dlss.addon64) being removed as stale on every launch and Refresh for games where it was installed via the Neural Rendering section.
- Fixed Nvidia Profile Overrides section showing stale DLSS versions after removing a Neural Rendering method — now refreshes immediately without needing a manual Refresh.

### Manifest Updates

- Baldur's Gate 3 forced to 64-bit detection.
- Hogwarts Legacy linked to Marat's UE-Extended addon.

---

## v2.5.9

### Bug Fixes

- Fixed DLSS5 Feeder refresh wiping lumenite shader files — `SyncGameFolder` was deleting all managed shaders then only redeploying DLSS5_Feed.fx, losing lumenite_Kernel.fx. Fixed by persisting pack-level exclusions via SetExcludedFiles so refresh correctly deploys only the two needed files.
- Fixed MFG Ada Unlock, DLSS5 Feeder, and DX11 Bridge missing from the per-game addon picker.

---

## v2.5.8

### Changes

- Clicking the installed version number on a UE-Extended game now opens Marat's commit history for the UE-Extended addon.

### Bug Fixes

- Fixed OptiPatcher not deploying for NVIDIA users — it was incorrectly gated to AMD/Intel only.
- Fixed MFG Ada Unlock, DLSS5 Feeder, and DX11 Bridge disappearing from the global addon manager after being switched to API-based auto-updating.
- Fixed MFG Ada Unlock, DLSS5 Feeder, and DX11 Bridge not auto-updating — these addons use dynamic release filenames so RHI now resolves the download URL from the GitHub releases API rather than a hardcoded URL.
- Fixed addon update check running before the manifest was applied, causing manifest-driven addons to be silently skipped on every startup.
- Fixed normal Refresh not re-checking games previously confirmed as "no DLSS" — newly installed DLSS (e.g. a game update that adds frame generation) now shows up on a standard Refresh instead of requiring a Full Refresh., causing manifest-driven addons to be silently skipped on every startup.

---

## v2.5.7

### Bug Fixes

- Fixed DLSS5 Feeder failing to download for some users — the zip filename changes with each release so the hardcoded URL broke on updates. RHI now resolves the download URL dynamically from the GitHub releases API so Feeder auto-updates correctly going forward.
- Fixed DOF Fix install failing — the releases API was returning only the first 30 results by default, pushing DOF Fix releases off the page as the repo grew. Now uses per_page=100.

---

## v2.5.6

### Bug Fixes

- Fixed `renodx-dlss5.addon64` deployed by the Neural Rendering section being removed on the next Refresh — the addon cleanup pass was treating it as stale since it wasn't deployed through the standard addon system.

---

## v2.5.5


### New

- **Neural Rendering section** — a dedicated self-contained section in the game detail panel (between Game Overrides and NVIDIA Profile Overrides) for installing DLSS 5 Neural Rendering. No addon picker required. Method combo with four options:
  - **DLSS5 Tool** — for native DLSS games. Deploys `renodx-dlss5.addon64`, upgrades DLSS SR/RR/FG to latest, and deploys `nvngx_dlssnr.dll`.
  - **DLSS5 Tool + DX11 Bridge** — for DX11/Vulkan native-DLSS games. Same as above plus `dlss5-bridge.addon64` (always downloads latest).
  - **DLSS Tool (ShortFuse)** — alternative for any 64-bit native-DLSS game. Deploys the full DLSS SR/RR/FG/NR stack and Streamline via the sentinel pattern.
  - **DLSS5 Feeder** — default for games with no native DLSS (DX11, DX12, Vulkan, OpenGL, 32-bit). Deploys the Feeder addon, DLSS5 Tool as neural consumer, `nvngx_dlss.dll`, `nvngx_dlssnr.dll`, and the required shaders (`DLSS5_Feed.fx` + LumeniteFX motion vectors) automatically. Writes a `ReShadePreset.ini` with both techniques pre-enabled in the correct render order.
  - ReShade is installed automatically if not already present.
  - NR DLL version picker, per-file status indicators with versions, Install/Reinstall/Remove buttons, automatic method detection for existing installs, and descriptions with links for each method.

### Manifest Updates

- Added a note to Ori and the Blind Forest: Definitive Edition warning that the generic Unity mod may have visual issues and the named mod is deprecated.
- Added install path override for The Witcher 3: Wild Hunt - Complete Edition (`bin\x64_dx12`), engine hint (REDengine), and graphics API override (DX12).
- Fixed Outlast detecting as 32-bit and resolving to the wrong path — now forced 64-bit with `Binaries\Win64` path override and engine hint set to Unreal (Legacy).
- Fixed DLSS5 DX11 Bridge download URL — old repo was deleted; updated to `NIGos/dlss5-bridge` with correct filename `dlss5-bridge.addon64`.

---

## v2.5.4

### Changes

- Clicking "Check For Updates" now also triggers a silent auto-install pass immediately after the check completes, so any updates found are installed without needing a separate "Update All" click (when Automatic Updates is enabled).
- Renamed "Export Profiles" / "Import Profiles" buttons in Settings to "Backup Profiles" / "Restore Profiles" for clarity.
- ReBAR Enable now has three options: Auto (Default), Off, and On — reflecting the new driver setting (0x000BFA21). Previously only Off and On were available. Both the global Settings page and per-game overrides panel are updated.

### Bug Fixes

- Fixed `nvngx_dlssnr.dll` not being removed from the game folder when uninstalling DLSS5 Tool. RHI now uses a sentinel file to track whether it placed the DLL, so it only removes what it deployed.
- Fixed Automatic Updates setting reverting to Yes on restart when set to No.
- Fixed addon downloads aborting entirely when one URL (e.g. the 32-bit variant) returns a 404 — remaining URLs now continue independently.
- Fixed per-game addon selection being lost when switching the addon mode to Global and back.
- Fixed pre-selected addons not re-downloading on launch if their staging files were missing.

---

## v2.5.3

### Bug Fixes

- Fixed `RenoDX DLSS5.addon64` still being deployed to game folders after v2.5.2. Per-game addon selections stored in `settings.json` still referenced the old name (`RenoDX DLSS5`) — these are now migrated to `DLSS5 Tool` on load. This is separate from the global addon list and stale file fixes in v2.5.2.

---

## v2.5.2

### Bug Fixes

- Fixed `RenoDX DLSS5.addon64` being deployed to game folders on every launch due to a stale file left over from renaming the addon to DLSS5 Tool. RHI now removes it automatically on startup and cleans it up from all affected game folders, including per-game addon selections that still referenced the old name.
- Fixed DLSS5 Tool and DLSS Tool (ShortFuse) being deployed as `.addon32` on 32-bit games, causing a ReShade load error. Both addons now always deploy as `.addon64`.

---

## v2.5.1

### Bug Fixes

- Fixed DLSS5 Tool addon not deploying to game folders after being selected. The internal package name change from "RenoDX DLSS5" to "DLSS5 Tool" was not reflected in all deploy paths.
- Fixed stale `RenoDX DLSS5.addon64` file from the pre-rename version being re-deployed to games on every startup. RHI now removes it automatically on launch.
- Fixed co-deployed DLSS and Streamline files not being cleaned up when switching away from DLSS Tool (ShortFuse). Files RHI placed are now fully restored or removed on deselect.
- Fixed mutual exclusivity between DLSS5 Tool and DLSS Tool (ShortFuse) — selecting one now greys out the other in the addon picker.

---

## v2.5.0

### New

- Added a search bar to the shader pack picker — filter by pack name or individual shader filename.
- **DLSS Tool (ShortFuse)** — ShortFuse's DLSS5 addon is now in the addon picker as a second option alongside DLSS5 Tool. Supports DX12, DX11 and DX9 with HDR scaling. On install, RHI automatically downloads and deploys the newest DLSS SR, RR, FG, NR and Streamline files to the game folder. Supports RTX 20-50 Series. Still WIP — fall back to DLSS5 Tool if you have issues.
- **Updated nvngx_dlssnr.dll** to ShortFuse's latest build, now supporting RTX 20, 30, 40 and 50 Series GPUs with identical performance to the original NVIDIA build on RTX 50 Series.

### Changes

- Moved the Neural Rendering column to the far right of the Nvidia Profile section, after Streamline.
- Renamed RenoDX DLSS5 addon to DLSS5 Tool. The current version is now shown next to the name in the addon picker.

---

## v2.4.9

### New

- **nvngx_dlssnr.dll 310.8.SF** — a modified Neural Rendering DLL by ShortFuse that extends support to RTX 20, 30, 40 and 50 Series GPUs. This is now the default version RHI deploys. Shown as `310.8.1` in Windows Explorer, `310.8.SF` in RHI.

### Changes

- The Neural Rendering Deploy DLL button now also deploys `nvngx_dlss.dll` to the game folder alongside `nvngx_dlssnr.dll`. Any existing `nvngx_dlss.dll` is backed up as `.original` first.
- Added an MOTD button to the status bar next to Patch Notes — click it to re-read the current message at any time.

### Manifest Updates

- Added Reshade Motion Estimation by JakobPCoder to the shader pack library — dense real-time optical flow motion estimation.

---

## v2.4.8

### Bug Fixes

- Fixed "How to use" link not appearing in the per-game addon picker.
- Fixed `renodx-dlss5.addon64` triggering an install prompt when double-clicked or drag-dropped. It is managed by RHI internally and should only be installed via the addon picker or placed in the Custom Addons folder.

### Manifest Updates

- Added DLSS5 DX11 Bridge and DLSS5 Feeder to the addon picker — both enable DLSS 5 Neural Rendering in D3D11 games. Additional setup steps are required; the How To Use button on each addon links to the repo for instructions.
- Added DLSS5 Feeder companion shader to the shader pack library.
- Fixed Metal Gear Solid 4 (Master Collection) showing as Unreal Engine — now correctly shows MGS4 Engine.

---

## v2.4.7

### Bug Fixes

- Fixed the Neural Rendering column not showing `nvngx_dlssnr.dll` as installed after deploying it. It now updates immediately without needing a Refresh.
- The Neural Rendering column now clearly shows "Custom" when a custom DLL is active.

---

## v2.4.6

### Bug Fixes

- Fixed RenoDX DLSS5 not auto-updating to games when a new version is released. The addon now deploys the updated file directly from its own staging folder and no longer creates a redundant copy in the addons folder.

### Manifest Updates

- Added CubeLUT3Ddith by aron7awol to the shader pack library — Cube 3D LUT shader with dithering to reduce banding.

---

## v2.4.5

### Bug Fixes

- Fixed RenoDX DLSS5 not deploying to game folders after the addons staging folder was deleted. The addon now deploys directly from its own staging location.

---

## v2.4.4

### New

- **RenoDX DLSS5 addon** — `renodx-dlss5.addon64` is now a first-class addon in the per-game addon picker, listed above RenoDX Upgrade. Enable it per game from the Addons combo → Select. RHI downloads it automatically, keeps it updated silently alongside other components, and deploys `nvngx_dlssnr.dll` to the game folder alongside it if not already present. For 50 Series GPUs only.
