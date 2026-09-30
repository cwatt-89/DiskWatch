#!/bin/bash
# Builds DiskWatch.app into ./build (and optionally installs it to /Applications with --install).
set -euo pipefail
cd "$(dirname "$0")/.."

# The Command Line Tools' newest SDK needs SwiftUI macro plugins that only ship with full Xcode.
if [ -z "${SDKROOT:-}" ] && [ -d /Library/Developer/CommandLineTools/SDKs/MacOSX26.sdk ] && ! xcode-select -p | grep -q Xcode.app; then
  export SDKROOT=/Library/Developer/CommandLineTools/SDKs/MacOSX26.sdk
fi

swift build -c release

APP=build/DiskWatch.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp .build/release/DiskWatch "$APP/Contents/MacOS/DiskWatch"
cp Resources/Info.plist "$APP/Contents/Info.plist"

if [ ! -f Resources/AppIcon.icns ]; then
  TMP=$(mktemp -d)
  swift scripts/make_icon.swift "$TMP/icon_1024.png"
  ICONSET="$TMP/AppIcon.iconset"
  mkdir -p "$ICONSET"
  for s in 16 32 128 256 512; do
    sips -z $s $s "$TMP/icon_1024.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    sips -z $((s*2)) $((s*2)) "$TMP/icon_1024.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o Resources/AppIcon.icns
  rm -rf "$TMP"
fi
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

codesign --force --sign - "$APP"
echo "Built $APP"

if [ "${1:-}" = "--install" ]; then
  rm -rf /Applications/DiskWatch.app
  cp -R "$APP" /Applications/DiskWatch.app
  echo "Installed to /Applications/DiskWatch.app"
fi
