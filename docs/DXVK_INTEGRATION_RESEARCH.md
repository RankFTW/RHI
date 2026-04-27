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
- Nvidia, AMD, or Intel GPU with up-to-date drivers

---

## Why Users Want DXVK on Windows

1. **ReShade compute shaders** — Older DX8/DX9/DX10 games don't support compute shaders natively. DXVK translates them to Vulkan, which does support compute. This enables advanced ReShade effects (depth-based effects, MXAO, etc.) that wouldn't work on native DX9.

2. **Performance** — Vulkan often has lower CPU overhead than DX9/DX11. Games like FFXIV, God of War, Metaphor: ReFantazio, and Watch Dogs 2 see measurable CPU-bound performance improvements.

3. **Shader stutter reduction** — DXVK compiles shaders asynchronously and caches them. The dxvk-gplasync fork takes this further with graphics pipeline async compilation, nearly eliminating shader compilation stutter.

4. **HDR support** — DXVK supports HDR output via `dxgi.enableHDR = True` in dxvk.conf. This can enable HDR in DX11 games that don't natively support it, when combined with tools like SpecialK or Luma.

---

## Release Structure

DXVK releases are published as zip files on GitHub: `dxvk-X.Y.Z.tar.gz`

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

- ReShade must be installed as **Vulkan** (via the Vulkan layer), not as DX9/DX11
- The standard ReShade DLL proxy approach (dxgi.dll, d3d9.dll) **conflicts** with DXVK because DXVK itself IS those DLLs
- ReShade's Vulkan layer installs globally via the registry, not per-game DLL placement

### Conflict with RHI's Current Approach
RHI currently installs ReShade by copying a DLL (e.g., dxgi.dll) into the game folder. If DXVK is also using dxgi.dll, these conflict. The solution is to switch to Vulkan layer-based ReShade installation when DXVK is enabled.

RHI already has a Vulkan ReShade install path (`RequiresVulkanInstall`) — this same path should be used for DXVK games.

---

## Configuration (dxvk.conf)

DXVK reads configuration from `dxvk.conf` in the game's executable directory. Key options:

```ini
# Enable HDR output (requires Windows HDR enabled)
dxgi.enableHDR = True

# Frame rate limiter (0 = unlimited)
dxgi.maxFrameRate = 0

# Async shader compilation (reduces stutter)
dxvk.enableAsync = True

# Number of compiler threads (0 = auto)
dxvk.numCompilerThreads = 0

# Allow fullscreen exclusive mode
dxvk.allowFse = True

# Enable descriptor buffer for better CPU performance (modern GPUs)
dxvk.enableDescriptorBuffer = True
```

---

## Known Issues on Windows

Source: [DXVK Windows Wiki](https://github.com/doitsujin/dxvk/wiki/Windows)

### DLL Loading
Some games load DLLs from System32 instead of the game directory. DXVK won't work for these games without registry modifications (DevOverride), which is risky.

### Anti-Cheat
Games with anti-cheat (EAC, BattlEye) will often refuse to run with DXVK DLLs or may ban players. **DXVK should never be installed on games with active anti-cheat.**

### Third-Party Software Conflicts
- Steam/Epic/Uplay overlays may conflict
- Nvidia GeForce Experience overlay
- RTSS (RivaTuner Statistics Server)
- OBS recording
- These may work but can cause crashes

### Fullscreen
- Variable refresh rate may not work
- HDR may not work properly in fullscreen exclusive
- Borderless fullscreen is recommended

### AMD Windows Driver
DXVK developers state: "Due to a growing number of compatibility issues with the AMD Windows driver in general, supporting it is no longer a priority." AMD Polaris and Vega GPUs have effectively discontinued driver support for Vulkan 1.3.

---

## Pros of DXVK Integration in RHI

1. **Enables compute shaders** for DX8/DX9/DX10 games — unlocks advanced ReShade effects
2. **Performance improvement** for CPU-bound DX9/DX11 games
3. **Shader stutter reduction** via async compilation and caching
4. **HDR enablement** for older DX games via dxvk.conf
5. **Consistent ReShade experience** — all games use Vulkan path
6. **User demand** — commonly requested by ReShade/RenoDX community

## Cons of DXVK Integration in RHI

1. **Not officially supported on Windows** — DXVK devs explicitly state this
2. **Anti-cheat incompatibility** — can get users banned in online games
3. **DLL conflicts** — DXVK uses the same DLL names as the game's native DX runtime
4. **AMD driver issues** — AMD Windows Vulkan driver has known problems
5. **Overlay conflicts** — Steam overlay, GeForce Experience, etc. may break
6. **Complexity** — adds another layer of configuration and potential failure points
7. **Support burden** — users will report DXVK-specific issues as RHI bugs

---

## Proposed Implementation for RHI

### User-Facing Design
- Per-game toggle in the Overrides panel: "Enable DXVK"
- When toggled on, RHI handles everything: downloads DXVK, deploys correct DLLs, switches ReShade to Vulkan mode
- When toggled off, RHI removes DXVK DLLs, restores originals, switches ReShade back to DX mode
- Warning dialog on first enable explaining risks (anti-cheat, unofficial support)

### Technical Flow

**Enable DXVK for a game:**
1. Download DXVK release from GitHub (cache locally like shader packs)
2. Determine game's DX version and bitness (from existing RHI detection or manifest)
3. Back up any existing DLLs that DXVK will replace (same pattern as OptiScaler)
4. Copy correct DXVK DLLs to game directory
5. Optionally deploy a default dxvk.conf with sensible defaults
6. If ReShade is installed as DX proxy, uninstall it
7. Reinstall ReShade via Vulkan layer path
8. Store DXVK state in a tracking record (like OptiScaler/Luma records)

**Disable DXVK for a game:**
1. Delete DXVK DLLs from game directory
2. Restore backed-up original DLLs
3. Remove dxvk.conf if RHI deployed it
4. If ReShade is installed via Vulkan layer, uninstall it
5. Reinstall ReShade as DX proxy (if user wants)
6. Remove tracking record

### Data Model
```csharp
public class DxvkInstalledRecord
{
    public string GameName { get; set; }
    public string InstallPath { get; set; }
    public string DxvkVersion { get; set; }
    public List<string> InstalledDlls { get; set; }  // e.g. ["d3d9.dll", "dxgi.dll"]
    public List<string> BackedUpFiles { get; set; }   // originals that were replaced
    public DateTime InstalledAt { get; set; }
}
```

### Staging
- Download URL: `https://github.com/doitsujin/dxvk/releases/latest`
- Cache directory: `%LocalAppData%\RHI\dxvk\`
- Version tracking: same pattern as OptiScaler/ReShade staging
- Update detection: compare cached version against latest GitHub release tag

### Safety Guards
- Block DXVK toggle for games known to have anti-cheat (manifest blacklist)
- Show warning dialog on first enable
- Never touch System32/SysWOW64
- Always back up originals before replacing
- Validate DLL architecture matches game bitness

### DXVK Variants to Consider
- **Standard DXVK** (doitsujin/dxvk) — the main project
- **dxvk-gplasync** (Ph42oN/dxvk-gplasync) — fork with async pipeline compilation for less stutter
- **dxvk-hdr** (ManOnAJetski/dxvk-hdr) — fork with HDR modifications
- Could offer variant selection in settings, or default to gplasync for best user experience

---

## Games That Benefit Most from DXVK

### DX9 Games (biggest benefit — enables compute shaders)
- The Sims 3, Fallout: New Vegas, Skyrim (original), GTA IV, Dark Souls 1, Bayonetta, older Assassin's Creed titles

### DX11 Games (performance/stutter improvement)
- FFXIV, God of War, Metaphor: ReFantazio, Watch Dogs 2, many Unity/UE4 titles

### Games to NEVER Use DXVK With
- Any game with EAC (Fortnite, Apex Legends, etc.)
- Any game with BattlEye (PUBG, Rainbow Six Siege, etc.)
- Any game with custom anti-cheat (Genshin Impact, Valorant, etc.)
- DX12-only games (DXVK doesn't translate DX12)

---

## References

- [DXVK GitHub Repository](https://github.com/doitsujin/dxvk)
- [DXVK Windows Wiki](https://github.com/doitsujin/dxvk/wiki/Windows)
- [DXVK Configuration Wiki](https://github.com/doitsujin/dxvk/wiki/Configuration)
- [DXVK Driver Support](https://github.com/doitsujin/dxvk/wiki/Driver-support)
- [Marty's Mods DXVK Guide](https://guides.martysmods.com/additionalguides/apiwrappers/dxvk)
- [DXVK Feature Support](https://github.com/doitsujin/dxvk/wiki/Feature-support)
- [dxvk-gplasync Fork](https://gitlab.com/Ph42oN/dxvk-gplasync)
- [dxvk-hdr Fork](https://github.com/ManOnAJetski/dxvk-hdr)
