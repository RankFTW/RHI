#!/usr/bin/env python3
"""Checks manifest.json and dlss_manifest.json before they're pushed (pushing to main is a deploy).

Fails on:
  - invalid JSON
  - duplicate keys in the same object (the app silently keeps only the last one)
  - keys that differ only by case inside the fields ManifestService.Normalize() makes
    case-insensitive: Normalize() throws and every user falls back to the stale cached manifest

Case pairs in other fields are allowed: those lookups are exact, so some games need both
spellings (e.g. "Dragon Quest VII Reimagined" / "DRAGON QUEST VII Reimagined").

The case-insensitive field list is read from ManifestService.cs + RemoteManifest.cs, so it
stays in sync with the code.

Usage: python tools/check-manifest.py            (from the repo root)
Exit code 0 = OK, 1 = problems found.
"""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MANIFESTS = ["manifest.json", "dlss_manifest.json"]
SERVICE = ROOT / "RenoDXCommander" / "Services" / "ManifestService.cs"
MODEL = ROOT / "RenoDXCommander" / "Models" / "RemoteManifest.cs"


def case_insensitive_fields():
    """JSON names of the RemoteManifest dictionaries that Normalize() rebuilds with OrdinalIgnoreCase."""
    service = SERVICE.read_text(encoding="utf-8")
    body = service[service.index("static void Normalize("):]
    props = set(re.findall(r"m\.(\w+)\s*=\s*new Dictionary<.*StringComparer\.OrdinalIgnoreCase", body))

    model = MODEL.read_text(encoding="utf-8")
    json_names = {}
    for json_name, prop in re.findall(r'\[JsonPropertyName\("([^"]+)"\)\]\s*public\s+[^\n]*?\s(\w+)\s*\{', model):
        json_names[prop] = json_name

    missing = sorted(p for p in props if p not in json_names)
    if missing:
        print(f"  note: no [JsonPropertyName] found for {missing}; using camelCase")
    return {json_names.get(p, p[0].lower() + p[1:]) for p in props}


def check(path, ci_fields):
    problems = []
    current_path = []

    def hook(pairs):
        keys = [k for k, _ in pairs]
        seen = {}
        for k in keys:
            if k in seen:
                problems.append(f"duplicate key {k!r}")
            seen[k] = True
        return dict(pairs)

    text = path.read_text(encoding="utf-8-sig")
    try:
        data = json.loads(text, object_pairs_hook=hook)
    except json.JSONDecodeError as e:
        return [f"invalid JSON: {e}"]

    if isinstance(data, dict):
        for field in ci_fields:
            value = data.get(field)
            if not isinstance(value, dict):
                continue
            by_lower = {}
            for k in value:
                by_lower.setdefault(k.lower(), []).append(k)
            for variants in by_lower.values():
                if len(variants) > 1:
                    problems.append(
                        f"{field}: keys differ only by case {variants} "
                        f"(Normalize() would throw and users keep the old manifest)")
    return problems


def main():
    ci_fields = case_insensitive_fields()
    failed = False
    for name in MANIFESTS:
        path = ROOT / name
        if not path.exists():
            continue
        problems = check(path, ci_fields if name == "manifest.json" else set())
        if problems:
            failed = True
            print(f"FAIL {name}")
            for p in problems:
                print(f"  - {p}")
        else:
            print(f"OK   {name}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
