"""Package 3 and 6: sprites/items.png, the 16 x 16 object sheet (items, forage, resource nodes).

Crop produce comes from the CC0 crop pictures (crops.py); a few pictures come from Kenney Tiny Town; every other icon is drawn here with
shaded blobs and pixel dots in the Kenney palette and a 1 px outline (ADR-040 D1). OBJECTS maps the sheet index the game uses (24 columns)
to a function that returns the picture.
"""
import draw
from draw import Canvas
from trees import blob
import crops

GREY = ((82, 96, 124), (140, 156, 181), (192, 203, 220))
COAL = ((30, 24, 34), (58, 50, 66), (96, 88, 110))
BROWN = ((119, 56, 51), (170, 96, 66), (214, 140, 90))
STRAW = ((190, 120, 40), (240, 180, 60), (255, 224, 120))
GREENS = ((38, 92, 66), (62, 137, 72), (99, 199, 77))
RED = ((150, 30, 40), (222, 60, 52), (255, 120, 100))
PURPLE = ((70, 40, 110), (120, 70, 160), (175, 130, 210))
BLUE = ((20, 70, 150), (50, 130, 220), (130, 200, 255))
ORANGE = ((170, 70, 30), (240, 130, 50), (255, 190, 100))
WHITE = ((170, 160, 150), (235, 228, 215), (255, 252, 245))
PINK = ((170, 70, 110), (230, 120, 160), (255, 180, 205))
YELLOW = ((200, 140, 20), (255, 210, 60), (255, 244, 150))
COPPER = ((140, 70, 40), (216, 128, 64), (244, 180, 110))


def cv():
    return Canvas()


def done(c):
    return c.outline()


def rock(tones, specks=None, wide=True):
    c = cv()
    blob(c, 8, 10, 6.5 if wide else 5, 4.8, tones)
    blob(c, 5, 11, 3.5, 3, tones)
    blob(c, 11, 11, 3.5, 3, tones)
    for x, y, col in (specks or []):
        c.dot(x, y, col)
    return done(c)


def packet(src, crop):
    """A seed packet: paper, a fold, a band in the crop's own colour and a sprout."""
    pic = crops.produce_icon(src, crop)
    counts = {}
    for row in pic.px:
        for p in row:
            if p[3] and max(p[:3]) - min(p[:3]) > 60 and sum(p[:3]) > 200:
                key = tuple(v // 16 * 16 for v in p[:3])
                counts[key] = counts.get(key, 0) + 1
    band = max(counts, key=counts.get) if counts else (99, 199, 77)
    band = tuple(min(255, v + 8) for v in band)
    c = cv()
    c.rect(3, 1, 10, 14, (240, 230, 200))
    c.rect(3, 1, 10, 3, (214, 200, 160))
    c.rect(3, 4, 10, 1, (190, 175, 135))
    c.rect(3, 8, 10, 5, band)
    c.rect(3, 8, 10, 1, tuple(min(255, v + 50) for v in band))
    c.rect(7, 5, 2, 3, draw.GREEN)               # a sprout above the band
    c.dot(6, 5, draw.GREEN_L)
    c.dot(9, 5, draw.GREEN_L)
    return done(c)


def tint(img, rgb):
    return img.map_pixels(lambda p: rgb + (255,) if p[:3] == (1, 2, 3) else p)


def egg(big=False, brown=False):
    c = cv()
    tones = ((170, 110, 70), (220, 160, 110), (245, 205, 160)) if brown else WHITE
    blob(c, 8, 9, 4.2 if big else 3.6, 5.5 if big else 4.8, tones)
    return done(c)


def bottle(big=False):
    c = cv()
    c.rect(5 if big else 6, 1, 6 if big else 4, 3, draw.STEEL_L)
    c.rect(4 if big else 5, 4, 8 if big else 6, 11, (245, 245, 250))
    c.rect(4 if big else 5, 4, 2, 11, (255, 255, 255))
    c.rect(4 if big else 5, 12, 8 if big else 6, 3, (200, 215, 235))
    c.rect(5 if big else 6, 7, 6 if big else 4, 3, draw.BLUE)
    return done(c)


def hay():
    c = cv()
    blob(c, 8, 9, 6.5, 5, STRAW)
    for x in range(3, 14, 2):
        c.line(x, 5, x + 1, 13, STRAW[0])
    c.rect(2, 8, 12, 2, (170, 100, 50))
    return done(c)


def bowl(fill, extra=None):
    c = cv()
    c.rect(2, 7, 12, 2, draw.WOOD_L)
    c.rect(3, 9, 10, 4, draw.WOOD)
    c.rect(4, 13, 8, 1, draw.WOOD_D)
    c.rect(3, 5, 10, 3, fill[1])
    c.rect(4, 4, 8, 1, fill[2])
    for x, y in (extra or []):
        c.dot(x, y, fill[2])
    return done(c)


def log():
    c = cv()
    c.rect(1, 5, 14, 6, draw.WOOD)
    c.rect(1, 5, 14, 2, draw.WOOD_L)
    c.rect(1, 10, 14, 1, draw.WOOD_D)
    c.rect(2, 9, 12, 5, draw.WOOD)
    c.rect(2, 9, 12, 1, draw.WOOD_L)
    c.rect(2, 13, 12, 1, draw.WOOD_D)
    c.rect(12, 5, 3, 6, (240, 200, 140))
    c.dot(13, 7, draw.WOOD)
    return done(c)


def bar(tones):
    c = cv()
    c.rect(2, 8, 12, 5, tones[1])
    c.rect(3, 5, 10, 3, tones[2])
    c.rect(2, 8, 12, 1, tones[2])
    c.rect(2, 12, 12, 1, tones[0])
    c.rect(12, 5, 1, 8, tones[0])
    c.dot(5, 6, (255, 255, 255))
    return done(c)


def grass_tuft(tones=GREENS, n=7):
    c = cv()
    for i, x in enumerate(range(2, 14, 12 // n + 1)):
        top = 3 + (i * 5) % 5
        c.line(x, 14, x + (i % 3 - 1), top, tones[1 + i % 2])
    return done(c)


def twig(flip):
    c = cv()
    c.line(2, 13, 13, 4, draw.WOOD)
    c.line(3, 13, 14, 4, draw.WOOD_D)
    c.line(7, 9, 5, 4, draw.WOOD)
    c.line(10, 7, 13, 11, draw.WOOD)
    c.dot(5, 3, draw.GREEN_L)
    c.dot(13, 12, draw.GREEN_L)
    c = done(c)
    return c.flip_h() if flip else c


def weed(k):
    c = cv()
    for i, (x, h) in enumerate(((4, 9), (7, 12), (10, 9), (13, 7))[: 3 + k % 2]):
        c.line(x, 14, x + (k + i) % 3 - 1, 14 - h, GREENS[1 + i % 2])
        c.dot(x + (k + i) % 3 - 1, 14 - h, GREENS[2])
    c.rect(3, 14, 11, 1, GREENS[0])
    return done(c)


def acorn():
    c = cv()
    blob(c, 8, 10, 3.5, 4.2, BROWN)
    c.rect(4, 5, 8, 3, (110, 80, 50))
    c.rect(7, 3, 2, 2, (110, 80, 50))
    return done(c)


def samara():
    c = cv()
    blob(c, 8, 10, 2.4, 2.8, ORANGE)
    c.line(8, 8, 13, 3, (230, 200, 140))
    c.line(9, 8, 14, 4, (230, 200, 140))
    c.rect(10, 4, 3, 2, (240, 215, 160))
    return done(c)


def pinecone():
    c = cv()
    blob(c, 8, 9, 3.6, 5.4, BROWN)
    for y in range(5, 14, 2):
        c.line(5, y, 11, y, BROWN[0])
    return done(c)


def shell(tones, ridges=True):
    c = cv()
    blob(c, 8, 10, 6, 4.5, tones)
    c.rect(2, 12, 12, 2, tones[0])
    if ridges:
        for x in (4, 6, 8, 10, 12):
            c.line(8, 13, x, 7, tones[0])
    return done(c)


def urchin():
    c = cv()
    blob(c, 8, 9, 4.5, 4.5, PURPLE)
    for i in range(12):
        a = i * 0.5236
        import math
        c.line(8, 9, round(8 + 6.5 * math.cos(a)), round(9 + 6.5 * math.sin(a)), PURPLE[0])
    blob(c, 8, 9, 3.2, 3.2, PURPLE)
    return done(c)


def coral():
    c = cv()
    for x, top in ((4, 4), (8, 2), (12, 5)):
        c.line(8, 14, x, top, PINK[1])
        c.line(9, 14, x + 1, top, PINK[0])
        c.dot(x, top - 1, PINK[2])
    c.rect(5, 13, 7, 2, PINK[0])
    return done(c)


def nautilus():
    c = cv()
    blob(c, 8, 9, 6.2, 5.8, ORANGE)
    c.rect(8, 4, 1, 11, ORANGE[0])
    for r in (4, 2.4, 1):
        for y in range(16):
            for x in range(16):
                if abs(((x - 9) ** 2 + (y - 9) ** 2) ** 0.5 - r) < 0.6:
                    c.dot(x, y, WHITE[1])
    return done(c)


def rainbow_shell():
    c = cv()
    blob(c, 8, 10, 6.5, 5.2, WHITE)
    cols = [RED[1], ORANGE[1], YELLOW[1], GREENS[2], BLUE[1], PURPLE[1]]
    for i, col in enumerate(cols):
        c.line(8, 14, 2 + i * 2, 6 + abs(i - 2) // 2, col)
    return done(c)


def flower(petals, centre=draw.GOLD, stem=True):
    c = cv()
    if stem:
        c.line(8, 14, 8, 8, draw.GREEN)
        c.line(8, 12, 5, 10, draw.GREEN_L)
    for dx, dy in ((0, -3), (3, 0), (0, 3), (-3, 0)):
        blob(c, 8 + dx, 6 + dy, 2.4, 2.4, petals)
    c.rect(7, 5, 2, 2, centre)
    return done(c)


def berries(tones, n=5):
    c = cv()
    spots = ((5, 10), (10, 10), (7, 7), (11, 6), (4, 6), (8, 12))[:n]
    for x, y in spots:
        blob(c, x, y, 2.3, 2.3, tones)
    c.line(8, 4, 8, 6, draw.GREEN)
    c.rect(8, 3, 3, 1, draw.GREEN_L)
    return done(c)


def plum():
    c = cv()
    blob(c, 8, 9, 5, 5, PURPLE)
    c.line(8, 4, 9, 2, draw.WOOD)
    c.rect(9, 2, 3, 1, draw.GREEN_L)
    return done(c)


def nut():
    c = cv()
    blob(c, 8, 9, 4.2, 4.8, BROWN)
    c.rect(5, 4, 6, 3, (170, 130, 80))
    c.dot(7, 8, BROWN[2])
    return done(c)


def mushroom():
    c = cv()
    c.rect(6, 9, 4, 5, (240, 225, 190))
    blob(c, 8, 8, 6, 4.5, ((160, 90, 60), (205, 140, 90), (235, 190, 140)))
    c.rect(2, 9, 12, 2, (160, 90, 60))
    return done(c)


def root(tones):
    c = cv()
    blob(c, 8, 8, 4.5, 5.5, tones)
    c.line(8, 13, 8, 15, tones[0])
    c.rect(6, 2, 4, 3, draw.GREEN)
    c.dot(5, 2, draw.GREEN_L)
    c.dot(10, 2, draw.GREEN_L)
    return done(c)


def crystal_fruit():
    c = cv()
    blob(c, 8, 9, 5, 5, BLUE)
    for x, y in ((6, 7), (9, 6), (7, 10)):
        c.dot(x, y, (255, 255, 255))
    c.rect(7, 2, 2, 3, draw.GREEN)
    return done(c)


def leek():
    c = cv()
    c.rect(6, 8, 4, 7, (240, 245, 220))
    c.rect(6, 8, 1, 7, (255, 255, 250))
    c.rect(5, 2, 6, 7, GREENS[1])
    c.rect(5, 2, 2, 7, GREENS[2])
    c.rect(9, 3, 2, 6, GREENS[0])
    return done(c)


def spring_onion():
    c = cv()
    blob(c, 8, 11, 3.6, 3.2, WHITE)
    c.line(7, 8, 6, 2, GREENS[1])
    c.line(8, 8, 8, 1, GREENS[2])
    c.line(9, 8, 10, 2, GREENS[1])
    c.line(8, 14, 8, 15, WHITE[0])
    return done(c)


def sprinkler():
    c = cv()
    c.rect(7, 6, 2, 8, draw.STEEL)
    c.rect(7, 6, 1, 8, draw.STEEL_L)
    c.rect(4, 13, 8, 2, draw.STEEL_D)
    c.rect(5, 3, 6, 3, draw.STEEL_L)
    c.rect(5, 5, 6, 1, draw.STEEL_D)
    for x, y in ((3, 2), (12, 2), (2, 5), (13, 5)):
        c.dot(x, y, draw.BLUE_L)
    return done(c)


def stone_path():
    c = cv()
    for cx, cy, rx, ry in ((4, 4, 3.4, 3), (11, 4, 3.4, 3.4), (4, 11, 3.6, 3.4), (11, 11, 3.4, 3)):
        blob(c, cx, cy, rx, ry, GREY)
    return done(c)


def fence(town):
    return town.cell(81 % 12, 81 // 12)


def baked_potato():
    c = cv()
    c.rect(2, 11, 12, 3, draw.WOOD_L)
    blob(c, 8, 8, 5.5, 3.8, ((150, 100, 50), (200, 150, 80), (235, 200, 130)))
    c.rect(5, 6, 6, 1, (255, 240, 180))
    c.dot(7, 5, (255, 255, 255))
    return done(c)


def cauliflower():
    c = cv()
    blob(c, 4, 10, 4, 3, GREENS)
    blob(c, 12, 10, 4, 3, GREENS)
    blob(c, 8, 12, 5, 3, GREENS)
    blob(c, 8, 7, 5.2, 4.6, WHITE)
    for x, y in ((6, 6), (10, 5), (8, 9)):
        c.dot(x, y, (200, 190, 175))
    return done(c)


def wheat():
    c = cv()
    for x, lean in ((4, -1), (8, 0), (12, 1)):
        c.line(x, 14, x + lean * 2, 5, STRAW[0])
        for k in range(4):
            c.dot(x + lean * 2 - 1, 4 + k * 2, STRAW[1])
            c.dot(x + lean * 2 + 1, 5 + k * 2, STRAW[2])
        c.dot(x + lean * 2, 2, STRAW[2])
        c.dot(x + lean * 2, 3, STRAW[1])
    c.rect(3, 12, 10, 2, (170, 100, 50))
    return done(c)


def seeds_of(crop):
    f = lambda src, _t: packet(src, crop)
    f.note = f'drawn seed packet, band in the colour of the `{crop}` harvest picture'
    return f


def produce(crop):
    f = lambda src, _t: crops.produce_icon(src, crop)
    f.note = f'CC0 crop `{crop}` harvest picture (see the crop table)'
    return f


def static(fn, *a):
    f = lambda src, _t: fn(*a)
    f.note = f'drawn here: `icons.{fn.__name__}`'
    return f


def tiny_town_fence(src, town):
    return fence(town)


tiny_town_fence.note = 'Kenney Tiny Town tile 81 (horizontal fence)'

# sheet index -> picture(src, town sheet)
OBJECTS = {
    # crop produce
    24: produce('parsnip'), 264: produce('radish'), 190: static(cauliflower), 192: produce('potato'), 188: produce('green_bean'),
    250: produce('kale'), 248: produce('garlic'), 400: produce('strawberry'), 256: produce('tomato'), 258: produce('blueberry'),
    254: produce('melon'), 260: produce('hot_pepper'), 270: produce('corn'), 262: static(wheat), 421: produce('sunflower'),
    276: produce('pumpkin'), 282: produce('cranberries'), 272: produce('eggplant'), 280: produce('yam'), 278: produce('bok_choy'), 398: produce('grape'),
    # seed packets
    472: seeds_of('parsnip'), 473: seeds_of('green_bean'), 474: seeds_of('cauliflower'), 475: seeds_of('potato'), 476: seeds_of('garlic'),
    477: seeds_of('kale'), 745: seeds_of('strawberry'), 480: seeds_of('tomato'), 481: seeds_of('blueberry'), 479: seeds_of('melon'),
    482: seeds_of('hot_pepper'), 487: seeds_of('corn'), 483: seeds_of('wheat'), 431: seeds_of('sunflower'), 490: seeds_of('pumpkin'),
    493: seeds_of('cranberries'), 488: seeds_of('eggplant'), 492: seeds_of('yam'), 491: seeds_of('bok_choy'), 301: seeds_of('grape'),
    484: seeds_of('radish'),
    # forage
    18: static(flower, YELLOW), 22: static(flower, YELLOW, draw.WOOD_D), 402: static(flower, PINK), 20: static(leek), 399: static(spring_onion),
    396: static(berries, RED, 4), 410: static(berries, PURPLE, 5), 408: static(nut), 406: static(plum), 404: static(mushroom),
    412: static(root, ((120, 50, 60), (170, 90, 100), (215, 140, 150))), 414: static(crystal_fruit), 416: static(root, ((150, 110, 60), (210, 170, 110), (245, 220, 170))),
    # animal products and food
    176: static(egg), 174: static(egg, True, True), 184: static(bottle), 186: static(bottle, True), 178: static(hay),
    196: static(bowl, GREENS, ((5, 5), (9, 4), (7, 6), (11, 5))), 208: static(baked_potato), 236: static(bowl, ORANGE, ((6, 5), (10, 5))),
    # resources
    388: static(log), 390: static(rock, GREY), 382: static(rock, COAL, [(5, 8, (150, 150, 170)), (9, 9, (150, 150, 170))]),
    378: static(rock, GREY, [(5, 9, COPPER[1]), (9, 8, COPPER[2]), (8, 11, COPPER[1])]),
    380: static(rock, GREY, [(5, 9, (210, 120, 100)), (9, 8, (170, 190, 235)), (8, 11, (210, 120, 100))]),
    334: static(bar, COPPER), 335: static(bar, GREY), 771: static(grass_tuft),
    309: static(acorn), 310: static(samara), 311: static(pinecone),
    # sea
    372: static(shell, WHITE), 718: static(shell, ((170, 120, 80), (220, 175, 125), (245, 220, 175))), 393: static(coral), 397: static(urchin),
    392: static(nautilus), 394: static(rainbow_shell),
    # nodes and placed pieces
    290: static(rock, GREY, [(4, 8, (110, 130, 190)), (9, 7, (170, 190, 235)), (10, 11, (110, 130, 190))]),
    751: static(rock, ((110, 60, 40), (170, 100, 60), (214, 150, 100)), [(5, 8, COPPER[2]), (9, 7, COPPER[1]), (10, 11, COPPER[2])]),
    343: static(rock, ((120, 125, 140), (190, 195, 205), (230, 232, 240))), 450: static(rock, BROWN),
    294: static(twig, False), 295: static(twig, True), 313: static(weed, 0), 314: static(weed, 1), 315: static(weed, 2),
    322: tiny_town_fence, 407: static(stone_path), 599: static(sprinkler),
}


def build(src, size):
    from pngio import Image
    town = src.sheet('town')
    sheet = Image(*size('sprites/items.png'))
    for index, fn in OBJECTS.items():
        sheet.paste(fn(src, town), index % 24 * 16, index // 24 * 16)
    return sheet
