# v2.0.0-beta4 — Changes from beta3

## Fixes

- Removed "CPU Scheduling" dropdown from the driver settings row — the setting (`0x105E2A1D`) is actually "Freestyle Modes" (game filter support), not CPU thread scheduling. Misleading control removed until its actual function is confirmed.
- Fixed ReBAR Size Limit not reading/writing correctly (binary/QWORD setting). Now reads via `profile.GetSetting()` and writes via PowerShell helper.
