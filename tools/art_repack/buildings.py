"""Package 7: the five sprites/buildings/ sheets, composed from Kenney Tiny Town house tiles and one piece drawn here."""
import draw
from draw import Canvas
from pngio import Image, recolor

# Tiny Town tile indices (12 columns). Roofs: left end, middle, right end of the top row and of the eave row.
ROOF = {
    'red': dict(top=(52, 53, 54), eave=(64, 65, 66)),
    'slate': dict(top=(48, 49, 50), eave=(60, 61, 62)),
}
# Walls: left end, middle, right end, window, single door, left and right half of a double door.
WALL = {
    'brown': dict(ends=(72, 73, 75), win=84, door=85, dbl=(86, 87)),
    'grey': dict(ends=(76, 77, 79), win=88, door=89, dbl=(90, 91)),
}


class Builder:
    def __init__(self, src, width, height):
        sheet = src.sheet('town')
        self.tile = lambda i: sheet.cell(i % 12, i // 12)
        self.img = Image(width, height)

    def put(self, i, col, row):
        self.img.paste(self.tile(i), col * 16, row * 16)

    def roof(self, col, row, w, h, kind):
        """A roof w x h tiles: the top row, the shingle pattern stretched over the middle rows, the eave row."""
        top, eave = ROOF[kind]['top'], ROOF[kind]['eave']
        for c in range(w):
            k = 0 if c == 0 else 2 if c == w - 1 else 1
            self.put(top[k], col + c, row)
            body = self.tile(top[k])
            filler = Image(16, 16)
            for y in range(16):
                filler.px[y] = body.px[8 + y % 8][:]     # lower half of the top tile: shingles without the ledge
            for r in range(1, h - 1):
                self.img.paste(filler, (col + c) * 16, (row + r) * 16)
            self.put(eave[k], col + c, row + h - 1)

    def wall_row(self, col, row, w, kind, windows=(), door=None, double=False):
        k = WALL[kind]
        for c in range(w):
            self.put(k['ends'][0 if c == 0 else 2 if c == w - 1 else 1], col + c, row)
        for c in windows:
            self.put(k['win'], col + c, row)
        if door is not None:
            if double:
                self.put(k['dbl'][0], col + door, row)
                self.put(k['dbl'][1], col + door + 1, row)
            else:
                self.put(k['door'], col + door, row)


def farmhouse(src):
    b = Builder(src, 144, 144)
    b.roof(0, 0, 9, 4, 'red')
    for r in (4, 6, 7):
        b.wall_row(0, r, 9, 'brown')
    b.wall_row(0, 5, 9, 'brown', windows=(1, 4, 7))
    b.wall_row(0, 8, 9, 'brown', windows=(1, 7), door=4)
    return b.img


def glass_tile(edge):
    """A 16 x 16 piece of glass wall: 8 px panes in a wooden frame; edge is -1 for the left end, 1 for the right end, 0 inside."""
    c = Canvas()
    c.rect(0, 0, 16, 16, (172, 222, 238))
    for x in (3, 4, 11, 12):
        c.rect(x, 0, 1, 16, (214, 240, 248))        # a highlight streak per pane
    for x in (0, 7, 8, 15):
        c.rect(x, 0, 1, 16, draw.WOOD_D if x in (7, 8) else draw.WOOD)
    c.rect(0, 7, 16, 2, draw.WOOD)
    c.rect(0, 7, 16, 1, draw.WOOD_L)
    if edge < 0:
        c.rect(0, 0, 1, 16, draw.OUT)
    if edge > 0:
        c.rect(15, 0, 1, 16, draw.OUT)
    return c


def greenhouse(src):
    b = Builder(src, 112, 160)
    b.roof(0, 0, 7, 3, 'slate')
    for r in range(3, 9):
        for c in range(7):
            b.img.paste(glass_tile(-1 if c == 0 else 1 if c == 6 else 0), c * 16, r * 16)
    b.wall_row(0, 9, 7, 'grey', door=3)
    return b.img


class FarmBuilder(Builder):
    """Adds Tiny Farm's red barn wall: three-tile rows of cross-braced boards, plank rows, a braced door and a window."""

    def __init__(self, src, width, height):
        super().__init__(src, width, height)
        farm = src.sheet('farm')
        self.farm = lambda i: farm.cell(i % 12, i // 12)

    def board_row(self, col, row, w, first, recipe=None):
        """One wall row w tiles wide from the three Tiny Farm tiles first, first + 1, first + 2 (left end, middle, right end)."""
        for c in range(w):
            pic = self.farm(first + (0 if c == 0 else 2 if c == w - 1 else 1))
            self.img.paste(pic.map_pixels(recipe) if recipe else pic, (col + c) * 16, row * 16)

    def put_farm(self, i, col, row, recipe=None):
        pic = self.farm(i)
        self.img.paste(pic.map_pixels(recipe) if recipe else pic, col * 16, row * 16)


def barn(src):
    b = FarmBuilder(src, 112, 112)
    b.roof(0, 0, 7, 3, 'red')
    b.board_row(0, 3, 7, 90)
    b.board_row(0, 4, 7, 102)
    b.put_farm(128, 3, 4)
    b.board_row(0, 5, 7, 114)
    b.board_row(0, 6, 7, 114)
    for col in (1, 4):
        b.put_farm(126, col, 6)
        b.put_farm(126, col + 1, 6)
    return b.img


COOP_WOOD = recolor(0.06, sat=0.85, val=0.82)     # the barn's red boards turned to warm pine


def coop(src):
    b = FarmBuilder(src, 96, 112)
    b.roof(0, 0, 6, 3, 'slate')
    b.board_row(0, 3, 6, 102, COOP_WOOD)
    b.board_row(0, 4, 6, 102, COOP_WOOD)
    b.board_row(0, 5, 6, 114, COOP_WOOD)
    b.board_row(0, 6, 6, 114, COOP_WOOD)
    for col in (1, 4):
        b.put_farm(128, col, 4, COOP_WOOD)      # windows
    b.put_farm(128, 4, 6, COOP_WOOD)
    b.put_farm(127, 2, 6, COOP_WOOD)            # the door
    return b.img


def shipping_bin():
    c = Canvas(32, 32)
    c.rect(2, 12, 28, 18, draw.WOOD)
    c.rect(2, 12, 28, 3, draw.WOOD_L)
    for x in (2, 10, 18, 26):
        c.rect(x, 12, 4, 18, draw.WOOD_D)           # upright planks' shadows
    for y in (18, 24):
        c.rect(2, y, 28, 1, draw.WOOD_D)
    c.rect(0, 8, 32, 5, draw.WOOD_L)                # the lid
    c.rect(0, 8, 32, 1, draw.CREAM)
    c.rect(0, 12, 32, 1, draw.WOOD_D)
    c.rect(11, 17, 10, 6, draw.OUT)                 # the slot
    c.rect(12, 18, 8, 4, (30, 18, 24))
    return c.outline()


def build(src, size):
    """{path under assets/: Image} for the Buildings sheets; each sheet is transparent but for the rectangle the game draws."""
    out = {}
    for name, (img, x, y) in {
        'sprites/buildings/farmhouse.png': (farmhouse(src), 16, 0),
        'sprites/buildings/greenhouse.png': (greenhouse(src), 0, 160),
        'sprites/buildings/barn.png': (barn(src), 0, 0),
        'sprites/buildings/coop.png': (coop(src), 0, 0),
        'sprites/buildings/shipping_bin.png': (shipping_bin(), 0, 0),
    }.items():
        sheet = Image(*size(name))
        sheet.paste(img, x, y)
        out[name] = sheet
    return out
