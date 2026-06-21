# RHI — Still To Do

## UX Fixes

- [x] Wiki unlink via in-app "Excluded" dropdown should show "No RenoDX mod available" — currently shows "Download from Discord"
- [x] Prompt to restart as admin when Vulkan ReShade install fails due to missing privileges — offer to enable Admin Mode or restart elevated
- [X] Scrungus feedback: add "Driver Override" label to version text when driver has "Latest DLL" on but user hasn't done a swap (currently just shows greyed Default version)

## Pre-Release Polish

- [ ] Bump version to 2.0.0 in csproj
- [ ] Final patch notes cleanup (remove beta iteration details before release)
- [ ] Delete beta2.md, beta3.md, beta_test_checklist.md before release
- [ ] Update README.md and README_NexusMods.bbcode with new features
- [ ] Update Inno Setup version number

## Future Enhancements

- [x] DLSS Dynamic Multi Frame Generation (DMFG) control — dropdown under FG in the DLSS section (Generation Factor setting `0x104D6667`)
- [x] DC/ReShade auto-reinstall after bitness override change (currently user must manually uninstall + reinstall)
- [x] Manifest-driven addon pack list (partially remote via Addons.ini, remaining metadata hardcoded)
- [x] Remove 8GB and 16GB from ReBAR size limit options
- [x] Global ReBAR settings in Settings page — move Export/Import buttons side by side, add ReBAR On/Off + Size Limit combos in the freed space (writes to global/base profile). Per-game ReBAR dropdown should show "Global On" or "Global Off" as the default selection (reflecting the global setting) but still allow per-game override to On/Off. Same pattern as ReShade channel (Global = inherit, explicit = per-game override).
- [x] Hide DLSS/Streamline section entirely if game has no DLSS or Streamline files detected (same pattern as DXVK row visibility)
- [ ] Look at allowing Luma install for all games listed on the Luma wiki (not just games with a specific Luma mod archive)
