# RHI Database — Full Wiki Replacement Plan

## Goal

Replace the RenoDX wiki (`https://github.com/clshortfuse/renodx/wiki/Mods`) as the source of truth for mod data with the RHI-maintained database (`rhi-repo`). The wiki will no longer be fetched at all once the DB is complete and trusted.

---

## Current State

- `RenoDxDbSource` setting controls which source drives `_allMods`: `"WikiOnly"` (default), `"DbOnly"`, `"Hybrid"`
- `WikiService.FetchAllAsync` always runs regardless of `RenoDxDbSource` — the wiki result is then discarded by `MergeDbSources` in DbOnly mode
- The wiki is also used for Luma matching, game name normalization, and `SeenWikiMods` tracking
- DB is currently dev-only (`DevUnlockService.IsUnlocked` gate in `InitializeAsync` and `RunBackgroundScanAndMergeAsync`)

---

## Phase 1 — Skip wiki fetch in DbOnly mode (quick win)

**Files:** `MainViewModel.Init.cs`, `MainViewModel.BackgroundScan.cs`

Add a guard around `WikiService.FetchAllAsync` calls:

```csharp
if (!string.Equals(_settingsViewModel.RenoDxDbSource, "DbOnly", StringComparison.OrdinalIgnoreCase))
{
    _allMods = await _wikiService.FetchAllAsync(progress).ConfigureAwait(false);
}
```

Also guard `SeenWikiMods` updates — those should only run when the wiki is the active source.

**Result:** Saves ~300ms on startup and one unnecessary HTTP request per background scan in DbOnly mode.

---

## Phase 2 — Migrate remaining wiki dependencies to DB

These wiki-only features must be ported before the wiki can be fully removed:

### 2a. Luma matching
`MatchLumaGame(gameName)` searches `_allMods` for Luma entries by name. This is currently wiki-sourced. The DB has a separate `RenoDXdb-unreal.json` table for UE-Extended games. Luma named mods should be added to the DB as a `lumaUrl` or `isLuma: true` field, or kept as a separate DB endpoint.

### 2b. `SeenWikiMods` / "New Mods" detection
`SeenWikiModsService` tracks which mod names the user has already seen — used to show the "New Mods" notification badge. Currently seeded from wiki mod names. Should be seeded from DB mod names instead. Low risk change.

### 2c. Game name normalization for wiki matching
`GameDetectionService.MatchGame` compares detected game names against normalized wiki names for `IsWikiExclusion`, `WikiStatusBadge`, etc. If the wiki is removed, these lookups need to be driven from DB entries. The DB `name` field already uses the same naming convention.

### 2d. `ToggleWikiExclusion` and per-game wiki inclusion overrides
`IGameNameService.WikiExclusions` / `WikiOptIns` currently reference "the wiki" in their semantics. Rename to `ModDbExclusions` / `ModDbOptIns` and update all call sites. Pure rename, no logic change.

### 2e. Deprecated mods filter
`WikiService` skips mods listed after a "Deprecated" heading. The DB should have a `"status": "Deprecated"` or just omit deprecated mods. Confirm DB has no deprecated entries before removing the filter.

---

## Phase 3 — Remove wiki entirely

1. Delete `WikiService.cs` and `IWikiService.cs`
2. Remove `WikiService` from `App.xaml.cs` DI registration
3. Remove all `_wikiService` field references in `MainViewModel`
4. Remove `_allMods` population from `WikiService.FetchAllAsync` — replace with DB-only fetch
5. Change `RenoDxDbSource` default to `"DbOnly"` permanently (or remove the setting entirely)
6. Remove `RenoDxDbSourceCard` from `MainWindow.xaml` and `SettingsHandler.InitRenoDxDbSourceCombo()`
7. Remove `DevUnlockService.IsUnlocked` gate from `RenoDXDbService.FetchAllAsync` call sites
8. Update `SeenWikiMods` → `SeenDbMods` references throughout

---

## DB Completeness Requirements Before Phase 3

The DB must cover all mods currently on the RenoDX wiki before the wiki can be removed:
- All named game-specific mods (currently ~954 wiki entries vs 272 DB entries)
- All generic UE-Extended games (currently wiki-scraped; DB has ~70)
- Status field parity: `"Done"`, `"WIP"`, `"Deprecated"` matching wiki ✅/🚧 semantics
- `snapshotUrl` for every mod (same URLs as wiki — already the case for existing DB entries)

Run `docs/merge_game_db.py` against community submissions to grow the DB coverage faster.

---

## Key Files

| File | Role |
|------|------|
| `RenoDXCommander/Services/WikiService.cs` | Fetches and parses RenoDX wiki — to be removed in Phase 3 |
| `RenoDXCommander/Services/RenoDXDbService.cs` | Fetches rhi-repo DB — becomes the sole mod source |
| `RenoDXCommander/ViewModels/MainViewModel.cs` | `MergeDbSources()` — merge logic to simplify once wiki removed |
| `RenoDXCommander/ViewModels/MainViewModel.Init.cs` | Add Phase 1 guard here |
| `RenoDXCommander/ViewModels/MainViewModel.BackgroundScan.cs` | Add Phase 1 guard here |
| `RenoDXCommander/Services/SeenWikiModsService.cs` | Rename + reroute to DB in Phase 2 |
| `game-db/game_db.json` | Community submission data |
| `docs/RenoDXdb.json` | Named mods DB file |
| `docs/RenoDXdb-unreal-ue-extended.json` | UE-Extended DB file |
