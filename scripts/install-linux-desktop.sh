#!/usr/bin/env bash
set -euo pipefail
rhi_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ -f "$rhi_dir/RHI.Linux" && -x "$rhi_dir/RHI.Linux" ]]; then
    rhi_launcher="$rhi_dir/run-linux.sh"
    rhi_icon="$rhi_dir/rhi.png"
else
    rhi_repo="$(cd -- "$rhi_dir/.." && pwd)"
    if [[ ! -x "$rhi_repo/artifacts/linux-x64/RHI.Linux" ]]; then "$rhi_repo/scripts/build-linux.sh"; fi
    rhi_launcher="$rhi_repo/run-linux.sh"
    rhi_icon="$rhi_repo/RHI.Linux/Assets/rhi.png"
fi
if ! command -v python3 >/dev/null 2>&1; then
    echo 'The menu installer needs Python 3. You can still launch with ./run-linux.sh.' >&2
    exit 1
fi
rhi_apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$rhi_apps"
python3 - "$rhi_launcher" "$rhi_icon" "$rhi_apps" <<'PY'
from pathlib import Path
import sys
launcher, icon, apps = map(Path, sys.argv[1:])
def desktop_quote(value):
    return '"' + str(value).replace('\\', '\\\\\\\\').replace('"', '\\\\"').replace('`', '\\\\`').replace('$', '\\\\$').replace('%', '%%') + '"'
entry = '\n'.join([
    '[Desktop Entry]', 'Type=Application', 'Name=RHI (Linux / Proton)',
    'Comment=Manage ReShade and RenoDX for Proton games',
    'Exec=' + desktop_quote(launcher),
    'Icon=' + str(icon).replace('\\', '\\\\'),
    'Terminal=false', 'Categories=Game;', 'StartupWMClass=RHI.Linux', ''
])
(apps / 'rhi-linux.desktop').write_text(entry)
print('Installed menu entry:', apps / 'rhi-linux.desktop')
print('Keep the RHI folder here; the menu entry launches it from this location.')
PY
