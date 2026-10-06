#!/usr/bin/env bash
# Builds a downloadable Qtoxide package for one platform into dist/.
#   build/package.sh <version> <rid>      rid: win-x64 win-arm64 osx-arm64 osx-x64 linux-x64 linux-arm64
# macOS packages must be built on macOS (the app is ad-hoc signed, which Apple Silicon requires).
set -euo pipefail

VERSION="$1"
RID="$2"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PUBLISH="$ROOT/artifacts/publish/$RID"
DIST="$ROOT/dist"
NAME="Qtoxide-$VERSION-$RID"

rm -rf "$PUBLISH" "$ROOT/artifacts/stage/$RID"
mkdir -p "$DIST"
dotnet publish "$ROOT/src/Qtoxide/Qtoxide.csproj" -c Release -r "$RID" -p:Version="$VERSION" -o "$PUBLISH" --nologo

STAGE="$ROOT/artifacts/stage/$RID"
mkdir -p "$STAGE"

case "$RID" in
  win-*)
    mkdir -p "$STAGE/Qtoxide"
    cp "$PUBLISH"/* "$STAGE/Qtoxide/"
    cp "$ROOT/LICENSE" "$STAGE/Qtoxide/LICENSE.txt"
    (cd "$STAGE" && rm -f "$DIST/$NAME.zip" && zip -qr "$DIST/$NAME.zip" Qtoxide)
    ;;

  osx-*)
    APP="$STAGE/Qtoxide.app"
    mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
    cp "$PUBLISH"/* "$APP/Contents/MacOS/"
    cp "$ROOT/build/qtoxide.icns" "$APP/Contents/Resources/qtoxide.icns"
    sed "s/@VERSION@/$VERSION/g" "$ROOT/build/macos/Info.plist" > "$APP/Contents/Info.plist"
    chmod +x "$APP/Contents/MacOS/Qtoxide"
    codesign --force --deep --sign - "$APP"   # ad-hoc signature (no Apple developer account)
    rm -f "$DIST/$NAME.zip"
    ditto -c -k --keepParent "$APP" "$DIST/$NAME.zip"
    ;;

  linux-*)
    DIR="$STAGE/$NAME"
    mkdir -p "$DIR"
    cp "$PUBLISH"/* "$DIR/"
    chmod +x "$DIR/Qtoxide"
    cp "$ROOT/src/Qtoxide/Assets/qtoxide-256.png" "$DIR/qtoxide.png"
    cp "$ROOT/build/linux/qtoxide.desktop" "$ROOT/build/linux/install.sh" "$ROOT/LICENSE" "$DIR/"
    chmod +x "$DIR/install.sh"
    tar -C "$STAGE" -czf "$DIST/$NAME.tar.gz" "$NAME"
    ;;

  *)
    echo "Unknown runtime: $RID" >&2
    exit 1
    ;;
esac

echo "Built $(ls "$DIST"/"$NAME".*)"
