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
- `game-db/seed_from_manifest.py` reads `manifest.json` and keeps working unchanged.
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

### Steam Achievements in Spotlight
Show the selected Steam game's achievements on the Spotlight page: progress, recent unlocks, and the full list in the side panel. No API key, sign-in or public profile needed, because the data comes from Steam's local cache.

**Data sources (verified on a real install):**
- Achievement list: `<SteamPath>\appcache\stats\UserGameStatsSchema_<appid>.bin`. Binary KeyValues: `stats` → `<block>` → `bits` → `<bit>` → `name`, `display` → `name`/`desc` (per language), `hidden`, `icon`, `icon_gray`.
- Unlock state: `<SteamPath>\appcache\stats\UserGameStats_<accountid>_<appid>.bin`. `cache` → `<block>` → `data` (unlocked bitfield) + `AchievementTimes` → `<bit>`: unix time.
- Account id = SteamID64 − 76561197960265728, SteamID64 from `<SteamPath>\config\loginusers.vdf` (`MostRecent` user). SteamPath from `HKCU\Software\Valve\Steam\SteamPath`.
- Binary KeyValues types: 0 subtree, 1 string, 2/4/6 int32, 3 float, 7 uint64, 8/11 end.
- Icons: `https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appid}/{hash}.jpg`.
- Optional rarity: `ISteamUserStats/GetGlobalAchievementPercentagesForApp` (no key), cached for a day.

**What needs building:**
- `SteamAchievementService`: KeyValues binary reader, account lookup, maps block + bit to unlocked/time, picks the Steam display language with English fallback, caches results and re-reads only when a file changes.
- Icons downloaded in the background into the artwork cache (`artwork\{appid}\ach\`), only while Spotlight is on, like covers.
- Spotlight: an Achievements tile under "Tune this game" ("31 / 78 · 40%", progress bar, last few unlocked icons); clicking it opens the side panel with the full list (unlocked newest first, then locked greyed out; hidden locked ones masked as "Hidden achievement").
- Refresh when the game exits (running-game tracking) and on selection.
- AppID via `DetailPanelBuilder.ResolveCommunityAppId`; Steam games only.

**Limitations:** only covers games run on this PC (or whose achievements were opened in Steam); undocumented format (hide the tile if a file doesn't parse); updates only when Steam writes the cache.

**Rejected:** Steam Web API `GetPlayerAchievements` (needs a per-user key and a public game-details profile); scraping community XML (fragile); Steamworks SDK (not viable).

**Open questions:** Spotlight only, or also an At a glance tile in Tabs? Include rarity? Mask hidden achievements until unlocked?

**Size:** medium, about 600–800 lines, no new libraries.

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
