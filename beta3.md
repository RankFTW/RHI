# v2.0.0-beta3 — Changes from beta2

## New

- **Multi Frame Generation controls** — New "Multi Frame Gen" button in the FG column opens a dialog to configure MFG Mode (Default/Fixed/Dynamic), frame count multiplier (2x-6x), and dynamic target frame rate (60-500 FPS / Max Refresh Rate). Per-game, written to the NVIDIA driver profile. Includes first-time warning about 50 Series GPU requirement with driver version info. Restore All resets MFG settings.
- **Restore Profile Defaults button** — New button in the Power/CPU column resets the game's entire NVIDIA driver profile to factory defaults. Warning dialog confirms before proceeding. Handles both standard settings (via NvAPIWrapper) and newer settings (Smooth Motion, ULL, MFG) via raw NVAPI reset.
- **Bitness override auto-uninstall** — Changing the bitness override (Auto/32-bit/64-bit) now automatically uninstalls all installed components (ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, DXVK, RE Framework, Luma) so the user can reinstall cleanly with the correct bitness.
- **Vulkan install admin prompt** — When Vulkan ReShade layer install fails due to missing privileges, a dialog now offers "Enable Admin Mode" (persistent Task Scheduler elevation) or "Restart as Admin" (one-time UAC restart) instead of only showing a status bar message.
- **Driver version display** — The "Nvidia Profile Overrides" section header now shows the installed NVIDIA driver version (e.g. "Driver 610.47").
- **Manifest-driven profile exe exclusions** — `"profileExeExclusions"` array in manifest adds generic exe names to skip during NVIDIA profile matching.
- **Launch exe overrides used for profile matching** — `"launchExeOverrides"` entries now also inform NVIDIA profile matching, providing a direct exe→profile link without folder scanning.
- **Manifest-driven addon packs** — `"addonPacks"` field in manifest can now add, modify, or disable addon entries without an app update. Overridable fields: name, description, download URLs, repository URL, deploy filename. Same pattern as shader packs.

## Fixes

- DLSS/RR/FG Preset combos now detect NVIDIA's "Use recommended preset" driver override and show "Driver Override Active" (greyed out), matching the existing version override detection.
- Version and Preset dropdowns both display "Driver Override Active" text when the driver is controlling that setting, with consistent 0.4 opacity dimming.
- Quick Apply now skips preset application when the "Use recommended preset" driver override is active.
- Fixed Power Mode showing "Adaptive" instead of "Optimal Performance" after Restore Profile Defaults (0 = setting absent was confused with 0 = Adaptive).
- Fixed NVIDIA profile matching using generic exe names (Launcher.exe, EpicOnlineServicesInstaller.exe) causing wrong game profiles to be modified (e.g. Returnal matching "Icarus Online").
- Fuzzy title match (strips ™, ®, colons) now runs before exe scan — games with special characters in their name match correctly without relying on folder scanning.
- Fixed Quick Apply swapping DLLs on games with "Driver override active" — now skips SR/RR/FG components where NVIDIA App has Latest DLL enabled.
- Fixed DLSS FG column not being greyed out for games with v1.x Frame Generation DLLs.
- Fixed DLSS/Streamline version dropdown showing a cached version for games that don't have the component — now shows "None".
- Fixed "None" incorrectly showing for v1.x components that DO have files — now shows the detected version (e.g. `1.0.3 (Default)`) greyed out.
- Fixed version dropdown falling through to a random managed version when original version cache hasn't backfilled — now falls back to installed version for the `(Default)` label.
- Fixed Quick Apply and Restore All buttons being dimmed by the Streamline column's opacity when SL is v1.x or absent.
- Fixed ReBAR Size Limit read crashing on profiles with pre-set binary values (NvAPIWrapper BlockCopy overflow). Now reads via raw NVAPI directly.
- Suppressed noisy FindProfile log spam (fuzzy match + exe scan logged on every call).
- Fixed wiki exclusion ("Excluded" dropdown) showing "Download from Discord" instead of "No RenoDX mod available". Excluded games are no longer marked as external-only.
