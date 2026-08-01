#!/bin/bash
# Build and upload Multiplayer Colors to the Steam Workshop in one command.
#
# Usage:
#   scripts/publish-workshop.sh                 # test -> build -> assemble -> upload
#   scripts/publish-workshop.sh "change note"   # also update workshop.json changeNote first
#   SKIP_TESTS=1 scripts/publish-workshop.sh    # skip the test gate
#   MOD_UPLOADER=/path/to/dir scripts/...       # override where the ModUploader binary lives
#
# Requires Steam to be running and logged in (the ModUploader talks to Steamworks).
#
# Unlike the sibling the-apprentice-mod, this is a DLL-only mod: no PCK, so there is no MegaDot
# export step and `dotnet build` produces everything that ships.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

DOTNET="$HOME/.dotnet/dotnet"
WS="$ROOT/workshop/MultiplayerColors"
CONTENT="$WS/content"
UPLOADER="${MOD_UPLOADER:-$ROOT/../the-apprentice-mod/tools/mod-uploader}"
MANIFEST="$ROOT/MultiplayerColors.json"
DLL="$ROOT/bin/Debug/net9.0/MultiplayerColors.dll"

step() { printf '\n\033[1;36m==> %s\033[0m\n' "$1"; }
die()  { printf '\033[1;31mERROR: %s\033[0m\n' "$1" >&2; exit 1; }

# 0. Preconditions -----------------------------------------------------------
[ -x "$DOTNET" ] || die "dotnet not found at $DOTNET"
pgrep -x steam_osx >/dev/null || die "Steam is not running/logged in — start Steam first."
[ -x "$UPLOADER/ModUploader" ] || die "ModUploader missing at $UPLOADER/ModUploader (see workshop/README.md, or set MOD_UPLOADER)."
xattr -dr com.apple.quarantine "$UPLOADER" 2>/dev/null || true

# Optional changeNote update (first arg) -------------------------------------
if [ "${1:-}" != "" ]; then
  step "Updating workshop changeNote"
  NOTE="$1" perl -pi -e 's/("changeNote":\s*")(?:[^"\\]|\\.)*(")/$1.$ENV{NOTE}.$2/e' "$WS/workshop.json"
fi

# 1. Test gate ---------------------------------------------------------------
if [ "${SKIP_TESTS:-0}" != "1" ]; then
  step "Running tests"
  "$DOTNET" test "$ROOT/tests/MultiplayerColors.Tests.csproj" --nologo -clp:ErrorsOnly \
    || die "tests failed — aborting upload."
fi

# 2. Build the shipping DLL --------------------------------------------------
# CopyToModsFolder=false so a publish run doesn't also stomp the local dev install.
step "Building shipping DLL"
"$DOTNET" build "$ROOT/MultiplayerColors.csproj" --nologo -clp:ErrorsOnly -p:CopyToModsFolder=false \
  || die "DLL build failed."
[ -f "$DLL" ] || die "no DLL produced at $DLL"
[ "$DLL" -nt "$ROOT/src/PlayerTint.cs" ] || die "DLL is older than src/ — the build did not run."

# 3. Assemble content/ -------------------------------------------------------
step "Assembling $CONTENT"
mkdir -p "$CONTENT"
cp "$DLL"      "$CONTENT/MultiplayerColors.dll"
cp "$MANIFEST" "$CONTENT/MultiplayerColors.json"
printf '   version %s\n' "$(grep -o '"version": *"[^"]*"' "$MANIFEST")"

# 4. Upload ------------------------------------------------------------------
# mod_id.txt in the workspace makes this update the SAME item; without it a new item is created.
step "Uploading to Steam Workshop (item $(cat "$WS/mod_id.txt" 2>/dev/null || echo 'NEW'))"
cd "$UPLOADER"
./ModUploader upload -w "$WS"

step "Done."
