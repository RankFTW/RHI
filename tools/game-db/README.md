# RHI Community Game Database

The goal: a community-sourced database of per-game data (API, install path, bitness, engine)
that RHI can pull at startup to correctly detect games without relying solely on PE scanning
or manual manifest entries.

## Scripts

### `seed_from_manifest.py`
Pre-seeds `game_db.json` with known-good data already in `manifest.json`
(graphicsApiOverrides, installPathOverrides, engineOverrides, bitness, PCGW URLs, exe paths).
Run this once to bootstrap the database.

```
python seed_from_manifest.py
```

### `merge_game_db.py`
Merges community submissions from the `submissions/` folder into `game_db.json`.
Uses majority vote per field — PCGW+PE submissions get double weight.
Deduplicates by content hash across runs (same file can't be counted twice).

```
python merge_game_db.py
python merge_game_db.py --min-votes 2   # require 2+ users per game
```

## Workflow

1. Run `seed_from_manifest.py` once to create the baseline `game_db.json`
2. Users click **Export Game Data** in RHI Settings and paste the zip into Discord
3. Save incoming zips into the `submissions/` folder
4. Run `merge_game_db.py` — new submissions are merged, duplicates skipped
5. Review `conflicts.json` for games where users disagree
6. Publish `game_db.json` to wherever RHI fetches it

## Fields

| Field | Source | Notes |
|-------|--------|-------|
| `api` | PE scan + PCGW + manifest | Primary graphics API (DX9/11/12/Vulkan/OpenGL) |
| `all_apis` | PE scan + PCGW | All detected APIs |
| `bitness` | PE scan + manifest | 32 or 64 |
| `engine` | Detection + manifest | Engine name string |
| `install_subpath` | User export + manifest | Subfolder from game root to mod install location |
| `exe_relative` | User export + manifest | Relative path to main exe |
| `steam_appid` | Steam detection | Steam App ID |
| `xbox_aumid` | Xbox detection | Xbox App User Model ID |
| `epic_app_name` | Epic detection | Epic app identifier |
| `pcgw_url` | PCGW lookup + manifest | PCGamingWiki page URL |
