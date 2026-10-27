"""Package 9: the UI sheets (ui/menu.png, ui/cursors.png, ui/text_box.png, ui/start_background.png).

Every window piece is a 9-slice frame with a 1 px dark outline, a two-tone bevel (light on the top and left, dark on the bottom and right),
rounded corners and a textured fill, all in the one palette of palette.py (the tileset's wood, gold and cream, so the UI matches the world).
The "fat" pieces of menu.png and text_box.png (CSS border-image slices, nosum-theme.js) are drawn at art pixels and enlarged 4 x, so one art
pixel is one UI pixel; the "thin" pieces of cursors.png are 1 : 1. The rectangles are the ones nosum-theme.js reads. The start-screen picture is
a sky and a sea drawn from dithered bands and shaded cloud blobs. No CC0 pack has the nine-slice pieces at these sizes, and none matches the palette.
"""
import os

import palette as P
from draw import Canvas
from pngio import Image
from trees import blob
import animals

OUT = P.OUT


# --- frames ------------------------------------------------------------------------------------------------------------------

def put(c, x, y, col):
    """One pixel; a colour with four components carries its own alpha (the glow of the selection frame)."""
    if len(col) == 4:
        c.px[y][x] = col
    else:
        c.dot(x, y, col)


def frame(w, h, rings, radius=2, fill=None):
    """A w x h frame. rings[d] is (light, dark) or one colour for the d-th pixel in from the edge; the light colour is used on the top and left,
    the dark one on the bottom and right (a tie on a diagonal takes the dark one). radius 2 clips the corner pixel, radius 3 also its two
    neighbours and makes the pixel inside the outline too. The middle is `fill`, or transparent."""
    c = Canvas(w, h)
    for y in range(h):
        for x in range(w):
            cx, cy = min(x, w - 1 - x), min(y, h - 1 - y)
            d = min(cx, cy)
            if (cx, cy) == (0, 0) or (radius == 3 and cx < 2 and cy < 2 and (cx, cy) != (1, 1)):
                continue
            if radius == 3 and (cx, cy) == (1, 1):
                d = 0
            if d < len(rings):
                ring = rings[d]
                if isinstance(ring[0], tuple):
                    light = min(x, y) < min(w - 1 - x, h - 1 - y)
                    ring = ring[0] if light else ring[1]
                put(c, x, y, ring)
            elif fill is not None:
                put(c, x, y, fill)
    return c


def fat(c):
    """One art pixel becomes a 4 x 4 block: the 'fat' sheets are drawn at 4 sheet pixels per UI pixel."""
    return c.scale(4)


def panel():
    """The window frame, 15 x 15 art pixels, 4 px edge: outline, lit wood, mid wood, an inner lip; a rivet in each corner. The straight runs stay
    plain, because the middle of each edge stretches to the window's size and any detail in it would become a long dash."""
    c = frame(15, 15, [OUT, (P.WOOD_HI, P.WOOD_MID), (P.WOOD_L, P.WOOD_D), (P.WOOD_DD, P.WOOD_L)], radius=3)
    for x, y in ((2, 2), (12, 2), (2, 12), (12, 12)):
        c.dot(x, y, P.GOLD_HI)
    return c


def plaque():
    """The small frame of the HUD widgets, tooltips and the message line: 12 x 12 art pixels, 3 px edge."""
    return frame(12, 12, [OUT, (P.WOOD_HI, P.WOOD_MID), (P.WOOD_L, P.WOOD_D)])


def card():
    """A recessed card inside a window: a brown outline and a shaded top and left, a lit bottom and right; 2 px edge."""
    return frame(15, 15, [P.OUT_SOFT, (P.PARCH_SH, P.PARCH_HI)])


def slot():
    """An inventory slot: sunk into the page, 2 px edge (the item picture overlaps the inner ring)."""
    return frame(16, 16, [P.OUT_SOFT, (P.PARCH_DK, P.PARCH_HI)])


def select():
    """The frame of the selected slot: a two-step gold glow outside a dark outline round a lit gold line; 5 px edge."""
    glow_far, glow_near = (255, 214, 100, 70), (255, 214, 100, 150)
    c = frame(20, 20, [glow_far, glow_near, OUT, (P.GOLD_L, P.GOLD), (P.GOLD_HI, P.GOLD_L)], radius=3)
    return c


def parchment():
    """The page texture: 16 x 16 art pixels that tile; a few darker flecks, lighter flecks and short fibres, none on a seam."""
    c = Canvas(16, 16)
    for y in range(16):
        for x in range(16):
            n = (x * 73856093 ^ y * 19349663 ^ 0x5bd1) % 97
            col = P.PARCH
            if n < 5:
                col = P.PARCH_LO
            elif n > 93:
                col = P.PARCH_HI
            c.dot(x, y, col)
    for x, y in ((3, 4), (4, 4), (11, 9), (12, 9), (13, 9), (6, 13), (7, 13), (1, 10), (9, 1)):
        c.dot(x, y, P.PARCH_LO)
    for x, y in ((8, 6), (2, 14), (14, 3)):
        c.dot(x, y, P.PARCH_HI)
    return c


def field():
    """The text field: sunk, 47 x 12 art pixels with a 3 px edge; the middle is the light page the ink sits on."""
    return frame(47, 12, [P.OUT_SOFT, (P.PARCH_DK, P.PARCH_HI), (P.PARCH_SH, P.PARCH)], fill=P.PARCH_HI)


def title_strip():
    """The title bar of a window: 8 x 14 thin pixels repeated along the bar, wood lit from the top, a dark foot on the frame's inner lip."""
    rows = [(214, 142, 98), (176, 98, 68), (160, 86, 62), (160, 86, 62), (152, 80, 60), (152, 80, 60), (146, 74, 58), (146, 74, 58), (140, 68, 56),
            (140, 68, 56), (128, 60, 52), (119, 56, 51), (98, 46, 46), OUT]
    c = Canvas(8, 14)
    for y, col in enumerate(rows):
        c.rect(0, y, 8, 1, col)
    for y in range(2, 11):                       # a faint plank joint every 8 px
        c.dot(0, y, tuple(max(0, v - 14) for v in rows[y]))
    return c


def gauge():
    """The frame of a vertical gauge, 10 x 26 thin pixels, 3 px edge: gold end caps on lit and shaded wood, a dark track between."""
    w, h = 10, 26
    c = Canvas(w, h)
    for y in range(h):
        for x in range(w):
            cx, cy = min(x, w - 1 - x), min(y, h - 1 - y)
            if (cx, cy) == (0, 0):
                continue
            if min(cx, cy) == 0:
                col = OUT
            elif cy == 1 or cy == 2:
                col = (P.GOLD_HI, P.GOLD_L)[cy - 1] if y < h // 2 else (P.GOLD, P.GOLD_D)[cy - 1]
            elif cx == 1:
                col = P.WOOD_L if x < w // 2 else P.WOOD_D
            elif cx == 2:
                col = (24, 14, 18) if x < w // 2 else (96, 72, 64)
            else:
                col = (48, 32, 38)
            c.dot(x, y, col)
    return c


# --- buttons and small pieces (thin) -------------------------------------------------------------------------------------------

def button(w, tones):
    """A pill button w x 9, 3 px slice: outline, a lit edge, the face (lighter above, darker below), a shaded foot."""
    hi, f1, base, dark, shade = tones
    h = 9
    c = Canvas(w, h)
    for y in range(h):
        for x in range(w):
            if (x, y) in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
                continue
            if y in (0, h - 1) or x in (0, w - 1):
                col = OUT
            elif y == 1 or x == 1:
                col = hi
            elif y == h - 2 or x == w - 2:
                col = shade
            elif y == h - 3:
                col = dark
            elif y == 2:
                col = f1
            else:
                col = base
            c.dot(x, y, col)
    return c


BUTTON = (P.WOOD_HI, (240, 176, 118), (226, 146, 90), (200, 112, 72), (160, 84, 60))
BUTTON_HOT = (P.GOLD_HI, P.GOLD_L, (254, 200, 80), (232, 160, 44), P.GOLD_D)
BUTTON_DANGER = (P.RED_HI, P.RED_L, (226, 76, 62), (190, 52, 48), P.RED_D)


def close_button():
    """The close button, 10 x 10: a lit square with a red cross, to stand out on the title bar."""
    c = frame(10, 10, [OUT, (P.PARCH_HI, P.PARCH_DK)], fill=P.PARCH)
    for i in range(6):
        for x in (2 + i, 7 - i):
            c.dot(x, 2 + i, P.RED)
            c.dot(x + (1 if x < 5 else -1), 2 + i, P.RED_D if i in (2, 3) else P.RED)
    for x, y in ((2, 2), (7, 2)):
        c.dot(x, y, P.RED_L)
    return c


def arrow(left):
    rows = ['....O.......', '...OHO......', '..OHHGOOOOO.', '.OHHGGGGGGGO', 'OHHGGGGGGGGO', 'OHGGGGGGGGGO', '.OGGGGGGGGGO', '..OGGGOOOOO.', '...OGO......', '....O.......', '............']
    c = Canvas(12, 11)
    c.art([r.ljust(12, '.')[:12] for r in rows], {'O': OUT, 'H': P.GOLD_L, 'G': P.GOLD})
    for y in range(11):                                      # the lower edge of the body in the darker gold
        for x in range(12):
            if c.px[y][x][:3] == P.GOLD and y + 1 < 11 and c.px[y + 1][x][:3] == OUT:
                c.dot(x, y, P.GOLD_D)
    return c if left else c.flip_h()


def coin():
    """The gold coin, 10 x 10: a lit rim and a darker engraved ring round a bright face, outlined."""
    c = Canvas(10, 10)
    for y in range(10):
        for x in range(10):
            dx, dy = x - 4.5, y - 4.5
            r = (dx * dx + dy * dy) ** 0.5
            if r <= 4.6:
                lit = -0.7 * dx - 0.7 * dy
                col = P.GOLD_L if lit > 0.9 else P.GOLD_D if lit < -1.8 else P.GOLD
                if 2.3 <= r <= 3.0:
                    col = P.GOLD_D if lit < 0.3 else P.GOLD
                elif r < 2.3:
                    col = P.GOLD_HI if lit > 0.3 else P.GOLD_L
                c.dot(x, y, col)
    return c.outline()


# --- tab plates and their pictograms ----------------------------------------------------------------------------------------

def plate():
    """The tab plate, 16 x 16: parchment in a bevelled outline, rounded at the top where it stands on the window."""
    return frame(16, 16, [OUT, (P.PARCH_HI, P.PARCH_SH)], fill=P.PARCH)


def tones(base):
    """(base, light, dark) of a pictogram colour."""
    return base, tuple(min(255, int(v + (255 - v) * 0.45)) for v in base), tuple(int(v * 0.62) for v in base)


def picture(rows, main, accent=None):
    """A pictogram from ASCII rows: '#' main colour, 'a' accent, 'o' dark fill; edges that face up are lit, edges that face down shaded; 1 px outline."""
    h, w = len(rows), max(len(r) for r in rows)
    c = Canvas(w, h)
    mb, ml, md = tones(main)
    ab, al, ad = tones(accent) if accent else (None, None, None)

    def filled(x, y):
        return 0 <= x < w and 0 <= y < h and x < len(rows[y]) and rows[y][x] != '.'

    for y in range(h):
        for x in range(len(rows[y])):
            ch = rows[y][x]
            if ch == '.':
                continue
            if ch == 'o':
                c.dot(x, y, (74, 50, 56))
                continue
            base, light, dark = (mb, ml, md) if ch == '#' else (ab, al, ad)
            above = rows[y - 1][x] if y > 0 and x < len(rows[y - 1]) else '.'
            below = rows[y + 1][x] if y + 1 < h and x < len(rows[y + 1]) else '.'
            col = light if above == '.' else dark if below == '.' else base
            c.dot(x, y, col)
    return c.outline()


def gear_rows():
    """An 11 x 11 cog: a disc with a 3 x 3 hole, a 3 px tooth on each side and a 2 x 2 tooth on each diagonal."""
    rows = []
    for y in range(11):
        row = ''
        for x in range(11):
            ax, ay = abs(x - 5), abs(y - 5)
            disc = 3 <= ax * ax + ay * ay <= 14
            side = (ax <= 1 and ay >= 4) or (ay <= 1 and ax >= 4)
            diagonal = ax in (3, 4) and ay in (3, 4)
            row += '#' if disc or side or diagonal else '.'
        rows.append(row)
    return rows


ICONS = {
    'inventory': (['...####...', '..#....#..', '.########.', '##########', '####aa####', '####aa####', '##########', '.########.'], (176, 104, 64), P.GOLD),
    'wardrobe': (['.###..###.', '##########', '##########', '##.####.##', '##.####.##', '...####...', '...####...', '...####...'], (70, 130, 200), None),
    'shop': (['...aa.....', '...aa.....', '..######..', '.########.', '####aa####', '####aa####', '##########', '.########.'], P.GOLD, (150, 84, 40)),
    'trade': (['..#.......', '..##......', '.#########', '..##......', '..#.......', '.......#..', '......##..', '#########.', '......##..', '.......#..'], (78, 156, 92), None),
    'visitors': (['.aa....aa.', '.aa....aa.', '..........', '.####.####', '.####.####', '.####.####', '.####.####'], (96, 120, 176), (240, 190, 150)),
    'market': (['#a#a#a#a#a', '#a#a#a#a#a', '.#a#a#a#a.', '..........', '.oo....oo.', '.oo.##.oo.', '.oo.##.oo.', '.oo....oo.'], (214, 70, 60), (240, 230, 200)),
    'friends': (['.###..###.', '##########', '##########', '##########', '.########.', '..######..', '...####...', '....##....'], (226, 70, 80), None),
    'mail': (['##########', '##a####a##', '###a##a###', '####aa####', '##########', '##########', '##########'], (240, 230, 200), (176, 100, 84)),
    'skills': (['....##....', '....##....', '...####...', '##########', '.########.', '..######..', '..######..', '.###..###.', '.##....##.'], P.GOLD, None),
    'profile': (['...####...', '..######..', '..######..', '...####...', '..........', '.########.', '##########', '##########', '##########'], (150, 110, 170), None),
    'settings': (gear_rows(), (150, 160, 182), None),
    'goals': (['...####...', '##########', '#aaaaaaaa#', '#a#####aa#', '#aaaaaaaa#', '#a####aaa#', '#aaaaaaaa#', '##########'], (176, 104, 64), (240, 230, 200)),
}


def tab(name):
    c = plate()
    rows, main, accent = ICONS[name]
    pic = picture(rows, main, accent)
    c.paste(pic, (16 - pic.width) // 2, (16 - pic.height) // 2)
    return c


def tab_coop():
    c = plate()
    pic = animals.chicken('down', 0)
    c.paste(pic.crop(1, 1, 14, 14), 1, 1)
    return c


# --- cursors ---------------------------------------------------------------------------------------------------------------

def cursor(rows):
    """A mouse cursor from ASCII rows: '#' outline, 'W' light face; face pixels with an outline or a gap to their right take the shade."""
    h, w = len(rows), max(len(r) for r in rows)
    c = Canvas(w, h)
    rows = [r.ljust(w, '.') for r in rows]
    for y in range(h):
        for x in range(w):
            ch = rows[y][x]
            if ch == '#':
                c.dot(x, y, OUT)
            elif ch == 'W':
                right = rows[y][x + 1] if x + 1 < w else '.'
                below = rows[y + 1][x] if y + 1 < h else '.'
                c.dot(x, y, (206, 190, 158) if right != 'W' or below != 'W' else (255, 250, 236))
    return c


def cursor_arrow():
    return cursor(['#.........', '##........', '#W#.......', '#WW#......', '#WWW#.....', '#WWWW#....', '#WWWWW#...', '#WWWWWW#..', '#WWWWWWW#.', '#WWWWW####', '#WW#WW#...', '#W#.#WW#..', '##..#WW#..', '#....#WW#.', '.....###..'])


def cursor_hand():
    return cursor(['...##.....', '..#WW#....', '..#WW#....', '..#WW#.##.', '..#WW##WW#', '###WWWWWW#', '#WWWWWWWW#', '#WWWWWWWW#', '.#WWWWWWW#', '..########'])


# --- the sheets ------------------------------------------------------------------------------------------------------------

def menu_tiles(size):
    sheet = Image(*size('ui/menu.png'))
    for pic, x, y in ((fat(panel()), 0, 256), (fat(card()), 0, 320), (fat(slot()), 128, 128), (fat(plaque()), 64, 256), (fat(parchment()), 128, 256)):
        sheet.paste(pic, x, y)
    return sheet


def cursors(size):
    sheet = Image(*size('ui/cursors.png'))
    pieces = [
        (fat(select()), 448, 128),
        (button(13, BUTTON), 145, 338), (button(13, BUTTON_HOT), 159, 338), (button(7, BUTTON_DANGER), 137, 338),
        (close_button(), 337, 494), (arrow(True), 352, 495), (arrow(False), 365, 495), (coin(), 193, 373),
        (title_strip(), 0, 400), (gauge(), 16, 400),
        (cursor_arrow(), 0, 0), (cursor_hand(), 32, 0),
        (tab('inventory'), 0, 368), (tab('wardrobe'), 16, 368), (tab_coop(), 32, 368), (tab('trade'), 48, 368), (tab('visitors'), 64, 368),
        (tab('market'), 80, 368), (tab('friends'), 96, 368), (tab('mail'), 112, 368), (tab('skills'), 128, 368), (tab('profile'), 144, 368),
        (tab('settings'), 160, 368), (tab('goals'), 176, 368), (tab('shop'), 0, 384),
    ]
    for pic, x, y in pieces:
        sheet.paste(pic, x, y)
    return sheet


def text_box(size):
    sheet = Image(*size('ui/text_box.png'))
    sheet.paste(fat(field()), 4, 0)
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


# --- the style sheet's palette block ------------------------------------------------------------------------------------------

THEME_CSS = 'wasm/Nosum.Wasm.Controls/wwwroot/nosum-theme.css'


def texts(repo):
    """{path under the repo: text} of the text files this package generates: nosum-theme.css with the palette block of palette.py."""
    path = os.path.join(repo, THEME_CSS)
    with open(path, encoding='utf-8', newline='') as f:
        return {THEME_CSS: P.apply_css(f.read())}
