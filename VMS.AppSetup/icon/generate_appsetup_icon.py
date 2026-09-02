"""Generate the AppSetup (system configuration wizard) icon.

Concept: dark navy rounded square (same family as VisionSetup / LicGen icons)
+ gold gear (AppSetup card headers use #FFD700) with an indigo check mark in the
hub — "configure and confirm". Rendered at 4x and downsampled for anti-aliasing.

Run:  python generate_appsetup_icon.py   (requires Pillow)
"""
from PIL import Image, ImageDraw
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
SS = 4                      # supersampling factor
SIZE = 256


def gear_polygon(cx, cy, r_out, r_in, teeth, tooth_frac=0.5, rot=0.0):
    """Polygon points for a gear: `teeth` teeth, each spanning tooth_frac of its pitch."""
    pts = []
    pitch = 2 * math.pi / teeth
    half_tooth = pitch * tooth_frac / 2
    # flank width (angular) so teeth are trapezoidal, not square
    flank = pitch * 0.08
    for i in range(teeth):
        a = rot + i * pitch
        # root before tooth
        pts.append((cx + r_in * math.cos(a - half_tooth - flank), cy + r_in * math.sin(a - half_tooth - flank)))
        # tooth top (narrower)
        pts.append((cx + r_out * math.cos(a - half_tooth), cy + r_out * math.sin(a - half_tooth)))
        pts.append((cx + r_out * math.cos(a + half_tooth), cy + r_out * math.sin(a + half_tooth)))
        # root after tooth
        pts.append((cx + r_in * math.cos(a + half_tooth + flank), cy + r_in * math.sin(a + half_tooth + flank)))
    return pts


def generate_icon():
    size = SIZE * SS
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    margin = 12 * SS
    radius = 40 * SS
    cx = cy = size // 2

    # Background tile — navy (#0F172A family) with indigo (#6366F1) rim
    draw.rounded_rectangle([margin, margin, size - margin, size - margin],
                           radius=radius, fill=(15, 23, 42, 255))
    draw.rounded_rectangle([margin, margin, size - margin, size - margin],
                           radius=radius, outline=(99, 102, 241, 150), width=3 * SS)

    # Soft indigo glow behind the gear — composited on a separate layer
    # (ImageDraw replaces pixels instead of blending, which would punch translucent
    # holes into the navy tile).
    glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for i in range(12, 0, -1):
        alpha = int(14 * (1 - i / 12)) + 4
        r = 100 * SS + i * 3 * SS
        gd.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(99, 102, 241, alpha))
    # clip the glow to the tile
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([margin, margin, size - margin, size - margin],
                                           radius=radius, fill=255)
    glow.putalpha(Image.composite(glow.getchannel("A"), Image.new("L", (size, size), 0), mask))
    img = Image.alpha_composite(img, glow)
    draw = ImageDraw.Draw(img)

    # Gear — gold body with a slightly darker gold edge
    gold = (255, 215, 0, 255)
    gold_dark = (214, 170, 0, 255)
    r_out, r_in, teeth = 92 * SS, 74 * SS, 9
    rot = -math.pi / 2  # a tooth points straight up
    draw.polygon(gear_polygon(cx, cy, r_out, r_in, teeth, rot=rot), fill=gold_dark)
    draw.polygon(gear_polygon(cx, cy, r_out - 4 * SS, r_in - 4 * SS, teeth, rot=rot), fill=gold)

    # Hub — dark disc with thin indigo ring
    r_hub = 46 * SS
    draw.ellipse([cx - r_hub, cy - r_hub, cx + r_hub, cy + r_hub], fill=(15, 23, 42, 255))
    draw.ellipse([cx - r_hub, cy - r_hub, cx + r_hub, cy + r_hub],
                 outline=(99, 102, 241, 220), width=3 * SS)

    # Check mark inside the hub — indigo-light (#A5B4FC) so it reads on navy
    check = (165, 180, 252, 255)
    w = 9 * SS
    p1 = (cx - 22 * SS, cy + 2 * SS)
    p2 = (cx - 6 * SS, cy + 18 * SS)
    p3 = (cx + 24 * SS, cy - 16 * SS)
    draw.line([p1, p2], fill=check, width=w)
    draw.line([p2, p3], fill=check, width=w)
    for p in (p1, p2, p3):  # round the stroke joints/ends
        draw.ellipse([p[0] - w // 2, p[1] - w // 2, p[0] + w // 2, p[1] + w // 2], fill=check)

    base = img.resize((SIZE, SIZE), Image.LANCZOS)

    sizes_list = [16, 24, 32, 48, 64, 128, 256]
    icon_images = [base.resize((s, s), Image.LANCZOS) if s != SIZE else base for s in sizes_list]

    ico_path = os.path.join(HERE, "AppSetup.ico")
    icon_images[0].save(ico_path, format="ICO",
                        sizes=[(s, s) for s in sizes_list],
                        append_images=icon_images[1:])
    print(f"Icon saved: {ico_path}")

    png_path = os.path.join(HERE, "AppSetup.png")
    base.save(png_path)
    print(f"PNG saved: {png_path}")


if __name__ == "__main__":
    generate_icon()
