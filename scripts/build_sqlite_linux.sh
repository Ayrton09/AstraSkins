#!/usr/bin/env bash
# Builds the Linux x64 SQLite library shipped in the release zip.
#
# The library in the SQLitePCLRaw NuGet package is linked against glibc 2.34,
# which older CS2 host images (Debian 11, Ubuntu 20.04) do not have. This build
# uses the same SQLite options as that package (e_sqlite3) but targets glibc
# 2.28, older than what CS2 itself needs, so it loads on every host that can
# run the server at all.
#
# Usage: scripts/build_sqlite_linux.sh [output-dir]   (default: native/linux-x64)
# Needs curl, python3 and tar; zig is downloaded into the work directory.
set -euo pipefail

SQLITE_ZIP="2026/sqlite-amalgamation-3530400.zip"   # SQLite 3.53.4
SQLITE_SHA3="628a44cfe82c66aed1ccbbe85a562d2e33ebe64b3288981ed76285612227934e"
ZIG_VERSION="0.15.2"
GLIBC_TARGET="2.28"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/native/linux-x64}"
WORK="${ASTRA_NATIVE_WORK:-${TMPDIR:-/tmp}/astra-sqlite-build}"
mkdir -p "$WORK" "$OUT"
cd "$WORK"

# SQLite amalgamation, checked against the hash published on sqlite.org.
if [ ! -f sqlite.zip ]; then
  curl -sSL -o sqlite.zip "https://sqlite.org/$SQLITE_ZIP"
fi
python3 - "$SQLITE_SHA3" <<'PY'
import hashlib, sys, zipfile
data = open("sqlite.zip", "rb").read()
digest = hashlib.sha3_256(data).hexdigest()
if digest != sys.argv[1]:
    raise SystemExit("sqlite.zip hash mismatch: " + digest)
zipfile.ZipFile("sqlite.zip").extractall(".")
PY
SRC="$(ls -d sqlite-amalgamation-*/ | head -1)"

# zig cc cross-compiles against a chosen glibc version without a sysroot.
ZIG_DIR="zig-x86_64-linux-$ZIG_VERSION"
if [ ! -x "$ZIG_DIR/zig" ]; then
  curl -sSL -o zig.tar.xz "https://ziglang.org/download/$ZIG_VERSION/$ZIG_DIR.tar.xz"
  tar xJf zig.tar.xz
fi

"$ZIG_DIR/zig" cc -target "x86_64-linux-gnu.$GLIBC_TARGET" -shared -fPIC -O2 -Wl,--strip-all \
  -DNDEBUG -DSQLITE_OS_UNIX \
  -DSQLITE_ENABLE_COLUMN_METADATA -DSQLITE_ENABLE_FTS3_PARENTHESIS -DSQLITE_ENABLE_FTS4 -DSQLITE_ENABLE_FTS5 \
  -DSQLITE_ENABLE_JSON1 -DSQLITE_ENABLE_MATH_FUNCTIONS -DSQLITE_ENABLE_RTREE -DSQLITE_ENABLE_SNAPSHOT \
  -DSQLITE_DEFAULT_FOREIGN_KEYS=1 \
  -o "$OUT/libe_sqlite3.so" "$SRC/sqlite3.c" "$ROOT/native/sqlite/stubs.c" -lm -lpthread -ldl

# Check the result: glibc symbols no newer than the target, SQLite entry points present.
python3 - "$OUT/libe_sqlite3.so" "$GLIBC_TARGET" <<'PY'
import re, sys
data = open(sys.argv[1], "rb").read()
versions = sorted({m.decode() for m in re.findall(rb"GLIBC_2\.\d+", data)}, key=lambda v: int(v.split(".")[1]))
print("glibc symbol versions:", ", ".join(versions) or "none")
if versions and int(versions[-1].split(".")[1]) > int(sys.argv[2].split(".")[1]):
    raise SystemExit("library requires " + versions[-1] + ", newer than the target glibc " + sys.argv[2])
for symbol in (b"sqlite3_libversion_number", b"sqlite3_open_v2", b"sqlite3_key_v2", b"sqlite3_snapshot_get", b"sqlite3_column_database_name"):
    if symbol not in data:
        raise SystemExit("missing export: " + symbol.decode())
print("ok:", sys.argv[1], len(data), "bytes")
PY
