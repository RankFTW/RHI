# RHI Code Health Plan

From the full code audit on 26 Oct 2026: five reviews covering dead code, structure, background work, persistence and tooling. The main claims were checked in the code. Line numbers and counts are as of commit `b8094e6` and will drift.

Tick items off as they land. Add the commit hash next to an item when it's done.

**Status key:** `[ ]` to do · `[x]` done · `[~]` in progress · `[-]` decided against (add the reason)

---

## Phase 1: Now (small, high value)

### 1.1 Settings and record-file safety

- [x] **A corrupt `settings.json` wipes every setting.** (Checked.)
  - **What happens:**
    - A half-written file (crash, antivirus lock, full disk) loads as an empty dictionary.
    - `App.xaml.cs` treats `Count == 0` as a first launch, shows the SetupWindow and overwrites the file.
    - Every override, rename and exclusion is lost.
  - **Fix:**
    - On a parse failure, move the bad file to `settings.json.corrupt-<timestamp>` and restore from `settings.json.bak`.
    - Never treat a parse failure as a first launch; only a missing file counts as one.
    - Write atomically: write `settings.json.tmp`, then `File.Replace` with a `.bak`.
- [x] **Record stores can lose entries.** Done: `installed`, `aux_installed`, `luma_installed`, `reframework_installed` and `dxvk_installed` use `SafeJsonFile`, with load-modify-save under `SafeJsonFile.LockFor(path)`.
  - **Files:** `installed.json`, `aux_installed.json` and the other `*InstalledRecord` stores.
  - **What happens:**
    - Writes aren't atomic and there's no lock.
    - A corrupt file returns `new()`, which silently drops every record. RHI then loses update detection and uninstall can't restore files.
  - **Fix:** the same atomic write, `.bak` and quarantine pattern, plus a lock per store.
- [x] **Settings timer save can silently drop changes.**
  - **What happens:**
    - `SaveNameMappings` is debounced 250 ms. The timer save enumerates collections without a lock while the UI changes them.
    - A "collection modified" exception is logged and the save is lost.
  - **Fix:** snapshot the collections under a lock on the UI side, then serialize the snapshot.
  - **Also:** the 250 ms debounce goes against the CLAUDE.md "no deferred saves" rule. Decided: keep the debounce; the rule was updated.
  - **Done:** all settings.json access goes through `SettingsFile` / `SafeJsonFile` (one lock, .bak, quarantine). The save retries on "collection modified", and saves are blocked if the file couldn't be read at startup.
- [x] **`ShaderPackService` and Lilium write `settings.json` without the shared lock.** Concurrent saves lose updates. Route them through the same save path.

### 1.2 Small bugs found in the audit

- [x] **Renaming a game loses its Luma TAA setting.** `_lumaTaaEnabled` is not migrated in `GameNameService.RenameGame`. (Checked.)
- [x] **Cleared overrides come back on the next launch.**
  - `OsStreamlineVersion`, `OsNrRuntime` and `UalInstalledAs` have no else-remove in save (`GameNameService.cs` ~934–936). Clearing the last entry doesn't remove the key, so the old value reloads. (Checked.)
- [x] **The two ASI Loader bitnesses share one update flag.** Done; also found that updates never reached games at all. Games are now synced by comparing their copy with staging.
  - UAL 32-bit and 64-bit share one `HasUpdate`.
  - The 64-bit copy re-downloads every launch, and the 32-bit copy never updates.
  - Track them separately.
- [x] **No-op ternary:** `? 6 : 6` at `NvidiaProfile.cs:662`. Harmless (the column is 6 either way since NR is always shown); simplified to `6`.

### 1.3 Repository and manifest hygiene

- [x] **Put `.kiro` in git.** Scanned for secrets (none found) and un-ignored. It's gitignored today, so `rhi-knowledge.md` (232 KB) and all 36 specs exist only on the G: drive. Remove it from `.gitignore` and commit.
- [x] **Fix duplicate and case-colliding keys in `manifest.json`.** Only the RDR2 exact duplicate was a problem and it has been removed. The two case pairs are **intentional**: those fields are looked up exactly, so both spellings are needed.
  - `engineHintOverrides`: the two Dragon Quest VII spellings (~lines 547/548).
  - `pdUpscalerGames`: the two Resident Evil 7 spellings (~686/687).
  - `wikiNameOverrides`: an exact duplicate of Red Dead Redemption 2 (~128/165).
  - These are harmless today only because those sections aren't in the `Normalize()` case check. If they were added, the manifest would fail for every user.
- [x] **Add a manifest check** on every push. Done: `tools/check-manifest.py` plus `.github/workflows/manifest-check.yml`. It reads the case-insensitive field list from `Normalize()`.
  - Checks for both `manifest.json` and `dlss_manifest.json`:
    - valid JSON
    - no duplicate keys
    - no keys that differ only by case
  - This can be a Python script plus a GitHub Action, and can also run locally before "push manifest".
- [x] `publish.bat` is listed in `.gitignore` but is tracked. Remove it from `.gitignore`.
- [x] Remove the stale CLAUDE.md "known issue" about the test project pinning WindowsAppSDK 1.6 (NU1605). It's fixed now.

---

## Phase 2: Next (background speed and dead code)

### 2.1 Startup and background work

Measured from logs on a 102-game library.

- [x] **Cache "Unknown" engine results.** Done: reused while an `EngineFolderStamp` fingerprint (folder mtimes, two levels deep) matches. Measured: Unknown re-scans were ~1.6–2.7 s warm and the main cause of the ~11 s cold-launch gap (other games queue behind them in the parallel loop). The log line `[BuildCards] Engine detection: …` shows the per-launch time. Also: when PCGW names an engine other than Unreal/Unity/RE Engine, only the exe folder is looked up (`DetectExeFolder`). Follow-up: The Sinking City Remastered is UE5 per PCGW but detects as Unknown.
  - `BuildCards` takes about 13–15 s per launch.
  - 39 of 103 games are cached as Unknown, and those get a full folder scan every launch.
  - Cache the Unknown result as well. Re-check only when the install folder's timestamp changes or on a manual Refresh.
- [x] **Shader sync touches every game folder on every launch.** Done: `ShaderPackService.Mirror.cs` mirrors the planned file set (copy missing/changed, delete extras) instead of wipe + redeploy. Verified: 0 of 98 folders touched on launch + Refresh. `SyncGameFolder` creates/removes files in `reshade-shaders\Shaders` and `\Textures` (and the game root) for ~38 games each launch, even when nothing changed. Make it a no-op when the folder already matches. (Found while fixing the Unknown-engine cache: it changed folder times every launch, so the stamp now uses folder/exe names instead.)
- [x] **Silent auto-update runs Update All for every card.** Done: one call per component with only outdated, non-running games (`UpdateAll*Async(IReadOnlyList<GameCardViewModel>)` overloads); retries update one game (Vulkan = whole group). Manual Update All unchanged (still reinstalls ReShade into every RS game; revisit if wanted).
  - It runs Update All once per game that needs an update, so the work is N².
  - `UpdateAllReShade` targets every installed card, not just outdated ones.
  - Run Update All once, and only for outdated cards.
- [x] **Stop full downloads on every launch.** Done: RenoDX snapshot checks send If-None-Match with the ETag of the installed build (`renodx_snapshot_etags.json`); measured ~310 MB per check before. ReShade nightly size check uses a 1-byte range GET (nightly.link 404s on HEAD). Note: renodx.com ignores ETags, so clshortfuse addons still download (~3 MB each).
  - RenoDX snapshot addons are downloaded in full just to compare with the installed copy. Use ETag or Content-Length, or the release asset's `updated_at`.
  - ReShade nightly: the `nightly.link` HEAD returns 404, so 2.4 MB downloads every launch. Compare Content-Length from a ranged GET instead (see the CLAUDE.md pitfall).
  - UAL: fixed by the per-bitness flag in 1.2.
- [x] **Duplicate init on cache launch.** Done for the one that mattered: the pre-manifest addon update check in the MainWindow constructor is removed (it skipped manifest addons and doubled Addons.ini + GitHub API calls); the first-launch full-scan path now runs the check after the manifest is applied. Left as-is: Nexus/PCGW/UWFix/Ultra+ cache loads run twice but cost milliseconds (fresh caches, local files).
  - Nexus, PCGW, UWFix and Ultra+ start twice, and the addon check runs twice.
  - Add a once-per-launch guard to each.
- [x] **GitHub API usage.** Done: `GitHubETagCache` is saved to `github_etag_cache.json` (30-day expiry, batched writes) so first calls each launch are conditional. Then (27 Oct): every release/README/commits call goes through it (DLSS Enabler, DOF Fix, RDX5 + Feeder/Bridge, DLSSG 20/30, UAL, Luma; the forceRefresh bypasses in NR Cost Scaler/RTX 40 MFG/RTX Encore removed, since an ETag request is always current); overlapping calls for one URL share a request and answers are reused for 60 s (covers the rhi-repo list); `_rateLimited` only applies to api.github.com. Only `/user` auth checks stay direct. Note: `InvalidateAll` has no callers.
  - The ETag cache is memory-only. Save it to disk so each launch doesn't start from zero.
  - About 10 call sites bypass `GitHubETagCache`. Route them through it.
  - The `RankFTW/rhi-repo` releases list is fetched 3× by separate services. Fetch it once and share it.
  - The `_rateLimited` flag also blocks `raw.githubusercontent.com`, which isn't API-limited. Scope it to `api.github.com`.
  - User logs show 35 hits of the 60/hour limit.
- [x] **HTTP hygiene.** Done: 30 s timeout for all `GitHubETagCache` API calls (returns cached body or null). Then: the remaining API calls now use the cache (30 s timeout); the undisposed responses have `using` (Nexus 429 retry and RenoDX fallback attempts dispose the earlier response).
  - The 10-minute global `HttpClient` timeout also applies to small API calls. Use a short per-request timeout (`CancellationTokenSource`) for API calls, and keep the long timeout for downloads.
  - About 21 `HttpResponseMessage`s are never disposed. Add `using`.
- [-] **UI-thread work.** `LoadCacheAndBuildCardsAsync` and the saves do file and JSON work on the UI thread. Move it to `Task.Run`. Measured 27 Oct: the whole cache phase is ~0.7 s once at startup (104 cards), and settings saves already run on the debounce timer thread. Not worth moving: cards are bound view models (must be created on the UI thread) and this is freeze-sensitive code.
- [x] **Heartbeat and logging.** Done: "UI responsive" logged on last-action change or once a minute. Then: only one auto-restart can be pending (`_restartPending`, cleared if the restart is cancelled); the log writer appends everything queued in one write instead of one file open per line, and logs how many lines were dropped when the queue overflowed. Not done: thread/level fields (would change the log format).
  - The heartbeat logs "UI responsive" every second (about 3,600 lines/hour). Log only on state change or every N minutes.
  - The heartbeat has no reentrancy guard.
  - `CrashReporter.Log` opens the file once per line.
    - Keep one `StreamWriter` open and flush periodically.
    - Add thread and level fields.
    - Count dropped entries.

### 2.2 Dead code (done 27 Oct 2026, about 3,100 lines)

- [x] Removed (sub-agent, re-verified against all .cs/.xaml/tests/tools): `LiliumShaderService`, `CompactViewBuilder` + window size-lock code, old install handlers + `InstallMod32Async` + unreachable UE-Extended warning dialog, skeleton spec helpers, 9 of 10 converters (`BoolToVisibility` kept, used in XAML), 9 unread fields in `GameInitializationService`, ~58 unused private members, ~45 public members + 18 interface members with no callers, 24 never-read cached NVIDIA profile properties, `GitHubETagCache.InvalidateAll`.
- [x] `lumaDefaultGames` removed (app, Manifest Editor, manifest.json): it was never implemented.
- [x] `FEATURES.txt` (ReLimiter reference) no longer shipped. `SfAutoConfigDisabled` legacy setting removed.
- [-] **`ReShade.Vulkan.ini` is USED** (the audit was wrong): seeded to `iniseshade.vulkan.ini` and used as the Vulkan reshade.ini template (`AuxInstallService.EnsureInisDir`, `.Ini.cs`). Referenced in lowercase, which case-sensitive searches miss.
- [-] `FeatureFlags.DlssNr` kept as a remote kill switch.
- [-] `tools/pcgw-fetch` kept: the PCGW Editor calls it.
- [x] Manifest `optiScalerDllOverrides` never worked (its loader was never called). Removed from the app and the Manifest Editor, along with the unused per-game Auto HDR launch toggle (the card's HDR button covers it).
- [x] Found while removing: `SaveNameMappingsImmediate` and `PcgwService.FlushCacheAsync` had no callers despite comments saying to call them at rename/shutdown (removed). Checked: shutdown flushes settings (`FlushPendingSaves`); PCGW's 500 ms debounced cache write can be lost at exit, which only means those lookups are fetched again.
- [ ] Skipped: `DlssPresetService._nativeSetSettingLegacy` (set from NVAPI lookup), `UeExtendedWarningDismissed` setting key (no reader now), constructor parameters that no longer feed a field.
- [x] **Duplicates to merge:** Done: `AppPaths` (`ExeDir`, `InExeDir`, `Bundled7Zip`, `Find7Zip`) replaces the three finders and every `AppContext.BaseDirectory` path; `MainViewModel.IsLegacyVersion` delegates to `AuxInstallService`. The "version comparers" are one-line `Version.TryParse` calls, not worth merging. Was: three `Find7Zip` copies + inline `AppContext.BaseDirectory` 7-Zip paths; `IsLegacyVersion` twice; several version comparers. Also `EnsureInisDir` seeds templates from `AppContext.BaseDirectory` (CLAUDE.md says use `Environment.ProcessPath`).

---

## Phase 3: Before 10 Nov 2026 (.NET 8 end of support)

- [ ] **Move to .NET 10.**
  - Change the TFM in the app, test, DropHelper and tools projects.
  - Re-test single-file publish, the WinUI 3 / WindowsAppSDK 2.5.1 compatibility and ClrMD.
- [ ] **The installer doesn't install the .NET runtime.**
  - Add a runtime check and install to `RHI Setup.iss`, or publish self-contained.
  - It currently installs the Windows App Runtime, which isn't needed if the app is self-contained. Pick one approach.
- [ ] Fix the wrong `PublishDir` default in `RHI Setup.iss`.

---

## Phase 4: Ongoing (one per release)

These make future features cheaper to build. Do them in this order; each is safe to stop after.

### 4.1 `GitHubReleaseClient`

- [ ] One client handles "fetch latest release, pick asset, ETag cache, rate limit". It replaces the per-service copies, and shares the rhi-repo release list.

### 4.2 `StagedComponent` base class

- [ ] About 17 components each have their own copy of fetch → stage → `version.txt` → update check → deploy to game → record.
  - `Rtx40Mfg` and `DlssNrCostScaler` are almost identical.
  - A shared base (using 4.1) plus a `GameFileDeployer` handles backup, the `.original` sentinel, the WindowsApps check, `SafeCombine` and `RecordDlssFound`.
  - Bug fixes to update logic then happen once.
- Start with Rtx40Mfg and DlssNrCostScaler, which are the closest pair, then move the others one at a time.

### 4.3 Per-game settings registry

- [ ] `GameNameService` has 71 collection fields, and each new override needs the 10-step recipe in CLAUDE.md. Steps get missed (see the Luma TAA rename bug).
  - Replace it with `PerGameSetting<T>`, declared once with:
    - key type (name-only or `Name|Store` composite)
    - default value
    - JSON name
  - Load, save, rename migration, reset, report encoding and the `IGameNameService` surface come from the registry.
  - A new override is then one declaration plus UI.
- [ ] Keep the JSON format the same, so existing `settings.json` files load unchanged.

### 4.4 Install store and Update All registry

- [ ] `ComponentInstallStore`: replace the scattered `XxxInstalledAs` maps with one store (atomic writes, as in 1.1).
- [ ] Update All component registry: each component registers how it checks and updates, and Update All loops over the registry instead of hard-coding each component.

### 4.5 Persistence layout

- [ ] Target layout:
  - `preferences.json`, `games.json` (per-game overrides) and `installs.json`
  - each through `JsonStore<T>` (atomic write, `.bak`, quarantine on corrupt, `schemaVersion` with migrations)
- [ ] Store Hidden and Favourite in one place. Today they live in two places, merged as a union.
- [ ] ManifestEditor duplicates the `RemoteManifest` model. Share the model through a linked file or a small shared project.

### 4.6 UI helpers

- [ ] Grow `UIFactory` into shared builders for the repeated rows, cogs, toggles and status pills in the `DetailPanelBuilder.*` and `SettingsHandler.*` files.
- [ ] Still too big after the split (each is one method):
  - `DetailPanelBuilder.NeuralRendering.SectionUi.cs` (94 KB)
  - `MainWindow.Events.Components.OptiScaler.cs` (84 KB)
  - `MainWindow.Events.Components.Rdx.cs` (56 KB)
  - Break these up as their sections move onto the helpers.

### 4.7 DI and static state

- [ ] There are 209 `GetRequiredService` calls (service locator). Inject dependencies instead, starting with new code.
- [ ] Register `AddonPackService` in DI.
- [ ] `MainViewModel` is 16k lines with a 38-parameter constructor. It shrinks as 4.2–4.4 move logic out. Don't split it for its own sake.

### 4.8 `CardFactory` (last; highest risk)

- [ ] `BuildCards`, `CacheLoad` and the background scan share about 48 property assignments by copy-paste. The RTX 40 MFG block is duplicated.
  - Use a `CardFactory` plus `ICardEnricher`s (one per component), so a new feature is added in one place.
- [ ] **Check first:** some newer components are set only in the background scan, not in the `InitializeAsync(forceRescan: true)` path that Refresh uses. Fix that as a bug fix before the refactor.

---

## Tooling and release

- [ ] **CI** (GitHub Actions on `windows-latest`):
  - the manifest check from 1.3
  - `dotnet build` and `dotnet test` (x64)
  - optionally a publish artifact
  - **Blocker:** the ClrMD DLL is referenced by a hard-coded NuGet path in the csproj and `publish.bat`. Switch to a `PackageReference`, or vendor the DLL into the repo.
- [ ] **Loose files in `publish.bat`.** About 25 files are copied by hand. Declare them as `<Content CopyToPublishDirectory="PreserveNewest">` (or a glob) in the csproj, so new files ship automatically.
- [ ] **Tests.** The suite is thin. Highest-value additions:
  - settings round-trip and corrupt-file recovery
  - `RenameGame` migrates every per-game collection (would have caught the Luma TAA bug)
  - save/load with a cleared last entry
  - manifest `Normalize` with case collisions
  - version comparers
  - `SafeCombine` (done)
  - `.original` sentinel install and uninstall
  - UAL per-bitness update detection
  - ETag cache behaviour
  - `GameKey` composite migration
- [ ] **Dependencies:**
  - `Microsoft.Win32.Registry` is redundant on .NET 8 and later. Remove it.
  - `NvAPIWrapper` is abandoned. Plan to replace it or vendor it.
  - `MarkdownTextBlock` is a preview package. Watch for a stable release.
  - ManifestEditor package versions are out of step with the app. Use `Directory.Packages.props` for central versions.
- [ ] **`.editorconfig` with a warning ratchet.**
  - Turn on IDE0051/0052/0005 as suggestions.
  - Keep nullable warnings from rising above the current ~190.
- [ ] **Repo size (196 MB).** Most of it is old ManifestEditor `bin` DLLs and 70 versions of `database/pcgw_data.json` in history. Only rewrite history if size becomes a problem; it would need a force-push.
- [ ] **Remaining error handling:**
  - 19 empty catches remain in the files excluded from the cleanup (CrashReporter, AsyncExtensions, FileHelper, Heartbeat, NativeStack).
  - 126 `async void` handlers remain, all now wrapped in try/catch.

---

## Done already (26 Oct 2026, `b8094e6`, plus uncommitted follow-up)

- [x] The GitHub token is sent only to `api.github.com` (`GitHubTokenHandler`). That also fixed shader pack 404s for signed-in users.
- [x] Archive path traversal guard (`FileHelper.SafeCombine`) in Luma, Lilium, ShaderPack and ShaderProfile.
- [x] Split the five largest files into partials.
- [x] Silent empty catches now log, across about 85 files. `async void` handlers are wrapped.
