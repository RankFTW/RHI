# v2.0.0-beta1 Test Checklist

Focus on the new Nvidia Profile Overrides and Admin Mode features. Everything else has been tested internally.

## Driver Settings (requires admin)

- [ ] Verify the driver settings row is greyed out when NOT running as admin
- [ ] With admin: change VSync, Low Latency, ReBAR, Smooth Motion, Power/CPU — do values stick in NVIDIA Profile Inspector?
- [ ] ReBAR Mode/Size greyed out when Enable is Off
- [ ] Smooth Motion APIs/Flip Pacing greyed out when Enable is Off
- [ ] Low Latency Ultra — does ULL actually write? (check NVPI)
- [ ] Switch between games rapidly — do combos update without crashing?

## Admin Mode

- [ ] Toggle On — UAC prompt appears, combo stays on "On" after confirming
- [ ] Cancel UAC — combo reverts to "Off"
- [ ] Restart RHI after toggling On — does it auto-relaunch elevated (no UAC on startup)?
- [ ] Toggle Off — task removed, next launch is non-elevated
- [ ] Drag-drop still works when elevated (addons, Luma archives, exe files)

## Quick Apply & Defaults

- [ ] Configure Defaults — dialog shows 4 columns, not clipped
- [ ] Set some defaults, click Quick Apply on a game — correct versions/presets applied?
- [ ] Quick Apply downloads versions on-demand if not cached?
- [ ] Restore All reverts everything back to Default?

## Profile Export/Import

- [ ] Export doesn't hang (should complete in a few seconds)
- [ ] Set some driver settings, Export, change them in NVPI, Import — do they restore?
- [ ] Settings at 0/default are included in the export (not skipped)

## Compact View

- [ ] 3 pages cycle correctly with nav arrows
- [ ] Page 2 shows Nvidia Profile + Management (not overflowing)

## Edge Cases

- [ ] No NVIDIA GPU — app doesn't crash, Nvidia section handles gracefully
- [ ] Game with no DLSS/Streamline — DLSS row greyed out, driver settings still work
- [ ] Batch Deploy dialog — separator visible on right side, combos not clipped
