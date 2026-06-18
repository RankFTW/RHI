# v2.0.0 Full Test Checklist

## Nvidia Profile Overrides (beta1)

### DLSS / Streamline Row
- [x] Change DLSS SR/RR/FG versions — downloads and swaps correctly
- [x] Select version with `(Default)` — restores original DLL
- [x] Game with no component — shows "None" greyed out
- [x] Game with v1.x component — shows version greyed out, not swappable
- [ ] Select "Custom" — deploys from Custom folder
- [x] Change presets (J/K/L/M, D/E, A/B) — verify in NVPI
- [x] Change render scale — verify in NVPI
- [x] Quick Apply stamps all configured defaults
- [x] Quick Apply skips v1.x components
- [x] Quick Apply skips driver-overridden components
- [x] Restore All reverts DLLs + presets

### Driver Override Detection (beta2)
- [x] "Latest DLL" ON in NVPI — version dropdown greyed, "Driver override active" shown
- [x] Tooltip on the warning text explains how to disable
- [x] Quick Apply respects driver override (skips DLL, still applies presets)
- [x] Quick Apply button stays blue/enabled when overrides are active

### Driver Settings Row (requires admin)
- [x] Row greyed out when NOT running as admin
- [x] Row interactive when running as admin
- [x] VSync Mode/Tear Control — change and verify in NVPI
- [x] Low Latency Off/On/Ultra — verify in NVPI
- [x] ReBAR Enable/Mode/Size — verify in NVPI (needs admin)
- [x] ReBAR Mode/Size greyed when Enable is Off
- [x] Smooth Motion Enable/APIs/Flip Pacing
- [x] APIs + Flip Pacing greyed when Enable is Off
- [x] Power Management Mode
- [x] CPU Scheduling (needs admin)

## Admin Mode (beta1)

- [x] Toggle On — UAC prompt, combo stays "On"
- [x] Cancel UAC — reverts to "Off"
- [x] Restart dialog shown after toggle
- [x] Restart RHI — auto-relaunches elevated (no UAC on startup)
- [x] Does NOT auto-start at Windows logon
- [x] Toggle Off — task removed, next launch non-elevated
- [x] Drag-drop still works when elevated

## DLSS & Streamline Defaults (beta1)

- [x] Configure Defaults dialog — 4 columns, not clipped
- [x] Set defaults, panel rebuilds (Quick Apply goes blue)
- [ ] Quick Apply downloads on-demand if version not cached

## Global Nvidia Settings (beta1)

- [x] Shader Cache Size — change, verify in NVPI
- [x] Shader Pre-Compile — change, verify
- [x] G-Sync Mode — change, verify
- [x] Preferred Refresh Rate — change, verify
- [x] Values persist after reopening Settings page

## Profile Export/Import (beta1)

- [x] Export completes without hanging
- [x] Exported JSON has settings for RHI games
- [ ] Import restores settings after driver update/wipe
- [ ] 0-value settings included in export

## NVIDIA Profile Matching (beta3)

- [x] Games with ™/® (e.g. Returnal™) — correct profile matched
- [x] Games with Launcher.exe — no false match to other games
- [ ] `launchExeOverrides` entry → direct profile match
- [x] Settings apply to correct game

## Version Display (beta2/3)

- [x] Game at original DLL — shows `version (Default)` selected
- [x] Game swapped to managed version — shows that version, original has `(Default)` in list
- [x] Game with unmanaged original (not in our list) — version `(Default)` at top
- [x] v1.x components — show detected version greyed, not "None"
- [x] Truly absent components — show "None"

## Compact View (beta1)

- [x] 3 pages cycle with nav arrows
- [x] Page 0 = Components, Page 1 = Game Overrides, Page 2 = Nvidia Profile + Management
- [x] Window height appropriate (not overflowing)

## Manifest-Driven Features (beta2)

- [x] Shader pack disabled via manifest — gone from picker
- [x] Shader pack added via manifest — appears in picker
- [x] DLSS preset added via manifest — appears in dropdown
- [x] DLSS preset disabled via manifest — removed from dropdown
- [ ] `componentUrls` override — UE-Extended uses manifest URL if set
- [ ] `profileExeExclusions` — excluded exe names not used for matching

## Bug Fixes to Verify

- [ ] Hotkeys: Ctrl and Shift not swapped when applied to games
- [x] Luma/RE Framework update status persists over restart
- [ ] DXVK extraction — no repeated Defender flags (fixed temp path)
- [ ] Streamline "Custom" persists after panel rebuild
- [x] ReBAR Size Limit read — no crash on profiles with binary settings

## Multi Frame Generation (beta3)

- [x] MFG button visible in FG column when game has FG (not v1.x)
- [ ] MFG button greyed when no FG or v1.x FG
- [x] First-time warning shows with driver version info, "Don't show again" persists
- [x] FG Mode Default — boxes 2 and 3 greyed
- [x] FG Mode Fixed — frame count combo enabled (2x-6x), target FPS greyed
- [x] FG Mode Dynamic — dynamic max count enabled, target FPS enabled
- [x] Switching Fixed→Dynamic clears fixed generation factor
- [x] Switching Dynamic→Fixed clears dynamic max count + target FPS
- [x] Settings persist in NVPI after closing dialog
- [x] Restore All resets MFG settings
- [x] Restore All button turns blue when MFG settings are non-default

## Restore Profile Defaults (beta3)

- [x] Button visible in Power/CPU column, aligned with Flip Pacing
- [x] Warning dialog shows game name, Restore/Cancel
- [x] After restore: all NVPI settings back to defaults
- [x] After restore: Smooth Motion and ULL also cleared (raw NVAPI path)
- [x] Panel rebuilds to reflect default values
- [x] Power Mode shows "Optimal Performance" after restore (not "Adaptive")

## Driver Override Detection — Presets (beta3)

- [x] "Use recommended preset" in NVIDIA App → preset combo shows "Driver Override Active"
- [x] Preset combo greyed + 0.4 opacity when overridden
- [x] Version combo shows "Driver Override Active" when Latest DLL active
- [x] Quick Apply skips presets when "Use recommended" is active
- [x] Tooltip on overridden combos explains how to disable

## Bitness Override Auto-Uninstall (beta3)

- [x] Change bitness 64→32 — all components uninstalled (RS, RDX, UL, DC, OS, DXVK, REF, Luma)
- [ ] Change bitness 32→64 — same
- [x] Change to same effective bitness (e.g. Auto=64, pick 64) — no uninstall
- [x] ReLimiter file removed correctly (correct bitness filename used)
- [ ] Vulkan games excluded from auto-uninstall

## Vulkan Admin Prompt (beta3)

- [x] Click Install ReShade on Vulkan game (not admin) — dialog with 3 options
- [x] "Enable Admin Mode" — UAC prompt, task created, app restarts elevated
- [x] "Restart as Admin" — UAC prompt, one-time restart
- [x] "Cancel" — no action taken

## Manifest-Driven Addon Packs (beta3)

- [x] Addon description overridden from manifest (DevKit, DLSS Fix)
- [x] Addon disabled via manifest — removed from addon manager list
- [x] New addon added via manifest — appears in addon manager
- [x] Re-opening addon manager preserves manifest overrides (not wiped by EnsureLatest)

## Wiki Exclusion Fix (beta3)

- [x] Set game to "Excluded" — shows "No RenoDX mod available" (disabled button)
- [x] Not "Download from Discord"
- [x] WikiStatus shows "—" not "💬"

## Driver Version Display (beta3)

- [x] Section header shows "Nvidia Profile Overrides — Driver XXX.XX"
- [ ] No version shown when no NVIDIA GPU
