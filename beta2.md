# v2.0.0-beta2 — Changes from beta1

## New

- **DLSS Driver Override detection** — When NVIDIA App or Profile Inspector has "Latest DLL" enabled for SR, RR, or FG on a game, RHI detects this and greys out the version dropdown with "⚠ Driver override active" and a tooltip explaining how to disable it.
- **Rebrand** — App rebranded from "ReShade HDR Installer" to just "RHI". Window title, about page, installer, and all user-facing text updated.
- **Manifest-driven shader packs** — Shader packs can now be added, disabled, or updated from the remote manifest without an app update. The `"shaderPacks"` field in manifest.json overrides the hardcoded defaults.

## Fixes

- Quick Apply no longer changes DLSS/Streamline versions for games with v1.x DLLs.
- Version dropdown now shows the game's original DLL version with `(Default)` suffix instead of ambiguous "Default" text. Original versions are cached after first scan.
- Smooth Motion "Allowed APIs" and "Flip Pacing" now update their greyed state immediately after toggling Enable (matching VSync/ReBAR behaviour).
- Quick Apply button no longer appears dim after setting defaults — panel rebuilds after the Configure Defaults dialog closes.
- Admin Mode toggle now shows a dialog confirming a restart is needed for it to take effect.
- Admin Mode no longer auto-starts RHI at Windows logon (was using ONLOGON trigger, now uses expired ONCE trigger — task only runs on demand).
- Fixed overlay/screenshot hotkeys having Ctrl and Shift swapped when applied to games. ReShade's INI format is `vk,ctrl,shift,alt` — RHI was incorrectly writing `vk,shift,ctrl,alt`.

## Known Issues

- "DLSS Latest DLL" setting IDs (`0x10E41E01/02/03`) may only work on whitelisted games. Non-whitelisted games won't respond to the override even if set. RHI only detects — does not toggle these settings.
