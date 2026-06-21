# v2.0.0-beta4 — Changes from beta3

## New

- **Global ReBAR settings** — New ReBAR On/Off and Size Limit controls in the Global Nvidia Settings section. Writes to the base driver profile (applies to all games). Export/Import buttons now side by side.
- **Per-game ReBAR "Global" option** — When global ReBAR is enabled, per-game dropdowns show "Global (On/Off)", "Global (Standard)", and "Global (1GB)" as the first option. Selecting it inherits from global; selecting On/Off/Optimized/size overrides per-game.
- Removed 8GB and 16GB from ReBAR Size Limit options, added 1.5GB.

## Fixes

- Removed "CPU Scheduling" dropdown from the driver settings row — the setting (`0x105E2A1D`) is actually "Freestyle Modes" (game filter support), not CPU thread scheduling. Misleading control removed.
- Fixed ReBAR Size Limit not reading/writing correctly (binary/QWORD setting). Now reads via `profile.GetSetting()` and writes via PowerShell helper.
- Fixed ReBAR Feature not enabling/disabling on the global base profile — now uses PowerShell helper (same elevation requirement as per-game). Delete-based disable instead of writing 0.
- Global ReBAR combos greyed out with "Admin required" warning when not running elevated.
- ReBAR Size Limit resets to "1GB (Default)" in UI when global ReBAR is turned off.
- Changing global ReBAR settings now auto-refreshes the per-game detail panel.
- When enabling global ReBAR, 1GB default size is written automatically (not left blank).
- Profile Export/Import now includes global settings (Shader Cache, G-Sync, Refresh Rate, ReBAR). Import uses PS helper for ReBAR to ensure elevation works correctly.
