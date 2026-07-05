# RHI — Post v2.0.0 Technical Roadmap

This document captures structural improvements and refactoring targets for after the v2.0.0 release. These are not feature requests — they're engineering debt items and architectural upgrades that will make the codebase more maintainable, testable, and resilient as the project grows.

---

## Priority 1: Split the Mega-Files

The biggest single improvement with the highest payoff. Several files are 100KB+ and growing with every feature.

### Targets

| File | Size | Split Strategy |
|------|------|---------------|
| `DetailPanelBuilder.Overrides.cs` | 177KB | Extract per-section builders: `BuildDxvkSection()`, `BuildDlssSection()`, `BuildDriverSettingsSection()`, `BuildReBarSection()` into their own partial classes or helper classes |
| `MainViewModel.Init.cs` | 164KB | Extract `BuildCards()` internals into `CardBuildingService`. Move manifest application logic to `ManifestApplicationService`. Split cache vs fresh paths into separate methods |
| `MainViewModel.Install.cs` | 124KB | Extract each component's install/uninstall into dedicated orchestrator methods. Consider an `IComponentInstaller` interface with per-component implementations |
| `DlssPresetService.cs` | 103KB | Split into `DlssPresetService.Read.cs`, `.Write.cs`, `.ProfileMatching.cs`, `.RawNvApi.cs`, `.Export.cs` |
| `MainWindow.Events.cs` | 89KB | Group event handlers by section (Settings page, toolbar, game list, detail panel) into partials |
| `OverridesFlyoutBuilder.cs` | 87KB | Same split strategy as DetailPanelBuilder — extract section builders into partials |

### Approach

- Use partial classes (already the established pattern) — no architectural change required
- Target: no single file over 50KB
- Each partial should have a clear single responsibility described in a summary comment
- Extract shared logic between `DetailPanelBuilder.Overrides` and `OverridesFlyoutBuilder` into a common helper (eliminates the "must update BOTH builders" pitfall)

---

## Priority 2: Unify the Dual UI Builders

**Problem**: `DetailPanelBuilder.Overrides.cs` and `OverridesFlyoutBuilder.cs` implement the same overrides UI twice with slightly different layouts. Every new feature must be added to both. Bugs regularly appear when one is updated but not the other.

### Solution Options

**Option A — Shared Builder Methods (Incremental)**
- Extract a `SharedOverridesBuilder` class with methods like `BuildDxvkCombo(card, handlers)`, `BuildRsChannelCombo(card, handlers)`, etc.
- Both builders call the shared methods, passing in their own layout containers
- Least disruptive, keeps existing architecture

**Option B — Templated UserControls (Proper WinUI)**
- Create `DxvkOverrideControl.xaml`, `RsChannelOverrideControl.xaml`, etc.
- Data-bind to card properties with commands
- Eliminates code-behind UI construction entirely for these sections
- More upfront work, but makes future changes trivial (change one XAML file)

**Recommendation**: Start with Option A to stop the bleed, then migrate individual sections to Option B as time permits.

---

## Priority 3: Formalize the Concurrency Model

**Problem**: Race conditions are patched individually with SemaphoreSlims, lock objects, guard flags, and DispatcherQueue enqueues. The steering documents 6+ distinct races found by users in production.

### Improvements

1. **Introduce a `BackgroundTaskCoordinator`** service that serializes/queues operations that touch the same resources:
   - Game library writes (SaveLibrary, SaveNameMappings)
   - Staging downloads (per-component lock)
   - Card property updates from background threads
   - Panel rebuilds (debounce/coalesce)

2. **Establish a clear threading contract**:
   - Services run on background threads, return results
   - ViewModel mutations happen on the UI thread via a single `DispatcherQueue.TryEnqueue` callsite
   - No `card.Property = x` from background threads — always marshal through the coordinator

3. **Replace individual guard flags with a state machine** for panel rebuild:
   - `comboInitializing`, `_suppressSelectionChanged`, debounce timers → single `PanelState` enum (Building, Interactive, Rebuilding)
   - SelectionChanged handlers check `PanelState == Interactive` instead of individual booleans

4. **Separate shader pack timestamps from settings.json**:
   - `ShaderPackService` reads/writes pack version timestamps to `settings.json` concurrently with other services during startup
   - File contention causes `LoadStoredVersion` to fail → `versionMatch=False` → spurious re-downloads (~5-6 packs per launch, wastes ~500ms)
   - Fix: Move shader pack version tracking to a dedicated `shader_pack_versions.json` file (same pattern as `addon_deployments.json`, `dlss_scan_cache.json`)
   - Alternative: Add a file-level lock around all `settings.json` reads/writes (heavier, affects more code paths)

---

## Priority 4: Reduce ViewModel Surface Area

**Problem**: `MainViewModel` has 40+ injected services and exposes most of them as public properties (`DxvkServiceInstance`, `AddonPackServiceInstance`, etc.) because UI builders need direct service access.

### Improvements

1. **Pass services to builders via constructor/parameter** instead of reaching through the ViewModel:
   ```csharp
   // Instead of: _window.ViewModel.DxvkServiceInstance.Uninstall(card)
   // Use: _dxvkService.Uninstall(card)  (injected into builder)
   ```

2. **Extract command handlers into dedicated classes**:
   - `DxvkCommandHandler` (install, uninstall, update, toggle)
   - `DlssCommandHandler` (swap, restore, quick apply)
   - `ReShadeCommandHandler` (install, uninstall, channel switch)
   - Each handler takes only the services it needs

3. **Reduce forwarding properties**: The 25+ `XxxServiceInstance` properties on MainViewModel only exist for UI builder access. With builders receiving services directly, these can be removed.

---

## Priority 5: Structured Error Handling

**Problem**: Most operations have try/catch with `CrashReporter.Log()` and a status message. There's no structured way to surface errors to the user, retry operations, or distinguish recoverable from fatal errors.

### Improvements

1. **Define a `Result<T>` type** (or use an existing library):
   ```csharp
   public record OperationResult(bool Success, string? Error, Exception? Exception);
   ```

2. **Service methods return results instead of throwing**:
   - Callers decide whether to show a dialog, log, retry, or ignore
   - Eliminates the pattern of catching exceptions just to set `ActionMessage = "❌ ..."`

3. **Centralized error dialog service**:
   - `ErrorDialogService.ShowAsync(result, retryAction?)` 
   - Replaces ad-hoc error handling in each button handler

---

## Priority 6: NVAPI Abstraction Layer

**Problem**: `DlssPresetService.cs` at 103KB mixes profile matching, preset logic, raw NVAPI calls, PowerShell elevation helpers, and setting ID constants. It's also the most fragile code in the project (undocumented APIs, driver-version-dependent behavior).

### Improvements

1. **Extract `NvApiWrapper.cs`** — thin abstraction over raw NVAPI:
   - `ReadSetting(profileHandle, settingId)` → `uint?`
   - `WriteSetting(profileHandle, settingId, value)` → `bool`
   - `WriteBinarySetting(profileHandle, settingId, bytes)` → `bool` (routes through PS helper)
   - `DeleteSetting(profileHandle, settingId)` → `bool`
   - Encapsulates the "try NvAPIWrapper, fall back to raw API, fall back to PS helper" chain

2. **Extract `NvidiaProfileMatcher.cs`** — the 5-pass matching logic with caching

3. **Extract setting ID constants** into a static class with documentation comments linking to NVPI XML

4. **Version-gate features**: Check driver version before enabling settings (MFG needs 572.16+, render scale needs 565+, etc.) — currently the UI just silently fails on older drivers

---

## Priority 7: Data-Driven Component System

**Problem**: Adding a new component (e.g. DXVK, OptiScaler, Display Commander) requires touching 10+ files per the steering doc's "Per-Game Overrides Pattern". Each component has its own install/uninstall/update/detect methods scattered across services, viewmodels, and UI builders.

### Long-term Vision

Define components declaratively:
```csharp
public interface IGameComponent
{
    string Id { get; }
    string DisplayName { get; }
    GameStatus Detect(string installPath);
    Task InstallAsync(GameCardViewModel card, IProgress<...>? progress);
    void Uninstall(GameCardViewModel card);
    Task<bool> CheckForUpdateAsync(GameCardViewModel card);
    Task UpdateAsync(GameCardViewModel card);
}
```

Each component registers itself in DI. The detail panel iterates registered components and builds rows dynamically. Adding a new component = one new class + registration line.

**This is a major refactor** — not recommended until after v2.0.0 stabilizes. But it would eliminate the "add to 11 files" pattern entirely.

---

## Priority 8: Test Infrastructure

**Problem**: 229 property-based tests exist but the test project has pre-existing build errors. Test stubs must be manually updated when interface signatures change.

### Improvements

1. **Fix the test project build** — resolve `IGameLibraryService` and `IUltrawideFixService` stub issues
2. **Auto-generate test stubs** from interfaces using a source generator or T4 template
3. **Add integration-level tests** for critical flows:
   - Install → detect → uninstall roundtrip (on a temp folder)
   - Manifest parse → card build → property assertions
   - Settings save → load roundtrip
4. **CI pipeline**: Even a simple `dotnet build && dotnet test` on push would catch regressions before beta testers hit them

---

## Priority 9: Configuration/Settings Modernization

**Problem**: `settings.json` is a flat `Dictionary<string, string>` serialized manually in `GameNameService.SaveNameMappings()`. Every new per-game setting requires touching Load, Save, Rename, Reset, and the interface.

### Improvements

1. **Structured settings model**:
   ```csharp
   public class PerGameSettings
   {
       public string? ReShadeChannel { get; set; }
       public string? DxvkVariant { get; set; }
       public string? ShaderMode { get; set; }
       // ... all per-game overrides as nullable properties
   }
   ```

2. **Single serialization point**: `JsonSerializer.Serialize(settingsDict)` where `settingsDict` is `Dictionary<string, PerGameSettings>`

3. **Migration system**: Version the settings format, run migrations on load (handles renames, removed fields, type changes)

4. **Eliminates** the manual dict/hashset pattern with MigrateDict/MigrateHashSet in `RenameGame()`

---

## Priority 10: Incremental Panel Updates

**Problem**: Any per-game override change triggers a full `BuildOverridesPanel()` rebuild (documented in steering as causing "panel rebuild clobbers ComboBox state"). The rebuild recreates all controls from scratch, losing focus, scroll position, and transient state.

### Improvements

1. **Identify which section changed** and only rebuild that section:
   - RS Channel changed → rebuild only the RS Channel row
   - DXVK variant changed → rebuild only the DXVK row
   - Most changes only affect one row

2. **Bind panel elements to observable properties** where possible:
   - Version labels, status dots, and enabled states can be data-bound
   - Only structural changes (adding/removing rows) need imperative code

3. **Short-term fix**: After setting a per-game override, suppress NotifyAll() during the handler execution (the comboInitializing guard pattern generalized)

---

## Non-Priority Items (Nice-to-Have)

- **Remove Grid View** — Grid/card view is unused and adds maintenance burden (separate layout code path, card builder complexity). Remove entirely — Detail view and Compact view cover all use cases.
- **Localization framework** — Currently English-only. WinUI supports `.resw` files. Not urgent for the target audience.
- **Accessibility audit** — Screen reader support for code-behind UI elements needs manual ARIA/automation properties.
- **Plugin system** — Allow third-party components (shader packs, custom ReShade builds) to register without app changes. Very long-term.
- **Telemetry/analytics** — Understand which features are used, which games are most popular. Privacy-sensitive, opt-in only.

---

## Execution Strategy

The improvements above are ordered by impact-to-effort ratio. Recommended execution order based on dependencies and risk:

### Phase 1 — Reduce Surface Area (Do First)

These steps delete code and make everything else safer. No behavioral changes.

| Step | Task | Rationale |
|------|------|-----------|
| 1 | **Remove Grid View** | Deletes code, reduces blast radius for all subsequent refactors. Less to split, unify, and maintain. |
| 2 | **Split mega-files** (Priority 1) | Pure mechanical work, zero behavioral change. Makes every following step easier to navigate and review. |
| 3 | **Unify dual UI builders** (Priority 2) | With files smaller and grid view gone, extract SharedOverridesBuilder. Eliminates the "update both" pitfall before adding more features. |

### Phase 2 — Structural Improvements

Clean boundaries and reduced coupling. Each step benefits from the splits done in Phase 1.

| Step | Task | Rationale |
|------|------|-----------|
| 4 | **NVAPI abstraction** (Priority 6) | DlssPresetService is 103KB and the most fragile code. Splitting into Read/Write/ProfileMatching/RawApi establishes clean boundaries for future driver profile work. |
| 5 | **Reduce ViewModel surface** (Priority 4) | With builders using shared helpers (step 3), pass services directly. Removes 25+ forwarding properties. |
| 6 | **Formalize concurrency** (Priority 3) | Hardest one — do after splits so concurrent access points are clearly visible. |

### Phase 3 — Foundation (v3.0 Territory)

Major architectural changes. Depend on stable boundaries from Phase 2.

| Step | Task | Rationale |
|------|------|-----------|
| 7 | **Settings modernization** (Priority 9) | Structured per-game settings model. Do before component system — it will want clean per-game config. |
| 8 | **Data-driven components** (Priority 7) | The big win, but depends on settings modernization and clean service boundaries from steps 4-5. |
| 9 | **Structured error handling** (Priority 5) | Incremental alongside anything, but component system benefits most. |
| 10 | **Fix test infrastructure** (Priority 8) | After interfaces stabilize from steps 7-8. Otherwise stubs break immediately. |
| 11 | **Incremental panel updates** (Priority 10) | Last — the unified builder (step 3) makes this much simpler. Only worth doing once panel code is in one place. |

### Key Insight

Remove Grid View → Split files → Unify builders. That sequence eliminates the most maintenance debt with the least risk, and every subsequent step benefits from it.
