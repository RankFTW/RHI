
## v2.7.0

### New

- **OptiScaler DLSS NR variant** — new third option in the OptiScaler version picker alongside Stable and Nightly. Uses a community fork with DLSS 5 Neural Rendering built in — supporting multi-pass NR, pre/post-upscaler placement, and working scale to control the performance cost. All the same features as Nightly (Streamline, FG settings, presets, etc.) are available. The cog gains a Neural Rendering Settings section to tune everything without touching config files.
- **DLSS5 Feeder on 32-bit DX9 games** — the DLSS5 Feeder method in Neural Rendering now fully supports 32-bit DX9 games (Borderlands 2, The Witcher 2, and much more). RHI sets up the required 64-bit helper process automatically, including all the files it needs to run alongside the 32-bit game.
- **Luma support for DX9 games** — Borderlands 2, Borderlands: The Pre-Sequel, The Witcher 2, Medal of Honor: Airborne, and Vanquish now work with Luma. RHI automatically handles the DX9 compatibility layer (dgVoodoo2) alongside the Luma install — no manual setup required. Still one click.

### Bug Fixes

- Fixed ReBAR Size Limit writing and reading incorrect values — values set in RHI now show correctly in NVPI, and values set in NVPI are correctly read back by RHI.
- Fixed Output Colour Settings applying to the wrong monitor on multi-monitor setups.

---

## v2.6.9

### New

- **RTX 40 MFG Unlock v1.3** — updated to the new standalone format. No longer requires ASI Loader — it's a single DLL that you deploy under any proxy name the game loads (version.dll, dinput8.dll, etc.). RHI shows a name picker on install, and automatically cleans up any old ASI-based install on first launch.
- **20/30 FG Unlock** — new entry in the MFG Unlocks section. Brings DLSS Frame Generation to RTX 20 and 30 series GPUs (D3D12 games only, no ReShade or ASI Loader required). Pick your GPU generation (RTX 30 or RTX 20) in the cog before installing — RHI handles the rest and keeps it updated automatically.
- **Resolution & Colour Control** — automatically switch to a target resolution when a game launches and restore it on exit. Also includes Output Colour Settings to control colour depth and HDR dynamic range per-display, without touching NVIDIA Control Panel.
- **Standalone DLSS Enabler** — new row in the Extras section. Installs DLSS Enabler as a proxy DLL directly into any game folder, independent of OptiScaler. Updates automatically.
- **MFG Ada Unlock** — new row in the Extras section. Unlocks DLSS Multi Frame Generation (3x/4x+) on RTX 40-series GPUs. Requires ReShade. Mutually exclusive with RTX 40 MFG Unlock.

### Changes

- Changing a DLSS preset, render scale, or driver override in the NVIDIA Profile Overrides section no longer flashes or rebuilds the panel — the value is written immediately and the combo stays exactly as you set it.
- Extras section install buttons (ASI Loader, RTX 40 MFG, DLSS Enabler) now show the same blue installed style as the Components section when installed.
- Extras section is now split into groups with separators: ASI Loader at the top, MFG Unlocks (RTX 40 MFG, MFG Ada Unlock, 20/30 FG Unlock), and Other (OptiScaler, DLSS Enabler).
- Neural Rendering: added a note below the NR Cost Scaler toggle when the ShortFuse method is selected, explaining that Cost Scaler is now built into the addon.

### Maintenance

- RHI now tracks every DLL it deploys into game folders using a sentinel file. If the game already had a file at that location, the original is backed up and restored on uninstall. If there was nothing there, a 0-byte marker is written so RHI knows to clean up cleanly. This prevents game-original DLLs from being lost after uninstall, and fixes orphaned leftover files. Covers OptiScaler, DLSS version swaps, RE Framework, and Luma.

### Bug Fixes

- Fixed DLSS Fix writing the wrong Streamline path in `reshade.ini` — it was pointing to the wrong file, which caused DLSS Fix to fail to hook Streamline correctly.
- Fixed DLSS Fix config not being written at all when Streamline files were added to a game after RHI had already scanned it (e.g. from a game update or another component install in the same session).
- Fixed the NR Cost Scaler toggle staying greyed out for an entire session after a fresh RHI install — it downloads in the background at startup but the toggle wasn't updating to reflect it, requiring an install+uninstall workaround to un-grey it.
- Fixed ASI Loader leaving its INI file behind on uninstall (e.g. uninstalling `winmm.dll` now also removes `winmm.ini`).
- Fixed a crash when clicking "Apply Peak Nits to All" or "Apply to All Games" in Settings when a game's folder no longer exists on disk.
- Fixed Admin Mode not being detected on certain system configurations — RHI now uses a more reliable check that correctly handles accounts with full admin rights even when the standard elevation check returns false.
- Fixed the Neural Rendering section showing ReShade as not installed immediately after installing it — you had to navigate away and back to see the correct status.
- Fixed RE Framework showing a stale build number on the card after an update — the version now syncs correctly on the next update check.
- Fixed NVIDIA Profile Overrides showing Default/Off on every launch until a manual Refresh — caused by a startup timing issue where profile reads would fail silently before NVAPI finished initialising.

### Manifest Updates

- Onimusha: Way of the Sword: added ultrawide fix link.
- Red Dead Redemption 2: matched to the RenoDX wiki entry (Vulkan mod).

---

## v2.6.8

### Bug Fixes

- Fixed the NVIDIA Profile Overrides section not applying preset and render scale changes — a stale background scan callback was overwriting the panel after a user change, discarding the new values.
- Fixed the driver settings section (VSync, ReBAR, Smooth Motion, etc.) not appearing after a Refresh.
- Fixed DLSS presets and render scale showing Default/Off instead of the actual driver values — reads now go directly to the live driver state instead of a stale in-memory cache.

### Manifest Updates

- Added DLSS SR, RR, and FG version 310.9.1.
- Added Streamline 2.14.1.

---

## v2.6.7

### Changes

- ReShade uninstall now preserves `reshade.log` in the game folder.
- ReShade config files are now deployed to game folders as `ReShade.ini` instead of `reshade.ini`.
- Neural Rendering DLL selection simplified to 310.8.2 — ShortFuse's modified build with support for all RTX GPUs (20/30/40/50 series).
- Added tooltips to the DLSS SR, RR, FG, and Streamline version dropdowns explaining that selecting a version copies it into the game folder, what Default and Custom do, and how NVIDIA Override works.

### Bug Fixes

- Fixed Custom render scale in DLSS & Streamline Defaults — selecting "Custom" now shows a text box so you can type in a specific percentage (33–100%).
- Fixed an intermittent UI freeze where the window would stay active (moveable, minimisable) but all buttons and controls stopped responding. This could happen when opening cogs, install dialogs, or other popups while a background dialog was already showing. Affects the Luma Settings cog, ASI Loader cog, ShortFuse settings, RTX 40 MFG cog, and install warning prompts.
- Fixed a freeze that could occur when an app update was found while the "Checking for updates…" progress dialog was open — the update dialog would silently block for up to 10 seconds.
- Fixed a brief freeze when RHI updated the taskbar jump list after launching a game or changing the Recent Games setting — the update now runs in the background.
- Fixed the Batch Deploy DLSS dialog occasionally leaving a ghost overlay that blocked all input after finishing quickly.
- Fixed a flicker where the entire Overrides panel disappeared and rebuilt itself when changing a DLSS version, preset, render scale, or driver override — now only the NVIDIA Profile section refreshes.
- Fixed a freeze that could occur when changing a DLSS version and clicking a section header at the same time.
- Fixed the NVIDIA Profile Overrides section briefly going blank when selecting a game or installing OptiScaler — the section now shows immediately using cached values and updates silently in the background.
- Fixed changing the Vulkan ReShade channel (Stable/Nightly/Custom) blocking the UI for up to 10 seconds while copying files to `C:\ProgramData\ReShade\`.
- Fixed the OptiScaler cog potentially rebuilding off the UI thread when switching between Stable and Nightly variants, or after applying a preset — could cause a freeze in certain timing conditions.
- Fixed OptiScaler install and uninstall progress not showing in the Extras section — the progress bar and status message now appear directly below the OptiScaler row where they belong. *(Thanks Sapphire)*
- Fixed installing or removing ASI Loader and RTX 40 MFG Unlock resetting the Extras panel scroll position.
- Fixed the drop helper window appearing as a separate entry in the taskbar and Alt+Tab switcher. *(Thanks Owen)*
- Fixed an intermittent UI freeze when rapidly clicking through games — the NVIDIA Profile section's background scans now run at low priority so they can't block pointer input, and the DLSS/driver rows are rebuilt atomically instead of element-by-element.

### Manifest Updates

- Grand Theft Auto V Enhanced: added a ReShade install warning about the DirectStorage incompatibility that causes "Unable to save configuration" errors, with a link to the DirectStorageFix and a note to use an older ReShade version as an alternative.
- Satisfactory: added Frame Generation setup instructions — the Engine.ini keys needed to enable DLSS FG in Satisfactory are now shown in the ReShade info button.

---

## v2.6.6

### Bug Fixes

- Fixed DLL naming override toggle becoming unresponsive after being toggled — any in-progress drag state is now cleared when the overrides panel rebuilds, and the toggle is always re-enabled via a `finally` block even if file operations throw.
- Fixed Recent Games Off setting not hiding games from the system tray right-click menu — all call sites now pass an empty list when the setting is off.
- Fixed Recent Games Off setting not clearing the taskbar jump list — jump list now uses `ICustomDestinationList` instead of `SHAddToRecentDocs`, giving RHI full control to clear it when the setting is toggled off.
- Fixed reinstalling ASI Loader with a different DLL name leaving the old DLL behind — the previous install is now cleaned up (and any Hooked backup restored) before deploying under the new name.
- Fixed ShortFuse DLSS Tool download failing with "file in use" when both DLSS5 variants download concurrently — each variant now uses its own temp filename.
- Fixed ShortFuse DLSS Tool zip extraction failing when the addon file inside the zip doesn't match the expected exact filename — falls back to any `.addon64` entry in the zip.
- Fixed OptiScaler install/uninstall status messages appearing in the Components section — suppressed there since OptiScaler is in the Extras section.

---

## v2.6.5

### Bug Fixes

- Fixed OptiScaler install and uninstall not refreshing the Extras section — the installed/uninstalled state now updates immediately without needing a manual refresh.
- Fixed DLL naming override toggle requiring a refresh to interact with after enabling or disabling — the overrides panel now rebuilds immediately after the toggle operation completes.
- Fixed RTX 40 MFG Unlock not writing the UAL ini file (`version.ini`, `dinput8.ini`, etc.) on install — without it, UAL didn't know to load `RTX40MFG.asi` and ReShade would stop working. RHI now writes the required `[GlobalSets]` keys to the matching UAL proxy ini file automatically.

---

## v2.6.4

### Changes

- DLSS Super Resolution, Ray Reconstruction, and Frame Generation version combos now include **NVIDIA Override** as a selectable option. Choosing it writes the driver's "Latest DLL" flag to the game's NVIDIA profile, equivalent to enabling "DLSS — Enable DLL Override" in Profile Inspector. Selecting any other version or clicking Restore DLSS/SL clears the override. NVIDIA Override is also available as a default in Configure Defaults (applies via Quick Apply) and as a selectable option in Batch Deploy.
- Added support for per-game custom Engine.ini files, hosted in the rhi-repo `engine-files/` folder and referenced by game name in the manifest. When set, RHI fetches and merges only the keys in that file instead of the standard HDR key set — allowing precise control over what gets written for games where the full set causes issues.

### Bug Fixes

- Fixed NR Cost Scaler and RTX 40 MFG Unlock not detecting new releases mid-session — Check for Update and the 4-hour timer now bypass the ETag cache and always fetch fresh release data.
- Fixed NR Cost Scaler and RTX 40 MFG Unlock not auto-deploying updated files to game folders after a new version is staged.

### Manifest Updates

- Black Myth: Wukong — custom Engine.ini file applied on UE-Extended install/update (writes only `r.HDR.EnableHDROutput=1` instead of the standard key set). Standard HDR and LUT controls in the UE-Extended cog are disabled for this game.

---

## v2.6.3

### New

- RTX 40 MFG Unlock added to the Extras section — enables DLSS Multi Frame Generation multipliers beyond 2x (up to 6x) on RTX 40 Series GPUs for games with Streamline FG support. Requires ASI Loader and ReShade installed first.
- DLSS NR Cost Scaler added to the Neural Rendering section — proxy for nvngx_dlssnr.dll that runs the neural model at reduced resolution (default 75%) for significant GPU savings while preserving native-resolution detail. Toggle on before installing an NR method to deploy it in one click.

### Changes

- OptiScaler moved from the Components section to the Extras section, alongside ASI Loader.
- RenoDX cog Compatibility Settings: Upgrade_ prefix stripped from format labels for readability (e.g. `R10G10B10A2_UNORM` instead of `Upgrade_R10G10B10A2_UNORM`).
- RenoDX cog dialog widened so Compatibility Settings labels no longer truncate.
- Installing a Neural Rendering method now automatically removes conflicting global addons (DLSS5 Tool, DLSS Tool (ShortFuse)) from the global set and cleans up their files immediately.
- Neural Rendering auto-select now defaults to ShortFuse (was DLSS5 Tool) for DX12 games with native DLSS.
- RTX 40 MFG Unlock and MFG Ada Unlock are mutually exclusive — installing one blocks the other.
- DLSS/Streamline version combo now shows `Default (x.x.x)` as a separate top entry rather than marking a version in the list — you can now select any version including the original without triggering a restore.
- Streamline version is now read from `sl.common.dll` instead of `sl.interposer.dll`.
- "ReShade Addons" renamed to "Global Addons" in the Shaders/Addons dropdown.
- Clicking "Select" in the per-game shader picker while already on Select now re-opens the picker (same behaviour as the addon picker).

### Bug Fixes

- Fixed NR Cost Scaler and RTX 40 MFG Unlock services not respecting the session-wide GitHub API rate limit flag — they now go through the shared ETag cache.
- Fixed RenoDX update check HEAD requests hanging the UI for up to 100 seconds when offline or rate-limited — now times out after 10 seconds.
- Fixed `renodx-dlss.addon64` not being removed when switching per-game addons to Off on games that ship with `nvngx_dlssnr.dll` natively (e.g. Cyberpunk 2077) — the stale-removal guard now correctly distinguishes RHI-placed NR DLLs from game-native ones.
- Fixed Neural Rendering method auto-select inferring DLSS5 Tool for games that have a backed-up NR DLL but no active install.

### Known Limitations

- DLSS5 Feeder: 32-bit game support not yet implemented.

### Manifest Updates

- METAL GEAR SOLID 4: Guns of the Patriots - Master Collection Version linked to Luma wiki entry.
- METAL GEAR SOLID 4: Guns of the Patriots — install warning added: switch to DX11 before installing Luma.
- Eternal Strands — UE-Extended compat entry added (HDR keys skipped, LUT only) + INI overrides.

---

## v2.6.2

### Changes

- Switching the Neural Rendering method now automatically removes any components installed by the previous method, giving a clean slate before installing the new one.
- ShortFuse ASI auto-config (ReShade rename + UAL install) is now opt-in rather than enabled by default.
- Detail view is now the default layout. All users are switched to it once on first launch of this version.

### Bug Fixes

- Fixed switching away from DLSS5 Feeder not removing the shaders it deployed (DLSS5_Feed.fx, lumenite_Kernel.fx).

---

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
