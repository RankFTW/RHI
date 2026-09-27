# RHI on Bazzite / Linux

This repository now includes a native Linux desktop application for managing Windows ReShade and RenoDX components in Proton games. It runs without Wine, a system .NET installation, root access, or changes to Bazzite's immutable base image. The original Windows WinUI app remains a separate build.

## Run

For a ready-to-run build, download **RHI-linux-x64.tar.gz** from [a Linux release](https://github.com/TwoToneEddy/RHI/releases), extract it into a permanent folder, and run `./run-linux.sh` there. Run `./install.sh` in that folder to add an application-menu entry. The package includes .NET; no SDK, compilation, or root access is needed. Keep the accompanying files together. See [release installation and updates](LINUX-RELEASE.md).

To build from source on Bazzite:

```bash
git clone --branch linux_port https://github.com/TwoToneEddy/RHI.git
cd RHI
./run-linux.sh
```

The first run downloads a user-local .NET SDK if needed, restores packages, runs tests, and builds the app. Allow several minutes and an internet connection. Later runs launch the existing build. After pulling source updates, run `./scripts/build-linux.sh` to rebuild.

The standalone build is `artifacts/linux-x64/RHI.Linux`. The portable package and checksum are `artifacts/RHI-linux-x64.tar.gz` and `artifacts/RHI-linux-x64.tar.gz.sha256`.

To add a KDE application-menu entry:

```bash
./scripts/install-linux-desktop.sh
```

## First game

1. Select your game from the Windows-style library sidebar. Use the search and **All Games / Installed / Favourites** filters to find it.
2. Close the game and choose **Install recommended**. RHI detects the executable and architecture, installs ReShade and the matched RenoDX addon, and applies supported UE Extended HDR settings. Installing RenoDX on its own also installs ReShade when needed.
3. If **Finish Steam setup** appears, click it. **Apply & restart Steam** saves the required launch options, retaining existing arguments and making a backup. Close other running games first. Copy/paste is available as an alternative.
4. Choose **Launch**. Let the game's shader compilation finish, confirm the menu and gameplay render correctly, and press **Home** to adjust RenoDX or enable optional ReShade effects.

The **Components** table shows green **Installed** labels when the actual managed files are present and match their recorded hashes. **Applied** means the current installed plugins appear in the last recorded game launch log. A log from before the latest plugin update does not count. **Ready to launch**, **Steam setup applied**, and **HDR settings applied** reflect setup checks, not a gameplay or display-calibration guarantee. Missing or changed plugin files show **Needs repair**.

**Nightly** is displayed beside ReShade and under **Game overrides → ReShade channel**. Click its cog or **Change**, select a channel, and choose **Apply & install** to download and install it. Changing a selection and cancelling does not change the game. A previously saved channel that differs from the installed version is explicitly marked as needing application. Nightly is the default for new games because stable 6.8.0 lacks newer Proton compatibility fixes.

The interface uses the original Windows app's logo, dark palette, component-table layout, green status colors, blue install/reinstall buttons, sidebar filters, and settings/removal buttons. **Views** toggles the optional rows for a compact component view. **Shaders/Addons** installs optional packs or custom addons for the selected game. **Update All** updates managed ReShade/RenoDX installations using each game's saved channel; local custom builds are kept.

**Advanced settings** retains executable and API selection, custom Proton prefixes, local ReShade builds, manual catalogue matching, raw launch options, and backup/recovery controls. Normal Steam setup does not require editing these. **Settings → Add Steam library** adds custom/external libraries; **+** adds a Windows executable from another launcher.

For Heroic/Lutris, add `WINEDLLOVERRIDES` as an environment variable in the launcher's per-game settings; the value for DX10/11/12 is `dxgi=n,b;d3dcompiler_47=n,b`. Keep any other existing DLL entries. Use the launcher to run the game with its existing runner/prefix.

## Neural Rendering (DLSS 5) and DLSS overrides

The detail panel has the Windows app's **Neural Rendering** and **Nvidia Profile Overrides** sections below **Game overrides**. Click a section's title to collapse it; collapsed sections show a one-line summary.

**Neural Rendering** offers the same four methods as Windows. Inapplicable methods are disabled, and a recommended method is preselected:

| Method | Use for | Deploys |
| --- | --- | --- |
| ShortFuse DLSS Tool | Most 64-bit games with native DLSS (DX12, DX11, DX9, Vulkan) | `renodx-dlss.addon64`, the newest DLSS SR/RR/FG, the NR DLL and Streamline |
| DLSS5 Tool | DX12 games with native DLSS | `renodx-dlss5.addon64`, the newest DLSS SR/RR/FG and the NR DLL |
| DLSS5 Tool + DX11 Bridge | DX11/Vulkan games with native DLSS | as DLSS5 Tool, plus `dlss5-bridge.addon64` |
| DLSS5 Feeder | Games without native DLSS, OpenGL and 32-bit games | `dlss5-feed.addon64/32`, DLSS5 Tool as the neural consumer, DLSS SR, the NR DLL, `DLSS5_Feed.fx` and LumeniteFX's `lumenite_Kernel.fx`; for 32-bit games a `host64/` helper with 64-bit ReShade; for DX9 games dgVoodoo2 |

Choose the addon, Feeder/Bridge and NR DLL versions (or **Latest**, which **Update All** keeps current). Changing a version while installed swaps it in place. **NR Cost Scaler** (set before installing) adds the DLSS NR Cost Scaler proxy. The **⚙** beside ShortFuse writes `HookStreamline=1` and `HookDirectX=1` to ReShade.ini; the Windows ASI-loader rename is not needed on Linux (ShortFuse v0.54+ doesn't require it). RHI installs ReShade first when needed and, for the Feeder, the Standard shader pack (its shaders include `ReShade.fxh`). The Feeder's techniques are added to your existing ReShade preset rather than replacing it. For DX9 Feeder games ReShade is loaded as `dxgi.dll` behind dgVoodoo2, as on Windows. The neural model currently runs on NVIDIA RTX 50-series GPUs.

**Nvidia Profile Overrides** shows the game's DLSS Super Resolution, Ray Reconstruction, Frame Generation, Neural Rendering and Streamline DLLs, wherever they are in the game folder (including Unreal plugin folders):

- **Version** swaps a DLL: *Default* restores the game's own, a version downloads it from the same list the Windows app uses, and *Custom* uses your own file from `~/.local/share/rhi-linux/Custom/DLSS/` (or `Custom/Streamline/`). **Settings → Open custom DLSS folder** opens it.
- **Preset**, **Render Scale**, **Multi Frame Gen** and the **NVIDIA Override** version option write the same driver settings as the Windows app. Proton has no NVIDIA driver profile, so RHI passes them to dxvk-nvapi through `DXVK_NVAPI_DRS_SETTINGS` in the game's launch options. When they change, the section shows **Launch settings need updating**; **Apply launch settings** uses the normal Steam setup (existing options and your own `DXVK_NVAPI_DRS_SETTINGS` entries are kept). NVIDIA Override also sets `PROTON_ENABLE_NGX_UPDATER=1`. Managed DLSS also sets `PROTON_ENABLE_NVAPI=1`.
- **Deploy DLL / ✕** adds or removes `nvngx_dlssnr.dll`. **Quick Apply** applies your **Settings → DLSS defaults…**, which can also be applied to every installed DLSS game at once. **Restore DLSS/SL** restores every swapped DLL and resets the presets.

DLL swaps use the Windows app's `.original` convention (a real backup, or an empty marker when RHI created the file), so a library shared with Windows RHI stays consistent; Neural Rendering installed by the Windows app is recognised and can be removed here. Neural Rendering's addons and shaders are also tracked in `.rhi-linux/` with the other components, and `.rhi-linux/neural-rendering.json` records the DLLs it placed. The Windows driver-profile extras (ReBAR, Present Method and similar NVIDIA Profile Inspector settings) are not available under Proton.

## Proton locations and HDR

Typical locations:

| Purpose | Linux location |
| --- | --- |
| Game payload | `<library>/steamapps/common/<install directory>/<executable directory>/` |
| Proton prefix | `<library>/steamapps/compatdata/<appid>/pfx/` |
| Windows LocalAppData | `<prefix>/drive_c/users/steamuser/AppData/Local/` |
| Flatpak Steam root | `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/` |

The prefix may be in a different Steam library from the game. It may not exist until the first launch, and it may be overridden by your launcher. You can save an explicit prefix in the app. Game payloads use relative Windows shader paths, so they work through both native and Flatpak Steam without relying on access to RHI's cache.

For UE Extended games that require `Engine.ini`, the app finds config files **inside the game's prefix**. Select the correct file and use **RenoDX cog → Apply recommended HDR settings** (also available in Advanced settings). This preserves existing ray-tracing/FSR and other keys and backs up the settings it changes. The recipe enables the Unreal HDR output path and real-time LUT updates; it also selects `Set_Path=0` in ReShade's RenoDX configuration. The game can rewrite Engine.ini at launch, so RHI marks this file read-only as required by the wiki. The dedicated restore button restores the original permissions as well as the keys. These tweaks are not recommended for UE4 games; follow the game's wiki instructions.

HDR must also be enabled and supported by your display, compositor and Proton runtime. RHI does not change desktop display settings or force a particular Proton version. Bazzite desktop/Gamescope setup can differ; see the sources below. ReShade loading successfully is separate from confirming your display's HDR output and calibrating it.

For the installed **Mortal Shell II** test case, Steam calls the game folder `Sparta`, but the rendering executable is `MortalShell2/Binaries/Win64/MortalShell2-Win64-Shipping.exe`. Its app ID is `2584270`, and its UE config is under `MortalShell2/Saved/Config/Windows` inside that app's prefix. The wiki lists the game under **UE Extended**, with an `Engine.ini` requirement.

## Backups and removal

- Plugin ownership, SHA-256 hashes, original files and transaction snapshots are in `.rhi-linux/` beside the chosen executable. Keep this directory to preserve uninstall/restore support.
- Existing unmanaged plugin files require the explicit backup-and-replace checkbox. A DLL modified after RHI installed it is preserved and reported as a conflict.
- Updates keep the original pre-RHI backup. Interrupted transactions are recovered before the next install/remove. Removing a component restores its original files. Edited ReShade settings are preserved.
- HDR INI recovery records are in `~/.local/share/rhi-linux/ini-backups/` (or `$XDG_DATA_HOME/rhi-linux/ini-backups`). Restore undoes only RHI's keys and preserves subsequent unrelated edits.
- Removing RenoDX or all managed components also restores HDR INI edits for the selected game. Remove the injected DLL override from your Steam/launcher launch options afterwards.
- Downloads/cache: `~/.cache/rhi-linux/`. Preferences and logs: `~/.local/share/rhi-linux/`. XDG overrides are respected. Clearing downloads does not delete game-local plugin backups.

## Linux support boundaries

Supported: Steam/Flatpak library scanning; manual games; ReShade stable/nightly/local; live/cached RenoDX catalogue; named and shared addons; shader packs; local addons; API/architecture selection; Proton launch options; UE Extended prefix configuration; component updates and reversible removal.

Also supported: Neural Rendering (all four DLSS 5 methods, Cost Scaler), DLSS/Streamline version swaps, and DLSS presets/render scale/Multi Frame Gen/NVIDIA Override through dxvk-nvapi.

This is not full Windows feature parity. Windows-only NVIDIA driver profile settings (ReBAR, Present Method), Windows HDR toggles, Windows global Vulkan layers, automatic detection of every non-Steam launcher, OptiScaler/Luma workflows, and native Linux Vulkan games are not ported. Addon compatibility and anti-cheat policies remain game-specific. Choose games that allow DLL modding.

## Build and validation

```bash
./scripts/build-linux.sh
./run-linux.sh --scan
./run-linux.sh --catalog-check
./run-linux.sh --smoke-test
./run-linux.sh --nr-smoke-test
```

The build script uses an installed .NET SDK or installs SDK 8.0.425 in the user's data directory, runs Linux unit/integration tests, and publishes a self-contained Linux x64 build. It needs network access for NuGet on the first build. Bazzite supplies the native desktop libraries and `7z` used to extract official ReShade setup executables. On another distribution, install 7zip, X11/XWayland, fontconfig, libc and the normal .NET native prerequisites.

`--scan` is read-only and prints detected paths as JSON. `--smoke-test` downloads real x86/x64 ReShade, a real RenoDX addon, and shader packs, then checks install/update/remove and original restoration **in an isolated temporary folder**, plus the nightly x64 download. It never installs into detected games. `--nr-smoke-test` downloads the real Neural Rendering components and, in disposable game folders, installs each method (including 32-bit and DX9 Feeder), checks the status, launch settings and an in-place version swap, then verifies removal restores every original file. Headless UI tests exercise installed/applied indicators, filtering, and the ReShade channel dialog against isolated game folders. Unit tests cover stale launch logs, changed payloads, launch-readiness checks, flatpak/external libraries, symlink deduplication, casing, malformed manifests, PE architecture rejection, safe launch-option merges, interrupted transactions, file conflicts, and INI restoration.

The original Windows solution requires its Windows build environment; use `RHI.Linux.sln` or the Linux build script on Bazzite. The GUI uses Avalonia with software rendering to avoid depending on the game's graphics stack.

The command `./run-linux.sh --prepare APPID --ue-hdr --nightly` can prepare a matching UE Extended Steam game directly; omit `--ue-hdr` for other named mods. Without `--nightly`, preparation uses the game's saved ReShade channel (Nightly for new entries). This command writes game files. Use `--save-launch-options APPID` after exiting Steam to persist the required DLL overrides.

## Creating Linux releases

The **Linux build and release** GitHub Actions workflow runs the tests, publishes a self-contained x64 app, checks the extracted package and menu installer, and uploads a downloadable build artifact on pushes and pull requests to `linux_port`, `main`, and `master`. It also supports **Run workflow** once the workflow exists on the repository's default branch.

To prepare a release, commit the changes and push a unique Linux tag pointing at the version you want to ship, for example:

```bash
git tag -a linux-v0.1.0 -m 'Linux preview 0.1.0'
git push origin linux-v0.1.0
```

When that tag's build and package checks pass, the workflow creates a **draft prerelease** with the archive, SHA-256 checksum, and installation instructions attached. Open **Releases** on GitHub, review the draft, and publish it. Linux tags are separate from Windows version tags. GitHub Actions must be enabled and permitted to write repository contents for the release job.

You can also prepare and verify the same files locally, then attach them to a release yourself:

```bash
./scripts/build-linux.sh
./scripts/check-linux-package.sh
```

Only the application is bundled; ReShade, RenoDX, and shader packs are downloaded when users install them. `BUILD-INFO.txt` in the archive records the source commit and SDK. The Linux release remains a preview with the support boundaries described above.

## Black screen with an accessible ReShade overlay

On the tested GE-Proton11-6 setup, stable ReShade 6.8.0 caused a black screen in Mortal Shell II even with RenoDX disabled. ReShade nightly from September 12, 2026 restored the menu background. With RenoDX UE Extended and the native HDR recipe re-enabled, the user also confirmed normal gameplay without cursor trails or a black background. Its newer VKD3D interface hooks include an upstream compatibility fix absent from 6.8.0. Select **Nightly** in RHI and install/update ReShade before changing game graphics settings.

To undo RHI's HDR edits with the game closed, use **RenoDX cog → Restore previous HDR settings** or `./run-linux.sh --restore-hdr 2584270`. This also restores Engine.ini's original file permissions. Removal controls can restore the pre-install plugin files for a baseline test. Keep gameplay validation separate from the isolated download/install smoke test.

## Sources

- [RenoDX mod catalogue and UE Extended instructions](https://github.com/clshortfuse/renodx/wiki/Mods)
- [Valve Proton runtime configuration](https://github.com/ValveSoftware/Proton#runtime-config-options)
- [Bazzite launch options](https://docs.bazzite.gg/Gaming/launch-options-env-variables/)
- [ReShade](https://reshade.me/)
- [Avalonia Linux platform support](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)
- [dxvk-nvapi driver settings (DXVK_NVAPI_DRS_SETTINGS)](https://github.com/jp7677/dxvk-nvapi#tweaks-debugging-and-troubleshooting)
- [DLSS5 Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder), [DLSS5 DX11 Bridge](https://github.com/NIGos/dlss5-bridge), [DLSS NR Cost Scaler](https://github.com/xenmods/DLSSNR-Cost-Scaler)

- [Verified Microsoft shader compiler extraction used by reshade-steam-proton](https://github.com/kevinlekiller/reshade-steam-proton/blob/main/reshade-linux.sh)
- [ReShade support for newer VKD3D device interfaces](https://github.com/crosire/reshade/commit/ec0346e035b7d1c267103ea0d7c231b3945fc2b1)
