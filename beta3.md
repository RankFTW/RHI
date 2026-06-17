# v2.0.0-beta3 — Changes from beta2

## Fixes

- Fixed NVIDIA profile matching using generic exe names (Launcher.exe, EpicOnlineServicesInstaller.exe) causing wrong game profiles to be modified (e.g. Returnal matching "Icarus Online").
- Fuzzy title match (strips ™, ®, colons) now runs before exe scan — games with special characters in their name match correctly without relying on folder scanning.
- Fixed Quick Apply swapping DLLs on games with "Driver override active" — now skips SR/RR/FG components where NVIDIA App has Latest DLL enabled.
- Fixed DLSS FG column not being greyed out for games with v1.x Frame Generation DLLs.
- Fixed DLSS/Streamline version dropdown showing a cached version for games that don't have the component — now shows "None".
- Fixed ReBAR Size Limit read crashing on profiles with pre-set binary values (NvAPIWrapper BlockCopy overflow). Now reads via raw NVAPI directly.
- Suppressed noisy FindProfile log spam (fuzzy match + exe scan logged on every call).

## New

- **Manifest-driven profile exe exclusions** — `"profileExeExclusions"` array in manifest adds generic exe names to skip during NVIDIA profile matching.
- **Launch exe overrides used for profile matching** — `"launchExeOverrides"` entries now also inform NVIDIA profile matching, providing a direct exe→profile link without folder scanning.
