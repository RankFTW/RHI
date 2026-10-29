#!/usr/bin/env python3
"""Fail when upstream source changes without an explicit Linux disposition review.

No network access and no baseline-writing mode: fetch the desired upstream ref,
review the diff, then edit docs/LINUX-PORT-SYNC.json deliberately.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = "docs/LINUX-PORT-SYNC.json"
STATUSES = {"shared", "adapted", "deferred", "windows-only", "data"}


def tracked(path):
    p = Path(path)
    # Build outputs and upstream's explicitly excluded backups are not production input.
    if any(part in {"bin", "obj", "original"} for part in p.parts):
        return False
    if p.parts[0] in {"RenoDXCommander", "RHI.Core", "RHI.DropHelper"}:
        return p.suffix in {".cs", ".xaml", ".csproj", ".props", ".targets", ".ini", ".json", ".conf", ".manifest"}
    if p.parts[0] in {"database", "game-db", "engine-files"}:
        return p.suffix in {".json", ".ini"}
    return path in {"docs/RenoDXdb.json", "manifest.json", "dlss_manifest.json", "RenoDXCommander.sln", "Directory.Build.props", "Directory.Build.targets"}


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args], stderr=subprocess.PIPE)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def local_sources(root):
    candidates = []
    for directory in ("RenoDXCommander", "RHI.Core", "RHI.DropHelper", "database", "game-db", "engine-files"):
        candidates.extend((root / directory).rglob("*"))
    candidates.extend(root / name for name in ("docs/RenoDXdb.json", "manifest.json", "dlss_manifest.json", "RenoDXCommander.sln", "Directory.Build.props", "Directory.Build.targets"))
    return {str(p.relative_to(root)): digest(p.read_bytes()) for p in candidates
            if p.is_file() and tracked(str(p.relative_to(root)))}


def upstream_sources(root, ref):
    # Resolve first, so a missing ref cannot silently reduce the audited source set.
    commit = git(root, "rev-parse", "--verify", ref + "^{commit}").decode().strip()
    paths = git(root, "ls-tree", "-r", "--name-only", commit).decode().splitlines()
    return {p: digest(git(root, "show", commit + ":" + p)) for p in paths if tracked(p)}


def check(root, upstream=None):
    manifest = json.loads((root / MANIFEST).read_text())
    errors = []
    if manifest.get("version") != 1:
        return ["Unsupported or missing tracking manifest version"]
    entries = manifest.get("files", {})
    if not entries:
        return ["Tracking manifest has no source mappings"]
    baseline = manifest.get("reviewed_commit", "")
    if len(baseline) != 40 or any(c not in "0123456789abcdef" for c in baseline):
        errors.append("reviewed_commit must be a full Git commit hash")
    else:
        git(root, "cat-file", "-e", baseline + "^{commit}")
    local = local_sources(root)
    remote = upstream_sources(root, upstream) if upstream else None
    for path in sorted(set(local) | set(entries) | set(remote or {})):
        entry = entries.get(path)
        if not isinstance(entry, dict):
            errors.append(f"UNMAPPED: {path}; add an explicit Linux disposition")
            continue
        if not tracked(path):
            errors.append(f"OUTSIDE AUDITED SCOPE: {path}")
        if entry.get("status") not in STATUSES or not entry.get("reason", "").strip():
            errors.append(f"INVALID DISPOSITION: {path}; supply status and review rationale")
        counterparts = entry.get("linux", [])
        if not isinstance(counterparts, list) or not all(isinstance(p, str) for p in counterparts):
            errors.append(f"INVALID COUNTERPARTS: {path}")
            counterparts = []
        if entry.get("status") in {"shared", "adapted", "data"} and not counterparts:
            errors.append(f"MISSING LINUX COUNTERPART: {path}")
        for counterpart in counterparts:
            target = root / counterpart
            if Path(counterpart).is_absolute() or ".." in Path(counterpart).parts or not target.is_file():
                errors.append(f"MISSING/INVALID COUNTERPART: {path} -> {counterpart}")
        if entry.get("local_sha256") != local.get(path):
            errors.append(f"LOCAL SOURCE CHANGED: {path}; review and update local_sha256")
        if remote is not None and entry.get("upstream_sha256") != remote.get(path):
            errors.append(f"UPSTREAM SOURCE CHANGED: {path}; inspect the upstream diff and port or document the decision")
        if path not in local and (remote is None or path not in remote):
            errors.append(f"STALE MAPPING: {path}; explicitly review its removal")
        for key in ("local_sha256", "upstream_sha256"):
            value = entry.get(key)
            if key not in entry or value is not None and (len(value) != 64 or any(c not in "0123456789abcdef" for c in value)):
                errors.append(f"INVALID REVIEW HASH: {path}: {key}")
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=ROOT, help=argparse.SUPPRESS)
    parser.add_argument("--upstream", help="Git ref to compare as well as the working tree (for example origin/main)")
    args = parser.parse_args()
    try:
        errors = check(args.repo.resolve(), args.upstream)
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        print(f"Upstream review check could not run: {exc}", file=sys.stderr)
        return 1
    if errors:
        print("Linux upstream review required:\n" + "\n".join("  " + error for error in errors), file=sys.stderr)
        print("See docs/LINUX-PORT-SYNC.md. This check never updates review hashes automatically.", file=sys.stderr)
        return 1
    print("PASS: every tracked upstream source has an explicit, unchanged Linux review mapping."
          + (f" Compared upstream ref {args.upstream}." if args.upstream else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
