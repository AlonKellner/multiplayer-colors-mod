#!/bin/bash
# Compile the outline shader with the real Godot shader parser and fail if it errors.
#
# The xUnit suite can only check the shader SOURCE as a string — it has no engine. A shader that fails to
# compile does not crash anything: Godot logs an error and draws the node normally, so the outline silently
# renders as untouched art. That is exactly how "MODULATE" (not a canvas_item built-in in this engine
# version) got as far as being written. This catches that class of mistake.
#
# Usage: scripts/check-shader.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="$HOME/Applications/MegaDot.app/Contents/MacOS/Godot"
SRC="$ROOT/src/OutlineShader.cs"

[ -x "$GODOT" ] || { echo "MegaDot not found at $GODOT" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Pull the shader source out of the raw string literal in OutlineShader.cs, so this checks what ships
# rather than a copy that can drift.
python3 - "$SRC" "$WORK/shader.gdshader" <<'PY'
import re, sys
src = open(sys.argv[1]).read()
m = re.search(r'public const string Code = """\n(.*?)\n\s*""";', src, re.S)
if not m:
    sys.exit("could not find the shader source in OutlineShader.cs")
body = m.group(1)
# Raw string literals strip the indentation of the closing delimiter.
indent = min((len(l) - len(l.lstrip())) for l in body.splitlines() if l.strip())
open(sys.argv[2], "w").write("\n".join(l[indent:] for l in body.splitlines()) + "\n")
PY

cat > "$WORK/check.gd" <<GD
extends SceneTree
func _initialize():
    var code := FileAccess.open("$WORK/shader.gdshader", FileAccess.READ).get_as_text()
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
  echo "FAIL: shader does not compile" >&2
  grep 'SHADER ERROR' <<<"$OUT" >&2
  exit 1
fi

if ! grep -q 'UNIFORMS \["outline_color"\]' <<<"$OUT"; then
  echo "FAIL: outline_color uniform did not parse — the C# sets it by that name" >&2
  grep 'UNIFORMS' <<<"$OUT" >&2
  exit 1
fi

echo "OK: shader compiles and exposes outline_color"
