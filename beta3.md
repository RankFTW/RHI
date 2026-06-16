# v2.0.0-beta3 — Changes from beta2

## Fixes

- Fixed NVIDIA profile matching using generic exe names (Launcher.exe, EpicOnlineServicesInstaller.exe) causing wrong game profiles to be modified (e.g. Returnal matching "Icarus Online").
- Fuzzy title match (strips ™, ®, colons) now runs before exe scan — games with special characters in their name match correctly without relying on folder scanning.

## New

- **Manifest-driven profile exe exclusions** — `"profileExeExclusions"` array in manifest adds generic exe names to skip during NVIDIA profile matching.
- **Launch exe overrides used for profile matching** — `"launchExeOverrides"` entries now also inform NVIDIA profile matching, providing a direct exe→profile link without folder scanning.
