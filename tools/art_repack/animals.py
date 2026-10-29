"""Package 8: sprites/animals/chicken.png, goat.png and sheep.png (16 x 16 frames) cut from Tiny Creatures (CC0); duck.png and pig.png (16 x 16) drawn here; cow.png (32 x 32 frames) drawn here.

Layout (AnimalsDecorator): four walk columns, rows down, right, up; left is the right row mirrored by the game. Tiny Creatures has one picture
per animal, facing left, so the right row is that picture flipped, the down and up rows use it as it is, and the walk frames 1 and 3 lift it 1 px
(the bob the drawn animals had). The cow stays the one drawn here at its 32 x 32 frame size (cow() below): Tiny Creatures' 16 x 16 cow would have to be upscaled, which gives a 2 px outline that clashes with the rest of the art.

cow() below draws the cow; chicken() is still read by ui.py for the co-op tab pictogram.
"""
import draw
from draw import Canvas
from pngio import Image
from trees import blob

WHITE = ((176, 168, 160), (240, 234, 224), (255, 252, 248))
DARK = ((30, 24, 34), (58, 50, 66), (96, 88, 110))
ORANGE = (240, 150, 50)
COMB = (222, 60, 52)
COMB_D = (160, 36, 44)
BEAK_D = (214, 110, 30)
PINK = (240, 170, 170)
LEGS = [(0, 0), (1, 1), (0, 0), (-1, 1)]        # per walk frame: horizontal shift of the front leg, how far the other leg lifts


def chicken(direction, frame):
    c = Canvas(16, 16)
    shift, lift = LEGS[frame]
    bob = 1 if frame in (1, 3) else 0
    if direction == 'right':
        for x, up in ((6, lift), (9 + shift, 0)):
            c.rect(x, 12 + up, 1, 3 - up, ORANGE)
            c.rect(x, 14, 2, 1, ORANGE)
        blob(c, 7, 9 + bob, 5, 4, WHITE)
        blob(c, 3, 6 + bob, 2.2, 3, WHITE)                    # tail
        blob(c, 11, 5 + bob, 2.6, 2.8, WHITE)                  # head
        c.rect(11, 2 + bob, 3, 1, COMB)
        c.dot(11, 3 + bob, COMB_D)
        c.rect(14, 5 + bob, 2, 1, ORANGE)
        c.dot(14, 6 + bob, BEAK_D)
        c.dot(13, 7 + bob, COMB)                                # wattle
        c.dot(12, 4 + bob, draw.OUT)
        c.rect(5, 8 + bob, 4, 2, WHITE[0])                     # wing
        c.line(5, 9 + bob, 8, 9 + bob, WHITE[1])
        c.dot(4, 6 + bob, WHITE[0])                            # tail feather shade
    elif direction == 'down':
        for x, up in ((5, lift), (10 + shift, 0)):
            c.rect(x, 12 + up, 1, 3 - up, ORANGE)
            c.rect(x - 1, 14, 3, 1, ORANGE)
        blob(c, 8, 9 + bob, 5.2, 4.6, WHITE)
        blob(c, 8, 5 + bob, 3, 3, WHITE)
        c.rect(7, 2 + bob, 2, 1, COMB)
        c.dot(7, 3 + bob, COMB_D)
        c.rect(7, 6 + bob, 2, 1, ORANGE)
        c.dot(7, 7 + bob, COMB)                                 # wattle under the beak
        c.dot(8, 7 + bob, COMB_D)
        c.dot(6, 4 + bob, draw.OUT)
        c.dot(9, 4 + bob, draw.OUT)
        c.rect(2, 8 + bob, 2, 3, WHITE[0])
        c.rect(12, 8 + bob, 2, 3, WHITE[0])
        c.line(3, 8 + bob, 3, 10 + bob, WHITE[1])
        c.line(12, 8 + bob, 12, 10 + bob, WHITE[1])
    else:
        for x, up in ((5, lift), (10 + shift, 0)):
            c.rect(x, 12 + up, 1, 3 - up, ORANGE)
            c.rect(x - 1, 14, 3, 1, ORANGE)
        blob(c, 8, 9 + bob, 5.2, 4.6, WHITE)
        blob(c, 8, 5 + bob, 2.6, 2.6, WHITE)
        c.rect(7, 2 + bob, 2, 1, COMB)
        blob(c, 8, 12 + bob, 3, 2, WHITE)                      # tail feathers
        c.rect(7, 8 + bob, 2, 4, WHITE[0])
    return c.outline()


def cow(direction, frame):
    c = Canvas(32, 32)
    shift, lift = LEGS[frame]
    bob = 1 if frame in (1, 3) else 0
    spot = DARK
    if direction == 'right':
        for x, up in ((6, lift), (10 + shift, 0), (19, 0), (23 - shift, lift)):
            c.rect(x, 19 + up, 3, 9 - up, WHITE[0])
            c.rect(x, 26, 3, 2, DARK[1])
        c.line(3, 11 + bob, 2, 18, WHITE[0])
        c.rect(1, 18, 2, 2, DARK[1])
        blob(c, 14, 14 + bob, 10.5, 6.5, WHITE)
        blob(c, 11, 12 + bob, 3.6, 3, spot)
        blob(c, 19, 17 + bob, 3.2, 2.6, spot)
        blob(c, 26, 11 + bob, 4.4, 4.2, WHITE)
        c.rect(27, 12 + bob, 4, 4, PINK)
        c.rect(27, 15 + bob, 4, 1, (214, 130, 140))
        c.dot(28, 13 + bob, draw.OUT)
        c.dot(29, 13 + bob, draw.OUT)
        c.rect(15, 19 + bob, 4, 2, PINK)                        # udder
        c.dot(16, 21 + bob, (214, 130, 140))
        c.rect(24, 7 + bob, 2, 3, (230, 210, 170))
        c.rect(23, 9 + bob, 2, 3, DARK[1])
        c.dot(27, 9 + bob, draw.OUT)
    elif direction == 'down':
        for x, up in ((8, lift), (21 + shift, 0)):
            c.rect(x, 22 + up, 4, 6 - up, WHITE[0])
            c.rect(x, 26, 4, 2, DARK[1])
        blob(c, 16, 17 + bob, 10, 9, WHITE)
        blob(c, 11, 18 + bob, 3.6, 3.4, spot)
        blob(c, 22, 15 + bob, 3, 4, spot)
        blob(c, 16, 9 + bob, 6, 5.4, WHITE)
        c.rect(12, 11 + bob, 8, 4, PINK)
        c.rect(12, 14 + bob, 8, 1, (214, 130, 140))
        c.dot(14, 12 + bob, draw.OUT)
        c.dot(17, 12 + bob, draw.OUT)
        c.dot(14, 13 + bob, (180, 90, 100))
        c.dot(17, 13 + bob, (180, 90, 100))
        c.dot(11, 7 + bob, draw.OUT)
        c.dot(20, 7 + bob, draw.OUT)
        c.rect(8, 3 + bob, 2, 3, (230, 210, 170))
        c.rect(22, 3 + bob, 2, 3, (230, 210, 170))
        blob(c, 10, 9 + bob, 2, 1.6, DARK)
        blob(c, 22, 9 + bob, 2, 1.6, DARK)
    else:
        for x, up in ((8, lift), (21 + shift, 0)):
            c.rect(x, 22 + up, 4, 6 - up, WHITE[0])
            c.rect(x, 26, 4, 2, DARK[1])
        blob(c, 16, 15 + bob, 10, 9.5, WHITE)
        blob(c, 12, 13 + bob, 3.6, 3.4, spot)
        blob(c, 21, 17 + bob, 3, 3, spot)
        blob(c, 16, 7 + bob, 5, 3.4, WHITE)
        c.rect(8, 4 + bob, 2, 3, (230, 210, 170))
        c.rect(22, 4 + bob, 2, 3, (230, 210, 170))
        c.line(16, 14 + bob, 16, 22, WHITE[0])
        c.rect(15, 22, 2, 2, DARK[1])
    return c.outline()


CHICKEN = (0, 15)       # column, row of tilemap_packed.png
BOB = [0, 1, 0, 1]      # pixels each walk frame lifts the picture


def chicken_frames(picture, right_pic=None):
    """The 4 x 3 sheet of a creature: the 16 x 16 Tiny Creatures picture for rows down, right and up, lifted 1 px on frames 1 and 3 when row 0 is clear."""
    sheet = Image(64, 48)
    right = right_pic if right_pic is not None else picture.flip_h()
    top_clear = all(p[3] == 0 for p in picture.px[0])
    for row, pic in enumerate((picture, right, picture)):
        for column in range(4):
            bob = BOB[column] if top_clear else 0
            cell = Image(16, 16)
            cell.paste(pic, 0, -bob)
            sheet.paste(cell, column * 16, row * 16)
    return sheet


DUCK_WHITE = ((196, 178, 120), (246, 236, 170), (255, 252, 226))     # a white duck with a yellow cast: shaded, base, lit
DUCK_BILL = (240, 150, 50)
DUCK_BILL_D = (200, 104, 36)
PIG = ((200, 104, 124), (244, 176, 184), (255, 214, 218))          # pink: shaded, base, lit
PIG_SNOUT = (222, 130, 146)
PIG_EYE = (60, 30, 40)
GOAT = (2, 15)          # column, row of tilemap_packed.png (Tiny Creatures' goat)
SHEEP = (3, 15)         # Tiny Creatures' sheep


def duck(direction, frame):
    """A 16 x 16 duck facing the way of its row; legs and bob follow the chicken's walk."""
    c = Canvas(16, 16)
    shift, lift = LEGS[frame]
    bob = 1 if frame in (1, 3) else 0
    if direction == 'right':
        for x, up in ((6, lift), (9 + shift, 0)):
            c.rect(x, 13 + up, 1, 2 - up, DUCK_BILL)
            c.rect(x - 1, 14, 3, 1, DUCK_BILL)
        blob(c, 7, 9 + bob, 5, 3.6, DUCK_WHITE)
        blob(c, 11, 5 + bob, 2.6, 2.6, DUCK_WHITE)             # head
        c.rect(3, 6 + bob, 2, 2, DUCK_WHITE[1])                # tail
        c.dot(2, 6 + bob, DUCK_WHITE[1])
        c.rect(13, 4 + bob, 3, 2, DUCK_BILL)                   # flat bill
        c.dot(15, 5 + bob, DUCK_BILL_D)
        c.dot(11, 4 + bob, draw.OUT)                           # eye
        c.line(5, 9 + bob, 9, 9 + bob, DUCK_WHITE[0])          # wing line
    elif direction == 'down':
        for x, up in ((5, lift), (10 + shift, 0)):
            c.rect(x, 13 + up, 1, 2 - up, DUCK_BILL)
            c.rect(x - 1, 14, 3, 1, DUCK_BILL)
        blob(c, 8, 9 + bob, 5, 4.4, DUCK_WHITE)
        blob(c, 8, 5 + bob, 3, 3, DUCK_WHITE)
        c.rect(6, 6 + bob, 4, 2, DUCK_BILL)                    # bill
        c.dot(7, 7 + bob, DUCK_BILL_D)
        c.dot(6, 4 + bob, draw.OUT)
        c.dot(9, 4 + bob, draw.OUT)
        c.line(4, 9 + bob, 4, 11 + bob, DUCK_WHITE[0])         # wings
        c.line(11, 9 + bob, 11, 11 + bob, DUCK_WHITE[0])
    else:
        for x, up in ((5, lift), (10 + shift, 0)):
            c.rect(x, 13 + up, 1, 2 - up, DUCK_BILL)
            c.rect(x - 1, 14, 3, 1, DUCK_BILL)
        blob(c, 8, 9 + bob, 5, 4.4, DUCK_WHITE)
        blob(c, 8, 5 + bob, 2.8, 2.8, DUCK_WHITE)
        blob(c, 8, 12 + bob, 2, 1.6, DUCK_WHITE)            # tail
        c.line(8, 8 + bob, 8, 12 + bob, DUCK_WHITE[0])         # back seam
    return c.outline()


def pig(direction, frame):
    """A 16 x 16 pink pig facing the way of its row, with a snout, small ears and four legs."""
    c = Canvas(16, 16)
    shift, lift = LEGS[frame]
    bob = 1 if frame in (1, 3) else 0
    if direction == 'right':
        for x, up in ((4, lift), (9 + shift, 0), (11, lift)):
            c.rect(x, 12 + up, 2, 3 - up, PIG[0])
        blob(c, 7, 9 + bob, 5.2, 4, PIG)
        blob(c, 12, 7 + bob, 3, 3.2, PIG)                      # head
        c.rect(14, 7 + bob, 2, 3, PIG_SNOUT)                   # snout
        c.dot(14, 8 + bob, PIG[0])                             # nostril
        c.dot(12, 5 + bob, PIG[0])                             # ear
        c.dot(11, 6 + bob, PIG[0])
        c.dot(12, 6 + bob, PIG_EYE)                            # eye
        c.dot(2, 8 + bob, PIG[2])                              # curly tail
        c.dot(1, 7 + bob, PIG[0])
    elif direction == 'down':
        for x, up in ((4, lift), (9 + shift, 0)):
            c.rect(x, 13 + up, 2, 2 - up, PIG[0])
        blob(c, 8, 9 + bob, 5, 4.5, PIG)
        blob(c, 8, 5 + bob, 3.8, 3.2, PIG)
        c.rect(4, 2 + bob, 2, 3, PIG[1])                       # ears
        c.rect(10, 2 + bob, 2, 3, PIG[1])
        c.dot(4, 2 + bob, PIG[0])
        c.dot(11, 2 + bob, PIG[0])
        c.rect(6, 6 + bob, 4, 2, PIG_SNOUT)                    # snout
        c.dot(6, 6 + bob, PIG[0])
        c.dot(9, 6 + bob, PIG[0])
        c.dot(6, 4 + bob, PIG_EYE)
        c.dot(9, 4 + bob, PIG_EYE)
    else:
        for x, up in ((4, lift), (9 + shift, 0)):
            c.rect(x, 13 + up, 2, 2 - up, PIG[0])
        blob(c, 8, 9 + bob, 5, 4.5, PIG)
        blob(c, 8, 5 + bob, 3.8, 3.2, PIG)
        c.rect(4, 2 + bob, 2, 3, PIG[1])                       # ears
        c.rect(10, 2 + bob, 2, 3, PIG[1])
        c.dot(4, 2 + bob, PIG[0])
        c.dot(11, 2 + bob, PIG[0])
        c.dot(13, 11 + bob, PIG[0])                            # curly tail
        c.dot(14, 10 + bob, PIG[2])
    return c.outline()


def sheet_of(picture):
    """The 4 x 3 sheet of a procedurally drawn creature: picture(direction, frame) for every cell."""
    sheet = Image(64, 48)
    for row, d in enumerate(['down', 'right', 'up']):
        for column in range(4):
            sheet.paste(picture(d, column), column * 16, row * 16)
    return sheet


def build(src, size):
    chick = Image(*size('sprites/animals/chicken.png'))
    cell = src.sheet('creatures').cell(*CHICKEN)
    chick.paste(chicken_frames(cell, right_pic=cell), 0, 0)
    cows = Image(*size('sprites/animals/cow.png'))
    for r, d in enumerate(['down', 'right', 'up']):
        for f in range(4):
            cows.paste(cow(d, f), f * 32, r * 32)
    creatures = src.sheet('creatures')
    return {
        'sprites/animals/chicken.png': chick,
        'sprites/animals/cow.png': cows,
        'sprites/animals/goat.png': chicken_frames(creatures.cell(*GOAT)),
        'sprites/animals/sheep.png': chicken_frames(creatures.cell(*SHEEP)),
        'sprites/animals/duck.png': sheet_of(duck),
        'sprites/animals/pig.png': sheet_of(pig),
    }
