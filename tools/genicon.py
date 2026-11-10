#!/usr/bin/env python3
"""Generate assets/tempbar.ico (multi-size, PNG compressed) - a white thermometer glyph."""
import os
import struct
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets")
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 8  # supersampling factor


def draw(size):
    w = size * SS
    img = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)

    cx = w / 2
    tube_w = w * 0.20
    tube_top = w * 0.11
    bulb_r = w * 0.185
    bulb_cy = w * 0.745
    stem_bottom = bulb_cy - bulb_r * 0.42

    # outer tube (rounded) + bulb
    d.rounded_rectangle(
        [cx - tube_w / 2, tube_top, cx + tube_w / 2, stem_bottom],
        radius=tube_w / 2, fill=white)
    d.ellipse([cx - bulb_r, bulb_cy - bulb_r, cx + bulb_r, bulb_cy + bulb_r], fill=white)

    # carve a hollow look: erase the tube interior so it reads as an outline
    inner_w = tube_w * 0.46
    d.rounded_rectangle(
        [cx - inner_w / 2, tube_top + tube_w * 0.55, cx + inner_w / 2, bulb_cy - bulb_r * 0.55],
        radius=inner_w / 2, fill=(0, 0, 0, 0))
    inner_bulb = bulb_r * 0.52
    d.ellipse([cx - inner_bulb, bulb_cy - inner_bulb, cx + inner_bulb, bulb_cy + inner_bulb], fill=(0, 0, 0, 0))

    # mercury column inside, filled from the bulb upwards
    mer_w = tube_w * 0.34
    d.rounded_rectangle(
        [cx - mer_w / 2, bulb_cy - bulb_r * 1.1, cx + mer_w / 2, bulb_cy + bulb_r * 0.55],
        radius=mer_w / 2, fill=white)
    d.ellipse([cx - inner_bulb * 0.62, bulb_cy - inner_bulb * 0.62,
               cx + inner_bulb * 0.62, bulb_cy + inner_bulb * 0.62], fill=white)

    # graduation ticks
    tick_w = max(SS, int(w * 0.055))
    for i in range(3):
        y = tube_top + w * (0.13 + i * 0.15)
        d.rounded_rectangle([cx + tube_w * 0.85, y - tick_w / 2, cx + tube_w * 2.15, y + tick_w / 2],
                            radius=tick_w / 2, fill=white)

    return img.resize((size, size), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    frames = []
    for s in SIZES:
        frames.append(draw(s))

    # ICO container with embedded PNG payloads (supported since Vista)
    pngs = []
    for f in frames:
        import io
        buf = io.BytesIO()
        f.save(buf, format="PNG", optimize=True)
        pngs.append(buf.getvalue())

    count = len(frames)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries = b""
    for img, png in zip(frames, pngs):
        s = img.width
        entries += struct.pack("<BBBBHHII", 0 if s >= 256 else s, 0 if s >= 256 else s,
                               0, 0, 1, 32, len(png), offset)
        offset += len(png)

    ico_path = os.path.join(OUT, "tempbar.ico")
    with open(ico_path, "wb") as fh:
        fh.write(header + entries + b"".join(pngs))
    print("wrote", ico_path, os.path.getsize(ico_path), "bytes")

    # a preview PNG for the README / quick inspection
    draw(128).save(os.path.join(OUT, "tempbar-preview.png"))


if __name__ == "__main__":
    main()
