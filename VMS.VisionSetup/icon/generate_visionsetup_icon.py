"""Generate a Vision Setup icon — camera lens + crosshair inspection concept."""
from PIL import Image, ImageDraw
import math


def generate_icon():
    size = 256
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    margin = 12
    cx, cy = size // 2, size // 2

    # Background
    draw.rounded_rectangle(
        [margin, margin, size - margin, size - margin],
        radius=40,
        fill=(24, 28, 40, 255),
    )
    draw.rounded_rectangle(
        [margin, margin, size - margin, size - margin],
        radius=40,
        outline=(60, 130, 255, 120),
        width=3,
    )

    # Outer lens ring
    r_outer = 88
    for i in range(6, 0, -1):
        alpha = int(30 * (1 - i / 6))
        draw.ellipse(
            [cx - r_outer - i, cy - r_outer - i, cx + r_outer + i, cy + r_outer + i],
            fill=(60, 130, 255, alpha),
        )
    draw.ellipse(
        [cx - r_outer, cy - r_outer, cx + r_outer, cy + r_outer],
        outline=(80, 160, 255, 220),
        width=4,
    )

    # Inner lens ring
    r_inner = 62
    draw.ellipse(
        [cx - r_inner, cy - r_inner, cx + r_inner, cy + r_inner],
        outline=(100, 180, 255, 150),
        width=2,
    )

    # Lens glass gradient (dark center)
    r_glass = 58
    draw.ellipse(
        [cx - r_glass, cy - r_glass, cx + r_glass, cy + r_glass],
        fill=(20, 24, 36, 200),
    )

    # Lens reflection arc
    for offset in range(3):
        arc_r = r_glass - 8 - offset
        draw.arc(
            [cx - arc_r, cy - arc_r, cx + arc_r, cy + arc_r],
            start=200, end=260,
            fill=(255, 255, 255, 50 - offset * 15),
            width=2,
        )

    # Crosshair
    ch_color = (76, 175, 80, 255)  # Green
    ch_len = 35
    ch_gap = 12
    ch_width = 2

    # Horizontal
    draw.line([(cx - ch_len, cy), (cx - ch_gap, cy)], fill=ch_color, width=ch_width)
    draw.line([(cx + ch_gap, cy), (cx + ch_len, cy)], fill=ch_color, width=ch_width)
    # Vertical
    draw.line([(cx, cy - ch_len), (cx, cy - ch_gap)], fill=ch_color, width=ch_width)
    draw.line([(cx, cy + ch_gap), (cx, cy + ch_len)], fill=ch_color, width=ch_width)

    # Center dot
    draw.ellipse(
        [cx - 3, cy - 3, cx + 3, cy + 3],
        fill=(76, 175, 80, 255),
    )

    # Corner brackets (inspection region)
    bracket_color = (0, 173, 181, 200)
    b_len = 18
    b_offset = 45
    bw = 3

    # Top-left
    draw.line([(cx - b_offset, cy - b_offset), (cx - b_offset + b_len, cy - b_offset)], fill=bracket_color, width=bw)
    draw.line([(cx - b_offset, cy - b_offset), (cx - b_offset, cy - b_offset + b_len)], fill=bracket_color, width=bw)
    # Top-right
    draw.line([(cx + b_offset, cy - b_offset), (cx + b_offset - b_len, cy - b_offset)], fill=bracket_color, width=bw)
    draw.line([(cx + b_offset, cy - b_offset), (cx + b_offset, cy - b_offset + b_len)], fill=bracket_color, width=bw)
    # Bottom-left
    draw.line([(cx - b_offset, cy + b_offset), (cx - b_offset + b_len, cy + b_offset)], fill=bracket_color, width=bw)
    draw.line([(cx - b_offset, cy + b_offset), (cx - b_offset, cy + b_offset - b_len)], fill=bracket_color, width=bw)
    # Bottom-right
    draw.line([(cx + b_offset, cy + b_offset), (cx + b_offset - b_len, cy + b_offset)], fill=bracket_color, width=bw)
    draw.line([(cx + b_offset, cy + b_offset), (cx + b_offset, cy + b_offset - b_len)], fill=bracket_color, width=bw)

    # Save
    sizes_list = [16, 24, 32, 48, 64, 128, 256]
    icon_images = [img.resize((s, s), Image.LANCZOS) for s in sizes_list]

    ico_path = r"D:\Repo\VMS\VMS.VisionSetup\icon\VisionSetup.ico"
    icon_images[0].save(
        ico_path,
        format="ICO",
        sizes=[(s, s) for s in sizes_list],
        append_images=icon_images[1:],
    )
    print(f"Icon saved: {ico_path}")

    png_path = r"D:\Repo\VMS\VMS.VisionSetup\icon\VisionSetup.png"
    img.save(png_path)
    print(f"PNG saved: {png_path}")


if __name__ == "__main__":
    generate_icon()
