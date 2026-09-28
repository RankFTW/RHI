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
rhi_apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$rhi_apps"
# Desktop entries have their own escaping, followed by Exec argument escaping.
# Quote one argument without invoking a shell or requiring Python at runtime.
rhi_exec='"'
for ((rhi_i = 0; rhi_i < ${#rhi_launcher}; rhi_i++)); do
    rhi_char="${rhi_launcher:rhi_i:1}"
    case "$rhi_char" in
        '\') rhi_exec+='\\\\' ;;
        '"') rhi_exec+='\\"' ;;
        '`') rhi_exec+='\\`' ;;
        '$') rhi_exec+='\\$' ;;
        '%') rhi_exec+='%%' ;;
        $'\n'|$'\r') echo 'Move RHI to a folder without a newline in its name before installing the menu entry.' >&2; exit 1 ;;
        *) rhi_exec+="$rhi_char" ;;
    esac
done
rhi_exec+='"'
# Icon is a desktop string value, not an Exec argument.
rhi_icon="${rhi_icon//\\/\\\\}"
{
    printf '%s\n' '[Desktop Entry]' 'Type=Application' 'Name=RHI (Linux / Proton)' \
        'Comment=Manage ReShade and RenoDX for Proton games' \
        "Exec=$rhi_exec" "Icon=$rhi_icon" 'Terminal=false' 'Categories=Game;' 'StartupWMClass=RHI.Linux'
} > "$rhi_apps/rhi-linux.desktop"
printf 'Installed menu entry: %s\n' "$rhi_apps/rhi-linux.desktop"
printf '%s\n' 'Keep the RHI folder here; the menu entry launches it from this location.'
