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

# The Linux library from NuGet needs glibc 2.34; ship the one built by
# scripts/build_sqlite_linux.sh against glibc 2.28 instead.
NATIVE_SQLITE="native/linux-x64/libe_sqlite3.so"
if [ ! -f "$NATIVE_SQLITE" ]; then
  echo "missing $NATIVE_SQLITE: run scripts/build_sqlite_linux.sh first" >&2
  exit 1
fi
cp "$NATIVE_SQLITE" "$PLUG/runtimes/linux-x64/native/libe_sqlite3.so"

cp gamedata/astra_skins.json "$PKG/gamedata/"
cp config.json "$PKG/configs/plugins/AstraSkins/AstraSkins.json"

ZIP="AstraSkins-${VERSION}.zip"
rm -f "$ZIP"
(cd package && zip -qr "../$ZIP" addons)
echo "$ZIP"
