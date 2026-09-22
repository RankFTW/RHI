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
    python fetch_pcgw_data.py --update      # only fetch pages changed since last run
    python fetch_pcgw_data.py --diff        # show changes vs existing, don't write
    python fetch_pcgw_data.py --test        # fetch first 500 games only (for testing)

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
from datetime import datetime, timezone
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

CARGO_LIMIT   = 500
REQUEST_DELAY = 2.0  # seconds between paginated requests (~30 req/min, well under 60 limit)


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
    r = session.get(API_URL, params={"action": "query", "meta": "tokens",
                                      "type": "login", "format": "json"})
    r.raise_for_status()
    token = r.json()["query"]["tokens"]["logintoken"]

    r = session.post(API_URL, data={"action": "login", "lgname": username,
                                     "lgpassword": password, "lgtoken": token, "format": "json"})
    r.raise_for_status()
    result = r.json().get("login", {})

    if result.get("result") == "Success":
        print(f"  Logged in as '{result.get('lgusername', username)}'")
        return True

    print(f"ERROR: Login failed — {result.get('reason', result)}")
    sys.exit(1)


# ── Recent changes ────────────────────────────────────────────────────────────

def get_changed_pages_since(session, since_ts):
    """
    Returns the set of article page titles changed since `since_ts` (ISO 8601 string).
    Uses action=query&list=recentchanges, paginating with rccontinue.
    Only namespace 0 (articles). Filters out redirects and non-article edits.
    """
    print(f"Fetching recent changes since {since_ts}...")
    pages = set()
    params = {
        "action":    "query",
        "list":      "recentchanges",
        "rcnamespace": "0",
        "rcstart":   since_ts,      # most recent → oldest (descending)
        "rcdir":     "newer",       # oldest → newest (ascending from since_ts)
        "rcprop":    "title|type",
        "rclimit":   "500",
        "rctype":    "edit|new",
        "format":    "json",
    }
    while True:
        r = session.get(API_URL, params=params)
        if r.status_code == 429:
            print("  Rate limited — waiting 61s...")
            time.sleep(61)
            r = session.get(API_URL, params=params)
        r.raise_for_status()
        data = r.json()

        for rc in data.get("query", {}).get("recentchanges", []):
            title = rc.get("title", "").strip()
            if title:
                pages.add(title)

        cont = data.get("continue", {}).get("rccontinue")
        if not cont:
            break
        params["rccontinue"] = cont
        time.sleep(1.0)

    print(f"  {len(pages):,} changed pages found")
    return pages


# ── CargoQuery helpers ────────────────────────────────────────────────────────

def cargo_query(session, tables, fields, join_on=None, where=None, offset=0, limit=CARGO_LIMIT):
    params = {"action": "cargoquery", "tables": tables, "fields": fields,
              "limit": limit, "offset": offset, "format": "json"}
    if join_on: params["join_on"] = join_on
    if where:   params["where"]   = where

    r = session.get(API_URL, params=params)
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
    results = []
    offset  = 0
    while True:
        batch = cargo_query(session, tables, fields, join_on=join_on,
                            where=where, offset=offset, limit=CARGO_LIMIT)
        results.extend(batch)
        print(f"  {len(results):,} rows fetched...", end="\r")
        if len(batch) < CARGO_LIMIT:
            break
        if limit and len(results) >= limit:
            break
        offset += CARGO_LIMIT
        time.sleep(REQUEST_DELAY)
    print(f"  {len(results):,} rows total          ")
    return results


def cargo_query_pages(session, tables, fields, page_names, join_on=None, chunk_size=50):
    """
    Fetch CargoQuery data for a specific set of page names (no full pagination needed).
    Chunks the page list into batches of `chunk_size` to stay within URL length limits.
    """
    results = []
    pages   = sorted(page_names)
    for i in range(0, len(pages), chunk_size):
        chunk = pages[i:i + chunk_size]
        # Build an IN clause: Game._pageName IN ("A","B","C",...)
        in_list = ",".join(f'"{p.replace(chr(34), chr(39))}"' for p in chunk)
        where   = f"Game._pageName IN ({in_list})"
        batch   = cargo_query(session, tables, fields, join_on=join_on, where=where)
        results.extend(batch)
        time.sleep(REQUEST_DELAY)
    return results


# ── Data fetching ─────────────────────────────────────────────────────────────

def fetch_api_data(session, test_limit=None, page_names=None):
    """Fetch Direct3D/Vulkan/OpenGL + Steam AppIDs."""
    if page_names is not None:
        print(f"Fetching API data for {len(page_names):,} specific pages...")
        rows = cargo_query_pages(
            session,
            tables  = "Game,API",
            fields  = "Game._pageName=Page,Game.Steam_AppID=SteamAppID,"
                      "API.Direct3D_versions=DX,API.Vulkan_versions=Vulkan,API.OpenGL_versions=OpenGL",
            page_names = page_names,
            join_on = "Game._pageID=API._pageID",
        )
    else:
        print("Fetching API data (Direct3D / Vulkan / OpenGL + Steam AppIDs)...")
        rows = cargo_query_all(
            session,
            tables  = "Game,API",
            fields  = "Game._pageName=Page,Game.Steam_AppID=SteamAppID,"
                      "API.Direct3D_versions=DX,API.Vulkan_versions=Vulkan,API.OpenGL_versions=OpenGL",
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

        steam_id = None
        if steam_raw:
            for part in steam_raw.split(","):
                try:
                    val = int(part.strip())
                    if val > 0: steam_id = val; break
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


def fetch_config_paths(session, test_limit=None, page_names=None):
    """Fetch Windows + Microsoft Store config paths."""
    where_filter = "GameData.Type='Config' AND (GameData.Platform='Windows' OR GameData.Platform='Steam' OR GameData.Platform='Microsoft Store')"

    if page_names is not None:
        print(f"Fetching config paths for {len(page_names):,} specific pages...")
        rows = cargo_query_pages(
            session,
            tables  = "Game,GameData",
            fields  = "Game._pageName=Page,GameData.Type=Type,GameData.Platform=Platform,GameData.Paths=Paths",
            page_names = page_names,
            join_on = "Game._pageID=GameData._pageID",
        )
        # Filter manually since we can't add the Type/Platform filter to an IN query easily
        rows = [r for r in rows if r.get("Type") == "Config"
                and r.get("Platform") in ("Windows", "Steam", "Microsoft Store")]
    else:
        print("Fetching config file paths (GameData table)...")
        rows = cargo_query_all(
            session,
            tables  = "Game,GameData",
            fields  = "Game._pageName=Page,GameData.Type=Type,GameData.Platform=Platform,GameData.Paths=Paths",
            join_on = "Game._pageID=GameData._pageID",
            where   = where_filter,
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
        if platform in ("Windows", "Steam"):
            if "config_path" not in entry or platform == "Windows":
                entry["config_path"] = paths
        elif platform == "Microsoft Store":
            entry["config_path_xbox"] = paths

    return result


# ── Merge + output ─────────────────────────────────────────────────────────────

def merge_data(api_data, config_data):
    all_pages = set(api_data) | set(config_data)
    merged = {}
    for page in sorted(all_pages):
        entry = {}
        if page in api_data:    entry.update(api_data[page])
        if page in config_data: entry.update(config_data[page])
        merged[page] = entry
    return merged


def load_existing(path):
    if path.exists():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            games = data.get("games", data) if isinstance(data, dict) and "games" in data else data
            generated = data.get("generated") if isinstance(data, dict) else None
            return games, generated
        except Exception:
            pass
    return {}, None


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
        for p in sorted(new_pages)[:20]: print(f"  + {p}")
    if changed_pages:
        print(f"\nChanged ({min(len(changed_pages), 20)} of {len(changed_pages)}):")
        for p in sorted(changed_pages)[:20]:
            diffs = {k for k in set(existing_games[p]) | set(new_data[p]) if existing_games[p].get(k) != new_data[p].get(k)}
            print(f"  ~ {p}  [{', '.join(sorted(diffs))}]")


# ── Entry point ───────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Fetch PCGW game data for RHI")
    parser.add_argument("--update", action="store_true",
                        help="Only fetch pages changed since the last run (fast update)")
    parser.add_argument("--diff",   action="store_true",
                        help="Show diff vs existing file (don't write)")
    parser.add_argument("--test",   action="store_true",
                        help="Fetch first 500 games only (for testing)")
    args = parser.parse_args()

    existing_games, last_generated = load_existing(OUTPUT_FILE)

    username, password = load_credentials()
    session = make_session()
    login(session, username, password)

    if args.update:
        if not last_generated:
            print("No existing file found — running full fetch instead.")
            args.update = False
        else:
            print(f"Update mode — fetching changes since {last_generated}")
            changed_pages = get_changed_pages_since(session, last_generated)
            if not changed_pages:
                print("No changes since last run. File is up to date.")
                return

            time.sleep(REQUEST_DELAY)
            api_data    = fetch_api_data(session, page_names=changed_pages)
            time.sleep(REQUEST_DELAY)
            config_data = fetch_config_paths(session, page_names=changed_pages)

            # Merge updates into existing data
            new_entries = merge_data(api_data, config_data)
            merged = dict(existing_games)
            merged.update(new_entries)
            merged = dict(sorted(merged.items()))

            added   = len(set(new_entries) - set(existing_games))
            updated = len(set(new_entries) & set(existing_games))
            print(f"\nUpdate: {added} new pages, {updated} updated pages")

            if args.diff:
                show_diff(new_entries, {k: existing_games[k] for k in new_entries if k in existing_games})
            else:
                save_output(merged, OUTPUT_FILE)
                print("Done. Commit database/pcgw_data.json to publish.")
            return

    # Full fetch
    test_limit = 500 if args.test else None
    if args.test:
        print("TEST MODE — fetching first 500 games only")

    api_data    = fetch_api_data(session, test_limit=test_limit)
    time.sleep(REQUEST_DELAY)
    config_data = fetch_config_paths(session, test_limit=test_limit)

    merged = merge_data(api_data, config_data)
    print(f"\nMerged: {len(merged):,} games total")

    if args.diff:
        show_diff(merged, existing_games)
    else:
        save_output(merged, OUTPUT_FILE)
        print("Done. Commit database/pcgw_data.json to publish.")


if __name__ == "__main__":
    main()
