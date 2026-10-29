"""The pet companions and the emote bubble: sprites/pets/cat.png and sprites/pets/dog.png, ui/emotes.png and ui/emote_bubble.png.

No CC0 pack in art/source has a cat, a dog or an emote picture (Pixel Animals has cows and a sheep, Random Animals a fox), so every picture
is drawn here with draw.Canvas, in the Kenney palette, and gets the same 1 px dark outline as the other drawn animals.

Pet sheets (4 x 4 frames of 16 x 16, 64 x 64): row 0 facing down, row 1 facing right (the game mirrors it for left), row 2 facing up,
row 3 special poses. Columns 0-3 of rows 0-2 are the walk cycle: column 0 stands, columns 1 and 3 step (the moving legs shift 1 px and
one leg lifts, WALK below), column 2 stands again. Row 3 column 0 is sitting (facing down), column 1 sleeping, columns 2-3 are empty.
Cat: pointed ears, a long thin tail, a small face. Dog: floppy ears, a muzzle, a short tail.

Emote icons (ui/emotes.png, 12 x 16 px in one row, index = emote id): 0 heart, 1 laugh, 2 wave, 3 question mark, 4 exclamation mark,
5 zzz, 6 music note, 7 angry, 8 sad, 9 thumbs up, 10 coin, 11 flower. Speech bubble (ui/emote_bubble.png, 24 x 24): white, 1 px outline,
a tail at the bottom centre; a 16 x 16 icon sits centred at (4, 4).
"""
from draw import Canvas, RED, GOLD, GOLD_L, GREEN, GREEN_L, BLUE_D
from pngio import Image
from trees import blob

CAT = ((92, 84, 104), (150, 140, 158), (196, 188, 204))         # fur: dark, base, light (blue-grey tabby)
DOG = ((120, 72, 44), (176, 118, 68), (222, 168, 104))          # fur: brown and gold
DOG_EAR = ((96, 56, 36), (120, 72, 44), (150, 98, 60))
MUZZLE = ((210, 190, 150), (240, 224, 190), (252, 244, 224))
PINK = (232, 150, 160)
EYE = (30, 24, 34)
WALK = [0, 1, 0, -1]     # per walk column: the stride of the moving legs (animals.LEGS uses the same idea)

SKIN = (240, 196, 150)
SKIN_D = (200, 140, 100)
YELLOW = (255, 210, 60)
PURPLE = (120, 70, 160)
TEAR = (90, 180, 240)
NOTE = (70, 50, 120)
ZZZ = (50, 70, 130)
PETAL = (240, 130, 170)
COIN_D = (190, 120, 40)


# --- pets --------------------------------------------------------------------------------------------------------------

def front_head(c, dog, back):
    """The head seen from the front (back=False: eyes and nose) or from behind (back=True)."""
    if dog:
        blob(c, 8, 5.5, 4.2, 3.4, DOG, texture=False)
        blob(c, 3.6, 6.4, 1.5, 2.8, DOG_EAR, texture=False)           # floppy ears hang either side
        blob(c, 12.4, 6.4, 1.5, 2.8, DOG_EAR, texture=False)
        if not back:
            blob(c, 8, 8.2, 2.3, 1.5, MUZZLE, texture=False)
            c.dot(8, 7, EYE)
            c.dot(6, 4, EYE)
            c.dot(10, 4, EYE)
    else:
        blob(c, 8, 6, 3.9, 3.0, CAT, texture=False)
        c.dot(4, 1, CAT[0])                                            # pointed ears
        c.rect(4, 2, 2, 1, CAT[1])
        c.dot(11, 1, CAT[0])
        c.rect(10, 2, 2, 1, CAT[1])
        c.dot(5, 2, PINK)
        c.dot(10, 2, PINK)
        if back:
            c.line(8, 3, 8, 6, CAT[0])                                 # tabby stripe
        else:
            c.dot(6, 5, EYE)
            c.dot(9, 5, EYE)
            c.dot(8, 7, PINK)


def pet(kind, view, col):
    """One 16 x 16 frame: view is 'down', 'right', 'up', 'sit' or 'sleep'; col the walk column (0-3)."""
    dog = kind == 'dog'
    fur = DOG if dog else CAT
    dark = fur[0]
    step = WALK[col]
    c = Canvas(16, 16)
    if view == 'right':
        if dog:
            blob(c, 7, 8.5, 4.6, 2.8, fur, texture=False)
        else:
            blob(c, 6, 8.5, 4.4, 2.6, fur, texture=False)
        for x, lift in ((9 + step, step == 1), ((2 if dog else 3) - step, step == -1)):
            c.rect(x, 11, 2 if dog else 1, 3 - lift, dark)            # the moving leg lifts: one row shorter
        if dog:
            blob(c, 11, 5.5, 3.2, 3.0, fur, texture=False)
            blob(c, 9, 6.5, 1.6, 2.6, DOG_EAR, texture=False)        # floppy ear
            blob(c, 13, 6.5, 1.6, 1.4, MUZZLE, texture=False)
            c.dot(14, 6, EYE)                                          # nose
            c.dot(11, 4, EYE)
            c.line(2, 8, 2 - step, 3, fur[1])                          # short tail
        else:
            blob(c, 11, 5.5, 2.9, 2.6, fur, texture=False)
            c.dot(9, 1, dark)                                          # two pointed ears
            c.rect(8, 2, 3, 1, fur[1])
            c.dot(12, 1, dark)
            c.rect(11, 2, 3, 1, fur[1])
            c.dot(9, 2, PINK)
            c.dot(12, 2, PINK)
            c.dot(12, 5, EYE)
            c.dot(14, 6, PINK)                                         # nose
            c.dot(5, 6, dark)                                          # tabby stripes
            c.dot(7, 7, dark)
            c.line(2, 8, 1, 4, fur[1])                                 # long tail
            c.line(1, 4, 2 - step, 2, fur[1])
    elif view in ('down', 'up'):
        front_head(c, dog, back=view == 'up')
        if dog:
            blob(c, 8, 11.5, 3.6, 2.4, fur, texture=False)
            for x, lift in ((5 + step, step == 1), (9 - step, step == -1)):
                c.rect(x, 13, 2, 2 - lift, dark)
            if view == 'up':
                c.line(11, 10, 13 - step, 8, fur[1])                   # tail wags up at the side
        else:
            blob(c, 8, 11, 3.2, 2.2, fur, texture=False)
            for x, lift in ((5 + step, step == 1), (10 - step, step == -1)):
                c.rect(x, 12, 1, 2 - lift, dark)
            if view == 'up':
                c.line(11, 11, 13 - step, 4, fur[1])                   # tail up at the side
    elif view == 'sit':
        front_head(c, dog, back=False)
        if dog:
            blob(c, 8, 10.8, 3.7, 3.0, fur, texture=False)
            c.rect(6, 14, 2, 1, dark)
            c.rect(9, 14, 2, 1, dark)
            c.line(11, 13, 13, 12, fur[1])
            c.line(13, 12, 13, 10, fur[1])
        else:
            blob(c, 8, 10.5, 3.6, 3.0, fur, texture=False)
            c.rect(6, 14, 2, 1, dark)
            c.rect(9, 14, 2, 1, dark)
            c.line(11, 13, 13, 12, fur[1])                              # tail curled round the feet
            c.line(13, 12, 13, 9, fur[1])
    elif view == 'sleep':
        if dog:
            blob(c, 9, 10.5, 5.4, 2.6, fur, texture=False)
            blob(c, 4, 9.5, 2.8, 2.6, fur, texture=False)
            blob(c, 5, 8.5, 1.2, 2.0, DOG_EAR, texture=False)        # ear over the eye
            c.line(2, 9, 3, 9, EYE)                                     # closed eye
            c.dot(1, 10, EYE)
            c.line(12, 12, 14, 10, fur[1])
        else:
            blob(c, 9, 10.5, 5.2, 2.6, fur, texture=False)
            blob(c, 3.5, 9.5, 2.6, 2.4, fur, texture=False)
            c.dot(1, 6, dark)
            c.dot(5, 6, dark)
            c.line(2, 9, 3, 9, EYE)                                     # closed eyes
            c.dot(1, 10, PINK)
            c.line(5, 13, 12, 13, fur[1])                               # tail lying on the floor
            c.dot(12, 12, fur[1])
    return c.outline()


def pet_sheet(kind):
    sheet = Image(64, 64)
    for col in range(4):
        for row, view in enumerate(('down', 'right', 'up')):
            sheet.paste(pet(kind, view, col), col * 16, row * 16)
    sheet.paste(pet(kind, 'sit', 0), 0, 48)
    sheet.paste(pet(kind, 'sleep', 0), 16, 48)
    return sheet


# --- emotes ------------------------------------------------------------------------------------------------------------

HEART = [
    ".XXX....XXX.",
    "XXhXXXXXXXXX",
    "XhXXXXXXXXXX",
    ".XXXXXXXXXX.",
    "..XXXXXXXX..",
    "...XXXXXX...",
    "....XXXX....",
    ".....XX.....",
]

HAND = [
    "........XX......",
    ".....XX.XX.XX...",
    ".....XX.XX.XX...",
    ".....XX.XX.XX...",
    ".....XX.XX.XX...",
    "..XXXXXXXXXXX...",
    "..XXXXXXXXXXX...",
    "...XXXXXXXXXX...",
    "...XXXXXXXXXX...",
    "...XXXXXXXXX....",
    "....XXXXXXXX....",
    "....XXXXXXX.....",
    "....XXXXXXX.....",
    "....XXXXXXX.....",
]

THUMB = [
    ".....XXX........",
    ".....XXX........",
    ".....XXX........",
    ".....XXX........",
    "....XXXXXXXX....",
    "....XXXXXXXXX...",
    "...XXXXXXXXXX...",
    "...XXXXXXXXXX...",
    "...XXXXXXXXXX...",
    "...XXXXXXXXXX...",
    "....XXXXXXXXX...",
    "....XXXXXXXX....",
    ".....XXXXXXX....",
]

QUESTION = [
    ".#####.",
    "##...##",
    ".....##",
    "....##.",
    "...##..",
    "..##...",
    "..##...",
    ".......",
    "..##...",
    "..##...",
]

EXCLAMATION = [
    ".##.",
    ".##.",
    ".##.",
    ".##.",
    ".##.",
    ".##.",
    "....",
    ".##.",
    ".##.",
]


def art(c, rows, palette, x, y):
    c.art(rows, palette, x, y)


def heart():
    c = Canvas(16, 16)
    art(c, HEART, {'X': RED, 'h': (255, 150, 140)}, 2, 4)
    return c.outline()


def laugh():
    c = Canvas(16, 16)
    c.ellipse(7.5, 7.5, 6.6, 6.6, YELLOW)
    for x, y in ((4, 6), (5, 5), (6, 6), (9, 6), (10, 5), (11, 6)):    # happy closed eyes
        c.dot(x, y, EYE)
    c.ellipse(7.5, 10.5, 2.8, 2.2, EYE)                                # open mouth
    c.ellipse(7.5, 11.4, 1.5, 0.9, PINK)                               # tongue
    return c.outline()


def wave():
    c = Canvas(16, 16)
    art(c, HAND, {'X': SKIN}, 0, 1)
    return c.outline()


def question():
    c = Canvas(16, 16)
    art(c, QUESTION, {'#': BLUE_D}, 4, 3)
    return c.outline()


def exclamation():
    c = Canvas(16, 16)
    art(c, EXCLAMATION, {'#': RED}, 6, 3)
    return c.outline()


def zzz():
    c = Canvas(16, 16)
    big = ["XXXXX", "...X.", "..X..", ".X...", "XXXXX"]
    med = ["XXXX", "..X.", ".X..", "XXXX"]
    small = ["XXX", ".X.", "XXX"]
    art(c, big, {'X': ZZZ}, 1, 10)
    art(c, med, {'X': ZZZ}, 6, 6)
    art(c, small, {'X': ZZZ}, 11, 2)
    return c.outline()


def music():
    c = Canvas(16, 16)
    c.ellipse(4.5, 11.5, 2.4, 1.8, NOTE)
    c.ellipse(11.5, 9.5, 2.4, 1.8, NOTE)
    c.rect(6, 2, 1, 9, NOTE)
    c.rect(13, 0, 1, 9, NOTE)
    c.line(6, 2, 13, 0, NOTE)
    c.line(6, 3, 13, 1, NOTE)
    return c.outline()


def angry():
    c = Canvas(16, 16)
    for cx, cy in ((4.6, 4.6), (10.4, 4.6), (4.6, 10.4), (10.4, 10.4)):   # four lobes with a cross-shaped notch
        c.ellipse(cx, cy, 2.1, 2.1, RED)
    c.dot(5, 4, (255, 140, 120))
    c.dot(11, 4, (255, 140, 120))
    return c.outline()


def sad():
    c = Canvas(16, 16)
    c.ellipse(7.5, 7.5, 6.6, 6.6, YELLOW)
    c.dot(5, 5, EYE)
    c.dot(10, 5, EYE)
    for x, y in ((5, 11), (6, 10), (7, 10), (8, 10), (9, 10), (10, 11)):   # frown
        c.dot(x, y, EYE)
    c.dot(12, 8, TEAR)                                                     # tear
    c.rect(11, 9, 2, 2, TEAR)
    return c.outline()


def thumbs():
    c = Canvas(16, 16)
    art(c, THUMB, {'X': SKIN}, 0, 1)
    return c.outline()


def coin():
    c = Canvas(16, 16)
    c.ellipse(7.5, 7.5, 6.6, 6.6, COIN_D)
    c.ellipse(7.5, 7.5, 5.6, 5.6, GOLD)
    c.ellipse(7.5, 7.5, 3.6, 3.6, GOLD_L)
    c.dot(4, 5, (255, 246, 200))
    c.dot(5, 4, (255, 246, 200))
    return c.outline()


def flower():
    c = Canvas(16, 16)
    c.line(7, 12, 7, 14, GREEN)
    c.dot(8, 13, GREEN_L)
    c.dot(9, 12, GREEN_L)
    for cx, cy in ((7.5, 4.6), (10.6, 6.6), (9.4, 10.6), (5.6, 10.6), (4.4, 6.6)):
        c.ellipse(cx, cy, 2.2, 2.2, PETAL)
    c.ellipse(7.5, 7.5, 1.8, 1.8, YELLOW)
    return c.outline()


EMOTES = [heart, laugh, wave, question, exclamation, zzz, music, angry, sad, thumbs, coin, flower]


def emotes_sheet():
    sheet = Image(16 * len(EMOTES), 16)
    for i, fn in enumerate(EMOTES):
        sheet.paste(fn(), i * 16, 0)
    return sheet


def bubble():
    """24 x 24: a rounded white box (x 1-22, y 1-20) with a wedge tail at the bottom centre (rows 21-22), outlined."""
    c = Canvas(24, 24)
    for y in range(1, 21):
        for x in range(1, 23):
            if (x, y) not in ((1, 1), (22, 1), (1, 20), (22, 20)):
                c.dot(x, y, (255, 255, 255))
    c.rect(9, 21, 6, 1, (255, 255, 255))
    c.rect(11, 22, 2, 1, (255, 255, 255))
    return c.outline()


def build():
    """{path under assets/: Image} for the pet sheets, the emote strip and the speech bubble."""
    return {
        'sprites/pets/cat.png': pet_sheet('cat'),
        'sprites/pets/dog.png': pet_sheet('dog'),
        'ui/emotes.png': emotes_sheet(),
        'ui/emote_bubble.png': bubble(),
    }


def provenance_entries(nosum):
    """{path: (package, [sources], text)} for provenance.py."""
    entries = {}
    for kind in ('cat', 'dog'):
        entries[f'sprites/pets/{kind}.png'] = ('8 Animals', [nosum], f'{kind.capitalize()} companion, 16 x 16 frames in a 4 x 4 sheet: rows down, right, up (four walk frames, the moving legs shift 1 px), row 3 sitting and sleeping (`pets_emotes.py`)')
    entries['ui/emotes.png'] = ('9 UI', [nosum], 'Twelve 16 x 16 emote icons in one row, index = emote id: heart, laugh, wave, question mark, exclamation mark, zzz, music note, angry, sad, thumbs up, coin, flower (`pets_emotes.py`)')
    entries['ui/emote_bubble.png'] = ('9 UI', [nosum], '24 x 24 speech bubble: white, 1 px outline, tail at the bottom centre; a 16 x 16 icon sits at (4, 4) (`pets_emotes.py`)')
    return entries
