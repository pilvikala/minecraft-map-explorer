#!/usr/bin/env bash
# Builds a linux-x64 .deb from a self-contained `dotnet publish` output.
#
# Usage: build-deb.sh <publish-dir> <output-dir> <version>
set -euo pipefail

PUBLISH_DIR="${1:?usage: build-deb.sh <publish-dir> <output-dir> <version>}"
OUTPUT_DIR="${2:?usage: build-deb.sh <publish-dir> <output-dir> <version>}"
VERSION="${3:?usage: build-deb.sh <publish-dir> <output-dir> <version>}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

PKG_ROOT="$WORK_DIR/minecraft-map"
mkdir -p "$PKG_ROOT/DEBIAN" \
         "$PKG_ROOT/usr/lib/minecraft-map" \
         "$PKG_ROOT/usr/bin" \
         "$PKG_ROOT/usr/share/applications" \
         "$PKG_ROOT/usr/share/icons/hicolor/256x256/apps"

cp -r "$PUBLISH_DIR"/. "$PKG_ROOT/usr/lib/minecraft-map/"
chmod +x "$PKG_ROOT/usr/lib/minecraft-map/MapExplorer.App"

cat > "$PKG_ROOT/usr/bin/minecraft-map" <<'EOF'
#!/bin/sh
exec /usr/lib/minecraft-map/MapExplorer.App "$@"
EOF
chmod +x "$PKG_ROOT/usr/bin/minecraft-map"

cp "$SCRIPT_DIR/minecraft-map.desktop" "$PKG_ROOT/usr/share/applications/minecraft-map.desktop"
cp "$SCRIPT_DIR/assets/icon-256.png" "$PKG_ROOT/usr/share/icons/hicolor/256x256/apps/minecraft-map.png"

INSTALLED_SIZE_KB=$(du -sk "$PKG_ROOT/usr" | cut -f1)

cat > "$PKG_ROOT/DEBIAN/control" <<EOF
Package: minecraft-map
Version: ${VERSION}
Section: games
Priority: optional
Architecture: amd64
Installed-Size: ${INSTALLED_SIZE_KB}
Maintainer: pilvikala <info@alnus.fi>
Description: Minecraft Map Explorer
 Explore Minecraft world saves as a 2D map, with surface, heightmap,
 cave, Y-slice, and biome view modes plus ore overlays.
EOF

mkdir -p "$OUTPUT_DIR"
dpkg-deb --build --root-owner-group "$PKG_ROOT" "$OUTPUT_DIR/minecraft-map_${VERSION}_amd64.deb"

echo "Built $OUTPUT_DIR/minecraft-map_${VERSION}_amd64.deb"
