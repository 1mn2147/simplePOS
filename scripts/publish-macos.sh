#!/bin/bash
set -euo pipefail

RUNTIME="osx-arm64"
CONFIGURATION="Release"
SIGNING_IDENTITY="-"
NOTARY_PROFILE=""
BUNDLE_IDENTIFIER="com.simplepos.desktop"

usage() {
  cat <<'USAGE'
Usage: scripts/publish-macos.sh [options]

Options:
  --identity NAME          Developer ID Application identity. Default: ad-hoc (-)
  --notary-profile NAME    notarytool keychain profile. Submits, waits, and staples.
  --bundle-id ID           Bundle identifier. Default: com.simplepos.desktop
  --configuration NAME     Build configuration. Default: Release
  --help                   Show this help.

Examples:
  scripts/publish-macos.sh
  scripts/publish-macos.sh --identity "Developer ID Application: Example (TEAMID)"
  scripts/publish-macos.sh --identity "Developer ID Application: Example (TEAMID)" \
    --notary-profile simplepos-notary
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --identity)
      [[ $# -ge 2 ]] || { echo "--identity requires a value" >&2; exit 2; }
      SIGNING_IDENTITY="$2"
      shift 2
      ;;
    --notary-profile)
      [[ $# -ge 2 ]] || { echo "--notary-profile requires a value" >&2; exit 2; }
      NOTARY_PROFILE="$2"
      shift 2
      ;;
    --bundle-id)
      [[ $# -ge 2 ]] || { echo "--bundle-id requires a value" >&2; exit 2; }
      BUNDLE_IDENTIFIER="$2"
      shift 2
      ;;
    --configuration)
      [[ $# -ge 2 ]] || { echo "--configuration requires a value" >&2; exit 2; }
      CONFIGURATION="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ -n "$NOTARY_PROFILE" && "$SIGNING_IDENTITY" == "-" ]]; then
  echo "--notary-profile requires a Developer ID --identity" >&2
  exit 2
fi
if [[ ! "$BUNDLE_IDENTIFIER" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ]]; then
  echo "Invalid --bundle-id: $BUNDLE_IDENTIFIER" >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd -P)"
PROJECT="$ROOT_DIR/src/Pos.Desktop/Pos.Desktop.csproj"
ENTITLEMENTS="$SCRIPT_DIR/macos.entitlements"
OUTPUT_PARENT="$ROOT_DIR/artifacts/publish"
OUTPUT_DIR="$OUTPUT_PARENT/$RUNTIME"
mkdir -p "$OUTPUT_PARENT"
STAGE_DIR="$(mktemp -d "$OUTPUT_PARENT/.${RUNTIME}.stage.XXXXXX")"
trap 'rm -rf "$STAGE_DIR"' EXIT
PUBLISH_DIR="$STAGE_DIR/publish"
APP="$STAGE_DIR/SimplePOS.app"
CONTENTS="$APP/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"

case "$OUTPUT_DIR" in
  "$ROOT_DIR"/artifacts/publish/*) ;;
  *) echo "Refusing output outside artifacts/publish: $OUTPUT_DIR" >&2; exit 1 ;;
esac

cd "$ROOT_DIR"

DOTNET="$ROOT_DIR/.dotnet-sdk/dotnet"
if [[ ! -x "$DOTNET" ]]; then
  DOTNET="$(command -v dotnet || true)"
fi
[[ -x "$DOTNET" ]] || { echo ".NET SDK was not found" >&2; exit 1; }

VERSION="$("$DOTNET" msbuild "$PROJECT" -nologo -getProperty:Version | tail -1 | tr -d '\r')"
[[ "$VERSION" =~ ^[0-9]+(\.[0-9]+){1,3}([+-][A-Za-z0-9.-]+)?$ ]] || {
  echo "Invalid project version: $VERSION" >&2
  exit 1
}
SHORT_VERSION="${VERSION%%[-+]*}"
ZIP="$STAGE_DIR/SimplePOS-${SHORT_VERSION}-${RUNTIME}.zip"
CHECKSUM="$ZIP.sha256"

mkdir -p "$PUBLISH_DIR" "$MACOS_DIR" "$RESOURCES_DIR"

export DOTNET_CLI_HOME="$ROOT_DIR/.dotnet-home"
export NUGET_PACKAGES="$ROOT_DIR/.nuget/packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

"$DOTNET" publish "$PROJECT" \
  --configuration "$CONFIGURATION" \
  --runtime "$RUNTIME" \
  --self-contained true \
  --output "$PUBLISH_DIR" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=false \
  -p:PublishTrimmed=false \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:EnableCompressionInSingleFile=true

[[ -x "$PUBLISH_DIR/Pos.Desktop" ]] || {
  echo "Published executable was not produced" >&2
  exit 1
}

cp -R "$PUBLISH_DIR/." "$MACOS_DIR/"
rm -rf "$PUBLISH_DIR"

/usr/libexec/PlistBuddy -c "Add :CFBundleDevelopmentRegion string ko" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleDisplayName string Simple POS" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleExecutable string Pos.Desktop" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleIdentifier string $BUNDLE_IDENTIFIER" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleInfoDictionaryVersion string 6.0" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleName string SimplePOS" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundlePackageType string APPL" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleShortVersionString string $SHORT_VERSION" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleVersion string $SHORT_VERSION" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :LSMinimumSystemVersion string 15.0" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :NSHighResolutionCapable bool true" "$CONTENTS/Info.plist"
/usr/libexec/PlistBuddy -c "Add :NSPrincipalClass string NSApplication" "$CONTENTS/Info.plist"
plutil -lint "$CONTENTS/Info.plist"

while IFS= read -r -d '' candidate; do
  if [[ "$candidate" != "$MACOS_DIR/Pos.Desktop" ]] && file -b "$candidate" | grep -q '^Mach-O'; then
    if [[ "$SIGNING_IDENTITY" == "-" ]]; then
      codesign --force --sign - --timestamp=none "$candidate"
    else
      codesign --force --sign "$SIGNING_IDENTITY" --options runtime --timestamp \
        "$candidate"
    fi
  fi
done < <(find "$MACOS_DIR" -type f -print0)

if [[ "$SIGNING_IDENTITY" == "-" ]]; then
  codesign --force --sign - --timestamp=none "$APP"
else
  codesign --force --sign "$SIGNING_IDENTITY" --options runtime --timestamp \
    --entitlements "$ENTITLEMENTS" "$APP"
fi
codesign --verify --deep --strict --verbose=2 "$APP"

ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"

if [[ -n "$NOTARY_PROFILE" ]]; then
  xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$APP"
  xcrun stapler validate "$APP"
  rm -f "$ZIP"
  ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
fi

shasum -a 256 "$ZIP" | sed "s#  .*#  $(basename "$ZIP")#" > "$CHECKSUM"

mkdir -p "$OUTPUT_DIR"
FINAL_APP="$OUTPUT_DIR/SimplePOS.app"
FINAL_ZIP="$OUTPUT_DIR/$(basename "$ZIP")"
FINAL_CHECKSUM="$OUTPUT_DIR/$(basename "$CHECKSUM")"
if [[ -e "$FINAL_APP" ]]; then
  PREVIOUS_APP="$STAGE_DIR/previous-SimplePOS.app"
  mv "$FINAL_APP" "$PREVIOUS_APP"
fi
if ! mv "$APP" "$FINAL_APP"; then
  if [[ -n "${PREVIOUS_APP:-}" && -e "$PREVIOUS_APP" ]]; then
    mv "$PREVIOUS_APP" "$FINAL_APP"
  fi
  exit 1
fi
mv "$ZIP" "$FINAL_ZIP"
mv "$CHECKSUM" "$FINAL_CHECKSUM"

echo "macOS bundle: $FINAL_APP"
echo "Distribution ZIP: $FINAL_ZIP"
echo "SHA-256: $FINAL_CHECKSUM"
if [[ "$SIGNING_IDENTITY" == "-" ]]; then
  echo "Signing: local ad-hoc (not suitable for public distribution)"
elif [[ -z "$NOTARY_PROFILE" ]]; then
  echo "Signing: Developer ID (not notarized)"
else
  echo "Signing: Developer ID, notarized and stapled"
fi
