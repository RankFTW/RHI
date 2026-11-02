"""
Generates the tool icons (same family as RHI's neon-hexagon icon).
Run:  python tools/Shared/icons/make_icons.py
Writes <name>.ico (16-256 px, one frame per size) and <name>.png (256 px preview) next to this script.
Needs Pillow and the Segoe Fluent Icons font (Windows 11).
"""
import math
import os
import struct
from io import BytesIO

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ICON_FONT = r"C:\Windows\Fonts\SegoeIcons.ttf"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# name: (glyph, gradient start, gradient end)
TOOLS = {
    "manifest-editor": ("\uF0E3", (62, 230, 255), (79, 107, 255)),   # checklist, cyan to blue
    "renodxdb-editor": ("\uEE94", (92, 242, 154), (31, 184, 201)),   # database, green to teal
    "pcgw-editor":     ("\uE774", (255, 201, 60), (255, 94, 58)),    # globe, amber to orange
    "rhi-stats":       ("\uE9D2", (255, 94, 196), (139, 92, 255)),   # chart, pink to purple
}


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def gradient(size, c1, c2):
    """Diagonal gradient, top-left c1 to bottom-right c2."""
    g = Image.new("RGB", (size, size))
    px = g.load()
    for y in range(size):
        for x in range(size):
            px[x, y] = lerp(c1, c2, (x + y) / (2 * size - 2))
    return g


def hexagon(cx, cy, r):
    # Pointy-top, like RHI's
    return [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a)))
            for a in range(-90, 270, 60)]


def render(glyph, c1, c2, out):
    S = 1024                      # draw big, then scale down per size
    small = out <= 24             # tiny sizes: bolder strokes, no glow
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # Dark rounded tile
    tile = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    inset = int(S * 0.04)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([inset, inset, S - inset, S - inset], radius=int(S * 0.2), fill=255)
    bg = Image.new("RGBA", (S, S))
    bpx = bg.load()
    for y in range(S):
        c = lerp((34, 34, 44), (12, 12, 16), y / (S - 1))
        for x in range(S):
            bpx[x, y] = c + (255,)
    tile.paste(bg, (0, 0), mask)
    img.alpha_composite(tile)
    ImageDraw.Draw(img).rounded_rectangle([inset, inset, S - inset, S - inset], radius=int(S * 0.2),
                                          outline=(255, 255, 255, 28), width=int(S * 0.008))

    grad = gradient(S, c1, c2).convert("RGBA")

    # Neon hexagon outline (gradient through a stroke mask), with a soft glow
    stroke = int(S * (0.075 if small else 0.045))
    hmask = Image.new("L", (S, S), 0)
    pts = hexagon(S / 2, S / 2, S * (0.40 if small else 0.37))
    ImageDraw.Draw(hmask).line(pts + [pts[0]], fill=255, width=stroke, joint="curve")
    if not small:
        glow = hmask.filter(ImageFilter.GaussianBlur(S * 0.03)).point(lambda v: int(v * 0.85))
        img.paste(grad, (0, 0), glow)
    img.paste(grad, (0, 0), hmask)

    # Glyph, white with a faint tinted glow
    font = ImageFont.truetype(ICON_FONT, int(S * (0.46 if small else 0.40)))
    gmask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(gmask).text((S / 2, S / 2), glyph, font=font, fill=255, anchor="mm")
    if small:
        gmask = gmask.filter(ImageFilter.MaxFilter(9))   # thicken so it survives 16 px
    else:
        gglow = gmask.filter(ImageFilter.GaussianBlur(S * 0.02)).point(lambda v: int(v * 0.6))
        img.paste(Image.new("RGBA", (S, S), lerp(c1, c2, 0.5) + (255,)), (0, 0), gglow)
    img.paste(Image.new("RGBA", (S, S), (255, 255, 255, 255)), (0, 0), gmask)

    return img.resize((out, out), Image.LANCZOS)


def write_ico(path, frames):
    """ICO with one PNG-compressed frame per size (Vista+)."""
    blobs = []
    for im in frames:
        b = BytesIO()
        im.save(b, "PNG")
        blobs.append(b.getvalue())
    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries = b""
    for im, data in zip(frames, blobs):
        w = im.width if im.width < 256 else 0
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(path, "wb") as f:
        f.write(header + entries + b"".join(blobs))


if __name__ == "__main__":
    for name, (glyph, c1, c2) in TOOLS.items():
        frames = [render(glyph, c1, c2, s) for s in SIZES]
        write_ico(os.path.join(HERE, name + ".ico"), frames)
        frames[-1].save(os.path.join(HERE, name + ".png"))
        print("wrote", name)
