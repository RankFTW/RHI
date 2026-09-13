#!/usr/bin/env bash
set -euo pipefail
rhi_repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ -f "$rhi_repo/RHI.Linux" && -x "$rhi_repo/RHI.Linux" ]]; then
    exec "$rhi_repo/RHI.Linux" "$@"
fi
if [[ ! -x "$rhi_repo/artifacts/linux-x64/RHI.Linux" ]]; then
    "$rhi_repo/scripts/build-linux.sh"
fi
exec "$rhi_repo/artifacts/linux-x64/RHI.Linux" "$@"
