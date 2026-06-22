# v2.0.0-beta5 — Changes from beta4

## New

- **Lilium HDR Preset selector** — per-game dropdown (Safest → Experimental) controls how aggressively Lilium DXVK upgrades render targets for HDR. Default: Safest (swap chain only). Higher tiers add back buffer and render target upgrades. Shown next to the DXVK variant combo when Lilium HDR is active. Changes re-deploy dxvk.conf immediately.
- DLL naming override toggle now available in Luma mode (previously greyed out).
- Removed "Global" option from per-game ReBAR Mode and Size Limit dropdowns — shows effective values directly.

## Fixes

- ReBAR Mode and Size Limit now correctly grey out when per-game Enable is set to "Off" (previously stayed enabled when global ReBAR was On).
- Fixed Quick Apply not applying FG preset after upgrading from FG v1.x — stale version property was blocking the preset write.
- Batch Deploy no longer greys out games with v1.x DLSS/Streamline. Games are always selectable; v1.x SR and Streamline are skipped per-component during deployment. FG v1.x can be upgraded freely.
