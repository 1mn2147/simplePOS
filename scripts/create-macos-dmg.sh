#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
OUTPUT_DIR="$SCRIPT_DIR/../artifacts/publish/osx-arm64"
APP="$OUTPUT_DIR/SimplePOS.app"
if [[ ! -d "$APP" ]]; then
  echo "Build SimplePOS.app with scripts/publish-macos.sh first." >&2
  exit 1
fi
codesign --verify --deep --strict "$APP"
VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")"
[[ "$VERSION" =~ ^[0-9]+(\.[0-9]+){1,3}$ ]] || { echo "Invalid app version: $VERSION" >&2; exit 1; }

DMG="SimplePOS-${VERSION}-osx-arm64.dmg"
STAGE_DIR="$(mktemp -d "$OUTPUT_DIR/.dmg.XXXXXX")"
trap 'rm -rf "$STAGE_DIR"' EXIT
cd "$STAGE_DIR"
mkdir image
ditto "$APP" image/SimplePOS.app
ln -s /Applications image/Applications
hdiutil create -volname "SimplePOS" -srcfolder image -format UDZO "$DMG"
hdiutil verify "$DMG"
shasum -a 256 "$DMG" > "$DMG.sha256"
mv "$DMG" "$DMG.sha256" "$OUTPUT_DIR/"
echo "Local test DMG: $OUTPUT_DIR/$DMG"
