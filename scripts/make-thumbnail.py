#!/usr/bin/env python3
"""Regenerate the Workshop thumbnail at workshop/MultiplayerColors/image.png.

Builds a grid: one row per base-game character (their compendium filter face), one column per variation
(cool / hot / light / dark). Each cell is that character's own icon under the mod's real sprite multiplier,
sitting on that character's real map-ink colour for the same variation - so the preview is a genuine sample
of the output rather than a mock-up.

    python3 scripts/make-thumbnail.py

Needs macOS (uses qlmanage to rasterise HTML, so the emoji come from the system font) and the game
installed. Character icons are extracted from the shipped pack on first run into a temp dir - they are
deliberately NOT committed to this repo.

Keep MULTIPLIERS and INKS below in step with PlayerTint; re-dump them with a scratch xUnit test if the
tuning changes. Values are as of v0.1.9.
"""
import base64
import os
import shutil
import subprocess
import sys
import tempfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, "workshop", "MultiplayerColors", "image.png")
GODOT = os.path.expanduser("~/Applications/MegaDot.app/Contents/MacOS/Godot")
PCK = os.path.expanduser(
    "~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2"
    "/SlayTheSpire2.app/Contents/Resources/Slay the Spire 2.pck")

CHARACTERS = ["ironclad", "silent", "defect", "necrobinder", "regent"]

# Columns, in the order they appear. (key, emoji, PlayerVariation name)
COLUMNS = [
    ("cool", "\N{SNOWFLAKE}\N{VARIATION SELECTOR-16}", "Cooler"),
    ("hot", "\N{FIRE}", "Warmer"),
    ("light", "\N{BLACK SUN WITH RAYS}\N{VARIATION SELECTOR-16}", "Brighter"),
    ("dark", "\N{CRESCENT MOON}", "Darker"),
]

# PlayerTint.Modulate - the per-channel sprite multipliers.
MULTIPLIERS = {
    "Brighter": (1.2000, 1.2000, 1.2000),
    "Darker": (0.8333, 0.8333, 0.8333),
    "Warmer": (1.2800, 1.0000, 0.7812),
    "Cooler": (0.7812, 1.0000, 1.2800),
}

# PlayerTint.MapInkFor - each character's real map-ink colour per variation.
INKS = {
    "ironclad": {"Brighter": "f02f33", "Darker": "a62123", "Warmer": "cb4628", "Cooler": "cb284c"},
    "silent": {"Brighter": "3a8033", "Darker": "244e1f", "Warmer": "5c6729", "Cooler": "296750"},
    "defect": {"Brighter": "1078aa", "Darker": "0a4e6e", "Warmer": "0d6e8c", "Cooler": "0d588c"},
    "necrobinder": {"Brighter": "cd05a0", "Darker": "8b036c", "Warmer": "ac0470", "Cooler": "ac049c"},
    "regent": {"Brighter": "f1870a", "Darker": "754105", "Warmer": "934406", "Cooler": "936006"},
}

EXTRACT_GD = """extends SceneTree
func _initialize():
    for id in %s:
        var p = "res://images/ui/top_panel/character_icon_%%s.png" %% id
        if not ResourceLoader.exists(p):
            print("MISSING ", p); continue
        var img: Image = load(p).get_image()
        img.decompress(); img.convert(Image.FORMAT_RGBA8)
        img.save_png("%s/" + id + ".png")
        print("SAVED ", id)
    quit()
"""


def extract_icons(into):
    """Pull the character face icons out of the shipped .pck via headless Godot."""
    for tool, path in (("MegaDot", GODOT), ("game pack", PCK)):
        if not os.path.exists(path):
            sys.exit(f"{tool} not found at {path}")

    script = os.path.join(into, "extract.gd")
    with open(script, "w") as f:
        f.write(EXTRACT_GD % (CHARACTERS, into))

    subprocess.run([GODOT, "--headless", "--main-pack", PCK, "--script", script],
                   capture_output=True, check=False)

    missing = [c for c in CHARACTERS if not os.path.exists(os.path.join(into, c + ".png"))]
    if missing:
        sys.exit(f"failed to extract icons: {', '.join(missing)}")


def data_uri(path):
    with open(path, "rb") as f:
        return "data:image/png;base64," + base64.b64encode(f.read()).decode()


def build_html(icons):
    # Per-channel multiply, matching Godot's Modulate. color-interpolation-filters="sRGB" matters: SVG
    # filters default to linearRGB, which would tint noticeably differently from the game.
    filters = "".join(
        f'<filter id="f_{key}" color-interpolation-filters="sRGB">'
        f'<feColorMatrix type="matrix" values="'
        f'{MULTIPLIERS[var][0]} 0 0 0 0  0 {MULTIPLIERS[var][1]} 0 0 0  '
        f'0 0 {MULTIPLIERS[var][2]} 0 0  0 0 0 1 0"/></filter>'
        for key, _, var in COLUMNS)

    head = "".join(f'<th><span class="e">{emoji}</span><br><span class="l">{key}</span></th>'
                   for key, emoji, _ in COLUMNS)

    rows = ""
    for c in CHARACTERS:
        cells = "".join(
            f'<td style="background:#{INKS[c][var]}">'
            f'<img src="{icons[c]}" style="filter:url(#f_{key})">'
            f'</td>'
            for key, _, var in COLUMNS)
        rows += f'<tr><th class="ch"><img src="{icons[c]}"></th>{cells}</tr>'

    # Everything is sized in vw rather than px: qlmanage rasterises at a viewport of its own choosing and
    # then scales the result to a square, so a fixed-pixel layout ends up small in one corner. Viewport
    # units make the design fill whatever it is given. 1vw here == 6px in the 600px output.
    return f"""<!doctype html><html><head><meta charset="utf-8"><style>
html,body{{margin:0;width:100vw;height:100vh;background:#17141a;overflow:hidden;
  display:flex;flex-direction:column;justify-content:center;align-items:center;
  font-family:-apple-system,"Helvetica Neue",sans-serif;-webkit-font-smoothing:antialiased}}
h1{{margin:0;font-size:4.4vw;color:#f4ead8;letter-spacing:.05vw}}
p{{margin:1vw 0 2.2vw;font-size:2.2vw;color:#a79cb0}}
table{{border-collapse:separate;border-spacing:1vw}}
th,td{{width:15vw;height:11vw;text-align:center;vertical-align:middle}}
th.ch{{width:12.5vw;background:#241f2b;border-radius:1.5vw}}
td{{border-radius:1.5vw;box-shadow:inset 0 0 0 .17vw rgba(0,0,0,.3)}}
img{{width:8.5vw;height:8.5vw;vertical-align:middle;
  filter:drop-shadow(0 .17vw .34vw rgba(0,0,0,.55))}}
th.ch img{{width:7.5vw;height:7.5vw}}
.e{{font-size:3.8vw;line-height:1.1}}
.l{{font-size:1.6vw;color:#a79cb0;letter-spacing:.25vw;text-transform:uppercase}}
.foot{{margin:2.2vw 0 0;font-size:1.95vw;color:#7d7386}}
</style></head><body>
<svg width="0" height="0" style="position:absolute">{filters}</svg>
<h1>Multiplayer Colors</h1>
<p>four shifts per character, so players sharing one stay tellable apart</p>
<table><tr><th></th>{head}</tr>{rows}</table>
<p class="foot">character art &amp; map ink both shift &nbsp;·&nbsp; works on modded characters too</p>
</body></html>"""


def main():
    tmp = os.path.join(tempfile.gettempdir(), "mpcolors-thumb-icons")
    os.makedirs(tmp, exist_ok=True)
    if any(not os.path.exists(os.path.join(tmp, c + ".png")) for c in CHARACTERS):
        print("extracting character icons from the game pack...")
        extract_icons(tmp)

    icons = {c: data_uri(os.path.join(tmp, c + ".png")) for c in CHARACTERS}

    work = tempfile.mkdtemp(prefix="mpcolors-thumb-")
    try:
        page = os.path.join(work, "thumb.html")
        with open(page, "w") as f:
            f.write(build_html(icons))

        subprocess.run(["qlmanage", "-t", "-s", "1200", "-o", work, page],
                       capture_output=True, check=False)

        rendered = os.path.join(work, "thumb.html.png")
        if not os.path.exists(rendered):
            sys.exit("qlmanage did not produce a PNG")

        # Render at 2x for crispness, then halve to the layout's real size.
        subprocess.run(["sips", "-Z", "600", rendered], capture_output=True, check=False)
        shutil.copy(rendered, OUT)
        size = subprocess.run(["sips", "-g", "pixelWidth", "-g", "pixelHeight", OUT],
                              capture_output=True, text=True).stdout
        print(f"wrote {OUT} ({os.path.getsize(OUT)} bytes)")
        print("  " + " ".join(size.split()[-4:]))
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    main()
