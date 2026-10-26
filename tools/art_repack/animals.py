"""Package 8: sprites/animals/chicken.png (16 x 16 frames) and sprites/animals/cow.png (32 x 32 frames), drawn here.

Layout (AnimalsDecorator): four walk columns, rows down, right, up; left is the right row mirrored by the game. No CC0 pack has top-down farm
animals with a walk cycle in this style, so the pictures are drawn from shaded blobs in the Kenney palette.
"""
import draw
from draw import Canvas
from pngio import Image
from trees import blob

WHITE = ((176, 168, 160), (240, 234, 224), (255, 252, 248))
DARK = ((30, 24, 34), (58, 50, 66), (96, 88, 110))
ORANGE = (240, 150, 50)
COMB = (222, 60, 52)
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
        c.rect(11, 2 + bob, 2, 1, COMB)
        c.rect(14, 5 + bob, 2, 1, ORANGE)
        c.dot(12, 4 + bob, draw.OUT)
        c.rect(5, 8 + bob, 4, 2, WHITE[0])                     # wing
    elif direction == 'down':
        for x, up in ((5, lift), (10 + shift, 0)):
            c.rect(x, 12 + up, 1, 3 - up, ORANGE)
            c.rect(x - 1, 14, 3, 1, ORANGE)
        blob(c, 8, 9 + bob, 5.2, 4.6, WHITE)
        blob(c, 8, 5 + bob, 3, 3, WHITE)
        c.rect(7, 2 + bob, 2, 1, COMB)
        c.rect(7, 6 + bob, 2, 2, ORANGE)
        c.dot(6, 4 + bob, draw.OUT)
        c.dot(9, 4 + bob, draw.OUT)
        c.rect(2, 8 + bob, 2, 3, WHITE[0])
        c.rect(12, 8 + bob, 2, 3, WHITE[0])
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
        c.dot(29, 13 + bob, draw.OUT)
        c.dot(30, 13 + bob, draw.OUT)
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
        c.dot(14, 12 + bob, draw.OUT)
        c.dot(17, 12 + bob, draw.OUT)
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


def build(size):
    rows = ['down', 'right', 'up']
    chick = Image(*size('sprites/animals/chicken.png'))
    cows = Image(*size('sprites/animals/cow.png'))
    for r, d in enumerate(rows):
        for f in range(4):
            chick.paste(chicken(d, f), f * 16, r * 16)
            cows.paste(cow(d, f), f * 32, r * 32)
    return {'sprites/animals/chicken.png': chick, 'sprites/animals/cow.png': cows}
