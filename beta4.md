# v1.9.9-beta4 Changes (from beta3)

## New Features

- **Ryubing emulator support** — Drag `Ryujinx.exe` into RHI to add Ryubing as a game. Install RenoDX downloads all 9 Souperman9 Switch game addons in one click (Mario Kart 8, Zelda BotW/TotK, Metroid Dread, Splatoon 2/3, Luigi's Mansion 3, Monster Hunter Generations Ultimate, Xenoblade Chronicles X DE). Addons self-detect which game is running — no swapping needed. Requires RenoVK (custom Vulkan ReShade) in the Custom folder. Update detection checks each addon individually. Uninstall removes all 9.
- **Shader pack list reorganized** — Recommended slimmed to 3 packs (crosire master, PumboAutoHDR, clshortfuse). All others moved to Extra. Packs now sorted alphabetically within each category. Added crosire reshade-shaders (legacy) pack.

## Bug Fixes

- Fixed OptiScaler uninstall potentially deleting the game's DLSS DLLs when no backup existed. Now only removes DLSS files if a .original backup is present (confirming OptiScaler deployed them).
- ReShade uninstall now also removes reshade.ini, ReShade2.ini, ReShadePreset.ini, and reshade.log from the game folder.
- ReLimiter uninstall now also removes relimiter.ini, log files, and CSV files from the game folder.
- Fixed INI merge button not applying [renodx] UE-Extended section when UE-Extended was already installed. Now injects the section on INI deploy if the mod is active.
