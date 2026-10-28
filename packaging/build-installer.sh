#!/usr/bin/env bash
#
# Publishes Elite Dangerous Companion for Windows and packages it as an installer, on Linux or on
# Windows (Git Bash, as in the GitHub release workflow): WPF cross-compiles from the NuGet targeting
# packs, and makensis builds the installer.
#
# Usage: packaging/build-installer.sh <version> [output-dir]
set -euo pipefail

VERSION="${1:?usage: build-installer.sh <version> [output-dir]}"
OUT_DIR="${2:-artifacts}"

PROJECT="src/EDC/EDC.csproj"
PUBLISH_DIR="artifacts/publish"
ICON="src/EDC/Assets/app.ico"
SETUP_NAME="EliteDangerousCompanion-Setup-${VERSION}.exe"

cd "$(dirname "$0")/.."
# Windows programs (dotnet, makensis) want C:/... where Git Bash says /c/...
native() { if command -v cygpath >/dev/null; then cygpath -m "$1"; else printf '%s' "$1"; fi; }
# makensis on Windows finds nothing under a "File /r" wildcard written with forward slashes.
backslashed() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
ROOT="$(native "$PWD")"
mkdir -p "$OUT_DIR"
OUT_DIR="$(native "$(cd "$OUT_DIR" && pwd)")"
SETUP="$OUT_DIR/$SETUP_NAME"

echo ">>> Publishing $VERSION"
rm -rf "$PUBLISH_DIR"
dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime win-x64 \
    --self-contained false \
    -p:EnableWindowsTargeting=true \
    -p:Version="$VERSION" \
    -p:PublishDir="$ROOT/$PUBLISH_DIR/"

FILE_COUNT="$(find "$PUBLISH_DIR" -type f | wc -l)"

echo ">>> Packaging $SETUP_NAME from $FILE_COUNT files"
# -V4 names every file taken in: a solid LZMA block hides even the file names, so this log is the
# only account of what went in, and the checks below read it.
makensis -V4 \
    "-DVERSION=$VERSION" \
    "-DSOURCE_DIR=$(backslashed "$ROOT/$PUBLISH_DIR")" \
    "-DICON=$ROOT/$ICON" \
    "-DOUT_FILE=$SETUP" \
    packaging/EDC.nsi \
    | tee "$OUT_DIR/makensis.log"

echo ">>> Checking the installer"
fail() {
    echo "    FAIL $1" >&2
    exit 1
}

[ -s "$SETUP" ] || fail "$SETUP_NAME was not produced"

setup_version="$(perl packaging/pe-version.pl "$SETUP")"
if [ "$setup_version" = "$VERSION.0" ]; then
    echo "    ok   installer version: $setup_version"
else
    fail "installer version is '$setup_version', expected '$VERSION.0'"
fi

# The executable must carry the same number: it is what the window shows.
exe_version="$(perl packaging/pe-version.pl "$PUBLISH_DIR/EDC.exe")"
if [ "$exe_version" = "$VERSION.0" ]; then
    echo "    ok   EDC.exe version: $exe_version"
else
    fail "EDC.exe version is '$exe_version', expected '$VERSION.0'"
fi

# Counted, not sampled: a file quietly missing from the package would only show as a slightly
# smaller installer. The NSIS plugin lines carry an arrow, the published files do not.
packed="$(grep -cE '^File: "[^"]+" [0-9]+ bytes$' "$OUT_DIR/makensis.log" || true)"
if [ "$packed" -eq "$FILE_COUNT" ]; then
    echo "    ok   files packed: $packed, one per published file"
else
    fail "$packed files packed, $FILE_COUNT were published"
fi

grep -qE '^File: "EDC\.exe" ' "$OUT_DIR/makensis.log" \
    || fail "EDC.exe is not in the package"
echo "    ok   EDC.exe is in the package"

rm -f "$OUT_DIR/makensis.log"
ls -l "$SETUP"
