"""Package 3 (placed objects): sprites/furniture.png and sprites/craftables.png, drawn in the Kenney palette.

furniture.png: the first 13 pieces (rows 0 to 1488) at the rectangles PlaceableCatalog names, then the farmhouse band below them from
y 1488 (NEW_PIECES, shelf-packed by band_layout on the 16 px grid with a 1 px transparent gap; the layout is written to the feature's
scratchpad, not to the repo). Some band pieces are cut from Kenney Roguelike Indoors cells (stitched), the rest are drawn here.
Each piece is drawn once at its rectangle; the inventory icon is the 16 x 16 window of the same picture that ItemIcons names, so a window
never falls on an empty part of the picture.
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


def armchair(view, h=32, fab=FAB, fab_l=FAB_L, fab_d=FAB_D):
    """32 x h armchair in the fabric colours given (blue by default): view front, side (facing right) or back."""
    c = Canvas(32, h)
    y0 = h - 32
    if view == 'side':
        c.rect(4, y0 + 4, 8, 22, fab)
        c.rect(4, y0 + 4, 8, 3, fab_l)
        c.rect(4, y0 + 16, 24, 10, fab)
        c.rect(4, y0 + 16, 24, 3, fab_l)
        c.rect(14, y0 + 19, 14, 3, fab_d)
        c.rect(5, y0 + 26, 3, 4, draw.WOOD_D)
        c.rect(24, y0 + 26, 3, 4, draw.WOOD_D)
        return c.outline()
    c.rect(5, y0 + 4, 22, 14, fab)
    c.rect(5, y0 + 4, 22, 3, fab_l)
    if view == 'front':
        c.rect(8, y0 + 8, 16, 10, fab_l)
        c.rect(8, y0 + 16, 16, 2, fab_d)
        c.rect(14, y0 + 8, 1, 8, fab)                 # a seam in the cushion
    c.rect(3, y0 + 12, 6, 14, fab_d)                  # arms
    c.rect(3, y0 + 12, 6, 2, fab)
    c.rect(23, y0 + 12, 6, 14, fab_d)
    c.rect(23, y0 + 12, 6, 2, fab)
    c.rect(9, y0 + 18, 14, 8, fab if view == 'back' else fab_l)
    c.rect(9, y0 + 25, 14, 2, fab_d)
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


def fence_tile(mask):
    c = Canvas(16, 16)
    north = (mask & 1) != 0
    east = (mask & 2) != 0
    south = (mask & 4) != 0
    west = (mask & 8) != 0
    if north:
        c.rect(6, 0, 4, 9, draw.WOOD_D)
        c.rect(7, 0, 2, 9, draw.WOOD)
    if south:
        c.rect(6, 6, 4, 10, draw.WOOD_D)
        c.rect(7, 6, 2, 10, draw.WOOD)
    if west:
        for ry in (4, 9):
            c.rect(0, ry, 8, 3, draw.WOOD)
            c.rect(0, ry, 8, 1, draw.WOOD_L)
    if east:
        for ry in (4, 9):
            c.rect(8, ry, 8, 3, draw.WOOD)
            c.rect(8, ry, 8, 1, draw.WOOD_L)
    if mask == 0:
        for ry in (5, 10):
            c.rect(2, ry, 12, 2, draw.WOOD)
            c.rect(2, ry, 12, 1, draw.WOOD_L)
    c.rect(5, 2, 6, 12, draw.WOOD)
    c.rect(5, 2, 6, 2, draw.WOOD_L)
    c.rect(5, 12, 6, 2, draw.WOOD_D)
    return c.outline()


# (rectangle the placed picture occupies: x, y) -> picture
FURNITURE_VIEWS = [
    ((0, 0), chair_front()), ((16, 0), chair_side()), ((32, 0), chair_back()),
    ((0, 144), armchair('front')), ((32, 140), armchair('side', 36)), ((64, 144), armchair('back')),
    ((0, 208), couch('front')), ((48, 208), couch('side')), ((80, 208), couch('back')),
    ((0, 569), table()), ((48, 640), bookcase()), ((48, 733), lamp()), ((0, 688), plant()),
]
FENCE_TILES = [(((m % 8) * 16, 64 + (m // 8) * 16), fence_tile(m)) for m in range(16)]
CRAFTABLES = [((0, 32), scarecrow()), ((80, 32), furnace()), ((96, 32), furnace(lit=True))] + FENCE_TILES
CHEST = (32, 512)    # the chest is Tiny Dungeon's closed chest (tile 5, 7), standing at the foot of the 16 x 32 cell


# ---- Farmhouse furniture and decor band (below the old sheet, y >= FURNITURE_BASE_H) ----
CUSHION = (198, 101, 39)        # Kenney orange (Roguelike Indoors), the cushion and the wardrobe doors
CUSHION_L = (230, 140, 80)
RUG_D = (120, 40, 40)           # the dark edge of a red rug
BLUE_RUG_D = (30, 70, 130)
GREEN_RUG_D = (38, 92, 66)
ROOF_OUT = (63, 38, 49)


def ring(c, cx, cy, ro, ri, col):
    for y in range(c.height):
        for x in range(c.width):
            d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
            if ri <= d <= ro:
                c.dot(x, y, col)
    return c


def stitched(src, key, rows):
    """A picture stitched from one pack sheet: rows of (column, row) cells, left to right, top to bottom."""
    sheet = src.sheet(key)
    out = Image(16 * len(rows[0]), 16 * len(rows))
    for j, row in enumerate(rows):
        for i, (col, r) in enumerate(row):
            out.paste(sheet.cell(col, r), 16 * i, 16 * j)
    return out


def stool():
    c = Canvas(16, 16)
    c.rect(3, 4, 10, 4, draw.WOOD_L)
    c.rect(3, 4, 10, 1, draw.CREAM)
    c.rect(3, 8, 10, 2, draw.WOOD)
    c.rect(4, 10, 2, 5, draw.WOOD_D)
    c.rect(10, 10, 2, 5, draw.WOOD_D)
    return c.outline()


def cushioned_chair(view):
    c = Canvas(16, 32)
    if view == 'front':
        c.rect(3, 3, 10, 11, draw.WOOD)
        c.rect(3, 3, 10, 2, draw.WOOD_L)
        c.rect(4, 6, 8, 6, CUSHION)                 # back cushion
        c.rect(4, 6, 8, 1, CUSHION_L)
        c.rect(2, 14, 12, 4, CUSHION)               # seat cushion
        c.rect(2, 14, 12, 1, CUSHION_L)
        c.rect(2, 18, 12, 2, draw.WOOD)
        c.rect(3, 20, 2, 9, draw.WOOD_D)
        c.rect(11, 20, 2, 9, draw.WOOD_D)
    elif view == 'side':
        c.rect(2, 3, 3, 17, draw.WOOD)
        c.rect(2, 3, 1, 17, draw.WOOD_L)
        c.rect(5, 14, 9, 4, CUSHION)
        c.rect(5, 14, 9, 1, CUSHION_L)
        c.rect(2, 18, 12, 2, draw.WOOD)
        c.rect(2, 20, 2, 9, draw.WOOD_D)
        c.rect(12, 20, 2, 9, draw.WOOD_D)
    else:
        c.rect(3, 3, 10, 17, draw.WOOD)
        c.rect(3, 3, 10, 2, draw.WOOD_L)
        c.rect(4, 6, 8, 10, CUSHION)                # back cushion from behind
        c.rect(2, 17, 12, 3, draw.WOOD_L)
        c.rect(3, 20, 2, 9, draw.WOOD_D)
        c.rect(11, 20, 2, 9, draw.WOOD_D)
    return c.outline()


def bench(view):
    if view == 'side':
        c = Canvas(16, 32)
        c.rect(4, 3, 8, 22, draw.WOOD_L)
        c.rect(4, 3, 2, 22, draw.CREAM)
        for y in (9, 16):
            c.rect(4, y, 8, 1, draw.WOOD)
        c.rect(5, 25, 2, 6, draw.WOOD_D)
        c.rect(9, 25, 2, 6, draw.WOOD_D)
        return c.outline()
    c = Canvas(32, 16)
    c.rect(1, 2, 30, 6, draw.WOOD_L)
    c.rect(1, 2, 30, 1, draw.CREAM)
    for x in (9, 17, 25):
        c.rect(x, 3, 1, 4, draw.WOOD)
    c.rect(1, 8, 30, 3, draw.WOOD)
    c.rect(3, 11, 3, 4, draw.WOOD_D)
    c.rect(26, 11, 3, 4, draw.WOOD_D)
    if view == 'back':
        c.rect(1, 2, 30, 6, draw.WOOD)
        c.rect(1, 2, 30, 1, draw.WOOD_L)
    return c.outline()


def round_table():
    c = Canvas(32, 32)
    c.ellipse(16, 13, 15, 10, draw.WOOD)
    c.ellipse(16, 11, 15, 9, draw.WOOD_L)
    c.ellipse(16, 11, 12, 6, draw.CREAM)           # the tablecloth-free top, a lighter centre
    c.rect(14, 19, 4, 9, draw.WOOD_D)
    c.rect(9, 27, 14, 3, draw.WOOD_D)
    return c.outline()


def side_table():
    c = Canvas(16, 16)
    c.rect(2, 4, 12, 4, draw.WOOD_L)
    c.rect(2, 4, 12, 1, draw.CREAM)
    c.rect(2, 8, 12, 2, draw.WOOD)
    c.rect(3, 10, 2, 5, draw.WOOD_D)
    c.rect(11, 10, 2, 5, draw.WOOD_D)
    return c.outline()


def long_table():
    c = Canvas(48, 32)
    c.rect(1, 4, 46, 12, draw.WOOD_L)
    c.rect(1, 4, 46, 2, draw.CREAM)
    for x in range(8, 46, 8):
        c.rect(x, 6, 1, 9, draw.WOOD)
    c.rect(1, 16, 46, 5, draw.WOOD)
    c.rect(3, 21, 3, 10, draw.WOOD_D)
    c.rect(22, 21, 3, 10, draw.WOOD_D)
    c.rect(42, 21, 3, 10, draw.WOOD_D)
    return c.outline()


def bed(w, h, pillows):
    """A bed: wooden frame and headboard, a red or blue blanket folded at the top, one pillow per half."""
    c = Canvas(w, h)
    c.rect(2, 2, w - 4, h - 4, draw.WOOD_D)
    c.rect(3, 3, w - 6, 6, draw.WOOD)              # headboard
    c.rect(3, 3, w - 6, 1, draw.WOOD_L)
    c.rect(3, 9, w - 6, h - 14, draw.BLUE if w > 32 else draw.RED)
    c.rect(3, 22, w - 6, 3, draw.CREAM)            # the fold of the blanket
    step = (w - 6) // pillows
    for i in range(pillows):
        c.rect(5 + i * step, 10, step - 4, 6, draw.CREAM)
    c.rect(3, h - 6, w - 6, 3, draw.WOOD)          # footboard
    return c.outline()


def wall_clock():
    c = Canvas(16, 16)
    c.ellipse(8, 8, 7, 7, draw.WOOD_D)
    c.ellipse(8, 8, 6, 6, draw.CREAM)
    c.line(8, 8, 8, 4, draw.OUT)
    c.line(8, 8, 11, 8, draw.OUT)
    c.dot(8, 8, draw.OUT)
    for x, y in ((8, 3), (13, 8), (8, 13), (3, 8)):
        c.dot(x, y, draw.WOOD_D)
    return c.outline()


def window():
    c = Canvas(32, 32)
    c.rect(0, 0, 32, 30, draw.WOOD)
    c.rect(0, 0, 32, 2, draw.WOOD_L)
    c.rect(3, 4, 26, 22, draw.BLUE_L)
    c.rect(3, 4, 26, 3, draw.BLUE)
    c.rect(15, 4, 2, 22, draw.WOOD)
    c.rect(3, 14, 26, 2, draw.WOOD)
    c.rect(20, 7, 5, 2, (255, 255, 255))
    c.rect(22, 6, 4, 2, (255, 255, 255))
    c.rect(0, 28, 32, 4, draw.WOOD_L)
    return c.outline()


def mirror():
    c = Canvas(16, 32)
    c.rect(1, 1, 14, 30, draw.WOOD_L)
    c.rect(1, 1, 14, 2, draw.GOLD_L)
    c.rect(3, 4, 10, 25, draw.STEEL_L)
    c.line(4, 27, 12, 5, draw.CREAM)
    c.line(6, 27, 12, 11, draw.CREAM)
    return c.outline()


def table_lamp():
    c = Canvas(16, 16)
    c.rect(3, 3, 10, 5, draw.GOLD)
    c.rect(4, 2, 8, 1, draw.GOLD_L)
    c.rect(3, 3, 10, 1, draw.GOLD_L)
    c.rect(7, 8, 2, 3, draw.WOOD_D)
    c.rect(4, 11, 8, 3, draw.WOOD)
    c.rect(4, 11, 8, 1, draw.WOOD_L)
    return c.outline()


def wall_sconce():
    c = Canvas(16, 16)
    c.rect(5, 8, 6, 6, draw.STEEL)
    c.rect(5, 8, 6, 1, draw.STEEL_L)
    c.rect(7, 4, 2, 4, draw.CREAM)
    c.rect(6, 2, 4, 2, draw.GOLD)
    c.dot(7, 1, draw.GOLD_L)
    c.dot(8, 1, draw.GOLD_L)
    return c.outline()


def paper_lantern():
    c = Canvas(16, 16)
    c.ellipse(8, 8, 6, 7, draw.RED)
    c.ellipse(8, 8, 3, 5, draw.GOLD)
    for x in (4, 12):
        c.rect(x, 3, 1, 10, (200, 40, 40))
    c.rect(5, 0, 6, 2, draw.WOOD_D)
    c.rect(5, 14, 6, 2, draw.WOOD_D)
    return c.outline()


def potted_fern():
    c = Canvas(16, 32)
    for (x1, y1), col in (((2, 8), draw.GREEN_D), ((14, 8), draw.GREEN_D), ((4, 4), draw.GREEN), ((12, 4), draw.GREEN),
                          ((8, 2), draw.GREEN_L), ((1, 14), draw.GREEN), ((15, 14), draw.GREEN)):
        c.line(8, 20, x1, y1, col)
        c.line(8, 19, x1 + 1, y1 + 1, col)
    c.rect(4, 22, 8, 8, draw.WOOD)
    c.rect(3, 21, 10, 2, draw.WOOD_L)
    c.rect(5, 30, 6, 2, draw.WOOD_D)
    return c.outline()


def cactus_pot():
    c = Canvas(16, 16)
    c.rect(7, 2, 2, 8, draw.GREEN)
    c.rect(7, 2, 1, 8, draw.GREEN_L)
    c.rect(4, 6, 3, 2, draw.GREEN)
    c.rect(4, 4, 2, 3, draw.GREEN)
    c.rect(9, 5, 3, 2, draw.GREEN)
    c.rect(10, 3, 2, 3, draw.GREEN)
    c.dot(8, 1, draw.RED)
    c.rect(4, 11, 8, 4, draw.WOOD)
    c.rect(3, 10, 10, 2, draw.WOOD_L)
    return c.outline()


def flower_box():
    c = Canvas(16, 16)
    for x, col in ((2, draw.RED), (5, draw.GOLD), (8, draw.CREAM), (11, draw.BLUE)):
        c.rect(x, 6, 2, 2, col)
        c.rect(x, 8, 2, 1, draw.GREEN)
    c.rect(1, 9, 14, 1, draw.GREEN_D)
    c.rect(0, 10, 16, 5, draw.WOOD)
    c.rect(0, 10, 16, 1, draw.WOOD_L)
    c.rect(0, 14, 16, 1, draw.WOOD_D)
    return c.outline()


def bonsai():
    c = Canvas(16, 16)
    c.ellipse(8, 6, 5, 4, draw.GREEN)
    c.dot(6, 4, draw.GREEN_L)
    c.dot(10, 5, draw.GREEN_L)
    c.dot(8, 3, draw.GREEN_L)
    c.rect(7, 9, 2, 2, draw.WOOD_D)
    c.rect(4, 11, 8, 4, (140, 90, 60))
    c.rect(3, 10, 10, 2, (170, 110, 80))
    return c.outline()


def pumpkin():
    c = Canvas(16, 16)
    c.ellipse(8, 9, 6, 5, (230, 120, 40))
    for x in (4, 8, 12):
        c.rect(x, 6, 1, 6, (180, 80, 30))
    c.rect(7, 2, 2, 3, draw.GREEN_D)
    c.dot(9, 3, draw.GREEN)
    return c.outline()


def wreath():
    c = Canvas(16, 16)
    ring(c, 8, 8, 7.5, 5, draw.GREEN_D)
    ring(c, 8, 8, 6.5, 5.5, draw.GREEN)
    c.rect(5, 1, 2, 2, draw.RED)
    c.rect(9, 12, 2, 2, draw.RED)
    c.rect(11, 3, 2, 2, draw.GOLD)
    return c.outline()


def rug(w, h, body, edge, pattern):
    """A flat floor rug: the body, a dark edge, an inner border and a row of pattern dots. No outline: it lies on the floor."""
    c = Canvas(w, h)
    c.rect(0, 0, w, h, edge)
    c.rect(1, 1, w - 2, h - 2, body)
    c.rect(3, 3, w - 6, h - 6, edge)
    c.rect(4, 4, w - 8, h - 8, body)
    for x in range(6, w - 6, 4):
        c.dot(x, h // 2, pattern)
        c.dot(x + 2, h // 2 - 2, pattern)
        c.dot(x + 2, h // 2 + 2, pattern)
    return c


def round_rug():
    c = Canvas(32, 32)
    c.ellipse(16, 16, 15, 15, GREEN_RUG_D)
    c.ellipse(16, 16, 13, 13, draw.GREEN)
    c.ellipse(16, 16, 10, 10, draw.GREEN_L)
    c.ellipse(16, 16, 6, 6, draw.GREEN)
    return c


def doormat():
    c = Canvas(16, 16)
    c.rect(0, 0, 16, 16, draw.WOOD_D)
    c.rect(1, 1, 14, 14, draw.WOOD)
    for x in range(3, 14, 3):
        c.rect(x, 3, 1, 10, draw.WOOD_D)
    return c


def blue_rug_picture():
    return rug(48, 32, draw.BLUE, BLUE_RUG_D, draw.BLUE_L)


def red_rug_picture():
    return rug(32, 32, draw.RED, RUG_D, draw.GOLD_L)


# The band: each key -> its rectangles (w, h, footprint w, footprint h) per view in rotation order (DOWN, RIGHT, UP; LEFT is RIGHT mirrored
# when flip_left is set). A single-view piece has one entry.
NEW_PIECES = {
    'wooden_stool': dict(views=[(16, 16, 1, 1)]),
    'cushioned_chair': dict(views=[(16, 32, 1, 1)] * 3, flip_left=True),
    'wooden_bench': dict(views=[(32, 16, 2, 1), (16, 32, 1, 2), (32, 16, 2, 1)], flip_left=True),
    'green_armchair': dict(views=[(32, 32, 2, 1), (32, 36, 2, 1), (32, 32, 2, 1)], flip_left=True),
    'round_table': dict(views=[(32, 32, 2, 2)]),
    'side_table': dict(views=[(16, 16, 1, 1)]),
    'long_table': dict(views=[(48, 32, 3, 1)]),
    'single_bed': dict(views=[(32, 48, 2, 2)]),
    'double_bed': dict(views=[(48, 48, 3, 2)]),
    'wardrobe': dict(views=[(32, 48, 2, 1)]),
    'chest_of_drawers': dict(views=[(32, 32, 2, 1)]),
    'kitchen_cabinet': dict(views=[(16, 32, 1, 1)]),
    'wall_shelf': dict(views=[(32, 16, 2, 1)]),
    'candelabra': dict(views=[(16, 32, 1, 1)]),
    'table_lamp': dict(views=[(16, 16, 1, 1)]),
    'wall_sconce': dict(views=[(16, 16, 1, 1)]),
    'paper_lantern': dict(views=[(16, 16, 1, 1)]),
    'potted_fern': dict(views=[(16, 32, 1, 1)]),
    'cactus_pot': dict(views=[(16, 16, 1, 1)]),
    'flower_box': dict(views=[(16, 16, 1, 1)]),
    'bonsai': dict(views=[(16, 16, 1, 1)]),
    'landscape_painting': dict(views=[(32, 16, 2, 1)]),
    'portrait_painting': dict(views=[(16, 16, 1, 1)]),
    'window': dict(views=[(32, 32, 2, 1)]),
    'wall_clock': dict(views=[(16, 16, 1, 1)]),
    'mirror': dict(views=[(16, 32, 1, 1)]),
    'red_rug': dict(views=[(32, 32, 2, 2)]),
    'blue_rug': dict(views=[(48, 32, 3, 2)]),
    'round_rug': dict(views=[(32, 32, 2, 2)]),
    'doormat': dict(views=[(16, 16, 1, 1)]),
    'pumpkin_decor': dict(views=[(16, 16, 1, 1)]),
    'winter_wreath': dict(views=[(16, 16, 1, 1)]),
}
BAND_TOP = 1488          # the old sheet's height: the band starts below it, on the 16 px grid
SHEET_W = 512


def _slot(w, h):
    """The cell a picture takes on the 16 px grid: its size plus a 1 px transparent gap on the right and bottom, rounded up to 16."""
    return -(-(w + 1) // 16) * 16, -(-(h + 1) // 16) * 16


def band_layout():
    """{(key, view index): (x, y, w, h)} for the band, shelf-packed tallest first; the band's height is the last row's bottom."""
    items = []
    for key, spec in NEW_PIECES.items():
        for vi, (w, h, _, _) in enumerate(spec['views']):
            sw, sh = _slot(w, h)
            items.append((-sh, -sw, key, vi, w, h))
    items.sort()
    place = {}
    x, y, row_h = 0, BAND_TOP, 0
    for neg_sh, neg_sw, key, vi, w, h in items:
        sw, sh = -neg_sw, -neg_sh
        if x + sw > SHEET_W:
            x, y, row_h = 0, y + row_h, 0
        place[(key, vi)] = (x, y, w, h)
        x += sw
        row_h = max(row_h, sh)
    return place, y + row_h


FURNITURE_HEIGHT = band_layout()[1]


def wardrobe():
    c = Canvas(32, 48)
    c.rect(1, 1, 30, 40, draw.WOOD)
    c.rect(1, 1, 30, 2, draw.WOOD_L)
    for x0 in (3, 17):
        c.rect(x0, 4, 12, 36, draw.WOOD_L)
        c.rect(x0 + 1, 6, 10, 32, draw.WOOD)
        c.rect(x0 + 2, 8, 8, 1, draw.WOOD_D)
    c.rect(14, 22, 1, 5, draw.GOLD)
    c.rect(17, 22, 1, 5, draw.GOLD)
    c.rect(2, 41, 4, 5, draw.WOOD_D)
    c.rect(26, 41, 4, 5, draw.WOOD_D)
    return c.outline()


def band_pictures(src):
    """{(key, view index): picture} for every drawn view of the band."""
    p = {
        'wooden_stool': [stool()],
        'cushioned_chair': [cushioned_chair('front'), cushioned_chair('side'), cushioned_chair('back')],
        'wooden_bench': [bench('front'), bench('side'), bench('back')],
        'green_armchair': [armchair('front', 32, draw.GREEN, draw.GREEN_L, draw.GREEN_D), armchair('side', 36, draw.GREEN, draw.GREEN_L, draw.GREEN_D),
                           armchair('back', 32, draw.GREEN, draw.GREEN_L, draw.GREEN_D)],
        'round_table': [round_table()],
        'side_table': [side_table()],
        'long_table': [long_table()],
        'single_bed': [bed(32, 48, 1)],
        'double_bed': [bed(48, 48, 2)],
        'wardrobe': [wardrobe()],
        'chest_of_drawers': [stitched(src, 'roguelike_indoors', [[(0, 15), (1, 15)], [(0, 16), (1, 16)]])],
        'kitchen_cabinet': [stitched(src, 'roguelike_indoors', [[(0, 12)], [(0, 16)]])],
        'wall_shelf': [stitched(src, 'roguelike_indoors', [[(16, 1), (18, 1)]])],
        'candelabra': [stitched(src, 'roguelike_indoors', [[(19, 4)], [(19, 5)]])],
        'table_lamp': [table_lamp()],
        'wall_sconce': [wall_sconce()],
        'paper_lantern': [paper_lantern()],
        'potted_fern': [potted_fern()],
        'cactus_pot': [cactus_pot()],
        'flower_box': [flower_box()],
        'bonsai': [bonsai()],
        'landscape_painting': [stitched(src, 'roguelike_indoors', [[(20, 12), (21, 12)]])],
        'portrait_painting': [stitched(src, 'roguelike_indoors', [[(19, 13)]])],
        'window': [window()],
        'wall_clock': [wall_clock()],
        'mirror': [stitched(src, 'roguelike_indoors', [[(22, 14)], [(22, 15)]])],
        'red_rug': [red_rug_picture()],
        'blue_rug': [blue_rug_picture()],
        'round_rug': [round_rug()],
        'doormat': [doormat()],
        'pumpkin_decor': [pumpkin()],
        'winter_wreath': [wreath()],
    }
    out = {}
    for key, pics in p.items():
        for vi, pic in enumerate(pics):
            w, h = NEW_PIECES[key]['views'][vi][:2]
            assert (pic.width, pic.height) == (w, h), (key, vi, pic.width, pic.height, w, h)
            out[(key, vi)] = pic
    return out


def build(src, size):
    furniture = Image(SHEET_W, FURNITURE_HEIGHT)
    for (x, y), pic in FURNITURE_VIEWS:
        furniture.paste(pic, x, y)
    place = band_layout()[0]
    for key_view, pic in band_pictures(src).items():
        x, y, _, _ = place[key_view]
        furniture.paste(pic, x, y)
    craftables = Image(*size('sprites/craftables.png'))
    for (x, y), pic in CRAFTABLES:
        craftables.paste(pic, x, y)
    craftables.paste(src.sheet('dungeon').cell(5, 7), CHEST[0], CHEST[1] + 14)
    return {'sprites/furniture.png': furniture, 'sprites/craftables.png': craftables}
