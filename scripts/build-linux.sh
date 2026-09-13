#!/usr/bin/env bash
set -euo pipefail
rhi_repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
rhi_sdk="${RHI_DOTNET:-${XDG_DATA_HOME:-$HOME/.local/share}/rhi-dotnet/dotnet}"
if command -v dotnet >/dev/null 2>&1; then rhi_sdk="$(command -v dotnet)"; fi
if [[ ! -x "$rhi_sdk" ]]; then
    echo 'Installing the .NET 8 SDK in your user data directory (no root required).'
    rhi_install="$(mktemp)"
    trap 'rm -f "$rhi_install"' EXIT
    curl --fail --location --silent --show-error https://dot.net/v1/dotnet-install.sh -o "$rhi_install"
    bash "$rhi_install" --version 8.0.425 --install-dir "$(dirname -- "$rhi_sdk")" --no-path
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
"$rhi_sdk" test "$rhi_repo/RHI.Linux.Tests/RHI.Linux.Tests.csproj" -c Release --nologo
"$rhi_sdk" test "$rhi_repo/RHI.Linux.UiTests/RHI.Linux.UiTests.csproj" -c Release --nologo
"$rhi_sdk" publish "$rhi_repo/RHI.Linux/RHI.Linux.csproj" -c Release -r linux-x64 --self-contained true -o "$rhi_repo/artifacts/linux-x64" --nologo
cp "$rhi_repo/docs/LINUX.md" "$rhi_repo/artifacts/linux-x64/LINUX.md"
cp "$rhi_repo/LICENSE" "$rhi_repo/artifacts/linux-x64/LICENSE"
tar -czf "$rhi_repo/artifacts/RHI-linux-x64.tar.gz" -C "$rhi_repo/artifacts/linux-x64" .
echo "Ready: $rhi_repo/artifacts/linux-x64/RHI.Linux"
