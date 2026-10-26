"""Package 9: the UI sheets (ui/menu.png, ui/cursors.png, ui/text_box.png, ui/start_background.png).

Window frames, slots, buttons, the pictograms and the cursors are drawn here in the warm wood-and-parchment palette at the rectangles
nosum-theme.js reads (CSS border-image slices them, so each piece keeps its slice width). The start-screen picture is a sky and a sea drawn
from dithered bands and shaded cloud blobs. No CC0 pack has the nine-slice pieces at these sizes without re-slicing, and none matches the palette.
"""
import draw
from draw import Canvas
from pngio import Image
from trees import blob
import animals

OUT = draw.OUT
PARCH = (244, 228, 192)
PARCH_D = (222, 198, 150)
PEACH = (250, 214, 168)
PEACH_D = (226, 176, 126)


def box(w, h, bands, corner=True):
    """A w x h frame: bands is a list of colours from the outside in (None = transparent); the middle stays transparent."""
    c = Canvas(w, h)
    for y in range(h):
        for x in range(w):
            d = min(x, y, w - 1 - x, h - 1 - y)
            if d < len(bands) and bands[d] is not None:
                c.dot(x, y, bands[d])
    if corner:
        for x, y in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
            c.px[y][x] = (0, 0, 0, 0)
    return c


def bevel(w, h, depth, tl, br, outline=OUT, fill=None):
    """A box with a light and a dark bevel: tl on the top and left, br on the bottom and right; outline outside it."""
    c = Canvas(w, h)
    for y in range(h):
        for x in range(w):
            d = min(x, y, w - 1 - x, h - 1 - y)
            if d == 0:
                c.dot(x, y, outline)
            elif d <= depth:
                top_left = min(x, y) < min(w - 1 - x, h - 1 - y)
                c.dot(x, y, tl if top_left else br)
            elif fill is not None:
                c.dot(x, y, fill)
    for x, y in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        c.px[y][x] = (0, 0, 0, 0)
    return c


def panel():
    return box(60, 60, [OUT, draw.WOOD_L, draw.WOOD, draw.WOOD, draw.WOOD_D, OUT, PARCH_D, PARCH_D])


def inset():
    return bevel(60, 60, 2, (252, 240, 210), (190, 150, 100), outline=OUT)


def slot():
    return bevel(64, 64, 2, (250, 232, 196), (150, 100, 64))


def select():
    return box(64, 64, [OUT, (255, 130, 110), draw.RED, draw.RED, (160, 30, 40), OUT])


def field():
    return bevel(188, 48, 2, (200, 150, 100), (255, 232, 200), fill=PEACH)


def pill(w, h, base, light, dark):
    c = Canvas(w, h)
    for y in range(h):
        for x in range(w):
            d = min(x, y, w - 1 - x, h - 1 - y)
            if d == 0:
                c.dot(x, y, OUT)
            elif y == 1:
                c.dot(x, y, light)
            elif y >= h - 3:
                c.dot(x, y, dark)
            else:
                c.dot(x, y, base)
    for x, y in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        c.px[y][x] = (0, 0, 0, 0)
    return c


def close_button():
    c = Canvas(12, 12)
    c.rect(0, 0, 12, 12, OUT)
    c.rect(1, 1, 10, 10, draw.WOOD)
    c.rect(1, 1, 10, 1, draw.WOOD_L)
    c.rect(1, 10, 10, 1, draw.WOOD_D)
    for i in range(6):
        c.dot(3 + i, 3 + i, draw.RED)
        c.dot(8 - i, 3 + i, draw.RED)
    for x, y in ((0, 0), (11, 0), (0, 11), (11, 11)):
        c.px[y][x] = (0, 0, 0, 0)
    return c


def arrow(left):
    rows = ['....#.......', '...#W#......', '..#WWW#####.', '.#WWWWWWWW#.', '#WWWWWWWWWW#', '#WWWWWWWWWW#', '.#WWWWWWWW#.', '..#WWW#####.', '...#W#......', '....#.......', '............']
    rows = [r.ljust(12, '.')[:12] for r in rows]
    c = Canvas(12, 11)
    c.art(rows, {'#': OUT, 'W': draw.WOOD_L})
    return c if left else c.flip_h()


def coin():
    c = Canvas(9, 9)
    blob(c, 4, 4, 4, 4, ((190, 120, 30), (254, 190, 52), (255, 240, 150)))
    c.rect(4, 2, 1, 5, (190, 120, 30))
    return c.outline()


def plate():
    c = Canvas()
    c.rect(0, 0, 16, 16, OUT)
    c.rect(1, 1, 14, 14, (214, 170, 110))
    c.rect(1, 1, 14, 1, draw.WOOD_L)
    c.rect(1, 14, 14, 1, draw.WOOD)
    for x, y in ((0, 0), (15, 0), (0, 15), (15, 15)):
        c.px[y][x] = (0, 0, 0, 0)
    return c


def tab_inventory():
    c = plate()
    c.rect(4, 6, 8, 6, draw.WOOD_D)
    c.rect(4, 6, 8, 2, draw.WOOD_L)
    c.rect(7, 8, 2, 2, draw.GOLD)
    c.rect(3, 5, 10, 1, OUT)
    return c


def tab_coop():
    c = plate()
    pic = animals.chicken('down', 0)
    c.paste(pic.crop(1, 1, 14, 14), 1, 1)
    return c


def tab_market():
    c = plate()
    for x in range(3, 13, 2):
        c.rect(x, 4, 1, 4, draw.RED)
        c.rect(x + 1, 4, 1, 4, draw.CREAM)
    c.rect(3, 8, 10, 1, OUT)
    c.rect(4, 9, 1, 4, draw.WOOD_D)
    c.rect(11, 9, 1, 4, draw.WOOD_D)
    c.rect(4, 11, 8, 1, draw.WOOD_L)
    return c


def tab_shop():
    c = plate()
    blob(c, 8, 9, 4.2, 4, ((190, 120, 30), (254, 190, 52), (255, 240, 150)))
    c.rect(7, 3, 2, 2, OUT)
    c.rect(6, 5, 4, 1, OUT)
    c.rect(7, 8, 2, 3, draw.OUT)
    return c


def cursor_arrow():
    rows = ['#.......', '##......', '#B#.....', '#BB#....', '#BBB#...', '#BBBB#..', '#BBBBB#.', '#BBB####', '#B#B#...', '##.#B#..']
    c = Canvas(8, 10)
    c.art(rows, {'#': OUT, 'B': draw.WOOD_L})
    return c


def cursor_hand():
    rows = ['...##.....', '..#WW#....', '..#WW#....', '..#WW#.##.', '..#WW##WW#', '###WWWWWW#', '#WWWWWWWW#', '#WWWWWWWW#', '.#WWWWWWW#', '..########']
    c = Canvas(10, 10)
    c.art(rows, {'#': OUT, 'W': (250, 220, 180)})
    return c


def menu_tiles(size):
    sheet = Image(*size('ui/menu.png'))
    sheet.paste(panel(), 0, 256)
    sheet.paste(inset(), 0, 320)
    sheet.paste(slot(), 128, 128)
    return sheet


def cursors(size):
    sheet = Image(*size('ui/cursors.png'))
    for pic, x, y in (
            (select(), 448, 128),
            (pill(13, 9, (240, 150, 50), (255, 200, 110), (190, 100, 30)), 145, 338),
            (pill(13, 9, (254, 220, 80), (255, 244, 160), (210, 160, 30)), 159, 338),
            (pill(7, 9, (222, 70, 60), (255, 130, 110), (150, 30, 40)), 137, 338),
            (close_button(), 337, 494), (arrow(True), 352, 495), (arrow(False), 365, 495), (coin(), 193, 373),
            (tab_inventory(), 0, 368), (plate(), 16, 368), (tab_coop(), 32, 368), (tab_market(), 80, 368), (tab_shop(), 0, 384),
            (cursor_arrow(), 0, 0), (cursor_hand(), 32, 0)):
        sheet.paste(pic, x, y)
    return sheet


def text_box(size):
    sheet = Image(*size('ui/text_box.png'))
    sheet.paste(field(), 4, 0)
    return sheet


# --- the start screen picture -------------------------------------------------------------------------------------------

BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def dithered(y, x, top, bottom, y0, y1, steps=14):
    """Banded gradient: the colour steps are mixed across the band edge by a 4 x 4 Bayer matrix."""
    t = (y - y0) / max(1, y1 - y0) * steps
    band = int(t)
    frac = t - band
    if BAYER[y % 4][x % 4] / 16 < frac:
        band += 1
    return lerp(top, bottom, min(1.0, band / steps))


def sky(size):
    w, h = size('ui/start_background.png')
    img = Image(w, h)
    horizon = 345
    for y in range(h):
        for x in range(w):
            if y < horizon:
                img.px[y][x] = dithered(y, x, (0, 162, 255), (160, 224, 255), 0, horizon) + (255,)
            else:
                img.px[y][x] = dithered(y, x, (24, 138, 226), (0, 96, 188), horizon, h) + (255,)
    cloud = Canvas(w, h)
    seed = 7
    tones = ((176, 204, 232), (238, 246, 255), (255, 255, 255))
    for i in range(26):
        seed = (seed * 1103515245 + 12345) % 2 ** 31
        cx = seed % (w + 200) - 100
        seed = (seed * 1103515245 + 12345) % 2 ** 31
        cy = 30 + seed % 270
        scale = 0.6 + (cy / 300) * 0.9
        for dx, dy, rx, ry in ((0, 0, 46, 17), (-34, 6, 28, 12), (36, 7, 30, 12), (8, -10, 26, 14), (-12, -6, 22, 12)):
            blob(cloud, cx + dx * scale, cy + dy * scale, rx * scale, ry * scale, tones, texture=False)
    img.paste(cloud, 0, 0)
    for k in range(260):                      # wave glints on the sea, denser and wider toward the viewer
        seed = (seed * 1103515245 + 12345) % 2 ** 31
        x = seed % w
        seed = (seed * 1103515245 + 12345) % 2 ** 31
        y = horizon + 6 + seed % (h - horizon - 8)
        length = 4 + (y - horizon) // 14
        for i in range(length):
            if 0 <= x + i < w:
                img.px[y][x + i] = (150, 214, 255, 255) if i % 3 else (255, 255, 255, 255)
    for x in range(w):                        # a bright line where the sea meets the sky
        img.px[horizon][x] = (200, 236, 255, 255)
    return img


def build(size):
    return {
        'ui/menu.png': menu_tiles(size),
        'ui/cursors.png': cursors(size),
        'ui/text_box.png': text_box(size),
        'ui/start_background.png': sky(size),
    }
