#!/usr/bin/env bash
set -euo pipefail
rhi_repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ ! -x "$rhi_repo/artifacts/linux-x64/RHI.Linux" ]]; then "$rhi_repo/scripts/build-linux.sh"; fi
rhi_apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$rhi_apps"
python3 - "$rhi_repo" "$rhi_apps" <<'PY'
from pathlib import Path
import sys
repo, apps = map(Path, sys.argv[1:])
def desktop_quote(value):
    return '"' + str(value).replace('\\', '\\\\\\\\').replace('"', '\\\\"').replace('`', '\\\\`').replace('$', '\\\\$').replace('%', '%%') + '"'
entry = '\n'.join([
    '[Desktop Entry]', 'Type=Application', 'Name=RHI (Linux / Proton)',
    'Comment=Manage ReShade and RenoDX for Proton games',
    'Exec=' + desktop_quote(repo / 'run-linux.sh'),
    'Icon=' + str(repo / 'RenoDXCommander/icon.ico'),
    'Terminal=false', 'Categories=Game;', 'StartupWMClass=RHI.Linux', ''
])
(apps / 'rhi-linux.desktop').write_text(entry)
print('Installed menu entry:', apps / 'rhi-linux.desktop')
PY
