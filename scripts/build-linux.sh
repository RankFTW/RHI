#!/usr/bin/env bash
set -euo pipefail
# Physical path: on Bazzite /home is a symlink to /var/home, and restoring through the symlink
# drops the tests' project reference, so its NuGet dependencies are missing at runtime.
rhi_repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
source "$rhi_repo/scripts/linux-dependencies.env"
if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
    echo 'This build targets x86_64 Linux (including Bazzite).' >&2
    exit 1
fi
rhi_sdk="${RHI_DOTNET:-${XDG_DATA_HOME:-$HOME/.local/share}/rhi-dotnet/dotnet}"
if [[ -z "${RHI_DOTNET:-}" ]] && command -v dotnet >/dev/null 2>&1; then rhi_sdk="$(command -v dotnet)"; fi
if [[ ! -x "$rhi_sdk" ]]; then
    echo 'Installing the .NET 8 SDK in your user data directory (no root required).'
    rhi_install="$(mktemp)"
    trap 'rm -f "$rhi_install"' EXIT
    curl --fail --location --silent --show-error https://dot.net/v1/dotnet-install.sh -o "$rhi_install"
    bash "$rhi_install" --version "$RHI_SDK_VERSION" --install-dir "$(dirname -- "$rhi_sdk")" --no-path
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
"$rhi_sdk" test "$rhi_repo/RHI.Linux.Tests/RHI.Linux.Tests.csproj" -c Release --nologo
"$rhi_sdk" test "$rhi_repo/RHI.Linux.UiTests/RHI.Linux.UiTests.csproj" -c Release --nologo
mkdir -p "$rhi_repo/artifacts"
rhi_publish="$(mktemp -d "$rhi_repo/artifacts/publish.XXXXXX")"
trap 'rm -rf -- "$rhi_publish"; if [[ -n "${rhi_install:-}" ]]; then rm -f -- "$rhi_install"; fi' EXIT
"$rhi_sdk" publish "$rhi_repo/RHI.Linux/RHI.Linux.csproj" -c Release -r linux-x64 --self-contained true -o "$rhi_publish" --nologo
"$rhi_repo/scripts/bundle-linux-dependencies.sh" "$rhi_publish"
cp "$rhi_repo/docs/LINUX.md" "$rhi_publish/LINUX.md"
cp "$rhi_repo/docs/LINUX-RELEASE.md" "$rhi_publish/README.md"
cp "$rhi_repo/docs/LINUX-RELEASE.md" "$rhi_publish/LINUX-RELEASE.md"
cp "$rhi_repo/LICENSE" "$rhi_publish/LICENSE"
cp "$rhi_repo/run-linux.sh" "$rhi_publish/run-linux.sh"
cp "$rhi_repo/scripts/install-linux-desktop.sh" "$rhi_publish/install.sh"
cp "$rhi_repo/RHI.Linux/Assets/rhi.png" "$rhi_publish/rhi.png"
{
    printf 'Commit: %s\n' "$(git -C "$rhi_repo" rev-parse HEAD 2>/dev/null || echo unknown)"
    printf 'Revision: %s\n' "$(git -C "$rhi_repo" describe --always --dirty 2>/dev/null || echo unknown)"
    printf 'SDK: %s\n' "$("$rhi_sdk" --version)"
    printf '7-Zip: %s (%s)\n' "$RHI_7ZIP_VERSION" "$RHI_7ZIP_SHA256"
    printf '7-Zip source: %s\n' "$RHI_7ZIP_SOURCE_URL"
    printf 'Built: %s\n' "$(date -u +%FT%TZ)"
} > "$rhi_publish/BUILD-INFO.txt"
# Package a fresh publish directory so files left by older builds cannot leak in.
tar -czf "$rhi_repo/artifacts/RHI-linux-x64.tar.gz" -C "$rhi_publish" .
(cd "$rhi_repo/artifacts" && sha256sum RHI-linux-x64.tar.gz > RHI-linux-x64.tar.gz.sha256)
mkdir -p "$rhi_repo/artifacts/linux-x64"
# Unlink old files before copying so an open app can finish using its old binaries.
cp -a --remove-destination "$rhi_publish/." "$rhi_repo/artifacts/linux-x64/"
echo "Ready: $rhi_repo/artifacts/linux-x64/RHI.Linux"
echo "Package: $rhi_repo/artifacts/RHI-linux-x64.tar.gz"
