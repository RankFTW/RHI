# RHI v2.0.0 — New Features Guide

A walkthrough of every major new feature in v2.0.0 and how to use it.

---

## Nvidia Profile Overrides

The biggest addition in v2.0.0. A new section below Game Overrides in the detail panel lets you control NVIDIA driver profile settings per-game — no NVIDIA Profile Inspector needed.

### DLSS / Streamline Row

Visible for any game with DLSS or Streamline DLLs detected.

**Version Swapping:**
1. Select a game with DLSS installed
2. Click the Version dropdown for SR, RR, FG, or Streamline
3. Pick any version — it downloads on-demand if not cached
4. The game's DLL is swapped immediately
5. Select the item marked `(Default)` to restore the original

**Presets:**
1. Open the Preset dropdown (below Version) for SR, RR, or FG
2. Choose a preset (SR: J/K/L/M, RR: D/E, FG: A/B)
3. Applied instantly to the NVIDIA driver profile — no game restart needed

**Render Scale:**
1. Open the Render Scale dropdown for SR or RR
2. Pick a percentage (33%–100%) or a named preset (DLAA, Quality, etc.)
3. Forces the DLSS internal render resolution for that game

**Quick Apply:**
1. Go to Settings → DLSS & Streamline Defaults → Configure Defaults
2. Set your preferred versions, presets, and render scales
3. On any game, click "Quick Apply" — stamps all your defaults in one click

### Driver Settings Row

Controls per-game NVIDIA driver profile settings. Requires Admin Mode (see below).

| Column | What you can change |
|--------|-------------------|
| VSync | Mode, Tear Control, Low Latency (Off/On/Ultra) |
| Smooth Motion | Enable, Allowed APIs, Flip Pacing |
| Power | Power Mode (Adaptive/Optimal/Max Performance) |
| ReBAR | Enable (Off/On/Global), Mode (Standard/Optimized), Size Limit |

All changes are instant — written directly to the game's NVIDIA driver profile.

---

## Admin Mode

Some NVIDIA settings (ReBAR, Low Latency Ultra, Smooth Motion APIs) require admin privileges.

**How to enable:**
1. Go to Settings → Data & Custom Files
2. Set Admin Mode to **On**
3. Accept the one-time UAC prompt
4. RHI will now auto-launch elevated on every startup — no more UAC prompts

**How to disable:**
1. Set Admin Mode back to **Off** — deletes the scheduled task

When not elevated, the Driver Settings row is greyed out with a warning.

---

## Multi Frame Generation (MFG)

Per-game control over NVIDIA's Multi Frame Generation. RTX 50 Series (Blackwell) only.

**How to use:**
1. Select a game with Frame Generation detected
2. Click the "Multi Frame Gen" button below the FG column
3. Choose a mode:
   - **Fixed** — constant frame multiplier (2x–6x)
   - **Dynamic** — adaptive multiplier with a target frame rate
4. For Dynamic mode, set the Target FPS from the VRR cap presets (matched to common monitor refresh rates) or pick any custom FPS value

**Requirements:** RTX 50 Series GPU, driver 572.16+ (Fixed) or 595.97+ (Dynamic).

---

## DLSS & Streamline Defaults + Quick Apply

Configure your preferred settings once, apply to any game instantly.

**Setup:**
1. Go to Settings → DLSS & Streamline Defaults → "Configure Defaults"
2. Set your preferred Version, Preset, and Render Scale for each component
3. Leave anything at "Default (don't change)" to skip that component

**Usage:**
1. Select any game with DLSS/Streamline
2. Click "Quick Apply" in the DLSS/Streamline row
3. All your configured defaults are applied in one click (downloads on-demand)

---

## Global Nvidia Settings

System-wide NVIDIA settings that affect all games. Located in Settings page.

| Setting | What it does |
|---------|-------------|
| Shader Cache Size | How much disk space the driver uses for shader caching |
| Shader Pre-Compile | Background shader compilation aggressiveness |
| G-Sync Mode | Off / Fullscreen only / Fullscreen/Windowed |
| Preferred Refresh Rate | App Setting / Highest available |
| Global ReBAR | On/Off + Size Limit (applies to all games) |
| DLSS On-Screen Indicator | The NVIDIA DLSS text overlay in game corners |

ReBAR settings require Admin Mode.

---

## Profile Export / Import

Back up your per-game NVIDIA profile settings and restore them after driver updates.

**Export:**
1. Go to Settings → Global Nvidia Settings → "Export Profiles"
2. All per-game settings + global settings are saved to `%LocalAppData%\RHI\nvidia_profiles_backup.json`

**Import:**
1. Click "Import Profiles"
2. Missing profiles are created, exe associations registered, and all settings restored

**Reset All:**
1. Click "Reset All Game Profiles to Default" (red outline button)
2. Confirm in the warning dialog
3. ALL per-game AND global NVIDIA settings are wiped to factory defaults

---

## Lilium HDR DXVK

DXVK's HDR fork now uses Vulkan layer ReShade on DX9 games, enabling SM5 HDR shaders that don't work in DX9 mode.

**How to use:**
1. Select a DX8/DX9/DX10 game
2. In the DXVK dropdown (Game Overrides), select "Lilium HDR"
3. DXVK installs with HDR swap chain upgrades enabled
4. A "Lilium HDR Preset" dropdown appears — choose how aggressive the HDR upgrades are:
   - **Safest** — swap chain only (default, ~100% compatible)
   - **2nd Safest** — adds back buffer upgrade
   - **Slightly Unsafe** → **Experimental** — progressively adds render target upgrades

Changing the preset immediately rewrites `dxvk.conf` — no reinstall needed.

**On uninstall:** ReShade is automatically restored as the local DLL.

---

## Batch DLSS & Streamline Deploy

Update DLSS/Streamline versions and presets across multiple games at once.

**How to use:**
1. Go to Settings → "Batch Deploy"
2. Select games from the checklist (all games are selectable — v1.x components are skipped per-component)
3. Pick versions for SR, RR, FG, and Streamline (or "None" to skip)
4. Pick presets for SR, RR, FG
5. Enable "Auto-create NVIDIA profiles" if desired
6. Click "Deploy" — shows progress per game

Games already at the selected version are automatically skipped.

---

## Simple View (formerly Compact)

The default view for fresh installs. A paged layout split across 3 pages:

- **Page 1** — Components (game info, install buttons)
- **Page 2** — Game Overrides (DLL naming, shaders, addons, DXVK, RS channel)
- **Page 3** — Nvidia Profile Overrides + Management

Use the arrow buttons on the sides to navigate between pages.

---

## DLL Naming Override in Luma Mode

Previously locked when Luma was active. Now you can rename the ReShade DLL (`dxgi.dll`) to any custom filename even in Luma mode — useful for games that need a specific DLL name.

---

## Restore Profile Defaults (Per-Game)

In the Driver Settings row of any game's Nvidia Profile Overrides section:

1. Click "Restore Defaults"
2. Confirm the warning
3. That game's NVIDIA profile is wiped to factory defaults (also restores DLSS/SL DLLs)

---

## DLSS Driver Override Detection

When NVIDIA App has "Latest DLL" or "Use recommended preset" enabled:
- Affected dropdowns are greyed out with "⚠ Driver Override Active" text
- Tooltips explain how to disable in NVIDIA App
- Quick Apply skips overridden components

---

## Manifest-Driven Updates

Several features can now be updated server-side without an app release:
- **Shader packs** — add, remove, or change download URLs
- **DLSS presets** — add new presets when NVIDIA releases them
- **Addon packs** — add, disable, or update addon entries
- **Component URLs** — redirect downloads if repos move

These happen automatically on every launch — no user action needed.

---

## Tips

- **Admin Mode is recommended** if you want full control over driver settings (ReBAR, Low Latency Ultra, Smooth Motion)
- **Export your profiles** before driver updates — NVIDIA resets custom profiles on every major driver install
- **Quick Apply** is the fastest way to configure a new game — set your defaults once, stamp everywhere
- **VRR cap values** in the DMFG Target FPS dropdown are calculated for common monitor refresh rates — pick the one matching your display for optimal frame pacing
- **Lilium HDR Preset "Safest"** is recommended unless you know your game benefits from render target upgrades
