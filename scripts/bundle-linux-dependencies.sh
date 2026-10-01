#!/usr/bin/env bash
set -euo pipefail
rhi_repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
source "$rhi_repo/scripts/linux-dependencies.env"
rhi_publish="${1:?Usage: bundle-linux-dependencies.sh PUBLISH_DIRECTORY}"
rhi_cache="$rhi_repo/artifacts/dependencies"
mkdir -p "$rhi_cache"
rhi_archive="$rhi_cache/7zip-$RHI_7ZIP_VERSION-linux-x64.tar.xz"
rhi_temporary="$(mktemp -d "$rhi_cache/download.XXXXXX")"
trap 'rm -rf -- "$rhi_temporary"' EXIT
rhi_verified() { printf '%s  %s\n' "$RHI_7ZIP_SHA256" "$1" | sha256sum --check --status; }
if [[ ! -f "$rhi_archive" ]] || ! rhi_verified "$rhi_archive"; then
    curl --fail --location --silent --show-error --retry 3 "$RHI_7ZIP_URL" -o "$rhi_temporary/archive.tar.xz"
    if ! rhi_verified "$rhi_temporary/archive.tar.xz"; then
        echo '7-Zip download does not match the pinned SHA-256; refusing to package it.' >&2
        exit 1
    fi
    mv -- "$rhi_temporary/archive.tar.xz" "$rhi_archive"
fi
# Extract only the pinned static binary and redistribution notices.
tar -xJf "$rhi_archive" -C "$rhi_temporary" 7zzs License.txt readme.txt
mkdir -p "$rhi_publish/tools" "$rhi_publish/licenses/7zip"
install -m 755 "$rhi_temporary/7zzs" "$rhi_publish/tools/7zz"
install -m 644 "$rhi_temporary/License.txt" "$rhi_temporary/readme.txt" "$rhi_publish/licenses/7zip/"
printf '7-Zip %s\nBinary archive: %s\nSHA-256: %s\nSource code: %s\n' "$RHI_7ZIP_VERSION" "$RHI_7ZIP_URL" "$RHI_7ZIP_SHA256" "$RHI_7ZIP_SOURCE_URL" \
    > "$rhi_publish/licenses/7zip/BUILD-INFO.txt"
"$rhi_publish/tools/7zz" i > /dev/null
