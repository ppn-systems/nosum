"""Package 4: sprites/crops.png and sprites/soil.png; also the produce pictures other sheets reuse."""
import re

import pngio
from pngio import Image, recolor

# game crop name -> (CC0 crop index 0..19 in the order of the pack's README, recolour or None, note)
# Hue ranges are 0..1: purple 0.69-0.92, yellow-green 0.15-0.30, red 0.92-0.04, brown 0.02-0.12.
PURPLE = [(0.69, 0.92)]
RECIPES = {
    'parsnip': (0, None, 'Turnip as drawn'),
    'radish': (0, recolor(0.19, only=PURPLE), 'Turnip, purple shifted to red'),
    'cauliflower': (17, recolor(0.0, sat=0.12, val=1.05, only=[(0.15, 0.30)]), 'Avocado plant, fruit recoloured cream'),
    'potato': (14, None, 'Potato as drawn'),
    'green_bean': (2, None, 'Cucumber vines (they climb a trellis like beans)'),
    'kale': (4, recolor(0.40, sat=0.9, val=0.75, only=[(0.92, 0.04)]), 'Tomato plant, red fruit shifted to dark green leaves'),
    'garlic': (0, recolor(0.0, sat=0.10, val=1.7, only=PURPLE), 'Turnip, purple bulb recoloured white'),
    'strawberry': (12, None, 'Strawberry as drawn'),
    'tomato': (4, None, 'Tomato as drawn'),
    'blueberry': (11, recolor(-0.12, only=PURPLE), 'Grapes, purple shifted to blue'),
    'melon': (5, None, 'Melon as drawn'),
    'hot_pepper': (6, recolor(0.17, sat=1.2, only=PURPLE), 'Eggplant, purple shifted to red'),
    'corn': (18, None, 'Corn as drawn'),
    'wheat': (10, None, 'Wheat as drawn'),
    'sunflower': (19, None, 'Sunflower as drawn'),
    'pumpkin': (16, None, 'Orange plant (round orange fruit among leaves)'),
    'cranberries': (11, recolor(0.19, only=PURPLE), 'Grapes, purple shifted to red'),
    'eggplant': (6, None, 'Eggplant as drawn'),
    'yam': (14, recolor(0.0, sat=1.35, val=0.95, only=[(0.0, 0.14)]), 'Potato, brown made redder'),
    'bok_choy': (13, recolor(0.31, sat=1.0, val=1.0, only=[(0.92, 0.04), (0.04, 0.12)]), 'Cassava, pink-brown leaves shifted to green'),
    'grape': (11, None, 'Grapes as drawn'),
}
ROW = re.compile(r'new\(\w+, "(\w+)".*?SpriteRow = (\d+), SpriteFrames = \[([\d, ]+)\]')


def read_catalogue(repo):
    """[(crop name, sprite row, [frames])] from the game's CropCatalog, so the sheet follows the code."""
    with open(f'{repo}/gameplay/Nosum.Gameplay/Farm/CropCatalog.cs', encoding='utf-8') as f:
        rows = [(m.group(1), int(m.group(2)), [int(x) for x in m.group(3).split(',')]) for m in ROW.finditer(f.read())]
    if not rows:
        raise SystemExit('CropCatalog.cs: no `SpriteRow = n, SpriteFrames = [...]` entries found; update tools/art_repack/crops.py')
    return rows


def cc0_stages(sheet, index, recipe):
    """The six pictures of one CC0 crop: produce, ripe, three growth stages, seeds."""
    row, side = index // 2, index % 2 * 6
    pics = [sheet.cell(side + i, row) for i in range(6)]
    if recipe is not None:
        pics = [p.map_pixels(recipe) for p in pics]
    return dict(produce=pics[0], ripe=pics[1], s3=pics[2], s2=pics[3], s1=pics[4], seed=pics[5])


def produce_icon(src, crop_name):
    """The harvest picture of a game crop (16 x 16), reused as its item icon."""
    index, recipe, _ = RECIPES[crop_name]
    return cc0_stages(src.sheet('crops'), index, recipe)['produce']


def build_crops(src, repo, width, height):
    out = Image(width, height)
    sheet = src.sheet('crops')
    for name, row, frames in read_catalogue(repo):
        index, recipe, _ = RECIPES[name]
        st = cc0_stages(sheet, index, recipe)
        ladder = [st['seed'], st['s1'], st['s2'], st['s3'], st['ripe']]
        n = len(frames)
        for k, frame in enumerate(frames):
            # The frame ids are the game's: 7 is the grown plant without fruit that a regrowing crop returns to.
            pic = st['s3'] if frame == 7 and k != n - 1 else ladder[round(k * 4 / (n - 1))]
            out.paste(pic, (row % 2 * 128) + frame * 16, (row // 2) * 32 + 16)
    return out


# --- soil ------------------------------------------------------------------------------------------------------------

SOIL_CELL = [0, 12, 13, 9, 4, 8, 1, 5, 15, 11, 14, 10, 3, 7, 2, 6]  # FarmDecorator.s_soilCell: neighbour mask -> sheet cell


def soil_tile(mask, wet):
    """One 16 x 16 tilled-soil tile. mask: bit 1 up, 2 right, 4 down, 8 left are tilled neighbours (the edge is open without one)."""
    base = (101, 72, 53) if not wet else (66, 46, 38)
    furrow = (84, 58, 44) if not wet else (49, 33, 29)
    light = (122, 90, 66) if not wet else (84, 60, 50)
    edge = (62, 42, 33) if not wet else (38, 26, 24)
    t = Image(16, 16)
    for y in range(16):
        for x in range(16):
            c = base
            if y % 4 == 1:
                c = furrow          # horizontal furrows every fourth row
            elif y % 4 == 3 and (x * 7 + y * 3) % 5 == 0:
                c = light           # a few lit crumbs on the ridges
            t.px[y][x] = c + (255,)
    for y in range(16):
        for x in range(16):
            off = (not mask & 1 and y == 0) or (not mask & 4 and y == 15) or (not mask & 8 and x == 0) or (not mask & 2 and x == 15)
            if off:
                t.px[y][x] = edge + (255,)
    # Round the corners where both sides meet open edges.
    for open_a, open_b, corner in ((1, 8, (0, 0)), (1, 2, (15, 0)), (4, 8, (0, 15)), (4, 2, (15, 15))):
        if not mask & open_a and not mask & open_b:
            t.px[corner[1]][corner[0]] = pngio.TRANSPARENT
    return t


def build_soil(width, height):
    out = Image(width, height)
    for mask in range(16):
        cell = SOIL_CELL[mask]
        for wet in (False, True):
            out.paste(soil_tile(mask, wet), (cell & 3) * 16 + (64 if wet else 0), (cell >> 2) * 16)
    return out
