# RHI — Still To Do

## UX Fixes

- [ ] Wiki unlink via in-app "Excluded" dropdown should show "No RenoDX mod available" — currently shows "Download from Discord"
- [ ] Prompt to restart as admin when Vulkan ReShade install fails due to missing privileges — offer to enable Admin Mode or restart elevated
- [ ] Scrungus feedback: add "Driver Override" label to version text when driver has "Latest DLL" on but user hasn't done a swap (currently just shows greyed Default version)

## Pre-Release Polish

- [ ] Bump version to 2.0.0 in csproj
- [ ] Final patch notes cleanup (remove beta iteration details before release)
- [ ] Delete beta2.md, beta3.md, beta_test_checklist.md before release
- [ ] Update README.md and README_NexusMods.bbcode with new features
- [ ] Update Inno Setup version number

## Future Enhancements

- [ ] DLSS Dynamic Multi Frame Generation (DMFG) control — dropdown under FG in the DLSS section (Generation Factor setting `0x104D6667`)
- [ ] DC/ReShade auto-reinstall after bitness override change (currently user must manually uninstall + reinstall)
- [ ] DLSS "Latest DLL" driver override toggle (setting IDs known: `0x10E41E01/02/03`, but whitelisting complicates write support)
- [ ] Custom mouse cursor in app window (P/Invoke approach — needs .cur file)
- [ ] Manifest-driven addon pack list (partially remote via Addons.ini, remaining metadata hardcoded)
- [ ] Frame rate limiter setting in driver profile (from NVPI: `0x10835002`)
