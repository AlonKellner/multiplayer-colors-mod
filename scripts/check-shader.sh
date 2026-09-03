#!/bin/bash
# Compile every shader this mod ships with the real Godot shader parser and fail if one errors.
#
# The xUnit suite can only check the shader SOURCE as a string — it has no engine. A shader that fails to
# compile does not crash anything: Godot logs an error and draws the node normally, so the effect silently
# renders as untouched art. That is exactly how "MODULATE" (not a canvas_item built-in in this engine
# version) got as far as being written. This catches that class of mistake.
#
# The uniform list is not hard-coded either. Every `uniform` the source declares is compared against what
# Godot actually parsed, so a uniform that the C# sets by string name but the parser dropped is caught too.
#
# Usage: scripts/check-shader.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="$HOME/Applications/MegaDot.app/Contents/MacOS/Godot"

[ -x "$GODOT" ] || { echo "MegaDot not found at $GODOT" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

status=0

for SRC in "$ROOT"/src/*Shader.cs; do
  NAME="$(basename "$SRC" .cs)"

  # Pull the shader source out of the raw string literal, so this checks what ships rather than a copy
  # that can drift.
  python3 - "$SRC" "$WORK/$NAME.gdshader" "$WORK/$NAME.uniforms" <<'PY'
import re, sys
src = open(sys.argv[1]).read()
m = re.search(r'public const string Code = """\n(.*?)\n\s*""";', src, re.S)
if not m:
    sys.exit(f"could not find the shader source in {sys.argv[1]}")
body = m.group(1)
# Raw string literals strip the indentation of the closing delimiter.
indent = min((len(l) - len(l.lstrip())) for l in body.splitlines() if l.strip())
code = "\n".join(l[indent:] for l in body.splitlines()) + "\n"
open(sys.argv[2], "w").write(code)
open(sys.argv[3], "w").write("\n".join(re.findall(r'^uniform\s+\S+\s+(\w+)', code, re.M)) + "\n")
PY

  cat > "$WORK/check.gd" <<GD
extends SceneTree
func _initialize():
    var code := FileAccess.open("$WORK/$NAME.gdshader", FileAccess.READ).get_as_text()
    var sh := Shader.new()
    sh.code = code
    var names := []
    for u in sh.get_shader_uniform_list():
        names.append(u["name"])
    print("UNIFORMS ", names)
    quit()
GD

  OUT="$("$GODOT" --headless --script "$WORK/check.gd" 2>&1 || true)"

  if grep -q 'SHADER ERROR' <<<"$OUT"; then
    echo "FAIL: $NAME does not compile" >&2
    grep 'SHADER ERROR' <<<"$OUT" >&2
    status=1
    continue
  fi

  PARSED="$(grep 'UNIFORMS' <<<"$OUT")"
  missing=""
  while read -r u; do
    [ -n "$u" ] || continue
    grep -q "\"$u\"" <<<"$PARSED" || missing="$missing $u"
  done < "$WORK/$NAME.uniforms"

  if [ -n "$missing" ]; then
    echo "FAIL: $NAME declares uniform(s)$missing that Godot did not parse" >&2
    echo "$PARSED" >&2
    status=1
    continue
  fi

  echo "OK: $NAME compiles and exposes $(tr '\n' ' ' < "$WORK/$NAME.uniforms")"
done

exit $status
