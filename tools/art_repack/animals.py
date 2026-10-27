"""Package 8: sprites/animals/chicken.png (16 x 16 frames) and sprites/animals/cow.png (32 x 32 frames), cut from Tiny Creatures (CC0).

Layout (AnimalsDecorator): four walk columns, rows down, right, up; left is the right row mirrored by the game. Tiny Creatures has one picture
per animal, facing left, so the right row is that picture flipped, the down and up rows use it as it is, and the walk frames 1 and 3 lift it 1 px
(the bob the drawn animals had). The cow is 16 x 16 in the pack and is doubled (nearest neighbour) to fill its 32 x 32 frame: at 1x it was as small as the chicken.

chicken() and cow() below are the earlier drawn animals; only ui.py still reads chicken() for the co-op tab pictogram (assets/ui is not part of this swap).
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
        c.rect(28, 12 + bob, 4, 4, PINK)
        c.rect(28, 15 + bob, 4, 1, (214, 130, 140))
        c.dot(29, 13 + bob, draw.OUT)
        c.dot(30, 13 + bob, draw.OUT)
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
COW = (1, 15)
BOB = [0, 1, 0, 1]      # pixels each walk frame lifts the picture


def frames(picture, frame, bottom, scale=1):
    """The 4 x 3 sheet of one animal: picture is the 16 x 16 cell, frame the cell size of the sheet, bottom whether it stands at the foot of the frame, scale its pixel size."""
    size = 16 * scale
    sheet = Image(frame * 4, frame * 3)
    for row, pic in enumerate((picture, picture.flip_h(), picture)):
        pic = pic.scale(scale) if scale > 1 else pic
        for column in range(4):
            x = column * frame + (frame - size) // 2
            y = row * frame + (frame - size if bottom else 0) - BOB[column] * scale
            sheet.paste(pic, x, y)
    return sheet


def build(src, size):
    creatures = src.sheet('creatures')
    out = {}
    for rel, cell, frame, bottom, scale in (('sprites/animals/chicken.png', CHICKEN, 16, False, 1), ('sprites/animals/cow.png', COW, 32, True, 2)):
        full = Image(*size(rel))
        full.paste(frames(creatures.cell(*cell), frame, bottom, scale), 0, 0)
        out[rel] = full
    return out
