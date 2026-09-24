## v2.7.6 beta 3

### New

- **Available HDR Mods** — new button next to Quick Start. Opens a searchable list of every supported game, showing which of RenoDX, Luma, and OptiScaler are available, with direct download links.
- **DLL naming overrides redesigned** — the old enable/disable toggle is gone. ReShade, Display Commander, and OptiScaler each have their own dropdown in the Game Overrides panel. Selecting a name renames the file immediately; selecting `--------` reverts it. A Reset button reverts all three at once.
- **Collapsed sections now show a live summary** — when a detail panel section is collapsed, key info is shown inline: installed component versions (Components), active DLSS/Streamline versions (NVIDIA Profile), active NR method (Neural Rendering), installed extras (Extras), and any active overrides (Game Overrides).
- **Luma mods from GitHub release assets** — mods that have been released as a build but not yet published on the Luma wiki now appear automatically in RHI.
- **PCGamingWiki data now loaded as a single file** — replaces per-game requests. Covers 55,000+ games. PCGW links, API detection, and Engine.ini paths all work as before, just faster.

### Neural Rendering

- **Swap addon version while installed** — changing the version dropdown while Neural Rendering is installed now swaps the addon file in-place. No need to uninstall first. Only the addon file is replaced — DLSS DLLs, configs, and shaders are left alone.
  - DLSS5 Tool / DLSS5 Tool + Bridge: swaps `renodx-dlss5.addon64`
  - ShortFuse (DLSS Tool): swaps `renodx-dlss.addon64`
  - Feeder: swaps the neural consumer (`renodx-dlss5.addon64`) and/or the Feeder addon itself (`dlss5-feed.addon64`) independently
  - Bridge: swaps `dlss5-bridge.addon64`
- **Version dropdown shows latest version number** — "Latest" now shows the actual version in brackets, e.g. `Latest (7.0.0-rc1)`.

### Changes

- Simple View removed — the app is always in Detail View.
- DLSS5 Tool, ShortFuse, DLSS5 Feeder, DX11 Bridge, and MFG Ada Unlock removed from the addon picker — use the Neural Rendering and Extras sections instead. Existing per-game selections are cleaned up silently; nothing is uninstalled.
- "HDR Mods" separator added to the Components section.
- "Combo" added to the OptiScaler FG Nvngx Override dropdown.
- Preset F added to the OptiScaler DLSS RR preset dropdown.

### Bug Fixes

**UI responsiveness**
- Fixed UI freezing during navigation, installs, uninstalls, and menu interactions. Moved 20+ blocking operations off the UI thread: async logging, cached filesystem state on game cards, debounced settings saves, async 7-Zip extraction, async mass-deploy loops, async cog dialog reads, async auto-update pass, async Settings page init, and more.
- Fixed window size and position not being restored when RHI starts minimized to tray (e.g. on Windows startup). The window now opens at the correct size and position when shown from the tray.

**PCGW reliability**
- Fixed PCGamingWiki lookups permanently failing for the rest of a session after a single timeout or rate limit error. The service now pauses temporarily and retries automatically after recovery. Respects the `Retry-After` header on 429 responses.
- Fixed transient PCGW failures being persisted as permanent "no result" cache entries, which would suppress a game's PCGW link even after the service recovered.

**Neural Rendering**
- Fixed `DLSS5_Feed.fx` and `lumenite_Kernel.fx` not deploying on games with no prior shader selection (e.g. Dragon Age Inquisition on a fresh install). The shader selection is now written before the ReShade install step runs.

**Startup hang**
- Fixed RHI hanging indefinitely on "Building cards..." for users with RTX Remix installed on a game (e.g. Fallout New Vegas). RTX Remix creates circular directory symlinks that the DLSS scanner would follow forever. The scanner now stops at depth 8 and bails on paths over 300 characters.

**Sleep/wake freeze**
- Fixed the app freezing after waking from sleep when a game with DLSS or driver profile settings was selected. NVAPI reads now run with a 5-second timeout.

**Other**
- Fixed the "New Mods" notification not showing for Nexus-only mods.
- Fixed leftover `renodx-dlss5.addon64` files in game folders after clearing the global addon picker on ShortFuse or Feeder games.
- Fixed the OptiScaler version/Info button opening the wrong releases page when the DLSS NR variant was installed.

### Manifest Updates

- Added dgVoodoo2 v2.87.5 (released by the author specifically to avoid false-positive Defender detections of D3D9.dll).
- Added install warnings for 21 Luma mods available as release builds but not yet on the wiki.
- Added `L.A. Noire` name mapping for Luma release asset matching.
- Added Overwatch install subpath (`_retail_`).
- Added SILENT HILL: Townfall engine hint (UE 5.6.1) and install subpath.

## v2.7.5

### Bug Fixes

**Settings**
- Fixed the app freezing when opening Settings after the PC had been idle or the GPU woke from sleep. NVAPI reads now run on a background thread with a 5-second timeout, so the Settings page always opens immediately.

**DLSS5 Feeder**
- Fixed dgVoodoo2 not being deployed for DX9 games (Gothic II, Diablo, etc.) where the API scan returned an empty result set. DX9 detection now falls back to the primary detected API, so dgVoodoo2 installs correctly on all DX9 games.
- Fixed the host64\\ folder not being deployed on 32-bit games that aren't detected as DX9 (e.g. Diablo GOG). The host64\\ folder is required for all 32-bit Feeder installs — it no longer depends on DX9 being detected.
- Fixed LumeniteFX and DLSS5_Feed.fx disappearing from the game folder after restarting RHI. The shader selection was being saved on the UI thread and could be overwritten by a concurrent settings save — it's now written synchronously during the install.
- Fixed the 64-bit Feeder addon being copied to the game folder on 32-bit games when a specific Feeder version was pinned. Versioned staging only stores `.addon64`, so 32-bit games now always use the AddonPackService which has the correct `.addon32`.

**Luma**
- Fixed a crash when clicking "Install Luma" on a game where Luma was originally installed via drag-drop. These games have no download URL, so RHI now shows "Drop a Luma archive onto the card to reinstall." instead of crashing.

### Manifest Updates

- Fixed Engine.ini being written to the wrong folder for Clive Barker's Hellraiser: Revival Demo (`Hellraiser` instead of `Hellraiser_Demo`).

## v2.7.4

### Bug Fixes

- Fixed `DLSS5_Feed.fx` not deploying when the shader staging file had been deleted — RHI now re-extracts it from the cached Feeder zip automatically. Stale registration entries that were blocking re-extraction are also cleared on startup.

### Manifest Updates

- Added engine hint for Mount & Blade II: Bannerlord (Daroya Engine).

## v2.7.3

### New

- UE-Extended games now show a status icon next to the addon name — a green ✓ for mods marked complete, and 🔨 for mods still in progress.
- You can now select individual files from your Custom Shaders folder in the shader picker. Files from `%LocalAppData%\RHI\reshade\Custom\Shaders\` and `\Textures\` appear as a "Custom Shaders" section between Recommended and Extra packs, grouped by subfolder. Tick or untick individual files to control exactly what gets deployed. An "Open Custom Folder" button in the Profiles panel opens the folder directly.
- DXVK has moved to the Extras section, under a new "API Upgrades" sub-header. The install button is always available on eligible games (DX8/9/10). Variant selection (Lilium HDR by default, Development, Stable) and the Lilium preset are now in the DXVK cog alongside the existing present method settings.

### Neural Rendering

- Feeder and Bridge version selection — you can now pin a specific release version of the Feeder or Bridge addon instead of always using the latest. The dropdown shows the full release history (28+ versions including betas).
- ShortFuse (DLSS Tool) is now available on all 64-bit games except OpenGL — previously it only showed on games with native DLSS.
- Fixed Feeder installing the wrong dgVoodoo2 file on 64-bit DX9 games — the 32-bit version was always used, so dgVoodoo2 did nothing and the shader failed to compile. Reinstall Feeder on any affected game to fix it.
- Fixed global shaders not being removed from the game folder immediately when Feeder is installed.
- Fixed `DLSS5_Feed.fx` not deploying to the game folder on install.
- Fixed the Neural Rendering panel not building on some sessions.

### Game Detection

- Unity games now correctly report their API based on Unity's own configuration file, rather than PE import scanning (which reads the Unity player DLL and sees every API). This fixes games like Caves of Qud showing DX12 instead of their actual runtime API.
- Games with a trademark symbol in their name (®, ™) now correctly match against RHI database entries — Borderlands® 4 and similar were not being found.
- Unreal Legacy (UE1/2/3) games that have a DX11 compatibility shim in their imports now correctly show DX9 as their primary API.
- PCGamingWiki API detection now reads DX9, DX10, Vulkan, and OpenGL in addition to DX11/DX12. Games where PE scanning returns no result will now use PCGW data as the source of truth.

### Bug Fixes

**Crashes and freezes**
- Fixed intermittent UI freezes affecting installs, Nexus sign-in, drag-drop, Update All, Luma installs, and app launch with `--launch`.
- Fixed a slow memory and connection leak that built up over a long session.
- Fixed a leak where event handlers accumulated every time a game card was opened.

**Luma**
- Fixed Luma uninstall removing the shader folder even when ReShade was still installed — shaders are now kept and redeployed.
- Fixed Luma uninstall leaving a `reshade-shaders-original` folder behind.
- Fixed Luma uninstall leaving `reshade.ini` in a Luma-configured state — a fresh copy is now deployed with your hotkeys and settings intact.
- Fixed Luma uninstall leaving `nvngx_dlss.dll` behind.
- Fixed Luma mods on Nexus showing "Update Available" repeatedly — this was a false positive from comparing page edit timestamps rather than actual file releases.

**Game Pass**
- Fixed ReShade being lost after a Game Pass game updates — Windows replaces the install folder with a new versioned path on update. RHI now detects this and reinstalls ReShade automatically on the next launch.

**Other**
- Fixed PCGamingWiki links never appearing on game cards.
- Fixed the render scale input box keeping keyboard focus after pressing Enter.
- Fixed occasional incorrect DLSS scan counts when multiple games were scanned at the same time.
- Fixed file cleanup silently stopping partway through if a file was locked by another process.
- Window position and size are now saved whenever you finish moving or resizing the window, not only on a clean close — so your layout is preserved even if RHI is force-closed or restarted by an update.
- The version number in the status bar is now clickable and checks for app updates.

### Manifest Updates

- Added "Banishers: Ghosts of New Eden - The Wanderer Set DLC" to blacklist — was being incorrectly detected as a game.
- Removed Elden Ring and Elden Ring: Nightreign from the external-only list — both mods now have direct download links in RHI.
- Added engine hint for Insurgency: Sandstorm.
- Added DX9 API override for Outlast and Outlast 2 — both were showing DX11 due to a Unreal Legacy PE import shim.
- Updated engine hint for MGS4 and Peace Walker (Master Collection) to KojiPro Engine.

### Diagnostic Logging

This build includes enhanced session logging to help track down a UI freeze that occurs intermittently. Every major UI rendering operation logs timing and state to the session log. If the app freezes, close it and share the log from `%LocalAppData%\RHI\Logs\` — it will help narrow down the cause.

## v2.7.2

### New

- **RHI Database is now the default mod data source** — RHI now uses its own community database instead of scraping the RenoDX wiki. The database is easier to maintain, more reliable, and gives RHI full control over the data without depending on third-party infrastructure. There is also a security benefit: the RenoDX wiki is publicly editable, and with RenoDX's growing userbase and by extension RHI's there is a real risk that someone could upload a malicious file disguised as a legitimate HDR mod — RHI would have no way to prevent it from being delivered to users. The RHI database is controlled and reviewed before any changes go live. Big thanks to Scrungus for the work transferring all wiki mods and details across to the new database. You can still switch back to the RenoDX Wiki scraper in Settings if needed.

### Bug Fixes

- Fixed UI freezing when navigating between games — RHI was doing filesystem I/O (directory scans for the AppData button) on the UI thread on every card selection. This is now pre-computed in the background at startup and cached per game.
- Fixed UI becoming permanently unresponsive after clicking Install on Luma (and potentially other components) — shader pack settings read/write operations were using a synchronous lock that could block the UI thread when background download tasks held the lock concurrently.
- Fixed DXVK uninstall blocking the UI thread during the ReShade mode switch that follows it.
- Fixed the shader pack picker briefly freezing the UI when opened during an active shader pack download.
- Fixed Settings page driver combo changes (Shader Cache, G-Sync, FPS Limit, ReBAR, VSync, Power Mode, etc.) causing brief UI hitches — all NVAPI profile writes are now dispatched to a background thread.
- Fixed per-game NVIDIA driver setting combos (VSync, Low Latency, Smooth Motion, Power Mode, G-Sync, ReBAR) in the Game Overrides panel causing brief UI hitches on every change.
- Fixed DLSS preset combos, render scale, and driver override toggles in the NVIDIA Profile section causing brief UI hitches on every change.
- Fixed RTX HDR toggle and Configure RTX HDR apply button blocking the UI thread on NVAPI writes.
- Fixed MFG dialog (FG Mode, Generation Factor, Target FPS) blocking the UI thread on NVAPI writes.
- Fixed drag-dropping a `.addon` file (some Luma mods use this extension) showing "No Addon Found" — `.addon` files are now accepted alongside `.addon64` and `.addon32`. Luma-named addon files are now correctly routed through the Luma install flow (with ReShade deploy, dgVoodoo2, shaders, etc.) instead of the RenoDX addon flow. The install picker now shows "🌙 Install Luma Addon" and uses fuzzy filename matching to pre-select the correct game.
- Fixed ReShade installing as `dxgi.dll` on Borderlands 2 and Borderlands: The Pre-Sequel — these are DX9 games that should use `d3d9.dll`. The `dxgi.dll` override was intended for Luma+dgVoodoo installs but was incorrectly applied to plain ReShade installs as well.
- Fixed ReShade remaining as `dxgi.dll` after uninstalling Luma on DX9+dgVoodoo games — it now reinstalls automatically with the correct `d3d9.dll` filename.
- Fixed dgVoodoo2 not deploying for DX9 games when installing DLSS5 Feeder — it was incorrectly gated on the Luma dgVoodoo manifest list, which only covers Luma-specific games. dgVoodoo2 is now deployed for all DX9 Feeder installs.
- Fixed DLSS5 Feeder installing ReShade as `d3d9.dll` on DX9 games — dgVoodoo2 owns `d3d9.dll` on those games, so ReShade must be `dxgi.dll`. Existing wrong installs are corrected automatically on re-install.
- Fixed background scan renaming ReShade from `dxgi.dll` back to `ReShade32.dll` on DX9+dgVoodoo Feeder games — the reconciliation logic now correctly skips games where dgVoodoo2 is active.
- Fixed auto-update installing ReShade as `d3d9.dll` on DX9+dgVoodoo Feeder games — same fix as above applied to the Update All ReShade path.

### Manifest Updates

- Updated UltraShade (formerly Ultra ReShade by Ultra+) to the new repo URL and display name.

## v2.7.1

### New

- **Export Game Data** — new button in Settings next to Copy Logs. Gathers your game library data (graphics API, exe paths, engine, store IDs) and copies a zip to clipboard. Paste into Discord to share with the community — submissions help RHI detect APIs and install paths correctly for more games.
- **GitHub API token support** — if you're hitting rate limits (OptiScaler NR staging not available, version checks not completing), create `%LocalAppData%\RHI\github_api.txt` and paste a GitHub personal access token on a single line. No scopes needed — a free token for public repos takes about 30 seconds to generate at github.com/settings/tokens. Raises the limit from 60 to 5000 API calls per hour.
- **Nexus Mods support for Luma** — Luma mods hosted on Nexus Mods (e.g. Mass Effect, Medal of Honor: Airborne, Borderlands 2) now show a "Get on Nexus Mods" button that takes you directly to the download page. Mods with both a GitHub and Nexus link (e.g. Prey, BioShock Remastered) now show both options. Full one-click Nexus install is wired but pending Nexus API approval.
- **dgVoodoo2 auto-detection** — RHI now automatically detects when a Luma mod requires dgVoodoo2 (for DX9 games like Mass Effect, Borderlands 2, Medal of Honor: Airborne) by reading the Luma wiki's Special Notes column, including which specific dgVoodoo2 version the mod recommends. No longer relies on a hardcoded list.

### Changes

- **PCGamingWiki API detection** — RHI now uses PCGamingWiki in the background to verify DirectX versions for games it can't scan directly (Xbox/Game Pass titles, access-denied paths). Halo Infinite, Resonance, and similar titles that previously showed no API badge now correctly show DX12.
- **PCGamingWiki config path detection** — RHI now scrapes the config file location from PCGamingWiki and uses it to place Engine.ini in the correct folder. Fixes UE-Extended installs for games that haven't been launched yet, where the config folder doesn't exist yet and RHI previously had to guess the project name.
- **Neural Rendering: Feeder and Bridge now support addon version pinning** — the Addon Version dropdown is now active for all four NR methods.
- **OptiScaler cog Upscaler API defaults to the game's detected API** — opens on DX12 for DX12 games instead of always defaulting to DX11.
- **More reliable OptiScaler and Neural Rendering install records** — RHI now writes a `rhi_install.txt` file to the game folder on every OptiScaler and Neural Rendering install, recording the exact version, variant, and file list that was deployed. This is used on the next launch to show the correct version (fixes nightly builds reverting to stable version numbers on restart), and on uninstall to know exactly which files to clean up regardless of whether the staging folder has since been updated.

### Bug Fixes

- Fixed OptiScaler uninstall leaving DLSS files behind when a NR method (DLSS5 Tool, ShortFuse, Feeder) was also installed — RHI now tracks which component owns each shared file and only removes a file when the last component that deployed it is uninstalled.
- Fixed `nvngx_dlss*.dll` files left in the game root (e.g. Mortal Shell II) after uninstalling DLSS5 Tool, when the game's actual DLSS lived in a plugin subfolder.
- Fixed OptiScaler DLL naming override not correctly tracking the renamed DLL, causing uninstall to leave files behind.
- Fixed MFG Ada Unlock being reinstalled on every Refresh after removing it via the Extras section.
- Fixed batch DLSS/Streamline deploy getting stuck when a game's install folder no longer exists.
- Fixed DLSS5 DX11 Bridge and MFG Ada Unlock re-downloading on every launch.
- Fixed shader pack folder occasionally being renamed to `reshade-shaders-original` during Refresh.
- Fixed `renodx-dlss5.addon64` reappearing in the addon picker after removing Neural Rendering.
- Fixed RHI database (rhi-repo) changes not picking up after a standard Refresh — previously required a full restart.
- Fixed the detail panel not refreshing for the currently selected game after a Refresh.
- Fixed UE-Extended game notes from the rhi-repo database not appearing in the RenoDX info dialog.
- Fixed pressing Backspace in the ReShade screenshot hotkey box being recorded as a hotkey — Backspace now clears the shortcut instead (matching ReShade's own behaviour). Thanks to @tzachbon for the contribution.
- Fixed "Apply to All Games" in Screenshots & Hotkeys doing nothing when the screenshot path is blank — hotkeys and effect list style now apply regardless. Thanks to @tzachbon for the contribution.
- Fixed UI freezing for a few seconds during OptiScaler downloads — progress and status message updates no longer trigger a full detail panel rebuild. Thanks to @Nypheena for the contribution.
- Fixed an empty `reshade-shaders-original` folder being created on clean ReShade installs — on a fresh game folder with no prior `reshade-shaders`, RHI was accidentally creating the folder and immediately renaming it as a backup. On uninstall this empty folder would be restored as `reshade-shaders`, leaving just the management marker with no shaders.
- Fixed Luma installs via drag-drop or the downloads watcher missing several post-install steps — ReShade, DLSS, dgVoodoo2, Engine.ini keys, and launch arguments are now all applied correctly regardless of how the Luma archive is installed.
- Fixed ReShade being deployed as `d3d9.dll` on DX9 games when dgVoodoo2 is also being installed — dgVoodoo2 needs `d3d9.dll` to intercept the game's DX9 calls, so ReShade now correctly installs as `dxgi.dll` instead, hooking dgVoodoo2's DX11 output.

### Manifest Updates

- Fixed Engine.ini being written to the wrong AppData folder for **Solasta 2** (was using the `Brimstone` exe subfolder name instead of the game name).
- Added **ReShade Screenshot Discord Fix** to the addon picker — strips the cICP colour chunk from HDR screenshots so Discord previews them correctly instead of showing washed-out colours.
- Kingdom Come: Deliverance II added to the 64-bit override list.
- Added **Ultra ReShade by Ultra+** to the shader picker — a single-shader "poor man's DLSS5" effect with bloom, contrast, haze/dehaze and saturation.
- Five Hearts Under One Roof added to the 64-bit override list.
- Mass Effect (2007) install path corrected to the `Binaries` subfolder.
- 007 First Light install path corrected to the `Retail` subfolder.

---

## v2.7.0

### New

- **OptiScaler DLSS NR variant** — new third option in the OptiScaler version picker alongside Stable and Nightly. Uses a community fork with DLSS 5 Neural Rendering built in. Same features as Nightly (Streamline, FG settings, presets, etc.) plus a new Neural Rendering Settings section in the cog to tune NR behaviour without touching config files. Switch between variants in the OptiScaler cog in the Extras section.
- **DLSS5 Feeder on 32-bit DX9 games** — the DLSS5 Feeder method in Neural Rendering now fully supports 32-bit DX9 games (Borderlands 2, The Witcher 2, and much more). RHI handles all the setup automatically — still one click.
- **Luma support for DX9 games** — Borderlands 2, Borderlands: The Pre-Sequel, The Witcher 2, Medal of Honor: Airborne, and Vanquish now work with Luma. RHI automatically handles the DX9 compatibility layer alongside the Luma install. Still one click.
- **Neural Rendering addon version picker** — the Neural Rendering section now has an Addon Version dropdown. You can pin a game to a specific version of DLSS5 Tool or DLSS Tool (ShortFuse) before installing — useful if a newer release causes issues with a particular game. Defaults to Latest and auto-updates as normal. The picker greys out when Neural Rendering is already installed; uninstall first to switch.
- **ReShade HDR Metadata** — new addon in the picker. Writes HDR metadata into the swapchain alongside ReShade, fixing washed-out or badly tone-mapped HDR output on TVs and monitors that rely on it to calibrate their HDR pipeline. Works with any DX11/DX12 game running ReShade. No configuration needed.

### Bug Fixes

- Fixed Lilium HDR Shaders (and other shader packs) not picking up new releases — updates were silently skipped if the pack was already on disk, even when a newer version was available.
- Fixed MFG Ada Unlock install button being permanently greyed out on fresh installs — it now downloads on demand when you click Install.
- Fixed games with manifest install path overrides (e.g. The Witcher 2) showing the wrong install path and missing mod status on first launch.
- Fixed ReBAR Size Limit writing and reading incorrect values — values set in RHI now show correctly in NVPI, and values set in NVPI are correctly read back by RHI.
- Fixed Output Colour Settings applying to the wrong monitor on multi-monitor setups.

---

## v2.6.9

### New

- **RTX 40 MFG Unlock v1.3** — updated to the new standalone format. No longer requires ASI Loader — it's a single DLL that you deploy under any proxy name the game loads (version.dll, dinput8.dll, etc.). RHI shows a name picker on install, and automatically cleans up any old ASI-based install on first launch.
- **20/30 FG Unlock** — new entry in the MFG Unlocks section. Brings DLSS Frame Generation to RTX 20 and 30 series GPUs (D3D12 games only, no ReShade or ASI Loader required). Pick your GPU generation (RTX 30 or RTX 20) in the cog before installing — RHI handles the rest and keeps it updated automatically.
- **Resolution & Colour Control** — automatically switch to a target resolution when a game launches and restore it on exit. Also includes Output Colour Settings to control colour depth and HDR dynamic range per-display, without touching NVIDIA Control Panel.
- **Standalone DLSS Enabler** — new row in the Extras section. Installs DLSS Enabler as a proxy DLL directly into any game folder, independent of OptiScaler. Updates automatically.
- **MFG Ada Unlock** — new row in the Extras section. Unlocks DLSS Multi Frame Generation (3x/4x+) on RTX 40-series GPUs. Requires ReShade. Mutually exclusive with RTX 40 MFG Unlock.

### Changes

- Changing a DLSS preset, render scale, or driver override in the NVIDIA Profile Overrides section no longer flashes or rebuilds the panel — the value is written immediately and the combo stays exactly as you set it.
- Extras section install buttons (ASI Loader, RTX 40 MFG, DLSS Enabler) now show the same blue installed style as the Components section when installed.
- Extras section is now split into groups with separators: ASI Loader at the top, MFG Unlocks (RTX 40 MFG, MFG Ada Unlock, 20/30 FG Unlock), and Other (OptiScaler, DLSS Enabler).
- Neural Rendering: added a note below the NR Cost Scaler toggle when the ShortFuse method is selected, explaining that Cost Scaler is now built into the addon.

### Maintenance

- RHI now tracks every DLL it deploys into game folders using a sentinel file. If the game already had a file at that location, the original is backed up and restored on uninstall. If there was nothing there, a 0-byte marker is written so RHI knows to clean up cleanly. This prevents game-original DLLs from being lost after uninstall, and fixes orphaned leftover files. Covers OptiScaler, DLSS version swaps, RE Framework, and Luma.
- Added internal RenoDX database service that fetches mod and UE-Extended configuration data from the RHI repository. Includes a dev-only "RenoDX Data Source" setting (Wiki Only / DB Only / Hybrid) for testing and validation ahead of a full transition away from wiki scraping.

### Bug Fixes

- Fixed DLSS Fix writing the wrong Streamline path in `reshade.ini` — it was pointing to the wrong file, which caused DLSS Fix to fail to hook Streamline correctly.
- Fixed DLSS Fix config not being written at all when Streamline files were added to a game after RHI had already scanned it (e.g. from a game update or another component install in the same session).
- Fixed the NR Cost Scaler toggle staying greyed out for an entire session after a fresh RHI install — it downloads in the background at startup but the toggle wasn't updating to reflect it, requiring an install+uninstall workaround to un-grey it.
- Fixed ASI Loader leaving its INI file behind on uninstall (e.g. uninstalling `winmm.dll` now also removes `winmm.ini`).
- Fixed a crash when clicking "Apply Peak Nits to All" or "Apply to All Games" in Settings when a game's folder no longer exists on disk.
- Fixed Admin Mode not being detected on certain system configurations — RHI now uses a more reliable check that correctly handles accounts with full admin rights even when the standard elevation check returns false.
- Fixed the Neural Rendering section showing ReShade as not installed immediately after installing it — you had to navigate away and back to see the correct status.
- Fixed RE Framework showing a stale build number on the card after an update — the version now syncs correctly on the next update check.
- Fixed NVIDIA Profile Overrides showing Default/Off on every launch until a manual Refresh — caused by a startup timing issue where profile reads would fail silently before NVAPI finished initialising.

### Manifest Updates

- Onimusha: Way of the Sword: added ultrawide fix link.
- Red Dead Redemption 2: matched to the RenoDX wiki entry (Vulkan mod).

---

## v2.6.8

### Bug Fixes

- Fixed the NVIDIA Profile Overrides section not applying preset and render scale changes — a stale background scan callback was overwriting the panel after a user change, discarding the new values.
- Fixed the driver settings section (VSync, ReBAR, Smooth Motion, etc.) not appearing after a Refresh.
- Fixed DLSS presets and render scale showing Default/Off instead of the actual driver values — reads now go directly to the live driver state instead of a stale in-memory cache.

### Manifest Updates

- Added DLSS SR, RR, and FG version 310.9.1.
- Added Streamline 2.14.1.

---

## v2.6.7

### Changes

- ReShade uninstall now preserves `reshade.log` in the game folder.
- ReShade config files are now deployed to game folders as `ReShade.ini` instead of `reshade.ini`.
- Neural Rendering DLL selection simplified to 310.8.2 — ShortFuse's modified build with support for all RTX GPUs (20/30/40/50 series).
- Added tooltips to the DLSS SR, RR, FG, and Streamline version dropdowns explaining that selecting a version copies it into the game folder, what Default and Custom do, and how NVIDIA Override works.

### Bug Fixes

- Fixed Custom render scale in DLSS & Streamline Defaults — selecting "Custom" now shows a text box so you can type in a specific percentage (33–100%).
- Fixed an intermittent UI freeze where the window would stay active (moveable, minimisable) but all buttons and controls stopped responding. This could happen when opening cogs, install dialogs, or other popups while a background dialog was already showing. Affects the Luma Settings cog, ASI Loader cog, ShortFuse settings, RTX 40 MFG cog, and install warning prompts.
- Fixed a freeze that could occur when an app update was found while the "Checking for updates…" progress dialog was open — the update dialog would silently block for up to 10 seconds.
- Fixed a brief freeze when RHI updated the taskbar jump list after launching a game or changing the Recent Games setting — the update now runs in the background.
- Fixed the Batch Deploy DLSS dialog occasionally leaving a ghost overlay that blocked all input after finishing quickly.
- Fixed a flicker where the entire Overrides panel disappeared and rebuilt itself when changing a DLSS version, preset, render scale, or driver override — now only the NVIDIA Profile section refreshes.
- Fixed a freeze that could occur when changing a DLSS version and clicking a section header at the same time.
- Fixed the NVIDIA Profile Overrides section briefly going blank when selecting a game or installing OptiScaler — the section now shows immediately using cached values and updates silently in the background.
- Fixed changing the Vulkan ReShade channel (Stable/Nightly/Custom) blocking the UI for up to 10 seconds while copying files to `C:\ProgramData\ReShade\`.
- Fixed the OptiScaler cog potentially rebuilding off the UI thread when switching between Stable and Nightly variants, or after applying a preset — could cause a freeze in certain timing conditions.
- Fixed OptiScaler install and uninstall progress not showing in the Extras section — the progress bar and status message now appear directly below the OptiScaler row where they belong. *(Thanks Sapphire)*
- Fixed installing or removing ASI Loader and RTX 40 MFG Unlock resetting the Extras panel scroll position.
- Fixed the drop helper window appearing as a separate entry in the taskbar and Alt+Tab switcher. *(Thanks Owen)*
- Fixed an intermittent UI freeze when rapidly clicking through games — the NVIDIA Profile section's background scans now run at low priority so they can't block pointer input, and the DLSS/driver rows are rebuilt atomically instead of element-by-element.

### Manifest Updates

- Grand Theft Auto V Enhanced: added a ReShade install warning about the DirectStorage incompatibility that causes "Unable to save configuration" errors, with a link to the DirectStorageFix and a note to use an older ReShade version as an alternative.
- Satisfactory: added Frame Generation setup instructions — the Engine.ini keys needed to enable DLSS FG in Satisfactory are now shown in the ReShade info button.

---

## v2.6.6

### Bug Fixes

- Fixed DLL naming override toggle becoming unresponsive after being toggled — any in-progress drag state is now cleared when the overrides panel rebuilds, and the toggle is always re-enabled via a `finally` block even if file operations throw.
- Fixed Recent Games Off setting not hiding games from the system tray right-click menu — all call sites now pass an empty list when the setting is off.
- Fixed Recent Games Off setting not clearing the taskbar jump list — jump list now uses `ICustomDestinationList` instead of `SHAddToRecentDocs`, giving RHI full control to clear it when the setting is toggled off.
- Fixed reinstalling ASI Loader with a different DLL name leaving the old DLL behind — the previous install is now cleaned up (and any Hooked backup restored) before deploying under the new name.
- Fixed ShortFuse DLSS Tool download failing with "file in use" when both DLSS5 variants download concurrently — each variant now uses its own temp filename.
- Fixed ShortFuse DLSS Tool zip extraction failing when the addon file inside the zip doesn't match the expected exact filename — falls back to any `.addon64` entry in the zip.
- Fixed OptiScaler install/uninstall status messages appearing in the Components section — suppressed there since OptiScaler is in the Extras section.

---

## v2.6.5

### Bug Fixes

- Fixed OptiScaler install and uninstall not refreshing the Extras section — the installed/uninstalled state now updates immediately without needing a manual refresh.
- Fixed DLL naming override toggle requiring a refresh to interact with after enabling or disabling — the overrides panel now rebuilds immediately after the toggle operation completes.
- Fixed RTX 40 MFG Unlock not writing the UAL ini file (`version.ini`, `dinput8.ini`, etc.) on install — without it, UAL didn't know to load `RTX40MFG.asi` and ReShade would stop working. RHI now writes the required `[GlobalSets]` keys to the matching UAL proxy ini file automatically.

---

## v2.6.4

### Changes

- DLSS Super Resolution, Ray Reconstruction, and Frame Generation version combos now include **NVIDIA Override** as a selectable option. Choosing it writes the driver's "Latest DLL" flag to the game's NVIDIA profile, equivalent to enabling "DLSS — Enable DLL Override" in Profile Inspector. Selecting any other version or clicking Restore DLSS/SL clears the override. NVIDIA Override is also available as a default in Configure Defaults (applies via Quick Apply) and as a selectable option in Batch Deploy.
- Added support for per-game custom Engine.ini files, hosted in the rhi-repo `engine-files/` folder and referenced by game name in the manifest. When set, RHI fetches and merges only the keys in that file instead of the standard HDR key set — allowing precise control over what gets written for games where the full set causes issues.

### Bug Fixes

- Fixed NR Cost Scaler and RTX 40 MFG Unlock not detecting new releases mid-session — Check for Update and the 4-hour timer now bypass the ETag cache and always fetch fresh release data.
- Fixed NR Cost Scaler and RTX 40 MFG Unlock not auto-deploying updated files to game folders after a new version is staged.

### Manifest Updates

- Black Myth: Wukong — custom Engine.ini file applied on UE-Extended install/update (writes only `r.HDR.EnableHDROutput=1` instead of the standard key set). Standard HDR and LUT controls in the UE-Extended cog are disabled for this game.

---

## v2.6.3

### New

- RTX 40 MFG Unlock added to the Extras section — enables DLSS Multi Frame Generation multipliers beyond 2x (up to 6x) on RTX 40 Series GPUs for games with Streamline FG support. Requires ASI Loader and ReShade installed first.
- DLSS NR Cost Scaler added to the Neural Rendering section — proxy for nvngx_dlssnr.dll that runs the neural model at reduced resolution (default 75%) for significant GPU savings while preserving native-resolution detail. Toggle on before installing an NR method to deploy it in one click.

### Changes

- OptiScaler moved from the Components section to the Extras section, alongside ASI Loader.
- RenoDX cog Compatibility Settings: Upgrade_ prefix stripped from format labels for readability (e.g. `R10G10B10A2_UNORM` instead of `Upgrade_R10G10B10A2_UNORM`).
- RenoDX cog dialog widened so Compatibility Settings labels no longer truncate.
- Installing a Neural Rendering method now automatically removes conflicting global addons (DLSS5 Tool, DLSS Tool (ShortFuse)) from the global set and cleans up their files immediately.
- Neural Rendering auto-select now defaults to ShortFuse (was DLSS5 Tool) for DX12 games with native DLSS.
- RTX 40 MFG Unlock and MFG Ada Unlock are mutually exclusive — installing one blocks the other.
- DLSS/Streamline version combo now shows `Default (x.x.x)` as a separate top entry rather than marking a version in the list — you can now select any version including the original without triggering a restore.
- Streamline version is now read from `sl.common.dll` instead of `sl.interposer.dll`.
- "ReShade Addons" renamed to "Global Addons" in the Shaders/Addons dropdown.
- Clicking "Select" in the per-game shader picker while already on Select now re-opens the picker (same behaviour as the addon picker).

### Bug Fixes

- Fixed NR Cost Scaler and RTX 40 MFG Unlock services not respecting the session-wide GitHub API rate limit flag — they now go through the shared ETag cache.
- Fixed RenoDX update check HEAD requests hanging the UI for up to 100 seconds when offline or rate-limited — now times out after 10 seconds.
- Fixed `renodx-dlss.addon64` not being removed when switching per-game addons to Off on games that ship with `nvngx_dlssnr.dll` natively (e.g. Cyberpunk 2077) — the stale-removal guard now correctly distinguishes RHI-placed NR DLLs from game-native ones.
- Fixed Neural Rendering method auto-select inferring DLSS5 Tool for games that have a backed-up NR DLL but no active install.

### Known Limitations

- DLSS5 Feeder: 32-bit game support not yet implemented.

### Manifest Updates

- METAL GEAR SOLID 4: Guns of the Patriots - Master Collection Version linked to Luma wiki entry.
- METAL GEAR SOLID 4: Guns of the Patriots — install warning added: switch to DX11 before installing Luma.
- Eternal Strands — UE-Extended compat entry added (HDR keys skipped, LUT only) + INI overrides.

---

## v2.6.2

### Changes

- Switching the Neural Rendering method now automatically removes any components installed by the previous method, giving a clean slate before installing the new one.
- ShortFuse ASI auto-config (ReShade rename + UAL install) is now opt-in rather than enabled by default.
- Detail view is now the default layout. All users are switched to it once on first launch of this version.

### Bug Fixes

- Fixed switching away from DLSS5 Feeder not removing the shaders it deployed (DLSS5_Feed.fx, lumenite_Kernel.fx).

---

## v2.6.1

### Changes

- Neural Rendering auto-select now defaults to ShortFuse (was DLSS5 Tool) for DX12 games with native DLSS.
- ReShade Settings cog: Overlay Key and Screenshot Key fields are now side by side.

### Bug Fixes

- Fixed UI hang when rapidly clicking through the game list — all synchronous NVAPI driver profile reads (DLSS presets, driver overrides, VSync/ReBAR/Smooth Motion) are now fetched off the UI thread.
- Fixed UI hang when rapidly changing DLSS/Streamline version dropdowns.
- Fixed toggling the DLL naming overrides switch hanging the UI.
- Fixed games using the D3D12 Agility SDK (e.g. Onimusha: Way of the Sword) being detected as DX11.
- Fixed Neural Rendering method incorrectly defaulting to DLSS5 Tool for games that have a backed-up NR DLL but nothing actively installed.
- Fixed DLSS5 Feeder deploying a 0-byte nvngx_dlss.dll when the cached file was unavailable.

### Manifest Updates

- Onimusha: Way of the Sword and PRAGMATA forced to DX12 detection.
- RoboCop: Rogue City — Unfinished Business Engine.ini config path corrected.

---

## v2.6.0

### New

- Detail view sections (Components, Game Overrides, Neural Rendering, Nvidia Profile Overrides, Management) are now collapsible. Click the section heading to toggle it open or closed. Collapsed state persists across restarts.
- Detail view sections can be reordered by dragging the ≡ handle on the left of each section header. Order persists across restarts.
- New Extras section in detail view. Contains Ultimate ASI Loader — install the UAL proxy DLL into any game folder to enable .asi plugin loading. Choose from the full list of supported DLL names (bitness-filtered, with Recommended badges and conflict warnings). Keeps itself up to date automatically. Hooked chaining handled automatically when the chosen DLL name is already in use by a game file.
- ShortFuse DLSS Tool now auto-configures ReShade for FrameGen on install: renames ReShade to Reshade64.asi, installs ASI Loader automatically (winmm → version → dinput8 priority), and writes HookStreamline=1 and HookDirectX=1 to reshade.ini. Controlled via the ⚙ cog next to the Neural Rendering install button — enabled by default, can be turned off per-game.

### Changes

- RenoDX renamed to RenoDX HDR in the detail view component list.
- Version number now shown next to MFG Ada Unlock, DLSS5 Feeder, and DX11 Bridge in the addon panel (same as DLSS5 Tool).
- ASI Loader status now appears in the ShortFuse Neural Rendering status line alongside ReShade and DLSS versions.

### Bug Fixes

- Fixed ShortFuse DLSS Tool addon (renodx-dlss.addon64) being removed as stale on every launch and Refresh for games where it was installed via the Neural Rendering section.
- Fixed Nvidia Profile Overrides section showing stale DLSS versions after removing a Neural Rendering method — now refreshes immediately without needing a manual Refresh.

### Manifest Updates

- Baldur's Gate 3 forced to 64-bit detection.
- Hogwarts Legacy linked to Marat's UE-Extended addon.

---

## v2.5.9

### Bug Fixes

- Fixed DLSS5 Feeder refresh wiping lumenite shader files — `SyncGameFolder` was deleting all managed shaders then only redeploying DLSS5_Feed.fx, losing lumenite_Kernel.fx. Fixed by persisting pack-level exclusions via SetExcludedFiles so refresh correctly deploys only the two needed files.
- Fixed MFG Ada Unlock, DLSS5 Feeder, and DX11 Bridge missing from the per-game addon picker.

---

## v2.5.8

### Changes

- Clicking the installed version number on a UE-Extended game now opens Marat's commit history for the UE-Extended addon.

### Bug Fixes

- Fixed OptiPatcher not deploying for NVIDIA users — it was incorrectly gated to AMD/Intel only.
- Fixed MFG Ada Unlock, DLSS5 Feeder, and DX11 Bridge disappearing from the global addon manager after being switched to API-based auto-updating.
- Fixed MFG Ada Unlock, DLSS5 Feeder, and DX11 Bridge not auto-updating — these addons use dynamic release filenames so RHI now resolves the download URL from the GitHub releases API rather than a hardcoded URL.
- Fixed addon update check running before the manifest was applied, causing manifest-driven addons to be silently skipped on every startup.
- Fixed normal Refresh not re-checking games previously confirmed as "no DLSS" — newly installed DLSS (e.g. a game update that adds frame generation) now shows up on a standard Refresh instead of requiring a Full Refresh., causing manifest-driven addons to be silently skipped on every startup.

---

## v2.5.7

### Bug Fixes

- Fixed DLSS5 Feeder failing to download for some users — the zip filename changes with each release so the hardcoded URL broke on updates. RHI now resolves the download URL dynamically from the GitHub releases API so Feeder auto-updates correctly going forward.
- Fixed DOF Fix install failing — the releases API was returning only the first 30 results by default, pushing DOF Fix releases off the page as the repo grew. Now uses per_page=100.

---

## v2.5.6

### Bug Fixes

- Fixed `renodx-dlss5.addon64` deployed by the Neural Rendering section being removed on the next Refresh — the addon cleanup pass was treating it as stale since it wasn't deployed through the standard addon system.

---

## v2.5.5


### New

- **Neural Rendering section** — a dedicated self-contained section in the game detail panel (between Game Overrides and NVIDIA Profile Overrides) for installing DLSS 5 Neural Rendering. No addon picker required. Method combo with four options:
  - **DLSS5 Tool** — for native DLSS games. Deploys `renodx-dlss5.addon64`, upgrades DLSS SR/RR/FG to latest, and deploys `nvngx_dlssnr.dll`.
  - **DLSS5 Tool + DX11 Bridge** — for DX11/Vulkan native-DLSS games. Same as above plus `dlss5-bridge.addon64` (always downloads latest).
  - **DLSS Tool (ShortFuse)** — alternative for any 64-bit native-DLSS game. Deploys the full DLSS SR/RR/FG/NR stack and Streamline via the sentinel pattern.
  - **DLSS5 Feeder** — default for games with no native DLSS (DX11, DX12, Vulkan, OpenGL, 32-bit). Deploys the Feeder addon, DLSS5 Tool as neural consumer, `nvngx_dlss.dll`, `nvngx_dlssnr.dll`, and the required shaders (`DLSS5_Feed.fx` + LumeniteFX motion vectors) automatically. Writes a `ReShadePreset.ini` with both techniques pre-enabled in the correct render order.
  - ReShade is installed automatically if not already present.
  - NR DLL version picker, per-file status indicators with versions, Install/Reinstall/Remove buttons, automatic method detection for existing installs, and descriptions with links for each method.

### Manifest Updates

- Added a note to Ori and the Blind Forest: Definitive Edition warning that the generic Unity mod may have visual issues and the named mod is deprecated.
- Added install path override for The Witcher 3: Wild Hunt - Complete Edition (`bin\x64_dx12`), engine hint (REDengine), and graphics API override (DX12).
- Fixed Outlast detecting as 32-bit and resolving to the wrong path — now forced 64-bit with `Binaries\Win64` path override and engine hint set to Unreal (Legacy).
- Fixed DLSS5 DX11 Bridge download URL — old repo was deleted; updated to `NIGos/dlss5-bridge` with correct filename `dlss5-bridge.addon64`.

---

## v2.5.4

### Changes

- Clicking "Check For Updates" now also triggers a silent auto-install pass immediately after the check completes, so any updates found are installed without needing a separate "Update All" click (when Automatic Updates is enabled).
- Renamed "Export Profiles" / "Import Profiles" buttons in Settings to "Backup Profiles" / "Restore Profiles" for clarity.
- ReBAR Enable now has three options: Auto (Default), Off, and On — reflecting the new driver setting (0x000BFA21). Previously only Off and On were available. Both the global Settings page and per-game overrides panel are updated.

### Bug Fixes

- Fixed `nvngx_dlssnr.dll` not being removed from the game folder when uninstalling DLSS5 Tool. RHI now uses a sentinel file to track whether it placed the DLL, so it only removes what it deployed.
- Fixed Automatic Updates setting reverting to Yes on restart when set to No.
- Fixed addon downloads aborting entirely when one URL (e.g. the 32-bit variant) returns a 404 — remaining URLs now continue independently.
- Fixed per-game addon selection being lost when switching the addon mode to Global and back.
- Fixed pre-selected addons not re-downloading on launch if their staging files were missing.

---

## v2.5.3

### Bug Fixes

- Fixed `RenoDX DLSS5.addon64` still being deployed to game folders after v2.5.2. Per-game addon selections stored in `settings.json` still referenced the old name (`RenoDX DLSS5`) — these are now migrated to `DLSS5 Tool` on load. This is separate from the global addon list and stale file fixes in v2.5.2.

---

## v2.5.2

### Bug Fixes

- Fixed `RenoDX DLSS5.addon64` being deployed to game folders on every launch due to a stale file left over from renaming the addon to DLSS5 Tool. RHI now removes it automatically on startup and cleans it up from all affected game folders, including per-game addon selections that still referenced the old name.
- Fixed DLSS5 Tool and DLSS Tool (ShortFuse) being deployed as `.addon32` on 32-bit games, causing a ReShade load error. Both addons now always deploy as `.addon64`.

---

## v2.5.1

### Bug Fixes

- Fixed DLSS5 Tool addon not deploying to game folders after being selected. The internal package name change from "RenoDX DLSS5" to "DLSS5 Tool" was not reflected in all deploy paths.
- Fixed stale `RenoDX DLSS5.addon64` file from the pre-rename version being re-deployed to games on every startup. RHI now removes it automatically on launch.
- Fixed co-deployed DLSS and Streamline files not being cleaned up when switching away from DLSS Tool (ShortFuse). Files RHI placed are now fully restored or removed on deselect.
- Fixed mutual exclusivity between DLSS5 Tool and DLSS Tool (ShortFuse) — selecting one now greys out the other in the addon picker.

---

## v2.5.0

### New

- Added a search bar to the shader pack picker — filter by pack name or individual shader filename.
- **DLSS Tool (ShortFuse)** — ShortFuse's DLSS5 addon is now in the addon picker as a second option alongside DLSS5 Tool. Supports DX12, DX11 and DX9 with HDR scaling. On install, RHI automatically downloads and deploys the newest DLSS SR, RR, FG, NR and Streamline files to the game folder. Supports RTX 20-50 Series. Still WIP — fall back to DLSS5 Tool if you have issues.
- **Updated nvngx_dlssnr.dll** to ShortFuse's latest build, now supporting RTX 20, 30, 40 and 50 Series GPUs with identical performance to the original NVIDIA build on RTX 50 Series.

### Changes

- Moved the Neural Rendering column to the far right of the Nvidia Profile section, after Streamline.
- Renamed RenoDX DLSS5 addon to DLSS5 Tool. The current version is now shown next to the name in the addon picker.

---

## v2.4.9

### New

- **nvngx_dlssnr.dll 310.8.SF** — a modified Neural Rendering DLL by ShortFuse that extends support to RTX 20, 30, 40 and 50 Series GPUs. This is now the default version RHI deploys. Shown as `310.8.1` in Windows Explorer, `310.8.SF` in RHI.

### Changes

- The Neural Rendering Deploy DLL button now also deploys `nvngx_dlss.dll` to the game folder alongside `nvngx_dlssnr.dll`. Any existing `nvngx_dlss.dll` is backed up as `.original` first.
- Added an MOTD button to the status bar next to Patch Notes — click it to re-read the current message at any time.

### Manifest Updates

- Added Reshade Motion Estimation by JakobPCoder to the shader pack library — dense real-time optical flow motion estimation.

---

## v2.4.8

### Bug Fixes

- Fixed "How to use" link not appearing in the per-game addon picker.
- Fixed `renodx-dlss5.addon64` triggering an install prompt when double-clicked or drag-dropped. It is managed by RHI internally and should only be installed via the addon picker or placed in the Custom Addons folder.

### Manifest Updates

- Added DLSS5 DX11 Bridge and DLSS5 Feeder to the addon picker — both enable DLSS 5 Neural Rendering in D3D11 games. Additional setup steps are required; the How To Use button on each addon links to the repo for instructions.
- Added DLSS5 Feeder companion shader to the shader pack library.
- Fixed Metal Gear Solid 4 (Master Collection) showing as Unreal Engine — now correctly shows MGS4 Engine.

---

## v2.4.7

### Bug Fixes

- Fixed the Neural Rendering column not showing `nvngx_dlssnr.dll` as installed after deploying it. It now updates immediately without needing a Refresh.
- The Neural Rendering column now clearly shows "Custom" when a custom DLL is active.

---

## v2.4.6

### Bug Fixes

- Fixed RenoDX DLSS5 not auto-updating to games when a new version is released. The addon now deploys the updated file directly from its own staging folder and no longer creates a redundant copy in the addons folder.

### Manifest Updates

- Added CubeLUT3Ddith by aron7awol to the shader pack library — Cube 3D LUT shader with dithering to reduce banding.

---

## v2.4.5

### Bug Fixes

- Fixed RenoDX DLSS5 not deploying to game folders after the addons staging folder was deleted. The addon now deploys directly from its own staging location.

---

## v2.4.4

### New

- **RenoDX DLSS5 addon** — `renodx-dlss5.addon64` is now a first-class addon in the per-game addon picker, listed above RenoDX Upgrade. Enable it per game from the Addons combo → Select. RHI downloads it automatically, keeps it updated silently alongside other components, and deploys `nvngx_dlssnr.dll` to the game folder alongside it if not already present. For 50 Series GPUs only.
