"""Package 9: the UI sheets (ui/menu.png, ui/cursors.png, ui/text_box.png, ui/start_background.png).

The window frames, the plaque, the block buttons (normal and pressed), the tab plates, the close button and the two mouse cursors are Kenney's CC0
UI packs (art/source/ui: UI Pack - Pixel Adventure and Pixel UI pack, pinned by SHA-256 in sources.py), cut, repaired where a tile has a stray
transparent pixel, darkened where the text on them needs the contrast, and assembled into 9-slice sprites at the rectangles nosum-theme.js reads
(CSS border-image slices them). What the packs have no piece for is drawn here in the pack's flat, outlined style from palette.py: the slot
bevel, the selection frame with its glow, the title strip, the energy gauge, the text field, the tab pictograms, the pager arrows and the coin.
The start-screen picture is a sky and a sea drawn from dithered bands and shaded cloud blobs.
"""
import os

import palette as P
import sources
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


# --- the pack pieces ---------------------------------------------------------------------------------------------------------

def darker(img, f):
    """The picture with every colour multiplied by f (alpha kept)."""
    return img.map_pixels(lambda p: (round(p[0] * f), round(p[1] * f), round(p[2] * f), p[3]))


def mode(values):
    return max(set(values), key=values.count)


def nine(src, w, h, t, r, b, l):
    """A w x h 9-slice sprite assembled from a larger tile: the corners are copied from the tile's corners (t, r, b, l pixels deep), each edge is the
    commonest colour along the tile's edge (so the rivets and speckles of the long runs drop out), the middle is the commonest colour of the face."""
    out = Image(w, h)
    sw, sh = src.width, src.height
    lo_x, hi_x, lo_y, hi_y = l + 2, sw - r - 2, t + 2, sh - b - 2
    for y in range(h):
        sy = y if y < t else sy_bottom(sh, h, y) if y >= h - b else None
        for x in range(w):
            sx = x if x < l else sw - (w - x) if x >= w - r else None
            if sx is not None and sy is not None:
                p = src.px[sy][sx]
            elif sx is not None:
                p = mode([src.px[j][sx] for j in range(lo_y, hi_y)])
            elif sy is not None:
                p = mode([src.px[sy][i] for i in range(lo_x, hi_x)])
            else:
                p = mode([src.px[j][i] for j in range(lo_y, hi_y) for i in range(lo_x, hi_x)])
            out.px[y][x] = p
    return out


def sy_bottom(sh, h, y):
    return sh - (h - y)


# The block buttons are the Pixel UI pack's 9-slice buttons: Ancient brown (primary, wood), Ancient grey (neutral, stone), Colored yellow (the active
# tab) and Colored red (destructive); the face of the wood, stone and red ones is darkened so the cream text on it reaches 4.5 : 1.
BUTTONS = {'button': ('brown', 0.82), 'button-plain': ('grey', 0.78), 'button-hot': ('yellow', 1.0), 'button-danger': ('red', 0.8)}
BUTTON_SLICE = (3, 3, 5, 3)             # top, right, bottom, left of every button sprite (the bottom holds the shadow)


def button_sprite(repo, name, pressed):
    key, factor = BUTTONS[name]
    src = darker(sources.ui_image(repo, key + ('_pressed' if pressed else '')), factor)
    t, r, b, l = BUTTON_SLICE
    return nine(src, l + r + 3, t + b + 3, t, r, b, l)


def pui_cell(repo, column, row):
    """One 16 x 16 cell of the Pixel UI pack's sprite sheet (a 2 px margin between cells)."""
    return sources.ui_image(repo, 'pui-sheet').crop(column * 18, row * 18, 16, 16)


def adventure_cell(repo, column, row):
    return sources.ui_image(repo, 'adventure-small').crop(column * 16, row * 16, 16, 16)


def panel(repo):
    """The window frame: the Pixel Adventure cream panel with a wood frame and steel corner brackets (16 x 16, 5 px slice). Two pixels of the tile
    are transparent where the frame's top edge and left edge should be solid; they are filled from their neighbours."""
    c = adventure_cell(repo, 15, 1)
    c.px[1][6] = c.px[1][7]
    c.px[10][1] = c.px[9][1]
    c.px[10][2] = c.px[9][2]
    return c


def plaque(repo):
    """The small frame of the HUD widgets, tooltips and the message line: the Pixel Adventure cream panel with a plain wood frame (16 x 16, 4 px slice)."""
    return adventure_cell(repo, 10, 0)


def slot():
    """An inventory slot: a 1 px inset bevel, dark on the top and left and light on the bottom and right (3 x 3, 1 px slice; the middle is clear)."""
    c = Canvas(3, 3)
    for y in range(3):
        for x in range(3):
            if (x, y) == (1, 1):
                continue
            c.dot(x, y, P.PARCH_DK if x < 1 or y < 1 else P.PARCH_HI if x > 1 or y > 1 else P.PARCH_SH)
    c.dot(2, 0, P.PARCH_SH)
    c.dot(0, 2, P.PARCH_SH)
    return c


def close_button(repo):
    """The close button: the Pixel UI pack's white square with an orange cross (14 x 14)."""
    return pui_cell(repo, 21, 6).crop(1, 1, 14, 14)


def plate(repo):
    """The tab plate, 16 x 16: the Ancient tan button, a bright edge on the top and left, a dark one on the bottom and right and a short shadow."""
    return nine(sources.ui_image(repo, 'tan'), 16, 16, 2, 2, 3, 2)


def cursor_arrow(repo):
    return pui_cell(repo, 3, 32).crop(0, 0, 12, 16)


def cursor_hand(repo):
    return pui_cell(repo, 0, 25).crop(2, 1, 12, 14)


def select():
    """The frame of the selected slot: a two-step gold glow outside a dark outline round a lit gold line; 5 px edge."""
    glow_far, glow_near = (255, 214, 100, 70), (255, 214, 100, 150)
    c = frame(20, 20, [glow_far, glow_near, OUT, (P.GOLD_L, P.GOLD), (P.GOLD_HI, P.GOLD_L)], radius=3)
    return c


def field():
    """The text field: sunk, 47 x 12 art pixels with a 3 px edge; the middle is the light page the ink sits on."""
    return frame(47, 12, [P.OUT_SOFT, (P.PARCH_DK, P.PARCH_HI), (P.PARCH_SH, P.PARCH)], fill=P.PARCH_HI)


def title_strip():
    """The title bar of a window: 8 x 18 thin pixels repeated along the bar, wood lit from the top and darkening to a dark foot (cream text on it
    reaches 4.5 : 1 over the middle rows)."""
    rows = [P.WOOD_L, P.WOOD_MID, P.WOOD, (150, 102, 52), (142, 96, 50), (136, 92, 48), (130, 88, 46), (124, 84, 44), (118, 80, 42), (112, 77, 40),
            (108, 74, 38), (102, 70, 36), (96, 66, 34), (90, 62, 32), P.WOOD_DD, P.WOOD_DD, (66, 44, 26), OUT]
    c = Canvas(8, len(rows))
    for y, col in enumerate(rows):
        c.rect(0, y, 8, 1, col)
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


ICONS = {
    'inventory': (['...####...', '..#....#..', '.########.', '##########', '####aa####', '####aa####', '##########', '.########.'], (176, 104, 64), P.GOLD),
    'wardrobe': (['.###..###.', '##########', '##########', '##.####.##', '##.####.##', '...####...', '...####...', '...####...'], (70, 130, 200), None),
    'shop': (['...aa.....', '...aa.....', '..######..', '.########.', '####aa####', '####aa####', '##########', '.########.'], P.GOLD, (150, 84, 40)),
    'trade': (['..#.......', '.########.', '..#.......', '..........', '.......#..', '.########.', '.......#..'], (78, 156, 92), None),
    'visitors': (['.aa....aa.', '.aa....aa.', '..........', '.####.####', '.####.####', '.####.####', '.####.####'], (96, 120, 176), (240, 190, 150)),
    'market': (['#a#a#a#a#a', '#a#a#a#a#a', '.#a#a#a#a.', '..........', '.oo....oo.', '.oo.##.oo.', '.oo.##.oo.', '.oo....oo.'], (214, 70, 60), (240, 230, 200)),
    'friends': (['.###..###.', '##########', '##########', '##########', '.########.', '..######..', '...####...', '....##....'], (226, 70, 80), None),
    'mail': (['##########', '##a####a##', '###a##a###', '####aa####', '##########', '##########', '##########'], (240, 230, 200), (176, 100, 84)),
    'skills': (['....##....', '...####...', '##########', '.########.', '..######..', '..######..', '.###..###.', '.##....##.'], P.GOLD, None),
    'profile': (['...####...', '..######..', '..######..', '...####...', '.########.', '##########', '##########', '##########'], (150, 110, 170), None),
    'settings': (['...###...', '.#.###.#.', '.#######.', '###...###', '###...###', '###...###', '.#######.', '.#.###.#.', '...###...'], (150, 160, 182), None),
    'goals': (['...####...', '##########', '#aaaaaaaa#', '#a#####aa#', '#aaaaaaaa#', '#a####aaa#', '#aaaaaaaa#', '##########'], (176, 104, 64), (240, 230, 200)),
}


def tab(repo, name):
    c = plate(repo)
    rows, main, accent = ICONS[name]
    pic = picture(rows, main, accent)
    c.paste(pic, (16 - pic.width) // 2, (16 - pic.height) // 2)
    return c


def tab_coop(repo):
    c = plate(repo)
    pic = animals.chicken('down', 0)
    c.paste(pic.crop(1, 1, 14, 14), 1, 1)
    return c


# --- the sheets ------------------------------------------------------------------------------------------------------------

def menu_tiles(repo, size):
    sheet = Image(*size('ui/menu.png'))
    for pic, x, y in ((panel(repo), 0, 256), (plaque(repo), 32, 256), (slot(), 128, 128)):
        sheet.paste(pic, x, y)
    return sheet


def cursors(repo, size):
    sheet = Image(*size('ui/cursors.png'))
    pieces = [
        (fat(select()), 448, 128),
        *[(button_sprite(repo, name, False), 10 * i, 432) for i, name in enumerate(BUTTONS)],
        *[(button_sprite(repo, name, True), 10 * i, 444) for i, name in enumerate(BUTTONS)],
        (title_strip(), 0, 400), (gauge(), 16, 400),
        (close_button(repo), 337, 494), (arrow(True), 352, 495), (arrow(False), 365, 495), (coin(), 193, 373),
        (cursor_arrow(repo), 0, 0), (cursor_hand(repo), 32, 0),
        (tab(repo, 'inventory'), 0, 368), (tab(repo, 'wardrobe'), 16, 368), (tab_coop(repo), 32, 368), (tab(repo, 'trade'), 48, 368),
        (tab(repo, 'visitors'), 64, 368), (tab(repo, 'market'), 80, 368), (tab(repo, 'friends'), 96, 368), (tab(repo, 'mail'), 112, 368),
        (tab(repo, 'skills'), 128, 368), (tab(repo, 'profile'), 144, 368), (tab(repo, 'settings'), 160, 368), (tab(repo, 'goals'), 176, 368),
        (tab(repo, 'shop'), 0, 384),
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


def build(repo, size):
    return {
        'ui/menu.png': menu_tiles(repo, size),
        'ui/cursors.png': cursors(repo, size),
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

