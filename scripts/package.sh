#!/usr/bin/env bash
# Builds the distributable layout from the Release build output.
# Usage: scripts/package.sh <version-label>
# Produces AstraSkins-<version-label>.zip containing the addons/ tree.
set -euo pipefail

VERSION="${1:?version label required}"
OUT="bin/Release/net10.0"
PKG="package/addons/counterstrikesharp"
PLUG="$PKG/plugins/AstraSkins"

rm -rf package
mkdir -p "$PLUG" "$PKG/gamedata" "$PKG/configs/plugins/AstraSkins"

# Plugin binaries: only the plugin itself and its own dependencies.
# CounterStrikeSharp.API and everything it pulls in is provided by the runtime.
cp "$OUT/AstraSkins.dll" "$OUT/AstraSkins.deps.json" \
   "$OUT/Microsoft.Data.Sqlite.dll" "$OUT/MySqlConnector.dll" \
   "$OUT"/SQLitePCLRaw.*.dll "$PLUG/"
cp -r "$OUT/data" "$OUT/lang" "$OUT/schema" "$PLUG/"

# Native SQLite for the two platforms CS2 dedicated servers run on.
for rid in win-x64 linux-x64; do
  mkdir -p "$PLUG/runtimes/$rid"
  cp -r "$OUT/runtimes/$rid/." "$PLUG/runtimes/$rid/"
done

cp gamedata/astra_skins.json "$PKG/gamedata/"
cp config.json "$PKG/configs/plugins/AstraSkins/AstraSkins.json"

ZIP="AstraSkins-${VERSION}.zip"
rm -f "$ZIP"
(cd package && zip -qr "../$ZIP" addons)
echo "$ZIP"
