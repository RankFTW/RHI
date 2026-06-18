# v2.0.0 Full Test Checklist

## Nvidia Profile Overrides (beta1)

### DLSS / Streamline Row
- [ ] Change DLSS SR/RR/FG versions — downloads and swaps correctly
- [ ] Select version with `(Default)` — restores original DLL
- [ ] Game with no component — shows "None" greyed out
- [ ] Game with v1.x component — shows version greyed out, not swappable
- [ ] Select "Custom" — deploys from Custom folder
- [ ] Change presets (J/K/L/M, D/E, A/B) — verify in NVPI
- [ ] Change render scale — verify in NVPI
- [ ] Quick Apply stamps all configured defaults
- [ ] Quick Apply skips v1.x components
- [ ] Quick Apply skips driver-overridden components
- [ ] Restore All reverts DLLs + presets

### Driver Override Detection (beta2)
- [ ] "Latest DLL" ON in NVPI — version dropdown greyed, "Driver override active" shown
- [ ] Tooltip on the warning text explains how to disable
- [ ] Quick Apply respects driver override (skips DLL, still applies presets)
- [ ] Quick Apply button stays blue/enabled when overrides are active

### Driver Settings Row (requires admin)
- [ ] Row greyed out when NOT running as admin
- [ ] Row interactive when running as admin
- [ ] VSync Mode/Tear Control — change and verify in NVPI
- [ ] Low Latency Off/On/Ultra — verify in NVPI
- [ ] ReBAR Enable/Mode/Size — verify in NVPI (needs admin)
- [ ] ReBAR Mode/Size greyed when Enable is Off
- [ ] Smooth Motion Enable/APIs/Flip Pacing
- [ ] APIs + Flip Pacing greyed when Enable is Off
- [ ] Power Management Mode
- [ ] CPU Scheduling (needs admin)

## Admin Mode (beta1)

- [ ] Toggle On — UAC prompt, combo stays "On"
- [ ] Cancel UAC — reverts to "Off"
- [ ] Restart dialog shown after toggle
- [ ] Restart RHI — auto-relaunches elevated (no UAC on startup)
- [ ] Does NOT auto-start at Windows logon
- [ ] Toggle Off — task removed, next launch non-elevated
- [ ] Drag-drop still works when elevated

## DLSS & Streamline Defaults (beta1)

- [ ] Configure Defaults dialog — 4 columns, not clipped
- [ ] Set defaults, panel rebuilds (Quick Apply goes blue)
- [ ] Quick Apply downloads on-demand if version not cached

## Global Nvidia Settings (beta1)

- [ ] Shader Cache Size — change, verify in NVPI
- [ ] Shader Pre-Compile — change, verify
- [ ] G-Sync Mode — change, verify
- [ ] Preferred Refresh Rate — change, verify
- [ ] Values persist after reopening Settings page

## Profile Export/Import (beta1)

- [ ] Export completes without hanging
- [ ] Exported JSON has settings for RHI games
- [ ] Import restores settings after driver update/wipe
- [ ] 0-value settings included in export

## NVIDIA Profile Matching (beta3)

- [ ] Games with ™/® (e.g. Returnal™) — correct profile matched
- [ ] Games with Launcher.exe — no false match to other games
- [ ] `launchExeOverrides` entry → direct profile match
- [ ] Settings apply to correct game

## Version Display (beta2/3)

- [ ] Game at original DLL — shows `version (Default)` selected
- [ ] Game swapped to managed version — shows that version, original has `(Default)` in list
- [ ] Game with unmanaged original (not in our list) — version `(Default)` at top
- [ ] v1.x components — show detected version greyed, not "None"
- [ ] Truly absent components — show "None"

## Compact View (beta1)

- [ ] 3 pages cycle with nav arrows
- [ ] Page 0 = Components, Page 1 = Game Overrides, Page 2 = Nvidia Profile + Management
- [ ] Window height appropriate (not overflowing)

## Manifest-Driven Features (beta2)

- [ ] Shader pack disabled via manifest — gone from picker
- [ ] Shader pack added via manifest — appears in picker
- [ ] DLSS preset added via manifest — appears in dropdown
- [ ] DLSS preset disabled via manifest — removed from dropdown
- [ ] `componentUrls` override — UE-Extended uses manifest URL if set
- [ ] `profileExeExclusions` — excluded exe names not used for matching

## Bug Fixes to Verify

- [ ] Hotkeys: Ctrl and Shift not swapped when applied to games
- [ ] Luma/RE Framework update status persists over restart
- [ ] DXVK extraction — no repeated Defender flags (fixed temp path)
- [ ] Streamline "Custom" persists after panel rebuild
- [ ] ReBAR Size Limit read — no crash on profiles with binary settings
