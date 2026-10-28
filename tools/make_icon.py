"""Draws windows/TapMirror.ico: the cast glyph from the Android tile on a blue rounded square."""
import pathlib
from PIL import Image, ImageDraw

BLUE = (61, 123, 253, 255)
WHITE = (255, 255, 255, 255)

def draw(n):
    s = 4  # supersample
    N = n * s
    im = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, N - 1, N - 1], radius=N * 0.22, fill=BLUE)
    # glyph units: the same 24x24 grid as res/drawable/ic_tile.xml
    off = N * 0.16
    k = (N - 2 * off) / 22.0
    X = lambda x: off + (x - 1) * k
    Y = lambda y: off + (y - 3) * k + (N - 2 * off - 18 * k) / 2
    w = max(1, round(2 * k))
    # screen outline, open at the bottom-left where the waves are
    d.line([(X(1), Y(8)), (X(1), Y(4)), (X(23), Y(4)), (X(23), Y(20)), (X(14), Y(20))],
           fill=WHITE, width=w, joint="curve")
    # cast waves around the bottom-left corner
    for r in (6, 10):
        d.arc([X(1) - r * k, Y(21) - r * k, X(1) + r * k, Y(21) + r * k], 270, 360, fill=WHITE, width=w)
    d.pieslice([X(1) - 3 * k, Y(21) - 3 * k, X(1) + 3 * k, Y(21) + 3 * k], 270, 360, fill=WHITE)
    return im.resize((n, n), Image.LANCZOS)

out = pathlib.Path(__file__).resolve().parent.parent / "windows" / "TapMirror.ico"
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
big = draw(256)
big.save(out, sizes=[(n, n) for n in sizes], append_images=[draw(n) for n in sizes])
big.save(out.parent.parent / "docs" / "icon.png")
print("wrote", out)
