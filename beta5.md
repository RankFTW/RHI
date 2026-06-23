# v2.0.0-beta5 — Changes from beta4

## New

- **Lilium HDR Preset selector** — per-game dropdown (Safest → Experimental) controls how aggressively Lilium DXVK upgrades render targets for HDR. Default: Safest (swap chain only). Higher tiers add back buffer and render target upgrades. 6 presets for DX9, 7 for DX10/DX11. Shown next to the DXVK variant combo when Lilium HDR is active. Changes re-deploy dxvk.conf immediately.
- **Reset All Game Profiles** — new button in Global Nvidia Settings. Resets ALL per-game NVIDIA profile overrides AND global base profile settings (Shader Cache, G-Sync, Refresh Rate, ReBAR, DLSS Latest DLL, presets, render scales) to factory defaults. Refreshes cards and settings page after.
- **DMFG Target FPS VRR caps** — Dynamic Target FPS dropdown now shows VRR cap values first (59–431 FPS with monitor refresh rate labels), followed by remaining FPS values.
- DLL naming override toggle now available in Luma mode (previously greyed out).
- Removed "Global" option from per-game ReBAR Mode and Size Limit dropdowns — shows effective values directly.
- G-Sync label shortened to "Fullscreen/Windowed" (no spaces).
- Restore Defaults dialog now mentions DLL restoration.
- LEGO Harry Potter Collection split into Years 1-4 and Years 5-7 (manifest).

## Fixes

- Fixed Lilium HDR DXVK Vulkan state not persisting across app restart — game showed as DX9 instead of Vulkan. Now derived from DxvkRecord.IsLiliumHdrMode during card building.
- Fixed DXVK Update failing with "does not support Vulkan" on Lilium HDR games after restart — update now uses original DX9 API for staging resolution.
- ReBAR Mode and Size Limit now correctly grey out when per-game Enable is set to "Off" (previously stayed enabled when global ReBAR was On).
- Fixed Quick Apply not applying FG preset after upgrading from FG v1.x — stale version property was blocking the preset write.
- Batch Deploy no longer greys out games with v1.x DLSS/Streamline. Games are always selectable; v1.x SR and Streamline are skipped per-component during deployment. FG v1.x can be upgraded freely.
