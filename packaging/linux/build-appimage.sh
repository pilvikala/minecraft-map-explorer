#!/usr/bin/env bash
# Builds a linux-x64 AppImage from a self-contained `dotnet publish` output.
#
# Usage: build-appimage.sh <publish-dir> <output-dir> <version>
#
# There's no first-party .NET tool for AppImage packaging (unlike
# electron-builder, which handled this for the old Electron app) — this is a
# small hand-rolled assembly of the standard AppDir layout, then a call out to
# the upstream `appimagetool`.
set -euo pipefail

PUBLISH_DIR="${1:?usage: build-appimage.sh <publish-dir> <output-dir> <version>}"
OUTPUT_DIR="${2:?usage: build-appimage.sh <publish-dir> <output-dir> <version>}"
VERSION="${3:?usage: build-appimage.sh <publish-dir> <output-dir> <version>}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

APPDIR="$WORK_DIR/MinecraftMap.AppDir"
mkdir -p "$APPDIR/usr/bin"

cp -r "$PUBLISH_DIR"/. "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/MapExplorer.App"

# AppImage convention: .desktop + icon at the AppDir root, plus an AppRun entry point.
cp "$SCRIPT_DIR/minecraft-map.desktop" "$APPDIR/minecraft-map.desktop"
cp "$SCRIPT_DIR/assets/icon-256.png" "$APPDIR/minecraft-map.png"
ln -sf minecraft-map.png "$APPDIR/.DirIcon"
ln -sf usr/bin/MapExplorer.App "$APPDIR/AppRun"

mkdir -p "$OUTPUT_DIR"

APPIMAGETOOL="$WORK_DIR/appimagetool"
if command -v appimagetool >/dev/null 2>&1; then
  APPIMAGETOOL="$(command -v appimagetool)"
else
  echo "Downloading appimagetool..."
  curl -fsSL -o "$APPIMAGETOOL" \
    https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod +x "$APPIMAGETOOL"
fi

# appimagetool is itself distributed as an AppImage, which needs FUSE to run
# directly — not available in many CI containers/sandboxes. --appimage-extract-and-run
# sidesteps that by self-extracting first; safe to use unconditionally.
ARCH=x86_64 "$APPIMAGETOOL" --appimage-extract-and-run "$APPDIR" "$OUTPUT_DIR/minecraft-map-${VERSION}-x86_64.AppImage"

echo "Built $OUTPUT_DIR/minecraft-map-${VERSION}-x86_64.AppImage"
