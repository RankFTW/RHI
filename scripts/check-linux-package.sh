#!/usr/bin/env bash
set -euo pipefail
rhi_repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
(cd "$rhi_repo/artifacts" && sha256sum -c RHI-linux-x64.tar.gz.sha256)
rhi_check="$(mktemp -d)"
trap 'rm -rf -- "$rhi_check"' EXIT
# Spaces and percent signs exercise desktop-entry quoting as well as relocation.
rhi_bundle="$rhi_check/RHI portable 100%"
mkdir -p "$rhi_bundle"
tar -xzf "$rhi_repo/artifacts/RHI-linux-x64.tar.gz" -C "$rhi_bundle"
export XDG_DATA_HOME="$rhi_check/data"
export XDG_CACHE_HOME="$rhi_check/cache"
export XDG_CONFIG_HOME="$rhi_check/config"
export DOTNET_ROOT="$rhi_check/no-runtime"
export DOTNET_MULTILEVEL_LOOKUP=0
cd "$rhi_check"
"$rhi_bundle/run-linux.sh" --help > help.txt
"$rhi_bundle/install.sh"
python3 - "$rhi_bundle" "$XDG_DATA_HOME/applications/rhi-linux.desktop" <<'PY'
from pathlib import Path
import sys
bundle, desktop = map(Path, sys.argv[1:])
assert 'RHI Linux' in Path('help.txt').read_text()
for name in ('RHI.Linux', 'libhostfxr.so', 'libcoreclr.so', 'rhi.png', 'LINUX.md', 'README.md', 'LICENSE', 'BUILD-INFO.txt'):
    assert (bundle / name).is_file(), name
entry = desktop.read_text()
assert f'Exec="{str(bundle).replace("%", "%%")}/run-linux.sh"' in entry
assert f'Icon={bundle}/rhi.png' in entry
print('PASS: standalone launch, package contents, and relocated menu installation.')
PY
