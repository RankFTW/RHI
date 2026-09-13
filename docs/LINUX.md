# RHI on Bazzite / Linux

This repository now includes a native Linux desktop application for managing Windows ReShade and RenoDX components in Proton games. It runs without Wine, a system .NET installation, root access, or changes to Bazzite's immutable base image. The original Windows WinUI app remains a separate build.

## Run

From this checkout:

```bash
./run-linux.sh
```

The prepared standalone build is `artifacts/linux-x64/RHI.Linux`. The portable package is `artifacts/RHI-linux-x64.tar.gz`; extract the whole archive and run `./RHI.Linux` inside it. Keep the accompanying files together.

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

This is not full Windows feature parity. Windows NVIDIA driver profiles, Windows HDR toggles, Windows global Vulkan layers, automatic detection of every non-Steam launcher, OptiScaler/Luma/DLSS bulk workflows, and native Linux Vulkan games are not ported. Addon compatibility and anti-cheat policies remain game-specific. Choose games that allow DLL modding.

## Build and validation

```bash
./scripts/build-linux.sh
./run-linux.sh --scan
./run-linux.sh --catalog-check
./run-linux.sh --smoke-test
```

The build script uses an installed .NET SDK or installs SDK 8.0.425 in the user's data directory, runs Linux unit/integration tests, and publishes a self-contained Linux x64 build. It needs network access for NuGet on the first build. Bazzite supplies the native desktop libraries and `7z` used to extract official ReShade setup executables. On another distribution, install 7zip, X11/XWayland, fontconfig, libc and the normal .NET native prerequisites.

`--scan` is read-only and prints detected paths as JSON. `--smoke-test` downloads real x86/x64 ReShade, a real RenoDX addon, and shader packs, then checks install/update/remove and original restoration **in an isolated temporary folder**, plus the nightly x64 download. It never installs into detected games. Headless UI tests exercise installed/applied indicators, filtering, and the ReShade channel dialog against isolated game folders. Unit tests cover stale launch logs, changed payloads, launch-readiness checks, flatpak/external libraries, symlink deduplication, casing, malformed manifests, PE architecture rejection, safe launch-option merges, interrupted transactions, file conflicts, and INI restoration.

The original Windows solution requires its Windows build environment; use `RHI.Linux.sln` or the Linux build script on Bazzite. The GUI uses Avalonia with software rendering to avoid depending on the game's graphics stack.

The command `./run-linux.sh --prepare APPID --ue-hdr --nightly` can prepare a matching UE Extended Steam game directly; omit `--ue-hdr` for other named mods. Without `--nightly`, preparation uses the game's saved ReShade channel (Nightly for new entries). This command writes game files. Use `--save-launch-options APPID` after exiting Steam to persist the required DLL overrides.

### Black screen with an accessible ReShade overlay

On the tested GE-Proton11-6 setup, stable ReShade 6.8.0 caused a black screen in Mortal Shell II even with RenoDX disabled. ReShade nightly from September 12, 2026 restored the menu background. With RenoDX UE Extended and the native HDR recipe re-enabled, the user also confirmed normal gameplay without cursor trails or a black background. Its newer VKD3D interface hooks include an upstream compatibility fix absent from 6.8.0. Select **Nightly** in RHI and install/update ReShade before changing game graphics settings.

To undo RHI's HDR edits with the game closed, use **RenoDX cog → Restore previous HDR settings** or `./run-linux.sh --restore-hdr 2584270`. This also restores Engine.ini's original file permissions. Removal controls can restore the pre-install plugin files for a baseline test. Keep gameplay validation separate from the isolated download/install smoke test.

## Sources

- [RenoDX mod catalogue and UE Extended instructions](https://github.com/clshortfuse/renodx/wiki/Mods)
- [Valve Proton runtime configuration](https://github.com/ValveSoftware/Proton#runtime-config-options)
- [Bazzite launch options](https://docs.bazzite.gg/Gaming/launch-options-env-variables/)
- [ReShade](https://reshade.me/)
- [Avalonia Linux platform support](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)

- [Verified Microsoft shader compiler extraction used by reshade-steam-proton](https://github.com/kevinlekiller/reshade-steam-proton/blob/main/reshade-linux.sh)
- [ReShade support for newer VKD3D device interfaces](https://github.com/crosire/reshade/commit/ec0346e035b7d1c267103ea0d7c231b3945fc2b1)
