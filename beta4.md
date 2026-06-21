# v2.0.0-beta4 — Changes from beta3

## New

- **Global ReBAR settings** — New ReBAR On/Off and Size Limit controls in the Global Nvidia Settings section. Writes to the base driver profile (applies to all games). Export/Import buttons now side by side.
- **Per-game ReBAR "Global" option** — When global ReBAR is enabled, per-game dropdowns show "Global (On/Off)", "Global (Standard)", and "Global (1GB)" as the first option. Selecting it inherits from global; selecting On/Off/Optimized/size overrides per-game.
- Removed 8GB and 16GB from ReBAR Size Limit options, added 1.5GB.
- Fresh installs now default to Simple view (previously Detail).
- **Lilium HDR DXVK — Vulkan layer mode** — When installing Lilium HDR DXVK on DX9 games, DXVK is now deployed as `d3d9.dll` directly (not proxy mode). ReShade uses the Vulkan layer instead of a local D3D9 hook, giving access to SM5 HDR shaders. On uninstall, ReShade is automatically restored as the local `d3d9.dll`. Development/Stable variants remain unchanged (proxy mode).

## Fixes

- Fixed DXVK combo "Off" not refreshing the detail panel correctly after Lilium HDR DXVK uninstall — API badge and DXVK combo disappeared until manual Refresh. Now re-resolves Graphics API from manifest/user overrides and updates RS version after uninstall.
- Removed "CPU Scheduling" dropdown from the driver settings row — the setting (`0x105E2A1D`) is actually "Freestyle Modes" (game filter support), not CPU thread scheduling. Misleading control removed.
- Fixed ReBAR Size Limit not reading/writing correctly (binary/QWORD setting). Now reads via `profile.GetSetting()` and writes via PowerShell helper.
- Fixed ReBAR Feature not enabling/disabling on the global base profile — now uses PowerShell helper (same elevation requirement as per-game). Delete-based disable instead of writing 0.
- Global ReBAR combos greyed out with "Admin required" warning when not running elevated.
- ReBAR Size Limit resets to "1GB (Default)" in UI when global ReBAR is turned off.
- Changing global ReBAR settings now auto-refreshes the per-game detail panel.
- When enabling global ReBAR, 1GB default size is written automatically (not left blank).
- Profile Export/Import now includes global settings (Shader Cache, G-Sync, Refresh Rate, ReBAR). Import uses PS helper for ReBAR to ensure elevation works correctly.
- DLSS/Streamline section now hidden entirely for games without DLSS or Streamline files (same pattern as DXVK row). Driver settings row always remains visible.
- DXVK version text is now a clickable link — opens the GitHub releases page for the installed variant (Lilium HDR, Stable, or Development).
- "Compact" view renamed to "Simple" in all user-facing text.
- Export/Import Profiles buttons now use blue accent colour scheme.
