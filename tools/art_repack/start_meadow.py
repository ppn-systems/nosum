"""Composes the start-screen meadow (art/own/ui/start_meadow.png) from Kenney Tiny Town tiles (CC0).

The tiles are cut from art/source/kenney_tiny-town/Tilemap/tilemap.png: 16 x 16 px cells on a 17 px pitch (1 px gap). The picture is 96 x 32 cells,
1536 x 512 px, drawn at 1 art pixel = 1 u on the start screen and repeated, so the page is meadow at any size. Run from the repository root: python3 tools/art_repack/start_meadow.py
The seed fixes the grass pattern, so the output is the same on every run.
"""

import os
import random

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "art", "source", "kenney_tiny-town", "Tilemap", "tilemap.png")
OUT = os.path.join(ROOT, "art", "own", "ui", "start_meadow.png")

sheet = Image.open(SRC).convert("RGBA")


def tile(col, row):
    x, y = col * 17, row * 17
    return sheet.crop((x, y, x + 16, y + 16))


W, H = 96, 32
img = Image.new("RGBA", (W * 16, H * 16), (0, 0, 0, 0))
rng = random.Random(7)
grass = [tile(0, 0), tile(0, 0), tile(0, 0), tile(0, 0), tile(1, 0), tile(1, 0)]
for ty in range(H):
    for tx in range(W):
        img.paste(rng.choice(grass), (tx * 16, ty * 16))

# A dirt path across the lower third.
PATH_TOP, PATH_BOTTOM = 19, 21
for tx in range(W):
    for ty in range(PATH_TOP, PATH_BOTTOM + 1):
        img.paste(tile(1, 1) if ty == PATH_TOP else tile(1, 2), (tx * 16, ty * 16))

# Trees and bushes scattered over the whole picture, off the path, so any window of the tiled meadow shows some.
TREES = [(4, 0), (4, 2), (5, 0)]
for _ in range(64):
    tx = rng.randrange(W)
    ty = rng.randrange(H)
    if PATH_TOP - 2 <= ty <= PATH_BOTTOM + 2:
        continue
    col, row = rng.choice(TREES)
    img.alpha_composite(tile(col, row), (tx * 16, ty * 16))

os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.save(OUT, optimize=True)
print(OUT, img.size)
