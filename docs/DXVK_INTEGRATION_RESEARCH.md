# DXVK Integration Research for RHI

## What is DXVK

DXVK is an open-source translation layer that converts DirectX 8/9/10/11 API calls into Vulkan. Originally built for Linux (Wine/Proton), it also works on Windows. The project is maintained at [github.com/doitsujin/dxvk](https://github.com/doitsujin/dxvk). Current version is **2.7.1** (as of April 2026).

### Supported DirectX Versions
- DirectX 8 (d3d8.dll)
- DirectX 9 (d3d9.dll)
- DirectX 10 (d3d10core.dll)
- DirectX 11 (d3d11.dll, dxgi.dll)

DirectX 12 is NOT supported by DXVK — that's handled by a separate project called vkd3d-proton.

### Requirements
- Vulkan 1.3 compatible GPU and driver
- Windows 10 or newer (Windows 7/8.1 not supported)
- Nvidia, AMD, or Intel GPU with up-to-date drivers (drivers from Windows Update often lack Vulkan support)

---

## Why Users Want DXVK on Windows

1. **ReShade compute shaders** — Older DX8/DX9/DX10 games don't support compute shaders natively. DXVK translates them to Vulkan, which does support compute. This enables advanced ReShade effects (depth-based effects, MXAO, iMMERSE, etc.) that wouldn't work on native DX9. This is the primary reason for the RHI community.

2. **Performance** — Vulkan often has lower CPU overhead than DX9/DX11. Games like FFXIV, God of War, Metaphor: ReFantazio, and Watch Dogs 2 see measurable CPU-bound performance improvements.

3. **Shader stutter reduction** — DXVK compiles shaders asynchronously and caches them via the graphics pipeline library feature (VK_EXT_graphics_pipeline_library). The dxvk-gplasync fork takes this further with additional async compilation, nearly eliminating shader compilation stutter.

4. **HDR support** — DXVK supports HDR output via `dxgi.enableHDR = True` in dxvk.conf. This can enable HDR in DX11 games that don't natively support it.

5. **Latency reduction** — DXVK 2.7+ supports Nvidia Reflex via VK_NV_low_latency2, and has a built-in latency sleep mode (`dxvk.latencySleep = True`) that works on all GPUs.

---

## DXVK Variants

### Standard DXVK (doitsujin/dxvk)
- **Repository**: [github.com/doitsujin/dxvk](https://github.com/doitsujin/dxvk)
- **Latest**: v2.7.1
- **Release format**: `dxvk-X.Y.Z.tar.gz` on GitHub Releases
- **API**: `https://api.github.com/repos/doitsujin/dxvk/releases/latest`
- Since v2.6, includes graphics pipeline library support which provides async-like shader compilation by default. The old `dxvk.enableAsync` environment variable is no longer needed for most games.
- This is the recommended default for RHI — it's the most stable and well-tested.

### dxvk-gplasync (Ph42oN/dxvk-gplasync)
- **Repository**: [gitlab.com/Ph42oN/dxvk-gplasync](https://gitlab.com/Ph42oN/dxvk-gplasync)
- A patch on top of standard DXVK that adds additional async pipeline compilation beyond what the GPL feature provides.
- Primarily useful for games where the standard GPL path still produces noticeable stutter.
- Since DXVK 2.6+ includes GPL by default, the gap between standard and gplasync has narrowed significantly. Standard DXVK is sufficient for most games.
- **Note**: This is a patch, not a standalone release. Pre-built binaries are available from community forks like [Digger1955/dxvk-gplasync-lowlatency](https://github.com/Digger1955/dxvk-gplasync-lowlatency) which adds both async and low-latency features.

### dxvk-hdr (ManOnAJetski/dxvk-hdr)
- **Repository**: [github.com/ManOnAJetski/dxvk-hdr](https://github.com/ManOnAJetski/dxvk-hdr)
- Fork with HDR modifications for DX9/DX10/DX11 games.
- Less actively maintained than standard DXVK.
- Standard DXVK already supports HDR via `dxgi.enableHDR = True`, so this fork's value is limited to edge cases.

### Recommendation for RHI
Default to **standard DXVK** (doitsujin/dxvk). It's the most stable, most actively maintained, and since v2.6 includes the GPL async feature that covers most stutter cases. Could offer a variant selector in Settings for advanced users who want gplasync.

---

## Release Structure

### Zip Contents
```
dxvk-2.7.1/
├── x64/
│   ├── d3d8.dll
│   ├── d3d9.dll
│   ├── d3d10core.dll
│   ├── d3d11.dll
│   └── dxgi.dll
├── x32/
│   ├── d3d8.dll
│   ├── d3d9.dll
│   ├── d3d10core.dll
│   ├── d3d11.dll
│   └── dxgi.dll
└── dxvk.conf (example configuration)
```

### GitHub API
- Releases API: `https://api.github.com/repos/doitsujin/dxvk/releases/latest`
- Direct download: `https://github.com/doitsujin/dxvk/releases/download/v2.7.1/dxvk-2.7.1.tar.gz`

---

## Installation Process (Manual)

Per [Marty's Mods guide](https://guides.martysmods.com/additionalguides/apiwrappers/dxvk):

1. Determine the game's DirectX version (DX8, DX9, DX10, DX11)
2. Determine if the game is 32-bit or 64-bit
3. Copy the matching DLL(s) from the correct architecture folder (x32 or x64) into the game's executable directory
4. Install ReShade as **Vulkan** (not as the game's original DirectX version)

### Which DLLs to Copy

| Game's DX Version | DLL(s) Needed |
|---|---|
| DX8 | d3d8.dll |
| DX9 | d3d9.dll |
| DX10 | d3d10core.dll, dxgi.dll |
| DX11 | d3d11.dll, dxgi.dll |

**Critical**: Use 32-bit DLLs for 32-bit games, 64-bit DLLs for 64-bit games. Windows will not load DLLs of the wrong architecture.

**Critical**: NEVER replace System32/SysWOW64 DLLs with DXVK. Only place them in the game directory.

---

## ReShade Interaction

When DXVK is active, the game presents itself as a Vulkan application to the system. This means:

- ReShade must be installed as **Vulkan** (via the Vulkan implicit layer), not as DX9/DX11
- The standard ReShade DLL proxy approach (dxgi.dll, d3d9.dll) **conflicts** with DXVK because DXVK itself IS those DLLs
- ReShade's Vulkan layer installs globally via the registry (`HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers`), not per-game DLL placement

### How This Maps to RHI

RHI already has a complete Vulkan ReShade install path (`RequiresVulkanInstall`). When DXVK is enabled for a game:

1. If ReShade is currently installed as a DX proxy DLL, uninstall it
2. Reinstall ReShade via the Vulkan layer path (same as native Vulkan games)
3. Deploy `reshade.vulkan.ini` with Vulkan-tuned depth buffer settings
4. Place the `RDXC_VULKAN_FOOTPRINT` marker for managed shader deployment

When DXVK is disabled:

1. Remove the Vulkan footprint and `reshade.vulkan.ini`
2. Reinstall ReShade as a DX proxy DLL (if the user wants)

This is the same flow that already works for native Vulkan games like DOOM: The Dark Ages and Red Dead Redemption 2.

---

## OptiScaler Coexistence

This is the scenario the Discord user (nCore) asked about. Both OptiScaler and DXVK can want to be `dxgi.dll`. There are two approaches:

### Approach 1: OptiScaler Plugins Folder (Recommended)

OptiScaler has a `[Plugins]` section in its INI:

```ini
[Plugins]
; Path that will be searched for same filename plugins (dxgi.dll, winmm.dll, etc.)
; Default is plugins under OptiDllPath ".\OptiScaler\plugins\"
Path=auto
```

OptiScaler automatically loads a DLL with the **same filename** from its plugins subfolder. So if OptiScaler is installed as `dxgi.dll`, it will look for `.\OptiScaler\plugins\dxgi.dll` and load it as a proxy.

**RHI implementation:**
1. OptiScaler is installed as `dxgi.dll` (or whatever proxy name)
2. DXVK's `dxgi.dll` is placed in `<game folder>\OptiScaler\plugins\dxgi.dll`
3. OptiScaler loads first, then chains to DXVK automatically
4. No INI changes needed — the plugins path is the default

This is the cleanest approach because it uses OptiScaler's built-in proxy mechanism with zero configuration.

### Approach 2: ENB-Style Proxy (Alternative)

The `[PROXY]` section pattern that nCore mentioned (`EnableProxyLibrary=1`, `ProxyLibrary=dxgi_dxvk.dll`) is actually an ENBSeries convention, not a native OptiScaler feature. However, OptiScaler's plugins folder achieves the same result more cleanly.

### Approach 3: No OptiScaler (Most Common)

For DX8/DX9 games (the primary DXVK use case), OptiScaler is irrelevant — OptiScaler is for upscaler redirection (DLSS/FSR/XeSS) which only exists in DX11/DX12 games. The overlap is only for DX11 games where a user wants both DXVK (for performance/stutter) and OptiScaler (for upscaler swapping).

### DLL Naming — Can DXVK DLLs Be Renamed?

**No.** DXVK DLLs cannot be arbitrarily renamed. They must match the system DLL names (`d3d9.dll`, `d3d11.dll`, `dxgi.dll`, `d3d10core.dll`, `d3d8.dll`) because Windows loads them by name when the game calls DirectX APIs. The entire mechanism relies on DLL search order — the game directory is checked before System32.

The only exception is the OptiScaler plugins folder approach, where DXVK's `dxgi.dll` keeps its original name but lives in a subfolder. OptiScaler handles the loading.

---

## Configuration (dxvk.conf)

DXVK reads configuration from `dxvk.conf` in the game's executable directory. Key options for RHI users:

### Essential Settings

```ini
# Enable HDR output (requires Windows HDR enabled)
dxgi.enableHDR = True

# Async shader compilation (reduces stutter) — on by default since v2.6
# via VK_EXT_graphics_pipeline_library, but can be explicitly enabled
dxvk.enableGraphicsPipelineLibrary = Auto

# Number of compiler threads (0 = auto, uses all CPU cores)
dxvk.numCompilerThreads = 0
```

### Performance Settings

```ini
# Frame rate limiter (0 = unlimited)
dxgi.maxFrameRate = 0
d3d9.maxFrameRate = 0

# Allow fullscreen exclusive mode (needed for VRR and HDR in some setups)
dxvk.allowFse = False

# Latency reduction (Auto = Reflex when available, True = built-in algorithm)
dxvk.latencySleep = Auto

# Descriptor buffer for better CPU performance (modern GPUs)
dxvk.enableDescriptorBuffer = Auto

# Descriptor heap (alternative to descriptor buffer)
dxvk.enableDescriptorHeap = Auto
```

### Display Settings

```ini
# Override Vsync (0 = off, positive = on with repeat count)
dxgi.syncInterval = -1
d3d9.presentInterval = -1

# Tear-free mode (mailbox present when Vsync off)
dxvk.tearFree = Auto

# Force refresh rate (useful for games that pick lowest rate)
dxgi.forceRefreshRate = 0
d3d9.forceRefreshRate = 0
```

### GPU Spoofing

```ini
# Report Nvidia GPUs as AMD (default for DXGI to avoid Nvidia-specific code paths)
dxgi.hideNvidiaGpu = Auto
d3d9.hideNvidiaGpu = Auto

# Override PCI vendor/device IDs
dxgi.customDeviceId = 0000
dxgi.customVendorId = 0000
```

### D3D9-Specific Settings

```ini
# Shader model (0-3, default 3)
d3d9.shaderModel = 3

# DPI awareness (avoids upscaling blur on Hi-DPI)
d3d9.dpiAware = True

# Max available texture memory in MB
d3d9.maxAvailableMemory = 4096

# Float emulation (True = fast, Strict = accurate, Auto = DXVK picks)
d3d9.floatEmulation = Auto
```

### D3D11-Specific Settings

```ini
# Max feature level (9_1 through 12_1)
d3d11.maxFeatureLevel = 12_1

# Max tessellation factor (8-64, 0 = default)
d3d11.maxTessFactor = 0

# Relaxed pipeline barriers (may improve performance, may cause artifacts)
d3d11.relaxedBarriers = False

# Anisotropic filtering override (0-16, -1 = no override)
d3d11.samplerAnisotropy = -1

# Disable MSAA (forces sample count to 1)
d3d11.disableMsaa = False
```

### Debugging / HUD

```ini
# DXVK HUD elements (same syntax as DXVK_HUD environment variable)
# Examples: "fps", "devinfo", "frametimes", "submissions", "compiler"
dxvk.hud =
```

---

## Known Issues on Windows

Source: [DXVK Windows Wiki](https://github.com/doitsujin/dxvk/wiki/Windows)

### DLL Loading
Some games load DLLs from System32 instead of the game directory. DXVK won't work for these games without registry modifications (DevOverride), which is risky and not something RHI should automate.

### Anti-Cheat
Games with anti-cheat (EAC, BattlEye, custom) will often refuse to run with DXVK DLLs or may ban players. **DXVK should never be installed on games with active anti-cheat.** RHI should block the toggle for known anti-cheat games via the manifest.

### Third-Party Software Conflicts
The following may conflict with DXVK:
- Steam/Epic/Uplay overlays
- Nvidia GeForce Experience overlay
- RTSS (RivaTuner Statistics Server) — implemented as a Vulkan layer, loads even when app isn't running
- OBS recording
- Bandicam
- Game mods like FiveM

These may work but can cause crashes. Borderless fullscreen is recommended over exclusive fullscreen.

### AMD Windows Driver
DXVK developers state: "We generally do not test the Windows drivers with DXVK" for AMD and Intel. AMD Polaris and Vega GPUs have effectively discontinued Vulkan 1.3 driver support. Nvidia is the most reliable because the Vulkan driver is shared between Windows and Linux.

### Fullscreen Issues
- Variable refresh rate may not work without `dxvk.allowFse = True`
- HDR may not work properly without FSE
- Performance may be degraded on multi-monitor systems
- Frame latency may be higher
- Borderless fullscreen is recommended

### Vendor-Specific Libraries
Games that tightly integrate AMDAGS or NVAPI may crash with DXVK because these libraries try to interact with the native D3D11 driver directly.

### Microsoft Dozen
The "OpenCL, OpenGL & Vulkan Compatibility Pack" from the Microsoft Store installs a Vulkan-to-D3D12 translation layer (Dozen) that cannot run DXVK. If installed on a system with a real Vulkan driver, it can cause issues. Users may need to uninstall it.

---

## Binary Signature Detection

RHI's foreign DLL protection needs to identify DXVK DLLs. DXVK DLLs contain identifiable strings that can be used for binary signature scanning:

- `"dxvk"` — appears in version strings and internal identifiers
- `"DXVK_"` — environment variable prefixes compiled into the binary
- `"doitsujin"` — author identifier in some builds

The scan should check the first ~2 MB of the DLL (same approach as ReShade and OptiScaler detection). DXVK DLLs are typically 1-5 MB depending on the API they implement.

---

## Proposed Implementation for RHI

### User-Facing Design

- Per-game toggle in the Overrides panel: **"Enable DXVK"**
- When toggled on, RHI handles everything: downloads DXVK, deploys correct DLLs, switches ReShade to Vulkan mode, optionally deploys dxvk.conf
- When toggled off, RHI removes DXVK DLLs, restores originals, switches ReShade back to DX mode
- Warning dialog on first enable explaining risks (unofficial Windows support, anti-cheat, overlay conflicts)
- Toggle disabled/hidden for DX12-only games (DXVK doesn't support DX12)
- Toggle blocked for games on the anti-cheat blacklist

### Technical Flow — Enable DXVK

1. **Download** — fetch DXVK release from GitHub, extract to `%LocalAppData%\RHI\dxvk\`, track version
2. **Determine DLLs** — use RHI's existing graphics API detection + bitness to select the right DLLs:
   - DX8 game, 32-bit → `x32/d3d8.dll`
   - DX9 game, 64-bit → `x64/d3d9.dll`
   - DX10 game → `d3d10core.dll` + `dxgi.dll` (matching bitness)
   - DX11 game → `d3d11.dll` + `dxgi.dll` (matching bitness)
3. **Back up originals** — same `.original` pattern as OptiScaler. Only back up genuine game files, not files already placed by other tools.
4. **Deploy DLLs** — copy DXVK DLLs to game directory
5. **Handle OptiScaler coexistence** — if OptiScaler is installed and both need `dxgi.dll`:
   - Move DXVK's `dxgi.dll` to `<game folder>\OptiScaler\plugins\dxgi.dll`
   - OptiScaler's built-in plugin loading handles the rest
6. **Deploy dxvk.conf** — optionally copy a default config with sensible defaults
7. **Switch ReShade** — if ReShade is installed as DX proxy, uninstall it and reinstall via Vulkan layer
8. **Save tracking record** — persist DXVK state for detection, update, and uninstall

### Technical Flow — Disable DXVK

1. **Remove DXVK DLLs** — delete deployed DLLs from game directory
2. **Remove from OptiScaler plugins** — if DXVK was in the plugins folder, remove it
3. **Restore originals** — restore `.original` backups
4. **Remove dxvk.conf** — if RHI deployed it (check tracking record)
5. **Switch ReShade back** — if ReShade is installed via Vulkan layer, uninstall and reinstall as DX proxy
6. **Remove tracking record**

### Data Model

```csharp
public class DxvkInstalledRecord
{
    public string GameName { get; set; }
    public string InstallPath { get; set; }
    public string DxvkVersion { get; set; }
    public List<string> InstalledDlls { get; set; }      // e.g. ["d3d9.dll"] or ["d3d11.dll", "dxgi.dll"]
    public List<string> BackedUpFiles { get; set; }       // originals that were replaced
    public bool DeployedConf { get; set; }                // whether RHI placed dxvk.conf
    public bool InOptiScalerPlugins { get; set; }         // whether dxgi.dll is in OptiScaler/plugins/
    public DateTime InstalledAt { get; set; }
}
```

### Staging

| Item | Details |
|------|---------|
| Download URL | `https://api.github.com/repos/doitsujin/dxvk/releases/latest` |
| Cache directory | `%LocalAppData%\RHI\dxvk\` |
| Version tracking | `%LocalAppData%\RHI\dxvk\version.txt` (same pattern as OptiScaler) |
| Update detection | Compare cached version tag against latest GitHub release tag |
| Archive format | `.tar.gz` — RHI already has 7-Zip bundled for extraction |

### Safety Guards

- **Anti-cheat blacklist** — manifest list of games where DXVK should be blocked (same pattern as existing blacklist)
- **DX12-only guard** — disable toggle for games detected as DX12-only (DXVK doesn't translate DX12)
- **First-time warning** — dialog explaining unofficial Windows support, anti-cheat risks, overlay conflicts
- **Never touch System32/SysWOW64** — only deploy to game directories
- **Always back up originals** — same `.original` pattern as OptiScaler
- **Validate DLL architecture** — ensure 32-bit DLLs go to 32-bit games and vice versa
- **Foreign DLL protection** — add DXVK binary signatures to the detection system so existing DXVK installs are recognised and not flagged as unknown

### UI Integration

| Element | Location | Details |
|---------|----------|---------|
| DXVK toggle | Overrides panel | Per-game toggle, disabled for DX12-only and anti-cheat games |
| DXVK status | Component section | Row showing Installed/Ready/Not Available, with version number |
| DXVK Info button | Component row | Shows what DXVK does, game-specific notes, known issues |
| DXVK conf deploy | Component row | 📋 button to copy dxvk.conf to game folder |
| DXVK variant selector | Settings page | Standard DXVK vs gplasync (advanced users) |
| DXVK update exclusion | Overrides panel | Per-game toggle to pin a specific version |

### What RHI Already Has

| Existing Feature | How It Helps DXVK |
|-----------------|-------------------|
| Graphics API detection | Already knows DX8/9/10/11/12 per game |
| Bitness detection | Already knows 32-bit vs 64-bit |
| `.original` backup pattern | OptiScaler already does this |
| Vulkan ReShade path | Already implemented and working |
| Binary signature scanning | Can detect DXVK DLLs (foreign DLL protection) |
| Per-game overrides panel | UI pattern for toggles already exists |
| Staging/download/version tracking | Same pattern as OptiScaler, ReShade, etc. |
| 7-Zip bundled | Can extract `.tar.gz` archives |
| OptiScaler plugins folder awareness | Already manages OptiScaler companion files |
| Manifest-based blacklists | Can block DXVK for anti-cheat games |

---

## Games That Benefit Most from DXVK

### DX9 Games (biggest benefit — enables compute shaders for ReShade)
The Sims 3, Fallout: New Vegas, Skyrim (original), GTA IV, Dark Souls 1, Bayonetta, older Assassin's Creed titles, Oblivion, Max Payne 3, Far Cry 2, Tomb Raider (2013), Sonic Generations

### DX8 Games
Older titles that can benefit from Vulkan translation for stability and ReShade compatibility.

### DX11 Games (performance/stutter improvement)
FFXIV, God of War, Metaphor: ReFantazio, Watch Dogs 2, Guild Wars 2, many Unity/UE4 titles, Assassin's Creed Origins/Odyssey

### Games to NEVER Use DXVK With
- Any game with EAC (Fortnite, Apex Legends, Dead by Daylight, etc.)
- Any game with BattlEye (PUBG, Rainbow Six Siege, Escape from Tarkov, etc.)
- Any game with custom anti-cheat (Genshin Impact, Valorant, etc.)
- DX12-only games (DXVK doesn't translate DX12)
- Games that tightly integrate AMDAGS or NVAPI without fallback paths

---

## dxvk.conf Defaults for RHI

RHI should ship a sensible default `dxvk.conf` that works well for most games:

```ini
# RHI Default DXVK Configuration
# Place in the game's executable directory

# Enable HDR output when Windows HDR is active
dxgi.enableHDR = True

# Borderless fullscreen recommended (FSE can cause issues on Windows)
dxvk.allowFse = False

# Latency reduction (uses Reflex when available, built-in algorithm otherwise)
dxvk.latencySleep = Auto

# DPI awareness (avoids upscaling blur on Hi-DPI displays)
d3d9.dpiAware = True
```

This is intentionally minimal. Advanced users can edit the file directly or RHI can provide a "DXVK Settings" section on the Settings page for common options.

---

## References

- [DXVK GitHub Repository](https://github.com/doitsujin/dxvk)
- [DXVK Windows Wiki](https://github.com/doitsujin/dxvk/wiki/Windows)
- [DXVK Configuration Reference (dxvk.conf)](https://github.com/doitsujin/dxvk/blob/master/dxvk.conf)
- [DXVK Driver Support](https://github.com/doitsujin/dxvk/wiki/Driver-support)
- [Marty's Mods DXVK Guide](https://guides.martysmods.com/additionalguides/apiwrappers/dxvk)
- [DXVK Feature Support](https://github.com/doitsujin/dxvk/wiki/Feature-support)
- [PCGamingWiki DXVK Page](https://www.pcgamingwiki.com/wiki/DXVK)
- [dxvk-gplasync Fork](https://gitlab.com/Ph42oN/dxvk-gplasync)
- [dxvk-gplasync-lowlatency Fork](https://github.com/Digger1955/dxvk-gplasync-lowlatency)
- [dxvk-hdr Fork](https://github.com/ManOnAJetski/dxvk-hdr)
- [OptiScaler INI Reference](https://github.com/optiscaler/OptiScaler/blob/master/OptiScaler.ini)
- [OptiScaler Wiki](https://github.com/optiscaler/OptiScaler/wiki)
- [ReadShade DXVK Guide](https://readshade.github.io/ReadShade/reshade/general/dxvk.html)
