"""Generate a Recipe Manager icon for VMS.VisionSetup."""
from PIL import Image, ImageDraw

def generate_icon():
    size = 256
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    margin = 12
    # Background
    draw.rounded_rectangle(
        [margin, margin, size - margin, size - margin],
        radius=40,
        fill=(30, 30, 48, 255),
    )
    draw.rounded_rectangle(
        [margin, margin, size - margin, size - margin],
        radius=40,
        outline=(80, 140, 255, 100),
        width=3,
    )

    cx, cy = size // 2, size // 2

    # Document/clipboard shape
    doc_left, doc_top = 60, 40
    doc_right, doc_bottom = 196, 220
    fold = 30

    # Shadow
    draw.rounded_rectangle(
        [doc_left + 4, doc_top + 4, doc_right + 4, doc_bottom + 4],
        radius=8,
        fill=(0, 0, 0, 60),
    )

    # Document body
    draw.polygon(
        [(doc_left, doc_top), (doc_right - fold, doc_top),
         (doc_right, doc_top + fold), (doc_right, doc_bottom),
         (doc_left, doc_bottom)],
        fill=(240, 240, 245, 255),
        outline=(180, 180, 190, 255),
    )

    # Page fold
    draw.polygon(
        [(doc_right - fold, doc_top),
         (doc_right, doc_top + fold),
         (doc_right - fold, doc_top + fold)],
        fill=(200, 200, 210, 255),
        outline=(180, 180, 190, 255),
    )

    # Lines representing recipe content
    line_y_start = doc_top + 50
    line_left = doc_left + 20
    line_right = doc_right - 20

    colors = [(76, 175, 80), (0, 173, 181), (100, 140, 255)]
    for i in range(3):
        y = line_y_start + i * 35

        # Colored bullet
        bullet_r = 7
        bx = line_left + bullet_r
        by = y + 6
        draw.ellipse(
            [bx - bullet_r, by - bullet_r, bx + bullet_r, by + bullet_r],
            fill=(*colors[i], 255),
        )

        # Text line
        lx = line_left + 24
        line_w = line_right - lx - (i * 15)
        draw.rounded_rectangle(
            [lx, y + 2, lx + line_w, y + 11],
            radius=3,
            fill=(120, 120, 130, 180),
        )

    # Gear/settings accent (bottom-right)
    gx, gy = doc_right - 20, doc_bottom - 25
    gr = 18
    # Gear circle
    draw.ellipse(
        [gx - gr, gy - gr, gx + gr, gy + gr],
        fill=(50, 50, 65, 220),
        outline=(100, 140, 255, 200),
        width=3,
    )
    # Inner circle
    draw.ellipse(
        [gx - 7, gy - 7, gx + 7, gy + 7],
        fill=(100, 140, 255, 255),
    )

    # Save as ICO
    sizes_list = [16, 24, 32, 48, 64, 128, 256]
    icon_images = [img.resize((s, s), Image.LANCZOS) for s in sizes_list]

    ico_path = r"D:\Repo\VMS\VMS.VisionSetup\icon\RecipeManager.ico"
    icon_images[0].save(
        ico_path,
        format="ICO",
        sizes=[(s, s) for s in sizes_list],
        append_images=icon_images[1:],
    )
    print(f"Icon saved: {ico_path}")

    png_path = r"D:\Repo\VMS\VMS.VisionSetup\icon\RecipeManager.png"
    img.save(png_path)
    print(f"PNG saved: {png_path}")


if __name__ == "__main__":
    generate_icon()
