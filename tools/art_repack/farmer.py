"""Package 1: farmer/*.png, the player layers, drawn here (no pack has a layered 16 x 32 base that fits).

The client stacks the layers as wasm/Nosum.Wasm/Game/FarmerSprite.cs does, on one 16 x 32 frame whose bottom edge is the feet:

    body (skin tint) -> pants (pants tint) -> shirt piece (no tint) -> arms and boots (no tint) -> boot tier (no tint) -> hair (hair tint) -> hat (hat tint)

boots.png holds four footwear tiers (leather, traveler, swift, winged) in 96 px bands; each tier covers every default boot pixel of body.png.

Frame positions: rows 0 down, 1 right, 2 up (left is the right row mirrored by the game); walk columns 0 stand, 1 and 2 the two steps
(the game plays 0, 1, 0, 2). Where a layer draws the 1 px walk bob in code (shirt, hair, hat drop one pixel on the step columns) the pictures
of the other layers carry the same bob, so the frames line up in every column. Offsets the code adds are undone here when a layer is
painted: the hair frame is drawn 2 px lower, the shirt piece at (4, 15), the hat 20 x 20 frame 2 px to the left.

Because the code multiplies a tint onto a layer, the tinted layers (body, pants, hair) are drawn light and grey-ish: skin in a light tone, pants
and hair in white-to-grey shades with a grey outline, so the dye gives the colour and the outline becomes a darker tone of it (ADR-040 D1).
Shirts, arms, boots and hats get no tint and carry their own colours. Everything is drawn with the Kenney Tiny Town palette of draw.py and
a 1 px outline, as the animals are.

Cells the game does not draw stay transparent: body columns 0-2 and arms columns 6-8 of rows 0-2, pants style 0, hair styles 0-7 (rows 0-2),
shirts 1, 3, 7, 11 (pieces 0 front, 8 right, 24 back) and hats 3 and 5 (rows down, right, left, up).
"""
import draw
from draw import Canvas
from pngio import Image

OUT = draw.OUT
GOLD = draw.GOLD
GOLD_L = draw.GOLD_L

# Skin as drawn (a skin tint of white leaves it unchanged); the other tones are tints of it.
SKIN = (246, 206, 168)
SKIN_D = (226, 170, 134)
SKIN_X = (200, 124, 106)
SKIN_L = (255, 232, 204)
EYE = (63, 38, 49)
CHEEK = (240, 160, 150)

# Grey-scale shades for the layers the code tints: the dye colour comes out at the base shade, darker at the shade, lightest at the highlight.
G_L = (255, 255, 255)
G_B = (238, 238, 238)
G_D = (190, 190, 190)
G_X = (150, 150, 150)
G_OUT_HAIR = (96, 96, 96)
G_OUT_PANTS = (118, 118, 118)

# Untinted colours.
BOOT = (150, 84, 60)
BOOT_D = (119, 56, 51)
BOOT_L = (189, 108, 74)
HAND = SKIN

FRAME_W, FRAME_H = 16, 32
HAIR_STYLES = 8
SHIRTS = {1: 'red', 2: 'grey', 3: 'grey', 4: 'grey', 7: 'blue', 11: 'green'}  # sheet index -> colour (LookPalette.cs: ShirtRed, PlainShirt/armors, ShirtBlue, ShirtGreen; 0 stays empty)
SHIRT_PIECES = {'down': 0, 'right': 8, 'up': 24}               # y offset of the 8 x 8 piece inside a shirt's 32 px band
HATS = {2: 'crown', 3: 'straw', 4: 'helmet', 5: 'cap'}         # sheet index -> hat (HatStraw, HatCap, plus greyscale crown/helmet slots; 0 stays empty)
HAT_ROWS = ['down', 'right', 'left', 'up']
DIRECTIONS = ['down', 'right', 'up']
WALK_COLUMNS = [0, 1, 2, 3, 4, 5]

HAIR_OFFSET_Y = 2          # FarmerSprite.HairOffsetY
SHIRT_OFFSET = (4, 15)     # FarmerSprite.ShirtOffsetX / ShirtOffsetY
HAT_OFFSET_X = 2           # (HatSize - Width) / 2


def _bob(column):
    return 1 if column in (1, 2, 3) else 0


# ---- legs: one pose table shared by body, pants and boots so the three layers always agree ----------------------------------------------

def _legs(direction, column):
    """[(x0, x1, bottom, shade)] for the legs of one frame: x range inclusive, the lowest boot row, 0 light / 1 dark."""
    if direction == 'right':
        if column == 0:
            return [(6, 9, 31, 0)]
        if column == 1:   # near leg forward, far leg back and lifted 2px
            return [(3, 5, 29, 1), (8, 10, 31, 0)]
        if column == 2:   # far leg forward and lifted 2px, near leg planted back
            return [(4, 6, 31, 0), (8, 10, 29, 1)]
        if column == 3:   # planted wide action lunge stance
            return [(4, 6, 31, 1), (8, 11, 31, 0)]
        if column == 4:   # passing mid-stride A (1px lift, half stride)
            return [(4, 6, 30, 1), (8, 10, 31, 0)]
        return [(4, 6, 31, 0), (8, 10, 30, 1)]   # column 5: passing mid-stride B
    if column == 3:
        return [(4, 6, 31, 0), (9, 11, 31, 0)]
    if column == 1:
        return [(5, 7, 31, 0), (8, 10, 29, 1)]
    if column == 2:
        return [(5, 7, 29, 1), (8, 10, 31, 0)]
    if column == 4:       # passing mid-stride A: left foot planted, right foot lifting 1px
        return [(5, 7, 31, 0), (8, 10, 30, 1)]
    if column == 5:       # passing mid-stride B: left foot lifting 1px, right foot planted
        return [(5, 7, 30, 1), (8, 10, 31, 0)]
    return [(5, 7, 31, 0), (8, 10, 31, 0)]


def _fill_legs(c, direction, column, color, dark, top, shrink=0, lower=2):
    for x0, x1, bottom, shade in _legs(direction, column):
        col = dark if shade else color
        c.rect(x0 + shrink, top, x1 - x0 + 1 - 2 * shrink, bottom - lower - top + 1, col)


# ---- body: head, neck, torso under the shirt, legs under the pants ------------------------------------------------------------------------

HEAD_FILL = {5: (4, 11), 6: (3, 12), 7: (3, 12), 8: (3, 12), 9: (3, 12), 10: (3, 12), 11: (3, 12), 12: (3, 12), 13: (4, 11)}


def body(direction, column):
    c = Canvas(FRAME_W, FRAME_H)
    b = _bob(column)
    for y, (x0, x1) in HEAD_FILL.items():
        c.rect(x0, y + b, x1 - x0 + 1, 1, SKIN)
    # light from the top left: shade the right edge and chin
    c.rect(12, 7 + b, 1, 6, SKIN_D)
    c.rect(4, 6 + b, 5, 1, SKIN_L)                                       # forehead highlight
    c.rect(5, 13 + b, 6, 1, SKIN_D)
    c.rect(7, 14 + b, 2, 3, SKIN_D)                                      # neck
    c.rect(5, 15 + b, 6, 8, SKIN)                                         # torso under the shirt (hidden by it)
    _fill_legs(c, direction, column, SKIN_D, SKIN_D, 22, shrink=1)        # legs under the pants, inside their outline
    if direction == 'down':
        # Expressive 2x2 eyes with brow shade and upper-left catchlight
        c.rect(4, 9 + b, 2, 1, SKIN_X)                                    # eyebrows
        c.rect(10, 9 + b, 2, 1, SKIN_X)
        c.rect(4, 10 + b, 2, 2, EYE)
        c.rect(10, 10 + b, 2, 2, EYE)
        c.dot(4, 10 + b, SKIN_L)                                          # eye shine
        c.dot(10, 10 + b, SKIN_L)
        c.dot(3, 12 + b, CHEEK)
        c.dot(12, 12 + b, CHEEK)
        c.rect(7, 13 + b, 2, 1, SKIN_X)
        c.dot(2, 9 + b, SKIN)                                             # ears
        c.dot(2, 10 + b, SKIN)
        c.dot(13, 9 + b, SKIN_D)
        c.dot(13, 10 + b, SKIN_D)
    elif direction == 'right':
        c.rect(10, 9 + b, 2, 1, SKIN_X)                                   # eyebrow
        c.rect(10, 10 + b, 2, 2, EYE)
        c.dot(10, 10 + b, SKIN_L)                                         # eye shine
        c.rect(13, 11 + b, 1, 2, SKIN)                                    # nose
        c.dot(9, 12 + b, CHEEK)
        c.dot(12, 13 + b, SKIN_X)
        c.rect(5, 9 + b, 2, 2, SKIN_D)                                    # ear
        c.dot(5, 9 + b, SKIN)
    else:
        c.rect(6, 13 + b, 4, 1, SKIN_D)                                   # nape
        c.dot(2, 9 + b, SKIN)
        c.dot(2, 10 + b, SKIN)
        c.dot(13, 9 + b, SKIN_D)
        c.dot(13, 10 + b, SKIN_D)
    return c.outline(OUT)


# ---- pants (grey-scale) -----------------------------------------------------------------------------------------------------------------

def pants(direction, column):
    c = Canvas(FRAME_W, FRAME_H)
    b = _bob(column)
    c.rect(5, 20, 6, 4 + b, G_B)                # hips and waist (the shirt hides the top rows)
    c.rect(4, 23, 8, 2, G_B) if direction != 'right' else c.rect(5, 23, 6, 2, G_B)
    c.rect(4, 24, 8, 1, G_X) if direction != 'right' else c.rect(5, 24, 6, 1, G_X)      # belt
    c.dot(7, 24, G_L)                                                                     # buckle
    c.dot(8, 24, G_L)
    legs = _legs(direction, column)
    for x0, x1, bottom, shade in legs:
        col = G_D if shade else G_B
        c.rect(x0, 25, x1 - x0 + 1, bottom - 2 - 25 + 1, col)
        if direction != 'right':
            c.rect(x0, 25, 1, bottom - 2 - 25 + 1, G_L if not shade else G_B)           # lit left edge
        c.rect(x1, 25, 1, bottom - 2 - 25 + 1, G_D if not shade else G_X)               # shaded right edge
    if direction == 'right' and len(legs) == 1:
        c.rect(6, 25, 1, 4, G_L)
    if direction == 'down':
        c.rect(7, 27, 2, 4, G_X)                                                         # crotch seam
        c.dot(6, 26, G_L)                                                                # pocket rivets
        c.dot(9, 26, G_L)
    return c.outline(G_OUT_PANTS)


# ---- arms and boots (untinted) ----------------------------------------------------------------------------------------------------------

def arms(direction, column):
    c = Canvas(FRAME_W, FRAME_H)
    b = _bob(column)
    if direction == 'right':
        if column == 3:
            # Action swing / cast pose: arm extended forward to hold/swing tool or cast spell
            c.rect(8, 16 + b, 5, 2, SKIN)
            c.rect(8, 17 + b, 5, 1, SKIN_D)
            c.rect(12, 15 + b, 2, 3, HAND)
        else:
            # one visible arm; it swings dynamically opposite the near leg (with intermediate passing frames 4 and 5)
            sx = {0: 7, 1: 4, 2: 9, 4: 5, 5: 8}[column]
            sy = 16 + b
            c.rect(sx, sy, 2, 6, SKIN)
            c.rect(sx + 1, sy, 1, 6, SKIN_D)
            c.rect(sx, sy + 5, 2, 2, HAND)
            c.dot(sx, sy + 5, SKIN_L)
    elif column == 3:
        # Action swing / cast pose for down/up: left arm braced, right arm raised/extended
        c.rect(3, 16 + b, 2, 5, SKIN)
        c.rect(3, 20 + b, 2, 2, HAND)
        if direction == 'down':
            c.rect(11, 14 + b, 2, 6, SKIN)
            c.rect(12, 14 + b, 1, 6, SKIN_D)
            c.rect(11, 19 + b, 3, 2, HAND)
        else:
            c.rect(11, 12 + b, 2, 6, SKIN)
            c.rect(12, 12 + b, 1, 6, SKIN_D)
            c.rect(11, 11 + b, 2, 2, HAND)
    else:
        for side, x in ((-1, 3), (1, 11)):
            dy = 0
            if column == 1:
                dy = 2 if side < 0 else -2
            elif column == 2:
                dy = -2 if side < 0 else 2
            elif column == 4:
                dy = 1 if side < 0 else -1
            elif column == 5:
                dy = -1 if side < 0 else 1
            top = 16 + b + dy
            c.rect(x, top, 2, 6, SKIN)
            c.rect(x + 1 if side < 0 else x, top, 1, 6, SKIN_D)
            c.rect(x, top + 5, 2, 2, HAND)
            c.dot(x, top + 5, SKIN_L)
    boots(c, direction, column)
    return c.outline(OUT)


def _boot_rects(direction, column):
    """[(left, top, right, bottom, shade)] of the default boot on each leg: the pixels every boot tier must cover (boot_layer)."""
    rects = []
    for x0, x1, bottom, shade in _legs(direction, column):
        right = x1 + (1 if direction == 'right' else 0)
        left = x0 - (0 if direction == 'right' else 1)
        rects.append((left, bottom - 3, right, bottom, shade))
    return rects


def boots(c, direction, column):
    """Brown boots with a cuff over the end of the pants, drawn on the arms layer so they ignore the pants tint."""
    two_legs = len(_legs(direction, column)) == 2
    for left, top, right, bottom, shade in _boot_rects(direction, column):
        body_col = BOOT_D if shade else BOOT
        c.rect(left, top, right - left + 1, 4, body_col)
        c.rect(left, top, right - left + 1, 1, BOOT_L if not shade else BOOT)               # cuff
        c.rect(left, bottom, right - left + 1, 1, BOOT_D)                                    # sole
        if direction != 'right' and two_legs and left == 7:
            c.rect(left, top + 1, 1, 3, BOOT_D)                                              # crease between the boots (the second leg starts at x 8)


# ---- boot tiers (boots.png, untinted, drawn over the arms layer) ----------------------------------------------------------------------------

BOOT_TIERS = 4
BOOT_BAND = 96                           # height of one tier band in boots.png: three facing rows of 32 px

# Per tier: near-leg and far-leg body, the cuff row, the sole row, and the tier's own trim colours.
BOOT_PALETTE = {
    1: {'near': BOOT, 'far': BOOT_D, 'cuff_near': BOOT_L, 'cuff_far': BOOT, 'sole': BOOT_D},                      # leather
    2: {'near': (186, 138, 88), 'far': (146, 102, 62), 'cuff_near': (98, 60, 42), 'cuff_far': (98, 60, 42),
        'sole': (84, 50, 38)},                                                                                    # traveler, tan
    3: {'near': (150, 170, 190), 'far': (110, 130, 152), 'cuff_near': (214, 228, 240), 'cuff_far': (176, 196, 214),
        'sole': (60, 74, 96)},                                                                                    # swift, blue-grey
    4: {'near': (90, 64, 50), 'far': (70, 48, 40), 'cuff_near': GOLD, 'cuff_far': GOLD, 'sole': (150, 96, 40)},  # winged, gold trim
}
BOOT_STRAP = (84, 50, 38)
BOOT_BUCKLE = GOLD
BOOT_STREAK = (206, 220, 234)            # swift boots' highlight
BOOT_TAN_LIGHT = (206, 160, 110)
WING = (255, 255, 255)
WING_SHADE = (196, 200, 214)
# (dx, dy) of the wing pixels from the boot's outer edge (dx) and cuff row (dy), for the left side; the right side mirrors dx.
WING_PIXELS = [(-2, -1, WING), (-3, 0, WING), (-2, 0, WING), (-3, 1, WING_SHADE), (-2, 1, WING)]


def _boot_leg(c, tier, rect, wing_side):
    """Draws one tier's boot on one leg. wing_side: 'left', 'right' or None (tier 4 wings only)."""
    left, top, right, bottom, shade = rect
    pal = BOOT_PALETTE[tier]
    near = not shade
    if tier == 2:
        top -= 2                         # traveler boots come up over the calf
    width = right - left + 1
    c.rect(left, top, width, bottom - top + 1, pal['near' if near else 'far'])
    c.rect(left, top, width, 1, pal['cuff_near' if near else 'cuff_far'])
    c.rect(left, bottom, width, 1, pal['sole'])
    if tier == 2:
        c.rect(left, bottom - 2, width, 1, BOOT_STRAP)                                       # strap across the shaft
        if near:
            c.dot(right, bottom - 2, BOOT_BUCKLE)                                            # buckle
            c.rect(left + 1, top + 1, 1, bottom - top - 3, BOOT_TAN_LIGHT)                   # lit tan
    if tier == 3 and near:
        c.rect(left + 1, top + 1, 1, bottom - top - 1, BOOT_STREAK)                          # highlight down the shaft
    if tier == 4 and near:
        c.dot(left + 1, top, GOLD_L)                                                         # gold glint on the cuff
    if tier == 4 and wing_side is not None:
        for dx, dy, col in WING_PIXELS:
            x = left + dx if wing_side == 'left' else right - dx
            c.dot(x, top + dy, col)


def boot_layer(tier, direction, column):
    """One frame of boot tier 1..4: covers every default boot pixel of body.png (_boot_rects) and adds the tier's own parts."""
    c = Canvas(FRAME_W, FRAME_H)
    rects = _boot_rects(direction, column)
    for index, rect in enumerate(rects):
        if direction == 'right':
            side = 'left'                # the heel is at the back, on the left of the right-facing picture
        else:
            side = 'left' if index == 0 else 'right'
        _boot_leg(c, tier, rect, side)
    if direction != 'right' and len(rects) == 2 and rects[1][0] == 7:
        left, top, _, bottom, _ = rects[1]
        if tier == 1:
            c.rect(left, top + 1, 1, 3, BOOT_D)                                              # the default's crease
    for left, top, right, bottom, _ in rects:
        for x in range(left, right + 1):
            for y in range(top, bottom + 1):
                if c.px[y][x][3] == 0:
                    raise ValueError(f'boot tier {tier} misses default boot pixel {(x, y)} ({direction}, column {column})')
    return c.outline(OUT)


BOOT_ICON_ROWS = [
    '................',
    '....CCCC........',
    '....CCCC........',
    '....MMMM........',
    '....MMMM........',
    '....MMMM........',
    '....MMMM........',
    '....MMMMMMMM....',
    '....MMMMMMMMMMM.',
    '....MMMMMMMMMMM.',
    '....DDDDDDDDDDD.',
    '...SSSSSSSSSSSS.',
    '................',
]
BOOT_ICON_PALETTE = {
    1: {'C': BOOT_L, 'M': BOOT, 'D': BOOT_D, 'S': (84, 44, 40)},
    2: {'C': (98, 60, 42), 'M': (186, 138, 88), 'D': (146, 102, 62), 'S': (84, 50, 38)},
    3: {'C': (214, 228, 240), 'M': (150, 170, 190), 'D': (110, 130, 152), 'S': (60, 74, 96)},
    4: {'C': GOLD, 'M': (90, 64, 50), 'D': (70, 48, 40), 'S': (150, 96, 40)},
}


def boot_icon(tier):
    """The 16 x 16 inventory icon of boot tier 1..4: a side-on boot, with the tier's trim drawn over the shared silhouette."""
    c = Canvas(16, 16)
    c.art(BOOT_ICON_ROWS, BOOT_ICON_PALETTE[tier])
    if tier == 2:
        c.rect(4, 6, 4, 1, BOOT_STRAP)                                                       # strap
        c.dot(7, 6, BOOT_BUCKLE)
        c.dot(5, 3, BOOT_TAN_LIGHT)
        c.dot(5, 4, BOOT_TAN_LIGHT)
    if tier == 3:
        for y in (2, 3, 4, 5):
            c.dot(5, y, BOOT_STREAK)                                                         # highlight
    if tier == 4:
        c.dot(5, 2, GOLD_L)
        for x, y, col in ((3, 3, WING), (2, 4, WING), (3, 4, WING), (1, 5, WING), (2, 5, WING), (3, 5, WING_SHADE), (2, 6, WING_SHADE)):
            c.dot(x, y, col)
    return c.outline(OUT)


# ---- shirt pieces (8 x 8, untinted) ------------------------------------------------------------------------------------------------------

SHIRT_COLORS = {
    'grey': (G_X, G_B, G_L),
    'red': ((150, 40, 40), (214, 72, 60), (240, 120, 96)),
    'straw': ((190, 146, 70), (236, 202, 112), (252, 232, 160)),
    'blue': ((30, 90, 150), (60, 130, 205), (110, 180, 240)),
    'green': ((38, 92, 66), (62, 137, 72), (120, 200, 100)),
}

FRONT = ['OOO..OOO',
         'OLBBBBBO',
         'OLBSBBBO',
         'OBBSBBSO',
         'OBBSBBSO',
         'OBBBBBSO',
         'OBBBBSSO',
         'OOOOOOOO']
SIDE = ['.OOO.OO.',
        '.OLBBBO.',
        '.OLBBSO.',
        '.OBBBSO.',
        '.OBBBSO.',
        '.OBBBSO.',
        '.OBBSSO.',
        '.OOOOOO.']
BACK = ['OOOOOOOO',
        'OLBBBBBO',
        'OLBSSBBO',
        'OBBBBBSO',
        'OBBBBBSO',
        'OBBBBSSO',
        'OBBBBSSO',
        'OOOOOOOO']


def shirt(color, direction):
    dark, base, light = SHIRT_COLORS[color]
    rows = {'down': FRONT, 'right': SIDE, 'up': BACK}[direction]
    pal = {'O': dark, 'B': base, 'L': light, 'S': dark}
    pal['O'] = tuple(max(0, v - 38) for v in dark)
    c = Canvas(8, 8)
    c.art(rows, pal)
    return c


# ---- hair (grey-scale, one frame per direction) -----------------------------------------------------------------------------------------

def _profile(top, bottom, x0):
    """{x: (top, bottom)} from two lists indexed from column x0."""
    return {x0 + i: (t, b) for i, (t, b) in enumerate(zip(top, bottom))}


def _union(*masks):
    out = set()
    for m in masks:
        out |= m
    return out


def _from_profile(prof):
    return {(x, y) for x, (t, b) in prof.items() for y in range(t, b + 1)}


def _ellipse(cx, cy, rx, ry):
    return {(x, y) for y in range(FRAME_H) for x in range(FRAME_W) if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1}


def _rect(x0, y0, x1, y1):
    return {(x, y) for y in range(y0, y1 + 1) for x in range(x0, x1 + 1)}


# Top rows shared by the short styles: a dome one pixel bigger than the skull.
DOME_TOP = [6, 5, 4, 4, 4, 4, 4, 4, 4, 4, 5, 6]          # down and up, x = 2..13
DOME_RIGHT = [6, 5, 4, 4, 4, 4, 4, 4, 4, 4, 5, 6]


def _hair_mask(style, direction):
    d, r, u = direction == 'down', direction == 'right', direction == 'up'
    dome = DOME_TOP
    if style == 0:        # short, tousled, side part
        if d:
            return _from_profile(_profile(dome, [9, 9, 8, 7, 7, 6, 6, 7, 7, 8, 9, 9], 2))
        if r:
            return _from_profile(_profile(dome, [12, 12, 12, 11, 8, 8, 8, 7, 7, 7, 7, 6], 2))
        return _from_profile(_profile(dome, [13, 13, 14, 14, 14, 14, 14, 14, 14, 14, 13, 13], 2))
    if style == 1:        # buzz cut
        if d:
            return _from_profile(_profile([6, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6], [8, 8, 6, 6, 6, 6, 6, 6, 6, 6, 8, 8], 2))
        if r:
            return _from_profile(_profile([6, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6], [11, 11, 11, 10, 8, 7, 6, 6, 6, 6, 6, 6], 2))
        return _from_profile(_profile([6, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6], [12, 12, 13, 13, 13, 13, 13, 13, 13, 13, 12, 12], 2))
    if style == 2:        # bowl cut
        if d:
            return _from_profile(_profile(dome, [12, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 12], 2))
        if r:
            return _from_profile(_profile(dome, [13, 13, 13, 13, 13, 8, 8, 8, 8, 8, 8, 7], 2))
        return _from_profile(_profile(dome, [14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14], 2))
    if style == 3:        # long, parted in the middle
        if d:
            return _from_profile(_profile(dome, [19, 9, 8, 8, 7, 6, 6, 7, 8, 8, 9, 19], 2)) | _rect(1, 8, 2, 19) | _rect(13, 8, 14, 19)
        if r:
            return _from_profile(_profile(dome, [19, 20, 20, 19, 17, 8, 8, 7, 7, 7, 7, 6], 2))
        return _from_profile(_profile(dome, [20, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 20], 2))
    if style == 4:        # ponytail
        base = _from_profile(_profile(dome, [9, 9, 8, 7, 7, 6, 6, 7, 7, 8, 9, 9], 2))
        if d:
            return base | {(7, 4), (8, 4)}
        if r:
            side = _from_profile(_profile(dome, [12, 12, 12, 11, 8, 8, 8, 7, 7, 7, 7, 6], 2))
            tail = _rect(1, 9, 2, 15) | _rect(0, 12, 1, 17) | {(3, 8), (3, 9), (3, 10)}
            return side | tail
        up = _from_profile(_profile(dome, [13, 13, 14, 14, 14, 14, 14, 14, 14, 14, 13, 13], 2))
        return up | _rect(7, 13, 8, 20) | _rect(6, 17, 9, 19)
    if style == 5:        # spiky
        tops = [5, 4, 4, 3, 4, 4, 4, 4, 3, 4, 4, 5]
        if d:
            return _from_profile(_profile(tops, [8, 7, 7, 6, 7, 6, 6, 7, 6, 7, 7, 8], 2)) | _rect(2, 7, 3, 9) | _rect(12, 7, 13, 9)
        if r:
            return _from_profile(_profile(tops, [12, 12, 11, 11, 8, 8, 8, 7, 7, 6, 7, 6], 2))
        return _from_profile(_profile(tops, [13, 13, 14, 14, 14, 14, 14, 14, 14, 14, 13, 13], 2))
    if style == 6:        # top knot
        bun = _ellipse(8, 5, 3, 2.3) | _rect(7, 6, 8, 6)
        tight = [7, 6, 5, 5, 5, 5, 5, 5, 5, 5, 6, 7]
        if d:
            return _from_profile(_profile(tight, [9, 9, 8, 7, 7, 6, 6, 7, 7, 8, 9, 9], 2)) | bun
        if r:
            return _from_profile(_profile(tight, [12, 12, 12, 11, 8, 8, 8, 7, 7, 7, 7, 7], 2)) | _ellipse(6.5, 5, 3, 2.3)
        return _from_profile(_profile(tight, [13, 13, 14, 14, 14, 14, 14, 14, 14, 14, 13, 13], 2)) | bun
    # style 7: curly puff
    if d:
        return _ellipse(8, 8, 7.2, 5.2) - _rect(3, 8, 12, 14) | _rect(1, 8, 2, 13) | _rect(13, 8, 14, 13)
    if r:
        return _ellipse(6.6, 8, 6.6, 5.2) - _rect(9, 9, 14, 14) | _rect(1, 8, 3, 14)
    return _ellipse(8, 8, 7.2, 5.6) | _rect(2, 10, 13, 14)


def hair(style, direction):
    mask = _hair_mask(style, direction)
    c = Canvas(FRAME_W, FRAME_H)
    for x, y in sorted(mask):
        col = G_B
        above, below, right_ = (x, y - 1) in mask, (x, y + 1) in mask, (x + 1, y) in mask
        if not above:
            col = G_L
        elif not below or not right_:
            col = G_D
        elif (x * 3 + y * 5) % 7 == 0:
            col = G_L
        elif (x * 5 + y * 3) % 11 == 0:
            col = G_D
        c.dot(x, y, col)
    c.outline(G_OUT_HAIR)
    # the game draws the hair frame HairOffsetY lower: move the picture up so it lands where it was drawn
    out = Canvas(FRAME_W, FRAME_H)
    out.paste(c, 0, -HAIR_OFFSET_Y)
    return out


# ---- hats (20 x 20, untinted) -----------------------------------------------------------------------------------------------------------

STRAW = ((196, 140, 60), (232, 190, 96), (255, 226, 130))
BAND = ((119, 56, 51), (190, 70, 60))
CAP = ((140, 30, 40), (214, 62, 56), (244, 118, 96))


def _hat_canvas():
    return Canvas(20, 20)


def _straw_hat(direction):
    c = _hat_canvas()
    d, m, l = STRAW
    dx = 0 if direction in ('down', 'up') else 1
    c.ellipse(9.5 + dx, 5.6, 8, 2.1, m)                    # brim
    c.rect(3 + dx, 6, 14, 1, d)
    c.rect(5, 1, 10, 4, m)                                  # crown
    c.rect(6, 1, 8, 1, l)
    c.rect(5, 2, 10, 1, l)
    c.rect(5, 3, 10, 1, BAND[1])                            # band
    c.rect(5, 4, 10, 1, BAND[0])
    c.rect(14, 1, 1, 3, d)
    c.rect(2 + dx, 5, 3, 1, l)
    return c.outline(OUT)


def _cap(direction):
    c = _hat_canvas()
    d, m, l = CAP
    if direction in ('down', 'up'):
        c.ellipse(9.5, 3.6, 6.5, 3.2, m)
        c.rect(4, 4, 12, 2, m)
        c.rect(6, 1, 6, 1, l)
        c.rect(4, 2, 3, 1, l)
        c.rect(4, 5, 12, 1, d)
        if direction == 'down':
            c.rect(5, 6, 10, 2, d)                          # bill seen from the front
            c.rect(6, 6, 8, 1, m)
        else:
            c.rect(8, 5, 4, 1, (60, 40, 40))               # adjustment strap
    else:
        c.ellipse(9, 3.6, 6.5, 3.2, m)
        c.rect(3, 4, 12, 2, m)
        c.rect(5, 1, 6, 1, l)
        c.rect(3, 5, 12, 1, d)
        c.rect(12, 6, 7, 2, d)                              # bill
        c.rect(12, 6, 6, 1, m)
    return c.outline(OUT)


def _helmet(direction):
    c = _hat_canvas()
    d, m, l = G_D, G_B, G_L
    if direction in ('down', 'up'):
        c.ellipse(9.5, 4.2, 6.5, 3.6, m)
        c.rect(4, 3, 12, 4, m)
        c.rect(6, 1, 8, 2, l)
        c.rect(4, 6, 12, 1, d)
        if direction == 'down':
            c.rect(9, 4, 2, 4, d)                               # nasal guard
        else:
            c.rect(5, 7, 10, 1, d)                              # neck guard
    else:
        c.ellipse(9.5, 4.2, 6.5, 3.6, m)
        c.rect(4, 3, 12, 4, m)
        c.rect(6, 1, 7, 2, l)
        c.rect(4, 6, 12, 1, d)
        c.rect(14, 5, 2, 3, d)                                  # visor / nasal edge
    return c.outline(G_OUT_HAIR)


def _crown(direction):
    c = _hat_canvas()
    d, m, l = G_D, G_B, G_L
    c.rect(4, 4, 12, 3, m)
    c.rect(4, 6, 12, 1, d)
    for px in (4, 9, 14):
        c.rect(px, 2, 2, 2, l)
        c.dot(px, 1, l)
    if direction == 'down':
        c.dot(9, 5, l)
        c.dot(10, 5, l)
    return c.outline(G_OUT_HAIR)


def hat(kind, row):
    """kind 'straw', 'cap', 'helmet' or 'crown'; row in HAT_ROWS. Left is the right picture mirrored here because the game does not mirror hats."""
    direction = 'right' if row == 'left' else row
    if kind == 'straw':
        img = _straw_hat(direction)
    elif kind == 'cap':
        img = _cap(direction)
    elif kind == 'helmet':
        img = _helmet(direction)
    else:
        img = _crown(direction)
    return img.flip_h() if row == 'left' else img


# ---- the five files ---------------------------------------------------------------------------------------------------------------------

def build(size):
    files = {
        'farmer/body.png': Image(*size('farmer/body.png')),
        'farmer/pants.png': Image(*size('farmer/pants.png')),
        'farmer/hair.png': Image(*size('farmer/hair.png')),
        'farmer/shirts.png': Image(*size('farmer/shirts.png')),
        'farmer/hats.png': Image(*size('farmer/hats.png')),
        'farmer/boots.png': Image(FRAME_W * 6, BOOT_BAND * BOOT_TIERS),
    }
    base, pant, hairs, shirts, hats, boot_tiers = (files['farmer/' + n] for n in ('body.png', 'pants.png', 'hair.png', 'shirts.png', 'hats.png', 'boots.png'))
    for row, direction in enumerate(DIRECTIONS):
        for column in WALK_COLUMNS:
            base.paste(body(direction, column), column * FRAME_W, row * FRAME_H)
            base.paste(arms(direction, column), (column + 6) * FRAME_W, row * FRAME_H)
            pant.paste(pants(direction, column), column * FRAME_W, row * FRAME_H)
        for style in range(HAIR_STYLES):
            hairs.paste(hair(style, direction), style % 8 * FRAME_W, style // 8 * 96 + row * FRAME_H)
    for tier in range(1, BOOT_TIERS + 1):
        for row, direction in enumerate(DIRECTIONS):
            for column in WALK_COLUMNS:
                boot_tiers.paste(boot_layer(tier, direction, column), column * FRAME_W, (tier - 1) * BOOT_BAND + row * FRAME_H)
    for index, color in SHIRTS.items():
        for direction, piece in SHIRT_PIECES.items():
            shirts.paste(shirt(color, direction), index % 16 * 8, index // 16 * 32 + piece)
    for index, kind in HATS.items():
        for row, name in enumerate(HAT_ROWS):
            hats.paste(hat(kind, name), index % 12 * 20, index // 12 * 80 + row * 20)
    return files


# ---- preview helpers (not used by the build) --------------------------------------------------------------------------------------------

def compose(files, look, direction, column):
    """The 16 x 32 (plus hat) picture the client draws: returns an Image 20 x 32 with the frame at x 2. look: dict of indices and tints."""
    def tint(img, t):
        return img.map_pixels(lambda p: (p[0] * t[0] // 255, p[1] * t[1] // 255, p[2] * t[2] // 255, p[3]))

    row = DIRECTIONS.index('right' if direction == 'left' else direction)
    out = Image(24, 40)
    ox, oy = 4, 6

    def put(img, x, y, flip=False):
        out.paste(img.flip_h() if flip else img, ox + x, oy + y)

    flip = direction == 'left'
    b = _bob(column)
    base = files['farmer/body.png']
    put(tint(base.crop(column * 16, row * 32, 16, 32), look['skin']), 0, 0, flip)
    pant = files['farmer/pants.png'].crop(column * 16, row * 32, 16, 32)
    put(tint(pant, look['pants']), 0, 0, flip)
    shirt_img = files['farmer/shirts.png'].crop(look['shirt'] % 16 * 8, look['shirt'] // 16 * 32 + {0: 0, 1: 8, 2: 24}[row], 8, 8)
    put(shirt_img, SHIRT_OFFSET[0], SHIRT_OFFSET[1] + b, flip)
    put(base.crop((column + 6) * 16, row * 32, 16, 32), 0, 0, flip)
    hair_img = files['farmer/hair.png'].crop(look['hair'] % 8 * 16, row * 32, 16, 32)
    put(tint(hair_img, look['dye']), 0, HAIR_OFFSET_Y + b, flip)
    if look.get('hat') is not None:
        hat_row = {'down': 0, 'right': 1, 'left': 2, 'up': 3}[direction]
        hat_img = files['farmer/hats.png'].crop(look['hat'] % 12 * 20, look['hat'] // 12 * 80 + hat_row * 20, 20, 20)
        put(hat_img, -HAT_OFFSET_X, b)
    return out
