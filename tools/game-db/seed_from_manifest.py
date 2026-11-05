#!/usr/bin/env python3
"""
seed_from_manifest.py — Pre-seed game_db.json with known data from manifest.json
=================================================================================
Reads manifest.json and extracts per-game overrides (API, install path, bitness,
engine, PCGW URL, exe path) to bootstrap game_db.json without needing submissions.

Run this once to create an initial game_db.json, then run merge_game_db.py on top
as community submissions arrive to fill in gaps and confirm values.

Usage:
  python seed_from_manifest.py [--manifest ../../manifest.json] [--output .]
"""

import argparse
import json
from pathlib import Path


def api_string_to_field(api_str: str) -> str:
    """Convert manifest API string (e.g. 'DX12', 'DX11', 'VLK') to canonical form."""
    s = api_str.strip().upper()
    if s in ("DX12", "D3D12", "DIRECTX12", "DIRECT3D 12"):
        return "DX12"
    if s in ("DX11", "D3D11", "DIRECTX11", "DIRECT3D 11"):
        return "DX11"
    if s in ("DX10", "D3D10", "DIRECTX10", "DIRECT3D 10"):
        return "DX10"
    if s in ("DX9", "D3D9", "DIRECTX9", "DIRECT3D 9"):
        return "DX9"
    if s in ("VLK", "VULKAN"):
        return "Vulkan"
    if s in ("OGL", "OPENGL"):
        return "OpenGL"
    return api_str.strip()


def parse_api_override(value: str) -> tuple[str, list[str]]:
    """
    Parse manifest graphicsApiOverrides value like 'DX12', 'DX11, DX12', 'DX12, VLK'.
    Returns (primary_api, all_apis_list).
    """
    parts = [api_string_to_field(p) for p in value.split(",")]
    primary = parts[0] if parts else ""
    return primary, sorted(set(parts))


def main():
    parser = argparse.ArgumentParser(
        description="Seed game_db.json from manifest.json"
    )
    parser.add_argument("--manifest", default="../../manifest.json",
                        help="Path to manifest.json (default: ../../manifest.json)")
    parser.add_argument("--output", default=".",
                        help="Output directory for game_db.json (default: current dir)")
    args = parser.parse_args()

    manifest_path = Path(args.manifest)
    output_dir    = Path(args.output)

    if not manifest_path.exists():
        print(f"Error: manifest not found at '{manifest_path}'")
        return

    with open(manifest_path, encoding="utf-8") as f:
        manifest = json.load(f)

    games: dict[str, dict] = {}  # name_lower → record

    def get_or_create(name: str) -> dict:
        key = name.lower()
        if key not in games:
            games[key] = {
                "name":            name,
                "store":           "",
                "vote_count":      0,
                "api":             None,
                "all_apis":        [],
                "bitness":         None,
                "engine":          None,
                "install_subpath": None,
                "exe_relative":    None,
                "steam_appid":     None,
                "xbox_aumid":      None,
                "epic_app_name":   None,
                "pcgw_url":        None,
                "confidence":      1.0,
                "source":          "manifest",
            }
        return games[key]

    # graphicsApiOverrides → api + all_apis
    for name, api_val in manifest.get("graphicsApiOverrides", {}).items():
        rec = get_or_create(name)
        primary, all_apis = parse_api_override(api_val)
        rec["api"]      = primary
        rec["all_apis"] = all_apis

    # installPathOverrides → install_subpath
    for name, subpath in manifest.get("installPathOverrides", {}).items():
        rec = get_or_create(name)
        # Take the first pipe-separated value as primary
        rec["install_subpath"] = subpath.split("|")[0].strip()

    # thirtyTwoBitGames → bitness 32
    for name in manifest.get("thirtyTwoBitGames", []):
        rec = get_or_create(name)
        rec["bitness"] = 32

    # sixtyFourBitGames → bitness 64
    for name in manifest.get("sixtyFourBitGames", []):
        rec = get_or_create(name)
        rec["bitness"] = 64

    # engineOverrides → engine
    for name, engine in manifest.get("engineOverrides", {}).items():
        rec = get_or_create(name)
        rec["engine"] = engine

    # engineHintOverrides → engine (only if engineOverrides didn't set it)
    for name, hint in manifest.get("engineHintOverrides", {}).items():
        rec = get_or_create(name)
        if not rec["engine"]:
            rec["engine"] = hint

    # pcgwUrlOverrides → pcgw_url
    for name, url in manifest.get("pcgwUrlOverrides", {}).items():
        rec = get_or_create(name)
        rec["pcgw_url"] = url

    # launchExeOverrides → exe_relative
    for name, exe in manifest.get("launchExeOverrides", {}).items():
        rec = get_or_create(name)
        rec["exe_relative"] = exe

    # Remove entries that only have the bare skeleton (nothing from manifest populated)
    result = [r for r in games.values() if any([
        r["api"], r["install_subpath"], r["bitness"],
        r["engine"], r["pcgw_url"], r["exe_relative"]
    ])]
    result.sort(key=lambda r: r["name"].lower())

    output_dir.mkdir(parents=True, exist_ok=True)
    out_path = output_dir / "game_db.json"
    db_output = {
        "version":    "1",
        "game_count": len(result),
        "games":      result,
    }
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(db_output, f, indent=2, ensure_ascii=False)

    print(f"Written: {out_path}  ({len(result)} games seeded from manifest)")

    # Summary by field
    fields = ["api", "install_subpath", "bitness", "engine", "pcgw_url", "exe_relative"]
    for field in fields:
        count = sum(1 for r in result if r.get(field))
        print(f"  {field:<20} {count} games")


if __name__ == "__main__":
    main()
