"""Re-grades the original sky-and-sea start art into the sign-in backdrop, art/own/ui/signin_background.png (chosen by the owner over a game capture).

Input: the original start_background.png (1500 x 520, Nosum's own art, drawn by tools/art_repack/ui.py; kept in git history at d2a3652). The
re-grade lowers saturation, lifts the shadows and blends toward a pale sky, so the blues are lighter and the contrast is lower. The geometry is unchanged.
Run from the repository root with the original file at the path given as the first argument:
  python3 tools/art_repack/signin_sky_regrade.py <original start_background.png>
"""

import colorsys
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "art", "own", "ui", "signin_background.png")
SATURATION = 0.55  # keep 55 % of the colour
LIFT = 0.80  # gamma below 1 lifts the shadows
BLEND = 0.30  # share of pale sky mixed in
PALE = (226, 242, 252)


def regrade(pixel):
    r, g, b, a = pixel
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    r, g, b = colorsys.hsv_to_rgb(h, s * SATURATION, v)
    r, g, b = (round(255 * (c ** LIFT)) for c in (r, g, b))
    r = round(r * (1 - BLEND) + PALE[0] * BLEND)
    g = round(g * (1 - BLEND) + PALE[1] * BLEND)
    b = round(b * (1 - BLEND) + PALE[2] * BLEND)
    return (r, g, b, a)


def main():
    source = Image.open(sys.argv[1]).convert("RGBA")
    out = Image.new("RGBA", source.size)
    out.putdata([regrade(p) for p in source.getdata()])
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    out.save(OUT, optimize=True)
    print(OUT, out.size)


if __name__ == "__main__":
    main()
