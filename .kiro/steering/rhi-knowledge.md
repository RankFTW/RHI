# RHI Project Knowledge & Conventions

## Build & Publish Rule

**After every code change, run `publish.bat` to build and deploy:**

```powershell
& "g:\RDXC\publish.bat"
```

`publish.bat` runs a single `dotnet publish` (Release, win-x64, single-file). The running app is at `C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI\RHI.exe`. Never add `--no-restore` or skip the publish step — the build output is NOT the running app.

**WinAppSDK publish constraint:** Do NOT add named XAML elements (`x:Name`) or new XAML border/control elements to `MainWindow.xaml` unless absolutely necessary. The WinAppSDK 1.6 publish pipeline generates duplicate `MainWindow.g.i.cs` when certain XAML changes are present, causing CS0102 compile errors. Dev-only and dynamically-created UI must be built entirely in C# code (see `SettingsHandler.cs` freeze diagnostic card as reference). Adding `x:Name` to an already-existing element is safe; adding new named elements is risky.

## Project Overview

RHI is a WinUI 3 desktop app (.NET 8, C#) that manages HDR mods across PC game libraries. It auto-detects games from 8 storefronts and provides one-click install/update/uninstall for ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, RE Framework, Luma Framework, and DXVK.

## Architecture

- **Framework**: WinUI 3 (Windows App SDK 1.6), .NET 8, CommunityToolkit.Mvvm
- **Pattern**: MVVM with partial classes. MainViewModel split across files (Init, Install, Update, Settings, Dxvk, GameMatching)
- **DI**: Microsoft.Extensions.DependencyInjection, registered in App.xaml.cs
- **Services**: Partial classes for complex services (DxvkService.cs, .Install.cs, .Staging.cs, .Tracking.cs)
- **UI**: Code-behind builders (DetailPanelBuilder, CompactViewBuilder) — NOT XAML templates
- **Tests**: xUnit + FsCheck property-based tests

## Key Directories

- `RenoDXCommander/Services/` — all service logic
- `RenoDXCommander/ViewModels/` — MVVM view models (partial classes)
- `RenoDXCommander/Models/` — data records and enums
- `docs/` — research docs and detailed guide
- `manifest.json` — remote game manifest (fetched from GitHub at runtime)
- `OPTI/` — bundled OptiScaler files for different GPU configs

## Conventions

### Code Style
- Use `CrashReporter.Log()` for all significant operations — include method name: `[ClassName.MethodName]`
- Use `StringComparison.OrdinalIgnoreCase` for all game name comparisons
- Per-game settings: `Dictionary<string, string>` with game name as key
- HashSets for boolean per-game flags (e.g. `_normalReShadeGames`, `_hiddenGames`)
- All settings persist immediately via `SaveNameMappings()` — no deferred saves
- UI updates on dispatcher: `DispatcherQueue?.TryEnqueue(() => { ... })`
- **No emojis in button labels or UI text** — the existing codebase uses them in specific places (e.g. the ⚙ cog, 📋 INI button, ⚡ UE-Extended toggle); do not add new ones. Plain text only for new UI elements.

### Naming
- Internal string values: no spaces, PascalCase (e.g. `"LiliumHdr"`, `"Development"`)
- Display labels: human-readable with spaces (e.g. `"Lilium HDR"`, `"Global"`)
- Service fields: `_camelCase` with underscore prefix
- Constants: `PascalCase` for public, `_camelCase` for private static

### Per-Game Overrides Pattern
When adding a new per-game override:
1. Add field to `GameNameService.cs` (private dict/hashset)
2. Add public accessor property
3. Add to `LoadNameMappings()` / `SaveNameMappings()`
4. Add to `RenameGame()` migration (MigrateDict/MigrateHashSet or MigrateCompositeDict/MigrateCompositeHashSet)
5. Add to `IGameNameService.cs` interface
6. Add to `MainViewModel.cs` (expose via property)
7. Add to `MainViewModel.Settings.cs` (Get/Set/Resolve methods)
8. Add UI in `DetailPanelBuilder.Overrides.cs`
9. Add to reset overrides handler
10. Add to `GameReportEncoder.cs`

### Multi-Store Composite Key Pattern
Per-game settings that should be independent per-store use composite keys: `"GameName|Store"` (e.g., `"Halo Infinite|Steam"`).

**Key types:**
- **Composite-keyed (per-store)**: Install records, update exclusions, shader/addon modes, channel overrides, DXVK variants, Luma enabled/disabled, normalReShade, folder overrides, hidden/favourite, etc.
- **Name-only (shared across stores)**: Wiki exclusions, UE-Extended opt-in/opt-out, RTX HDR (driver profile is per-exe)

**Implementation pattern:**
1. Use `GameKey.FromCard(gameName, source).ToKey()` to build composite keys
2. Use `GameKey.From(gameName, store).ToKey()` in methods with separate params
3. Add `string store = ""` optional parameter to Get/Set/Toggle methods
4. Migration: In `LoadNameMappings()`, use `.Select(k => k.Contains('|') ? k : $"{k}|")` to append empty store to legacy keys
5. In `RenameGame()`, use `MigrateCompositeHashSet()` / `MigrateCompositeDict()` for composite-keyed collections

**Files involved:**
- `Models/GameKey.cs` — readonly record struct with `ToKey()`, `Parse()`, `FromCard()`, `Matches()` helpers
- `Services/GameNameService.cs` — all per-game settings storage and migration
- `ViewModels/MainViewModel.Settings.cs` — all Get/Set/Resolve methods with store parameter
- Record models (`InstalledModRecord`, `AuxInstalledRecord`, etc.) — `Store` field added

### Staging/Download Pattern
When adding a new downloadable component variant:
1. Separate staging directories per variant (never share folders)
2. Each variant has its own `version.txt`
3. Download only the globally selected variant at startup
4. Other variants download on-demand when per-game override needs them
5. One-time migration from legacy shared folder on first launch
6. `EnsureStagingAsync()` is no-op when staging is already valid (+ `!HasUpdate` check)

### Shader & Addon Mode Values
- **Shader mode**: `"Global"` (default/removed), `"Custom"`, `"Select"`, `"Off"`
- **Addon mode**: `"Global"` (default/removed), `"Select"`, `"Off"`
- Both disabled when "No Addons" RS Channel active (`card.UseNormalReShade`)

## Manifest Overview
- Fetched from GitHub on every launch (ETag caching)
- Game names must match exactly (case-sensitive)
- `forceExternalOnly` uses detected name, NOT wiki-mapped name
- No duplicate JSON keys (parser only sees last)
- See `rhi-manifest.md` for full field reference
- `noUeExtendedGames`: list of game names that should NOT use UE-Extended as default — forces standard `renodx-unrealengine.addon64` instead. Counterpart to `ueExtendedGames`.
- `rtxHdrInfoUrl`: global URL for the RTX HDR info button link (shown in RenoDX info dialog when RTX HDR is enabled). Falls back to hardcoded Reddit post if not set.

## Testing
- xUnit + focused tests in `RenoDXCommander.Tests\CoreLogicTests.cs`
- Run: `dotnet test g:\RDXC\RenoDXCommander.Tests\RenoDXCommander.Tests.csproj --no-build -p:Platform=x64`
- Build: `dotnet build g:\RDXC\RenoDXCommander.Tests\RenoDXCommander.Tests.csproj --no-restore -v q -p:Platform=x64`
- App build: `dotnet build g:\RDXC\RenoDXCommander\RenoDXCommander.csproj --no-restore -v q -p:Platform=x64`
- **Publish & launch**: User always runs from `C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI\RHI.exe`. After code changes, run `publish.bat` to deploy the new build there — that IS the running build, not a separate release artifact.

## Git
- Push to main directly (single developer)
- **Always ask before committing or pushing**
- Commit messages: descriptive, prefixed with context (e.g. "Fix:", "Manifest:", "Release v1.9.x")
- Version in `RenoDXCommander.csproj` (AssemblyVersion + FileVersion)
- **NEVER bump the version number** — the user bumps it manually immediately before a release. Do not touch AssemblyVersion or FileVersion under any circumstances, even when adding features or fixes.
- Patch notes in `RenoDXCommander/RHI_PatchNotes.md`
- **Current release**: v2.8.5 (shipped). Next work targets v2.8.6 Beta 5.

## Debugging Approach
- **Stop and ask clarifying questions** before making extensive changes to debug issues
- Don't assume the root cause — gather evidence first
- Ask the user to test with logging and share relevant log output
- One change at a time when debugging

## Task Delegation
- **When to delegate**: File splits, multi-file refactors (4+ files), new features touching model + service + UI + persistence
- **When NOT to delegate**: Simple single-file edits, manifest changes, bug fixes <20 lines
- **Sub-agent prompt must include**: Goal, file paths, build command, rules, relevant code snippets

## Patch Notes Process
- **ANY manifest change MUST be added to patch notes immediately in the same turn**
- **ANY bug fix MUST be added to patch notes immediately in the same turn**
- **ANY feature or UI change MUST be added to patch notes immediately in the same turn** — do not wait to be asked
- Use **### New** for genuinely new features, **### Changes** for improvements/tweaks to existing features, **### Bug Fixes** for fixes
- Use **### Maintenance** for internal wiring, infrastructure changes, or background work that enables future features but isn't exciting or visible to users on its own
- If nothing in a release is genuinely new or exciting, drop **### New** entirely — don't force items into it
- Don't list fixes for features introduced in the same version (users never saw the broken state)
- **No teaser language** — never write "groundwork for upcoming X" or "prepares for future Y". If the change isn't user-visible and exciting on its own, it belongs in Bug Fixes or Maintenance, not New or Changes.
- **Internal/infrastructure changes** (badge refactors, detection improvements, dropdown splits) should be framed as bug fixes ("Fixed X showing incorrectly") rather than features, unless they genuinely add something the user will notice and appreciate.

## Common Pitfalls
- `nightly.link` HEAD requests unreliable — use Content-Length comparison
- `C:\ProgramData\ReShade\` requires admin — always use `CopyFileWithElevation()`
- GitHub API rate limit: 60 req/hour — minimize calls, use ETag caching
- `AppContext.BaseDirectory` wrong for single-file publish — use `Path.GetDirectoryName(Environment.ProcessPath)`
- Vulkan games share global state — per-game overrides must propagate to ALL Vulkan games

## Lifecycle & Shutdown Architecture (v2.8.5+)

`MainWindow` owns a `CancellationTokenSource _lifetime`. All background tasks, `Task.Delay` loops, and async operations should check `_window.LifetimeToken` or `_window.IsShuttingDown`. `MainWindow_Closed` runs an ordered shutdown via `RunShutdownStep(name, action)`. `ViewModel.StopBackgroundWork()` cancels timers and `_backgroundLifetime`. `_detailPanelBuilder.StopBackgroundWork()` cancels NVAPI panel scans. `SingleInstanceService.Stop()` releases the mutex. `DialogService` uses `DialogCoordinator` (not raw semaphore) — `ShowProgressAsync` + `await using session` is the correct pattern for all progress dialogs. `UiDispatch.InvokeAsync` handles cross-thread UI dispatch with cancellation. `MergeCardsAsync` uses `UiDispatch.InvokeAsync` with `_backgroundLifetime.Token`. `InitializeAsync`/`RefreshAsync`/`FullRefreshAsync` are serialised via `_libraryOperationGate`. `RunBackgroundScanAndMergeAsync` is now awaited (not fire-and-forget).

## UI Freeze Investigation (v2.8.6 Beta 5 — diagnostics complete, awaiting stress loop result)

**Fixes shipped in v2.8.6 Beta 4:**
- `BuildNeuralRenderingSection` outer `TryEnqueue(Low)` was missing `SelectedGame != card` and `scanToken.IsCancellationRequested` guards. Rapid A→B→A navigation queued two callbacks; both fired and cascaded into a WinUI layout hang. Fixed.
- `BuildDriverProfileSectionWithData` inner `TryEnqueue(Low)` (grid add) now also checks `SelectedGame != card`.
- Same-game-reselect skip: `_lastBuiltCard` in `MainWindow.xaml.cs` prevents redundant full panel rebuilds when the same card object is reselected.
- Three Settings-page `SelectionChanged` handlers (GlobalVSync, ReBarEnable, ReBarSize) were queuing `BuildOverridesPanel` during init, conflicting with NVAPI reads. Fixed.
- Several dialog handlers (`RtxHdrConfigButton_Click`, `DxvkCogButton_Click`, `MfgDialog.ShowConfigDialogAsync`, `RdxCogButton_Click`) were calling `GetRtxHdr*`/`GetVulkanPresent*`/`GetMfg*` getters directly on the UI thread. Each getter calls `_sessionLock.Wait(5000)` — if NVAPI hangs (e.g. after GPU sleep/wake), the UI blocks for up to 25s. All moved to `await Task.Run(...)` before dialog content is built.
- `RefreshDlssVersions` was called on the UI thread after `async` version-swap continuations in `DetailPanelBuilder.Overrides.NvidiaProfile.cs`. Moved to `await Task.Run(...)`.

**Diagnostic infrastructure (complete as of Beta 5):**
- **CPU sampling**: UI thread `TotalProcessorTime` delta over 1s — IDLE (<900ms) = waiting on lock/native call, PEGGED (≥900ms) = layout/compute loop. Validated.
- **ClrMD managed stacks**: `Microsoft.Diagnostics.Runtime 3.1` loose DLL, `EnumerateStackTrace(includeContext: true)`. DAC resolves from shared runtime. Frames confirmed working (Thread.Sleep visible on UI thread). Runs for all users.
- **ThreadState + WaitReason**: `ProcessThread.WaitReason` read after `Refresh()`. `LpcReceive`(9)/`LpcReply`(10) = COM/RPC cross-process wait. Runs for all users.
- **WM_NULL pump probe**: `SendMessageTimeout(hwnd, WM_NULL, SMTO_ABORTIFHUNG, 1s)` + `IsHungAppWindow`. Returns immediately if pump is alive, times out if thread is blocked in non-message wait. Runs for all users.
- **Native top frame**: `SuspendThread → GetThreadContext (aligned CONTEXT) → ResumeThread` (ResumeThread in `finally`). Module resolved via `GetModuleHandleExW(FROM_ADDRESS)`. Export name via PE export table with 0x1000 offset threshold — large offsets print `module+0xRVA (no nearby export)` instead of a misleading name. Runs for all users.
- **RSP stack scan**: `ReadProcessMemory` copies stack (clamped by `VirtualQuery` to committed region). Scans for call-preceded return addresses in loaded modules. Up to 10 heuristic frames. Validated: idle baseline shows `GetMessageW+0x22`, block test shows `WaitForSingleObjectEx+0xAF`. Runs for all users.
- **GPU/driver modules**: logged with FileVersion + PE TimeDateStamp + SizeOfImage. Third-party injected modules logged separately.
- **Minidump**: `MiniDumpWriteDump` (via `PssCaptureSnapshot` fallback to live handle) — gated behind `unlock.txt`. 952 MB–1.5 GB. Verified readable with `dotnet-dump analyze` + `clrstack`.
- **Dev test buttons** (`SettingsCardsPanel`): "Test IDLE (30s sleep)", "Test PEGGED (10s spin)", "Test Native Block (30s WaitForSingleObject)", "Capture Idle Baseline", "Test Dispatcher Exception" (kills process — `e.Handled = true` confirmed ineffective), "Stress Loop (30 passes)". All validated.

**Freeze captures so far:**

**First real freeze (2026-10-06, user DudleyTheGentleman, v2.8.5):**
- `UI FROZEN` | CPU = `IDLE (0ms/1s)`
- UI thread stack: `[InlinedCallFrame] × 2 → IApplicationStaticsMethods.Start → Application.Start → Program.Main` (no managed frames above Start)
- `BuildDriverProfileSection` released semaphore at `17:00:15.174` for NINJA GAIDEN 2 Black — deferred `TryEnqueue(Low)` for `BuildDriverProfileSectionWithData` grid add **never ran**. No `AddingGrid`/`Done` log entry.
- 13-second silence between semaphore release and freeze detection.
- WM_NULL probe not yet in that build — added in Beta 4.
- **Interpretation**: UI thread went into a native WinUI wait and never returned to process the dispatcher queue. Not a managed lock wait (no managed frames). Not a PEGGED layout loop (CPU = 0). Native top frame now captured — next freeze will show which system function the thread is blocked in.

**Second freeze (v2.8.5, unknown user):**
- CONTROL Resonant. Two rapid navigations (Star Wars → Batman → CONTROL) in 846ms. Two TryEnqueue(Low) grid-adds in flight. Dispatcher died after both completed. No dump.

**Third freeze (2026-10-07, developer, v2.8.6 Beta 5):**
- Control Ultimate Edition. Single game, no rapid navigation, 4s after `BuildDlssEnablerRow:Done`.
- All dispatcher probes failed. CPU = IDLE. WM_NULL returned immediately (pump alive).
- Dump written: `%LocalAppData%\RHI\freeze_dump.dmp` (~1033 MB). Auto-restart fired successfully.
- **Interpretation**: Same signature as capture 1 — pump alive, dispatcher not processing queued work, IDLE CPU. Dump available for WinDbg analysis.

**Open leads (in priority order):**
1. **Stress loop** — "Stress Loop (30 passes)" in Settings → bottom. Run it, check whether it reproduces the freeze and which game triggers it.
2. **WinDbg on dump** — `%LocalAppData%\RHI\freeze_dump.dmp`. `.symfix` + `.reload` + `~*k`. Symbols may resolve Xaml RVAs and show other threads waiting on UI thread.
3. **NVIDIA-off A/B** — force `DlssPresetService.IsSupported = false` to skip all NvAPI scans for a few days. All freezes came after panel builds that kick off background NvAPI reads.
4. **Ask other users** — display scaling % and NVIDIA GPU yes/no.
5. **WinUI 3 DPI bug** — known issue where zero-height visual kills dispatcher. Dev machine is 100% scaling. Check other users.

**Do NOT change locks, layouts, or panel code until stress loop or NVIDIA-off result comes in.**

**Parked (waiting for evidence):**
- Orphaned NVAPI `Task.Run` after 5s `Task.WhenAny` timeout (Finding 3) — IDLE confirmation needed
- Unlocked `_session` entry points in `DlssPresetService` (Finding 2) — background race, not confirmed freeze path
- Star columns in `DetailPanelBuilder.Overrides.cs`, `ShadersAddons.cs`, `Dxvk.cs`, `RsChannel.cs` — PEGGED needed to implicate

**How to read the next freeze block:**
- `NtUserGetMessage` + `WM_NULL` returns → pump healthy, dispatcher not running queued work → check `TryEnqueue(Low)` return value
- `NtUserMessageCall` or send-message frame → waiting on hung window (overlay/IME)
- `LpcReply`/`LpcReceive` or `combase`/`rpcrt4` in scan → COM/RPC cross-process wait → use Analyze Wait Chain
- `NtWaitForSingleObject` + `dcomp`/`dxgi`/`nvwgf2umx` in scan → composition or GPU wait → check driver version
- `NtWaitForSingleObject` + only Xaml/app frames → needs dump or Microsoft symbols for RVAs
- `FileOpenPicker` COM threading issues — use Win32 `GetOpenFileName` P/Invoke instead
- **RE Engine ReShade gate uses `ExcludeFromUpdateAllRef` as escape hatch**: On RE Engine games, `RsActionLabel` returns "⚠ RE Framework required" and `CardRsInstallEnabled` is false when `IsREEngineGame && !IsRefInstalled && !EffectiveLumaMode`. The escape hatch is `ExcludeFromUpdateAllRef` — when the user disables automatic RE Framework updates (setting this flag true), both the label and enabled state bypass the requirement, allowing ReShade to install without RE Framework. Both checks are identical: `IsREEngineGame && !IsRefInstalled && !EffectiveLumaMode && !ExcludeFromUpdateAllRef`. Defined in `GameCardViewModel.ReShade.cs` and `GameCardViewModel.UI.cs`.

- **`RequestOverridesPanelRebuild` callback pattern**: When post-install logic changes a persisted setting (e.g. auto-writing `-dx11` launch arg), the overrides panel won't reflect the change because it was already built imperatively. Fix: add an `Action<GameCardViewModel>? RequestOverridesPanelRebuild` property to `MainViewModel`, wire it in `MainWindow.xaml.cs` to `card => DispatcherQueue.TryEnqueue(() => BuildOverridesPanel(card))`, and call it after writing the setting. This is the same pattern as `ConfirmForeignDxgiOverwrite` and other UI callbacks on `MainViewModel`. Use this pattern whenever a ViewModel-side operation needs to trigger an imperative panel rebuild. The same escape-hatch pattern applies to RenoDX, ReLimiter, Display Commander, and DOF Fix — all four show "⚠ ReShade required" and are disabled when `!IsRsInstalled`. Unchecking ReShade in the Global Update Inclusion dialog sets `ExcludeFromUpdateAllReShade = true`, which bypasses the gate on all four: `InstallActionLabel`/`CanInstall` (RenoDX), `UlActionLabel`/`UlInstallEnabled`/`CardUlInstallEnabled` (ReLimiter), `DcActionLabel`/`DcInstallEnabled`/`CardDcInstallEnabled` (DC), `dofGreyed` in `DetailPanelBuilder.Components.cs` (DOF Fix). The gate condition is `!IsRsInstalled && !ExcludeFromUpdateAllReShade`.

- **Luma no longer bundles its own ReShade or DLSS — RHI manages both**: All ReShade DLLs (`dxgi.dll` etc.) and `nvngx_dlss.dll` are skipped during Luma zip extraction in all four code paths (InstallAsync zip, InstallFromArchiveAsync zip, InstallFromArchiveAsync 7z, Uninstall backwards-compat guard). After Luma installs: (1) `InstallReShadeAsync(card)` deploys RHI's staged ReShade; (2) `_dlssStreamlineService.EnsureNewestDlssCachedAsync()` fetches the newest DLSS and copies it to `installPath/nvngx_dlss.dll`. Both are managed independently from Luma — uninstalling Luma does not touch ReShade or DLSS.

- **Luma mode toggle removal plan (luma-wip) — IMPLEMENTED**: `IsLumaMode` is always `false`. `EffectiveLumaMode` now means `LumaFeatureEnabled && LumaMod != null` (no toggle). `LumaRenodxCompatible = true` whenever `LumaMod != null` (set in BuildCards, BackgroundScan, CacheLoad). RenoDX and Luma rows always visible simultaneously. Luma ON/OFF badge/toggle hidden (`Visibility.Collapsed`). Context menu toggle removed. `LumaIniButton_Click` (clipboard) replaced with `LumaCogButton_Click` (⚙ cog) — opens empty "Luma Settings" `ContentDialog`. Luma row in `DetailPanelBuilder.Components.cs` now shows when `card.LumaFeatureEnabled && card.LumaMod != null` (not `isLumaMode`). `CardInstallButton_Click` no longer routes to Luma install based on `IsLumaMode`.

- **Generic Luma UE mod — all phases complete (luma-wip)**: `LumaMod.IsGenericLuma` bool. `LumaGenericGameEntry` model (`Notes`, `EngineIniKeys` list of `(Section, Key, Value)`, `LaunchArgs` full string, `HdrSupported`, `DlssFsrSupported`, `DlssFsrBlocked`, `UeVersion`). `LumaService.FetchGenericUeTableAsync()` fetches the UE wiki page, parses the game table and `<details>` Engine.ini blocks, detects DLSS/FSR and HDR columns by header text, extracts full launch args from `<code>` elements. `_lumaGenericEntries` dict in `MainViewModel` populated at startup. BuildCards/CacheLoad inject generic `LumaMod` for DX11 UE games; blocked when `DlssFsrBlocked && !HdrSupported`. `AuxInstallService.ApplyEngineIniCustomKeys()` writes keys; `RemoveEngineIniCustomKeys()` removes by key name. `InstallLumaAsync` post-install: writes Engine.ini keys, sets `LumaTaaEnabled` if TAA keys found, writes full `LaunchArgs` to `LaunchArgsOverrides` (only if empty). Luma cog has TAA Settings Off/On combo — writes/removes the standard TAA keys (`r.DefaultFeature.AntiAliasing=2`, `r.PostProcessAAQuality=4`). Persisted as `LumaTaaEnabled` HashSet (name-only) in settings.json via `IGameNameService.LumaTaaEnabled`. Exposed via `IsLumaTaaEnabled`/`SetLumaTaaEnabled` on `MainViewModel`.

  **Known gaps in wiki parsing (not yet implemented):**
  - **Inline Engine.ini mentions in plain text** (e.g. "Recommended to use r.motionblur.amount=0 via Engine.ini") are NOT auto-applied — only `<details>` blocks are parsed into `EngineIniKeys`. These show as plain notes only.

  **Implemented parsing rules:**
  - **⛔ in DLSS/FSR column** blocks generic Luma injection only when DLSS/FSR is ⛔ AND HDR is also not ✅. If ⛔ DLSS + ✅ HDR (e.g. Abzu), Luma row still shows.
  - **Full launch args** extracted from `<code>` elements in the Notes cell when "launch" or "argument" keyword is present. Full string stored in `LaunchArgs` (e.g. `-nod3d9ex`, `-oss=Steam -dx11`). Written to `LaunchArgsOverrides` on install if empty; removed on uninstall only if value matches exactly.
- Game folder permission errors: `UnauthorizedAccessException` on restrictive ACLs
- `[RelayCommand]` doesn't support optional parameters — call methods directly
- Boolean settings: always persist both states explicitly (not "absent = default true")
- Forward slashes in paths break Win32 `GetOpenFileName` — normalize with `.Replace('/', '\\')`
- WinUI 3 nested ContentDialogs: Cannot open a ContentDialog from within another ContentDialog — use inline controls (TextBox + Button) instead
- WinUI 3 `Button` has no `ToolTip` property — use `ToolTipService.SetToolTip(button, "text")` instead
- `System.Text.Json` is NOT auto-imported in `MainWindow.Events.Components.cs` — add `using System.Text.Json;` explicitly when using `JsonSerializer` or `JsonElement` there
- **WinUI 3 element can only have one parent**: Adding a UI element to a second container (e.g. adding to a StackPanel then also to a Grid) throws `COMException: No installed components were detected` at runtime. The stack trace points to `AppendDynamic` on IList. Always ensure each element is added to exactly one parent. Remove any leftover add calls from intermediate containers.
- WinUI 3 window visibility on startup: `AppWindow.Hide()` called after construction is too late — the window is already presented during `InitializeComponent()`. To start hidden (e.g. minimized to tray), hide at THREE points: (1) after `InitializeComponent()` in constructor, (2) in `MainWindow_Activated` handler (return early + hide — this is where window becomes visible), (3) deferred low-priority `DispatcherQueue.TryEnqueue` hide as safety net. The `Activated` handler is the critical one — it's where WinUI presents and sizes the window.
- Cache phase timing: During `BuildCards` cache loading, use `cachedManifest` (local variable), NOT `_manifest` (instance field is null at that point)
- Post-install record access: On fresh installs, `card.InstalledRecord` is null when post-install logic runs — use the local `record` variable from `InstallAsync`
- Skipped feature deployment: When conditionally skipping a feature deployment, explicitly set the record field to reflect the skipped state
- Badge visual vs click handler: `isClickable` in `DetailPanelBuilder.cs` must match click handler guard — if they disagree, badge looks clickable but does nothing
- **Greying individual row elements vs the whole row**: When a row needs to be "disabled" (e.g. ReShade required), apply `Opacity = 0.35` and `IsEnabled = false` only to the **install button and cog** — never to the entire row `Grid`. Setting `Grid.Opacity = 0.35` also dims the Info button which should always remain fully visible and clickable. Pattern: set row `Opacity = 1.0` always, then set individual button opacities. The RenoDX/ReLimiter/DC rows already follow this; Luma initially used row-level opacity which dimmed the Info button.
- Drag-drop addon install (`DragDropHandler.Addon.cs`) must replicate post-install steps from `MainViewModel.Install.cs` — Engine.ini LUT, UE-Extended settings, renodx INI overrides. Currently only LUT is deployed; add others as needed.
- **Game Pass path versioning**: WindowsApps paths contain version numbers (e.g. `Microsoft.198377053870B_1.109.7863.0_x64__...`). When the game updates, the version segment changes, creating a new folder. InstalledModRecord path no longer matches the freshly detected path on Refresh → record lookup fails → card shows "not installed" → library save drops the record. This affects RenoDX, ReShade, and all per-game records stored with an install path. Fix needed: normalize/fuzzy-match WindowsApps paths ignoring the version segment.
- **Dual-store same-name conflict**: When a game exists on both Steam and Game Pass with the same detected name, detection can flip which store is "primary" between sessions. The path reconciliation log shows the old path (Steam) vs new path (Game Pass) and reports "addon not found at either path" because the mod was dropped onto the Game Pass version but the record stored the Steam path. Root cause: only one card per game name, whichever store wins detection overrides the other.
- **Pipe-separated `installPathOverrides`**: Use `"Game": "path1|path2"` to specify multiple candidate subpaths. Resolution tries each in order, uses the first that exists on disk. Used for games available on both Steam and Game Pass with different subfolder layouts (e.g. `Win64` vs `WinGDK`). Supported in BuildCards, CacheLoad, and Install path resolution.
- Update All must NOT override user's explicit toggle state for per-game settings
- **In-memory version fields (`_latestUlVersion`, `_latestDcVersion`, etc.) are null after a session restart** — they are only populated during `CheckXxxUpdateAsync` in the current session. If the pre-cached binary exists (from a previous session's update check) and `EnsureXxxCachedAsync` returns early on the cache-hit path, the version field is never re-populated. `SaveXxxMeta` guarded by `if (!string.IsNullOrEmpty(_latestXxxVersion))` will then silently skip the write, leaving the meta file with the old version. Fix: call `FetchLatestXxxReleaseInfoAsync` immediately before `SaveXxxMeta` when the version field is null. Applies to ReLimiter and any other component using the same pre-cache + in-memory version pattern.
- **RE Framework update check uses `FirstOrDefault()` for version comparison** — `UpdateOrchestrationService` picks one REF record to compare against remote. Using `FirstOrDefault()` without ordering can pick the oldest stale record (e.g. a game not in the active library, like an old DMC5 install at build 01320 while remote is 01394), causing the update check to always fire on every session. Fix: `OrderByDescending` on `InstalledVersion` so the newest installed build is used. Log which game/version is being used for the comparison to aid future debugging.
- **INI merge hotkey preservation (reverted)**: The merge loop in `MergeRsIni`/`MergeRsVulkanIni` does NOT skip `KeyOverlay`/`KeyScreenshot` — RHI's configured value always wins. Do not add preserve-existing logic here; the design decision is that in-game hotkey changes are not reliable (especially for Vulkan multi-swapchain games) and must be done via RHI Settings.
- **`ApplyOverlayHotkey` / `ApplyScreenshotHotkey` always overwrite** — RHI's configured hotkey is the single source of truth. These functions unconditionally write the value; there is no preserve-existing logic. For Vulkan games with multiple swapchain inis, changing hotkeys in-game is unreliable (wrong swapchain may be active). Users must set hotkeys via the RHI Settings picker, which writes to all `reshade*.ini` files in the game folder. `C:\ProgramData\ReShade\` contains only DLLs and JSON manifests, NOT a reshade.ini — per-game Vulkan config lives in the game folder as usual.
- **Vulkan multiple swapchain ini files**: Some games (e.g. KOA: Reckoning) create multiple Vulkan swapchains (main window + tiny UI overlay). ReShade hooks every swapchain and assigns each its own numbered config: `reshade.ini` (first swapchain), `ReShade2.ini` (second), etc. When the game switches between swapchains (tab out/in), it switches which config is active. RHI fix: `MergeRsVulkanIni` propagates `[ADDON]`, `[GENERAL]`, `[SCREENSHOT]`, and `[INPUT]` hotkeys (`KeyOverlay`, `KeyScreenshot`) to all numbered `reshade*.ini` files after merging. RHI's hotkey always overwrites — numbered inis are ephemeral. The "Apply Overlay Hotkey" button in Settings already covers numbered inis via the `reshade*.ini` glob pattern.
- **Game Pass launch requires AUMID, not exe**: `DetectedGame.XboxAumid` is populated from `package.GetAppListEntries().FirstOrDefault().AppUserModelId` during Xbox detection. Without this, clicking Launch on a Game Pass game does nothing — direct `Process.Start(exe)` fails silently for packaged GDK apps because WindowsApps paths are access-denied. Always check `XboxAumid` before falling through to the exe scan step.
- **Drag-drop named mod over UE-Extended card**: When a non-generic addon (named mod from Discord) is drag-dropped onto a card that had UE-Extended enabled, `DragDropHandler.Addon.cs` must: (1) set `UseUeExtended = false` on the card, (2) call `RemoveFromUeExtendedGames(gameName)` to persist the change, (3) update `Mod` to a Discord-style GameMod with `DiscordUrl` set, (4) set `IsExternalOnly = true` and `ExternalLabel = "Download from Discord"` so the button shows "Redownload from Discord" instead of "Reinstall UE-Extended".
- **Named addon on disk with generic UE fallback**: When a named addon file (e.g. `renodx-mygame.addon64`) exists on disk but no wiki entry exists, the game gets `effectiveMod = genericUnreal` (from fallback). Even though `hasNamedAddonOnDisk` blocks `useUeExt`, the generic fallback has a `SnapshotUrl`, so the button shows "Reinstall RenoDX" instead of "Download from Discord". Fix: after `hasNamedAddonOnDisk` is calculated, check if `hasNamedAddonOnDisk && effectiveMod?.IsGenericUnreal == true` — if so, replace `effectiveMod` with a Discord-style GameMod (`DiscordUrl` set, `SnapshotUrl` null). This ensures `IsExternalOnly = true` and the button shows the Discord link. Applied in both `BuildCards` and `AddManualGame`.
- **WinUI 3 layout jumps from Visibility.Collapsed**: Using `Visibility.Collapsed` to hide optional UI elements (e.g. warning text) causes the parent container to resize when they appear/disappear. For elements that conditionally appear within a fixed-size dialog (like RTX HDR Settings), use `Opacity = 0.0` / `Opacity = 1.0` instead — the element still occupies its space when invisible, preventing layout jumps.
- **DXVK uninstall must clear VulkanRenderingPaths**: When Lilium HDR DXVK is installed, `VulkanRenderingPath = "Vulkan"` is persisted to `settings.json`. On uninstall, `SetVulkanRenderingPath(gameName, "DirectX")` must be called unconditionally to clear the persisted entry. Bug pattern: guarding the clear call with `if (card.VulkanRenderingPath == "DirectX")` is backwards — the card is still "Vulkan" at that point, so the condition fails and the setting is never cleared. Result: game remains stuck installing Vulkan ReShade even after DXVK removal.
- **`lumaRenodxCompat` uses exact string matching**: Unlike other manifest lookups that use `MatchesGameSet()` with normalization, the `LumaRenodxCompat` list check does a direct `Contains(game.Name)` against the raw manifest list. Game names must match exactly — e.g. Steam folder "Mass Effect Andromeda" (no colon) won't match manifest entry "Mass Effect: Andromeda" (with colon). Always use the exact detected game name in the manifest.
- **CacheLoad must mirror BuildCards for manifest-driven card properties**: Properties set from manifest data in `BuildCards` (like `LumaRenodxCompatible`) must also be set in `LoadCacheAndBuildCardsAsync` using `cachedManifest`. Otherwise the property is false during Phase 1 (cache display) and only becomes true after Phase 2 merge — causing UI issues like missing rows until background scan completes.
- **Manifest dictionary keys must not have case-only duplicates**: `ManifestService.Normalize()` re-creates dictionaries with `StringComparer.OrdinalIgnoreCase` for forgiving game-name lookups. If the JSON contains two keys that differ only by case (e.g. `Watch_Dogs2` and `WATCH_DOGS2`), deserialization succeeds but `Normalize()` throws `ArgumentException: An item with the same key has already been added`. This causes the app to fall back to the stale cached manifest, silently ignoring all manifest updates. Always check for case-colliding keys before adding new entries to dictionary sections like `installPathOverrides`.
- **Dual initialization paths**: `InitializeAsync` has two code paths: (1) cached path — calls `LoadCacheAndBuildCardsAsync` then fires off `RunBackgroundScanAndMergeAsync`; (2) full scan path — runs when `forceRescan=true` OR no cache exists. Features that need to run on both app launch AND refresh (e.g. wiki fetch, new mods detection) must be added to BOTH `RunBackgroundScanAndMergeAsync` AND the full scan section of `InitializeAsync`. The 4-hour periodic timer in `StartPeriodicUpdateCheckTimer` is a third path that also fetches wiki/manifest but doesn't rebuild cards.
- **WinUI 3 PNG transparency lost on large images**: When displaying a large PNG (1MB+, e.g. 1024x1024) scaled down to a small size (16x16), WinUI 3 can lose transparency and show a white/gray background. Fix: use appropriately sized source images (32x32 or 64x64 for icons) instead of relying on runtime downscaling.
- **New asset files must be added to .csproj**: Adding a new file to `Assets/icons/` (or any content folder) does NOT automatically include it in the build. Each asset must have an explicit `<Content Include="...">` entry with `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>` and `<ExcludeFromSingleFile>true</ExcludeFromSingleFile>` in `RenoDXCommander.csproj`. Without this, the file won't appear in the publish output.
- **publish.bat must copy new asset file types**: The `publish.bat` script manually copies assets to the publish folder. If adding a new file type (e.g. PNG icons when previously only ICO existed), add a corresponding `copy` line to `publish.bat` — e.g. `copy /y "%SRC%\Assets\icons\*.png" "%OUT%\Assets\icons\" >nul`.
- **Imperative UI builders ignore property notifications**: `DetailPanelBuilder` and `CompactViewBuilder` use code-behind to build UI imperatively — they do NOT use data binding. Changing an `[ObservableProperty]` on a card (like `AuthorList` or `IsLumaMode`) fires `PropertyChanged` but the panel won't update because there's no binding listening. Fix: after changing properties that affect the detail panel, call `PopulateDetailPanel(card)` and/or `BuildOverridesPanel(card)` to rebuild the panel with current values. This applies to Luma mode toggles, RTX HDR toggles, and similar state changes.
- **Multi-store composite keys: preserve legacy keys as-is**: Don't migrate legacy per-game setting keys from `"GameName"` to `"GameName|"`. Legacy keys (without pipe) work for single-store users and coexist with new composite keys (`"GameName|Steam"`, `"GameName|Xbox"`). The migration pattern that appended `|` to all keys caused lookup failures because code searched for `"GameName"` but settings were stored as `"GameName|"`. Lookups use exact key matching, so legacy keys must remain unchanged.
- **Multi-store card matching must include store**: When finding a card for operations like `IsRunning` detection, match BOTH game name AND store: `c.GameName.Equals(name) && (string.IsNullOrEmpty(store) || c.Source == store)`. Using name-only matching picks the first card in the list when multiple stores exist.
- **MergeCards must use composite keys**: The background scan merge in `MainViewModel.BackgroundScan.cs` must use `GameKey.FromCard(name, source).ToKey()` for lookups and stale detection, not just `GameName`. Using name-only matching causes one store's data to overwrite another when the same game exists on multiple stores.
- **`OverridesPanelCtx` fields with `= null!` must be assigned in builder methods**: Fields declared as `public ComboBox SomeCombo = null!;` start as null despite the `null!` annotation. If any builder method fails to assign the field, accessing it in the reset action throws a NullReferenceException. The exception is silently swallowed by WinUI event handlers, causing the reset action to stop partway through. Symptom: some overrides reset (shaders, addons) but others don't (bitness, API, channel). Always verify every ctx field is assigned where expected. Example: `ctx.UpdateSummaryText` was never assigned from `BuildRsChannelSection`, causing the reset action to abort at `UpdateInclusionHelper.RefreshSummary(ctx.UpdateSummaryText, ...)` before reaching bitness/API/channel resets.

### File Split Conventions
- Partial class pattern: `FileName.SectionName.cs`
- Target: no source file over 50KB (auto-generated excluded)
- Grid View removed. Layout cycles Detail ↔ Compact only.
- Only ONE overrides builder exists (`DetailPanelBuilder`)

### Service Injection
- `DetailPanelBuilder` receives services via constructor injection
- `MainWindow` resolves from DI in constructor, stores as private fields
- `AddonPackService` is NOT in DI — resolve via `ViewModel.AddonPackServiceInstance`
- New dependencies: resolve from DI in consumer's constructor

### Store Detection
- Games in `steamapps\common\` paths always get Steam badge
- EA/Ubisoft registry can detect Steam games — path override fixes this
- **Ubisoft Connect uses folder names as game names** — unlike Steam (which has display names in appmanifest), Ubisoft registry keys are numeric IDs with no display name. `FindUbisoftGames()` uses `Path.GetFileName(gameDir)` as the detected name. Manifest entries (installPathOverrides, blacklist, etc.) must match the exact folder naming. Example: Watch Dogs 2 folder is `Watch_Dogs2` (no space), not `Watch_Dogs 2`.

### Game Launch Priority
1. User override (absolute path)
2. Manifest override (relative to InstallPath)
3. Steam (`steam://rungameid/{appId}` or `-applaunch {appId} {args}`)
4. Epic protocol (`com.epicgames.launcher://apps/{AppName}?action=launch&silent=true`)
5. Xbox/Game Pass — `shell:AppsFolder\{XboxAumid}` — AUMID captured from `package.GetAppListEntries()` during `FindXboxGames`. `DetectedGame.XboxAumid` stores it. Direct exe launch does NOT work for Game Pass games — WindowsApps paths are access-denied and packaged GDK apps require AUMID activation.
6. Auto-detected exe (largest in InstallPath)

### Last Selected Game
- `LastSelectedGameName` must NOT be nulled after consumption (background merge re-uses it)
- `HasInitialized` guard prevents overwriting during startup

### Permanent Exclusions
- `GameInitializationService.cs` `permanentExclusions` HashSet — independent of manifest `blacklist`
- Both must be checked when unblocking a game

### RenoDX Addon Exclusions
- Global exclusion list for non-game-specific RenoDX addons: `renodx-upgrade`, `renodx-devkit`, `renodx-dlssfix`, `renodx-dlss5`, `renodx-universal_ue`
- Use `IsGameSpecificRenodxAddon()` helper to filter — checks that mod name isn't in global exclusions
- Applies to scan methods and drag-drop handler

### Ryubing Emulator Bundle
- Ryubing addon names in `emulatorGames.Ryubing.addons` are wiki game names used for display and wiki lookup
- Install resolves URLs: manifest `addonUrls` dict takes priority over wiki-scraped `_allMods` list
- Use `addonUrls` in manifest when a Souperman9 addon isn't on the RenoDX wiki (e.g. multi-game addons)
- One `.addon64` file can cover multiple games — list each game name in `addons` and point all their `addonUrls` entries to the same URL
- Card title "Ryubing (N games)" is hardcoded in BuildCards, CacheLoad, and Install.cs — update all three if count changes

### Engine Hint Overrides (UE4 vs UE5)
- `engineHintOverrides` manifest field maps game name → engine version string (e.g. `"4.27.2"`, `"5.3.2"`)
- UE4 games on fresh UE-Extended install: `Set_Path=1` (SDR upgrade), skip Engine.ini HDR keys
- UE5 games: `Set_Path=0` (native HDR), Engine.ini HDR keys deployed
- Engine badge `isClickable` must be false when manifest specifies engine version (no toggling allowed)
- Check both manifest override AND installed record when determining badge behavior

### UE-Extended Default Behavior
- **UE-Extended is now the DEFAULT for all generic UE games** (games with `effectiveMod.IsGenericUnreal == true` — no named mod, just the engine fallback). This applies in BuildCards, AddManualGame, and ToggleWikiExclusion.
- The `useUeExt` decision logic (priority order):
  1. `noUeExtended` gate: manifest `noUeExtendedGames` OR user `_ueExtendedOptOutGames` → always standard generic (blocks all other conditions)
  2. Addon already on disk (`addonOnDisk == UeExtendedFile`) → UE-Extended
  3. Explicit user opt-in (`_ueExtendedGames`) → UE-Extended
  4. NativeHDR game → UE-Extended
  5. `effectiveMod.IsGenericUnreal == true` → UE-Extended (new default)
- **UE-Extended UI is a ComboBox in the RenoDX ⚙ cog dialog** (`MainWindow.Events.Components.cs`), not a standalone button. Items: `"Off"` (index 0 = standard generic) and `"On"` (index 1 = UE-Extended). Visible when `card.UeExtendedToggleVisibility == Visibility.Visible`. The Row 7b `UeExtendedLabel` button (`"⚡ UE Extended"` / `"⚡ Standard UE"`) exists on the card row but the primary control is the cog ComboBox. Both call `ViewModel.ToggleUeExtended(card)`.
- **User opt-out persistence**: toggling OFF adds to `_gameNameService.UeExtendedOptOutGames` (persisted as `"UeExtendedOptOutGames"` in settings.json). This is separate from `_ueExtendedGames` (explicit opt-ins). Both migrate via `MigrateHashSet` in `RenameGame()`.
- **Manifest escape-hatch**: `noUeExtendedGames` list in manifest forces standard generic for specific games without requiring a client update.
- **Named mods always take priority over UE-Extended**: `_ueExtendedGames` persists across sessions. If a game was ever toggled to UE-Extended (manually or via NativeHDR auto-add), it stays in the set permanently. On refresh, `IsUeExtendedGameMatch` fires and overrides even a named mod that has since been installed. Two conditions block UE-Extended: (1) `hasNamedMod` — wiki-matched mod with a SnapshotUrl; (2) `hasNamedAddonOnDisk` — any `.addon64` on disk that is NOT `renodx-ue-extended.addon64` or `renodx-unrealengine.addon64` (covers Discord drag-drops where SnapshotUrl is null but the file is a real named mod). Both checks must be present since Discord mods have DiscordUrl but no SnapshotUrl.
- `ApplyManifest` in `GameInitializationService` takes `manifestNoUeExtendedGames` as a parameter (same pattern as `manifestNativeHdrGames`) and clears+populates it from `manifest.NoUeExtendedGames`.

### Cross-Privilege Installer Shutdown
- Signal file: `%LocalAppData%\RHI\rhi_shutdown_requested`
- RHI polls via `DispatcherTimer` every 2 seconds, exits gracefully if file exists
- Inno Setup `[Code]` section writes signal file and polls for RHI exit
- Takes effect from version after signal file check is added (e.g., v2.2.2 plants listener, v2.2.3+ uses it)

### Admin Mode Relaunch Limitations
- `schtasks /Run` cannot pass extra command-line arguments to the scheduled task
- Any state that needs to survive the Admin Mode relaunch must use a signal file in `%LocalAppData%\RHI\`
- Pattern: write signal file before `Environment.Exit(0)`, check + delete it at the top of `OnLaunched`
- Example: `rhi_start_minimized` signal passes `--minimized` across the elevation boundary

### DXVK Installation
- All DXVK variants now use direct mode: `d3d9.dll` + Vulkan layer
- No more proxy mode (`dxvk.dll` wrapper) — eliminated for simplicity
- Legacy `IsProxyMode = true` records still handled on uninstall for backwards compatibility
- Persistence checks use `InstalledDlls.Contains("d3d9.dll")`

### VRR Presets + Custom Value Pattern
- For FPS/numeric settings, use a ComboBox with VRR-optimal presets (48, 60, 72, 90, 120, 144, 165, 180, 240, 288, 324, 360, 480)
- Add "Custom..." as the last option in the dropdown
- When "Custom..." is selected, show an inline TextBox + "Set" button underneath (NOT a nested ContentDialog)
- Store values as strings; "Global" or empty means inherit/default
- Apply immediately on Set button click or Enter key press

### Custom ReShade Auto-Redeploy
- `CustomReShadeHashService` tracks SHA256 hashes of DLLs in `%LocalAppData%\RHI\Custom\ReShade\`
- Hash file: `custom_reshade_hashes.json` lives in the Custom\ReShade folder itself (user-visible, deletable)
- `EnsureInitialized()` called in MainWindow constructor (alongside `EnsureInisDir`, `EnsureReShadeStaging`) — seeds hash file on first launch without redeploying
- `CheckAndRedeploy()` triggered on Refresh and every 4 hours (periodic timer in `MainViewModel.Update.cs`)
- DX games: direct `File.Copy()` to game folder using `card.RsRecord.InstalledAs`
- Vulkan games: `CopyFileWithElevation()` to `C:\ProgramData\ReShade\ReShade64.dll`
- Per-game custom DLL selection stored in `GameNameService.CustomReShadeSelection` dict (game name → filename)
- Resolved via `DI` as `App.Services.GetRequiredService<CustomReShadeHashService>()` (same pattern as DofFixService)

### RTX HDR Integration
- Per-game toggle in the RenoDX cog dialog (bottom, always visible on all games)
- **Requires NVIDIA App installed** (not just the driver) — the HDR injection pipeline is part of the NVIDIA App overlay. Settings written to the profile do nothing without it. Also requires Game Filter/Freestyle enabled in NVIDIA App settings, RTX GPU, driver 550+.
- **Overlay detection**: `HKCU\SOFTWARE\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS\IsShadowPlayEnabled` (REG_BINARY, 01000000=on, 00000000=off). This is the umbrella toggle for the NVIDIA overlay. Game Filters is a sub-feature that requires overlay ON, but has no separate registry toggle — it's controlled internally by the NVIDIA App.
- **RTX HDR formula**: `midGreyNits = paperWhiteNits × (0.5 ^ gamma)` where gamma maps from the Contrast setting: Gamma 2.0 = Contrast 0, Gamma 2.2 = Contrast +25, Gamma 2.4 = Contrast +50. Paper white is derived from peak brightness using the ITU lookup table: (400→101, 600→138, 800→172, 1000→203, 1500→276, 2000→343 nits). The Middle Grey API value (10–100) IS in nits directly. `CalcAutoMiddleGrey(peakNits, contrastVal)` implements this — interpolates paper white from the table, applies the formula, clamps to 10–100. The **reverse formula** `paperWhite = midGrey / (0.5 ^ gamma)` calculates perceived paperwhite from middle grey — displayed in the Configure dialog as "Middle Grey: 44 (203 nits)". C# closures cannot reference local variables declared later in the same method — forward references in lambdas cause CS0841. Wire auto-update events after the target variable is declared.
- Card property: `IsRtxHdrEnabled` (ObservableProperty) — set in BuildCards, merged in BackgroundScan
- NVIDIA profile settings via `DlssPresetService`: `RTX_HDR_ENABLE_ID` (0x00DD48FB) — set to 0x01 on enable, 0x00 on disable. `RTX_HDR_ALLOW_ID` (0x1077A11A) is NOT needed and should NOT be written — it shows as "Disallow" in the driver and has no effect on RTX HDR functionality.
- Additional settings: Contrast (0x00DD48FE), Saturation (0x00DD48FF), Peak Brightness (0x00DD48FC), Middle Grey (0x00DD48FD), Debanding (0x00432F84)
- Value encoding: Contrast/Saturation store `100 + display_value` (display -100 to +100, stored 0-200). Peak Brightness stores nits directly (400-2000). Middle Grey stores raw value (10-100, default 50). Debanding: No=0x06, Low=0x0A, High=0x02, High+Indicator=0x03, High+Indicator+Debug=0x23
- **CRITICAL**: RTX HDR settings MUST use `SetSettingRawNvApi` (raw NVAPI), NOT `SetPreset` (NvAPIWrapper). NvAPIWrapper's `profile.SetSetting()` silently succeeds without actually writing newer setting IDs. Same root cause as the ReBAR write issue.
- **Deleting settings**: NvAPIWrapper's `profile.DeleteSetting()` also silently fails for newer IDs. Use `DeleteSettingRawNvApi` (via `NvAPI_DRS_DeleteProfileSetting`, function ID `0xE4A26362`). Public method: `DeleteSettingRaw(gameName, installPath, settingId)`.
- **Delete limitations**: Some setting IDs (0x00DD48FB–FF) return -160 (SETTING_NOT_FOUND) on delete despite being writable. Only `0x00432F84` (Debanding) is deletable. For non-deletable settings, write their default values instead (Enable=0, Contrast=100, Saturation=100, PeakBrightness=0, MiddleGrey=50).
- **Debanding requires admin** — `0x00432F84` can only be written/deleted when running as admin. Grey out the Debanding control with `IsEnabled = false, Opacity = 0.4` when `!VulkanLayerService.IsRunningAsAdmin()` and show a small note. Pattern matches how ReBAR controls are disabled in `SettingsHandler.cs`.
- **Export/Import**: All 7 RTX HDR IDs (`0x1077A11A`, `0x00DD48FB`–`0x00DD48FF`, `0x00432F84`) must be in `ManagedSettingIds` in `DlssPresetService.Export.cs` for the profile backup to include them. The export's raw NVAPI second-pass loop handles reading them. The import must also bypass `profile.SetSetting` for these IDs (silently fails without throwing) — use a `rawOnlyIds` HashSet in the import loop to route them directly to `SetSettingRawNvApi`. The exception-based fallback in the generic import path does NOT work for these IDs because no exception is thrown.
- **Setting ID source**: Get IDs from NVPI's `CustomSettingNames.xml` (in the NVPI app folder at `C:\Users\Mark\OneDrive\Desktop\NVPI Revamped App\`). Raw NVAPI `NvAPI_DRS_SetSetting` returns success even for non-existent setting IDs — always verify in NVPI that values actually changed.
- "Configure RTX HDR" button opens `RtxHdrConfigButton_Click` dialog (separate from cog dialog)
- Enabling RTX HDR uninstalls any active RenoDX mod (mutual exclusivity)
- Enabling RTX HDR writes ALL settings with sensible defaults: Allow=1, Enable=1, PeakBrightness=global nits (or 510), Contrast=125 (Gamma 2.2), Saturation=75 (-25, reduces oversaturation), MiddleGrey=ITU-calculated for Gamma 2.2 at the user's peak nits. User can tweak via Configure dialog after.
- Disabling RTX HDR does NOT reinstall RenoDX
- Disabling RTX HDR DELETES all 7 settings from the profile via `DeletePresetPublic()` (reverts to global/inherited) — do NOT write 0x00 explicitly
- `InstallActionLabel` returns "Configure RTX HDR" when `IsRtxHdrEnabled` is true
- `CanInstall` must return true when `IsRtxHdrEnabled` is true (otherwise button is greyed out since no mod exists)
- `rdxGreyed` in `DetailPanelBuilder.Components.cs` must check `!card.IsRtxHdrEnabled` before applying grey (otherwise row is 35% opacity when RS not installed)
- `IsHitTestVisible` on `DetailRdxInstallBtn` must be true when `IsRtxHdrEnabled` (button must be clickable even without ReShade)
- `InstallButton_Click` in `InstallEventHandler.cs` intercepts: when `IsRtxHdrEnabled`, routes to `RdxCogButton_Click` (opens cog dialog) instead of installing a mod
- `hasRenoDxMod` in cog dialog: `!card.IsRtxHdrEnabled && (card.Mod?.SnapshotUrl != null || card.Status == Installed/UpdateAvailable)` — hides nits/presets for both RTX HDR active and no-mod-available cases
- When no RenoDX mod is available, cog dialog hides nits/presets sections (uses `hasRenoDxMod` flag)
- **RTX HDR state is read from the driver, not just from `_rtxHdrGames`**: `IsRtxHdrEnabled` is reconciled against the live NVIDIA driver profile (Allow=1 AND Enable=1) at two points: (1) post-loop in `BuildCards` for all games, (2) when the cog dialog opens. `_rtxHdrGames` is updated to match so the persisted set stays in sync. This handles driver updates or NVIDIA App changes clearing RTX HDR settings outside RHI.

- **Engine.ini cog section requires UseUeExtended fallback**: The Engine.ini Settings section in the RenoDX cog dialog (`MainWindow.Events.Components.cs`) uses `card.EngineHint?.Contains("Unreal")` to decide visibility. This fails for UE-Extended games where the engine hint is absent or doesn't contain "Unreal". Fix: use `(card.EngineHint?.Contains("Unreal") == true || card.UseUeExtended)` as the condition. Also include `UpdateAvailable` alongside `Installed` in the status check — the original `== GameStatus.Installed` missed games with pending updates.
- **MergeCards must use composite keys**: The background scan merge in `MainViewModel.BackgroundScan.cs` must use `GameKey.FromCard(name, source).ToKey()` for lookups and stale detection, not just `GameName`. Using name-only matching causes one store's data to overwrite another when the same game exists on multiple stores.
- **Multi-store card matching must include store**: When finding a card for operations like `IsRunning` detection, match BOTH game name AND store: `c.GameName.Equals(name) && (string.IsNullOrEmpty(store) || c.Source == store)`. Using name-only matching picks the first card in the list when multiple stores exist.
- **Multi-store composite keys: preserve legacy keys as-is**: Don't migrate legacy per-game setting keys from `"GameName"` to `"GameName|"`. Legacy keys (without pipe) work for single-store users and coexist with new composite keys (`"GameName|Steam"`, `"GameName|Xbox"`). Lookups use exact key matching, so legacy keys must remain unchanged.
- **Imperative UI builders ignore property notifications**: `DetailPanelBuilder` and `CompactViewBuilder` use code-behind to build UI imperatively — they do NOT use data binding. Changing an `[ObservableProperty]` on a card fires `PropertyChanged` but the panel won't update because there's no binding listening. Fix: after changing properties that affect the detail panel, call `PopulateDetailPanel(card)` and/or `BuildOverridesPanel(card)` to rebuild the panel with current values.

- **`snapshotOverrides` blocks UE-Extended for NativeHDR games**: `snapshotOverrides` runs before the UE-Extended logic in `BuildCards`. When it injects a `SnapshotUrl` onto `effectiveMod`, `hasNamedMod` becomes `true` (because `SnapshotUrl != null && !IsGenericUnreal`), which blocks `useUeExt` entirely — even for NativeHDR games that should auto-use UE-Extended. Fix: never add NativeHDR generic UE games to `snapshotOverrides`. Remove the entry and let the NativeHDR → UE-Extended path in BuildCards handle it automatically. The URL in `snapshotOverrides` doesn't matter — even pointing it at `renodx-ue-extended.addon64` still breaks it because the code treats any `SnapshotUrl` as a named mod.

- **Vulkan/OpenGL Present Method setting**: ID `0x20D690F8`. Values: `0x00000000` Native, `0x00000001` Preferred layered on DXGI Swapchain, `0x00000002` Auto (default). Per-game driver profile setting. Use `SetRtxHdrRaw` pattern (which handles profile create-if-missing). Exposed via `GetVulkanPresentMethod` / `SetVulkanPresentMethod` on `DlssPresetService`. This setting ID IS in the NVPI `CustomSettingNames.xml` files — use `GetPreset` to read (NvAPIWrapper can see it). The companion "Flags" setting (`0x000802A5 = Treat DXVK as Native`) is NOT in the NVPI XML files — ID unknown, needs live NVAPI enumeration to discover.

- **Engine.ini platform folder differs by store**: When `ResolveEngineIniDir` falls back to creating a config folder (no existing config found), it must use `WinGDK` for Xbox/Game Pass games and `Windows` for all other stores. The `store` parameter threads through `ResolveEngineIniDir`, `ApplyEngineIniHdrSettings`, `ApplyEngineIniLutSetting`, `RemoveEngineIniHdrSettings`, and `RemoveEngineIniLutSetting`. All call sites pass `card.Source`. Detection: `string.Equals(store, "Xbox") || string.Equals(store, "Game Pass")`.

- **NativeHDR takes priority over named wiki mods for UE-Extended**: The `hasNamedMod` check in `BuildCards` normally blocks UE-Extended when a wiki mod exists. However, `nativeHdrGames` is a manifest-level declaration that overrides this — `useUeExt = !noUeExtended && (!hasNamedMod || isNativeHdr) && !hasNamedAddonOnDisk && (... || isNativeHdr || ...)`. NativeHDR bypasses `hasNamedMod` so the game uses UE-Extended even if the wiki has a named mod. The named mod's Nexus/wiki links still show on the card. Exception: `hasNamedAddonOnDisk` still blocks even NativeHDR — respects user drag-drop installs. `wikiUnlinks` is NOT the right fix for NativeHDR + named mod conflicts.

## ueExtendedCompatibility Manifest Field

New field (v2.2.9+) for per-game UE-Extended control. Takes highest priority over `nativeHdrGames` and `ueExtendedGames`.

**Schema:**
```json
"ueExtendedCompatibility": {
  "Beast of Reincarnation": { "hdr": false },
  "Borderlands 4": {},
  "Some UE4 Game": { "hdr": false }
}
```

**Behavior:**
- Presence in dict = forced UE-Extended (same as `nativeHdrGames`)
- `hdr: false` = skip Engine.ini HDR keys on install/update (LUT still deploys)
- No `hdr` key = default: UE5 gets HDR+LUT, UE4 gets LUT only
- `lut: false` = skip LUT deploy too
- Entries are also added to `manifestNativeHdrGames` internally so `isNativeHdr=true` fires everywhere (toggle hidden, named mod bypass, correct card label)
- Old clients ignore the field entirely — zero breaking changes

**Implementation:**
- `RemoteManifest.cs` — `UeExtendedCompatibility` dict + `UeExtendedCompatEntry` model
- `MainViewModel.cs` — `_manifestUeExtendedCompat` dict field
- `GameInitializationService.ApplyManifest()` — populates dict, adds to `manifestNativeHdrGames`
- `MainViewModel.Install.cs` — `deployHdr`/`deployLut` computed from compat entry
- `UpdateOrchestrationService.cs` — same via `AuxInstallService.GlobalManifest?.UeExtendedCompatibility`

**Migration plan (3 phases):**
- Phase 1 (done): Add `ueExtendedCompatibility` alongside `nativeHdrGames`/`ueExtendedGames`. Games can exist in both during transition.
- Phase 2 (after all users updated): Move all `nativeHdrGames` + `ueExtendedGames` entries into `ueExtendedCompatibility`. New clients check compat first, fall back to old fields.
- Phase 3 (cleanup): Remove `nativeHdrGames` and `ueExtendedGames` from manifest and code.

- **WinUI 3 Button visual state overrides programmatic brush**: Setting `Button.Background`/`Foreground`/`BorderBrush` directly doesn't visually update when the button is in `Normal` state — WinUI's visual state machine re-applies its theme brushes on state transitions. The button only shows the programmatic color after a hover because `PointerOver` → `Normal` triggers re-evaluation. Fix: call `VisualStateManager.GoToState(button, "Normal", false)` immediately after setting the brushes to force WinUI to re-run the Normal state and pick up the new values. Applied to `UpdateBtn` in `MainWindow.UISync.cs`.

- **`CustomReShadeSelectionResolver` must use composite keys**: `CustomReShadeSelection` stores keys as `"GameName|Store"` (composite, set by `DetailPanelBuilder.Overrides.RsChannel.cs`), but the original resolver only looked up by `gameName` — always returning null after multi-store migration. The resolver signature was changed to `Func<string, string, string?>` taking `(gameName, store)`. The resolver tries composite key first, falls back to name-only for legacy entries. The call site in `AuxInstallService.Install.cs` passes `store` parameter. Symptom: custom ReShade install silently stuck at "Preparing ReShade files..." — the custom DLL path resolved to the default `ReShade64.dll` filename which doesn't exist in the Custom folder, causing a `FileNotFoundException` that briefly showed an error message then faded.

- **Composite key audit findings (v2.3.0 fix)**: Several places were reading composite-keyed dicts with name-only keys. Confirmed bugs found and fixed across two audit rounds: (1) `ApplyFolderOverrides` used `g.Name` to look up `_folderOverrides`. (2) `ToggleUpdateAllExclusionUl` used `set.Contains(gameName)` instead of `set.Contains(key)`. (3) `SetReShadeChannelOverride` auto-exclusion path used raw `gameName` against composite-keyed `_updateAllExcludedReShade`. (4) `GameNameService.RenameGame` called `MigrateCompositeDict` for `HdrToggleOverrides`, `LaunchExeOverrides`, `LaunchArgsOverrides`, `EngineVersionOverrides` — changed to `MigrateDict`. (5) `GameReportEncoder` looked up all per-store overrides with `gameName`. (6) `CustomReShadeHashService.GetCustomChannelGames` used `card.GameName` to look up `CustomReShadeSelection`. (7) `ResolveShaderSelection` used plain `gameName` for `PerGameShaderSelection` — added `store` parameter. (8) `ApplyPresetShadersAsync` wrote `PerGameShaderSelection` with plain `gameName` — added `store` parameter. (9) `DetailPanelBuilder.Overrides.cs` shader picker used `gameName` for `PerGameShaderMode.ContainsKey` and `PerGameShaderSelection` reads/writes. (10) `DetailPanelBuilder.Overrides.ShadersAddons.cs` addon picker used `gameName` for `PerGameAddonSelection` reads/writes. Fix pattern for all: try composite key first (`GameKey.FromCard/From(...).ToKey()`), fall back to name-only for legacy entries.
- **Name-only vs composite-keyed fields**: `HdrToggleOverrides`, `LaunchExeOverrides`, `LaunchArgsOverrides`, `EngineVersionOverrides` are **name-only** (not per-store) — all read/write sites use plain game name. `CustomReShadeSelection`, `FolderOverrides`, `BitnessOverrides`, `ApiOverrides`, `ReShadeChannelOverrides`, `DxvkVariantOverrides`, `LiliumPresetOverrides`, `VulkanRenderingPaths` are **composite-keyed**. When adding new per-game lookups, check the migration in `RenameGame` to confirm which pattern is being used, then ensure ALL read/write sites match.

- **`DllOverrideService` is name-only — not composite-keyed**: `DllOverrideService._dllOverrides` uses plain game names as keys (name-only), unlike most other per-game settings. `BuildCards` and `CacheLoad` were incorrectly using the composite `gameKey` to check `_dllOverrides.ContainsKey()`, so `DllOverrideEnabled` was always `false` after refresh. This caused `UpdateAllReShadeAsync` to ignore the user's custom DLL filename and redeploy `dxgi.dll`. Fix: use `game.Name` (not `gameKey`) for `_dllOverrides.ContainsKey()` in both `BuildCards` and `CacheLoad`. Same applies to `GetDllOverride(card.GameName)` — always pass plain name, never composite key.

- **`shaderResolver` delegate includes store**: `IUpdateOrchestrationService.UpdateAllReShadeAsync` takes `Func<string, string, string?, IEnumerable<string>?>? shaderResolver` — signature is `(gameName, store, shaderModeOverride)`. Call site in `MainViewModel.Update.cs` passes `(gameName, store, shaderModeOverride) => ResolveShaderSelection(gameName, shaderModeOverride, store)`. Required because `ResolveShaderSelection` now uses composite key for `PerGameShaderSelection` lookup.

- **Shader mode combo missing store in `DetailPanelBuilder.Overrides.cs`**: The "Global", "Custom", and "Off" branches of the shader mode `SelectionChanged` handler called `SetPerGameShaderMode(capturedName, mode)` without `card.Source`. The saved key was `"GameName|"` but the panel read back with `"GameName|Store"` — so the setting always appeared to revert. Fix: pass `card.Source ?? ""` as the third argument. Same issue existed in the Reset Overrides handler in `ShadersAddons.cs` for `GetPerGameShaderMode`, `SetPerGameShaderMode`, and `PerGameShaderSelection.Remove` — all needed composite key. **Rule: any `Set*` or `Get*` call in a UI handler that has `card` or `ctx.Card` in scope MUST pass `card.Source` / `ctx.Card.Source`. Search for `SetPerGame*(name, "value")` with no third arg as the detection pattern.**

- **Legacy name-only keys persist alongside composite keys**: Settings written before v2.2.9 (e.g. RS channel = "Custom", addon mode = "Select") were stored as `"GameName"` (name-only). After migration, new writes use `"GameName|Store"`. When resetting or reading overrides, both keys must be checked/cleared. Pattern for Get methods: try composite key first, then name-only fallback. Pattern for Set/remove operations: always `.Remove(compositeKey)` AND `.Remove(gameName)`. Affected fields confirmed: `_reShadeChannelOverrides`, `_perGameShaderMode`, `_perGameAddonMode`, `PerGameShaderSelection`, `PerGameAddonSelection`, `CustomReShadeSelection`. If a Reset Overrides button appears to do nothing, the setting is almost certainly stored as a legacy name-only key that the composite-only remove missed.

- **WinUI 3 automation peer `Invoke()` silently fails on `Visibility.Collapsed` buttons**: `ButtonAutomationPeer.Invoke()` does nothing when the target button has `Visibility.Collapsed`. The "Reset Overrides" button was hidden (`Visibility.Collapsed`) and the Management panel button used `peer?.Invoke()` to trigger it — this was a silent no-op, meaning Reset Overrides never worked. Fix: store the reset logic as an `Action` delegate in `OverridesPanelCtx.ResetAction`, wire both the hidden button's Click and the Management panel button's Click to call it directly. Never use automation peer invoke to trigger collapsed UI elements.

- **Programmatic `SelectedItem` change on ComboBox fires SelectionChanged with local initializing flag = false**: When a reset handler sets `comboBox.SelectedItem = "value"` programmatically, the `SelectionChanged` handler fires immediately. If the handler uses a local `bool` flag (e.g. `channelComboInitializing`) to guard against re-entrancy, and that flag is `false` at reset time (set to false after the panel was built), the handler executes fully — in the RS Channel case this opened the Custom ReShade picker dialog mid-reset. Fix: sync the local initializing flag to `ctx.ChannelComboInitializing` (a shared field), set `ctx.ChannelComboInitializing = true` before changing `SelectedItem` during reset, and check both flags in the handler. Pattern: any programmatic combo change during a reset must be guarded with an initializing flag.

- **`OverridesPanelCtx` fields must be explicitly assigned**: `OverridesPanelCtx` has fields like `ChannelCombo`, `ShaderModeCombo` etc. declared with `= null!`. If the builder method that creates the combo doesn't assign `ctx.ChannelCombo = channelCombo`, the reset handler calling `ctx.ChannelCombo.SelectedItem = "value"` throws a NullReferenceException silently. Always verify every `ctx.*Combo` field is assigned in its corresponding builder method. The `= null!` declaration suppresses the null warning, making the bug invisible until runtime.

- **Resolution methods need store parameter for composite-keyed lookups**: `ResolveIs32Bit`, `DetectGraphicsApi`, and `_DetectAllApisForCard` in `MainViewModel.GameMatching.cs` all access composite-keyed dicts (`_bitnessOverrides`, `_apiOverrides`). Their signatures include a `store` parameter, and all call sites pass the store. Lookups try composite key first, then fall back to name-only for legacy entries. (Fixed in v2.3.1)
- **`LegacyReShadeVersions` migration checks both key types**: In `MainViewModel.Init.cs` and `BackgroundScan.cs`, the legacy RS version migration checks both `_reShadeChannelOverrides.ContainsKey(gameName)` AND `Keys.Any(k => k.StartsWith($"{gameName}|"))` to handle both legacy and composite-keyed overrides. (Fixed in v2.3.1)


### Wiki Scraping
- `WikiService.FetchAllAsync()` parses all `<table>` elements on the RenoDX wiki Mods page
- Tables are identified as mod tables by looking for "Links" or "Status" column headers
- The wiki has a "Deprecated mods" section with its own table — mods listed there are broken, abandoned, or superseded
- WikiService finds the `<h2>`/`<h3>` heading containing "deprecated" and skips all tables appearing after it (using `StreamPosition` comparison)
- Without this filter, deprecated mods (Outer Wilds, Death's Door, Sea of Thieves, etc.) would show up as available in RHI

- **`AddonInfoResolver.cs` routes manifest info fields by `AddonType`**: All `*GameInfo` manifest dicts (`reshadeGameInfo`, `reframeworkGameInfo`, etc.) are resolved via a single switch in `AddonInfoResolver.cs` keyed on `AddonType` enum. When searching for where a component's info dialog content comes from, look here first — not in the component's own service.

- **`reshadeGameInfo` shows in the ReShade info button dialog** — confirmed working. `gameNotes` is the equivalent for RenoDX (there is no `renodxGameInfo` — `gameNotes` IS the RenoDX info field). `installWarnings` is a separate mechanism that shows a blocking confirm dialog when clicking install (component keys: `"luma"`, `"renodx"` — NOT yet wired for `"reshade"`).
- **`installWarnings` manifest field + `CheckInstallWarningAsync`**: Shows a blocking confirm dialog before install. Structure: `{ "GameName": { "component": "message" } }`. `CheckInstallWarningAsync(gameName, component)` is fully generic — accepts any string key, just does a dict lookup. All 8 components are now wired: `"reshade"` (`InstallReShadeAsync`), `"renodx"` (`InstallModAsync`), `"relimiter"` (`InstallUlAsync`), `"dc"` (`InstallDcAsync`), `"optiscaler"` (`InstallOsButton_Click` in InstallEventHandler), `"reframework"` (`InstallREFrameworkAsync`), `"dxvk"` (`InstallDxvkAsync`), `"luma"` (`InstallLumaAsync`).


## Manifest Architecture (from full field audit)

- **`optiScalerDllOverrides` is declared but not consumed**: The field exists in `RemoteManifest.cs` and `ManifestService.Normalize()` does NOT rebuild it with OrdinalIgnoreCase — it's reserved for future use. Any entries added to the manifest will silently have no effect.
- **`dxvkApiOverrides` value is not consumed**: Only existence is checked (`card.HasDxvkApiOverride = true/false`). The actual string value (`"DX9"`, `"DX12"`, etc.) is stored in the manifest but nothing reads it. The DXVK installer uses `card.GraphicsApi` (from `graphicsApiOverrides`) for DLL selection.
- **`dxvkGameNotes` bypasses `AddonInfoResolver`**: Unlike all other `*GameInfo` fields which go through `AddonInfoResolver.GetManifestDict(AddonType)`, `dxvkGameNotes` is read inline directly in `MainWindow.Events.Install.DxvkInfoButton_Click()`. Adding a DXVK note appends it after the generic DXVK description.
- **`gameNotes` has two read paths**: (1) `AddonInfoResolver` for the RenoDX info button dialog; (2) `GameInitializationService.ApplyManifestCardOverrides()` also writes it to `card.Notes` directly. Same for `lumaGameNotes` → `card.LumaNotes`.
- **`lumaDefaultGames` only fires in Phase 2**: Auto-Luma-enable is only applied in `BuildCards`, NOT in `LoadCacheAndBuildCardsAsync`. The card will show without Luma during Phase 1 (cache display) and flip to Luma mode when Phase 2 completes.
- **`lumaRenodxCompat` uses exact string matching**: Direct `Contains(game.Name)` — no normalization, no trademark stripping. Game names must match exactly as detected (e.g. Steam folder name).
- **`componentUrls` active keys**: Only `"ueExtended"` and `"ueDofFix"` are currently consumed. Any other key is silently ignored.

- **DOF Fix eligibility is UE 5.0–5.6 only**: DOF Fix backports a depth-of-field rendering fix from Unreal Engine 5.7. It only appears on games running UE 5.0–5.6 — not UE4 and not UE 5.7+. `DofFixService` uses `dofFixForceGames` (manifest) and `_dofFixSkipGames` to control eligibility. The UE version check gates the Recommended section row visibility.
- **`AuxInstallService.GlobalManifest` static reference**: A static field holding the live manifest, accessible from services that don't receive the manifest via DI. Used by `RenodxIniOverrides`, `RenodxExtraSettings`, and `UeExtendedCompatibility` in the update flow. If this is null (e.g. before first fetch), manifest-driven INI overrides won't apply on update.
- **Three init paths for manifest-driven card properties**: Fields that affect game display must be applied in ALL three code paths: (1) `InitializeAsync` full scan, (2) `LoadCacheAndBuildCardsAsync` Phase 1 cache display, (3) `RunBackgroundScanAndMergeAsync` Phase 2 update. Missing one path causes inconsistent state between phases. `lumaDefaultGames` is an example of a field only applied in path 1.
- **`GetManifestDllNames` / `GetGacSymlinkPath` / `ResolveEngineOverride` use 3-tier name matching**: (1) exact match, (2) trademark-stripped (™®©), (3) fully normalized via `NormalizeForLookup`. Most other manifest lookups use exact match only.
- **Full manifest field reference**: `docs/manifest-field-reference.md` — complete reference for all 60+ fields with file locations, method names, and runtime effects.

- **`*GameInfo` manifest notes for ReLimiter/DC now appear above release notes**: `relimiterGameInfo` and `displayCommanderGameInfo` go through `AddonInfoResolver` (Tier 1) but then `DialogService.Game.cs` calls `BuildReleaseNotesContent()` which renders GitHub release notes. Fixed in v2.3.2: if `result.Source == InfoSourceType.Manifest`, the manifest note is rendered first with a separator, then the release notes follow below. The `else` fallback to `result.Content` only fires when there are no release notes AND no manifest note was shown. This does NOT affect `reshadeGameInfo`, `reframeworkGameInfo`, etc. — those components don't use `BuildReleaseNotesContent`.

- **CompactViewBuilder (now called "Simple View") reuses DetailPanel XAML elements**: `CompactViewBuilder.cs` does NOT reparent or duplicate XAML. It populates all panels using the existing detail builders, then toggles section visibility to show only the relevant page. Any change to a DetailPanel XAML element automatically applies to simple/compact view too — no separate code path needed for UI changes to shared elements like the API badge.

- **Xbox/Game Pass Unreal games default API inference**: WindowsApps paths are access-denied so PE scanning fails. The last-resort fallback in `DetectGraphicsApi()` used to return DX11 for all Unreal games. Now: if `engineHintOverrides` contains a `"Unreal Engine 5.x"` version string for the game, the fallback returns DX12 instead (UE5 defaults to DX12 on Windows). UE4 and unknown version games still fall back to DX11 conservatively. User API overrides and manifest `graphicsApiOverrides` still take priority earlier in the chain. Pattern: set `engineHintOverrides` in manifest with the UE version string to get correct DX12 detection without needing a separate `graphicsApiOverrides` entry.

- **GitHub wiki subpages are JavaScript-rendered — fetch the main wiki page instead**: `https://github.com/org/repo/wiki/SubPage` returns a JS shell with no table HTML (0 `<table>` tags). The raw URL pattern `https://raw.githubusercontent.com/wiki/org/repo/SubPage` returns 404 for most repos. The main wiki page (`https://github.com/org/repo/wiki`) renders all sections including sub-tables in a single HTML response. For Luma Framework specifically, the completed mods table AND the Unreal Engine game table are both on the main wiki page — parse both from the same `_http.GetStringAsync(WikiUrl)` call. Detect the UE table by looking for headers containing both `dlss/fsr` and `hdr` and `ue version`.

- **GitHub wiki tables use `<table role="table">` not plain `<table>`**: HtmlAgilityPack's `SelectNodes("//table")` matches by tag name and finds these correctly — but PowerShell's `-match '<table'` will NOT match `<table role="table">` unless the search includes the space. The `//table` XPath selector in HtmlAgilityPack works fine regardless of attributes.

- **`LumaService.InstallAsync` had no cache check — re-downloaded every install**: The original implementation always did a full HTTP GET regardless of whether the zip was already cached. Fixed: do a HEAD request first, compare `Content-Length` to the local file size; skip download if they match. Critical for the generic `Luma-Unreal_Engine.zip` which would otherwise be re-downloaded for every DX11 UE game install. GitHub releases HEAD requests return reliable `Content-Length` (unlike `nightly.link`).

- **`ParseNotesCell` `<details>` blocks must distinguish INI blocks from plain notes, and must walk child nodes to preserve `<br>` line breaks**: The Luma UE wiki uses `<details>` for two purposes: (1) Engine.ini blocks (`<details><summary>▶ Modify Engine.ini</summary><code>[SystemSettings]</code><br><code>key=value</code>...</details>`) and (2) plain text notes. Two bugs: (a) all `<details>` were treated as INI blocks — fix: check for `[` + `]` to detect INI blocks, otherwise append as plain text. (b) using `node.InnerText` on a `<details>` element strips ALL HTML tags including `<br>`, concatenating everything onto one line — e.g. `[SystemSettings]r.DefaultFeature.AntiAliasing=2r.PostProcessAAQuality=4`. Fix: walk `node.ChildNodes` manually, converting `<br>` nodes to `\n` and `<code>` nodes to their text, skipping the `<summary>` child. Always walk child nodes when `<br>` line breaks need to be preserved — `InnerText` loses them. The `\WindowsApps\` guard is a hard early return that exits before reaching the `_gameApiCache` lookup. Any logic placed in the cache-hit section (e.g. UE5 DX12 override) is **unreachable** for WindowsApps paths. All WindowsApps-specific inference (UE5 → DX12, etc.) must live inside the WindowsApps early-return block itself. After the early return fires, `CacheGameApi` in BuildCards writes the returned value to disk — so if the early return returned DX11, the cache gets DX11 and the fix in the cache section never fires. Root cause of the persistent Avowed DX11 bug despite cache-hit fixes.

- **Generic Luma injection uses `DetectedApis.Contains(DX11)` not `GraphicsApi == DX11`**: Dual-API games (DX11+DX12) have DX12 as primary but DX11 in `DetectedApis`. The injection condition must check `DetectedApis` so these games still get the Luma row. On install of Luma on a dual-API game, `-dx11` is auto-set as launch arg (the game defaults to DX12, but Luma needs DX11 mode). On uninstall, the auto-set `-dx11` is removed. The wiki scrape `LaunchArgs` takes priority — the dual-API fallback only fires when the wiki didn't specify a launch arg. UE4 games that default to DX12 at runtime still statically import `d3d11.dll` in the regular PE import table as a legacy shim, while `d3d12.dll` is a **delay-load import** (PE data directory index 13). Both `Detect()` and `DetectAllApis()` must scan the delay-load table. Delay-load descriptor is 32 bytes with DLL name RVA at offset 4. PE32 delay-load dir offset: `optionalHeaderOffset + 200`; PE32+: `optionalHeaderOffset + 216`. Without this, `Detect()` returns DX11 (finds d3d11 in regular imports) and `DetectAllApis()` returns `[DX9, DX11, OGL]` missing DX12 — so the badge shows DX11 and the game incorrectly gets generic Luma (DX11 only). Also: skip `dxgi.dll` in the DLL folder scan — ReShade deploys as `dxgi.dll` and its own d3d11 imports cause false DX11. After any detection fix, delete both `game_api_cache.json` AND `api_cache.json` while RHI is closed, then Full Refresh. `DetectGraphicsApi` checks `_gameApiCache` early and returns the cached value without running any detection logic. The cache persists across sessions via `game_api_cache.json`, written by `SaveGameApiCache()`. For WindowsApps (Xbox/Game Pass) UE5+ games: the UE5→DX12 inference now intercepts the cache hit in `DetectGraphicsApi` AND updates `_gameApiCache[installPath]` to DX12 so `SaveGameApiCache()` writes the correct value to disk. Without the cache update, the stale DX11 file would be re-written every session, causing periodic scans to revert back to DX11. The fix is applied in three places: (1) `DetectGraphicsApi` cache hit (updates cache in-place), (2) `CacheLoad` after cache read (Phase 1 display), (3) `DetectGraphicsApi` last-resort fallback. All three must agree or the value oscillates.


## Manifest Push Workflow

When the user asks to "push manifest", ALWAYS:
1. Run `git diff manifest.json` first to see what changed
2. Identify user-facing changes (new split entries, engine tags, API overrides, install warnings, game notes, wiki unlinks, etc.)
3. Add a concise entry to the current version's `### Manifest Updates` section in `RHI_PatchNotes.md` — skip internal housekeeping, only note things users would notice
4. Then commit and push both `manifest.json` and `RHI_PatchNotes.md` together

Internal changes that do NOT need patch notes: re-ordering keys, adding empty sections, fixing trailing commas, bumping version number.

- **Game path cached during partial download / in-memory engine cache**: If RHI scans while a game is still downloading, `GameDetectionService._engineCache` caches the wrong root path in memory. `FindUEBinariesFolder` correctly looks for `GameName\Binaries\Win64` and only adds it if `.exe` files exist there — but if the binaries haven't downloaded yet, the cache gets a wrong result. Fixed: `ClearEngineCache()` is now called on full Refresh (`forceRescan: true`) so the engine detection re-runs with the complete file structure. Previously required a full RHI restart to fix.

- **Parallel BuildCards race condition on `_gameApiCache`**: `BuildCards` runs as `Parallel.ForEach`. Multiple game threads can race on `_gameApiCache[installPath]`. Even when a thread correctly updates the cache to DX12, another thread may overwrite it back to DX11 if they share the same `installPath` key (unlikely) OR if `SaveGameApiCache()` captures the state before the DX12 update is visible. Symptoms: `DetectGraphicsApi` is called with `cacheHas=True, manifestHas=True` but the inner override log never fires (cache has DX12 from a concurrent write), yet `game_api_cache.json` persists DX11. Deleting `game_api_cache.json` and letting it regenerate from scratch is the reliable fix for stale WindowsApps API values.

- **UE5+ games never show DX11 in API detection AND are excluded from generic Luma injection**: UE5 games always default to DX12. Their PE import table contains legacy `d3d11.dll` imports as a compatibility shim but they never actually run DX11. The fix is applied in BuildCards and CacheLoad using `newCard.EngineHint.Contains("Unreal Engine 5.")` — if UE5+, DX11 is stripped. **Critical**: the `_apiOverrides` bypass check must use composite key lookup (`GameKey.FromCard(game.Name, game.Source).ToKey()`) with name-only fallback — using plain `game.Name` always misses the override since `_apiOverrides` stores composite keys. User API overrides and manifest `graphicsApiOverrides` bypass the strip entirely.

- **Luma reshade.ini `[Luma]` section management**: Luma uses a `[Luma]` section in the game's `reshade.ini` for settings like `EnableHDR=1` and `FirstBoot=0`. RHI writes `EnableHDR=1` on Luma install. `AuxInstallService.GetLumaReshadeIniValue(gameDir, key)` and `SetLumaReshadeIniValue(gameDir, key, value)` are the helpers for reading/writing specific keys in that section. The Luma cog exposes these as Off/On combos. Unlike Engine.ini, reshade.ini is not made read-only after Luma section writes — it's a live config file that Luma itself also reads/writes during gameplay.

- **User explicit UE-Extended opt-in was blocked by `hasNamedMod` gate**: The `useUeExt` condition had `(!hasNamedMod || isNativeHdr)` as a required gate before checking `IsUeExtendedGameMatch`. When a game has a named mod on the RenoDX wiki, `hasNamedMod = true` and the entire expression became false — including cases where the user had explicitly opted in via `_ueExtendedGames`. This caused UE-Extended to silently turn off after a background wiki fetch that matched a Nexus mod for the game. Fix: `userExplicitUeExt = IsUeExtendedGameMatch(game.Name)` is extracted first and also added to the `(!hasNamedMod || isNativeHdr || userExplicitUeExt)` gate. User explicit opt-in now always wins over named mod detection.

- **Delay-load import scan is too aggressive for `DetectAllApis`**: Many UE4 games that support DX12 optionally (e.g. via `-dx12` launch arg) but default to DX11 still have `d3d12.dll` as a delay-load import. Scanning delay-load imports in `DetectAllApis` returned DX12 in the `All` set, causing these games to show dual DX11+DX12 badges and get Luma rows incorrectly. Fix: only apply delay-load DX12 upgrade when `bestPriority < Priority[DX11]` — i.e. no explicit DX11 was found in the regular import table. Games that explicitly import `d3d11.dll` are genuine DX11-default games even if they have DX12 as a delay-load option.

- **`RequestCardRebuild` / `RequestReselect` no-op when game already selected**: `RequestReselect` → `TryRestoreSelection` → `GameList.SelectedItem = match`. If the game is already selected, this is a no-op — `SelectionChanged` never fires and `PopulateDetailPanel` never runs. When needing to update the detail panel after changing card state for the currently selected game, call `PopulateDetailPanel(targetCard)` directly instead of relying on `RequestReselect`. The pattern: (1) update card state synchronously, (2) call `targetCard.NotifyAll()`, (3) call `_window.PopulateDetailPanel(targetCard)` directly, (4) optionally also call `RequestReselect` for fallback.

- **`ReevaluateLumaForCard` for single-card re-evaluation**: When the user changes an API override for a game, Luma eligibility may change (e.g. DX11 becomes available or unavailable). Rather than triggering a full Refresh, call `ReevaluateLumaForCard(card)` synchronously in `MainViewModel.Install.Luma.cs` before `RequestReselect`. This re-runs the Luma injection logic for the single card and updates `LumaMod`, `LumaFeatureEnabled`, etc. immediately. Pattern: (1) update card state, (2) call `ReevaluateLumaForCard(card)`, (3) call `RequestReselect` for panel rebuild. Defined in `MainViewModel.Install.Luma.cs`, called from `DetailPanelBuilder.Overrides.cs` inline after API override changes.

- **One-time install warning with "don't show again" checkbox**: The Luma + RenoDX compatibility warning uses a `SettingsViewModel` bool property (`LumaRenodxCombinedWarningDismissed`) persisted to settings. Pattern: check the flag before showing a `ContentDialog` with a `CheckBox` inside, wire `CheckBox.Checked` to set the flag immediately, then show the dialog. If dismissed or accepted with the checkbox ticked, the flag is saved and the dialog never appears again. Use this pattern for any one-time informational warning that shouldn't nag the user. The checkbox is placed inside the dialog's `Content` property as an inline `StackPanel`.

- **`ReevaluateLumaForCard` UE5 guard must allow explicit DX11 API overrides**: The `!isUe5` gate in `ReevaluateLumaForCard` blocks generic Luma injection for all UE5+ games. This is correct for auto-detected API (UE5 games default to DX12 and Luma only works on DX11), but it must be bypassed when the user has explicitly overridden the API to DX11 via the Graphics API dropdown. Fix: check `GetApiOverride(card.GameName, card.Source)?.Contains("DirectX11")` and set `hasExplicitDx11Override`; use `(!isUe5 || hasExplicitDx11Override)` as the gate. Without this, the Luma row never appears for UE5 games even after forcing DX11, but the DX12 hide still works (else-branch runs when `isDx11 = false` regardless).

- **`DlssStreamlineService` trusted path cache — three bugs causing perpetual full scans**:
  1. **`RecordTrustedPath` case-sensitive comparison**: compared cached DLL paths to freshly-detected paths using `==`. Windows filesystems return paths in inconsistent casing between sessions, so the comparison failed, reset `ConfirmCount` to 1, and games never reached `SkipThreshold = 3`. Fix: `StringComparison.OrdinalIgnoreCase` on all four path comparisons.
  2. **`PathsAreWithin` StreamlineFolder exact-match missing**: For flat-install games (e.g. Nioh 3), `StreamlineFolder` equals the install root exactly with no trailing slash — `StartsWith(root + "\")` always fails. Fix: also accept an exact match (both sides trimmed, `OrdinalIgnoreCase`).
  3. **`PathsAreWithin` mixed forward/backslash paths**: Games installed to paths with forward slashes (e.g. `D:/Assassin's Creed Black Flag Resynced`) get their DLSS paths stored with mixed separators (`D:/Game\\NVStreamline\\...`). `normalizedRoot` is built from the forward-slash install path, but the cached DLL path switches to backslashes mid-way — `StartsWith` fails. Fix: normalize all paths to backslashes via `.Replace('/', '\\')` before comparison in `PathsAreWithin`.
  - **WindowsApps games (e.g. Forza Horizon 6) will always re-scan after a game update** — the version segment in the WindowsApps path changes on update, so `PathsAreWithin` invalidation is correct and unavoidable for those.
  - **Remaining unavoidable slow scans**: Large flat-root installs with no `Binaries` subfolder (Ryubing, Mass Effect Andromeda, Diablo, Dragon's Dogma Online) and WindowsApps permission-walk games (Vampire Crawlers, Fuzion Frenzy) are slow due to directory size/structure, not cache misses.

- **`DlssPresetService.FindProfileUncached` does a full recursive exe scan every session**: When title/fuzzy matching fails, `FindProfileUncached` calls `Directory.EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories)` then iterates all 7940 NVIDIA driver profiles comparing exe names. `_profileLookupCache` stores results in-memory but is cleared on every app restart. For flat-root installs (Ryubing, Diablo, Mass Effect Andromeda, Dragon's Dogma Online, Endfield), this recursive exe scan on a large directory is the primary cause of the remaining slow BuildCards entries (3–8 seconds). **Fixed**: `nvidia_profile_cache.json` in `%LocalAppData%\RHI\` persists `"GameName" → "Profile Name"` (null = confirmed no profile) across sessions. `FindProfile` checks this file before falling through to `FindProfileUncached`. `ClearProfileNameCache()` deletes the file and is called on Full Refresh (`forceRescan=true` in `InitializeAsync`). `CreateProfileForGame` also writes to the cache immediately after creating a new profile. All cache logic is kept together in `DlssPresetService.ProfileMatching.cs`.

- **`PcgwService.ResolveUrlAsync` makes live HTTP calls for non-Steam/non-PCGW games every session**: `ResolveUrlAsync` is called synchronously via `.GetAwaiter().GetResult()` inside `Parallel.ForEach`. For games not in the AppID cache, it calls `SteamAppIdResolver.SearchSteamStoreAsync` (Steam Store API + 1s rate-limit delay) then `OpenSearchFallbackAsync` (PCGW OpenSearch, 5s timeout). Negative results (no AppID found, no PCGW page) were never cached — so non-Steam games (Ryubing, Diablo, Dragon's Dogma Online, Endfield, Mass Effect Andromeda) hit both HTTP endpoints every single launch, blocking parallel threads for 4–8 seconds each. Fix: cache `-1` as a sentinel in `_appIdCache` for confirmed misses; check for sentinel before making HTTP calls. The `steam_appid_cache.json` already persists positive results — negative results now also persist so the HTTP calls only fire once per game lifetime. On Full Refresh, `ClearNegativeCache()` removes all `-1` entries so games that later get a Steam release or PCGW page are retried. Manifest `pcgwUrlOverrides` are checked first in `ResolveUrlAsync` before any cache lookup — so manifest-added PCGW links bypass the sentinel entirely and appear on the next launch without any refresh. Symptom pattern: games slow despite all file-system caches warm, no DLSS/API/profile log entries, phase instrumentation showing <50ms for all sections — but `.GetAwaiter().GetResult()` on async HTTP calls doesn't appear in phase markers.

## OptiScaler Nightly Variant

- **OptiScaler nightly is a separate GitHub repo**: `https://api.github.com/repos/optiscaler/OptiScaler-nightly/releases` — uses the **list** endpoint (not `/latest`) because each day is its own release. Tag format: `nightly-20260813`. Version string: strip `"nightly-"` prefix → `"20260813"`.
- **Nightly archive structure is identical to stable**: `OptiScaler.dll` (rename target), `OptiScaler.ini` (skip — deploy pre-configured), `OptiScaler/` subfolder (deploy as-is to `<gamedir>\OptiScaler\`), `Licenses/` (skip), `.bat` (skip).
- **Staging dir**: `%LocalAppData%\RHI\optiscaler-nightly\` alongside `optiscaler\`. Has its own `version.txt` with the date string.
- **Per-game variant** (`"Stable"` / `"Nightly"`) stored in `GameNameService._osVariantOverrides` — composite-keyed `"GameName|Store"`, persisted as `"OsVariantOverrides"` in `settings.json`. Accessed via `MainViewModel.Settings.GetOsVariant`/`SetOsVariant`. Default (no entry) = Stable.
- **`AuxInstalledRecord.OsVariant`** field added — stores which variant was used at install time so `UpdateAsync` knows which staging dir to use without needing per-game lookup.
- **INI files**: 3 nightly INI files (`OptiScaler_nightly.nvidia.ini` etc.) created as copies of stable equivalents. `GetBundledIniPath` accepts a `variant` param and appends `_nightly` suffix for nightly. Each must be added to `.csproj` as `<Content>` and to `publish.bat` as a copy line.
- **Startup on-demand**: nightly staging only downloads if at least one game has `OsVariant = "Nightly"` — checked in `RunBackgroundScanAndMergeAsync`.
- **Update All variant grouping**: `UpdateAllOsAsync` splits cards into stable/nightly groups, ensures the correct staging per group. `CheckForUpdatesAsync` checks both `HasUpdate` and `HasUpdateNightly` per-card based on `GetOsVariant`.
- **UI**: ComboBox in OptiScaler ⚙ cog dialog with items `["Stable", "Nightly"]`. Wired to `SetOsVariant`. Plain text label "OptiScaler Version".

- **OptiScaler uninstall DLSS guard was too strict**: Steps 2b/2c/2d guarded DLSS deletion with `&& File.Exists(path + ".original")` to confirm OptiScaler deployed them. But OptiScaler only creates `.original` backups when a DLSS DLL already existed — games with no prior DLSS installation never get a backup. Result: uninstall left `nvngx_dlss.dll`, `nvngx_dlssd.dll`, `nvngx_dlssg.dll` behind on those games. Fix: remove the guard and always delete the DLSS DLLs on uninstall, then restore `.original` if it exists. The `RestoreOriginalIfExists` call is a no-op when no backup exists, so this is safe.

- **WinUI PRI compiler rejects hyphens in `<Content>` filenames**: Files with hyphens in their names (e.g. `OptiScaler.amd-dlss.ini`) declared as `<Content Include=...>` in the `.csproj` cause `WINAPPSDKGENERATEPROJECTPRIFILE` warnings (`PRI249: Invalid qualifier: AMD-DLSS`) and the file is silently excluded from the publish output. The workaround: declare them as `<None Include=...>` (preserves them in the project/bin but skips PRI processing) AND add an explicit `copy /y` line in `publish.bat`. Files without hyphens (`OptiScaler.nvidia.ini`) work fine as `<Content>`. This affects any content file whose name contains a hyphen.

- **OptiScaler INI files are per-GPU and per-variant, user-editable in appdata**: 6 files live in `%LocalAppData%\RHI\inis\` (`OptiScaler.nvidia.ini`, `OptiScaler.amd-dlss.ini`, `OptiScaler.amd-nodlss.ini`, + `_nightly` variants). Seeded on first launch from bundled templates via `SeedUserInis()` (called in `MainWindow` constructor). Never overwritten — users can edit freely. `GetUserIniPath(gpuType, dlssInputs, variant)` returns the correct one. On install/deploy, the matching file is copied to the game folder renamed as `OptiScaler.ini`. `SetHotkey` writes to all 6 existing files. Previous behavior (always overwriting `OsIniPath` = single `OptiScaler.ini`) meant user edits were lost on every install.

- **`CheckOptiPatcherUpdateAsync` was never called — now fixed**: The method existed but wasn't wired into the update check flow. Also used wrong version field (base version string) instead of build commit hash. Both fixed — OptiPatcher now auto-updates silently and deploys to all installed games.

- **OptiPatcher rolling release body uses Markdown bold formatting**: The GitHub API body field contains `**Build commit:** 8cddbfb2696ea72508914be90eb590737b0331bd` (with `**` markdown bold), not plain `Build commit: ...`. Regex must handle both: `\*?\*?Build commit:\*?\*?\s*([0-9a-f]+)`. The commit hash in the body is the full 40-char SHA — store the full hash in `version.txt` for comparison (nothing shows in UI so display length doesn't matter). Always fetch the actual API JSON before writing parsing regexes for GitHub release bodies — the rendered HTML and raw JSON body can differ significantly.

- **`CopyIniToGame` (Deploy OptiScaler.ini button) must apply all RHI settings**: The deploy button copies the user's appdata INI to the game folder but must also call `EnforceLoadReshade`, `EnforceLoadAsiPlugins`, and `WriteShortcutKey` — the same post-processing as `InstallAsync`. Without this, the deployed INI won't have `LoadReshade=true` or the configured hotkey. The hotkey must be passed in from the call site (`ViewModel.Settings.OsHotkey`), not read from the source INI file. Both `InstallEventHandler.CopyOsIniButton_Click` and `MassDeployHandler.MassDeployOsIni` must pass the hotkey.

- **OptiScaler nightly INI format diverges significantly from stable**: The nightly build has entirely new sections (`[DLSSG]`, `[NvngxFG]`, `[Magnifier]`, `[fakenvapi]`) and renamed/replaced settings (`Fsr4ForceModel` replaces `Fsr4Update`/`Fsr4ForceEnableInt8`, `FGNvngxReplacement` replaces `[Nukems]`). The nightly bundled INIs (`OptiScaler_nightly.*.ini`) must be sourced from the actual nightly archive's `OptiScaler.ini`, not copied from stable. When OptiScaler nightly ships a new INI format, update all 3 nightly bundled INIs from the extracted archive.

## OptiScaler Streamline & DLSS Enabler Deployment

- **Deploy Streamline** (per-game, nightly only): Copies all files from `%LocalAppData%\RHI\Streamline\` to `<gamedir>\OptiScaler\Streamline\`. **Note**: Streamline staging uses a version subfolder structure (`Streamline\2.12.0\sl.*.dll`) — DLLs are NOT at the root. `DeployStreamlineToGame` must detect the version subfolder by checking `Directory.GetDirectories(sourceRoot)` and using the first one that contains `.dll` files. Stored in `GameNameService._osDeployStreamline` (composite-keyed HashSet, persisted as `"OsDeployStreamline"`). `OptiScalerService.DeployStreamlineToGame` / `RemoveStreamlineFromGame` in `OptiScalerService.Streamline.cs`. Auto-redeployed after Streamline staging updates in `RunBackgroundScanAndMergeAsync`. UI: combo `No`/`Yes` in OptiScaler ⚙ cog, shown only when variant = Nightly.

- **Deploy DLSS Enabler** (per-game, requires Streamline = Yes): `DlssEnablerService` modeled after `DofFixService`. GitHub: `https://api.github.com/repos/RankFTW/rhi-repo/releases`, tag prefix `DLSS-Enabler-`. Staging: `%LocalAppData%\RHI\optiDE\`, staged as `version.dll`. Deploy as: `dlss-enabler-headless.dll` inside `<gamedir>\OptiScaler\`. Stored in `GameNameService._osDeployDlssEnabler` (composite-keyed HashSet, persisted as `"OsDeployDlssEnabler"`). Auto-deploy in `EnsureStagingAsync` updates only already-deployed files (won't fresh-install). Update check wired into `CheckForUpdatesAsync`. UI combo only enabled when Streamline = Yes — disabling Streamline cascades to force DLSS Enabler off.

- **GitHub `/releases` list API returns max 30 per page by default**: When a repo has 30+ releases, newer releases with older `created_at` dates (backfilled historical releases) can be pushed to page 2 and silently missed. Always append `?per_page=100` to the releases list URL. Example: `DlssEnablerService` couldn't find the `DLSS-Enabler-4.9.0.4` release because the repo had 30+ older releases paging it off. Fix: `"https://api.github.com/repos/.../releases?per_page=100"`.

- **GitHub release assets can be zip archives containing the actual file**: `DlssEnablerService` expected a direct `version.dll` asset but the release contains `DLSS-Enabler-4.9.0.4.zip` (a zip wrapping `version.dll`). When downloading from a `.zip` URL, use `System.IO.Compression.ZipFile.OpenRead` to find and extract the target file by name. Use `entry.Open()` + `CopyToAsync` rather than `ExtractToFile` (which requires the `ZipFileExtensions` assembly not always available in single-file publish). Always verify the actual asset filename on the release page before writing download code.

- **Cog dialogs with dynamic content must close and reopen when a setting changes the visible rows**: `ContentDialog` content is built once on open — changing a combo that affects which rows are visible (e.g. OptiScaler Version Stable→Nightly shows/hides deploy options) won't update the already-open dialog. Fix: declare `ContentDialog? cogDialog = null` before wiring `SelectionChanged`, assign it after dialog creation, then in the handler call `cogDialog?.Hide()` + `await Task.Delay(80)` + re-invoke the cog click handler to rebuild with fresh state. The variable must be declared before the lambda to avoid CS0841.

- **`SetOptiScalerIniValue` for section-aware INI editing**: OptiScaler.ini uses named sections (`[FrameGen]`, `[SystemSettings]` etc.). When writing FGInput/FGOutput/FGNvngxReplacement, a section-aware writer is needed: scan for the section header, find and replace the key line in that section, or insert at section end/create section if missing. Strip read-only attribute before writing, restore after — same pattern as Engine.ini. `ApplyFgSettings` wraps the three FG writes and only writes `FGNvngxReplacement` when `fgOutput == "dlssg"`. Applied both on immediate cog change and on Deploy OptiScaler.ini / post-install.

- **Conditional ComboBoxItem enablement**: To disable a specific item inside a WinUI 3 ComboBox (e.g. "Enabler" only when Streamline + DLSS Enabler both = Yes), use `new ComboBoxItem { Content = "Enabler", IsEnabled = false }` instead of a plain string in `ItemsSource`. The rest of the items can be strings — mix strings and `ComboBoxItem` objects in the same `ItemsSource` array. Read selected value with `(combo.SelectedItem as ComboBoxItem)?.Content as string ?? combo.SelectedItem as string`.

- **Two-column cog dialog layout helper pattern**: For cog dialogs with many settings, use a shared `Grid` with 4 columns (`Star, 110px, Star, 110px`) and `RowSpacing = 8`, `ColumnSpacing = 12` — matching the UE-Extended cog layout. Use `MakeSettingsGrid()` + `AddRow(grid, row, leftLabel, leftCombo, rightLabel?, rightCombo?)` local functions. Combos use `HorizontalAlignment.Stretch` to fill their fixed column. This ensures all labels and combos align vertically between left and right columns. For right-side-only rows (e.g. FG Nvngx Replacement), populate only columns 2+3 of the shared grid — do NOT create a separate grid with a spacer column, as that breaks alignment. Use `Opacity = 0.35` on individual label and combo elements for the disabled-but-visible state; do not apply opacity to the whole row grid.

- **Duplicate code corruption when str_replace inserts at wrong boundary**: When using str_replace to rewrite a large method body, if the `oldStr` doesn't include enough trailing context, the old code can be left orphaned outside the method while the new code is inserted at the start. The file then has two copies of the body — the new one as a valid method, and the old one floating at class scope causing 80+ compile errors. Fix: include the exact closing method brace and the start of the next method in `oldStr` to ensure full deletion of the old body. Always verify the replacement deleted all old code by checking for duplicate blocks before building.

- **Separate Grids with identical column definitions don't visually align**: Two independent `Grid` elements with identical column definitions (`Star, 110px, Star, 110px`) will NOT produce aligned columns — each grid lays out independently. Even an invisible spacer in cols 2+3 doesn't reliably constrain col 0 when the other grid has real content. **The only reliable fix is to put all rows that must align into the same Grid instance.** For the OptiScaler cog: the "OptiScaler Version" row and all nightly rows use a single shared `unifiedGrid` (via `MakeSettingsGrid()` + `AddRow()`), with the separator living in the outer `content` StackPanel between them. Never use a separate header grid above a settings grid and expect columns to align — merge them.

- **Phase timing instrumentation in BuildCards**: `MainViewModel.BuildCards.cs` logs `[BuildCards.Phase] 'GameName' — PhaseName took Xms` for any section exceeding 50ms. Useful for diagnosing user-submitted slow-startup logs. Add similar markers when introducing new per-game async work in `BuildCards`.

- **OptiScaler uninstall must delete the `OptiScaler/` subfolder**: Step 4d of uninstall unconditionally deletes `<gamedir>\OptiScaler\` after restoring DXVK (if applicable). This step was added in beta4. Without it, Streamline DLLs and DLSS Enabler files are left behind after uninstall.

- **ReShade DLL rename is allowed even when OptiScaler is installed**: The `!card.IsOsInstalled` guard that previously blocked ReShade DLL rename was removed. OptiScaler and a custom ReShade DLL coexist fine.

- **OptiScaler cog is accessible even when nothing is installed**: The `OsIniExists` gate that previously required an installed INI to open the cog was removed. The cog should always be openable so users can pre-configure variant/Streamline/Enabler settings before installing.

- **OptiScaler Engine.ini tweaks (Nightly only, UE games)**: Four per-game composite-keyed HashSets in `GameNameService`: `_osDilatedMotionVectorsOff`, `_osFsrCrashFix`, `_osFsrFgSwapchain`, `_osUpscalerPlugin`. Applied/removed immediately via `ApplyEngineIniCustomKeys` / `RemoveEngineIniCustomKeys` on cog combo change. Shown in the cog below a separator, visible only when Variant = Nightly and game is a UE title.

- **Streamline + DLSS Enabler auto-deploy after fresh OptiScaler install**: `InstallEventHandler` checks `GetOsDeployStreamline` / `GetOsDeployDlssEnabler` after a fresh install completes and deploys them immediately if pre-enabled. This mirrors the pattern used by Engine.ini LUT and UE-Extended settings in the drag-drop handler — post-install steps must replicate the full set of conditional deployments, not just the primary component.

- **`upload-dlss-releases.ps1` handles `DLSS-Enabler-*.zip` assets**: The upload script was updated to support DLSS Enabler zip archives alongside the existing DLSS DLL uploads. When adding new GitHub release asset types for auto-update, update this script.

- **`OptiScaler.ini` must NOT be set read-only**: `SetOptiScalerIniValue` previously stripped read-only before writing and re-applied it after, mirroring the Engine.ini pattern. This was wrong — only Engine.ini needs read-only protection (to prevent the UE engine from overwriting HDR settings on launch). OptiScaler.ini does not need this protection and the read-only attribute caused `File.Delete` in `Uninstall` to throw `UnauthorizedAccessException`, silently aborting the entire uninstall mid-way (after deleting the DLL but before deleting the INI, DLSS DLLs, subfolder, and tracking record). Symptom: OptiScaler still shows as installed after clicking Uninstall, game folder still has OptiScaler.ini and DLSS DLLs. Rule: only Engine.ini gets read-only. All other RHI-managed files (OptiScaler.ini, reshade.ini, etc.) stay writable.

- **All Engine.ini writers must merge into existing sections, never append duplicate headers**: Both `ApplyEngineIniCustomKeys` and `ApplyEngineIniHdrSettings` had the same bug — they appended a new `[SectionName]` block regardless of whether that section already existed. When UE-Extended wrote `[SystemSettings]` first and OptiScaler cog wrote it second (or vice versa), the result was two separate `[SystemSettings]` blocks. Fix applied to both methods: scan for the existing section header first; if found, insert keys at the end of that section (before the next header or EOF, stepping back over trailing blank lines); only create a new section block if none exists. **Rule: any future Engine.ini writer must use this same merge pattern — never `File.AppendAllText` with a section header.**

- **`RemoveEngineIniCustomKeys` left empty section headers behind**: After removing key lines, the section header (`[SystemSettings]`) was left in place even when it had no remaining content. Fix: after key removal, scan for section headers with no non-blank content before the next header or EOF, and remove those empty headers (along with their trailing blank lines). Rule: any method that writes to Engine.ini and then removes from it must clean up orphaned section headers.

- **OptiScaler ReShade coexistence rename must only fire on actual filename conflict**: The original code unconditionally renamed ReShade to `ReShade64.dll` on every OptiScaler install, regardless of what name ReShade was using. This broke setups where ReShade was installed as `d3d12.dll` (or any name other than the OptiScaler deploy name) — it got renamed to `ReShade64.dll` unnecessarily. Fix: only rename when `rsCurrentName == effectiveDllName` (i.e. ReShade and OptiScaler would occupy the same filename). If no conflict, skip the rename entirely and log it. The `effectiveDllName` is already resolved before step 4 so it's available for the check.

- **OptiScaler `UpdateAsync` must clean up old files before deploying new ones**: The original update just copied new staging files over the top, leaving files removed from the new nightly version behind (stale companion DLLs, old subdirectory contents). Fix: before deploying, (1) delete all subdirectories that exist in the new staging (except `Licenses`) — this handles `D3D12_OptiScaler/`, `OptiScaler/Streamline/` etc. being redeployed fresh; (2) delete companion DLLs in the game root that are in `CompanionFiles` but not in the new staging. The installed DLL itself and any `.original` backups are never touched.

- **OptiScaler.ini update strategy — deploy fresh then merge user changes**: On update, the old INI is preserved so user settings aren't lost, but the new nightly INI has new sections/keys the old one doesn't. Fix: `ParseIniSections` reads both the old user INI and the staged template INI into `section→key→value` dicts. The fresh staged INI is deployed, then any user value that differs from the template default is written back via `SetOptiScalerIniValue`. New keys from the template are picked up automatically; user customisations survive. `ParseIniSections` is a private static helper in `OptiScalerService.Install.cs`.

- **First-launch setup window pattern**: `SetupWindow` is a plain `Window` subclass shown in `App.OnLaunched` before `MainWindow` is created. Gate: check `SettingsViewModel.LoadSettingsFile()` for `FirstLaunchSetupDone` key — if absent (new install), show setup window; existing users skip it entirely. The `OnComplete` callback applies settings to the raw dict and calls `SettingsViewModel.SaveSettingsFile()` before creating `MainWindow` on the dispatcher queue. **ContentDialog cannot be used here** — it requires an `XamlRoot` which only exists after `MainWindow` is created. A separate `Window` has no such dependency. The `SingleInstanceService.StartListening()` and `FileReceived` wiring are duplicated in both the setup-done path and the `OnComplete` callback — extract to a shared method (`OnFileReceived`) to avoid duplication.

- **New Window subclass boilerplate for RHI**: Title bar styling requires three steps: (1) `NativeInterop.EnableDarkTitleBar(hwnd)`, (2) all 12 `AppWindow.TitleBar.*Color` properties set from `Application.Current.Resources`, (3) `AppWindow.SetPresenter(OverlappedPresenter)` + `overlapped.IsResizable = false` to prevent user resizing. Center on primary display via `DisplayArea.Primary.WorkArea` + `AppWindow.Move`. UI built imperatively in code-behind (no XAML template) using `UIFactory.Brush(ResourceKeys.*)` for colors — same pattern as `DetailPanelBuilder`. **DPI scaling**: `AppWindow.Resize` takes physical pixels, not logical pixels. At high DPI settings (e.g. 300%), passing logical pixel values makes the window tiny. Always multiply by the DPI scale: `uint dpi = NativeInterop.GetDpiForWindow(hwnd); double scale = dpi / 96.0; AppWindow.Resize(new SizeInt32((int)(logicalW * scale), (int)(logicalH * scale)))`. `GetDpiForWindow` is already declared in `NativeInterop.cs`.

- **Window close from button Click handler causes 0xc0000005 access violation**: Calling `window.Close()` synchronously from within a button's `Click` event handler (even indirectly via a callback) crashes with `Exception Processing Message 0xc0000005 - Unexpected parameters`. The window is still in the middle of event processing when Close tears it down. Fix: wrap the close logic (and any callback that may trigger a close) in `DispatcherQueue.TryEnqueue(() => { ... })` so it runs on the next dispatcher tick after the click handler has fully unwound. Additionally, creating a new `Window` immediately after closing the old one in the same `TryEnqueue` call can also AV — add a delay between `window.Close()` and the next window creation. **Do NOT use `async` lambdas with `TryEnqueue`** — `TryEnqueue` takes `Action`, not `Func<Task>`, so the async continuation silently detaches after the first `await` and never runs. **Do NOT use `Task.Delay().ContinueWith()` to re-enter the UI thread** — unreliable in this context, the `TryEnqueue` inside `ContinueWith` silently does nothing. **The reliable pattern**: call `TryEnqueue(() => { window.Close(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) }; timer.Tick += (_, _) => { timer.Stop(); /* create next window */ }; timer.Start(); })`. `DispatcherTimer` is created on the UI thread and its `Tick` fires on the UI thread guaranteed — no cross-thread marshaling. Applied in `App.OnLaunched` for the SetupWindow → MainWindow transition.

- **WinUI 3 window transition pattern (setup → main)**: Closing the only open window shuts down the WinUI message pump immediately — any timers, `TryEnqueue`, or `ContinueWith` posted after that point never fire. The correct pattern when transitioning from a setup/splash window to MainWindow: (1) defer `OnComplete` via `DispatcherQueue.TryEnqueue` to exit the button click handler first; (2) inside `OnComplete`, call `setupWindow.AppWindow.Hide()` — this removes it visually WITHOUT killing the pump; (3) create and `Activate()` MainWindow synchronously; (4) then call `setupWindow.Close()`. Hiding keeps the message loop alive while the new window initialises. Also: `window.Close()` called synchronously from a button `Click` handler (even one level deep) causes a `0xc0000005` access violation — always defer via `TryEnqueue`. Do NOT use `async` lambdas with `TryEnqueue` (`Action` not `Func<Task>` — async part silently detaches). Do NOT use `Task.Delay().ContinueWith()` to re-enter the UI thread after a window close — unreliable. Applied in `App.OnLaunched` for SetupWindow → MainWindow.

- **`BackupOriginalIfExists` must be called for all root files in OptiScaler `InstallAsync`**: ~~Previously this rule said "never call BackupOriginalIfExists for OptiScaler files" — that was wrong.~~ Games like Stalker 2 and The First Berserker ship their own companion DLLs (`amd_fidelityfx_*.dll`, etc.) that OptiScaler replaces. Without backup, those game-original files are permanently deleted on uninstall. Corrected rule: call `BackupOriginalIfExists(destPath)` before every `File.Copy` in the root staging file loop in `InstallAsync`. Subdirectory files (`D3D12_OptiScaler/`, `OptiScaler/Streamline/`) are exempt — those folders don't exist in game directories before OptiScaler. `UpdateAsync` is also exempt — during updates the file already belongs to OptiScaler, not the game.

- **`CreateProfileForGame` must check `_launchExeOverrides` before falling back to largest-exe**: The largest-exe heuristic picks whatever `.exe` is biggest in the install root — for Arma Reforger, `ArmaReforgerSteamDiag.exe` (38 MB) was larger than the actual game exe `ArmaReforgerSteam.exe` (35 MB), so the NVIDIA driver profile got the wrong application registered. Fix: check `_launchExeOverrides[gameName]` first (same dict used by `FindProfile`), use `Path.GetFileName(overrideExePath)` as the exe, and only fall back to the largest-exe scan if no override is set. The `_launchExeOverrides` dict is already populated from the manifest in `ApplyManifestProfileConfig`. After fixing, users need to delete the wrong profile in NVPI and re-run "Create NVIDIA Profile" to regenerate it.

- **`IsLumaArchive` content-based detection is not sufficient for all Luma releases**: The original check looked for `d3dcompiler_47.dll` inside the archive. Newer Luma releases (e.g. Black Ops 3 Luma 28) dropped this file from the archive, causing `IsLumaArchive` to return false and the archive to be misrouted to the generic addon extractor (which then fails). Fix: add a filename-based fast path first — if the archive filename contains "Luma" (case-insensitive) and does NOT contain "addon", treat it as a Luma archive immediately. The content check still runs as a fallback for archives without "Luma" in the name.

- **Per-game Streamline version override (`OsStreamlineVersion`)**: New composite-keyed dict in `GameNameService`. `GetOsStreamlineVersion`/`SetOsStreamlineVersion` in `MainViewModel.Settings.cs` follow the same pattern as `GetOsFgInput`/`SetOsFgInput`. When the user picks a version in the Streamline Version combo, `_dlssStreamlineService.SwapStreamlineAsync(Path.Combine(installPath, "OptiScaler", "Streamline"), version)` is called immediately to swap the DLLs in-place. The `SwapStreamlineAsync` method only replaces files that already exist in the destination folder — so Streamline must already be deployed before swapping works. Default version is the persisted value → `"2.12.0"` → first manifest entry. `IDlssStreamlineService` is resolved via `App.Services.GetRequiredService<IDlssStreamlineService>()` in the cog handler (not a MainWindow field).

- **"Deploy Streamline" and "Deploy DLSS Enabler" merged into one combined toggle**: Both settings always go together — Enabler requires Streamline and there's no use case for Streamline without Enabler. The combined "Streamline/DLSS Enabler" combo sets both `SetOsDeployStreamline` and `SetOsDeployDlssEnabler` in one action. The `enablerAvail` check for FG Nvngx Override "Enabler" item was simplified to just `GetOsDeployStreamline(...)`.

- **`SwapStreamlineAsync` and `SwapStreamlineCustomAsync` must NOT call `BackupAndReplace`**: The `OptiScaler/Streamline/` folder is entirely RHI-managed. Using `BackupAndReplace` created `.original` backup files on every version swap. **Root cause confirmed**: `SwapStreamlineAsync` was creating `.original` files even when replacing 2.12.0 with 2.12.0 (same version) because the backup logic checks `!File.Exists(backupPath)` before copying. Since the game never had Streamline before install, the files deployed by `DeployStreamlineToGame` are fresh — there is nothing to back up. Fix: use plain `File.Copy(overwrite: true)` directly. The AppData zip backup (`BackupStreamlineToAppData`) is the correct restore mechanism. The `BackupAndReplace` helper is only appropriate for DLSS DLLs where a game may have shipped its own copy.

- **`SetOptiScalerIniValue` key matching must handle spaces around `=`**: OptiScaler's own INI template writes keys as `Key = Value` (with spaces), but `SetOptiScalerIniValue` checked `trimmed.StartsWith(key + "=")` which only matches `Key=Value` (no spaces). When trying to update an existing key written by OptiScaler itself (e.g. `FramerateLimit = 324.899994`), the match failed, causing a duplicate line to be appended instead of replacing the existing one. Fix: also check `trimmed.StartsWith(key + " =")` in the matching condition.

- **Per-game cleanup on OptiScaler uninstall must live in `InstallEventHandler`, not `OptiScalerService`**: `OptiScalerService` doesn't have access to `MainViewModel` or `IGameNameService`, so per-game setting resets (`SetOsFgInput`, `SetOsDeployStreamline`, etc.) cannot be called from there. The cleanup must happen in `InstallEventHandler.cs` immediately after `_optiScalerService.Uninstall(card)` returns, where `ViewModel` is accessible. Engine.ini key removal (`AuxInstallService.RemoveEngineIniCustomKeys`) is also called here, checking each setting's current value before removing — e.g. only remove Dilated MV keys if `GetOsDilatedMotionVectorsOff` was true. Pattern: post-uninstall cleanup in the caller, not in the service.

- **WinUI 3 `IsEnabled` is on `Control`, not `FrameworkElement` or `UIElement`**: When iterating `content.Children` to disable UI elements, cast to `Control` for `IsEnabled` and `FrameworkElement` for `Opacity`. `FrameworkElement` only has layout/visual properties; interactive state lives on `Control`. However, `IsEnabled` on a container (`Grid`, `StackPanel` — which are `Panel`, not `Control`) does NOT cascade to children in WinUI 3 the way you'd expect. Setting `IsEnabled=false` on a `Grid` leaves the `ComboBox` children inside it fully interactive. **The reliable way to block all interaction on a container and its children is `IsHitTestVisible = false`** — this works on any `FrameworkElement` (including `Grid`, `StackPanel`, `Border`) and blocks all mouse/touch input regardless of whether children are `Control` or `Panel`. Use `IsHitTestVisible = false` + `Opacity = 0.45` for the "disabled" visual state on containers.

- **`DeployStreamlineToGame` always copies the currently staged version, ignoring the per-game version combo**: When the Streamline/DLSS Enabler toggle is set to "Yes", `DeployStreamlineToGame` copies whatever is in the staging folder (e.g. 2.11.3) regardless of what the Streamline Version combo shows (e.g. 2.12.0). The combo only fires `SwapStreamlineAsync` on `SelectionChanged`. Fix: `DeployStreamlineToGame` now accepts `string? version = null` and deploys from the correct version subfolder directly (no swap needed, no `.original` backups). Fallback when version is null: picks the subfolder with the highest version number using `Version.TryParse` + `OrderByDescending` — NOT alphabetical string sort (`"2.10.3"` sorts before `"2.12.0"` alphabetically). Always uses plain `File.Copy(overwrite: true)` — Streamline files deployed by RHI are never game originals so `.original` backups are wrong here. The post-install path in `InstallEventHandler` resolves `GetOsStreamlineVersion` and passes it directly to `DeployStreamlineToGame`.

- **`componentUrls["dc"]` manifest key overrides the DC GitHub API URL**: Display Commander's release API URL (`https://api.github.com/repos/pmnoxx/display-commander/releases/tags/...`) is now overridable via `componentUrls["dc"]` in the manifest without a client update. `DcReleasesApiUrlOverride` is set from the manifest in both `MainViewModel.Init.cs` and `MainViewModel.BackgroundScan.cs`. `EffectiveDcReleasesApiUrl` property in `MainViewModel.Install.Components.cs` resolves the override first, falling back to the hardcoded constant. Both `FetchLatestDcReleaseInfoAsync` and `CheckDcUpdateAsync` use `EffectiveDcReleasesApiUrl`.

- **Vulkan/OpenGL Present Method Flags setting ID confirmed: `0x20324987`**: This is `OGL_DX_PRESENT_METHOD_ID` already defined in `DlssPresetService.cs` as a private constant used by `SetLiliumPresentMethod`. It is NOT `0x000802A5` (which the steering doc previously noted as unknown). The NVPI CustomSettingNames.xml values for this setting: `0x00000000` = Default, `0x00000001` = Disable Fullscreen Opt, `0x00000002` = Disable Present Thread, `0x00080000` = Allow DXVK Promotion, `0x00080004` = Allow DXVK Promotion + DirectFlip, `0x000802A5` = Treat DXVK as Native. `GetVulkanPresentMethodFlags`/`SetVulkanPresentMethodFlags` are now public methods in `DlssPresetService.DriverSettings.cs` using the same `GetPreset`/`SetRtxHdrRaw` pattern as the companion `0x20D690F8` setting.

- **`0x20324987` (OGL_DX_PRESENT_METHOD_ID / Vulkan Flags) works with `SetPreset` (NvAPIWrapper)**: Unlike the RTX HDR IDs (`0x00DD48F*`) which silently fail via NvAPIWrapper, `0x20324987` is a well-known, older setting ID that NvAPIWrapper handles correctly. `SetLiliumPresentMethod` uses `SetPreset` for both `0x20D690F8` and `0x20324987` and the values are confirmed written (visible in NVPI). However the new `SetVulkanPresentMethodFlags` uses `SetRtxHdrRaw` (raw NVAPI) — this is conservative and correct, but `SetPreset` would also work for this specific ID. **Chosen value for Lilium HDR DXVK**: `0x000802A5` ("Treat DXVK as Native") — this is the community-standard recommendation (per xemu, NVPI, and multiple game-specific threads) and is now what `SetLiliumPresentMethod` writes. The previous value `0x00080004` ("Allow DXVK Promotion + DirectFlip") was more targeted for HDR + DirectFlip but less universally documented. Sources: [xemu issue #2845](https://github.com/xemu-project/xemu/issues/2845), [NVPI issue #197](https://github.com/Orbmu2k/nvidiaProfileInspector/issues/197).

- **`DxvkVariantPending` flag pattern for "selected but not installed" UI state**: When the DXVK variant combo is set to a non-Off value but DXVK isn't installed, `DxvkRowVisibility` needs to return `Visible` so the Install button is accessible. Added `[ObservableProperty] bool _dxvkVariantPending` to `GameCardViewModel.Dxvk.cs`. `DxvkRowVisibility` now returns `Visible` when either `DxvkEnabled` OR `DxvkVariantPending` is true. The flag is set in `PopulateDetailPanel` (checks `GetDxvkVariantOverride != null && !DxvkEnabled`) so it persists across sessions without extra storage. Cleared when "Off" is selected.

- **`SelectionChanged` fires during panel construction when `SelectedItem` is set**: Setting `combo.SelectedItem = value` while building the overrides panel triggers `SelectionChanged` immediately — before the combo is even added to the visual tree. This caused the DXVK variant combo to uninstall DXVK on every panel open because it fired with the already-selected value. Fix: declare `bool comboInitializing = true` before the `SelectionChanged` handler, set it to `false` immediately after `column.Children.Add(combo)`. Check `if (comboInitializing) return;` at the top of the handler. This pattern must be used for any combo that sets `SelectedItem` during construction and has a handler with side effects.

- **Use `card` directly in overrides panel handlers, not a re-lookup by name**: In `DetailPanelBuilder.Overrides.*.cs`, `SelectionChanged` handlers that re-find the card via `AllCards.FirstOrDefault(c => c.GameName == capturedName)` can get the wrong card or a stale one — especially when the card's state has changed since the panel was built. Always use `card` (the captured variable from the enclosing method) directly in the handler. The `card` reference is the live `GameCardViewModel` object and reflects the current state. Re-lookup by name-only also breaks for multi-store games.

- **`card.DxvkEnabled` can be false even when DXVK is physically installed**: `DxvkEnabled` is set from `_dxvkEnabledGames` (a persisted HashSet) in `BuildCards`/`CacheLoad`. If the HashSet is out of sync with the actual installed state (e.g. installed from an old session, HashSet not updated), `DxvkEnabled` will be false even though `DxvkStatus == Installed`. When checking "is DXVK currently installed?", always check `card.DxvkStatus == GameStatus.Installed || card.DxvkStatus == GameStatus.UpdateAvailable` — this comes from the tracking record scan and is always accurate. `card.DxvkEnabled` is for controlling the Vulkan ReShade mode, not for detecting installed state.

- **DXVK variant combo does NOT auto-install on selection**: Changing the variant combo saves the preference and makes the DXVK row visible (via `DxvkVariantPending`), but does NOT trigger an install. The user must explicitly click the Install button. Exception: if DXVK is currently installed and the user selects a different variant, the old version is uninstalled first before the new one can be installed. This is intentional — avoid silent installs from accidental combo changes.

- **OptiScaler cog settings greyed when not installed**: All cog settings except "OptiScaler Version" and "Framerate Limit" use `IsHitTestVisible = false` + `Opacity = 0.45` when `!card.IsOsInstalled`. This prevents pre-configuring settings that only apply post-install (FG mode, Streamline, Engine.ini tweaks, etc.) while still allowing the user to select their variant before installing. Apply `IsHitTestVisible = false` to the containing `Grid` or `StackPanel` rather than individual controls — see the `IsEnabled` container pitfall above.

- **Transient card fields for reversible driver setting cascades**: When one driver setting forces another (e.g. Smooth Motion On → Low Latency Ultra), save the previous value in a `uint?` transient property on `GameCardViewModel` so it can be restored when the forcing setting is turned off. Example: `PreSmoothMotionLowLatency` stores the Low Latency value active before Smooth Motion was enabled; on disable, the saved value is restored (falling back to Off if null). This avoids the overhead of persisting a "pre-action" value to `settings.json` for simple toggle-pair relationships. Limitation: the saved value is lost on app restart — if the user restarts with the forcing setting already on, toggling it off restores to the fallback (Off). Acceptable for driver profile settings; use full persistence if the restore value must survive restarts.

## DLL Naming Override System — Architecture & Known Bugs

### Architecture

- **`DllOverrideConfig`** (`Models/DllOverrideConfig.cs`) holds three filenames per game: `ReShadeFileName`, `DcFileName`, `OsFileName`. All three live in one record per game in `DllOverrideService._dllOverrides` (name-only keyed dict, `OrdinalIgnoreCase`).
- **`DllOverrideService`** owns all CRUD and disk-rename operations. `MainViewModel.Settings.cs` is a thin pass-through to it.
- **Three override layers**: (1) user config (persisted), (2) manifest RS/DC override (`_manifestDllOverrideGames`, not persisted, opt-outs persisted), (3) manifest OptiScaler override (`_manifestOsDllOverrides`, not persisted).
- **`GetEffectiveRsName` / `GetEffectiveOsName` / `GetEffectiveDcName`**: resolve the effective filename for each component — user config first, then manifest, then default.
- **`GetAvailableOsDllNames`**: pre-filters the OS dropdown to exclude names claimed by RS or DC effective names (from config/defaults). Used to populate the `osNameBox` in the overrides panel.
- **`UpdateDllOverrideNames`**: called when a combo selection changes while the override is already ON — physically renames files on disk and updates `AuxInstalledRecord.InstalledAs` + card state.
- **`EnableDllOverride`** / **`DisableDllOverride`**: called by the ToggleSwitch. Disable reverts DC first, then RS (order matters — DC may have claimed dxgi.dll).

### ReShade install filename resolution (call site → `InstallReShadeAsync`)

`MainViewModel.InstallReShadeAsync` resolves `rsFilenameOverride` before calling `_auxInstaller.InstallReShadeAsync(filenameOverride: ...)`:
```
card.DllOverrideEnabled  → GetDllOverride(gameName)?.ReShadeFileName
otherwise                → manifest DLL override → ResolveAutoReShadeFilename(DetectedApis)
```

Inside `AuxInstallService.InstallReShadeAsync`, `filenameOverride` is applied as `destName`, then two checks may override it:
1. **OptiScaler coexistence** — if an OptiScaler record exists, `destName = "ReShade64.dll"` unconditionally
2. **DC occupancy** — if DC is installed at the same name, falls back to `ReShade64.dll`/`ReShade32.dll`

`UpdateOrchestrationService` uses the same resolution pattern for Update All RS.

### Known Bugs — Fixed

- **Bug 1 — OptiScaler coexistence check was unconditional** (fixed): `InstallReShadeAsync` was forcing `destName = "ReShade64.dll"` any time an OptiScaler record existed, even with no filename conflict. Fixed: the check is now conditional — `if (osRecord != null && osRecord.InstalledAs.Equals(destName, OrdinalIgnoreCase))`. Only renames when both components would use the same filename.

- **Bug 2 — RS rename combo didn't exclude the OS effective name** (fixed): `UpdateRsDropdownItems()` now also filters `GetEffectiveOsName(gameName)` out of the RS dropdown, alongside the DC name. The RS combo is still editable (typed values bypass filtering), but the dropdown no longer offers OptiScaler's filename as an option.

- **`GetAvailableOsDllNames` was over-filtering based on defaults, not actual installs**: The original implementation excluded the RS effective name (`GetEffectiveRsName`) and DC effective name (`GetEffectiveDcName`) from the OS dropdown regardless of whether those components were installed. Since `GetEffectiveRsName` returns `"dxgi.dll"` as the default when no RS override is set, `dxgi.dll` was always absent from the OptiScaler dropdown even when ReShade wasn't installed. Fix: `GetAvailableOsDllNames` now takes optional `rsInstalledAs` / `dcInstalledAs` parameters (the actual `AuxInstalledRecord.InstalledAs` values). Only a filename that is actively in use by an installed component is excluded. The call site in `DetailPanelBuilder.Overrides.cs` passes `card.RsInstalledFile` and `card.DcInstalledFile`. Rule: collision filtering must be based on what's physically installed, not on what the default fallback name happens to be.

- **Bug 3 — `UpdateDllOverrideNames` renamed RS file without checking OS occupancy** (fixed): The RS rename path now has an OS collision guard matching the existing DC guard in `RenameDcFile`. If `newRsName.Equals(GetEffectiveOsName(name), OrdinalIgnoreCase)`, the rename is skipped and logged instead of overwriting the OptiScaler DLL.

- **`TryAcquireDialogGate()` is zero-timeout — never use it for critical user-initiated actions**: `TryAcquireDialogGate()` calls `_dialogGate.Wait(0)` and returns `false` immediately if any other dialog is open. Using it for the app update download progress dialog caused the update to silently abort if the MOTD or patch notes dialog happened to be showing at the same moment the user clicked "Update Now". Fix: use `WaitDialogGateAsync(timeoutSeconds)` instead for critical flows — it waits up to N seconds for the gate to become available. `TryAcquireDialogGate()` is still appropriate for low-priority non-blocking patterns (e.g. drag-drop confirmation) where skipping is acceptable. Rule: if the action is user-initiated and consequential (update, install), wait for the gate; if it's background/opportunistic, skip on conflict.

- **DLL collision filtering must check both installed filename AND configured override name**: `card.RsInstalledFile` is only populated when ReShade is physically installed (has a tracking record). If the user has a DLL override configured (RS = `d3d12.dll`) but hasn't installed ReShade yet, `card.RsInstalledFile` is null — so name-collision filters that only check `card.RsInstalledFile` miss the conflict entirely. Always use `card.RsInstalledFile ?? existingCfg?.ReShadeFileName` (installed name first, fall back to configured override) when building exclusion lists for the OS DLL dropdown. The same applies to DC: `card.DcInstalledFile ?? existingCfg?.DcFileName`. A second-layer guard in the `SelectionChanged` handler catches anything that slips through the dropdown filter.

- **OS combo `SelectionChanged` handler must use `card` directly, not re-lookup by name**: The original `osNameBox.SelectionChanged` handler used `AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName))` to find the card. Without store matching, this picks the wrong card for multi-store games (e.g. "20 Minutes Till Dawn" on Epic vs Steam). The wrong card's `RsInstalledFile` and `DcInstalledFile` were then used for collision checks and file rename operations, causing incorrect blocking and potentially renaming files in the wrong game folder. Fix: use `card` (the captured closure variable) directly — same rule as all other overrides panel handlers.

- **OS combo `_osComboInitializing` guard prevents save-on-panel-build**: The OS combo's `SelectionChanged` handler must have an initializing flag (`bool _osComboInitializing = true`, set to `false` after the handler is attached) to prevent the pre-selection from triggering `SetOsDllOverride`. Without this, every panel open could silently persist the display-default (`dxgi.dll`) as the stored override, overwriting a legitimate user setting like `dbghelp.dll`. Pattern matches the existing `comboInitializing` guards on the RS channel and DXVK variant combos.

- **`dllOverrideToggle.Toggled` handler must use `card` directly**: The Toggled handler used `AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName))` without store matching — for multi-store games this could return null or the wrong card, causing `DisableDllOverride` to be silently skipped (the `if (targetCard == null) return` early exit). The fix is the same pattern as all other overrides panel handlers: use `card` (the captured closure variable) directly.

- **`DisableDllOverride` does not revert the OptiScaler DLL name**: `DisableDllOverride` in `DllOverrideService` only reverts RS and DC filenames. The OptiScaler DLL override (`DllOverrideConfig.OsFileName`) is a separate field and must be manually reverted at the call site (toggle-off path in `DetailPanelBuilder.Overrides.cs`). On toggle-off: rename the OS file back to `OptiScalerService.DefaultDllName` ("dxgi.dll") if a custom name was set and OptiScaler is installed, update the tracking record, and call `SetOsDllOverride(capturedName, "")` to clear the config. Guard: only rename if the destination doesn't already exist.

- **OptiScaler preset system**: 4 global user-configurable preset slots stored in `%LocalAppData%\RHI\os_presets.json` as a `OsPreset?[4]` JSON array. `OsPresetService.Load()` / `Save()` are static helpers. `OsPreset` model captures: FgInput, FgOutput, FgNvngxReplacement, DeployStreamline, DeployDlssEnabler, SrPreset, RrPreset, RenderScale, DisableFlipMetering, HudFix, FramerateLimit, Name. Presets apply to any game (global, not per-game). Only the top 3 cog sections are captured — Engine.ini settings are excluded.

- **Hoisting local variables to method scope for cross-closure capture in `OsCogButton_Click`**: The cog dialog's combo variables (`fgInputCombo`, `fgOutputCombo`, etc.) and converter delegates (`FgInputToIni`, `FgOutputToIni`, `FgNvngxToIni`) are declared as nullable at method scope (`ComboBox? fgInputCombo = null; Func<string,string>? FgInputToIni = null;`) before the `if (isNightly)` block, then assigned inside it. This allows the presets section (also inside a separate `if (isNightly)` block in `bottomBorder`) to capture them in closures. Without hoisting, the presets lambdas couldn't see the combo variables. Handlers inside the original nightly block use null-forgiving operators (`FgInputToIni!(...)`) since they only run when the combos are guaranteed non-null.

- **"Don't show again" checkboxes must persist to `SettingsViewModel`, not in-memory service fields**: The DXVK warning dialog used `_dxvkService.FirstTimeWarningAcknowledged` (in-memory bool on the service) for the "Don't show this warning again" checkbox. This reset to `false` every session so the warning always reappeared. Fix: add a `[ObservableProperty] bool _dxvkWarningDismissed` to `SettingsViewModel` with matching load/save entries, check it in the install method, and call `SaveSettingsPublic()` when the checkbox is ticked. Pattern matches `AddonWarningDismissed`, `MfgWarningDismissed`, etc.

- **Conditional right-column UI in the DXVK row was gated on `card.DxvkEnabled`**: The Lilium HDR preset combo only showed when `card.DxvkEnabled` was true — meaning users who selected "Lilium HDR" from the variant dropdown but hadn't installed yet never saw the preset picker. Fix: check `GetDxvkVariantOverride == "LiliumHdr" || defaultDxvkSelection == "Lilium HDR"` as an additional condition, independent of install state. General rule: UI controls for configuring a variant should appear as soon as the variant is selected, not only after installation.

- **Bespoke drag-dropped Luma mods require `LumaMod` to be synthesized on the card**: The Luma row visibility gate is `card.LumaFeatureEnabled && card.LumaMod != null`. For DX9 or other non-UE games, neither the wiki named-mod path nor the generic UE DX11 injection path sets `card.LumaMod` during `BuildCards`/`CacheLoad`. The drag-drop handler (`DragDropHandler.Luma.cs`) also never set it — it only set `LumaRecord`, `LumaStatus`, and `IsLumaMode`. Fix: after a drag-drop Luma install, synthesize a minimal `LumaMod { Name, IsGenericLuma=false, Status="✅" }` on the card if `LumaMod == null`. Same fallback added to `BuildCards` and `CacheLoad`: if `LumaMod` is still null after all injection paths but a `LumaRecord` exists on disk, synthesize one. This ensures bespoke mods on non-standard-engine games persist the row across restarts.

- **`PopulateDetailPanel` must be called after drag-drop install for imperative UI updates**: After `DragDropHandler.Luma.cs` completes, it called `card.NotifyAll()` which fires `PropertyChanged` — but `DetailPanelBuilder` is imperative (not data-bound), so the panel never re-evaluates `hasLumaRow`. Fix: dispatch `_window.PopulateDetailPanel(card)` after setting the card state. This is the same pattern documented for other post-install UI updates but was missing from the Luma drag-drop path.

- **`TryAcquireDialogGate()` is zero-timeout — never use it for critical user-initiated actions**: `TryAcquireDialogGate()` calls `_dialogGate.Wait(0)` and returns `false` immediately if any other dialog is open. Using it for the app update download progress dialog caused the update to silently abort if the MOTD or patch notes dialog happened to be showing at the same moment the user clicked "Update Now". Fix: use `WaitDialogGateAsync(timeoutSeconds)` instead for critical flows — it waits up to N seconds for the gate to become available. `TryAcquireDialogGate()` is still appropriate for low-priority non-blocking patterns (e.g. drag-drop confirmation) where skipping is acceptable. Rule: if the action is user-initiated and consequential (update, install), wait for the gate; if it's background/opportunistic, skip on conflict.

- **"Don't show again" checkboxes must persist to `SettingsViewModel`, not in-memory service fields**: The DXVK warning dialog used `_dxvkService.FirstTimeWarningAcknowledged` (in-memory bool on the service) for the "Don't show this warning again" checkbox. This reset to `false` every session so the warning always reappeared. Fix: add a `[ObservableProperty] bool _dxvkWarningDismissed` to `SettingsViewModel` with matching load/save entries, check it in the install method, and call `SaveSettingsPublic()` when the checkbox is ticked. Pattern matches `AddonWarningDismissed`, `MfgWarningDismissed`, etc.

- **Conditional right-column UI in the DXVK row was gated on `card.DxvkEnabled`**: The Lilium HDR preset combo only showed when `card.DxvkEnabled` was true — meaning users who selected "Lilium HDR" from the variant dropdown but hadn't installed yet never saw the preset picker. Fix: check `GetDxvkVariantOverride == "LiliumHdr" || defaultDxvkSelection == "Lilium HDR"` as an additional condition, independent of install state. General rule: UI controls for configuring a variant should appear as soon as the variant is selected, not only after installation.

- **Bespoke drag-dropped Luma mods require `LumaMod` to be synthesized on the card**: The Luma row visibility gate is `card.LumaFeatureEnabled && card.LumaMod != null`. For DX9 or other non-UE games, neither the wiki named-mod path nor the generic UE DX11 injection path sets `card.LumaMod` during `BuildCards`/`CacheLoad`. The drag-drop handler (`DragDropHandler.Luma.cs`) also never set it — it only set `LumaRecord`, `LumaStatus`, and `IsLumaMode`. Fix: after a drag-drop Luma install, synthesize a minimal `LumaMod { Name, IsGenericLuma=false, Status="✅" }` on the card if `LumaMod == null`. Same fallback added to `BuildCards` and `CacheLoad`: if `LumaMod` is still null after all injection paths but a `LumaRecord` exists on disk, synthesize one. This ensures bespoke mods on non-standard-engine games persist the row across restarts.

- **`PopulateDetailPanel` must be called after drag-drop install for imperative UI updates**: After `DragDropHandler.Luma.cs` completes, it called `card.NotifyAll()` which fires `PropertyChanged` — but `DetailPanelBuilder` is imperative (not data-bound), so the panel never re-evaluates `hasLumaRow`. Fix: dispatch `_window.PopulateDetailPanel(card)` after setting the card state. This is the same pattern documented for other post-install UI updates but was missing from the Luma drag-drop path.

- **Transient card fields for reversible driver setting cascades**: When one driver setting forces another (e.g. Smooth Motion On → Low Latency Ultra), save the previous value in a `uint?` transient property on `GameCardViewModel` so it can be restored when the forcing setting is turned off. Example: `PreSmoothMotionLowLatency` stores the Low Latency value active before Smooth Motion was enabled; on disable, the saved value is restored (falling back to Off if null). This avoids the overhead of persisting a "pre-action" value to `settings.json` for simple toggle-pair relationships. Limitation: the saved value is lost on app restart — if the user restarts with the forcing setting already on, toggling it off restores to the fallback (Off). Acceptable for driver profile settings; use full persistence if the restore value must survive restarts.

- **OptiScaler preset system**: 4 global user-configurable preset slots stored in `%LocalAppData%\RHI\os_presets.json` as a `OsPreset?[4]` JSON array. `OsPresetService.Load()` / `Save()` are static helpers. `OsPreset` model (`Models/OsPreset.cs`) captures: FgInput, FgOutput, FgNvngxReplacement, DeployStreamline, DeployDlssEnabler, SrPreset, RrPreset, RenderScale, DisableFlipMetering, HudFix, FramerateLimit, Name. Presets apply to any game (global, not per-game). Only the top 3 cog sections are captured — Engine.ini settings are excluded.

- **`SeenLumaModsService` — new Luma mods alert**: `SeenLumaModsService.cs` tracks which Luma mod names the user has already seen. `NewLumaMods` property on `MainViewModel` is populated in all 3 scan paths (BuildCards, BackgroundScan, CacheLoad). The "New Mods" dialog includes a `🌙 Luma` section when new Luma mods are detected. Model lives in `Services/SeenLumaModsService.cs`.

- **RS DLL combo is dropdown-only (non-editable)**: The ReShade DLL rename combo was made non-editable — users select from the dropdown only, no free-text input. This prevents invalid filenames from being entered and simplifies the combo collision filtering logic.

- **OptiScaler cog greyed when not installed**: All cog settings except "OptiScaler Version" and "Framerate Limit" are disabled (`IsHitTestVisible = false`, `Opacity = 0.45`) when `!card.IsOsInstalled`. Settings that only apply post-install (FG mode, Streamline, Engine.ini tweaks) should not be pre-configurable; variant and framerate can be pre-set.

- **Per-game settings cleared on OptiScaler uninstall**: `InstallEventHandler` resets all per-game OptiScaler settings (`SetOsFgInput`, `SetOsDeployStreamline`, `SetOsDeployDlssEnabler`, Engine.ini keys, etc.) immediately after `_optiScalerService.Uninstall(card)` returns, where `ViewModel` is accessible. `OptiScalerService` itself does not have access to `IGameNameService` and cannot do this cleanup.

- **DLL collision filtering must check both installed filename AND configured override name**: `card.RsInstalledFile` is only populated when ReShade is physically installed. If the user has a DLL override configured but hasn't installed yet, `card.RsInstalledFile` is null — filters that only check `card.RsInstalledFile` miss the conflict. Always use `card.RsInstalledFile ?? existingCfg?.ReShadeFileName` (and `card.DcInstalledFile ?? existingCfg?.DcFileName`) when building exclusion lists. A second-layer guard in the `SelectionChanged` handler catches anything the dropdown filter misses.

- **`DisableDllOverride` does not revert the OptiScaler DLL name**: `DisableDllOverride` in `DllOverrideService` only reverts RS and DC filenames. The OptiScaler DLL override (`DllOverrideConfig.OsFileName`) must be manually reverted at the call site (toggle-off path in `DetailPanelBuilder.Overrides.cs`): rename the OS file back to `OptiScalerService.DefaultDllName` if a custom name was set and OptiScaler is installed, update the tracking record, and call `SetOsDllOverride(capturedName, "")` to clear the config.

- **`dllOverrideToggle.Toggled` handler must use `card` directly**: The Toggled handler originally used `AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName))` without store matching — for multi-store games this could return null or the wrong card, causing `DisableDllOverride` to be silently skipped (the `if (targetCard == null) return` early exit). Fix: use `card` (the captured closure variable) directly. Same rule as all overrides panel handlers.

## Developer Unlock Gate

- **`DevUnlockService`** (`Services/DevUnlockService.cs`) — static class that gates developer-only features behind the existence of `%LocalAppData%\RHI\unlock.txt`. `IsUnlocked` is a static bool property, checked once on first access and cached for the process lifetime. No DI required — call `DevUnlockService.IsUnlocked` directly anywhere. Restart required to pick up file add/remove. Usage pattern: `if (DevUnlockService.IsUnlocked) { ... }` or `button.Visibility = DevUnlockService.IsUnlocked ? Visibility.Visible : Visibility.Collapsed;`

## Resolution Auto-Toggle Feature

- **`ResolutionToggleService`** (`Services/ResolutionToggleService.cs`) — static class using Win32 `EnumDisplaySettings` / `ChangeDisplaySettingsEx` P/Invoke. `GetSupportedResolutions()` returns all modes sorted by width/height/Hz. `GetCurrentResolution()` reads the live desktop mode. `SetResolution(DisplayResolution)` applies a mode with `CDS_UPDATEREGISTRY`. `RestoreResolution()` passes null DEVMODE to revert to the registered default. `DisplayResolution` is a record with a `Key` property (`"WxH@Hz"`) used for persistence and a `Parse(string)` factory. No admin required for the primary display.

- **Per-game `ResToggleOverrides`**: name-only `Dictionary<string, string>` in `GameNameService` (same pattern as `HdrToggleOverrides` — values `"On"`/`"Off"`, absent = use global). Persisted as `"ResToggleOverrides"` in `settings.json`. Migrated via `MigrateDict` in `RenameGame`. Exposed on `IGameNameService`.

- **Global settings in `SettingsViewModel`**: `ResolutionAutoToggle` (bool), `ResolutionTarget` (string key `"WxH@Hz"`), `ResTargetDisplays` (List\<uint\> of CCD targetIds). Load/save follow the same pattern as `HdrAutoToggle` / `HdrTargetDisplays`.

- **Settings UI card** (`ResolutionControlCard` in `MainWindow.xaml`): entirely `Visibility="Collapsed"` — non-dev users see nothing. Shown by `SettingsHandler.InitSettings` only when `DevUnlockService.IsUnlocked`. Resolution combo populated from `ResolutionToggleService.GetSupportedResolutions()` with `DisplayMemberPath = "Label"`. Monitor selector dialog mirrors the HDR one but uses `HdrToggleService.GetAllDisplays()` (no HDR-support filter since all monitors can have resolution changed).

- **`DetailResToggleBtn` / `DetailResToggleText`** in XAML card row: `Visibility="Collapsed"` by default, shown only when `DevUnlockService.IsUnlocked` in `DetailPanelBuilder.cs`. Same purple active / neutral inactive color pattern as `DetailHdrToggleBtn`.

- **Launch integration**: resolution change fires in `LaunchGame` immediately after the HDR block, guarded by `DevUnlockService.IsUnlocked`. Current resolution is captured to `resolutionToRestore` before switching. Both `MonitorProcessForHdr` exit paths (direct process + protocol poll) call `SetResolution(resolutionToRestore)` on game exit, falling back to `RestoreResolution()` if the captured value is null.

- **Ungating for public release**: remove the `DevUnlockService.IsUnlocked` guard in `SettingsHandler.InitSettings` (two lines: card visibility + combo population) and in `DetailPanelBuilder.cs` (the if/else around `DetailResToggleBtn`). No other changes needed.

- **`BuildShadersAddonsSection` crashes on stale install paths — causes full UI freeze**: `DetailPanelBuilder.BuildShadersAddonsSection` calls `Directory.GetFiles(installPath, "*.exe", ...)` without a `Directory.Exists()` guard. If a manually-added game has a stale/deleted `InstallPath`, selecting that card throws `DirectoryNotFoundException`. WinUI catches it as non-terminating (`Handled = true`) but `SelectionChanged` fires again immediately on the next interaction and throws again — creating a tight loop that eats all user input and makes the app appear frozen. Confirmed cause: removing a game and re-adding it in the same session leaves `LastSelectedGameName` pointing to the old path; on restart, auto-selection triggers the loop instantly. Fix: check `!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath)` before the `Directory.GetFiles` call. Rule: any `Directory.GetFiles` or `Directory.EnumerateFiles` in a UI builder method must be guarded with `Directory.Exists` first.

- **OptiScaler backup rule — all root files must back up originals**: `BackupOriginalIfExists` is called before `File.Copy` for every file deployed to the game root during `InstallAsync`. This includes the main OptiScaler DLL (`effectiveDllName`), all companion DLLs from the staging root (e.g. `amd_fidelityfx_framegeneration_dx12.dll`, `amd_fidelityfx_upscaler_dx12.dll`), and the three DLSS DLLs (`nvngx_dlss.dll`, `nvngx_dlssd.dll`, `nvngx_dlssg.dll`). Games like Stalker 2 / The First Berserker ship their own AMD FidelityFX DLLs — without backup these are permanently lost on uninstall. Subdirectory files (`D3D12_OptiScaler/`, `OptiScaler/Streamline/`, etc.) are still never backed up — those are OptiScaler-created folders with no pre-existing game files. During `UpdateAsync`, no backups are created — those files are from the previous OptiScaler install, not game originals. The uninstall already calls `RestoreOriginalIfExists` in all three loops (main DLL, companion files, subdirectory files) — the restore calls were always there and are now actually meaningful for root companion files.

- **`BuildShadersAddonsSection` missing `Directory.Exists()` guard**: Calling `Directory.GetFiles(installPath, ...)` without checking `Directory.Exists(installPath)` first causes a `DirectoryNotFoundException` when a manually-added game has a stale/deleted install path. WinUI catches it as non-terminating but generates a crash file on every card selection. Fix: add `if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) return;` at the top of the method before any file enumeration.

- **`LumaService.Uninstall` must not delete the `reshade-shaders` root**: The uninstall sequence calls `_shaderPackService.RemoveFromGameFolder` (removes Luma's shader files) then `CleanEmptyDirs(rsDir)` on the `reshade-shaders` root. If no other shader packs are active for that game, `reshade-shaders` ends up empty and `CleanEmptyDirs` deletes it — even when ReShade is still installed and owns that folder. Fix: only clean subdirectories within `reshade-shaders` (`foreach sub in GetDirectories(rsDir) → CleanEmptyDirs(sub)`), never pass the root itself to `CleanEmptyDirs`. The root `reshade-shaders` directory belongs to ReShade, not Luma.

- **Luma `[Luma]` reshade.ini section — `DisplayMode` required alongside `EnableHDR`**: The Luma cog "HDR on First Boot" combo has three options: Default / Off / On. **Off**: writes `EnableHDR=0` + `DisplayMode=0`. **On**: writes `EnableHDR=1` + `DisplayMode=1`. **Default**: removes both keys from reshade.ini so Luma manages them itself. `EnableHDR=0` alone is insufficient to disable HDR output — `DisplayMode=0` must also be set. Both keys live in the `[Luma]` section of `reshade.ini`. Helpers: `SetLumaReshadeIniValue(gameDir, key, value)`, `GetLumaReshadeIniValue(gameDir, key)`, `RemoveLumaReshadeIniValue(gameDir, key)` — all in `AuxInstallService.Ini.cs`. On fresh Luma install, both keys are written with value `1` (explicit On). The combo selected index on open: key absent → Default (0), `"0"` → Off (1), `"1"` → On (2). The cog handler writes/removes both keys atomically.

- **`InstallEventHandler` extract pattern for reusable uninstall logic**: When an uninstall operation is complex (e.g. OptiScaler needs Engine.ini cleanup + per-game settings reset + REFramework restore), extract it into a `public void UninstallX(GameCardViewModel card)` method on `InstallEventHandler`, and have the button click handler call it. This makes the logic reusable from other call sites (e.g. the "Remove game" confirmation dialog) without duplicating the cleanup steps. Applied to `UninstallOptiScaler(card)` — `UninstallOsButton_Click` now just calls it, and `RemoveManualGame_Click` can also call it. Safe uninstall order when removing all components: Luma → OptiScaler → DXVK → ReLimiter → DC → RE Framework → RenoDX → ReShade.

- **Remove manual game confirmation dialog**: `RemoveManualGame_Click` is now `async` and shows a `ContentDialog` before executing. If installed components are detected, the dialog lists them and includes a checkbox "Also uninstall all RHI-managed components from game folder". If no components are installed, a simple confirm is shown. `ViewModel.RemoveManualGameCommand.Execute(card)` only fires after the user confirms. The method is in `MainWindow.Events.Install.cs` and has access to `Content.XamlRoot` for the dialog.

- **DLSS version dropdown falls back to index 0 when installed version is not in manifest**: The `BuildDlssColumn` loop searches for a matching item and leaves `selectedIndex = 0` if nothing matches — which silently shows the `(Default)` item instead of the real installed version. Fix: after the loop, if `matched == false`, insert the installed version string directly into the items list (before "Custom") and select it. This handles custom/pre-release/early-access DLSS builds that aren't in the manifest.

- **Streamline detection requires `sl.interposer.dll` — falls back to `sl.common.dll`**: `DlssStreamlineService` uses `sl.interposer.dll` as the primary indicator for Streamline presence and the source for version reading. Some Streamline builds (e.g. certain variants) don't include `sl.interposer.dll`. Fix applied in three places: (1) `DlssStreamlineService.Detection.cs` scan — if `StreamlineFolder` is found via another known SL DLL but no interposer exists, fall back to `sl.common.dll` for `StreamlineInterposerPath` and version. (2) `DlssStreamlineService.cs` `TryFastDetect` — try interposer first, fall back to `sl.common.dll` as `versionSourcePath`. (3) `MainViewModel.CacheLoad.cs` — prefer interposer path, fall back to common path when constructing `StreamlineInterposerPath` from the cached folder.

## Dev-Only DLSS Version Lists

- **`dlss_manifest.json` dev version fields**: `DlssManifestData` has four optional parallel arrays — `DlssDev`, `DlssdDev`, `DlssgDev`, `StreamlineDev` — same `{ version, url }` structure as the regular arrays. Added alongside the existing `Dlss`/`Dlssd`/`Dlssg`/`Streamline` fields. When `DevUnlockService.IsUnlocked`, dev entries are prepended to the regular list in `BuildVersionList()` and show at the top of the dropdown. When locked, dev entries are invisible and not downloadable.
- **`FindEntry` helper pattern**: `DlssStreamlineService.Swap.cs` uses four private `FindXxxEntry(version)` methods that search both the regular list and the dev list, so `SwapDlssAsync` etc transparently handle dev versions without the caller knowing which list the entry came from. Pattern: try regular list first, then dev list as fallback.

- **`dlssPresetsDev` manifest field — dev-only DLSS preset entries**: `RemoteManifest.DlssPresetsDev` (JSON key `"dlssPresetsDev"`) has the same structure as `dlssPresets` (`{ sr, rr, fg }` arrays of `{ name, value }`). In `DlssPresetService.ApplyManifestPresets`, dev presets are only merged into `SrPresets`/`RrPresets`/`FgPresets` when `DevUnlockService.IsUnlocked`. When locked, dev preset entries are completely invisible — not in per-game panels, defaults dialog, or mass deploy. Usage: `"dlssPresetsDev": { "rr": [{ "name": "F - CNN", "value": 6 }] }`. The same `MergePresets` logic applies — duplicates by value are skipped, disabled entries are removed.

- **Stale `_rhi_custom_streamline` marker causes version to show as "Custom"**: The `DetailPanelBuilder` Streamline column checks `IsCustomStreamlineActive(folder)` and overrides the displayed version to `"Custom"` if the marker file exists. If custom Streamline files are manually replaced with a real versioned build (without going through `SwapStreamlineAsync` which removes the marker), the marker persists and the correct version never shows. Fix: in `DetailPanelBuilder.Overrides.NvidiaProfile.cs`, only show "Custom" when the marker is present AND the DLL version is null or "Unknown". If we can read a real version number from `sl.common.dll` (or `sl.interposer.dll`), always show that version regardless of the marker. The marker is still written/cleared correctly for tracking purposes — it just no longer hides a readable version.

- **`RefreshDlssVersions` reads stale Streamline version when custom folder omits `sl.interposer.dll`**: `RefreshDlssVersions` reads `StreamlineInstalledVersion` from `DlssDetection.StreamlineInterposerPath` (normally `sl.interposer.dll`). After `SwapStreamlineCustomAsync`, if the custom folder doesn't include `sl.interposer.dll`, the game folder's interposer stays at the old version while `sl.common.dll` gets the new version. The UI then shows the old version number. Fix in `RefreshDlssVersions`: when the custom marker (`_rhi_custom_streamline`) is active, skip the interposer and go straight to `sl.common.dll` for the version — it's the most reliably updated DLL in partial custom installs. Fall back to the interposer path only when no custom marker, and fall back to `sl.common.dll` as a last resort for non-custom cases too. `DlssDetection.StreamlineInterposerPath` is updated in-place to the chosen source so future reads are consistent.

- **`ApplyManifestPresets` must also run during cache load Phase 1**: `ApplyManifestPresets` was only called in `MainViewModel.Init.cs` (full scan) and `MainViewModel.BackgroundScan.cs` (Phase 2). Manifest-driven preset entries (including dev presets) were absent until Phase 2 completed, causing the preset dropdown to show "Default" on launch. Fix: add `DlssPresetService.ApplyManifestPresets(cachedManifest)` to `LoadCacheAndBuildCardsAsync` right after the bitness overrides are applied. Same pattern as other cached manifest properties that must be correct during Phase 1 display.

- **`ApplyDlssDetection` overwrites Streamline version with stale interposer value on Phase 2 refresh**: `ApplyDlssDetection` directly sets `StreamlineInstalledVersion` from `detection.StreamlineVersion` (read from `sl.interposer.dll` during scan). When custom Streamline is active and only `sl.common.dll` was updated, the Phase 2 scan reverts the displayed version from the correct `sl.common.dll` value back to the stale interposer version. Fix: apply the same custom marker / `sl.common.dll` priority logic in `ApplyDlssDetection` as in `RefreshDlssVersions` — when `IsCustomStreamlineActive`, read version from `sl.common.dll` and update `StreamlineInterposerPath` in-place.

- **`FileVersionInfo.FileVersion` uses locale-specific separators (commas on some systems)**: Never use `FileVersionInfo.GetVersionInfo(path).FileVersion` directly for version strings — it returns `"2,12,128,0"` instead of `"2.12.128.0"` on systems where the locale decimal separator is a comma. Always use the individual int properties: `$"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}"`. This is exactly what `DlssStreamlineService.GetFileVersion()` does — always route through that helper rather than reading `FileVersion` string directly.

- **DLDSR/DSR factor settings are NOT in the 3D driver profile system**: DLDSR and DSR factors are not accessible via `NvAPI_DRS_*` (the profile API used for presets, RTX HDR, render scale etc). The NVPI `CustomSettingNames.xml` and `Reference.xml` do not contain DSR factor setting IDs. Community-sourced IDs `0x10D1220D` (DSR Factors bitmask) and `0x10D1220E` (DSR Smoothness 0–100) exist but could not be verified against a live driver profile — writing them had no effect. **Do not implement DLDSR control until the correct IDs are confirmed via NVPI Base Profile inspection with DLDSR actively enabled in NVIDIA App.** The implementation was reverted.

- **`dotnet build --no-restore` can produce stale XAML codegen errors**: When XAML is changed, `--no-restore` may use cached generated code from a previous build, causing `CS1061` errors like "MainWindow does not contain a definition for 'DetailPanel'" — all named XAML elements appear missing. This is a false positive from stale obj outputs, not a real code error. Fix: run `dotnet build` without `--no-restore` to let MSBuild regenerate the XAML codegen. Only use `--no-restore` when you're confident the XAML hasn't changed.

- **DLSS skip cache re-check on standard refresh**: `DlssStreamlineService.RecheckSkipList(games)` runs in `RunBackgroundScanAndMergeAsync` just before `BuildCards`. It re-scans games currently in the skip cache (count ≥ 3, confirmed no DLSS). If DLSS is now found (e.g. a preloaded game that got its files on release day), the game is removed from the skip cache so `BuildCards` will scan it normally and it flows into the trusted path system. Games that still have no DLSS are left completely untouched — count is not incremented. Full Refresh behaviour is unchanged (still wipes the entire skip cache via `ClearScanCaches`). This avoids requiring a full refresh just to detect newly-released DLSS files on games that were pre-installed before their content arrived.

- **Streamline AppData backup for original game DLLs**: `SwapStreamlineAsync` and `SwapStreamlineCustomAsync` both call `BackupStreamlineToAppData(gameFolder)` before replacing any DLLs. This stores the game's original Streamline DLLs as a single zip at `%LocalAppData%\RHI\StreamlineBackups\{hash}.zip` (8-char MD5 hash of the full gameFolder path, `CompressionLevel.Fastest`). Only runs once per game folder — zip existence check prevents overwriting original with a later-swapped version. `RestoreStreamline` falls back to extracting from this zip if in-game `.original` files are gone. Both swap methods also create in-game `.original` backups for immediate restore. Rule: always call `BackupStreamlineToAppData` at the start of any Streamline swap operation.

- **`settings.json` race condition on concurrent shader pack writes**: Two shader pack operations running in parallel (e.g. LumeniteFX and RenoFXHDRToolkit both completing at the same time) can race to write `settings.json`, causing `IOException: being used by another process`. Result: version cache isn't saved, packs re-download on next session. Fix: added a `static SemaphoreSlim(1,1) _settingsLock` in `ShaderPackService.Download.cs` that serializes all four methods accessing `settings.json` — `LoadStoredVersion`, `SaveStoredVersion`, `PackHasExtractedFiles`, and `RecordExtractedFiles`. Static ensures it's shared across all parallel pack operations regardless of instance.

- **Slow lightweight card build when UltrawideFixService fetches from network during Phase 1**: If the UW fix cache is stale, `UltrawideFixService` fetches from network (`Rose fetched from network`, `p1xel fetched from network`). If this happens during `LoadCacheAndBuildCardsAsync` (Phase 1), the lightweight card build blocks waiting for the network fetch, causing 2-3+ second delays. These network fetches should complete in background and not block the cache phase card build.

## Shader Pack System

### Architecture
- `ShaderPackService.cs` — pack definitions (`ShaderPack` record), `AvailablePacks`, `GetPackDescription`, `GetRequiredPacks`, `ExpandPackDependencies`. Pack-level dependencies declared via `Requires` string array on the record.
- `ShaderPackService.Download.cs` — downloads/extracts packs to `%LocalAppData%\RHI\reshade\Shaders\` staging dir. Per-pack file lists stored in `settings.json` as `ShaderPack_{id}_Files`. Per-pack version tokens stored as `ShaderPack_{id}_Version`. Uses `_settingsLock` (static `SemaphoreSlim`) to serialize all settings.json access.
- `ShaderPackService.Deploy.cs` — `DeployPacksIfAbsent`, `SyncGameFolder`, `DeployToGameFolder`. Deployment respects per-pack file exclusions via `Dictionary<string, HashSet<string>>? fileExclusions` parameter (packId → excluded leaf filenames).
- `ShaderPackService.IncludeScan.cs` — `BuildIncludeMap()` scans staged `.fx`/`.fxh` files, strips block/line comments, extracts `#include` leaf filenames, caches result in `_includeMapCache`. `ClearIncludeCache()` called after each successful pack extraction. `GetPackShaderFiles(IEnumerable<string> packIds)` returns sorted `.fx` leaf filenames from the stored file list for given packs.
- `ShaderPopupHelper.cs` — builds and shows the `ContentDialog` for both global and per-game shader selection. Single shared implementation; both contexts call `ShowAsync`.
- `IShaderPackService.cs` — public interface; all new methods must be added here.

### Per-File Shader Selection (v2.3.6+)
Users can expand any pack row to tick/untick individual `.fx` files. Excluded files are persisted to `settings.json` as `excluded_{packId}` (JSON array of leaf filenames). The deploy pipeline skips excluded files in every code path: `DeployPacksIfAbsent`, `SyncGameFolder`, `DeployToGameFolder`, `AuxInstallService.InstallReShadeAsync`, `LumaService`, and all `MainViewModel` batch sync loops.

**Exclusions dict pattern at call sites:**
```csharp
var exclusions = selectedPackIds?
    .ToDictionary(id => id, id => shaderPackService.GetExcludedFiles(id),
        StringComparer.OrdinalIgnoreCase);
```
Pass as `fileExclusions` to `SyncGameFolder` / `DeployToGameFolder`. Any new call site that deploys shaders must follow this pattern.

**UI pattern in `ShaderPopupHelper`:**
- Pack checkbox is tri-state (`IsThreeState = true`) when files are available: Checked = all files selected, Indeterminate = some excluded, Unchecked = pack off.
- Expand button (`▶`/`▼`) only shown when pack is cached (`IsPackCached`) and checked/indeterminate. Collapsed sub-panel (`Visibility.Collapsed`) of file checkboxes appears below the pack row — each added to `panel` exactly once (WinUI single-parent rule).
- All programmatic `IsChecked` mutations inside pack `Checked`/`Unchecked` handlers guarded by `bool packCbInitializing = false` flag to prevent re-entrancy.
- `BuildIncludeMap()` called once at start of `ShowAsync`; used by `AutoSelectDependencies()` to auto-tick `.fxh` header dependencies and their owning packs when a file is checked.
- On confirm: calls `SetExcludedFiles(packId, excludedFiles)` for each selected pack; clears exclusions for packs with no file-level changes.

**Expand button only when cached:** If a pack isn't yet downloaded, `IsPackCached` returns false and no expand button is shown — the file list isn't available until staging exists. First open after downloading will show the files.

**`ClearIncludeCache()` must be called after pack extraction** — `EnsurePackAsync` calls it at the end of a successful extraction so the map is rebuilt with the new files on next `BuildIncludeMap()` call.

**Cross-pack `.fxh` dependency status (verified Aug 2026)**: Running `check_missing_includes.py` against the full staged set shows only 3 genuinely missing includes — `multibounce.fxh`, `settings.fxh`, `stbn.fxh` — all required by `BFBFX/bfb_inc/VB.fxh` and absent from all packs entirely (not cross-pack, just missing upstream). All other `#include` deps in the current pack set resolve within the same pack. The `uncachedPackOwnership` fallback in `AutoSelectDependencies` is correct future-proofing but exercises no real cases today.

**`DeployAllShaders` must build and pass the exclusions dict** — the deploy loop in `MainViewModel.DeployAllShaders` calls `SyncGameFolder` for every game. It must build the exclusions dict from `GetExcludedFiles` for each selected pack ID before calling `SyncGameFolder`, identical to the pattern in `DeployShadersForCard`. Missing this causes per-file exclusion choices to be silently ignored on global deploy.

**Tri-state checkbox confirm loop: check `== false`, not `!= true`** — when a pack has some files excluded, its pack-level checkbox is indeterminate (`IsChecked == null`). The confirm loop must treat `null` as selected (pack is included, with exclusions). Use `if (box.IsChecked == false) continue;` — NOT `if (box.IsChecked != true) continue;`. The `!= true` form drops indeterminate packs from `SelectedShaderPacks` entirely, causing them to disappear from the saved selection and losing the file-level exclusions on next open. This pattern applies to any tri-state checkbox confirm loop.

**`_settingsLock` SemaphoreSlim serializes all `settings.json` access in `ShaderPackService` — but every read must use an in-memory cache**: Adding a `SemaphoreSlim(1,1)` lock to prevent concurrent writes is correct, but if every read also re-parses `settings.json` from disk under the lock, 50 parallel pack checks at startup produce 100+ serialized disk reads = 4s startup freeze. Fix: add `private static Dictionary<string, string>? _settingsCache`. `ReadSettings()` loads from disk once and serves subsequent reads from memory. `WriteSettings(d)` writes to disk AND sets `_settingsCache = d`. The lock still serializes access but now guards a dict lookup (microseconds) instead of a disk read (milliseconds). Rule: **any `SemaphoreSlim`-protected resource accessed frequently in parallel must be backed by an in-memory cache — never re-read from disk on every acquisition.**

**`RecheckSkipList` causes ~4s startup freeze due to thread pool starvation**: `RecheckSkipList` iterates 106 games with simple dict lookups — trivially fast in isolation. But it runs on the background scan thread at startup while 50 parallel shader pack `Task.Run` jobs are competing for thread pool threads. The background scan thread gets starved and its 106-iteration loop takes 4 seconds. Fix: `RunBackgroundScanAndMergeAsync` accepts `bool isStartup = false`; startup calls pass `isStartup: true` which skips `RecheckSkipList`. Manual refresh (via `RefreshAsync`/`FullRefreshAsync`) passes default `false` and gets the recheck. Rule: **any synchronous loop added to `RunBackgroundScanAndMergeAsync` that wasn't there in a stable release must be tested for thread-pool starvation effects at startup — the method runs while ~50 parallel shader/addon pack tasks saturate the thread pool.**

**`profileLoading` guard for programmatic checkbox state during profile load**: When loading a profile into the dialog, all pack and file checkboxes are set programmatically. This fires `Checked`/`Unchecked` handlers which can cascade (auto-select deps, re-check files, etc.). Prevent this with a top-level `bool profileLoading = false` variable in `ShowAsync`, checked at the top of every handler. Set `profileLoading = true` before applying the profile, `false` in a `finally` block after. This is distinct from the per-pack `packCbInitializing` flag which only prevents tri-state re-entrancy within a single pack.

**Shader profile storage**: `%LocalAppData%\RHI\shader_profiles.json` — `List<ShaderProfile>` serialized via `ShaderProfileService` (static Load/Save, same pattern as `OsPresetService`). `ShaderProfile` model: `Name`, `SelectedPacks` (List<string>), `FileExclusions` (Dictionary<string, List<string>> — packId → excluded filenames). Profiles are global-only; per-game context shows a read-only load list.

**Export zip path for shader files**: `ShaderProfileService.BuildExportZip` must walk `ShaderPackService.ShadersDir\{packId}\` directly (same tree used by deploy), NOT reconstruct paths from `GetPackShaderFiles` leaf names. `GetPackShaderFiles` returns leaf filenames only (e.g. `"Bloom.fx"`), but staged files live at `ShadersDir\{packId}\subfolder\Bloom.fx`. Using `Path.Combine(ShadersDir, leafName)` always produces a missing-file path. The correct pattern: `Directory.EnumerateFiles(Path.Combine(ShadersDir, packId), "*", AllDirectories)`, then `Path.GetRelativePath(packShadersDir, srcPath)` to preserve subfolder structure in the output. Include ReShade framework headers (`*.fxh` at `ShadersDir` root) separately since they live outside any pack subfolder.

## Zip-to-Clipboard Pattern

Used for sharing files directly into Discord (e.g. logs archive, future shader profile export). The pattern:

```csharp
// 1. Build the zip into a temp file
var archivePath = Path.Combine(Path.GetTempPath(), $"RHI_Something_{timestamp}.zip");
if (File.Exists(archivePath)) File.Delete(archivePath);
System.IO.Compression.ZipFile.CreateFromDirectory(sourceDir, archivePath);
// For selective files (not a whole dir), create a temp dir, copy files there, then ZipFile.CreateFromDirectory

// 2. Put it on the clipboard as a StorageFile
var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(archivePath);
var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
dp.SetStorageItems(new[] { storageFile });
Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
```

User can then paste directly into Discord. Confirmation dialog should say "Paste directly into Discord to share." Reference implementation: `SettingsHandler.CopyLogsArchive_Click`.

- **`SetOsDllOverride("")` creates a ghost `_dllOverrides` entry**: Calling `SetOsDllOverride(gameName, "")` to clear the OS DLL name on OptiScaler uninstall used to create a `DllOverrideConfig` entry with all fields empty. `HasDllOverride` only checked key existence, so it returned `true` for any game that ever had OptiScaler installed+uninstalled. This caused: (1) DLL naming override toggle showing ON on every panel rebuild for those games (triggered by anything that calls `BuildOverridesPanel`, including DLSS preset changes); (2) ReShade installing as `ReShade64.dll` on games like Halo CE even after OptiScaler removal. Fixed: `HasDllOverride` now checks that at least one field is non-empty; `SetOsDllOverride("")` removes the entry entirely when all fields become empty. Rule: any `Set*Override(name, "")` call that is meant to clear a setting must either delete the entry or guard against creating ghost entries.

- **`HasDllOverride` must check field contents, not just key existence**: `_dllOverrides.ContainsKey(gameName)` is an unreliable proxy for "user has configured a DLL override" because entries can exist with all-empty fields (ghost entries from partial clears). Always check that at least one meaningful field is populated. The same principle applies to any `Dictionary<string, SomeConfig>` where partial clears can leave empty-but-present entries.

- **`TextWrapping` on a `TextBlock` inside `CheckBox.Content` requires a fixed `Width`**: Setting `TextWrapping = TextWrapping.Wrap` with `MaxWidth` or `HorizontalAlignment = Stretch` on a `TextBlock` inside a `StackPanel` inside `CheckBox.Content` does not wrap — WinUI measures `CheckBox.Content` without a width constraint, so the `TextBlock` always sees infinite available width regardless of parent `MaxWidth`. The only reliable fix is to set a fixed `Width` on the `TextBlock` itself (e.g. `Width = 460`). `MaxWidth` and `HorizontalAlignment.Stretch` both fail in this context.

## ReShade INI Keys

- **`VariableListUseTabs` (`[OVERLAY]` section)**: Controls whether the ReShade overlay displays shaders grouped into tabs (`1`) or as a collapsible tree (`0`). The ReShade UI option is labeled "Group effect files with tabs instead of a tree". Added to `ReShade.ini` template default as `1`. Applied via `AuxInstallService.ApplyVariableListUseTabs(iniFilePath, bool)` which writes to `[OVERLAY]`. Surfaced in Settings → Screenshots & Hotkeys as "Effect list style" combo. Applied to all games via the "Apply to All Games" button in that section alongside hotkeys and screenshot path.

- **`ReShade.ini` template lives in the repo at `g:\RDXC\RenoDXCommander\ReShade.ini`** — built as `<Content>` in the csproj, published to `AppContext.BaseDirectory`, seeded to `%LocalAppData%\RHI\inis\reshade.ini` by `EnsureInisDir()` on first launch (never overwrites user edits). `MergeRsIni` always merges from the user's `%LocalAppData%\RHI\inis\reshade.ini` — editing the repo template and publishing is how you change the default for new installs. Existing users only get template changes applied when `MergeRsIni` runs (install/update) or when they click Apply to All Games in settings.

## Log Access Convention

When the user asks to check logs (e.g. "check my latest log", "check all logs", "look at the log"), always copy the relevant log files from `%LocalAppData%\RHI\Logs\` to `g:\RDXC\Logs\` before reading them. This makes logs visible in the workspace file tree.

```powershell
# Copy all logs from AppData to workspace Logs folder
Copy-Item "$env:LOCALAPPDATA\RHI\Logs\*" "g:\RDXC\Logs\" -Force
```

After copying, read from `g:\RDXC\Logs\`. For "latest log" requests, sort by LastWriteTime descending and take the first file.

- **`RsIniLockedGames` — per-game reshade.ini update lock**: Composite-keyed HashSet (`"GameName|Store"`). When a game is in this set, RHI skips all automatic reshade.ini merges for it — `InstallReShadeAsync` (`mergeIni` param), `UpdateAllReShadeAsync` (`keepRsIniUpdatedResolver` param), and `ApplyScreenshotPath_Click` AND `ApplyPeakNitsToAll_Click`. Exposed via `GetKeepRsIniUpdated` / `SetKeepRsIniUpdated` on `MainViewModel.Settings`. Persisted as `"RsIniLockedGames"` in settings.json. Default = Yes (absent = keep updated). UI: "Keep ReShade.ini Updated" Yes/No combo at the bottom of the per-game ReShade Settings cog (`RsCogButton_Click` in `MainWindow.Events.Components.cs`). The guard is passed as a `bool mergeIni` parameter to `InstallReShadeAsync` and as `Func<string, string, bool>? keepRsIniUpdatedResolver` to `UpdateAllReShadeAsync` — services don't access `IGameNameService` directly, so the bool/func is resolved by the caller and passed in. **Common mistake**: when adding a new "Apply to All" loop that writes to reshade.ini, always add `if (!ViewModel.GetKeepRsIniUpdated(card.GameName, card.Source ?? "")) continue;` — the Peak Nits apply handler was initially missed.

**Shader profile import/export**: Export zip contains shader files + `shader_import.json` (serialized `ShaderProfile`). `BuildExportZip` appends the JSON to the zip after `ZipFile.CreateFromDirectory` using `ZipFile.Open(zipPath, ZipArchiveMode.Update)`. `ImportFromZip` reads `shader_import.json`, extracts uncached pack files from `Shaders/{packId}/` and `Textures/{packId}/` subtrees, calls `RecordExtractedFilesFromDir(packId)` to register files in settings.json, clears the include cache. `RecordExtractedFilesFromDir` walks the pack's staging subfolder and writes the file list under the existing `_settingsLock`/`ReadSettings`/`WriteSettings` pattern — same as `RecordExtractedFiles` but from disk rather than an archive. The Win32 `GetOpenFileName` file picker (zip filter) is used for import — same pattern as the exe picker in `DetailPanelBuilder.Overrides.ShadersAddons.cs`. **hwnd in static helper classes**: Do NOT use `App.Services.GetRequiredService<MainWindow>()` to get the hwnd — this activates a new MainWindow instance. In static helpers where `_window` is unavailable, resolve the hwnd from the `xamlRoot` parameter: `Microsoft.UI.Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId)`.

- **`RefreshAsync()` uses `InitializeAsync(forceRescan: true)` — bypasses `RunBackgroundScanAndMergeAsync` entirely**: Standard Refresh and Full Refresh both call `InitializeAsync(forceRescan: true)`. `RecheckSkipList` only lives in `RunBackgroundScanAndMergeAsync` (which only runs on startup's background scan). This means any logic placed exclusively in `RunBackgroundScanAndMergeAsync` will NOT run on a standard Refresh. If you need something to run on Refresh, it must be in `InitializeAsync` or called directly from `RefreshAsync`. **Symptom**: `RecheckSkipList` never fired on Refresh — confirmed via log showing `forceRescan=True` with no `RecheckSkipList` entry.

- **DLSS skip cache not cleared on OptiScaler install — fix pattern**: When OptiScaler installs `nvngx_dlss.dll` and/or Streamline to a game that was previously skip-listed (count=3), the next `BuildCards` would skip the DLSS scan for that game because `ShouldSkipScan` still returned true. Fix: call `_dlssStreamlineService.RecordDlssFound(card.GameName)` immediately after a successful OptiScaler install in `InstallEventHandler`. This removes the game from the skip cache on the spot. General rule: any component install that deploys DLSS or Streamline files to a game folder should call `RecordDlssFound` post-install so the skip cache doesn't prevent detection on the next scan.

- **`FindRecord()` returns a different object from `card.RsRecord` — stale in-memory record causes DLL rename corruption**: `OptiScalerService.InstallAsync` calls `_auxInstaller.FindRecord(...)` to get the RS record, updates `rsRecord.InstalledAs`, and saves to disk. But `rsRecord` is a fresh object from disk — NOT the same reference as `card.RsRecord`. So `card.RsRecord.InstalledAs` stays stale at the old value (e.g. `"dxgi.dll"`) even though the file was physically renamed to `"ReShade64.dll"` and the disk record was updated. When `EnableDllOverride` later runs (e.g. user enables DLL naming override), it reads `card.RsRecord.InstalledAs = "dxgi.dll"` (stale), thinks ReShade is at `dxgi.dll` (actually OptiScaler), **deletes `ReShade64.dll`** (actual ReShade), and **renames `dxgi.dll` (OptiScaler) to `ReShade64.dll`**. Fix: after updating `rsRecord.InstalledAs`, also set `card.RsRecord.InstalledAs = ReShadeCoexistName` if `card.RsRecord != null`. Pattern: whenever a service updates a tracking record via `FindRecord()`, it must ALSO update the corresponding `card.*Record.InstalledAs` field if the card's record is a different object. The uninstall path already did this correctly; the install path was missing it.

- **DLL naming override toggle-OFF order matters — OS must revert before RS**: `DisableDllOverride` reverts RS to `dxgi.dll` (default). If OptiScaler is still on `dxgi.dll` at that point, `dxgi.dll` is occupied and RS falls back to `ReShade64.dll` with `rsReverted=false`. Fix: revert OptiScaler to its default name FIRST (before calling `DisableDllOverride`), so `dxgi.dll` is free when RS tries to reclaim it. The toggle-OFF handler in `DetailPanelBuilder.Overrides.cs` now does OS revert in Step 1, then calls `DisableDllOverride` in Step 2. Also: use `_dllOverrideService.GetDllOverride(capturedName)?.OsFileName` at toggle-off time (live lookup), NOT the stale `existingCfg?.OsFileName` captured at panel build time — the user may have changed the OS combo after the panel was built.

- **RS dropdown should NOT exclude the OS effective name**: The original `UpdateRsDropdownItems` excluded `GetEffectiveOsName(gameName)` from the RS dropdown to prevent collision. This caused `dxgi.dll` to disappear from RS options whenever OS was using it, forcing a refresh to get it back. The correct approach: show all names in the RS dropdown, but block the *save* in `rsNameBox.SelectionChanged` if the selected RS name collides with the installed OS name (`card.OsInstalledFile`). The existing `UpdateDllOverrideNames` collision guard in `DllOverrideService` also enforces this at the service level.

- **Toggle-OFF DLL revert must handle RS-at-default blocking OS revert**: When the user sets RS to `dxgi.dll` (the default) while override is ON, and OS is at `d3d12.dll`, turning the toggle OFF produces this state: OS wants to move `d3d12.dll` → `dxgi.dll` but `dxgi.dll` is occupied by RS. `DisableDllOverride` sees RS `cfg.ReShadeFileName = "dxgi.dll"` == default, treats it as "already at default, nothing to do". Result: RS stays at `dxgi.dll`, OS stays at `d3d12.dll`. Fix: in the OS revert block, if `dxgi.dll` is occupied by RS (detected via `targetCard.RsRecord.InstalledAs`), first move RS to `ReShadeCoexistName` (`ReShade64.dll`) to free the slot, then move OS to `dxgi.dll`. This leaves both at their correct coexistence state (OptiScaler = `dxgi.dll`, ReShade = `ReShade64.dll`).

## PCGamingWiki (PCGW) Integration

- **`appid.php` endpoint broke in August 2026 server migration**: `https://www.pcgamingwiki.com/api/appid.php?appid={id}` was the primary URL resolution method (Steam AppID → wiki page redirect). After their August 17, 2026 Canasta Docker migration it returns 404/500 errors. PCGW state on their API page: "The GOG/Steam redirect endpoints are currently non-functioning pending an available resource." It may be restored in future — do not remove it from the code.

- **Current resolution method: OpenSearch API**: `https://www.pcgamingwiki.com/w/api.php?action=opensearch&search={name}&limit=5&format=json` returns wiki page URLs directly. Works without authentication. Rate limited to 60 req/min. Results are cached to `%LocalAppData%\RHI\pcgw_url_cache.json` (ConcurrentDictionary, debounced write) so OpenSearch only fires once per game lifetime.

- **Manifest-driven switch**: `RemoteManifest.PcgwUseAppId` (JSON: `"pcgwUseAppId"`) — when `true`, RHI uses `appid.php` instead of OpenSearch. `RemoteManifest.PcgwUrlCacheVersion` (JSON: `"pcgwUrlCacheVersion"`) — integer, bumping forces all clients to wipe `pcgw_url_cache.json` on next launch. To restore `appid.php` when PCGW fixes it: set `pcgwUseAppId: true` and bump `pcgwUrlCacheVersion` by 1 in `manifest.json` — no app build needed. This is handled in `PcgwService.CheckManifestCacheVersion()` called after each manifest fetch.

- **URL cache versioning**: `%LocalAppData%\RHI\pcgw_cache_v2.txt` stores the current version number. On launch, if `manifest.PcgwUrlCacheVersion > local version`, the URL cache is wiped and the marker updated. `PcgwService.LoadCacheAsync` also has a hardcoded `const int UrlCacheVersion = 1` for the initial 2.4.2 migration — this only fires once if the marker file doesn't exist yet.

- **`cargoquery` requires authentication after August 2026 migration**: The Cargo API (`action=cargoquery`) now returns `permissiondenied` for anonymous requests. Do not use it for PCGW lookups without a registered bot password. OpenSearch (`action=opensearch`) remains freely accessible.

- **OpenSearch fallback pitfall — `_pcgwDown` flag too aggressive**: The original code set `_pcgwDown = true` on any non-200 HTTP response, silently killing all subsequent PCGW lookups for the session. This was triggered by the first game in a parallel `BuildCards` run failing, causing 100+ games to get no PCGW link. Fix: non-200 responses are now logged and return null per-call without setting `_pcgwDown`. The flag is only set on genuine connection exceptions.

- **Parallel BuildCards race condition on `_urlCache`**: `BuildCards` runs as `Parallel.ForEach`. Multiple threads writing to `_urlCache` simultaneously caused "Collection was modified; enumeration operation may not execute" during serialization. Fix: `_urlCache` is a `ConcurrentDictionary`. The disk write is debounced (500ms timer) and takes a snapshot copy before serialization to prevent mid-write enumeration.

- **`pcgwUrlOverrides` manifest dict for problem games**: Some games have folder names that don't match their wiki title (e.g. `"DARK SOULS™ II: Scholar of the First Sin"` vs the wiki page name). These must be added to `pcgwUrlOverrides` in the manifest as `"DetectedGameName": "https://www.pcgamingwiki.com/wiki/..."`. The detected name (from Steam folder/registry) is the key — always check `game_library.json` to find the exact detected name before adding an entry.

## DLSS Neural Rendering (nvngx_dlssnr.dll)

- **DLL filename**: `nvngx_dlssnr.dll`
- **Staging dir**: `%LocalAppData%\RHI\DLSS-NR\`
- **Manifest**: `dlssnr` array in `dlss_manifest.json` (same structure as `dlss`, `dlssd`, `dlssg`)
- **Dev presets A–O**: in `manifest.json` under `dlssPresetsDev.nr` (values 1–15). Non-dev presets go under `dlssPresets.nr` when ready.
- **NVIDIA driver profile setting IDs** (confirmed via NVPI CustomSettingNames.xml):
  - Preset: `0x10E41DF8`
  - Latest DLL: `0x10E41E04`
  - Latest Streamline DLL: `0x10E41E05`
- **Dev-gating**: The UI is gated behind `DevUnlockService.IsUnlocked`. The underlying plumbing (detection, swap, cache, card state, settings persistence) is ungated — it works for everyone and produces `null` when no `nvngx_dlssnr.dll` is found.
- **To ungate for public release**: Remove the `if (DevUnlockService.IsUnlocked)` guard in exactly two places:
  1. `DetailPanelBuilder.Overrides.NvidiaProfile.cs` — NR column insertion block
  2. `DlssDefaultsDialog.cs` — NR version + preset section
- **NR column position**: Between FG (col 4) and Streamline (col 8) when unlocked — 9-column grid. When locked, standard 7-column grid (SL stays at col 6).
- **Preset array**: `DlssPresetService.NrPresets` — Default + NVIDIA Recommended for non-dev; A–O injected via `ApplyManifestPresets` from `dlssPresetsDev.nr` when unlocked.

## Manifest-Driven Feature Flags

**`featureFlags` in `manifest.json`** — remotely enables dev-gated features for all users without an app update. When a flag is `true`, the feature is visible to everyone. When `false` or absent, the feature is only visible to users with `unlock.txt` (dev preview mode).

```json
"featureFlags": {
  "dlssNr": false,
  "nexusMods": false,
  "resolutionControl": false
}
```

**To release a feature to all users:** Set the flag to `true` in `manifest.json` and push. Users see the feature after the next manifest refresh (app restart or standard Refresh). No app build needed.

**Implementation:**
- `Services/FeatureFlags.cs` — static class with `DlssNr`, `NexusMods`, `ResolutionControl` bool properties
- Each property returns `DevUnlockService.IsUnlocked || _manifestFlags?.FlagName == true`
- `FeatureFlags.ApplyManifest(manifest?.FeatureFlags)` called in both `MainViewModel.Init.cs` and `MainViewModel.BackgroundScan.cs` after `DlssPresetService.ApplyManifestPresets`
- `Models/RemoteManifest.cs` — `ManifestFeatureFlags` class + `FeatureFlags` property on `RemoteManifest`
- Manifest editor's `RemoteManifest.cs` also has `ManifestFeatureFlags` so the editor preserves the flags on save

**What stays as `DevUnlockService.IsUnlocked` (intentional):**
- `DlssPresetService.ApplyManifestPresets` — gates dev preset *content* (SR/RR/FG dev presets from `dlssPresetsDev`), not a feature visibility gate
- `DlssStreamlineService.BuildVersionList` — gates dev *version entries* (from `DlssDev`, `DlssdDev`, etc.), not a feature visibility gate

**Adding a new feature flag:**
1. Add `bool? MyFeature { get; set; }` with `[JsonPropertyName("myFeature")]` to `ManifestFeatureFlags` in both `RemoteManifest.cs` files (app + editor)
2. Add `public static bool MyFeature => DevUnlockService.IsUnlocked || _manifestFlags?.MyFeature == true;` to `FeatureFlags.cs`
3. Replace all `DevUnlockService.IsUnlocked` gates for that feature with `FeatureFlags.MyFeature`
4. Add `"myFeature": false` to `featureFlags` in `manifest.json`

## OptiScaler Backup Rule (Updated)

- **`BackupOriginalIfExists` must be called for ALL root files in `InstallAsync`**: Any file deployed to the game root (main DLL, companion DLLs like `amd_fidelityfx_*.dll`, boost DLLs etc.) must be backed up before overwriting. Games like Stalker 2 and The First Berserker ship companion DLLs that OptiScaler replaces — without backup they are permanently deleted on uninstall. Fix: `BackupOriginalIfExists(destPath)` is called before every `File.Copy` in the root staging file loop. Subdirectory files (`D3D12_OptiScaler/`, `OptiScaler/Streamline/`) are exempt. `UpdateAsync` is also exempt — those files belong to a previous OptiScaler install, not the game.

## RenoDX Nexus Mods Integration

- **NXM endpoint**: `/v1/games/{domain}/mods/{modId}/files/{fileId}/download_link` (singular, NO `.json` suffix). Query params: `key` and `expires` only — do NOT include `user_id` (causes 404).
- **Free user NXM flow**: Browser fires `nxm://domain/mods/id/files/fileId?key=X&expires=Y&user_id=Z` → RHI's registered protocol handler forwards via single-instance pipe as `"nxm:{url}"` → `HandleNxmUrl` strips the `"nxm:"` prefix → `NxmProtocolHandler.Parse()` → `GetDownloadUriWithNxmKeyAsync` → download → extract → deploy.
- **Premium user flow**: `ExternalLink_Click` intercepts when `FeatureFlags.NexusMods && card.NexusUrl != null && nexusDl.IsApiKeyConfigured && nexusDl.IsPremium` → `InstallNexusModAsync` → `GetLatestMainFileAsync` → `GetDownloadUriAsync` (CDN, no key needed).
- **Opening Nexus in browser**: When not premium, `ExternalLink_Click` calls `GetLatestMainFileAsync` to resolve the latest file ID and opens `nexusmods.com/{domain}/mods/{id}?tab=files&file_id={fileId}&nmm=1` directly to the download page.
- **NXM double-prefix bug**: The pipe forwards `"nxm:nxm://..."` — `HandleNxmUrl` must strip `"nxm:"` prefix before parsing: `if (nxmUrl.StartsWith("nxm:nxm://")) nxmUrl = nxmUrl.Substring(4)`.
- **Registration**: `NxmProtocolHandler.RegisterProtocolHandler()` writes to `HKCU\Software\Classes\nxm\shell\open\command`. `AnyHandlerRegistered()` checks before overwriting — warns user if Vortex/MO2 already registered.
- **Gating**: All Nexus UI and logic gated via `FeatureFlags.NexusMods`. The `NexusDownloadService` itself has no gate — callers check the flag upstream.

## `blacklistPrefixes` Manifest Field

- **Purpose**: Blocks detected game entries whose name starts with a given prefix, without needing individual `blacklist` entries. Useful for `"Battle.net."` which generates entries like `Battle.net.12345` that change with every launcher update.
- **JSON key**: `"blacklistPrefixes"` — a `List<string>` in `RemoteManifest`.
- **Check**: Applied in `MainViewModel.Init.cs` and `MainViewModel.BackgroundScan.cs` alongside the exact-match `_manifestBlacklist` check. Uses `g.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)`.
- **Current entries**: `"Battle.net."` — covers all Battle.net launcher pseudo-games.
- **Manifest editor**: Editable in Detection page under "blacklistPrefixes" expander.

## DLSS Trusted Cache Version Bump

- **`CurrentTrustedCacheVersion`** in `DlssStreamlineService.cs` — increment this when new DLL types are added to detection (e.g. when `DlssnrPath` was added). On the next Full Refresh, all existing trusted cache entries with `CacheVersion < CurrentTrustedCacheVersion` are invalidated and rescanned. Current value: `1` (bumped for `DlssnrPath` addition).
- **Pattern**: Adding a new DLSS DLL type → bump `CurrentTrustedCacheVersion` by 1 → next Full Refresh forces rescan of all games that had an old entry.

## Streamline Version Detection — Higher-Version DLL Override

- **Problem**: Some Streamline releases bundle an older `sl.interposer.dll` alongside newer DLLs (e.g. 2.12.129 ships with a 2.12.128 interposer). RHI was showing 2.12.128 as the installed version.
- **Fix**: After reading the version from `sl.interposer.dll`, `DlssStreamlineService.Detection.cs` and `GameCardViewModel.RefreshDlssVersions` both compare against all other `KnownStreamlineDlls` in the folder and use the highest version found. `IsHigherVersion(string?, string?)` helper in `DlssStreamlineService.cs` does the comparison via `System.Version.TryParse`.

## DLSS Defaults Dialog — Custom Version Option

- `BuildCombo()` in `DlssDefaultsDialog.cs` now appends `"Custom"` to the version list (same as the per-game panel).
- Quick Apply handler routes `"Custom"` to `SwapDlssCustomAsync` / `SwapStreamlineCustomAsync` instead of the versioned swap methods.

## `.original` Sentinel Pattern for Deploy/Cleanup

When RHI deploys a file to a game folder, it needs to distinguish three cases on uninstall:
1. The game already had this file — restore the original
2. RHI placed this file and the game had nothing — delete it entirely
3. RHI has no record of placing this file — leave it untouched

**The sentinel solves case 2.** A 0-byte `.original` file is written alongside the deployed file when the dest did NOT exist before deploy. This marks "RHI owns this file entirely."

**Deploy contract (per file):**
- Dest exists, no `.original` → `File.Copy(dest, dest + ".original")` then overwrite dest (preserves game original)
- Dest exists, `.original` exists → just overwrite dest (backup already captured, idempotent)
- Dest does NOT exist → copy new file to dest, write `File.WriteAllBytes(dest + ".original", Array.Empty<byte>())` as sentinel

**Uninstall contract (per file):**
- `.original` is non-zero → `File.Delete(dest)` then `File.Move(dest + ".original", dest)` (restore game original)
- `.original` is 0-byte sentinel → `File.Delete(dest)` and `File.Delete(dest + ".original")` (clean up entirely)
- No `.original` exists → leave dest untouched (RHI has no record of placing it)

**Currently implemented in:** `Renodx5SfAddonService` (planned) for the full DLSS/Streamline co-deploy.

**Should be applied to:** Any future component that deploys files into game folders where the game may or may not already have that file (e.g. DLSS DLLs, Streamline DLLs deployed outside of OptiScaler context).

**NOT needed for:** Files RHI always owns entirely (e.g. `renodx-*.addon64`, `reshade.ini` sections, OptiScaler subfolder files) — those are always safe to delete without backup.

## Log Debugging Protocol

**When the user says "check my log" or "check the newest log", do this — in order:**

1. Copy logs from AppData to workspace: `Copy-Item "$env:LOCALAPPDATA\RHI\Logs\*" "g:\RDXC\Logs\" -Force`
2. Identify the newest session log: `Get-ChildItem "g:\RDXC\Logs\session_*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1`
3. **Search the log for the specific thing the user is complaining about** — use `Select-String` with relevant keywords (component name, error, action) before reading anything else
4. Read the matching lines first. If they contain an obvious error (HTTP 404, exception message, "Failed", "Skipping"), that IS the answer — fix it immediately
5. Only resort to reading the full log or diving into code if the targeted search turns up nothing

**The most common mistake**: assuming a bug is a code logic error and going straight to reading source files, when the log already contains the exact error message. Always check the log evidence first. A 404, a permission error, a missing file — these show up as single lines in the log and require no code analysis to diagnose.

## Neural Rendering Section

- `DetailPanelBuilder.NeuralRendering.cs` — self-contained section between Game Overrides and NVIDIA Profile Overrides. Four methods: DLSS5 Tool, DLSS5 Tool + DX11 Bridge, DLSS Tool (ShortFuse), DLSS5 Feeder.
- **Default method auto-selection**: `is32Bit → Feeder`, `!hasDlss → Feeder`, `isDx11/Vulkan → Bridge`, else `DLSS5 Tool`. Feeder is the default for no-DLSS games (DX11, DX12, Vulkan, OpenGL, 32-bit).
- **Method constants**: `NrMethodDlss5Tool`, `NrMethodDlss5ToolBridge`, `NrMethodShortFuse`, `NrMethodFeeder`
- **Feeder install deploys**: Feeder addon, DLSS5 Tool (neural consumer), `nvngx_dlss.dll` (root), `nvngx_dlssnr.dll`, shaders (`lumenite_Kernel.fx` + `DLSS5_Feed.fx` only — not full packs), `ReShadePreset.ini` with both techniques pre-enabled, `DLSS5_MV_PROVIDER=3` in reshade.ini.
- **ReShadePreset.ini format**: `Techniques=...` and `TechniqueSorting=...` as top-level orphaned lines (before any section header) AND again inside `[GENERAL]` — both required. `TechniqueSorting` includes ALL technique names including disabled debug passes. Written as a literal string, never via ParseIni/WriteIni.
- **Feeder shader deploy**: Uses `DeployToGameFolder` with exclusion dict — only `lumenite_Kernel.fx` and `DLSS5_Feed.fx` are deployed. Sets `PerGameShaderSelection` to `["LumeniteFX", "DLSS5Feeder"]` with mode `"Select"` so `SyncGameFolder` on refresh respects the same exclusions.
- **`renodx-dlss5.addon64` not tracked by AddonPackService**: NR section deploys it with `File.Copy` directly (no `TrackAddonDeployment`). Stale-cleanup guard: if `nvngx_dlssnr.dll` or its sentinel exists alongside the addon, the stale-removal pass skips it.
- **UE-Extended version link**: Clicking the installed version number on a UE-Extended game opens `https://github.com/marat569/renodx/commits/main/src/games/ue-extended`. Implemented in `MainWindow.Events.Install.cs` → `DetailRdxStatus_PointerPressed`.
- **Simple View page layout**: Page 0 = Components, Page 1 = Game Overrides + Neural Rendering, Page 2 = NVIDIA Profile + Management. Controlled in `CompactViewBuilder.ShowPage()`. Window height = 840px (`WindowStateManager._compactSize`).

## AddonPackService — releaseApiUrl Support

- **`releaseApiUrl` field** on `ManifestAddonPack` and `AddonEntry` — GitHub releases API URL (e.g. `https://api.github.com/repos/owner/repo/releases/latest`). When set, RHI fetches the latest release at download time and picks the best asset (`.addon64` > `.addon32` > `.zip`). Version token = release tag. Solves addons with dynamic zip filenames.
- **`ResolveDownloadUrlFromApiAsync`** in `AddonPackService.cs` — fetches GitHub API, returns `(downloadUrl, versionTag)`. Uses `Bearer {token}` if `DevUnlockService.GitHubApiToken` is set.
- **`GetActionType` in `AddonManagerDialog.cs`** must check `!string.IsNullOrEmpty(entry.ReleaseApiUrl)` alongside the static URL fields — otherwise `releaseApiUrl`-only addons are filtered out of the dialog (show as `"repository"` instead of `"download"`).
- **Addon update check timing**: `CheckAndUpdateAllAsync` must run AFTER `ApplyManifestOverrides` in `RunBackgroundScanAndMergeAsync` — otherwise manifest-driven `releaseApiUrl` entries are not in `_packs` yet and updates are silently skipped.
- **GitHub API rate limit**: 60 req/hour unauthenticated. Add `github_api=ghp_yourtoken` to `%LocalAppData%\RHI\unlock.txt` for 5,000 req/hour. Read via `DevUnlockService.GitHubApiToken` at first access.
- **Current `releaseApiUrl` addons**: `dlss5-dx11-bridge` (NIGos/dlss5-bridge), `dlss5-feed` (jlrouzies-fr/DLSS5-Feeder), `mfgunlock` (mavismmg/MFGAdaUnlock-RenoDx).

## DevUnlockService — GitHub API Token

- `DevUnlockService.GitHubApiToken` — reads `github_api=KEY` line from `unlock.txt`. Cached after first read. Used in `AddonPackService.ResolveDownloadUrlFromApiAsync` to authenticate GitHub API calls.
- Pattern: add `github_api=ghp_yourtoken` as a line in `%LocalAppData%\RHI\unlock.txt`.

## DLSS Skip Cache — Normal Refresh Behaviour

- **`RecheckSkipList` now runs on normal Refresh** (`RefreshAsync` in `MainViewModel.Init.cs`) — scans each skip-listed game to detect newly-added DLSS before `InitializeAsync` builds cards. Only updates the skip cache file if a change is found. This means newly installed DLSS (e.g. game update adds frame generation) is detected on normal Refresh without needing a Full Refresh.
- Previously `RecheckSkipList` only ran in `RunBackgroundScanAndMergeAsync` with `!isStartup` guard — which effectively meant it never ran since that path is only used at startup.

## OptiPatcher

- `OptiPatcher.asi` deploys to `<gamedir>/plugins/` alongside OptiScaler. Runtime patcher for OptiScaler compatibility fixes.
- **Now deploys for ALL GPU types** — previously gated to AMD/Intel only (bug). Fixed in `OptiScalerService.Install.cs` step 7.
- Auto-updates silently via rolling release at `https://api.github.com/repos/optiscaler/OptiPatcher/releases/tags/rolling`.
- `EnsureOptiPatcherStagingAsync` also auto-deploys to all games with existing OptiScaler records when a new version is downloaded.

## OptiScaler GPU Type Selection

- Three INI templates: `OptiScaler.nvidia.ini`, `OptiScaler.amd-dlss.ini`, `OptiScaler.amd-nodlss.ini` (+ nightly variants).
- **Only difference between NVIDIA and AMD-DLSS**: `Dxgi=false` (NVIDIA/AMD-DLSS) vs `Dxgi=auto` (AMD-no-DLSS).
- NVIDIA and AMD-with-DLSS-inputs INIs are identical except for that one line.
- `GetUserIniPath(gpuType, dlssInputs, variant)` resolves which file to use. `GetBundledIniPath` for the repo template.

## manifest.json — addonPacks releaseApiUrl

- When adding a new manifest-sourced addon that uses dynamic filenames, use `"releaseApiUrl"` instead of `"downloadUrl"`. This enables proper auto-update with real version tags.
- Addons with only `releaseApiUrl` (no static download URL) must have `releaseApiUrl` checked in `GetActionType()` in `AddonManagerDialog.cs` or they'll be invisible in the UI.
- `ApplyManifestOverrides` validation: entries need at least one of `downloadUrl`, `downloadUrl32`, `downloadUrl64`, or `releaseApiUrl` — otherwise skipped.

## MFG Ada Unlock Addon

- Package name: `"MFG Ada Unlock"`, section ID: `"mfgunlock"`
- GitHub: `https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases`
- Unlocks DLSS Multi Frame Generation (3x/4x+) on RTX 40-series. In-memory only, no files modified on disk.
- Requires ReShade + a game with DLSS FG via Streamline + modern `nvngx_dlssg.dll` (310.x).
- Uses `releaseApiUrl` — auto-updates when new versions release.

- **`REBAR_SIZE_LIMIT_ID` (0x000F00FF) is a QWORD setting (DRS_DWORD64, type 4), NOT Binary (type 1)**: The driver stores this setting as a native 64-bit integer. Writing it as Binary (type 1 with a 4-byte length prefix) produces garbage values like `0x4000000000000008` instead of `0x0000000040000000`. NvAPIWrapper's `SetSetting(uint, byte[])` encodes it as Binary and is therefore wrong for this setting. The correct approach: use `SetQwordSettingRawNvApi` which sets `settingType=4` and writes the 64-bit value directly at struct offset 8220 with no length prefix. Reading: check `settingType == 4` and read 8 bytes from offset 8220 directly (no length prefix at 8216). If using the raw NVAPI struct and you see type 4 but try to read it as type 1 (expecting length at 8216), you get the wrong value. NvAPIWrapper returns `CurrentValue = null` for type 4 (unknown type) — always use the raw NVAPI read path for this setting. The PS helper (`NvAPIWrapper.SetSetting(uint, byte[])` from a runas process) also writes type 1 and is wrong. ONLY `SetQwordSettingRawNvApi` writes it correctly.

- **`installPathOverrides` must be applied in Phase 1 (CacheLoad)**: Any manifest field that affects `card.InstallPath` must be seeded from `cachedManifest` in `LoadCacheAndBuildCardsAsync`, not only in Phase 2. The pattern for bitness, blacklist, and DLSS presets is already there — `installPathOverrides` was added to the same block (after line ~423 in `MainViewModel.CacheLoad.cs`). Rule: if a field affects how files are located on disk and any scan checks those paths, it must be applied during Phase 1 via `cachedManifest`. Hardcoding entries in `_installPathOverrides` (like Cyberpunk 2077) is only for permanent entries that will never be in the manifest; all manifest-driven overrides must come through `cachedManifest` so they work without a Refresh.

- **`DgVoodooService` — dgVoodoo2 deployment for DX9 Luma/Feeder games**: `DgVoodooService.cs` manages dgVoodoo2 deploy/remove for DX9 games. Cached zip at `%LocalAppData%\RHI\dgvoodoo\{version}\`. Deploys only `MS/x86/D3D9.dll` (sentinel-backed) and `dgVoodoo.conf` (always RHI-written). The `lumaRequiresDgVoodoo` manifest list controls which games get it. Both Luma install (`InstallLumaAsync`) and Feeder install (`InstallFeederAddonAsync`) call `DgVoodooService.DeployToGame` independently. **Uninstall conflict guard**: if both Luma and Feeder are installed on the same game, neither uninstall should remove dgVoodoo2. Luma uninstall strips `D3D9.dll`/`dgVoodoo.conf` from `InstalledFiles` before calling `_lumaService.Uninstall` when a Feeder addon is present. Feeder uninstall skips `RemoveFromGame` when `card.LumaStatus == Installed`. Both guards are in `MainViewModel.Install.Luma.cs` (UninstallLuma) and `DetailPanelBuilder.NeuralRendering.cs` (remove + method-switch paths).

- **Feeder 32-bit / DX9 architecture**: For 32-bit DX9 games, DLSS5 Feeder requires a 64-bit helper process. File layout: game folder gets `dlss5-feed.addon32`, `D3D9.dll` (dgVoodoo2), `dgVoodoo.conf`, NR/DLSS runtimes, shaders. The `host64\` subfolder gets: `dlss5-feed-host64.exe`, 64-bit `dxgi.dll` (ReShade), `renodx-dlss5.addon64`, `nvngx_dlssnr.dll`, `nvngx_dlss.dll`. **Critical**: `renodx-dlss5.addon64` must NOT be deployed to the game folder for 32-bit games — the 32-bit ReShade process cannot load 64-bit addons. The neural consumer only lives in `host64\`. The host64 exe is extracted by `AddonPackService.DownloadAndExtractZipAsync` from the Feeder zip's `host64\` path and staged as `{safeName}_host64.exe`. If the host64 exe is missing from staging (e.g. addon was downloaded before the extraction code was added), a re-download is forced in `InstallFeederAddonAsync` by checking `FindStagedAddon(FeederPackageName, ".exe") == null`. The checklist in `DetailPanelBuilder.NeuralRendering.cs` checks `host64\renodx-dlss5.addon64` for DLSS5 Tool status on 32-bit games, and adds a `host64.exe` item for the exe itself.

- **`ShaderPackService.EnsurePackAsync` early-exit bug (fixed v2.7.1)**: `GhRelease` packs had an early-exit block at the top of `EnsurePackAsync` that returned immediately if a stored version existed AND `PackHasExtractedFiles` was true — without ever calling the GitHub API. This meant new releases were never detected for any pack that was already on disk. Fix: removed the early exit entirely. The version comparison at line ~133 (`stored == versionToken`) still correctly skips the download when the remote version matches; the only change is that `ResolveGhRelease` (API call) is now always reached. The ETag cache ensures this is a 304 Not Modified in the common case. **Rule: never add an early exit to `EnsurePackAsync` based on stored version alone — it breaks update detection.**

- **`RenoDXDbService` — RHI-maintained mod database**: `Services/RenoDXDbService.cs` (`IRenoDXDbService`) fetches two JSON files from `https://raw.githubusercontent.com/RankFTW/rhi-repo/main/database/`. Uses `GitHubETagCache` for conditional GETs. Returns stale in-memory cache on any failure. Two files: `RenoDXdb.json` (named mods, lowercase field names: `name`, `author`, `snapshotUrl`, `snapshotUrl32`, `nexusUrl`, `discordUrl`, `discussionUrl`, `notes`, `status`) and `RenoDXdb-unreal.json` (UE-Extended games, PascalCase: `Name`, `Status`, `Method`, `Upgrades`, `Comments`). Registered as `IRenoDXDbService` singleton in `App.xaml.cs`. Dev-only — gated behind `DevUnlockService.IsUnlocked` in init paths.

- **`RenoDXDbUnrealEntry.Method` field encoding**: `"native"` → `Set_Path=0`, no Engine.ini HDR. `"ini"` → `Set_Path=0`, deploy Engine.ini HDR. `"upgrade"` → `Set_Path=1`, apply specific format from `Upgrades` field. `null` → `Set_Path=1`, no Engine.ini, no upgrade. Computed helpers: `UsesSdrPath`, `DeployEngineIniHdr`, `UpgradeIniKey` (parses backtick-quoted format name → `"Upgrade_B8G8R8A8_TYPELESS"`), `UpgradeIniValue` ("Output size"→"1", "Output ratio"→"2", "Any size"→"3").

- **`_nrAddonVersion` per-game setting**: Composite-keyed `Dictionary<string, string>` in `GameNameService`. Persisted as `"NrAddonVersion"` in settings.json (saved only when count > 0). `GetNrAddonVersion`/`SetNrAddonVersion` in `MainViewModel.Settings.cs`. Empty = use latest. Used by `InstallDlss5ToolAsync` and `InstallShortFuseAsync` to stage and deploy a specific versioned `.addon64` from `rdx5\dlss5tool\{ver}\` or `rdx5\dlsstool\{ver}\`.

- **`Renodx5AddonService` versioned staging**: `FetchAndCacheAvailableVersionsAsync()` fetches all releases for both tag prefixes (`renodx-dlss5-`, `renodx-dlss-SF-`), caches to `rdx5\available_versions.json` (1-hour cooldown). `GetAvailableVersions(addonType)` returns version list from in-memory cache. `EnsureVersionStagedAsync(addonType, version)` downloads a specific version to `rdx5\dlss5tool\{ver}\renodx-dlss5.addon64` or `rdx5\dlsstool\{ver}\renodx-dlss.addon64`. `GetVersionedStagedFilePath`/`IsVersionStaged` helpers. The flat `rdx5\renodx-dlss5.addon64` and `rdx5\renodx-dlss.addon64` are **untouched** — still used by everything except the NR section install. Constructor calls `LoadVersionsFromCache()` so lists are populated immediately. `FetchAndCacheAvailableVersionsAsync` is called in `MainViewModel.Init.cs` (fire-and-forget) and after `CheckForUpdateAsync` in `MainViewModel.Update.cs`.

- **NR section Addon Version combo**: Row 1 of the Neural Rendering section has 3 columns — Method / Addon Version / NR DLL Version. Addon Version is disabled and 40% opacity when NR is installed (tooltip: "Uninstall Neural Rendering first to change the addon version."). Disabled for Feeder method (Feeder doesn't use Renodx5AddonService). Repopulates with the correct list when Method changes (`dlss5tool` vs `dlsstool`). SelectionChanged saves via `SetNrAddonVersion` — `null` for Latest, version string for pinned. `InstallShortFuseAsync` now deploys the addon file directly from the versioned or flat staging path, then calls `InstallSfDllsOnlyAsync` (new thin wrapper over `DeploySfDllsAsync`) for DLL co-deploy instead of routing through `InstallSfAsync`.

- **MFG Ada Unlock on-demand download**: The install button in the Extras row was previously disabled when the addon wasn't yet staged (requiring a prior visit to the addon picker). Now it downloads on demand when clicked — shows "Downloading..." then deploys immediately. Same pattern should be applied to any Extras row whose staging depends on `releaseApiUrl`.

- **`RenoDXDbSource` dev setting**: `SettingsViewModel._renoDxDbSource` (default `"WikiOnly"`). Values: `"WikiOnly"` (unchanged behaviour), `"DbOnly"` (db mods only), `"Hybrid"` (db wins on conflict, wiki fills gaps). Persisted as `"RenoDxDbSource"` — omitted when `"WikiOnly"` (default). Changing value triggers immediate `RefreshAsync()` via `PropertyChanged` handler. Dev-only settings card `RenoDxDbSourceCard` in `MainWindow.xaml` (Visibility=Collapsed), shown by `SettingsHandler.InitRenoDxDbSourceCombo()` when `DevUnlockService.IsUnlocked`. `MergeDbSources()` on `MainViewModel` applies the merge after both wiki and db results are fetched.

- **`AttachThreadInput` pattern for reliable foreground activation**: `ForceToForeground(hwnd)` in `NativeInterop.cs` temporarily attaches the calling thread's input queue to the foreground thread, then calls `SetForegroundWindow` + `BringWindowToTop`. This is the only reliable way to bring a window to the foreground without a direct user input event. Used in `BringToFront()` on MainWindow, called from all tray restore paths and `MainWindow_Activated` on fresh launch. New P/Invokes declared in `NativeInterop.cs`: `SwitchToThisWindow`, `AllowSetForegroundWindow`, `GetWindowThreadProcessId`, `GetCurrentThreadId`, `AttachThreadInput`, `GetForegroundWindow`, `BringWindowToTop`.

- **`SetWindowPlacement` for atomic maximized restore**: Use `SetWindowPlacement` instead of `SetWindowPos` + `ShowWindow(SW_MAXIMIZE)` when restoring a maximized window. The two-call approach is vulnerable to a race where the deferred second `TryRestoreWindowBounds` call in `MainWindow_Activated` puts the window into a pseudo-maximized state (borderless but not actually maximized). `SetWindowPlacement` sets the restored rect and show command atomically. P/Invoke declared in `NativeInterop.cs`. Applied in `WindowStateManager.cs`.

- **`_staticDeploymentCache` in `AddonPackService` must be nulled after every write**: A static field caches `addon_deployments.json` content for `IsAddonTrackedInDeployments`. `SaveDeployments()` must set `_staticDeploymentCache = null` after writing — otherwise `AutoRedeployAsync` in `Renodx5AddonService` will always see stale (empty) data and skip all game folders, causing newly installed NR addons to never be auto-redeployed.

- **OptiScaler variant combo init guard**: `variantCombo.SelectionChanged` in `OsCogButton_Click` must be guarded by `bool variantComboInitializing = true`, set to `false` only after `osCogDialog = dialog` (just before `ShowSafeAsync`). WinUI fires `SelectionChanged` during dialog layout before `ShowSafeAsync` — without the guard, every cog open calls `SetOsVariant(null)` ("Stable") and silently wipes the stored DlssNr/Nightly setting, causing installs to use wrong staging. This applies to any combo in a cog dialog that sets `SelectedItem` during construction.

- **PCGW DX9 false-positive correction**: BuildCards and CacheLoad have a third PCGW correction branch: if PE scan returned DX9 but PCGW confirms the game does NOT support DX9 AND confirms DX11/12/Vulkan, trust PCGW over PE scan. Handles NW.js/Electron games (e.g. CrossCode) whose runtimes contain legacy D3D shims that PE scanning incorrectly reads as DX9. This branch runs alongside the existing `pcgwApiOverrides` and `graphicsApiOverrides` checks.

- **Vulkan API transition cleanup in `apiCombo.SelectionChanged`**: When the user changes the Graphics API override for a game, if the transition crosses the DX/Vulkan boundary, the OTHER side's ReShade footprint must be cleaned up. Check AFTER `DetectGraphicsApi` resolves the new API (not from the raw `apiEnumNames` combo value, which misses Auto→Vulkan resolution via manifest). DX→Vulkan: uninstall DX ReShade DLL if installed. Vulkan→DX: uninstall Vulkan ReShade footprint if present. This prevents stale ReShade DLLs being left in the game folder after an API switch. Implemented in `DetailPanelBuilder.Overrides.cs`.

- **`BuildNeuralRenderingSection` and `NeuralRendering.RefreshStatus` semaphore release pattern**: Both methods use `_panelScanSemaphore` for file scan serialization. The semaphore must be released BEFORE any `TryEnqueue` call — never hold it across a `TryEnqueue`. If `BuildNvidiaProfileBody` runs concurrently on the UI thread and calls `BuildDriverProfileSection` (which waits on the semaphore via `Task.Run`), holding the semaphore across `TryEnqueue` causes a dispatcher deadlock. Pattern: scan files, release semaphore, then `TryEnqueue` the UI update. File: `DetailPanelBuilder.NeuralRendering.cs`.

- **`SfZzzMode` per-game setting**: `GameNameService._sfZzzMode` composite-keyed HashSet. Persisted as `"SfZzzMode"`. `GetSfZzzMode`/`SetSfZzzMode` in `MainViewModel.Settings.cs`. When true, `InstallShortFuseAsync` renames `renodx-dlss.addon64` → `zzz_renodx-dlss.addon64` after deploy. `IsSfInstalledIn` checks both names. `AutoRedeployAsync`, `UninstallSf`, `RemoveSfAddonFromFolder` all handle both names. `AddonPackService` stale-cleanup guard covers `SfZzzDeployFileName`. In-place version swap uses whichever name is on disk. Toggle UI on same row as NR Cost Scaler.

- **`ControlUePostInstallService`**: Static class in `Services/`. Triggered after `renodx-control-rr.addon64` is placed (all three paths: normal install, drag-drop, watch folder). Five steps: DLSS upgrade, DLSSD deploy, renderer.ini patch, SR preset clear. On install, DLSS rescan via `dlssSvc.Detect()` → `ApplyDlssDetection` + `RefreshDlssVersions` dispatched to UI thread → `RequestDetailPanelRebuild` + `RequestOverridesPanelRebuild` for immediate panel update. `ShowInstallDialogAsync` shows bespoke confirm dialog before install.

- **UE-Extended cog layout**: `topGrid` is a 5-column grid (left-label | left-combo | 1px-divider | right-label | right-combo). Left side: UE-Extended toggle, nits TextBox (row), Auto button (separate row below, label spans both via RowSpan=2). Right side: Upgrade Path (when Set_Path in INI), Engine.ini HDR (when UseUeExtended and mod installed), LUT Update Every Frame (when Unreal and mod installed). The separate Engine.ini section that was below is removed.

- **Unity cog keys**: `SettingsMode` → "Proxy Revert State" (Off/On); `Proxy_Revert_State` → "Proxy Revert State" (Off/On); `Upgrade_SwapChainCompatibility` → "Swapchain Compat" (Off/On); `Blit_Copy_Hack` → Off/Auto/On/Scaling Only; `Upgrade_CopyDestinations` → Off/On/Auto Upgrade; `Upgrade_UseSCRGB` → "Swapchain Format" HDR10/scRGB; `Tonemap_Offset`/`Scaling_Offset` → 0–5 range.

- **`CheckReShadeUpdateLocal` real fix**: The correct patch is to update `card.RsRecord.Channel` directly (not `auxRecords`) before the update check. `MainViewModel.CheckForUpdatesAsync` iterates `cards`, patches `card.RsRecord.Channel = ResolveReShadeChannel(gameName, store)` when it differs. The service reads `card.RsRecord` directly, not from the auxRecords parameter.

- **`WorkingDirectory` on direct exe launch**: All three direct-exe launch paths (user override, manifest override, auto-detected) in `MainWindow.Events.Install.cs` now set `WorkingDirectory` to the game folder. Protocol launches (Steam, Epic, Xbox) are unaffected.


- **Donate dialog — `authorRoles` manifest field**: `RemoteManifest.AuthorRoles` (JSON: `"authorRoles"`) maps author display name → what they're known for (e.g. `"RenoDX mods, Luma mods"`). `GameCardViewModel.AuthorRoles` static dict is populated via `MergeManifestAuthorData`. `DonateButton_Click` reads live manifest first (`ViewModel.Manifest?.AuthorRoles`), falls back to merged static dict. Always prefer live manifest to avoid timing issues during Phase 1.

- **`_sessionLock.Wait()` must have a timeout**: All `_sessionLock.Wait()` calls in `DlssPresetService` use `Wait(5000)` — a 5-second timeout. If NVAPI hangs (driver bug, COM stall, driver update in progress), the lock would never be released and all subsequent callers block forever. Never add a new `_sessionLock.Wait()` without a timeout.

- **`_panelScanSemaphore` must be released in `finally`**: The `RefreshStatus` Task.Run in `DetailPanelBuilder.NeuralRendering.cs` releases `_panelScanSemaphore` in a `finally` block. If `Directory.GetFiles` or any file check throws before the release, the semaphore leaks permanently — every subsequent `BuildNvidiaProfileSection` and `BuildDriverProfileSection` blocks forever. All file scan results must be pre-declared before the `try` block so they're accessible in the `finally`.

- **`NeuralRendering.RefreshStatusWithData` repeated heartbeat**: If the heartbeat keeps showing `NeuralRendering.RefreshStatusWithData:Building(GameName)` for many cycles while "UI responsive", the NR section for that game is repeatedly rebuilding (RefreshStatus calls BuildNeuralRenderingSectionWithData which calls RefreshStatus again). This itself doesn't freeze but floods the dispatcher queue. When the user navigates away and a new game's `nvidiaGrid.Children.Add` runs, the backlog of queued callbacks may contribute to starvation.

- **`BuildDriverProfileSectionWithData:AddToTree` freeze**: The `targetPanel.Children.Add(nvidiaGrid)` inside the deferred `TryEnqueue(Low)` callback can cause a freeze for games with full DLSS+driver profiles (8-column grid: ReBAR, VSync, Power, Smooth Motion, etc.). This appears as UI responsive ~2s after `AddToTree`, then freezing 5-6s later. Sub-step markers `AddingGrid`/`AddingNotice`/`Done` are now inside the TryEnqueue to pinpoint exactly which add is slow.

- **OptiScaler preset `fgEnabledCombo`/`forceReflexCombo`/`reflexMarkersCombo` scope**: These three combos are declared inside the first `if (isNightly || isDlssNr)` block (lines ~2340–2880). The preset save/apply loop is inside a **second** separate `if (isNightly || isDlssNr)` block (lines ~2885–3195). They must be declared at method scope (nullable, initialized to null) before the first block — not as `var` inside it — so the preset lambda closures can capture them.

- **Path reconciliation subfolder guard**: `BuildCards` path reconciliation must not copy the old addon file when `oldPath` is a parent directory of `newPath` (e.g. old=`\The Witcher 3`, new=`\The Witcher 3\bin\x64_dx12`). This scenario means the new path is just a corrected subfolder, not a genuine game move. Copying would overwrite a newly-installed different addon with the wrong file. Check: `newPath.StartsWith(oldPath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, OrdinalIgnoreCase)`.

- **Addon stale-cleanup must skip Extras-managed files**: `DeployAddonsForGame` stale-cleanup must never remove files managed by the Extras row (e.g. `renodx-mfgunlock.addon64/32`). These are installed directly (not via the global addon pack selection) and tracked in `addon_deployments.json`, but the cleanup would remove them as "not in active selection". Add explicit guards alongside the existing `renodx-dlss5`, SF, and cost-scaler guards.

- **`CheckAndUpdateAllAsync` auto-redeploy after update**: After a successful `DownloadAddonAsync`, iterate `addon_deployments.json` and copy the new staged file to all tracked game folders. Match by `OriginalName64`/`OriginalName32` from versions.json in addition to the sanitized pack name. This ensures addon updates actually reach game folders instead of only updating staging.

- **Available HDR Mods button disabled during scan**: `HdrModsListBtn` starts `IsEnabled=False` in XAML. `MainWindow.UISync.cs` handles `IsBackgroundScanning` property change to enable/disable it. The count varies wildly (819 vs 838 vs 950+) because the four sources (`_allMods`, `_dbUnrealEntries`, `_dbUnityEntries`, `_lumaMods`) populate at different times during Phase 2.

- **`Refresh` vs `Check for Updates` vs `Minimal mode`**: `Refresh` should only update manifests/wiki/PCGW/DLSS manifest/seen mods — it must NOT bypass the Minimal mode check or the 4-hour cooldown. `Check for Updates` button calls `ForceNextUpdateCheck()` which sets `_forceUpdateCheck = true`, bypassing both the cooldown AND Minimal mode. Background scan uses `userInitiated: _forceUpdateCheck`. Never set `_forceUpdateCheck = true` inside `RefreshAsync`.

- **GitHub OAuth token flow**: `GitHubAuthService` implements Device Flow (POST to `github.com/login/device/code`, poll for token). Token stored as `GitHubOAuthToken` in `settings.json`. Applied at startup via `App.ApplyStoredGitHubToken()` which calls `GitHubAuthService.ApplyTokenToHttpClient(http, token)`. On disconnect, falls back to `DevUnlockService.GitHubApiToken` (file-based). The shared `HttpClient` carries the token as a default `Authorization: Bearer` header — strip it for non-GitHub hosts (Codeberg) in `UltrawideFixService.FetchOrLoadCache`.

## Epic Games Detection (classic + new eglv2 launcher)
- Classic launcher: `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON: DisplayName, InstallLocation, CatalogNamespace, AppName).
- New launcher (preview, Oct 2026, `com.epicgames.eglv2`): migrating **deletes the ProgramData manifests and uninstalls the old launcher**. Installs live in `%LOCALAPPDATA%\com.epicgames.eglv2\installations.jsonl`: header line, then an append log of entries (installationId, complete, dir, namespace, itemId, artifactId). Last entry per installationId wins; only `complete: true` counts. **No game names in it.**
- Names come from per-user uninstall keys `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\EpicGames-{artifactId}` (DisplayName, InstallLocation; UninstallString contains `apps/{ns}:{item}:{artifact}`). Migrated games also keep their old .item in `import-scan\v1-items\{installationId}.item`.
- Code: `GameDetectionService.Epic.cs` reads both launchers, de-duplicated by path. `artifactId` == old `AppName` (used for the launch URI).