#!/usr/bin/env python3
"""Regenerate the Workshop thumbnail at workshop/MultiplayerColors/image.png.

Renders Ironclad's map-ink red under the mod's four actual sprite multipliers, so the store preview is a
truthful sample of the variations rather than a mock-up. Re-run whenever the strength dials in
src/PlayerTint.cs change:

    python3 scripts/make-thumbnail.py

Keep BRIGHTNESS_GAIN / CHANNEL_TILT below in step with the constants of the same name in PlayerTint.
Pure stdlib PNG writer - no PIL or ImageMagick needed.
"""
import os
import struct
import zlib

W, H = 250, 190
BG = (26, 22, 24)
BASE = (0xCB, 0x28, 0x2B)  # Ironclad MapDrawingColor

# Mirrors PlayerTint.BrightnessGain / PlayerTint.ChannelTilt.
BRIGHTNESS_GAIN = 1.20
CHANNEL_TILT = 1.28

VARIATIONS = [
    ("brighter", (BRIGHTNESS_GAIN,) * 3),
    ("darker", (1 / BRIGHTNESS_GAIN,) * 3),
    ("warmer", (CHANNEL_TILT, 1.0, 1 / CHANNEL_TILT)),
    ("cooler", (1 / CHANNEL_TILT, 1.0, CHANNEL_TILT)),
]

OUT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "workshop", "MultiplayerColors", "image.png",
)


def apply(color, mul):
    return tuple(min(255, max(0, round(c * m))) for c, m in zip(color, mul))


def chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def main():
    swatches = [apply(BASE, mul) for _, mul in VARIATIONS]

    pad_x, pad_y = 18, 34
    gap = 6
    band_w = (W - 2 * pad_x - gap * (len(swatches) - 1)) // len(swatches)
    band_h = H - 2 * pad_y

    rows = []
    for y in range(H):
        row = bytearray()
        for x in range(W):
            px = BG
            if pad_y <= y < pad_y + band_h:
                offset = x - pad_x
                if offset >= 0:
                    slot, within = divmod(offset, band_w + gap)
                    if slot < len(swatches) and within < band_w:
                        px = swatches[slot]
            row += bytes(px)
        rows.append(row)

    raw = b"".join(b"\x00" + bytes(r) for r in rows)
    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))

    with open(OUT, "wb") as f:
        f.write(png)

    print(f"wrote {OUT} ({len(png)} bytes)")
    for (name, _), s in zip(VARIATIONS, swatches):
        print(f"  {name:9s} #{s[0]:02X}{s[1]:02X}{s[2]:02X}")


if __name__ == "__main__":
    main()
