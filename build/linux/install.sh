#!/usr/bin/env sh
# Installs Qtoxide for the current user (~/.local), with a menu entry. Run from the extracted folder.
set -e
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
install -m 755 "$here/Qtoxide" "$HOME/.local/bin/Qtoxide"
install -m 644 "$here/qtoxide.png" "$HOME/.local/share/icons/hicolor/256x256/apps/qtoxide.png"
sed "s|^Exec=.*|Exec=$HOME/.local/bin/Qtoxide|" "$here/qtoxide.desktop" > "$HOME/.local/share/applications/qtoxide.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$HOME/.local/share/applications" || true
echo "Qtoxide installed: find it in your applications menu, or run ~/.local/bin/Qtoxide"
