#!/usr/bin/env python3
"""
merge_game_db.py — RHI Community Game Database Merger
======================================================
Reads all rhi_game_data.json files from a submissions/ folder,
merges them by majority vote per field, and outputs:

  game_db.json     — the merged community database (ready to publish)
  conflicts.json   — fields where submissions disagree (for manual review)

Usage:
  python merge_game_db.py [--submissions submissions/] [--output .] [--min-votes 1]

Each submission file is a zip containing rhi_game_data.json, or a raw JSON file.
Games are keyed by (name, store) — treated as case-insensitive.

Fields merged by majority vote:
  api, all_apis, bitness, engine, install_subpath, exe_relative,
  steam_appid, xbox_aumid, epic_app_name, pcgw_url

Majority vote rules:
  - Plurality wins (most common value across all submissions for that field).
  - Ties: flagged in conflicts.json; first-seen value kept in game_db.json.
  - Null/empty values are ignored (only non-empty submissions vote).
  - api_source="pcgw+pe" entries are given double weight vs "pe" only.
  - For all_apis: union of all values that appear in >= 50% of non-empty submissions.
"""

import argparse
import collections
import json
import os
import sys
import zipfile
from pathlib import Path


# ---------------------------------------------------------------------------
# Field definitions
# ---------------------------------------------------------------------------

SCALAR_FIELDS = [
    "api",
    "bitness",
    "engine",
    "install_subpath",
    "exe_relative",
    "steam_appid",
    "xbox_aumid",
    "epic_app_name",
    "pcgw_url",
]

LIST_FIELDS = [
    "all_apis",
]

ALL_MERGE_FIELDS = SCALAR_FIELDS + LIST_FIELDS


# ---------------------------------------------------------------------------
# Load submissions
# ---------------------------------------------------------------------------

def load_submission(path: Path) -> list[dict]:
    """Load a single submission file (zip or raw JSON). Returns list of game dicts."""
    try:
        if path.suffix.lower() == ".zip":
            with zipfile.ZipFile(path) as zf:
                names = zf.namelist()
                json_names = [n for n in names if n.endswith(".json")]
                if not json_names:
                    print(f"  [WARN] No JSON file in zip: {path.name}")
                    return []
                with zf.open(json_names[0]) as f:
                    data = json.load(f)
        else:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)

        if isinstance(data, dict) and "games" in data:
            return data["games"]
        if isinstance(data, list):
            return data
        print(f"  [WARN] Unexpected format in {path.name}")
        return []
    except Exception as e:
        print(f"  [ERROR] Failed to load {path.name}: {e}")
        return []


def load_all_submissions(submissions_dir: Path) -> dict[tuple, list[dict]]:
    """
    Returns: {(name_lower, store_lower): [list of game dicts from each submission]}
    """
    all_games: dict[tuple, list[dict]] = collections.defaultdict(list)
    files = sorted(
        [p for p in submissions_dir.iterdir()
         if p.suffix.lower() in (".json", ".zip") and p.is_file()]
    )
    print(f"Found {len(files)} submission file(s) in '{submissions_dir}'")

    for path in files:
        print(f"  Loading: {path.name}")
        games = load_submission(path)
        for game in games:
            name  = str(game.get("name", "")).strip()
            store = str(game.get("store", "")).strip()
            if not name:
                continue
            key = (name.lower(), store.lower())
            all_games[key].append(game)

    print(f"  → {len(all_games)} unique (name, store) combinations\n")
    return all_games


# ---------------------------------------------------------------------------
# Voting helpers
# ---------------------------------------------------------------------------

def get_weight(game: dict) -> int:
    """PCGW+PE submissions get double weight — more reliable API data."""
    src = str(game.get("api_source", "pe"))
    return 2 if "pcgw" in src.lower() else 1


def majority_vote(values_with_weights: list[tuple]) -> tuple:
    """
    Given [(value, weight), ...], return (winner, is_tie, vote_counts).
    Ignores None and empty-string values.
    """
    counts: dict = collections.defaultdict(int)
    for val, w in values_with_weights:
        if val is None or val == "" or val == []:
            continue
        # Normalise: use JSON-serialised form so lists are hashable
        key = json.dumps(val, sort_keys=True) if isinstance(val, (list, dict)) else val
        counts[key] += w

    if not counts:
        return (None, False, {})

    max_votes = max(counts.values())
    winners   = [k for k, v in counts.items() if v == max_votes]
    is_tie    = len(winners) > 1

    # Deserialise back if it was JSON-encoded
    def decode(k):
        try:
            return json.loads(k)
        except Exception:
            return k

    winner = decode(winners[0])
    vote_counts = {decode(k): v for k, v in counts.items()}
    return (winner, is_tie, vote_counts)


def merge_all_apis(submissions: list[dict]) -> list[str]:
    """
    Merge all_apis by union: include an API value if >= 50% of non-empty
    submissions contain it.
    """
    non_empty = [g for g in submissions if g.get("all_apis")]
    if not non_empty:
        return []

    threshold  = len(non_empty) / 2.0
    api_counts: dict[str, int] = collections.defaultdict(int)
    for g in non_empty:
        for api in g.get("all_apis", []):
            api_counts[api.upper()] += 1

    result = sorted(k for k, v in api_counts.items() if v > threshold)
    return result


# ---------------------------------------------------------------------------
# Main merge
# ---------------------------------------------------------------------------

def merge(all_games: dict, min_votes: int) -> tuple[list[dict], list[dict]]:
    """
    Returns (merged_games, conflicts).
    merged_games: sorted list of merged game records for game_db.json
    conflicts: list of conflict records for conflicts.json
    """
    merged_games: list[dict] = []
    conflicts:    list[dict] = []

    for (name_lower, store_lower), submissions in sorted(all_games.items()):
        # Need at least min_votes submissions to include in the db
        if len(submissions) < min_votes:
            continue

        # Use the most common casing for name/store display
        name_counts:  dict[str, int] = collections.Counter(
            g.get("name", "").strip() for g in submissions if g.get("name")
        )
        store_counts: dict[str, int] = collections.Counter(
            g.get("store", "").strip() for g in submissions if g.get("store")
        )
        canonical_name  = name_counts.most_common(1)[0][0]
        canonical_store = store_counts.most_common(1)[0][0] if store_counts else ""

        record: dict = {
            "name":        canonical_name,
            "store":       canonical_store,
            "vote_count":  len(submissions),
        }
        game_conflicts: list[dict] = []

        # ── Scalar fields ──────────────────────────────────────────────────
        for field in SCALAR_FIELDS:
            pairs = [(g.get(field), get_weight(g)) for g in submissions]
            winner, is_tie, vote_counts = majority_vote(pairs)
            record[field] = winner

            if is_tie and len(vote_counts) > 1:
                game_conflicts.append({
                    "field":      field,
                    "winner":     winner,
                    "is_tie":     True,
                    "votes":      vote_counts,
                })
            elif len(vote_counts) > 1:
                # No tie, but multiple different values were seen — record as low-confidence conflict
                total = sum(vote_counts.values())
                winner_votes = vote_counts.get(winner, 0)
                if winner_votes / total < 0.75:
                    game_conflicts.append({
                        "field":      field,
                        "winner":     winner,
                        "is_tie":     False,
                        "confidence": f"{winner_votes}/{total}",
                        "votes":      vote_counts,
                    })

        # ── List fields ────────────────────────────────────────────────────
        record["all_apis"] = merge_all_apis(submissions)

        # ── Confidence score (0.0–1.0) ─────────────────────────────────────
        # Based on how many submissions agree on the primary api field
        api_votes = collections.Counter(
            g.get("api") for g in submissions if g.get("api")
        )
        top_api_count = api_votes.most_common(1)[0][1] if api_votes else 0
        record["confidence"] = round(top_api_count / len(submissions), 2)

        merged_games.append(record)

        if game_conflicts:
            conflicts.append({
                "name":   canonical_name,
                "store":  canonical_store,
                "votes":  len(submissions),
                "fields": game_conflicts,
            })

    return merged_games, conflicts


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(
        description="Merge RHI community game data submissions into game_db.json"
    )
    parser.add_argument(
        "--submissions", default="submissions",
        help="Folder containing submission zip/json files (default: submissions/)"
    )
    parser.add_argument(
        "--output", default=".",
        help="Output directory for game_db.json and conflicts.json (default: current dir)"
    )
    parser.add_argument(
        "--min-votes", type=int, default=1,
        help="Minimum number of submissions required to include a game (default: 1)"
    )
    args = parser.parse_args()

    submissions_dir = Path(args.submissions)
    output_dir      = Path(args.output)

    if not submissions_dir.exists():
        print(f"Error: submissions directory '{submissions_dir}' does not exist.")
        print(f"Create it and drop submission files there, then re-run.")
        sys.exit(1)

    output_dir.mkdir(parents=True, exist_ok=True)

    # Load
    all_games = load_all_submissions(submissions_dir)
    if not all_games:
        print("No games found in submissions. Exiting.")
        sys.exit(0)

    # Merge
    print(f"Merging (min_votes={args.min_votes})...")
    merged_games, conflicts = merge(all_games, min_votes=args.min_votes)

    # Write game_db.json
    db_path = output_dir / "game_db.json"
    db_output = {
        "version":    "1",
        "game_count": len(merged_games),
        "games":      merged_games,
    }
    with open(db_path, "w", encoding="utf-8") as f:
        json.dump(db_output, f, indent=2, ensure_ascii=False)
    print(f"Written: {db_path}  ({len(merged_games)} games)")

    # Write conflicts.json
    conflicts_path = output_dir / "conflicts.json"
    with open(conflicts_path, "w", encoding="utf-8") as f:
        json.dump(conflicts, f, indent=2, ensure_ascii=False)
    print(f"Written: {conflicts_path}  ({len(conflicts)} conflict(s))")

    # Summary
    print()
    print("=== Summary ===")
    print(f"  Total unique games across submissions : {len(all_games)}")
    print(f"  Games included in game_db.json        : {len(merged_games)}")
    print(f"  Games with field conflicts             : {len(conflicts)}")
    if conflicts:
        print()
        print("  Games needing manual review:")
        for c in conflicts[:20]:
            fields = ", ".join(f["field"] for f in c["fields"])
            print(f"    {c['name']} ({c['store']}) — {fields}")
        if len(conflicts) > 20:
            print(f"    ... and {len(conflicts) - 20} more (see conflicts.json)")


if __name__ == "__main__":
    main()
