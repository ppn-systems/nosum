"""Package 6: TerrainFeatures/tree{1,2,3}_spring.png (oak, maple, pine).

The pine, oak and maple are drawn here (Kenney's trees are 16 x 32, the game draws a 48 x 96 canopy and trunk): leaf blobs shaded from the
top left on the Kenney palette, with a 1 px outline. The stump, bush, sapling and seed pictures sit at the cells NodeSprites reads.
"""
import draw
from draw import Canvas
from pngio import Image

# (dark, base, light) leaf tones and (shadow, bark, light) trunk tones per tree.
OAK = dict(leaf=((38, 92, 66), (62, 137, 72), (99, 199, 77)), bark=((119, 56, 51), (150, 84, 60), (189, 108, 74)))
MAPLE = dict(leaf=((160, 60, 40), (222, 100, 52), (254, 174, 52)), bark=((110, 56, 51), (140, 80, 60), (180, 108, 74)))
PINE = dict(leaf=((24, 70, 60), (38, 110, 72), (70, 165, 80)), bark=((100, 52, 50), (130, 76, 58), (170, 100, 70)))


def _noise(x, y):
    return (x * 73856093 ^ y * 19349663) % 11


def blob(c, cx, cy, rx, ry, tones, texture=True):
    dark, base, light = tones
    for y in range(c.height):
        for x in range(c.width):
            if ((x - cx + 0.5) / rx) ** 2 + ((y - cy + 0.5) / ry) ** 2 <= 1:
                t = -0.7 * (x - cx) / rx - 0.7 * (y - cy) / ry     # light from the top left
                n = _noise(x, y) if texture else 9
                col = light if t > 0.42 else dark if t < -0.42 else base
                if n == 0:
                    col = dark if col != dark else base
                elif n == 1 and t > 0.1:
                    col = light
                c.dot(x, y, col)


def trunk(c, cx, y0, y1, w, tones, flare=True):
    shadow, bark, light = tones
    for y in range(y0, y1 + 1):
        grow = 2 if flare and y >= y1 - 2 else 1 if flare and y >= y1 - 4 else 0
        left, right = cx - w // 2 - grow, cx + w // 2 + grow
        for x in range(left, right):
            col = bark
            if x <= left + 1 + grow:
                col = light
            elif x >= right - 2:
                col = shadow
            if (x + y * 2) % 7 == 0 and col == bark:
                col = shadow
            c.dot(x, y, col)


def big_tree(tones, kind):
    c = Canvas(48, 96)
    if kind == 'pine':
        leaf = tones['leaf']
        for i, (cy, rx) in enumerate(((52, 22), (40, 18), (28, 14), (17, 10), (8, 6))):
            blob(c, 24, cy, rx, 12 if i < 4 else 8, leaf)
    else:
        leaf = tones['leaf']
        for cx, cy, rx, ry in ((24, 38, 22, 20), (13, 40, 11, 13), (35, 40, 11, 13), (24, 20, 16, 14), (14, 26, 11, 11), (34, 26, 11, 11), (24, 52, 15, 8)):
            blob(c, cx, cy, rx, ry, leaf)
    trunk(c, 24, 56, 95, 8, tones["bark"])
    # The canopy overlaps the trunk top; the outline must not cut the trunk's foot off the sheet.
    c.outline()
    return c


def stump(tones):
    c = Canvas(16, 32)
    shadow, bark, light = tones['bark']
    trunk(c, 8, 22, 31, 10, tones['bark'])
    for y in range(18, 23):
        for x in range(2, 14):
            if ((x - 7.5) / 6) ** 2 + ((y - 20) / 3) ** 2 <= 1:
                c.dot(x, y, light if (x + y) % 4 else bark)
    c.rect(6, 19, 4, 2, bark)
    c.dot(7, 20, shadow)
    return c.outline()


def bush(tones):
    c = Canvas(16, 32)
    trunk(c, 8, 18, 31, 2, tones['bark'], flare=False)
    blob(c, 8, 12, 7, 7, tones['leaf'])
    blob(c, 5, 15, 4, 4, tones['leaf'])
    return c.outline()


def sapling(tones):
    c = Canvas()
    trunk(c, 8, 8, 15, 2, tones['bark'], flare=False)
    blob(c, 8, 6, 4, 4, tones['leaf'])
    return c.outline()


def seed():
    c = Canvas()
    for y in range(8, 16):
        for x in range(16):
            if ((x - 7.5) / 7) ** 2 + ((y - 15) / 6.5) ** 2 <= 1:
                c.dot(x, y, draw.WOOD if (x + y) % 5 else draw.WOOD_L)
    c.dot(5, 13, draw.WOOD_D)
    c.dot(10, 12, draw.WOOD_D)
    return c.outline()


def tree_sheet(size, tones, kind):
    out = Image(*size)
    out.paste(big_tree(tones, kind), 0, 0)
    out.paste(bush(tones), 0, 96)
    out.paste(stump(tones), 32, 96)
    out.paste(sapling(tones), 16, 128)
    out.paste(seed(), 32, 128)
    return out


def build(size):
    """{path: Image}: the three tree sheets.

    Maps/Mines/mine.png is not here: it holds the mine's floor tiles (package 11, terrain) and the ladder cell NodeSprites draws, so it
    is replaced with the mine maps. Kenney Tiny Town tile 103 (a ladder in a stone frame) is a candidate for the ladder at cell (3, 4).
    """
    return {
        'TerrainFeatures/tree1_spring.png': tree_sheet(size('TerrainFeatures/tree1_spring.png'), OAK, 'oak'),
        'TerrainFeatures/tree2_spring.png': tree_sheet(size('TerrainFeatures/tree2_spring.png'), MAPLE, 'maple'),
        'TerrainFeatures/tree3_spring.png': tree_sheet(size('TerrainFeatures/tree3_spring.png'), PINE, 'pine'),
    }
