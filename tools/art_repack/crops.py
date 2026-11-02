"""Package 4: sprites/crops.png and sprites/soil.png; also the produce pictures other sheets reuse."""
import re

import pngio
from draw import Canvas
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

# Kenney Tiny Farm (CC0) draws six crops as five tiles in a row of its sheet: sprout, young plant, grown plant, withered plant, harvest.
# game crop -> (first tile, recolour of every picture or None, recolour that turns the grown plant's fruit unripe, note); with no unripe
# recolour the grown plant is the third growth stage and the harvest picture is the ripe one (roots and cabbages)
LEAF = [(0.18, 0.5)]
GREEN_FRUIT = recolor(0.3, sat=0.8, val=0.95, only=[(0.92, 0.04)])
FARM = {
    'parsnip': (4, recolor(0.0, sat=0.25, val=1.3, only=[(0.0, 0.15)]), None, 'Tiny Farm carrot (tiles 4-8), orange recoloured to cream'),
    'radish': (16, recolor(0.19, only=PURPLE), None, 'Tiny Farm turnip (tiles 16-20), purple shifted to red'),
    'kale': (52, recolor(0.07, sat=1.0, val=0.72, only=LEAF), None, 'Tiny Farm cabbage (tiles 52-56), shifted to a dark blue-green'),
    'garlic': (16, recolor(0.0, sat=0.10, val=1.7, only=PURPLE), None, 'Tiny Farm turnip (tiles 16-20), purple bulb recoloured white'),
    'tomato': (40, None, GREEN_FRUIT, 'Tiny Farm tomato (tiles 40-44) as drawn; the unripe stage has green fruit'),
    'corn': (28, None, recolor(0.15, sat=0.8, only=[(0.07, 0.17)]), 'Tiny Farm corn (tiles 28-32) as drawn; the unripe stage has green cobs'),
    'wheat': (64, None, recolor(0.18, sat=0.8, val=0.9, only=[(0.07, 0.17)]), 'Tiny Farm wheat (tiles 64-68) as drawn; the unripe stage is green'),
    'bok_choy': (52, recolor(-0.03, sat=0.85, val=1.05, only=LEAF), None, 'Tiny Farm cabbage (tiles 52-56), shifted to a lighter yellow-green'),
}
SUNFLOWER_PRODUCE = 83     # Tiny Farm sunflower: the harvest picture only, the growth stages stay the CC0 crops pack's
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


def farm_stages(sheet, first, recipe, unripe):
    """The pictures of one Tiny Farm crop. The seed is the crops pack's, which the caller adds; the unripe stage is the grown plant with green fruit."""
    pics = [sheet.cell((first + i) % 12, (first + i) // 12) for i in range(5)]
    if recipe is not None:
        pics = [p.map_pixels(recipe) for p in pics]
    if unripe is None:
        return dict(produce=pics[4], ripe=pics[4], s3=pics[2], s2=pics[1], s1=pics[0])
    return dict(produce=pics[4], ripe=pics[2], s3=pics[2].map_pixels(unripe), s2=pics[1].map_pixels(unripe), s1=pics[0])


def _early_stage(pic):
    """A 1 px shorter intermediate growth stage with the root anchored at the same bottom row."""
    out = Image(pic.width, pic.height)
    opaque_rows = [y for y in range(pic.height) if any(p[3] > 0 for p in pic.px[y])]
    if not opaque_rows:
        return pic
    bot = max(opaque_rows)
    for y in range(1, bot):
        out.px[y] = list(pic.px[y - 1])
    out.px[bot] = list(pic.px[bot])
    return out


# The seed picture (frame 0 of every crop) is the pack's red sprout. Its colours sit on the same browns as the tilled soil (contrast 1.0 to 1.3 on
# the dark soil), so a planted tile showed no sprout at stage 0 (world pass 7). Every opaque pixel is re-inked as a green by its brightness: the
# darkest pixels become a dark green outline, the brightest a pale highlight. Every other picture is left as it was.
SEED_RAMP = [(24, 70, 36), (74, 142, 62), (116, 184, 84), (172, 228, 124)]
SEED_LUMA_EDGES = (55, 80, 120)  # the original brightness (0 to 255) at which the ramp steps up


def _inked_seed(pic):
    out = Image(pic.width, pic.height)
    for y in range(pic.height):
        for x in range(pic.width):
            p = pic.px[y][x]
            if p[3] == 0:
                out.px[y][x] = list(p)
                continue
            luma = 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]
            step = sum(1 for edge in SEED_LUMA_EDGES if luma >= edge)
            ink = SEED_RAMP[step]
            out.px[y][x] = [ink[0], ink[1], ink[2], p[3]]
    return out


def stages(src, crop_name):
    """The seed, three growth stages, ripe and produce pictures of a game crop: Tiny Farm's where it has the crop, else the crops pack's."""
    index, recipe, _ = RECIPES[crop_name]
    pack = cc0_stages(src.sheet('crops'), index, recipe)
    if crop_name not in FARM:
        pack['seed'] = _inked_seed(pack['seed'])
        return pack
    first, farm_recipe, unripe, _ = FARM[crop_name]
    st = farm_stages(src.sheet('farm'), first, farm_recipe, unripe)
    st['seed'] = _inked_seed(pack['seed'])
    return st


def produce_icon(src, crop_name):
    """The harvest picture of a game crop (16 x 16), reused as its item icon."""
    if crop_name == 'sunflower':
        farm = src.sheet('farm')
        return farm.cell(SUNFLOWER_PRODUCE % 12, SUNFLOWER_PRODUCE // 12)
    return stages(src, crop_name)['produce']


# The withered picture (a dry, brown plant that replaces the growth stage of a crop that withered, CropSprites.WitheredFrame): one shared
# frame in row 0, frame 1; build_crops refuses a catalogue that puts a growth stage in that frame, so the picture never covers one.
WITHERED_ROW = 0
WITHERED_FRAME = 1
_WITHERED_OUTLINE = (70, 44, 28)


def withered_picture():
    """The 16 x 16 dry plant: a bare brown stalk with two curled tan leaves and a few cracks of soil at its foot (Nosum's own drawing)."""
    stalk, stalk_dark = (150, 104, 60), (112, 76, 44)
    leaf, leaf_dark, leaf_light = (206, 168, 104), (150, 108, 62), (226, 196, 130)
    soil = (104, 68, 42)
    c = Canvas(16, 16)
    c.line(8, 15, 8, 4, stalk_dark)
    c.line(7, 15, 7, 5, stalk)
    c.ellipse(4.5, 9.5, 2.6, 1.3, leaf_dark)
    c.ellipse(4.5, 8.5, 2.2, 1.0, leaf)
    c.ellipse(11.5, 7.5, 2.6, 1.2, leaf_dark)
    c.ellipse(11.5, 6.5, 2.0, 0.9, leaf)
    c.ellipse(10, 12.5, 1.8, 0.9, leaf_dark)
    for x, y, col in [(5, 8, leaf_light), (11, 6, leaf_light), (7, 5, leaf_light)]:
        c.dot(x, y, col)
    for x, y in [(3, 15), (4, 15), (11, 15), (12, 15), (13, 14), (2, 14), (6, 15), (9, 15)]:
        c.dot(x, y, soil)
    c.outline(_WITHERED_OUTLINE)
    return c


def build_crops(src, repo):
    catalogue = read_catalogue(repo)
    bands = (max(row for _, row, _ in catalogue) // 2) + 1
    out = Image(256, bands * 32)
    for name, row, frames in catalogue:
        if WITHERED_FRAME in frames and row == WITHERED_ROW:
            raise SystemExit(f'{name} uses frame {WITHERED_FRAME}, the withered picture: move the picture in crops.py')
        st = stages(src, name)
        ladder = [st['seed'], st['s1'], st['s2'], st['s3'], st['ripe']]
        n = len(frames)
        for k, frame in enumerate(frames):
            # The frame ids are the game's: 7 is the grown plant without fruit that a regrowing crop returns to.
            if frame == 7 and k != n - 1:
                pic = st['s3']
            elif n == 6 and k == 2:
                pic = _early_stage(st['s2'])
            else:
                pic = ladder[round(k * 4 / (n - 1))]
            out.paste(pic, (row % 2 * 128) + frame * 16, (row // 2) * 32 + 16)
    out.paste(withered_picture(), (WITHERED_ROW % 2 * 128) + WITHERED_FRAME * 16, (WITHERED_ROW // 2) * 32 + 16)
    return out


# --- soil ------------------------------------------------------------------------------------------------------------

SOIL_CELL = [0, 12, 13, 9, 4, 8, 1, 5, 15, 11, 14, 10, 3, 7, 2, 6]  # FarmDecorator.s_soilCell: neighbour mask -> sheet cell


def soil_tile(mask, wet):
    """One 16 x 16 tilled-soil tile: ridges lit from above with furrows between them, a few clods, a dark lip where the bed ends.

    mask: bit 1 up, 2 right, 4 down, 8 left are tilled neighbours (the edge is open without one).
    """
    if wet:
        base, furrow, light, glint, edge = (88, 62, 52), (58, 40, 36), (112, 82, 68), (136, 104, 88), (40, 28, 26)
    else:
        base, furrow, light, glint, edge = (128, 90, 60), (88, 58, 40), (156, 114, 78), (184, 142, 98), (62, 42, 33)
    t = Image(16, 16)
    for y in range(16):
        for x in range(16):
            band = y % 4
            c = light if band == 0 else base if band in (1, 2) else furrow     # lit ridge, body, furrow below it
            if band == 0 and (x * 5 + y) % 7 == 0:
                c = glint                                                         # a lit crumb on the ridge
            elif band in (1, 2) and (x * 7 + y * 3) % 11 == 0:
                c = furrow                                                        # a shadowed clod
            t.px[y][x] = c + (255,)
    for y in range(16):
        for x in range(16):
            if (not mask & 1 and y == 0) or (not mask & 4 and y == 15) or (not mask & 8 and x == 0) or (not mask & 2 and x == 15):
                t.px[y][x] = edge + (255,)
            elif not mask & 1 and y == 1:
                t.px[y][x] = light + (255,)
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
