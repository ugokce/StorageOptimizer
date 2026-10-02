#!/usr/bin/env bash
# Builds "Storage Optimizer.app" from the Swift package.
#
# Usage: scripts/build_app.sh [--universal] [--version X.Y.Z]
#   --universal   build for both Apple silicon and Intel (requires Xcode, not just the Command Line Tools)
#   --version     version string written to Info.plist (default: 1.0.0)
#
# Output: build/Storage Optimizer.app and build/StorageOptimizer.zip
set -euo pipefail

cd "$(dirname "$0")/.."

PRODUCT="StorageOptimizer"
APP_NAME="Storage Optimizer.app"
BUILD_DIR="build"
VERSION="1.0.0"
UNIVERSAL=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --universal) UNIVERSAL=1; shift ;;
    --version) VERSION="$2"; shift 2 ;;
    -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done

BUILD_FLAGS=(-c release)
if [[ $UNIVERSAL -eq 1 ]]; then
  BUILD_FLAGS+=(--arch arm64 --arch x86_64)
fi

echo "==> Building $PRODUCT (${BUILD_FLAGS[*]})"
swift build "${BUILD_FLAGS[@]}"
BIN_DIR="$(swift build "${BUILD_FLAGS[@]}" --show-bin-path)"

APP_DIR="$BUILD_DIR/$APP_NAME"
echo "==> Assembling $APP_DIR"
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp "$BIN_DIR/$PRODUCT" "$APP_DIR/Contents/MacOS/$PRODUCT"

BUILD_NUMBER="$(git rev-list --count HEAD 2>/dev/null || echo 1)"
sed -e "s/__VERSION__/$VERSION/" -e "s/__BUILD__/$BUILD_NUMBER/" Packaging/Info.plist > "$APP_DIR/Contents/Info.plist"
printf 'APPL????' > "$APP_DIR/Contents/PkgInfo"

# Copy SwiftPM resource bundles, if any target ever declares resources.
find "$BIN_DIR" -maxdepth 1 -name '*.bundle' -exec cp -R {} "$APP_DIR/Contents/Resources/" \;

echo "==> Signing (ad hoc)"
codesign --force --deep --sign - "$APP_DIR"

echo "==> Zipping"
rm -f "$BUILD_DIR/$PRODUCT.zip"
ditto -c -k --keepParent "$APP_DIR" "$BUILD_DIR/$PRODUCT.zip"

echo "Done: $APP_DIR"
