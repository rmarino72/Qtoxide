"""Draws the Qtoxide icon (a chat bubble with a Q) and writes PNG, ICO and the iconset for ICNS."""
import os, sys
from PIL import Image, ImageDraw, ImageFont

out = sys.argv[1]
S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

# Rounded square with a vertical gradient (Qtoxide accent colours).
top, bottom = (0x6D, 0x78, 0xF7), (0x42, 0x4C, 0xD6)
grad = Image.new("RGBA", (S, S))
gd = ImageDraw.Draw(grad)
for y in range(S):
    t = y / (S - 1)
    gd.line([(0, y), (S, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=220, fill=255)
img.paste(grad, (0, 0), mask)

# White speech bubble with a tail.
d = ImageDraw.Draw(img)
d.rounded_rectangle([190, 200, 834, 720], radius=170, fill="white")
d.polygon([(300, 640), (250, 860), (470, 700)], fill="white")

# The Q.
font = None
for path in ["/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/Library/Fonts/Arial Bold.ttf",
             "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"]:
    if os.path.exists(path):
        font = ImageFont.truetype(path, 430)
        break
d.text((512, 455), "Q", font=font, fill=(0x4E, 0x59, 0xE6), anchor="mm")

img.save(os.path.join(out, "qtoxide.png"))
img.resize((256, 256), Image.LANCZOS).save(os.path.join(out, "qtoxide-256.png"))
img.save(os.path.join(out, "qtoxide.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

iconset = os.path.join(out, "qtoxide.iconset")
os.makedirs(iconset, exist_ok=True)
for size in [16, 32, 128, 256, 512]:
    img.resize((size, size), Image.LANCZOS).save(os.path.join(iconset, f"icon_{size}x{size}.png"))
    img.resize((size * 2, size * 2), Image.LANCZOS).save(os.path.join(iconset, f"icon_{size}x{size}@2x.png"))
