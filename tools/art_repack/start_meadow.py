"""Composes the start-screen backdrop, art/own/ui/start_meadow.png, from CC0 tiles (Kenney Tiny Town and the josehzz crops sheet).

The picture is 96 x 32 cells of 16 px (1536 x 512 px). It is drawn at 1 art pixel = 1 u on the start screen, anchored to the top and repeated, so
the soft sky band stays at the top of any screen. The farm sits in the middle columns, where the window is drawn at 1280 px wide. The seed fixes the
random scatter, so a run reproduces the file. Run from the repository root: python3 tools/art_repack/start_meadow.py
"""

import os
import random

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TOWN = os.path.join(ROOT, "art", "source", "kenney_tiny-town", "Tilemap", "tilemap.png")
CROPS = os.path.join(ROOT, "art", "source", "josehzz_farming-crops-16x16", "Crop_Spritesheet.png")
OUT = os.path.join(ROOT, "art", "own", "ui", "start_meadow.png")

town = Image.open(TOWN).convert("RGBA")
crops = Image.open(CROPS).convert("RGBA")


def tile(col, row):
    """A Tiny Town cell: 16 px on a 17 px pitch (1 px gap)."""
    x, y = col * 17, row * 17
    return town.crop((x, y, x + 16, y + 16))


def crop_cell(col, row):
    """A crops-sheet cell: 16 px on a 16 px pitch."""
    x, y = col * 16, row * 16
    return crops.crop((x, y, x + 16, y + 16))


W, H = 96, 32
SKY_ROWS = 7
SKY = [(170, 220, 245), (170, 220, 245), (182, 228, 248), (196, 235, 250), (206, 239, 251), (216, 243, 252), (224, 246, 253)]
rng = random.Random(7)
img = Image.new("RGBA", (W * 16, H * 16), (0, 0, 0, 255))

# Sky band: flat soft rows at the top.
for ty in range(SKY_ROWS):
    img.paste(SKY[ty], (0, ty * 16, W * 16, ty * 16 + 16))

# Meadow below the sky: grass with a few specks; the lighter tile is used less.
grass = [tile(0, 0)] * 5 + [tile(1, 0)]
for ty in range(SKY_ROWS, H):
    for tx in range(W):
        img.paste(rng.choice(grass), (tx * 16, ty * 16))

# A distant tree line along the sky's edge, sparse and soft.
for tx in range(0, W, 3):
    img.alpha_composite(tile(4, 0) if rng.random() < 0.6 else tile(4, 2), (tx * 16 + rng.randrange(0, 2) * 4, (SKY_ROWS - 1) * 16 - 4))

# Path: one narrow band, low in the picture, across the full width.
PATH_TOP, PATH_BOTTOM = 22, 23
for tx in range(W):
    img.paste(tile(1, 1), (tx * 16, PATH_TOP * 16))
    img.paste(tile(1, 2), (tx * 16, (PATH_TOP + 1) * 16))

# The farm, centred on the visible columns (34 to 62): a red-roofed house left of centre, a rail fence, and a tilled plot with sprouts.
HOUSE_X, HOUSE_Y = 36, 8
for col in range(3):
    img.alpha_composite(tile(4 + col, 4), ((HOUSE_X + col) * 16, HOUSE_Y * 16))
    img.alpha_composite(tile(4 + col, 5), ((HOUSE_X + col) * 16, (HOUSE_Y + 1) * 16))
    img.alpha_composite(tile(1 + col, 6), ((HOUSE_X + col) * 16, (HOUSE_Y + 2) * 16))
    img.alpha_composite(tile(1 + col, 7), ((HOUSE_X + col) * 16, (HOUSE_Y + 3) * 16))

for tx in range(38, 47):
    img.alpha_composite(tile(9, 6), (tx * 16, 12 * 16))

PLOT_X, PLOT_Y, PLOT_W, PLOT_H = 52, 14, 6, 3
for ty in range(PLOT_Y, PLOT_Y + PLOT_H):
    for tx in range(PLOT_X, PLOT_X + PLOT_W):
        img.alpha_composite(tile(1, 2), (tx * 16, ty * 16))
SPROUTS = [crop_cell(3, 2), crop_cell(4, 2), crop_cell(3, 3), crop_cell(4, 3)]
for i, tx in enumerate(range(PLOT_X, PLOT_X + PLOT_W, 2)):
    img.alpha_composite(SPROUTS[i % len(SPROUTS)], (tx * 16, PLOT_Y * 16))

# Scattered trees and bushes in the meadow, kept out of the farm and the path.
TREES = [(4, 0), (4, 2), (5, 0)]
for _ in range(60):
    tx, ty = rng.randrange(W), rng.randrange(SKY_ROWS + 2, H)
    if PATH_TOP - 2 <= ty <= PATH_BOTTOM + 1:
        continue
    if HOUSE_X - 2 <= tx <= PLOT_X + PLOT_W + 2 and HOUSE_Y - 1 <= ty <= PLOT_Y + PLOT_H + 1:
        continue
    col, row = rng.choice(TREES)
    img.alpha_composite(tile(col, row), (tx * 16, ty * 16))

os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.save(OUT, optimize=True)
print(OUT, img.size)
