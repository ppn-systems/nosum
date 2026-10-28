"""Package 3 (placed objects): sprites/furniture.png and sprites/craftables.png, drawn in the Kenney palette.

Each piece is drawn once at the rectangle PlaceableCatalog names; the inventory icon is the 16 x 16 window of the same picture that
ItemIcons names, so a window never falls on an empty part of the picture.
"""
import draw
from draw import Canvas
from pngio import Image

FAB_D = (40, 70, 140)       # blue fabric: shadow, base, light
FAB = (60, 110, 190)
FAB_L = (110, 160, 230)
PAGE = [(233, 67, 52), (0, 154, 220), (99, 199, 77), (254, 174, 52), (240, 230, 200), (140, 80, 160)]


def chair_front():
    c = Canvas(16, 32)
    c.rect(3, 3, 10, 11, draw.WOOD)
    c.rect(3, 3, 10, 2, draw.WOOD_L)
    for x in (5, 8, 11):
        c.rect(x, 6, 1, 7, draw.WOOD_D)
    c.rect(2, 14, 12, 4, draw.WOOD_L)
    c.rect(2, 18, 12, 2, draw.WOOD)
    c.rect(3, 20, 2, 9, draw.WOOD_D)
    c.rect(11, 20, 2, 9, draw.WOOD_D)
    return c.outline()


def chair_side():
    c = Canvas(16, 32)
    c.rect(2, 3, 3, 17, draw.WOOD)
    c.rect(2, 3, 1, 17, draw.WOOD_L)
    c.rect(2, 14, 12, 4, draw.WOOD_L)
    c.rect(2, 18, 12, 2, draw.WOOD)
    c.rect(2, 20, 2, 9, draw.WOOD_D)
    c.rect(12, 20, 2, 9, draw.WOOD_D)
    return c.outline()


def chair_back():
    c = Canvas(16, 32)
    c.rect(3, 3, 10, 17, draw.WOOD)
    c.rect(3, 3, 10, 2, draw.WOOD_L)
    for x in (5, 8, 11):
        c.rect(x, 6, 1, 10, draw.WOOD_D)
    c.rect(2, 17, 12, 3, draw.WOOD_L)
    c.rect(3, 20, 2, 9, draw.WOOD_D)
    c.rect(11, 20, 2, 9, draw.WOOD_D)
    return c.outline()


def armchair(view, h=32):
    """32 x h blue armchair: view front, side (facing right) or back."""
    c = Canvas(32, h)
    y0 = h - 32
    if view == 'side':
        c.rect(4, y0 + 4, 8, 22, FAB)
        c.rect(4, y0 + 4, 8, 3, FAB_L)
        c.rect(4, y0 + 16, 24, 10, FAB)
        c.rect(4, y0 + 16, 24, 3, FAB_L)
        c.rect(14, y0 + 19, 14, 3, FAB_D)
        c.rect(5, y0 + 26, 3, 4, draw.WOOD_D)
        c.rect(24, y0 + 26, 3, 4, draw.WOOD_D)
        return c.outline()
    c.rect(5, y0 + 4, 22, 14, FAB)
    c.rect(5, y0 + 4, 22, 3, FAB_L)
    if view == 'front':
        c.rect(8, y0 + 8, 16, 10, FAB_L)
        c.rect(8, y0 + 16, 16, 2, FAB_D)
        c.rect(14, y0 + 8, 1, 8, FAB)                 # a seam in the cushion
    c.rect(3, y0 + 12, 6, 14, FAB_D)                  # arms
    c.rect(3, y0 + 12, 6, 2, FAB)
    c.rect(23, y0 + 12, 6, 14, FAB_D)
    c.rect(23, y0 + 12, 6, 2, FAB)
    c.rect(9, y0 + 18, 14, 8, FAB if view == 'back' else FAB_L)
    c.rect(9, y0 + 25, 14, 2, FAB_D)
    c.rect(5, y0 + 27, 3, 3, draw.WOOD_D)
    c.rect(24, y0 + 27, 3, 3, draw.WOOD_D)
    return c.outline()


def couch(view):
    if view == 'side':
        c = Canvas(32, 48)
        c.rect(4, 4, 7, 40, FAB)
        c.rect(4, 4, 7, 3, FAB_L)
        c.rect(4, 24, 24, 20, FAB)
        c.rect(11, 26, 17, 3, FAB_L)
        c.rect(11, 38, 17, 4, FAB_D)
        c.rect(5, 44, 3, 3, draw.WOOD_D)
        c.rect(24, 44, 3, 3, draw.WOOD_D)
        return c.outline()
    c = Canvas(48, 32)
    c.rect(4, 4, 40, 14, FAB)
    c.rect(4, 4, 40, 3, FAB_L)
    c.rect(2, 12, 6, 14, FAB_D)
    c.rect(2, 12, 6, 2, FAB)
    c.rect(40, 12, 6, 14, FAB_D)
    c.rect(40, 12, 6, 2, FAB)
    c.rect(8, 16, 32, 10, FAB if view == 'back' else FAB_L)
    c.rect(8, 25, 32, 2, FAB_D)
    if view == 'front':
        c.rect(23, 8, 1, 17, FAB_D)
        c.rect(24, 8, 1, 17, FAB)
    c.rect(4, 27, 3, 3, draw.WOOD_D)
    c.rect(41, 27, 3, 3, draw.WOOD_D)
    return c.outline()


def table():
    c = Canvas(32, 38)
    c.rect(1, 3, 30, 14, draw.WOOD_L)       # the top, seen from above at an angle
    c.rect(1, 3, 30, 2, draw.CREAM)
    for x in (4, 12, 20, 27):
        c.rect(x, 6, 1, 10, draw.WOOD)
    c.rect(1, 17, 30, 4, draw.WOOD)
    c.rect(1, 20, 30, 1, draw.WOOD_D)
    c.rect(3, 21, 3, 15, draw.WOOD_D)
    c.rect(26, 21, 3, 15, draw.WOOD_D)
    return c.outline()


def bookcase():
    c = Canvas(32, 48)
    c.rect(1, 1, 30, 46, draw.WOOD)
    c.rect(1, 1, 30, 2, draw.WOOD_L)
    for row, y in enumerate((4, 17, 30)):
        c.rect(3, y, 26, 11, draw.WOOD_D)
        x = 4
        k = row
        while x < 27:
            w = 3 + (x * 5 + row) % 3
            hgt = 7 + (x + row) % 3
            c.rect(x, y + 11 - hgt, w, hgt, PAGE[k % len(PAGE)])
            c.rect(x, y + 11 - hgt, 1, hgt, tuple(min(255, v + 40) for v in PAGE[k % len(PAGE)]))
            x += w + 1
            k += 1
        c.rect(1, y + 11, 30, 2, draw.WOOD)
    return c.outline()


def lamp():
    c = Canvas(16, 34)
    c.rect(3, 2, 10, 9, (254, 224, 140))    # the shade, lit
    c.rect(3, 2, 10, 2, (255, 244, 190))
    c.rect(1, 11, 14, 2, (222, 170, 80))
    c.rect(7, 13, 2, 17, draw.WOOD_D)
    c.rect(4, 30, 8, 3, draw.WOOD)
    c.rect(4, 30, 8, 1, draw.WOOD_L)
    return c.outline()


def plant():
    c = Canvas(16, 32)
    for cx, cy, rx, ry in ((8, 8, 6, 6), (4, 13, 4, 4), (12, 13, 4, 4), (8, 16, 5, 4)):
        for y in range(32):
            for x in range(16):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                    c.dot(x, y, draw.GREEN_L if (x + y) % 4 == 0 else draw.GREEN if y < cy + 1 else draw.GREEN_D)
    c.rect(7, 18, 2, 5, draw.GREEN_D)
    c.rect(3, 22, 10, 9, draw.WOOD)
    c.rect(2, 22, 12, 2, draw.WOOD_L)
    c.rect(3, 29, 10, 2, draw.WOOD_D)
    return c.outline()


def scarecrow():
    c = Canvas(16, 32)
    c.rect(7, 8, 2, 22, draw.WOOD_D)
    c.rect(1, 13, 14, 2, draw.WOOD)
    c.rect(4, 15, 8, 8, (190, 70, 60))      # shirt
    c.rect(4, 15, 8, 2, (230, 110, 90))
    c.rect(0, 14, 2, 3, draw.GOLD)          # straw at the sleeves
    c.rect(14, 14, 2, 3, draw.GOLD)
    c.rect(5, 8, 6, 6, (240, 200, 150))     # head
    c.dot(6, 10, draw.OUT)
    c.dot(9, 10, draw.OUT)
    c.rect(3, 6, 10, 2, draw.GOLD)          # hat brim
    c.rect(5, 2, 6, 4, draw.GOLD)
    c.rect(5, 5, 6, 1, draw.RED)
    return c.outline()


def furnace(lit=False):
    c = Canvas(16, 32)
    c.rect(5, 6, 6, 8, draw.STEEL_D)        # chimney
    c.rect(5, 6, 6, 2, draw.STEEL)
    if lit:
        c.rect(6, 2, 4, 4, draw.GOLD_L)     # chimney glow
        c.rect(7, 0, 2, 3, draw.GOLD)
    c.rect(1, 12, 14, 18, draw.STEEL)
    c.rect(1, 12, 14, 3, draw.STEEL_L)
    c.rect(1, 27, 14, 3, draw.STEEL_D)
    c.rect(4, 17, 8, 8, draw.OUT)
    if lit:
        c.rect(5, 18, 6, 6, (255, 120, 40))
        c.rect(5, 19, 6, 5, draw.GOLD)
        c.rect(6, 20, 4, 4, draw.GOLD_L)
    else:
        c.rect(5, 18, 6, 6, (230, 90, 40))
        c.rect(6, 20, 4, 4, draw.GOLD)
        c.rect(7, 22, 2, 2, draw.GOLD_L)
    for x in (3, 12):
        c.rect(x, 14, 1, 12, draw.STEEL_D)
    return c.outline()


# (rectangle the placed picture occupies: x, y) -> picture
FURNITURE_VIEWS = [
    ((0, 0), chair_front()), ((16, 0), chair_side()), ((32, 0), chair_back()),
    ((0, 144), armchair('front')), ((32, 140), armchair('side', 36)), ((64, 144), armchair('back')),
    ((0, 208), couch('front')), ((48, 208), couch('side')), ((80, 208), couch('back')),
    ((0, 569), table()), ((48, 640), bookcase()), ((48, 733), lamp()), ((0, 688), plant()),
]
CRAFTABLES = [((0, 32), scarecrow()), ((80, 32), furnace()), ((96, 32), furnace(lit=True))]
CHEST = (32, 512)    # the chest is Tiny Dungeon's closed chest (tile 5, 7), standing at the foot of the 16 x 32 cell


def build(src, size):
    furniture = Image(*size('sprites/furniture.png'))
    for (x, y), pic in FURNITURE_VIEWS:
        furniture.paste(pic, x, y)
    craftables = Image(*size('sprites/craftables.png'))
    for (x, y), pic in CRAFTABLES:
        craftables.paste(pic, x, y)
    craftables.paste(src.sheet('dungeon').cell(5, 7), CHEST[0], CHEST[1] + 14)
    return {'sprites/furniture.png': furniture, 'sprites/craftables.png': craftables}
