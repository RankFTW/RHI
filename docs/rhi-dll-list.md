# Files RHI can put in a game folder

Every `.dll`, `.asi` and ReShade add-on (`.addon64` / `.addon32`) that RHI (ReShade HDR Installer) can place in a game's folder, taken from RHI's source and its download cache on 26 Nov 2026 (2.9.6.3 beta).

Three things to know before using this as a detection list:

- **Proxy names are shared.** ReShade, OptiScaler, Ultimate ASI Loader, Universal MFG Unlocker, RTX Encore, DLSSG 2.0-3.0 and RE Framework can each sit under a system DLL name such as `dxgi.dll` or `version.dll`. The file name alone does not say which one it is.
- **Add-on names are open-ended.** RenoDX game mods are named `renodx-<game>.addon64` / `.addon32` (several hundred of them), and users can add any ReShade add-on. Match on the extension, not a fixed list.
- **Archive contents change.** OptiScaler, HSF, DLSSG 2.0-3.0 and Luma mods are copied as their authors package them, so a new release can add or rename files.

## By component

### ReShade

| File | Notes |
|---|---|
| `ReShade64.dll`, `ReShade32.dll` | Installed under one of the names below, or kept as-is when another component loads it |
| `dxgi.dll`, `d3d9.dll`, `d3d10.dll`, `d3d11.dll`, `d3d12.dll`, `opengl32.dll`, `dinput8.dll`, `version.dll`, `winmm.dll`, `ddraw.dll`, `xinput1_3.dll`, `msvcp140.dll`, `bink2w64.dll` | The names RHI offers for ReShade. The user can also type any other name |
| `ReShade64.asi`, `ReShade32.asi` | ReShade loaded as an ASI plugin |

Vulkan games use the global ReShade Vulkan layer in `C:\ProgramData\ReShade\` (`ReShade64.dll`, `ReShade32.dll`), not a file in the game folder.

### RenoDX

| File | Notes |
|---|---|
| `renodx-<game>.addon64` / `.addon32` | One per game mod |
| `renodx-unrealengine.addon64` | Generic Unreal Engine mod |
| `renodx-ue-extended.addon64` | UE-Extended |
| `renodx-unityengine.addon64` / `.addon32` | Generic Unity mod |
| `renodx-sourceengine.addon64` / `.addon32` | Source engine mod |
| `renodx-batmanac.addon32` | Batman: Arkham City |
| `renodx-control-rr.addon64` | Control |
| `renodx-universal_ue-dof-fix.addon64` | UE depth of field fix |
| `renodx-dlssfix.addon64` | DLSS Fix |
| `renodx-mfgunlock.addon64` / `.addon32` | MFG unlock add-on |

### Luma

| File | Notes |
|---|---|
| `Luma*.dll`, `Luma-*.addon` | Named per game by the mod |
| `d3dcompiler_47.dll` | Shipped with some Luma mods |

### ReLimiter and Display Commander

| File | Notes |
|---|---|
| `relimiter.addon64` / `.addon32` | ReLimiter |
| `ultra_limiter.addon64` / `.addon32` | ReLimiter's old name |
| `zzz_display_commander.addon64` / `.addon32` | Display Commander |
| `zzz_display_commander_lite.addon64` / `.addon32` | Display Commander Lite |
| `displaycommander.addon64` / `.addon32` | Display Commander's old name |

Display Commander can also be installed under a DLL proxy name chosen by the user.

### DLSS and Streamline

| File | Notes |
|---|---|
| `nvngx_dlss.dll` | DLSS Super Resolution |
| `nvngx_dlssd.dll` | DLSS Ray Reconstruction |
| `nvngx_dlssg.dll` | DLSS Frame Generation |
| `nvngx_dlssnr.dll` | DLSS Neural Rendering |
| `sl.interposer.dll`, `sl.common.dll`, `sl.dlss.dll`, `sl.dlss_d.dll`, `sl.dlss_g.dll`, `sl.reflex.dll`, `sl.pcl.dll`, `sl.nis.dll`, `sl.deepdvc.dll`, `sl.directsr.dll`, `sl.nvperf.dll` | Streamline |

### Neural Rendering

| File | Notes |
|---|---|
| `renodx-dlss.addon64` / `.addon32` | Also installed as `zzz_renodx-dlss.addon64` |
| `renodx-dlss5.addon64` / `.addon32` | |
| `dlss5-feed.addon64` / `.addon32` | DLSS 5 feeder |
| `dlss5-bridge.addon64` | |
| `dlssnr-companion.addon64` | DLSS NR cost scaler |
| `nvngx_dlssnr_real.dll` | The real NR runtime, kept beside the cost scaler's `nvngx_dlssnr.dll` |

### OptiScaler

| File | Notes |
|---|---|
| `OptiScaler.dll` | Normally renamed to `dxgi.dll`, `winmm.dll`, `d3d11.dll`, `d3d12.dll`, `dbghelp.dll`, `version.dll`, `wininet.dll` or `winhttp.dll` |
| `amd_fidelityfx_dx12.dll`, `amd_fidelityfx_vk.dll` | |
| `amd_fidelityfx_loader_dx12.dll`, `amd_fidelityfx_loader_vk.dll` | |
| `amd_fidelityfx_upscaler_dx12.dll`, `amd_fidelityfx_upscaler_vk.dll` | |
| `amd_fidelityfx_framegeneration_dx12.dll`, `amd_fidelityfx_framegeneration_vk.dll` | |
| `libxess.dll`, `libxess_dx11.dll`, `libxess_fg.dll`, `libxell.dll` | Intel XeSS |
| `D3D12_OptiScaler\D3D12Core.dll` | |
| `fakenvapi.dll` | |
| `dlssg_to_fsr3.dll`, `dlssg_to_fsr3_amd_is_better.dll` | |
| `OptiScaler\plugins\OptiPatcher.asi` | Older installs: `plugins\OptiPatcher.asi` |
| `dlss-enabler-headless.dll` | DLSS Enabler, in the OptiScaler folder |
| `nvngx.dll_dlssnr.dll` | DLSS NR and F5 builds |
| `OptiScaler\plugins\f5low.dll` | F5 build |
| `OptiScaler\streamline\nvngx_dlssg.dll`, `sl.common.dll`, `sl.dlss_g.dll`, `sl.interposer.dll`, `sl.pcl.dll`, `sl.reflex.dll` | F5 build |
| `OptiScaler\nvfp4\...\params_builder.dll` | DLSS NR build |

### DXVK

| File | Notes |
|---|---|
| `d3d8.dll`, `d3d9.dll`, `d3d10core.dll`, `d3d11.dll`, `dxgi.dll` | 32-bit or 64-bit, by game |
| `dxgi_dxvk.dll` | DXVK's dxgi when another file holds `dxgi.dll` (also Source engine games) |

### RE Framework

| File | Notes |
|---|---|
| `dinput8.dll` | |

### Ultimate ASI Loader

One file, installed under one of these names:

- 32-bit: `d3d8.dll`, `d3d9.dll`, `d3d10.dll`, `d3d11.dll`, `d3d12.dll`, `dxgi.dll`, `ddraw.dll`, `dinput.dll`, `dinput8.dll`, `dsound.dll`, `msacm32.dll`, `msvfw32.dll`, `version.dll`, `wininet.dll`, `winmm.dll`, `winhttp.dll`, `xlive.dll`, `binkw32.dll`, `bink2w32.dll`, `vorbisFile.dll`, `xinput1_1.dll`, `xinput1_2.dll`, `xinput1_3.dll`, `xinput1_4.dll`, `xinput9_1_0.dll`, `xinputuap.dll`
- 64-bit: `d3d9.dll`, `d3d10.dll`, `d3d11.dll`, `d3d12.dll`, `dxgi.dll`, `dinput8.dll`, `dsound.dll`, `version.dll`, `wininet.dll`, `winmm.dll`, `winhttp.dll`, `binkw64.dll`, `bink2w64.dll`, `xinput1_1.dll`, `xinput1_2.dll`, `xinput1_3.dll`, `xinput1_4.dll`, `xinput9_1_0.dll`, `xinputuap.dll`

`<name>Hooked.dll` (for example `versionHooked.dll`, `bink2w64Hooked.dll`) is a file chained behind a proxy of that name: either another mod loaded by ASI Loader, or the game's own Bink DLL.

### Universal MFG Unlocker

| File | Notes |
|---|---|
| `MFGUnlocker.dll` / `RTXMFG.dll` | Renamed to `version.dll`, `dinput8.dll`, `winmm.dll`, `d3d9.dll`, `d3d10.dll`, `d3d11.dll`, `d3d12.dll`, `dxgi.dll`, `dsound.dll`, `wininet.dll`, `winhttp.dll`, `binkw64.dll`, `bink2w64.dll`, `xinput1_1.dll`, `xinput1_2.dll`, `xinput1_3.dll`, `xinput1_4.dll`, `xinput9_1_0.dll` or `xinputuap.dll`. Kept as `MFGUnlocker.dll` when HSF loads it |
| `MFGUnlocker.asi` | When loaded by ASI Loader |
| `RTXMFG-XeSS\libxess_fg.dll`, `RTXMFG-XeSS\libxell.dll` | Intel XeSS frame generation |
| `RTX40MFG.asi`, `RTX40MFGCore.dll`, `RTX40MFG-UI.addon64` | Old versions; removed on update |

### RTX Encore

| File | Notes |
|---|---|
| `rtx-encore.dll` | Renamed to one of the same proxy names as Universal MFG Unlocker |
| `plugins\rtx-encore.asi` | When loaded by ASI Loader |
| `nvngx_dlssnr.dll` | Beside RTX Encore's file (`plugins\nvngx_dlssnr.dll` for the .asi install) |
| `libxess.dll`, `libxess_fg.dll`, `libxell.dll` | Intel XeSS |
| `dxcompiler.dll`, `dxil.dll` | RTX Hair |

### DLSSG 2.0-3.0

| File | Notes |
|---|---|
| `version.dll`, or `winmm.dll`, `dbghelp.dll`, `dinput8.dll`, `dxgi.dll`, `d3d12.dll` | One proxy |
| `dlssg_sm86\backend\sm86_backend.dll` | |
| `dlssg_sm86\kernels\fg_kernels.dll` | |
| `dlssg_sm86\runtime\nvngx_dlssg.dll` | |
| `dlssg_sm86\sr\nvngx_dlss.dll`, `dlssg_sm86\sr\sr_kernels.dll` | |
| `dlssg_sm86\xefg\xefg_kernels.dll` | |

### HSF

| File | Notes |
|---|---|
| `hsf.addon64` / `.addon32` | |
| `HSF_streamline\nvngx_dlssg.dll`, `sl.common.dll`, `sl.dlss_g.dll`, `sl.interposer.dll`, `sl.pcl.dll`, `sl.reflex.dll`, `libxess_fg.dll`, `libxell.dll` | |

### Other add-ons

| File | Notes |
|---|---|
| `FrameWarp.addon64` | FrameWarp |
| `FrameWarp\nvngx_latewarp.dll` | NVIDIA Latewarp, optional |
| `urti.addon64` | URTI |
| `uplift.addon64` / `.addon32`, `gitc-uplift.addon64` / `.addon32` | Uplift |
| `crcgi_injector.addon64` / `.addon32` | CRCGI |
| `BFIPresenter.addon64` / `.addon32` | BFI Presenter |
| `BFIPresenterVK.dll`, `BFIPresenterVK32.dll` | BFI Presenter's Vulkan layer |
| Add-ons from ReShade's own list (ShaderToggler, FreePIE, RenoDX Upgrade and others) | Chosen by the user; names vary |

### Batman: Arkham City DLAA mod

| File | Notes |
|---|---|
| `nvngx_dlss.dll` | |
| `amd_fidelityfx_loader_dx12.dll`, `amd_fidelityfx_upscaler_dx12.dll` | |

## All files, A to Z

Paths are relative to the game folder (or the folder of the component that owns them). Shared proxy names are listed once.

```
amd_fidelityfx_dx12.dll
amd_fidelityfx_framegeneration_dx12.dll
amd_fidelityfx_framegeneration_vk.dll
amd_fidelityfx_loader_dx12.dll
amd_fidelityfx_loader_vk.dll
amd_fidelityfx_upscaler_dx12.dll
amd_fidelityfx_upscaler_vk.dll
amd_fidelityfx_vk.dll
BFIPresenter.addon32
BFIPresenter.addon64
BFIPresenterVK.dll
BFIPresenterVK32.dll
bink2w32.dll
bink2w64.dll
binkw32.dll
binkw64.dll
crcgi_injector.addon32
crcgi_injector.addon64
d3d8.dll
d3d9.dll
d3d10.dll
d3d10core.dll
d3d11.dll
d3d12.dll
D3D12Core.dll
d3dcompiler_47.dll
dbghelp.dll
ddraw.dll
dinput.dll
dinput8.dll
displaycommander.addon32
displaycommander.addon64
dlss-enabler-headless.dll
dlss5-bridge.addon64
dlss5-feed.addon32
dlss5-feed.addon64
dlssg_to_fsr3.dll
dlssg_to_fsr3_amd_is_better.dll
dlssnr-companion.addon64
dsound.dll
dxcompiler.dll
dxgi.dll
dxgi_dxvk.dll
dxil.dll
f5low.dll
fakenvapi.dll
fg_kernels.dll
FrameWarp.addon64
gitc-uplift.addon32
gitc-uplift.addon64
hsf.addon32
hsf.addon64
libxell.dll
libxess.dll
libxess_dx11.dll
libxess_fg.dll
Luma*.dll
Luma-*.addon
MFGUnlocker.asi
MFGUnlocker.dll
msacm32.dll
msvcp140.dll
msvfw32.dll
nvngx.dll_dlssnr.dll
nvngx_dlss.dll
nvngx_dlssd.dll
nvngx_dlssg.dll
nvngx_dlssnr.dll
nvngx_dlssnr_real.dll
nvngx_latewarp.dll
opengl32.dll
OptiPatcher.asi
OptiScaler.dll
params_builder.dll
relimiter.addon32
relimiter.addon64
renodx-<game>.addon32
renodx-<game>.addon64
renodx-batmanac.addon32
renodx-control-rr.addon64
renodx-dlss.addon32
renodx-dlss.addon64
renodx-dlss5.addon32
renodx-dlss5.addon64
renodx-dlssfix.addon64
renodx-mfgunlock.addon32
renodx-mfgunlock.addon64
renodx-sourceengine.addon32
renodx-sourceengine.addon64
renodx-ue-extended.addon64
renodx-unityengine.addon32
renodx-unityengine.addon64
renodx-universal_ue-dof-fix.addon64
renodx-unrealengine.addon64
ReShade32.asi
ReShade32.dll
ReShade64.asi
ReShade64.dll
rtx-encore.asi
rtx-encore.dll
RTX40MFG-UI.addon64
RTX40MFG.asi
RTX40MFGCore.dll
RTXMFG.dll
sl.common.dll
sl.deepdvc.dll
sl.directsr.dll
sl.dlss.dll
sl.dlss_d.dll
sl.dlss_g.dll
sl.interposer.dll
sl.nis.dll
sl.nvperf.dll
sl.pcl.dll
sl.reflex.dll
sm86_backend.dll
sr_kernels.dll
ultra_limiter.addon32
ultra_limiter.addon64
uplift.addon32
uplift.addon64
urti.addon64
version.dll
vorbisFile.dll
winhttp.dll
wininet.dll
winmm.dll
xefg_kernels.dll
xinput1_1.dll
xinput1_2.dll
xinput1_3.dll
xinput1_4.dll
xinput9_1_0.dll
xinputuap.dll
xlive.dll
zzz_display_commander.addon32
zzz_display_commander.addon64
zzz_display_commander_lite.addon32
zzz_display_commander_lite.addon64
zzz_renodx-dlss.addon64
<name>Hooked.dll
```
