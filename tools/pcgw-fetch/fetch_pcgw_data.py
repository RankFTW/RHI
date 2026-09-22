#!/usr/bin/env python3
"""
fetch_pcgw_data.py — Fetches PCGW game data for RHI
=====================================================
Logs into PCGamingWiki using a bot account, then paginates through all
games using the CargoQuery API to build a single JSON file stored in
rhi-repo/database/pcgw_data.json.

RHI fetches this file at startup instead of hitting PCGW per-game,
dramatically reducing request volume.

Fields fetched per game:
  - PCGW page name (used as key)
  - Steam AppID
  - DirectX 9/10/11/12 support
  - Vulkan support
  - OpenGL support
  - Config file path (Windows/Steam)
  - Config file path (Microsoft Store / Xbox Game Pass)

Usage:
    python fetch_pcgw_data.py               # full fetch, overwrite output
    python fetch_pcgw_data.py --diff        # show changes vs existing, don't write
    python fetch_pcgw_data.py --test        # fetch first 500 games only

Credentials: stored in tools/pcgw-fetch/.env (never committed)
  PCGW_USERNAME=Rankftw@BotName
  PCGW_PASSWORD=generatedpassword

Rate limit: PCGW allows 60 req/min. We stay well under with REQUEST_DELAY.
"""

import argparse
import json
import os
import sys
import time
from pathlib import Path

import requests

# ── Paths ─────────────────────────────────────────────────────────────────────

SCRIPT_DIR  = Path(__file__).parent
REPO_ROOT   = SCRIPT_DIR.parent.parent
OUTPUT_FILE = REPO_ROOT / "database" / "pcgw_data.json"
ENV_FILE    = SCRIPT_DIR / ".env"

# ── PCGW API ──────────────────────────────────────────────────────────────────

API_URL    = "https://www.pcgamingwiki.com/w/api.php"
USER_AGENT = "RHI-PCGW-Fetcher/1.0 (github.com/RankFTW/RHI; rankftw@googlemail.com)"

# Max rows per CargoQuery call (PCGW allows up to 500)
CARGO_LIMIT = 500

# Seconds between requests — PCGW rate limit is 60/min, we target ~30/min
REQUEST_DELAY = 2.0


# ── Credentials ───────────────────────────────────────────────────────────────

def load_credentials():
    if ENV_FILE.exists():
        for line in ENV_FILE.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, _, val = line.partition("=")
                os.environ.setdefault(key.strip(), val.strip())

    username = os.environ.get("PCGW_USERNAME", "").strip()
    password = os.environ.get("PCGW_PASSWORD", "").strip()

    if not username or not password:
        print(f"ERROR: credentials not found. Create {ENV_FILE} with:")
        print("  PCGW_USERNAME=Rankftw@BotName")
        print("  PCGW_PASSWORD=generatedpassword")
        sys.exit(1)

    return username, password


# ── Session + Login ───────────────────────────────────────────────────────────

def make_session():
    s = requests.Session()
    s.headers.update({"User-Agent": USER_AGENT})
    return s


def login(session, username, password):
    print(f"Logging in as '{username}'...")

    r = session.get(API_URL, params={
        "action": "query", "meta": "tokens",
        "type": "login", "format": "json",
    })
    r.raise_for_status()
    token = r.json()["query"]["tokens"]["logintoken"]

    r = session.post(API_URL, data={
        "action": "login", "lgname": username,
        "lgpassword": password, "lgtoken": token, "format": "json",
    })
    r.raise_for_status()
    result = r.json().get("login", {})

    if result.get("result") == "Success":
        print(f"  Logged in as '{result.get('lgusername', username)}'")
        return True

    print(f"ERROR: Login failed — {result.get('reason', result)}")
    sys.exit(1)


# ── CargoQuery helpers ────────────────────────────────────────────────────────

def cargo_query(session, tables, fields, join_on=None, where=None, offset=0, limit=CARGO_LIMIT):
    params = {
        "action": "cargoquery",
        "tables": tables,
        "fields": fields,
        "limit":  limit,
        "offset": offset,
        "format": "json",
    }
    if join_on: params["join_on"] = join_on
    if where:   params["where"]   = where

    r = session.get(API_URL, params=params)

    # Handle rate limit — wait 60s and retry once
    if r.status_code == 429:
        print("  Rate limited (429) — waiting 61 seconds...")
        time.sleep(61)
        r = session.get(API_URL, params=params)

    r.raise_for_status()
    data = r.json()

    if "error" in data:
        raise RuntimeError(f"CargoQuery error: {data['error'].get('info', data['error'])}")

    return [item["title"] for item in data.get("cargoquery", [])]


def cargo_query_all(session, tables, fields, join_on=None, where=None, limit=None):
    """Paginate through all results. If limit is set, stop after that many rows."""
    results = []
    offset  = 0
    page_size = CARGO_LIMIT
    while True:
        batch = cargo_query(session, tables, fields, join_on=join_on,
                            where=where, offset=offset, limit=page_size)
        results.extend(batch)
        print(f"  {len(results):,} rows fetched...", end="\r")
        if len(batch) < page_size:
            break
        if limit and len(results) >= limit:
            break
        offset += page_size
        time.sleep(REQUEST_DELAY)
    print(f"  {len(results):,} rows total          ")
    return results


# ── Data fetching ─────────────────────────────────────────────────────────────

def fetch_api_data(session, test_limit=None):
    """
    Fetches graphics API support for all PCGW games.
    Returns dict: page_name → { dx9, dx10, dx11, dx12, vulkan, opengl, steam_appid? }
    """
    print("Fetching API data (Direct3D / Vulkan / OpenGL + Steam AppIDs)...")
    rows = cargo_query_all(
        session,
        tables  = "Game,API",
        fields  = "Game._pageName=Page,"
                  "Game.Steam_AppID=SteamAppID,"
                  "API.Direct3D_versions=DX,"
                  "API.Vulkan_versions=Vulkan,"
                  "API.OpenGL_versions=OpenGL",
        join_on = "Game._pageID=API._pageID",
        limit   = test_limit,
    )
    print(f"  Parsing {len(rows):,} API rows...")

    result = {}
    for row in rows:
        page = (row.get("Page") or "").strip()
        if not page:
            continue

        dx  = (row.get("DX")     or "").lower()
        vk  = (row.get("Vulkan") or "").lower()
        ogl = (row.get("OpenGL") or "").lower()
        steam_raw = (row.get("SteamAppID") or "").strip()

        # Steam AppID — may be comma-separated, take first positive integer
        steam_id = None
        if steam_raw:
            for part in steam_raw.split(","):
                part = part.strip()
                try:
                    val = int(part)
                    if val > 0:
                        steam_id = val
                        break
                except ValueError:
                    pass

        entry = {}
        if "9"  in dx or "9.0" in dx: entry["dx9"]  = True
        if "10" in dx:                 entry["dx10"] = True
        if "11" in dx:                 entry["dx11"] = True
        if "12" in dx:                 entry["dx12"] = True
        if vk  and vk  not in ("", "false", "none"): entry["vulkan"]  = True
        if ogl and ogl not in ("", "false", "none"): entry["opengl"]  = True
        if steam_id:                   entry["steam_appid"] = steam_id

        result[page] = entry

    return result


def fetch_config_paths(session, test_limit=None):
    """
    Fetches Windows + Microsoft Store config paths for all PCGW games.
    Returns dict: page_name → { config_path?, config_path_xbox? }
    """
    print("Fetching config file paths (GameData table)...")
    rows = cargo_query_all(
        session,
        tables  = "Game,GameData",
        fields  = "Game._pageName=Page,"
                  "GameData.Type=Type,"
                  "GameData.Platform=Platform,"
                  "GameData.Paths=Paths",
        join_on = "Game._pageID=GameData._pageID",
        where   = "GameData.Type='Config' AND (GameData.Platform='Steam' OR GameData.Platform='Microsoft Store')",
        limit   = test_limit,
    )
    print(f"  Parsing {len(rows):,} config rows...")

    result = {}
    for row in rows:
        page     = (row.get("Page")     or "").strip()
        platform = (row.get("Platform") or "").strip()
        paths    = (row.get("Paths")    or "").strip()
        if not page or not paths:
            continue

        entry = result.setdefault(page, {})
        if platform == "Steam":
            entry["config_path"] = paths
        elif platform == "Microsoft Store":
            entry["config_path_xbox"] = paths

    return result


# ── Merge + output ─────────────────────────────────────────────────────────────

def merge_data(api_data, config_data):
    """Merge API and config data. Result keyed by PCGW page name."""
    all_pages = set(api_data) | set(config_data)
    merged = {}
    for page in sorted(all_pages):
        entry = {}
        if page in api_data:
            entry.update(api_data[page])
        if page in config_data:
            entry.update(config_data[page])
        merged[page] = entry
    return merged


def load_existing(path):
    if path.exists():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            # Handle both raw dict and wrapped {version, games} format
            return data.get("games", data) if isinstance(data, dict) and "games" in data else data
        except Exception:
            pass
    return {}


def save_output(data, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    output = {
        "version":    1,
        "game_count": len(data),
        "generated":  time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "games":      data,
    }
    path.write_text(json.dumps(output, indent=2, ensure_ascii=False), encoding="utf-8")
    size_kb = path.stat().st_size // 1024
    print(f"Written: {path}  ({len(data):,} games, {size_kb:,} KB)")


def show_diff(new_data, existing_games):
    new_pages     = set(new_data) - set(existing_games)
    removed_pages = set(existing_games) - set(new_data)
    changed_pages = {p for p in new_data if p in existing_games and new_data[p] != existing_games[p]}

    print(f"\n{'='*60}")
    print(f"  New games:     {len(new_pages):,}")
    print(f"  Removed games: {len(removed_pages):,}")
    print(f"  Changed:       {len(changed_pages):,}")
    print(f"{'='*60}")

    if new_pages:
        print(f"\nNew ({min(len(new_pages), 20)} of {len(new_pages)}):")
        for p in sorted(new_pages)[:20]:
            print(f"  + {p}")

    if changed_pages:
        print(f"\nChanged ({min(len(changed_pages), 20)} of {len(changed_pages)}):")
        for p in sorted(changed_pages)[:20]:
            old = existing_games[p]
            new = new_data[p]
            diffs = {k for k in set(old) | set(new) if old.get(k) != new.get(k)}
            print(f"  ~ {p}  [{', '.join(sorted(diffs))}]")


# ── Entry point ───────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Fetch PCGW game data for RHI")
    parser.add_argument("--diff",  action="store_true", help="Show diff vs existing file (don't write)")
    parser.add_argument("--test",  action="store_true", help="Fetch first 500 games only (for testing)")
    args = parser.parse_args()

    test_limit = 500 if args.test else None
    if args.test:
        print("TEST MODE — fetching first 500 games only")

    username, password = load_credentials()
    session = make_session()
    login(session, username, password)

    api_data = fetch_api_data(session, test_limit=test_limit)
    time.sleep(REQUEST_DELAY)
    config_data = fetch_config_paths(session, test_limit=test_limit)

    merged = merge_data(api_data, config_data)
    print(f"\nMerged: {len(merged):,} games total")

    if args.diff:
        existing = load_existing(OUTPUT_FILE)
        show_diff(merged, existing)
    else:
        save_output(merged, OUTPUT_FILE)
        print("Done. Commit database/pcgw_data.json to rhi-repo to publish.")


if __name__ == "__main__":
    main()
