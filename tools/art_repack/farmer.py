"""Package 1: farmer/*.png, the player layers, drawn here (no pack has a layered 16 x 32 base that fits).

The client stacks the layers as wasm/Nosum.Wasm/Game/FarmerSprite.cs does, on one 16 x 32 frame whose bottom edge is the feet:

    body (skin tint) -> pants (pants tint) -> shirt piece (no tint) -> arms and boots (no tint) -> hair (hair tint) -> hat (hat tint)

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
SHIRTS = {1: 'red', 3: 'straw', 7: 'blue', 11: 'green'}      # sheet index -> colour (LookPalette.cs: ShirtRed, PlainShirt, ShirtBlue, ShirtGreen)
SHIRT_PIECES = {'down': 0, 'right': 8, 'up': 24}               # y offset of the 8 x 8 piece inside a shirt's 32 px band
HATS = {3: 'straw', 5: 'cap'}                                  # sheet index -> hat (HatStraw, HatCap)
HAT_ROWS = ['down', 'right', 'left', 'up']
DIRECTIONS = ['down', 'right', 'up']
WALK_COLUMNS = [0, 1, 2, 3]

HAIR_OFFSET_Y = 2          # FarmerSprite.HairOffsetY
SHIRT_OFFSET = (4, 15)     # FarmerSprite.ShirtOffsetX / ShirtOffsetY
HAT_OFFSET_X = 2           # (HatSize - Width) / 2


def _bob(column):
    return 0 if column == 0 else 1


# ---- legs: one pose table shared by body, pants and boots so the three layers always agree ----------------------------------------------

def _legs(direction, column):
    """[(x0, x1, bottom, shade)] for the legs of one frame: x range inclusive, the lowest boot row, 0 light / 1 dark."""
    if direction == 'right':
        if column == 0:
            return [(6, 9, 31, 0)]
        if column == 1:   # near leg forward, far leg back and lifted
            return [(4, 6, 30, 1), (8, 10, 31, 0)]
        if column == 3:   # planted wide action lunge stance
            return [(4, 6, 31, 1), (8, 11, 31, 0)]
        return [(4, 6, 30, 0), (8, 10, 31, 1)]
    if column == 3:
        return [(4, 6, 31, 0), (9, 11, 31, 0)]
    left_up = column == 2
    right_up = column == 1
    return [(5, 7, 30 if left_up else 31, 0), (8, 10, 30 if right_up else 31, 0)]


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
    c.rect(5, 20, 6, 4, G_B)                    # hips and waist (the shirt hides the top rows)
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
            # one visible arm; it swings opposite the near leg
            sx = {0: 7, 1: 5, 2: 9}[column]
            c.rect(sx, 16 + b, 2, 6, SKIN)
            c.rect(sx + 1, 16 + b, 1, 6, SKIN_D)
            c.rect(sx, 21 + b, 2, 2, HAND)
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
                dy = 1 if side < 0 else -1
            elif column == 2:
                dy = -1 if side < 0 else 1
            top = 16 + b + dy
            c.rect(x, top, 2, 6, SKIN)
            c.rect(x + 1 if side < 0 else x, top, 1, 6, SKIN_D)
            c.rect(x, top + 5, 2, 2, HAND)
    boots(c, direction, column)
    return c.outline(OUT)


def boots(c, direction, column):
    """Brown boots with a cuff over the end of the pants, drawn on the arms layer so they ignore the pants tint."""
    for x0, x1, bottom, shade in _legs(direction, column):
        top = bottom - 3
        right = x1 + (1 if direction == 'right' else 0)
        left = x0 - (0 if direction == 'right' else 1)
        body_col = BOOT_D if shade else BOOT
        c.rect(left, top, right - left + 1, 4, body_col)
        c.rect(left, top, right - left + 1, 1, BOOT_L if not shade else BOOT)               # cuff
        c.rect(left, bottom, right - left + 1, 1, BOOT_D)                                    # sole
        if direction != 'right' and len(_legs(direction, column)) == 2 and x0 == 8:
            c.rect(left, top + 1, 1, 3, BOOT_D)                                              # crease between the boots


# ---- shirt pieces (8 x 8, untinted) ------------------------------------------------------------------------------------------------------

SHIRT_COLORS = {
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


def hat(kind, row):
    """kind 'straw' or 'cap'; row in HAT_ROWS. Left is the right picture mirrored here because the game does not mirror hats."""
    direction = 'right' if row == 'left' else row
    img = _straw_hat(direction) if kind == 'straw' else _cap(direction)
    return img.flip_h() if row == 'left' else img


# ---- the five files ---------------------------------------------------------------------------------------------------------------------

def build(size):
    files = {
        'farmer/body.png': Image(*size('farmer/body.png')),
        'farmer/pants.png': Image(*size('farmer/pants.png')),
        'farmer/hair.png': Image(*size('farmer/hair.png')),
        'farmer/shirts.png': Image(*size('farmer/shirts.png')),
        'farmer/hats.png': Image(*size('farmer/hats.png')),
    }
    base, pant, hairs, shirts, hats = (files['farmer/' + n] for n in ('body.png', 'pants.png', 'hair.png', 'shirts.png', 'hats.png'))
    for row, direction in enumerate(DIRECTIONS):
        for column in WALK_COLUMNS:
            base.paste(body(direction, column), column * FRAME_W, row * FRAME_H)
            base.paste(arms(direction, column), (column + 6) * FRAME_W, row * FRAME_H)
            pant.paste(pants(direction, column), column * FRAME_W, row * FRAME_H)
        for style in range(HAIR_STYLES):
            hairs.paste(hair(style, direction), style % 8 * FRAME_W, style // 8 * 96 + row * FRAME_H)
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
