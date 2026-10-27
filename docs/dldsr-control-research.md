# DLDSR Programmatic Control — Technical Research Report

**Date:** October 8, 2026  
**Author:** Kiro (AI Assistant)  
**Purpose:** Investigate feasibility of programmatically enabling/disabling NVIDIA DLDSR from RHI  
**Conclusion:** **FEASIBLE** — Capture/replay approach works, requires SYSTEM-level registry access

---

## Executive Summary

~~NVIDIA's Deep Learning Dynamic Super Resolution (DLDSR) cannot be programmatically controlled through any known public API.~~ **UPDATE: A working solution has been discovered.**

While DLDSR cannot be controlled via the NvAPI DRS profile system or by synthesizing registry values, it **CAN** be controlled using a capture/replay approach:

1. **Capture** the registry state when DLDSR is configured as desired (via NVIDIA Control Panel)
2. **Replay** that captured state by writing the exact bytes back to the registry
3. **Restart** the NVIDIA display adapter to activate the changes

This approach bypasses the driver validation that rejects synthesized values — the driver accepts replayed bytes that it originally wrote.

**Key Requirements:**
- SYSTEM-level access (not just administrator) for registry writes
- GPU adapter restart (~15 seconds, brief display blackout)
- Pre-captured registry states for each DLDSR configuration

A working proof-of-concept exists at `C:\rhi-dldsr\` demonstrating this approach.

---

## Table of Contents

1. [Background](#1-background)
2. [Research Methodology](#2-research-methodology)
3. [Technical Findings](#3-technical-findings)
   - [3.1 NvAPI DRS Profile System](#31-nvapi-drs-profile-system)
   - [3.2 Registry-Based Control](#32-registry-based-control)
   - [3.3 NvAPI Display Control APIs](#33-nvapi-display-control-apis)
   - [3.4 GFE Settings API](#34-gfe-settings-api)
   - [3.5 NVPI Setting ID Search](#35-nvpi-setting-id-search)
4. [Working Solution: Capture/Replay Approach](#4-working-solution-capturereplay-approach)
   - [4.1 How It Works](#41-how-it-works)
   - [4.2 Registry Structure Deep Dive](#42-registry-structure-deep-dive)
   - [4.3 SYSTEM-Level Access Requirement](#43-system-level-access-requirement)
   - [4.4 Proof-of-Concept Implementation](#44-proof-of-concept-implementation)
5. [What IS Possible (Updated)](#5-what-is-possible-updated)
6. [Technical Deep Dive: Registry Structure](#6-technical-deep-dive-registry-structure)
7. [Comparison: DSR vs DLDSR Control](#7-comparison-dsr-vs-dldsr-control)
8. [RHI Integration Assessment](#8-rhi-integration-assessment)
9. [Recommendations](#9-recommendations)
10. [References](#10-references)

---

## 1. Background

### What is DLDSR?

DLDSR (Deep Learning Dynamic Super Resolution) is an NVIDIA technology introduced in January 2022 with driver version 511.23. It uses AI/deep learning to perform supersampling more efficiently than traditional DSR:

- **DSR (Traditional):** Renders at higher resolution (e.g., 4x = 4K for 1080p), then downsamples using Gaussian filtering
- **DLDSR:** Uses a neural network to achieve similar quality at lower render resolution (2.25x DLDSR ≈ 4x DSR quality)

### Current Control Methods

DLDSR can currently only be enabled/disabled through:
1. **NVIDIA Control Panel** → Manage 3D Settings → DSR - Factors
2. **NVIDIA App** → Graphics Settings → DSR/DLDSR toggles

### Why RHI Wants This Feature

RHI already controls numerous NVIDIA driver settings programmatically:
- DLSS presets (SR, RR, FG)
- RTX HDR (Enable, Contrast, Saturation, Peak Brightness, Middle Grey, Debanding)
- VSync modes
- Low Latency modes
- Power Management
- ReBAR size
- Vulkan/OpenGL Present Method
- DLSS DLL versions per-game

Adding DLDSR control would complete the driver settings integration, allowing users to:
- Auto-enable DLDSR when launching specific games
- Set per-game DLDSR factors and smoothness
- Toggle DLDSR as part of launch/exit routines (similar to HDR toggle)

---

## 2. Research Methodology

### Approaches Investigated

1. **NvAPI DRS Documentation Review** — Searched official NVIDIA API docs for DSR/DLDSR setting IDs
2. **NVPI XML Analysis** — Searched NVIDIA Profile Inspector's setting databases for DSR-related IDs
3. **Registry Reverse Engineering** — Analyzed registry paths used by NVIDIA Control Panel
4. **Community Research** — Searched NVIDIA Developer Forums, guru3d, and GitHub for prior work
5. **NVAPI Display Control APIs** — Reviewed custom resolution/display APIs for DSR applicability
6. **GFE Settings API** — Checked if GeForce Experience SDK exposes DSR controls

### Tools and Resources Used

- NVIDIA Profile Inspector (NVPI) Revamped App
- `CustomSettingNames.xml` and `Reference.xml` from NVPI
- NVIDIA Developer Forum posts
- guru3d forums (DSR/DLDSR registry research)
- FRAMED Screenshot Community guides
- Official NVAPI documentation at docs.nvidia.com

---

## 3. Technical Findings

### 3.1 NvAPI DRS Profile System

**Finding: DSR/DLDSR factor enable/disable is NOT in the DRS profile system**

The NvAPI DRS (Driver Settings) system is what RHI uses for per-game driver profile settings. This includes:
- `NvAPI_DRS_GetSetting` / `NvAPI_DRS_SetSetting`
- `NvAPI_DRS_CreateProfile` / `NvAPI_DRS_FindProfileByName`
- Setting IDs like `0x10F9DC83` (DLSS SR Preset), `0x00DD48FB` (RTX HDR Enable), etc.

**DSR-related settings found in the DRS system:**

| Setting ID | Name | Purpose |
|------------|------|---------|
| `0x00ADA000` | DSR Apply Disable | **Per-game disable** — prevents DSR from being used in a specific game (value: 0=allow, 1=block) |
| `0x11112256` | Overlay Indicator | Contains `DRAW_ON_SCREEN_INDICATOR_DLDSR` flag (bit 0x08) — debug indicator only |

**What's NOT in the DRS system:**
- DLDSR factor enable (1.78x, 2.25x)
- DSR factor enable (1.20x through 4.00x)
- DLDSR smoothness setting
- Global DSR/DLDSR on/off toggle

**Evidence from NVIDIA Developer Forum:**
> "Is it possible to set the DSR setting using the NVAPI functions? I haven't found the related setting in the NvApiDriverSettings.h header file."
> — Forum post, no official response with a solution

### 3.2 Registry-Based Control

**Finding: Registry approach works for DSR, but DLDSR factors are driver-locked**

#### Registry Location

DSR and DLDSR settings are stored at:
```
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\services\nvlddmkm\State\DisplayDatabase\{MonitorID}\
```

Where `{MonitorID}` is a display-specific identifier (multiple entries may exist for multi-monitor setups).

#### Registry Values

| Value Name | Type | Purpose |
|------------|------|---------|
| `SmoothScalingMultiplierData` | REG_BINARY | DSR factor definitions (resolution multipliers) |
| `SmoothScalingData` | REG_BINARY | DLDSR smoothness settings |

#### DSR Factor Control (WORKS)

Custom DSR factors CAN be added via registry. The hex format for `SmoothScalingMultiplierData`:

```
Header (12 bytes): 01,00,00,00,22,00,00,00,XX,00,00,00
                                          ^^ factor count

Per-factor entry (12 bytes each):
  WW,WW,00,00,HH,HH,00,00,00,00,00,00
  ^^         ^^
  Width      Height (little-endian, e.g., 0x38C7 = 14535 for 3840x2160 at 1080p base)
```

**Process to add custom DSR factors:**
1. Export registry key
2. Edit hex data using DSR Calculator tool
3. Import modified registry
4. Toggle GPU adapter (DevManView) or reboot

This is how the community achieves custom resolutions like 5K, 8K, or non-standard aspect ratios for DSR.

#### DLDSR Factor Control (DOES NOT WORK)

**Critical finding from NVIDIA Developer Forum (June 2025):**
> "DSR can actually support up to 9x or even more instead of 4x with an external tool however **DLDSR is locked by the driver so the custom resolutions do not work**."

The driver validates DLDSR factors against a hardcoded whitelist:
- **1.78x** (√(1920×1080 × 1.78) ≈ 2560×1440 render)
- **2.25x** (√(1920×1080 × 2.25) ≈ 2880×1620 render)

Any attempt to add custom DLDSR factors via registry is silently ignored by the driver.

#### DLDSR Smoothness Control (PARTIAL)

The smoothness percentage (0-100%) CAN be modified via registry in `SmoothScalingData`:

```
Hex pattern for DLDSR 2.25x:
db,01,00,00,20,00,00,00,01,00,00,00,XX,00,00,00,01,00,00,00,0a,00,00,00,1c,00,00,00,YY,01,00,00
                                    ^^                                              ^^
                                    Smoothness %                                    Checksum

Smoothness values:
  0x00 = 0%
  0x21 = 33%
  0x43 = 67%
  0x64 = 100%
```

**Limitations:**
- Requires SYSTEM-level registry access (admin elevation)
- Requires GPU adapter toggle or system reboot to take effect
- Cannot add/remove DLDSR factors, only modify smoothness of existing ones
- Complex hex manipulation required

### 3.3 NvAPI Display Control APIs

**Finding: Display APIs are for monitor custom resolutions, not DSR virtual resolutions**

The NVAPI Display Control group includes:

| Function | Purpose |
|----------|---------|
| `NvAPI_DISP_TryCustomDisplay` | Test a custom monitor timing/resolution |
| `NvAPI_DISP_SaveCustomDisplay` | Persist custom timing to driver |
| `NvAPI_DISP_DeleteCustomDisplay` | Remove custom timing |
| `NvAPI_DISP_SetDisplayConfig` | Apply display configuration |
| `NvAPI_DISP_EnumCustomDisplay` | List saved custom timings |

These APIs create **actual monitor modes** — physical timings sent to the display. DSR/DLDSR resolutions are **virtual** — the game renders at higher resolution internally, and the driver downsamples before output to the physical display resolution.

**Key distinction:**
- Custom Display APIs: Monitor thinks it's receiving 4K signal
- DSR/DLDSR: Monitor receives native 1080p signal, game renders at 4K internally

The display APIs cannot be used for DSR/DLDSR control.

### 3.4 GFE Settings API

**Finding: GFE Settings API (GSA) does not expose DSR/DLDSR controls**

The GFE Settings API is documented at:
`https://docs.nvidia.com/gameworks/content/gameworkslibrary/coresdk/gsa/index.html`

It provides:
- Game optimal settings queries
- GPU capability detection
- Settings recommendations

It does NOT provide:
- DSR/DLDSR factor control
- Driver profile manipulation
- Display configuration changes

### 3.5 NVPI Setting ID Search

**Finding: Comprehensive search of NVPI databases found no DLDSR enable/disable IDs**

Searched `CustomSettingNames.xml` and `Reference.xml` for patterns:
- `DSR`
- `DLDSR`
- `DL_DSR`
- `Smooth*Scaling`
- `Super*Resolution`

**Results:**

| Setting | ID | Location | Purpose |
|---------|-----|----------|---------|
| OpenGL - SDSR Tuning | Unknown | Reference.xml | SDSR VBO caching control |
| DSR Apply Disable | `0x00ADA000` | Reference.xml | Per-game DSR block |
| DRAW_ON_SCREEN_INDICATOR_DLDSR | Flag `0x08` in `0x11112256` | Reference.xml | Debug overlay indicator |
| MASK_FOR_DRAW_ON_SCREEN_INDICATOR_DLDSR | Flag `0x80000` in `0x11112256` | Reference.xml | Mask for indicator |
| ENABLE_SMOOTHSCALING_SUPPORT_ON_QUADRO | Flag `0x100000` | Reference.xml | Enable DSR on Quadro |
| ENABLE_DL_DSR_SUPPORT_ON_QUADRO | Flag `0x200000` | Reference.xml | Enable DLDSR on Quadro |

**Notable absence:** No setting ID for "DLDSR Factors Enable" or "DSR Factors" global toggle.

---

## 4. Working Solution: Capture/Replay Approach

### 4.1 How It Works

The breakthrough insight is that **you don't need to synthesize registry values** — you capture them when they're in a known-good state and replay them later.

**The Workflow:**

1. **One-Time Capture Phase** (manual setup):
   - User opens NVIDIA Control Panel
   - Configures DLDSR exactly as desired (e.g., "DLDSR 2.25x only, 33% smoothness")
   - Runs capture script which snapshots the entire `DisplayDatabase` registry tree
   - Labels the capture (e.g., `"dldsr-225-only"`)
   - Repeats for other desired states (`"all-off"`, `"dldsr-both"`, etc.)

2. **Runtime Replay Phase** (automated):
   - Application loads the desired capture by label
   - Writes captured registry bytes back to the exact same paths
   - Restarts the NVIDIA display adapter via `pnputil`
   - Monitors report the new DLDSR state after ~15 seconds

**Why This Works:**

The driver validates newly-written DSR/DLDSR values by checking:
- Structure validity (magic numbers, checksums)
- Value plausibility (expected ranges)
- Internal consistency

When you replay bytes that the driver itself originally wrote, all validation passes — the driver can't distinguish between "NVIDIA Control Panel just wrote this" vs "RHI replayed this from a capture."

### 4.2 Registry Structure Deep Dive

**Registry Path:**
```
HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\State\DisplayDatabase\{MonitorId}\
```

**Monitor ID Examples:**
- `CND3DE2_03_07E8_53` — EDID-based monitor identifier
- `MSI3DB2201_05_07E5_2E` — Another EDID-based identifier
- `ADAPTER_10DE_2B85_00000001_00000000` — GPU adapter state
- `CONNECTOR_10DE_2B85_00000001_00000000_1100` — Physical connector

**Key Values for DSR/DLDSR:**

| Value Name | Type | Purpose |
|------------|------|---------|
| `SmoothScalingData` | REG_BINARY | DSR metadata: entry count, smoothness %, factor count, checksum |
| `SmoothScalingMultiplierData` | REG_BINARY | Factor definitions: 10 slots × 28 bytes each |

**`SmoothScalingData` Structure (32 bytes):**
```
Offset  Size  Description
------  ----  -----------
0x00    4     Magic (0x000001db)
0x04    4     Header size (0x00000020 = 32)
0x08    4     Entry count / flags
0x0C    4     Smoothness % (0x00-0x64, or flags like 0x21 = 33%)
0x10    4     Factor count
0x14    4     Constant (0x0000000a)
0x18    4     Slot size (0x0000001c = 28)
0x1C    4     Checksum
```

**`SmoothScalingMultiplierData` Structure (280 bytes = 10 slots × 28 bytes):**
```
Per-slot (28 bytes):
Offset  Size  Description
------  ----  -----------
0x00    4     Magic (0x000002db)
0x04    4     Slot size (0x0000001c = 28)
0x08    4     X multiplier × 10000 (e.g., 15000 = 1.5x, 22500 = 2.25x)
0x0C    4     Y multiplier × 10000 (same as X for square scaling)
0x10    4     Flags (usually 0x00000000)
0x14    4     isDLDSR flag (0x00 = DSR, 0x01 = DLDSR)
0x18    4     Checksum
```

**Factor Encoding Examples:**
- `0x00003a98` = 15000 → 1.5x factor
- `0x00003415` = 13333 → ~1.33x factor (DLDSR 1.78x uses √1.78 ≈ 1.33 per axis)
- `0x00003a98` = 15000 → 1.5x per axis (DLDSR 2.25x uses √2.25 = 1.5 per axis)

**Empty Slot Pattern:**
```
db,02,00,00,1c,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,f9,00,00,00
^^          ^^                                                          ^^
Magic       Size    (all zeros = empty slot)                            Checksum
```

**DLDSR 2.25x Slot Pattern:**
```
db,02,00,00,1c,00,00,00,98,3a,00,00,98,3a,00,00,00,00,00,00,01,00,00,00,9e,02,00,00
^^          ^^          ^^          ^^          ^^          ^^          ^^
Magic       Size        X=15000     Y=15000     Flags=0     isDL=1      Checksum
```

### 4.3 SYSTEM-Level Access Requirement

**Critical Discovery:** Administrator elevation is NOT sufficient. The `nvlddmkm\State\DisplayDatabase` registry hive requires SYSTEM-level access for writes.

**Evidence:**
- Running as Administrator: Registry writes fail silently or throw access denied
- Running as SYSTEM: Registry writes succeed

**Methods to Achieve SYSTEM Access:**

1. **Scheduled Task with SYSTEM User** (recommended):
   ```powershell
   $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-File C:\path\to\script.ps1'
   Register-ScheduledTask -TaskName 'DLDSR-Toggle' -Action $action -User 'SYSTEM' -RunLevel Highest
   Start-ScheduledTask -TaskName 'DLDSR-Toggle'
   ```

2. **PsExec with -s flag**:
   ```cmd
   psexec -s powershell.exe -File C:\path\to\script.ps1
   ```

3. **Windows Service Running as LocalSystem**:
   - RHI could spawn a small helper service for privileged operations

**The proof-of-concept uses the scheduled task approach**, which:
- Doesn't require any third-party tools
- Can be registered once and invoked multiple times
- Cleans up after itself (one-time task pattern)

### 4.4 Proof-of-Concept Implementation

A complete working implementation exists at `C:\rhi-dldsr\` with the following files:

**`DldsrControl.psm1`** — Main PowerShell module (250+ lines):
```powershell
# Key functions:
Get-DldsrState          # Read current DLDSR config from registry
Set-DldsrState -State   # Apply a captured state by label
Restore-DsrBackup       # Revert to backup before changes
Restart-NvidiaAdapter   # Toggle GPU adapter via pnputil

# Internal helpers:
Read-Captures           # Load dsr-captures.json
ConvertFrom-SmoothScalingData       # Parse metadata struct
ConvertFrom-ScalingMultiplierData   # Parse factor slots
Format-EnabledFactors   # Human-readable factor list
```

**`Capture-DsrState.ps1`** — Capture script for creating new states:
```powershell
# Capture current state with label
.\Capture-DsrState.ps1 -Label "dldsr-225-only"

# Compare two captures to see what changed
.\Capture-DsrState.ps1 -Diff
```

**`dsr-captures.json`** — Storage for captured states:
```json
[
  {
    "Label": "all-off",
    "Time": "2026-10-08T06:16:26",
    "Driver": "32.0.16.1714",
    "Monitors": [
      {
        "MonitorId": "CND3DE2_03_07E8_53",
        "Values": {
          "SmoothScalingData": "db,01,00,00,20,00,00,00,...",
          "SmoothScalingMultiplierData": "db,02,00,00,1c,00,00,00,..."
        }
      }
    ]
  },
  {
    "Label": "dldsr-225-only",
    "Time": "2026-10-08T06:17:53",
    "Driver": "32.0.16.1714",
    "Monitors": [...]
  }
]
```

**`apply.ps1`** — Scheduled task wrapper:
```powershell
param([string]$State)
Import-Module C:\rhi-dldsr\DldsrControl.psm1 -Force
Start-Transcript -Path C:\rhi-dldsr\last-run.log -Force | Out-Null
Set-DldsrState -State $State -Verbose
Start-Sleep 15
Get-DldsrState | Format-List | Out-String
Stop-Transcript | Out-Null
```

**Execution Flow:**

```
┌────────────────────────────────────────────────────────────────────────┐
│ 1. RHI calls: Start-ScheduledTask -TaskName 'DLDSR-Toggle'             │
│    with -ArgumentList '-State dldsr-225-only'                          │
└────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ 2. Task runs as SYSTEM user, executes apply.ps1                        │
│    - Loads DldsrControl.psm1                                           │
│    - Calls Set-DldsrState -State 'dldsr-225-only'                      │
└────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ 3. Set-DldsrState:                                                     │
│    a) Loads dsr-captures.json                                          │
│    b) Finds capture with Label = 'dldsr-225-only'                      │
│    c) For each monitor in capture:                                     │
│       - Writes SmoothScalingData bytes to registry                     │
│       - Writes SmoothScalingMultiplierData bytes to registry           │
│    d) Calls Restart-NvidiaAdapter                                      │
└────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ 4. Restart-NvidiaAdapter:                                              │
│    - pnputil /disable-device "PCI\VEN_10DE&..."                        │
│    - Start-Sleep -Seconds 2                                            │
│    - pnputil /enable-device "PCI\VEN_10DE&..."                         │
│    - Wait for monitors to reinitialize (~10-15 seconds)                │
└────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ 5. DLDSR is now active with 2.25x factor                               │
│    - Games see new resolution options                                  │
│    - NVIDIA Control Panel shows updated state                          │
└────────────────────────────────────────────────────────────────────────┘
```

**Verified Working Output:**
```
MonitorId      : CND3DE2_03_07E8_53
EnabledFactors : DLDSR 2.25x
Smoothness     : 33
MatchesCapture : dldsr-225-only

MonitorId      : MSI3DB2201_05_07E5_2E
EnabledFactors : DLDSR 2.25x
Smoothness     : 33
MatchesCapture : dldsr-225-only
```

---

## 5. What IS Possible (Updated)

| Feature | Feasibility | Method | Complexity |
|---------|-------------|--------|------------|
| Disable DSR for specific game | ✅ Yes | DRS setting `0x00ADA000 = 0x01` | Low — same as other profile settings |
| Disable DLDSR for specific game | ✅ Yes | Same as above (blocks both DSR and DLDSR) | Low |
| **Toggle DLDSR factors globally** | ✅ **Yes** | **Capture/replay + GPU restart** | **Medium — requires SYSTEM access** |
| **Change DLDSR smoothness** | ✅ **Yes** | **Part of capture/replay** | **Medium** |
| Add custom DSR factors | ✅ Yes | Registry edit + GPU toggle | High |
| Add custom DLDSR factors | ❌ No | Driver-locked (only 1.78x, 2.25x available) | N/A |
| Enable DLDSR without GPU restart | ❌ No | Driver requires restart to read new values | N/A |

---

## 6. Technical Deep Dive: Registry Structure

### Path Discovery

The DisplayDatabase registry path contains per-monitor display settings. To find your monitor's entry:

```powershell
# Requires admin elevation
$basePath = "HKLM:\SYSTEM\CurrentControlSet\services\nvlddmkm\State\DisplayDatabase"
Get-ChildItem $basePath | ForEach-Object {
    $props = Get-ItemProperty $_.PSPath
    if ($props.SmoothScalingData) {
        Write-Host "Found DSR data at: $($_.PSChildName)"
    }
}
```

### SmoothScalingMultiplierData Structure (DSR Factors)

```
Offset  Size  Description
------  ----  -----------
0x00    4     Magic/Version (0x000001db)
0x04    4     Header size (0x00000018 = 24 bytes)
0x08    4     Factor count
0x0C    varies  Factor entries (12 bytes each)

Factor Entry:
0x00    4     Target width (little-endian)
0x04    4     Target height (little-endian)
0x08    4     Flags (usually 0x00000000)
```

### SmoothScalingData Structure (DLDSR Smoothness)

```
Offset  Size  Description
------  ----  -----------
0x00    4     Magic/Version (0x000001db)
0x04    4     Header size (0x00000020 = 32 bytes)
0x08    4     Entry count
0x0C    4     Smoothness value (0x00-0x64 = 0%-100%)
0x10    4     Factor count (1 = 2.25x only, 2 = 2.25x + 1.78x)
0x14    4     Unknown (0x0000000a)
0x18    4     Unknown (0x0000001c)
0x1C    4     Checksum (derived from smoothness + factor count + 0x23)
```

### Smoothness Calculation

The checksum at offset 0x1C appears to be calculated as:
```
checksum = (smoothness_value) + (factor_indicator) + 0x123
```

Where `factor_indicator`:
- 0x01 for DLDSR 2.25x only
- 0x02 for DLDSR 2.25x + 1.78x

---

## 7. Comparison: DSR vs DLDSR Control

| Aspect | DSR | DLDSR |
|--------|-----|-------|
| Factor control via registry | ✅ Works | ✅ **Works via capture/replay** |
| Custom factors possible | ✅ Up to 9x+ | ❌ Only 1.78x, 2.25x |
| Smoothness control | N/A (Gaussian only) | ✅ **Works via capture/replay** |
| Per-game disable | ✅ `0x00ADA000` | ✅ Same setting |
| GPU toggle required | ✅ For factor changes | ✅ For factor changes |
| Admin required | ✅ For registry | ⚠️ **SYSTEM required** |
| Public API exists | ❌ No | ❌ No |

### Why Capture/Replay Works When Synthesis Doesn't

**Synthesis approach (fails):**
- Calculate checksums, magic numbers, factor encodings
- Write synthetic bytes to registry
- Driver validation rejects non-authentic values

**Capture/replay approach (works):**
- Driver writes authentic bytes when user configures via NVIDIA CP
- Capture those exact bytes
- Replay them later — driver accepts its own output

The driver likely validates using:
1. Structure integrity (magic numbers, sizes)
2. Checksum verification
3. Value range checks
4. **Possibly internal sequence numbers or timestamps** (explains why synthesis fails)

Replayed bytes pass all validation because they're authentic driver output.

---

## 8. RHI Integration Assessment

### Viability: HIGH

The capture/replay approach is suitable for RHI integration. Here's the assessment:

### Pros

1. **Proven Working** — The POC successfully toggles DLDSR on multiple monitors
2. **No Third-Party Dependencies** — Uses only Windows built-in tools (pnputil, Task Scheduler)
3. **Portable State Format** — JSON capture files can be bundled or generated per-user
4. **Multi-Monitor Support** — Captures include all connected monitors
5. **Driver Version Tolerance** — Same driver version that created captures should work

### Cons

1. **SYSTEM Access Requirement** — Requires scheduled task or service elevation
2. **15-Second Display Blackout** — GPU restart causes brief interruption
3. **One-Time User Setup** — Users must create their own captures (or use bundled defaults)
4. **Driver Update Risk** — Registry format may change between driver versions
5. **No Arbitrary Factors** — Limited to NVIDIA's preset 1.78x and 2.25x

### Implementation Options

**Option A: PowerShell Helper (Simplest)**
```
RHI ships with:
- DldsrControl.psm1 (PowerShell module)
- Default captures for common configs
- apply.ps1 (task wrapper)

RHI C# code:
1. Register scheduled task on first use
2. Invoke via Start-ScheduledTask with arguments
3. Poll for completion
```

**Option B: C# Port (Most Integrated)**
```
RHI implements:
- DldsrService.cs with capture/replay logic
- Registry P/Invoke with SYSTEM impersonation
- GPU restart via SetupAPI/CfgMgr32

Benefits:
- No PowerShell dependency
- Tighter error handling
- Native logging integration
```

**Option C: Hybrid (Recommended)**
```
RHI ships with:
- Embedded PowerShell module as resource
- C# wrapper for task management
- UI for capture management

Workflow:
1. User opens DLDSR settings in RHI
2. RHI shows "Capture current state" if no captures exist
3. User configures DLDSR in NVIDIA CP, clicks capture
4. RHI saves capture with user-provided label
5. User selects state from dropdown, clicks Apply
6. RHI invokes scheduled task, shows progress
```

### Required C# Components

1. **`DldsrCaptureService.cs`**
   - Load/save dsr-captures.json
   - Parse SmoothScalingData/MultiplierData structures
   - Validate capture integrity

2. **`DldsrRegistryService.cs`**
   - Read DisplayDatabase registry (admin)
   - Write DisplayDatabase registry (SYSTEM via task)
   - Enumerate monitor subkeys

3. **`GpuRestartService.cs`**
   - Find NVIDIA GPU device instance path
   - Disable/enable via `pnputil` or SetupAPI
   - Wait for monitor reinitialization

4. **`DldsrTaskService.cs`**
   - Register one-time scheduled task
   - Invoke with state parameter
   - Clean up after execution
   - Poll for task completion

### UI Considerations

**Settings Page Addition:**
```
┌──────────────────────────────────────────────────────────────┐
│ DLDSR Control                                    [?] [⚙]    │
├──────────────────────────────────────────────────────────────┤
│ Active State: [dldsr-225-only        ▼] [Apply]             │
│                                                              │
│ Saved States:                                                │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ ○ all-off          No DLDSR/DSR enabled                  │ │
│ │ ● dldsr-225-only   DLDSR 2.25x, 33% smoothness          │ │
│ │ ○ dldsr-both       DLDSR 1.78x + 2.25x, 33% smoothness  │ │
│ └──────────────────────────────────────────────────────────┘ │
│                                                              │
│ [Capture Current State]  [Delete Selected]  [Import...]     │
│                                                              │
│ ⚠ Applying changes will briefly disable displays (~15 sec)  │
└──────────────────────────────────────────────────────────────┘
```

**Per-Game Toggle Option:**
```
Game Overrides panel could include:
- "Enable DLDSR for this game" checkbox
- Dropdown to select which DLDSR state
- On game launch: apply selected state
- On game exit: revert to previous state
```

### Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Registry format changes | Version captures with driver version; warn if mismatch |
| GPU restart fails | Fallback instruction to manually restart adapter |
| Task registration fails | Provide manual PowerShell command as alternative |
| Multi-GPU systems | Enumerate and target correct GPU by PCI ID |
| Capture corruption | Validate structure before applying; keep backups |

---

## 9. Recommendations

### Primary Recommendation

**Implement DLDSR control in RHI using the capture/replay approach.**

The proof-of-concept demonstrates this is viable. Recommended implementation phases:

**Phase 1: PowerShell-Based MVP**
- Ship the PowerShell module embedded in RHI
- Add basic UI in Settings for capture management
- Use scheduled task for SYSTEM elevation
- Target: 1-2 weeks development

**Phase 2: C# Native Port**
- Port capture/replay logic to C#
- Integrate with existing driver settings infrastructure
- Add per-game DLDSR toggle option
- Target: 2-3 weeks development

**Phase 3: Polish**
- Progress indicator during GPU restart
- Auto-detect optimal captures for common monitors
- Export/import capture files for sharing
- Integration with game launch/exit routines

### Alternative: Defer Implementation

If the 15-second display blackout is deemed too disruptive for users, defer this feature and:

1. **Document the POC** — Publish the working solution for power users
2. **Monitor NVIDIA** — Watch for official API support in future drivers
3. **Gather Feedback** — Survey users on whether they'd accept the blackout tradeoff

### Future Monitoring

Regardless of implementation decision:

1. **Watch NVIDIA Driver Releases** — Check if registry format changes
2. **Monitor NVIDIA App Updates** — Official API may eventually appear
3. **Track Community Developments** — Other tools may discover better methods

---

## 10. References

### Official NVIDIA Documentation

1. **NVAPI Reference Documentation**
   - Display Control: https://docs.nvidia.com/nvapi/group__dispcontrol.html
   - DRS (Driver Settings): https://docs.nvidia.com/nvapi/group__drsapi.html

2. **GFE Settings API**
   - https://docs.nvidia.com/gameworks/content/gameworkslibrary/coresdk/gsa/index.html

### Community Resources

3. **guru3d Forums — DLDSR Smoothness Registry**
   - https://forums.guru3d.com/threads/info-dl-dsr-smoothness-registry-key.451533/
   - Documents the hex format for SmoothScalingData

4. **FRAMED Screenshot Community — Custom DSR Guide**
   - https://framedsc.com/GeneralGuides/custom_dsr_resolutions.htm
   - Complete guide for custom DSR factors via registry

5. **NVIDIA Developer Forum — DLDSR Custom Values Request**
   - https://forums.developer.nvidia.com/t/dldsr-and-dsr-more-values-or-custom-values/337535
   - Confirms "DLDSR is locked by the driver"

6. **NVIDIA Developer Forum — DSR via NVAPI**
   - Confirms DSR settings not in NvApiDriverSettings.h

### Tools

7. **NVIDIA Profile Inspector (NVPI)**
   - Source of CustomSettingNames.xml and Reference.xml
   - Contains all known driver setting IDs

8. **pnputil.exe**
   - Windows built-in tool for device management
   - Used for GPU adapter disable/enable cycle

### Proof-of-Concept

9. **Working DLDSR Control Solution**
   - Location: `C:\rhi-dldsr\`
   - Files: `DldsrControl.psm1`, `Capture-DsrState.ps1`, `apply.ps1`, `dsr-captures.json`
   - Demonstrates capture/replay approach with SYSTEM elevation

---

## Appendix A: NVPI XML Search Results

### DSR-Related Entries in Reference.xml

```xml
<!-- DSR Apply Disable -->
<CustomSetting>
    <UserfriendlyName>Experimental - DSR Apply Disable</UserfriendlyName>
    <HexSettingID>0x00ADA000</HexSettingID>
    <Description>Disallow DSR apply</Description>
    <GroupName>05 - Upscaling and Frame Generation</GroupName>
    <OverrideDefault>0x00000000</OverrideDefault>
    <SettingValues>
        <CustomSettingValue>
            <UserfriendlyName>DISABLED</UserfriendlyName>
            <HexValue>0x00000000</HexValue>
        </CustomSettingValue>
        <CustomSettingValue>
            <UserfriendlyName>ENABLED</UserfriendlyName>
            <HexValue>0x00000001</HexValue>
        </CustomSettingValue>
    </SettingValues>
</CustomSetting>
```

---

## Appendix B: Capture File Format

### dsr-captures.json Schema

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "array",
  "items": {
    "type": "object",
    "required": ["Label", "Time", "Driver", "Monitors"],
    "properties": {
      "Label": {
        "type": "string",
        "description": "User-provided identifier for this state"
      },
      "Time": {
        "type": "string",
        "format": "date-time",
        "description": "ISO 8601 timestamp when captured"
      },
      "Driver": {
        "type": "string",
        "description": "NVIDIA driver version (e.g., '32.0.16.1714')"
      },
      "Monitors": {
        "type": "array",
        "items": {
          "type": "object",
          "required": ["MonitorId", "Values"],
          "properties": {
            "MonitorId": {
              "type": "string",
              "description": "Registry subkey name (EDID-based identifier)"
            },
            "Values": {
              "type": "object",
              "description": "Map of value names to hex-encoded byte strings",
              "additionalProperties": {
                "type": "string",
                "pattern": "^([0-9a-f]{2},)*[0-9a-f]{2}$"
              }
            }
          }
        }
      }
    }
  }
}
```

### Example Capture Entry

```json
{
  "Label": "dldsr-225-only",
  "Time": "2026-10-08T06:17:53",
  "Driver": "32.0.16.1714",
  "Monitors": [
    {
      "MonitorId": "CND3DE2_03_07E8_53",
      "Values": {
        "SmoothScalingData": "db,01,00,00,20,00,00,00,01,00,00,00,21,00,00,00,01,00,00,00,0a,00,00,00,1c,00,00,00,45,01,00,00",
        "SmoothScalingMultiplierData": "db,02,00,00,1c,00,00,00,98,3a,00,00,98,3a,00,00,00,00,00,00,01,00,00,00,9e,02,00,00,..."
      }
    }
  ]
}
```

---

## Appendix C: PowerShell Module API Reference

### DldsrControl.psm1 Exported Functions

```powershell
# Get current DLDSR state for all monitors
Get-DldsrState
# Returns: Array of PSCustomObject with MonitorId, EnabledFactors, Smoothness, MatchesCapture

# Apply a previously captured state
Set-DldsrState -State <string>
# Parameters:
#   -State: Label of the capture to apply
# Side effects: Writes registry, restarts GPU adapter

# Restore original state from backup
Restore-DsrBackup
# Side effects: Writes registry from .bak files, restarts GPU adapter

# Restart NVIDIA display adapter
Restart-NvidiaAdapter
# Side effects: ~15 second display blackout
```

### Internal Helper Functions

```powershell
# Parse SmoothScalingData binary
ConvertFrom-SmoothScalingData -Bytes <byte[]>
# Returns: PSCustomObject with Valid, Entries, Smoothness, FactorCount, Note

# Parse SmoothScalingMultiplierData binary
ConvertFrom-ScalingMultiplierData -Bytes <byte[]>
# Returns: PSCustomObject with Valid, Factors (array), Note

# Load captures from JSON file
Read-Captures -Path <string>
# Returns: Array of capture objects

# Format enabled factors for display
Format-EnabledFactors -Factors <array>
# Returns: String like "DLDSR 2.25x, DSR 4x"
```

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-10-08 | Kiro | Initial research report — concluded not feasible |
| 2.0 | 2026-10-08 | Kiro | **Major revision:** Documented working capture/replay solution from `C:\rhi-dldsr\`; changed conclusion to FEASIBLE; added integration assessment |

---

*End of Report*
