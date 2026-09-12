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

### Test Infrastructure
- Baseline verification (`ea55a3d`, .NET SDK 9.0.315): `dotnet test RenoDXCommander.Tests/RenoDXCommander.Tests.csproj -p:Platform=x64` — 33 passed
- Add integration tests: install/uninstall roundtrip, manifest parse → card assertions, settings save/load
- CI pipeline: `dotnet build && dotnet test` on push

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

### Store-Qualified installPathOverrides
Games on both Steam and Xbox with different subfolder structures (`Win64` vs `WinGDK`).

- Pipe-separated paths (try both, use whichever exists) — consistent with `engineIniPathOverrides`
- Or store-qualified dict: `{ "Steam": "..\\Win64", "Xbox": "..\\WinGDK" }` — needs migration

**Trigger:** When a game has the same name from both stores but different subfolder layouts.

### NVAPI Driver Version Gating
- Check driver version before enabling settings (MFG needs 572.16+, render scale needs 565+)
- Currently the UI silently fails on older drivers
- Show "Requires driver X.XX+" tooltip when disabled

### Custom ReShade Auto-Redeploy
When a user updates a custom ReShade DLL in the Custom folder, automatically redeploy it to all games using that DLL.

- Hash each `.dll` in Custom folder, store hashes in `custom_reshade_hashes.json` alongside the DLLs (in the Custom folder itself)
- On Refresh + 4-hour background cycle: re-hash and compare
- If hash changed → redeploy to all games with "Custom" RS channel that use that specific DLL
- Vulkan games: update the global layer in `%ProgramData%\ReShade\` (requires admin)
- Update stored hashes after successful redeploy

**Source:** Discord user request.

---

## 💡 Nice-to-Have

Low priority items with no current demand.

- **Localization** — WinUI `.resw` support. Not urgent for target audience.
- **Accessibility audit** — Screen reader support for code-behind UI elements.
- **Plugin system** — Third-party components register without app changes.
- **Telemetry** — Usage analytics. Privacy-sensitive, opt-in only.
