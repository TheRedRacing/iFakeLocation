#!/usr/bin/env bash
# Freezes pymobiledevice3 into a standalone "pmd3" executable via PyInstaller and drops it into
# iFakeLocation/pmd3-dist/<rid>/, where the backend's csproj picks it up for that RID's publish.
#
# IMPORTANT: PyInstaller cannot cross-compile. This must be run ON a machine whose OS+architecture
# matches the RID you're building for -- e.g. to produce a genuine osx-x64 build (our chosen RID,
# see ARCHITECTURE.md) on Apple Silicon, run this under an x86_64 Python via Rosetta
# (`arch -x86_64 python3 -m venv ...`), not the native arm64 interpreter. Running it as-is just
# uses whatever Python is on PATH and labels the output with the RID you pass in -- it does NOT
# verify the two actually match.
#
# Usage: ./build.sh <rid>   (e.g. ./build.sh osx-x64)
set -euo pipefail

RID="${1:?Usage: build.sh <rid>, e.g. osx-x64, linux-x64}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SCRIPT_DIR/../iFakeLocation/pmd3-dist/$RID"
VENV_DIR="$SCRIPT_DIR/.venv-$RID"
BUILD_DIR="$SCRIPT_DIR/.build-$RID"

echo "Building pmd3 for RID=$RID using $(python3 --version) ($(python3 -c 'import platform; print(platform.machine())'))"

python3 -m venv "$VENV_DIR"
"$VENV_DIR/bin/pip" install -q --upgrade pip
"$VENV_DIR/bin/pip" install -q -r "$SCRIPT_DIR/requirements.txt"

rm -rf "$BUILD_DIR" "$OUT_DIR"
mkdir -p "$OUT_DIR"

"$VENV_DIR/bin/pyinstaller" \
  --onefile \
  --name pmd3 \
  --distpath "$OUT_DIR" \
  --workpath "$BUILD_DIR/build" \
  --specpath "$BUILD_DIR" \
  --copy-metadata pymobiledevice3 \
  --copy-metadata ipsw-parser \
  --copy-metadata readchar \
  --hidden-import ipsw_parser \
  --hidden-import pyimg4 \
  --hidden-import apple_compress \
  --hidden-import readchar \
  --collect-submodules pymobiledevice3 \
  "$SCRIPT_DIR/entry.py"

# PyInstaller names the output after the entry script's stem by default when not using --name
# consistently across platforms; make sure it's exactly "pmd3" regardless.
if [ -f "$OUT_DIR/entry" ] && [ ! -f "$OUT_DIR/pmd3" ]; then
  mv "$OUT_DIR/entry" "$OUT_DIR/pmd3"
fi

echo "Built: $OUT_DIR/pmd3"
"$OUT_DIR/pmd3" version || echo "(warning: smoke-test invocation failed -- inspect the build above)"
