"""Generate a deep learning themed icon (neural network) for VMS.DeepLearning."""
from PIL import Image, ImageDraw
import math

def generate_icon():
    size = 256
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # Background: rounded dark square with gradient feel
    margin = 12
    draw.rounded_rectangle(
        [margin, margin, size - margin, size - margin],
        radius=40,
        fill=(30, 30, 48, 255),
    )

    # Inner glow border
    draw.rounded_rectangle(
        [margin, margin, size - margin, size - margin],
        radius=40,
        outline=(80, 140, 255, 100),
        width=3,
    )

    cx, cy = size // 2, size // 2

    # Neural network layers: 3 layers (input, hidden, output)
    layers = [
        [(-70, -50), (-70, 0), (-70, 50)],           # Input layer (3 nodes)
        [(-10, -65), (-10, -22), (-10, 22), (-10, 65)],  # Hidden layer 1 (4 nodes)
        [(50, -50), (50, 0), (50, 50)],               # Hidden layer 2 (3 nodes)
        [(110, -22), (110, 22)],                       # Output layer (2 nodes) -- shifted right more
    ]

    # Shift to center, adjusted
    offset_x = -18
    layers_abs = []
    for layer in layers:
        abs_layer = [(cx + x + offset_x, cy + y) for x, y in layer]
        layers_abs.append(abs_layer)

    # Draw connections between layers
    connection_colors = [
        (60, 120, 255, 80),   # blue
        (100, 200, 255, 80),  # cyan
        (140, 100, 255, 80),  # purple
    ]
    for i in range(len(layers_abs) - 1):
        color = connection_colors[i % len(connection_colors)]
        for x1, y1 in layers_abs[i]:
            for x2, y2 in layers_abs[i + 1]:
                draw.line([(x1, y1), (x2, y2)], fill=color, width=2)

    # Draw nodes
    node_colors = [
        (70, 160, 255),   # Input: blue
        (100, 220, 255),  # Hidden1: cyan
        (160, 120, 255),  # Hidden2: purple
        (76, 175, 80),    # Output: green
    ]
    for li, layer in enumerate(layers_abs):
        color = node_colors[li]
        for x, y in layer:
            r = 13
            # Glow effect
            for gr in range(r + 8, r, -1):
                alpha = int(40 * (1 - (gr - r) / 8))
                glow_color = (*color, alpha)
                draw.ellipse([x - gr, y - gr, x + gr, y + gr], fill=glow_color)
            # Solid node
            draw.ellipse([x - r, y - r, x + r, y + r], fill=(*color, 255))
            # Highlight
            hr = r - 4
            draw.ellipse([x - hr, y - hr - 2, x + hr - 2, y + hr - 4],
                         fill=(255, 255, 255, 60))

    # Save as ICO with multiple sizes
    sizes_list = [16, 24, 32, 48, 64, 128, 256]
    icon_images = []
    for s in sizes_list:
        resized = img.resize((s, s), Image.LANCZOS)
        icon_images.append(resized)

    ico_path = r"D:\Repo\VMS\VMS.DeepLearning\Resources\app.ico"
    icon_images[0].save(
        ico_path,
        format="ICO",
        sizes=[(s, s) for s in sizes_list],
        append_images=icon_images[1:],
    )
    print(f"Icon saved: {ico_path}")

    # Also save PNG for reference
    png_path = r"D:\Repo\VMS\VMS.DeepLearning\Resources\app_icon.png"
    img.save(png_path)
    print(f"PNG saved: {png_path}")


if __name__ == "__main__":
    generate_icon()
