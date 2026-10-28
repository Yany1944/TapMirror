"""Draws docs/images/social-preview.png (1280x640) for the repository's social preview setting."""
import pathlib
from PIL import Image, ImageDraw, ImageFilter, ImageFont

root = pathlib.Path(__file__).resolve().parent.parent
images = root / "docs" / "images"
W, H = 1280, 640
FONTS = pathlib.Path("C:/Windows/Fonts")


def font(name, size):
    return ImageFont.truetype(str(FONTS / name), size)


# dark blue vertical gradient
bg = Image.new("RGB", (W, H))
top, bottom = (16, 24, 40), (28, 46, 82)
for y in range(H):
    t = y / (H - 1)
    ImageDraw.Draw(bg).line([(0, y), (W, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)))


def phone(path, height):
    """A screenshot with rounded corners and a soft shadow."""
    shot = Image.open(path).convert("RGB")
    w = int(shot.width * height / shot.height)
    shot = shot.resize((w, height), Image.LANCZOS)
    mask = Image.new("L", shot.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w - 1, height - 1], radius=28, fill=255)
    pad = 40
    layer = Image.new("RGBA", (w + 2 * pad, height + 2 * pad), (0, 0, 0, 0))
    shadow = Image.new("L", layer.size, 0)
    ImageDraw.Draw(shadow).rounded_rectangle([pad, pad + 12, pad + w, pad + height + 12], radius=28, fill=150)
    layer.putalpha(shadow.filter(ImageFilter.GaussianBlur(18)))
    layer.paste(shot, (pad, pad), mask)
    return layer


canvas = bg.convert("RGBA")
for path, x, y in ((images / "app-en-3.png", 985, 70), (images / "app-en-1.png", 760, 40)):
    p = phone(path, 560)
    canvas.alpha_composite(p, (x - 40, y - 40))

icon = Image.open(root / "docs" / "icon.png").convert("RGBA").resize((120, 120), Image.LANCZOS)
canvas.alpha_composite(icon, (80, 120))
d = ImageDraw.Draw(canvas)
d.text((80, 270), "TapMirror", font=font("segoeuib.ttf", 84), fill=(255, 255, 255))
d.text((84, 380), "Your Android screen on your Windows PC,", font=font("segoeui.ttf", 30), fill=(210, 222, 245))
d.text((84, 422), "one tap in quick settings.", font=font("segoeui.ttf", 30), fill=(210, 222, 245))
d.text((84, 500), "Over Wi-Fi  ·  root or no root  ·  built on scrcpy", font=font("segoeui.ttf", 24), fill=(140, 160, 200))

out = images / "social-preview.png"
canvas.convert("RGB").save(out, optimize=True)
print("wrote", out)
