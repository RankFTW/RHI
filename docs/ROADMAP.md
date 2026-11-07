# RHI Roadmap

Ideas, technical debt, and planned improvements. Categorised for easy reference.

---

## ✅ Completed

### Phase 1 — Reduce Surface Area
- **Remove Grid View** — deleted CardBuilder, OverridesFlyoutBuilder, ViewLayout.Grid. -5000 lines.
- **Split mega-files** — 6 files split into 26 partials. All under 50KB.
- **Unify dual UI builders** — OverridesFlyoutBuilder deleted (was Grid-only dead code). Only DetailPanelBuilder remains.

### Phase 2 — Structural Improvements (Partial)
- **NVAPI abstraction** — DlssPresetService split into 6 partials (max 36KB). ProfileMatching, DriverSettings, ReBar, Export, Reset separated.
- **Reduce ViewModel surface** — All consumers use direct injection. 14 forwarding properties deleted. Only 3 remain (ShaderPack, AddonPack, GameName).

### Features
- **Custom ReShade auto-redeploy** — `CustomReShadeHashService` hashes the DLLs in the Custom folder (`custom_reshade_hashes.json`) and redeploys changed ones to every game on the Custom channel (Vulkan via the elevated global layer copy). Runs on launch, Refresh and the 4-hour background check.
- **Selectable layouts (v2.9.0)** — Classic / Classic+ / Tabs / Spotlight, switched from the top bar. Spotlight uses Steam artwork, with custom artwork per game. Plan and decisions: [layouts-plan-2.9.0.md](layouts-plan-2.9.0.md).
- **Steam achievements in Spotlight (v2.9.2)**: opt-in (Settings › Achievements). Read from Steam's local cache (`appcache\stats\UserGameStatsSchema_<appid>.bin` + `UserGameStats_<account>_<appid>.bin`, binary KeyValues) by `SteamAchievementService`; rarity from the keyless `GetGlobalAchievementPercentagesForApp` (cached a day); icons cached in `artwork\{appid}\ach\`. Hero strip under Launch / HDR / Resolution, full list in the side panel. Rejected: Steam Web API (per-user key), community XML (needs a public profile, fragile). **Xbox** (same version): own toggle; read from the Xbox app's `AsyncCache.db` (SQLite, via Windows' `winsqlite3.dll`, no package) by `XboxAchievementService`; card → PFN (from the AUMID) → Store product id → title id, name match as fallback; rarity, Gamerscore and progress come with the list. Next: Ubisoft (local cache), then GOG / Epic / EA (need sign-in); see `reports/Achievements beyond Steam.md`.

---

## 🔧 Engineering Debt

Items that improve maintainability, performance, or reliability. No user-facing changes.

### Concurrency Model
- Introduce `BackgroundTaskCoordinator` — serializes operations on shared resources (library saves, staging downloads, card updates, panel rebuilds)
- Establish threading contract: services on background threads → marshal results to UI via single `DispatcherQueue.TryEnqueue` point
- Replace individual guard flags (`comboInitializing`, `_suppressSelectionChanged`) with a `PanelState` enum (Building / Interactive / Rebuilding)
- Move shader pack version tracking out of `settings.json` into dedicated `shader_pack_versions.json` (fixes startup file contention)

### Settings Modernization
- Replace flat `Dictionary<string, string>` with structured `PerGameSettings` class
- Single serialization point instead of manual dict/hashset pattern
- Migration system for settings format changes
- Eliminates MigrateDict/MigrateHashSet in `RenameGame()`

### Data-Driven Component System
- `IGameComponent` interface: Detect, Install, Uninstall, CheckForUpdate, Update
- Components register in DI, detail panel iterates them dynamically
- Eliminates "add to 11 files" pattern for new components

### Structured Error Handling
- `OperationResult` type instead of try/catch + `ActionMessage = "❌ ..."`
- Centralized `ErrorDialogService.ShowAsync(result, retryAction?)`
- Service methods return results, callers decide how to surface

### Incremental Panel Updates
- Rebuild only the changed section instead of full `BuildOverridesPanel()`
- Bind version labels, status dots, enabled states to observable properties
- Structural changes (add/remove rows) stay imperative

### Split manifest.json into topic files
`manifest.json` is ~79 KB / 75 fields in one file, which is cumbersome to edit. Plan (agreed Nov 2026, not started):

- **Source of truth becomes `manifest/`**, ~9 topic files (largest ~13 KB), following the file's existing `_comment_` sections:
  | File | Fields |
  |---|---|
  | `core.json` | version, featureFlags, componentUrls, rtxHdrInfoUrl, pcgwUseAppId, pcgwUrlCacheVersion |
  | `detection.json` | blacklistPrefixes, blacklist, wikiUnlinks, wikiNameOverrides, lumaNameOverrides, installPathOverrides, splitGames, emulatorGames, wikiStatusOverrides |
  | `engine.json` | engineHintOverrides, engineIniPathOverrides, graphicsApiOverrides, thirtyTwoBitGames, sixtyFourBitGames, dlssSkipGames, pdUpscalerGames, dlssPathOverrides, streamlinePathOverrides |
  | `hdr-luma.json` | nativeHdrGames, ueExtendedCompatibility, ueExtendedGames, lumaRenodxCompat, lumaRequiresDgVoodoo, dgVoodooVersions, dofFixForceGames |
  | `install.json` | installWarnings, forceExternalOnly, snapshotOverrides, dllNameOverrides, gacSymlinkGames, launchExeOverrides, renodxIniOverrides, engineIniFiles, legacyReShadeVersions, legacyReShadeAvailable, dxvkBlacklist |
  | `notes.json` | gameNotes, reshadeGameInfo, lumaGameNotes, dxvkGameNotes |
  | `links.json` | authorDisplayNames, donationUrls, authorRoles, authorOverrides, nexusUrlOverrides, pcgwUrlOverrides, uwFixUrlOverrides, ultraPlusUrlOverrides, optiScalerWikiNames |
  | `nvidia.json` | profileNameOverrides, dlssPresets, dlssPresetsDev |
  | `packs.json` | shaderPacks, addonPacks, renodxExtraSettings |
- **The app keeps downloading the single `manifest.json`**, now generated from the parts. Reasons: every released RHI fetches `manifest.json` and must keep working; one GitHub API call per fetch instead of nine (60/hour limit without a token); no app code changes (cache, `Normalize()`, both init paths untouched).
- **Work needed:**
  1. One-time split script: cut today's file into the parts; verify that joining them gives identical data.
  2. Build script (`tools/build-manifest.py`): join parts → `manifest.json` in a fixed field order, CRLF, add a `_generated` note ("edit manifest/ instead"); fail if a field is in two parts or none.
  3. `tools/check-manifest.py`: also check each part, and fail if `manifest.json` doesn't match the parts. Add `manifest/**` to `.github/workflows/manifest-check.yml` paths.
  4. Manifest Editor (`tools/RHI-ManifestEditor`, `Json/JsonDoc.cs`): open one `JsonDoc` per part and route each field's edits to the part that holds it; saving stays text-preserving.
  5. Docs: "Push manifest" workflow in CLAUDE.md (edit part → build → push `manifest/` + `manifest.json` together), `.claude/rules/rhi-manifest.md` + `.kiro` copy, `docs/manifest-field-reference.md`.
- `tools/game-db/seed_from_manifest.py` reads `manifest.json` and keeps working unchanged.
- **Optional cleanup:** `_engineOverrides` (~5 KB) is switched-off data ("engine names now sourced from pcgw_data.json") and could be removed.
- **Rejected:** one file per game (hundreds of files; edits happen by topic), and having the app fetch the parts itself (more API calls, older versions still need `manifest.json`).

### Test Infrastructure
- Run: `dotnet test RenoDXCommander.Tests/RenoDXCommander.Tests.csproj -p:Platform=x64` (about 99 tests, xUnit + FsCheck)
- Add integration tests: install/uninstall roundtrip, manifest parse → card assertions, settings save/load
- CI: only the manifest check runs on push (`.github/workflows/manifest-check.yml`). Add `dotnet build && dotnet test` on push

---

## 🚀 Feature Ideas

User-requested features and enhancements. Not committed — just captured.

### Steam Full Library Integration
Show the user's complete Steam catalog (owned, family shared, not just installed) to browse compatibility before downloading.

- Auto-read SteamID from `loginusers.vdf`
- User provides Steam Web API key in Settings
- `GetOwnedGames` returns all owned games with app names
- Non-installed games show as greyed cards with compatibility badges
- Filter chip: "Not Installed"
- "Install" links to `steam://install/{appId}`

**Source:** Discord user request. Similar to SteamDB/SteamDD.

### One-click updates (Velopack)
Update like SCSKiller: download in the background, click **Restart to update**, done in seconds with no installer window or UAC prompt. Uses Velopack (per-user install in `%LocalAppData%\RHI.App\`, `Update.exe` swaps the app folder and restarts).

- Full plan: `docs/velopack-updates-plan.md` (install location, channels, release steps, migration from the Inno install, testing).
- **Decide first:** Admin Mode would run a user-writable exe elevated; plan option (a) is a small admin-owned launcher that checks RHI is genuine.
- **Pitfall:** the old Inno uninstaller deletes `%LocalAppData%\RHI` (user data), so the migration must not run it as-is.
- Pairs well with the .NET 10 move (on hold).

**Source:** user request, Nov 2026.

### Store-Qualified installPathOverrides
Games on both Steam and Xbox with different subfolder structures (`Win64` vs `WinGDK`).

- Pipe-separated paths (try both, use whichever exists) — consistent with `engineIniPathOverrides`
- Or store-qualified dict: `{ "Steam": "..\\Win64", "Xbox": "..\\WinGDK" }` — needs migration

**Trigger:** When a game has the same name from both stores but different subfolder layouts.

### NVAPI Driver Version Gating
- Check driver version before enabling settings (MFG needs 572.16+, render scale needs 565+)
- Currently the UI silently fails on older drivers
- Show "Requires driver X.XX+" tooltip when disabled

---

## 🐛 Known Issues / To Fix

Confirmed bugs or UX problems with a known root cause.

### UI freeze after selecting games with a full DLSS profile
**Symptom:** The UI stops responding after selecting a game with DLSS SR+RR+FG+Streamline installed (e.g. Control, Ninja Gaiden 2 Black). The last logged action is usually `BuildDriverProfileSectionWithData:AddToTree(GameName)`.

**What's known:** Freeze captures show the UI thread idle (CPU 0) with the message pump alive, while queued dispatcher work never runs. It is not a slow layout pass, so the earlier idea of replacing the Driver Settings grid's star columns is not the fix. Root cause still unknown.

**In place:** guards against stale or doubled panel builds, NVAPI reads moved off the UI thread, a 5-second lock timeout, freeze detection with diagnostics (native stack, ClrMD, dump with `unlock.txt`), and automatic restart that reselects the game.

**Next steps and full history:** "UI Freeze Investigation" in [rhi-knowledge.md](../.kiro/steering/rhi-knowledge.md).

---

### GitHub issues investigated (Nov 2026), not yet fixed
Root causes found; each is a small change unless noted.

- **#147 RHI doesn't open (process runs, no window).** If the first instance hangs during startup, every later launch hands off to it over the single-instance pipe and exits silently (`SingleInstanceService.SendToRunningInstance` swallows failures; the listener only starts after `Activate()`, App.xaml.cs ~391; the second instance deletes its own log). Fixes: start the pipe listener right after `TryAcquire`; on failed hand-off, keep the log and offer "RHI is already running but not responding — end it?"; startup watchdog that logs a stack if the window isn't up after ~20 s; re-add the tray icon on `TaskbarCreated`; check the Admin Mode task's `/TR` path still matches the exe.
- **#133 DLSS5 Tool NR DLL recorded but not copied.** `DeployNrDllSentinel` (DetailPanelBuilder.NeuralRendering.cs ~154) returns when `nvngx_dlssnr.dll.original` exists without checking the DLL is there; the shared-file owner is added anyway. Fix: use `DeployWithSentinel` like SR/RR/FG (also applies the chosen NR version) and only record the owner once the file exists. Same pattern in the Feeder deploy.
- **#138 Engine.ini written to the other store's folder.** `AuxInstallService.FindPlatformConfigDir` always prefers `WinGDK` over `Windows`, ignoring the store. Fix: Xbox prefers WinGDK, other stores Windows; for pipe-separated overrides pick the store-matching candidate. Update the "Platform priority" line in rhi-install.md.
- **#62 Uninstalled games stay listed.** Both merge paths (BackgroundScan.cs ~344, Init.cs ~661) keep every cached game the scan didn't find (meant for offline drives). Fix: keep only when the drive root is missing; drop when the drive is there but the folder is gone. Skip UNC paths (slow when unreachable).
- **#105 Installed filter is ReShade-only.** FilterViewModel.cs ~330. Fix: `IsAnyComponentInstalled` (ReShade, RenoDX, Luma, OptiScaler, ReLimiter/DC, RE Framework, DXVK, DOF Fix); keep the "N ReShade" counter as a separate count.
- **#159 Phantom Blade Zero.** The real exe is `platform\steam\PBZ.exe`; OptiScaler loads ReShade64.dll from the exe folder, but RHI installs to `ProjectPBZ\Binaries\Win64`. Fix (manifest): `installPathOverrides` "Phantom Blade Zero": `"platform\steam|ProjectPBZ\Binaries\Win64"`; check the Engine.ini project name still resolves; existing installs need a reinstall.
- **#139 Euro Truck Simulator 2.** RTX Encore needs to be installed as `winmm.dll` there (rtx-encore v1.0.0-rc.3). Check for a manifest field for the default RTX Encore DLL name, else add one.
- **#137 / #85 antivirus, remaining work.** Download only the needed bitness of DXVK; apply the "only when used" startup rule to DOF Fix, NR Cost Scaler and 20/30 FG Unlock too. **Smart App Control** blocking RHI.exe can only be fixed by Authenticode-signing RHI.exe and the installer (needs a certificate).

---

### Slow first launch after install/update (Windows Defender scanning)
**Symptom:** App takes 1–2 minutes to show the window on first launch after a fresh install or update. Black window, then eventually loads. Subsequent launches are instant.

**Root cause:** `PublishSingleFile=true` causes .NET to extract bundled assemblies to `%TEMP%\.net\RHI\` on first run. Windows Defender scans every extracted DLL before .NET can load them, blocking `InitializeComponent` on the UI thread for the duration. Confirmed via session logs: the gap is always between `NexusUpdateService.LoadBaselines` and `InitializeComponent complete`, and only affects the first 1–2 sessions after install. Installing to `C:\Program Files\` (elevated Inno Setup install) triggers stricter Defender scrutiny than user folders.

**Fix:** Add a Windows Defender exclusion for the install folder in the Inno Setup `[Run]` section:
```
Filename: "powershell.exe"; Parameters: "-Command ""Add-MpPreference -ExclusionPath 'C:\Program Files\ReShade HDR Installer'""""; Flags: runhidden
```

---

## 💡 Nice-to-Have

Low priority items with no current demand.

- **Localization** — WinUI `.resw` support. Not urgent for target audience.
- **Accessibility audit** — Screen reader support for code-behind UI elements.
- **Plugin system** — Third-party components register without app changes.
- **Telemetry** — Usage analytics. Privacy-sensitive, opt-in only.
- **Controller: Steam Input doubling (maybe)** — with Steam running, Steam Input's desktop layout can turn a PlayStation pad (or an Xbox pad with Steam Input on) into mouse + keyboard, so RHI may get each press twice (XInput + Steam's fake Enter / mouse). Test with Steam open; if it doubles, note it in the Controller support tooltip or ignore keyboard / mouse input while the controller is in use.
