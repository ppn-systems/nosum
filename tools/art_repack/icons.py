"""Package 3 and 6: sprites/items.png, the 16 x 16 object sheet (items, forage, resource nodes).

Crop produce comes from the CC0 crop pictures (crops.py); a few pictures come from Kenney Tiny Town; every other icon is drawn here with
shaded blobs and pixel dots in the Kenney palette and a 1 px outline (ADR-040 D1). OBJECTS maps the compact sheet index the game uses
(24 columns, numbered in item catalogue order from 1; 0 is no picture) to a function that returns the picture.
"""
import colorsys

import draw
from draw import Canvas
from trees import blob
import crops
from pngio import Image, recolor

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


PLAIN_BAG = 74       # Tiny Farm's seed bag without an emblem: the silhouette every packet is drawn on
BAG_LIGHT, BAG_MID, BAG_DARK = (254, 201, 156), (234, 165, 108), (207, 130, 84)    # its three paper tones


def tile_of(sheet, index):
    return sheet.cell(index % 12, index // 12)


def mini(pic):
    """A 16 x 16 picture halved to 8 x 8: per 2 x 2 block the most saturated opaque pixel (a block of mostly outline keeps the outline)."""
    out = Image(8, 8)
    for y in range(8):
        for x in range(8):
            block = [pic.px[2 * y + j][2 * x + i] for j in (0, 1) for i in (0, 1)]
            solid = [p for p in block if p[3]]
            if len(solid) < 2:
                continue
            body = [p for p in solid if sum(p[:3]) > 190]
            out.px[y][x] = max(body, key=lambda p: max(p[:3]) - min(p[:3])) if body else min(solid, key=lambda p: sum(p[:3]))
    return out


def crop_colour(pic):
    """The most common strongly coloured tone of a harvest picture, the colour of its seed packet."""
    counts = {}
    for row in pic.px:
        for p in row:
            if p[3] and max(p[:3]) - min(p[:3]) > 50 and sum(p[:3]) > 220:
                counts[p[:3]] = counts.get(p[:3], 0) + 1
    return max(counts, key=lambda c: (counts[c], c)) if counts else (200, 200, 190)


def tones(rgb):
    """Light, middle and dark paper tones of a packet of the given crop colour."""
    h, s, v = colorsys.rgb_to_hsv(*(c / 255 for c in rgb))
    s = max(s, 0.35)
    return [tuple(round(c * 255) for c in colorsys.hsv_to_rgb(h, min(1.0, s * k), val)) for k, val in ((0.7, 1.0), (1.0, 0.9), (1.0, 0.6))]


def packet(src, crop):
    """A seed packet on Tiny Farm's bag: the paper takes the crop's colour and the front carries a half-size picture of the harvest."""
    pic = crops.produce_icon(src, crop)
    swap = dict(zip((BAG_LIGHT, BAG_MID, BAG_DARK), tones(crop_colour(pic))))
    bag = tile_of(src.sheet('farm'), PLAIN_BAG).map_pixels(lambda p: swap.get(p[:3], p[:3]) + (p[3],))
    emblem = mini(pic)
    for y in range(16):                                 # a cream label under the picture so it reads on any paper colour
        for x in range(16):
            if 4 <= x < 12 and 6 <= y < 13 and bag.px[y][x][3] and bag.px[y][x][:3] in swap.values():
                bag.px[y][x] = (252, 240, 214, 255)
    bag.paste(emblem, 4, 5)
    return bag


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
    f.note = f'Tiny Farm bag (tile {PLAIN_BAG}) in the colour of the `{crop}` harvest picture, with a half-size picture of it'
    return f


def produce(crop):
    f = lambda src, _t: crops.produce_icon(src, crop)
    f.note = f'Tiny Farm sunflower (tile {crops.SUNFLOWER_PRODUCE})' if crop == 'sunflower' else f'Tiny Farm `{crop}` harvest picture (see the crop table)' if crop in crops.FARM else f'CC0 crop `{crop}` harvest picture (see the crop table)'
    return f


def static(fn, *a):
    f = lambda src, _t: fn(*a)
    f.note = f'drawn here: `icons.{fn.__name__}`'
    return f


def farm_tile(index, recipe=None):
    """A Tiny Farm tile as an object-sheet picture."""
    def fn(src, _town):
        pic = tile_of(src.sheet('farm'), index)
        return pic.map_pixels(recipe) if recipe else pic
    fn.note = f'Tiny Farm tile {index}' + (' recoloured' if recipe else '')
    return fn


def library_tile(sheet, column, row, what):
    """A cell of a library pack (sources.SHEETS) as an object-sheet picture."""
    def fn(src, _town):
        return src.sheet(sheet).cell(column, row)
    fn.note = what
    return fn


def tiny_town_fence(src, town):
    return fence(town)


tiny_town_fence.note = 'Kenney Tiny Town tile 81 (horizontal fence)'

def slime_gel_icon(src, _town):
    return src.sheet('slime_green').cell(0, 0).crop(8, 4, 16, 16)


slime_gel_icon.note = '16x16 Animated Slimes (`Slime_Small_Green.png` cell 0, 0 cropped at +8, +4)'

# sheet index -> picture(src, town sheet)
OBJECTS = {
    # crop produce
    25: produce('parsnip'), 26: produce('radish'), 27: static(cauliflower), 28: produce('potato'), 29: produce('green_bean'),
    30: produce('kale'), 31: produce('garlic'), 32: produce('strawberry'), 33: produce('tomato'), 34: produce('blueberry'),
    35: produce('melon'), 36: produce('hot_pepper'), 37: produce('corn'), 38: produce('wheat'), 39: produce('sunflower'),
    40: produce('pumpkin'), 41: produce('cranberries'), 42: produce('eggplant'), 43: produce('yam'), 44: produce('bok_choy'), 45: produce('grape'),
    # seed packets
    1: seeds_of('parsnip'), 5: seeds_of('green_bean'), 3: seeds_of('cauliflower'), 4: seeds_of('potato'), 7: seeds_of('garlic'),
    6: seeds_of('kale'), 8: seeds_of('strawberry'), 9: seeds_of('tomato'), 10: seeds_of('blueberry'), 11: seeds_of('melon'),
    12: seeds_of('hot_pepper'), 13: seeds_of('corn'), 14: seeds_of('wheat'), 15: seeds_of('sunflower'), 16: seeds_of('pumpkin'),
    17: seeds_of('cranberries'), 18: seeds_of('eggplant'), 19: seeds_of('yam'), 20: seeds_of('bok_choy'), 21: seeds_of('grape'),
    2: seeds_of('radish'),
    # forage
    68: static(flower, YELLOW), 67: static(flower, YELLOW, draw.WOOD_D), 70: static(flower, PINK), 66: library_tile('dawn_food', 3, 3, 'DawnLike Food (3, 3), the leek'), 65: static(spring_onion),
    69: library_tile('dawn_food', 7, 2, 'DawnLike Food (7, 2), red berries'), 71: static(berries, PURPLE, 5), 72: library_tile('dawn_food', 3, 5, 'DawnLike Food (3, 5), the hazelnut'), 73: static(plum), 74: library_tile('dawn_food', 1, 0, 'DawnLike Food (1, 0), the pink-capped mushroom'),
    75: static(root, ((120, 50, 60), (170, 90, 100), (215, 140, 150))), 76: static(crystal_fruit), 77: static(root, ((150, 110, 60), (210, 170, 110), (245, 220, 170))),
    # animal products and food
    57: static(egg), 58: farm_tile(125), 59: farm_tile(123), 60: farm_tile(124), 61: farm_tile(96),
    62: static(bowl, GREENS, ((5, 5), (9, 4), (7, 6), (11, 5))), 63: static(baked_potato), 64: library_tile('dawn_food', 4, 5, 'DawnLike Food (4, 5), the bowl of soup'),
    # resources
    46: static(log), 47: farm_tile(89), 49: static(rock, COAL, [(5, 8, (150, 150, 170)), (9, 9, (150, 150, 170))]),
    50: library_tile('dawn_rock', 3, 0, 'DawnLike Rock (3, 0), the copper chunk'),
    51: library_tile('dawn_rock', 0, 1, 'DawnLike Rock (0, 1), the steel-blue ore chunk'),
    52: static(bar, COPPER), 53: static(bar, GREY), 48: static(grass_tuft),
    22: static(acorn), 23: static(samara), 24: static(pinecone),
    # sea
    78: static(shell, WHITE), 79: static(shell, ((170, 120, 80), (220, 175, 125), (245, 220, 175))), 80: static(coral), 81: static(urchin),
    82: static(nautilus), 83: static(rainbow_shell),
    # nodes and placed pieces
    92: static(rock, GREY, [(4, 8, (110, 130, 190)), (9, 7, (170, 190, 235)), (10, 11, (110, 130, 190))]),
    91: library_tile('dawn_rock', 2, 1, 'DawnLike Rock (2, 1), the copper boulder'),
    89: static(rock, ((120, 125, 140), (190, 195, 205), (230, 232, 240))), 90: static(rock, BROWN),
    84: static(twig, False), 85: static(twig, True), 86: static(weed, 0), 87: static(weed, 1), 88: static(weed, 2),
    54: tiny_town_fence, 55: static(stone_path), 56: static(sprinkler),
    # potions, monster drops, and RPG relics (ADR-045)
    93: library_tile('rpg_potions', 3, 2, 'Assorted RPG Icons `potions.png` (3, 2), crimson health potion'),
    94: library_tile('rpg_potions', 11, 2, 'Assorted RPG Icons `potions.png` (11, 2), sapphire mana potion'),
    95: library_tile('rpg_potions', 5, 4, 'Assorted RPG Icons `potions.png` (5, 4), golden elixir'),
    96: slime_gel_icon,
    97: library_tile('tiny16_dungeon', 10, 9, 'Tiny 16 `dungeon.png` (10, 9), bat wing'),
    98: library_tile('trash_mobs', 6, 0, 'Fantasy RPG Trash Mobs (6, 0), spore cap'),
    99: library_tile('rpg_items', 6, 5, '16x16 RPG items (6, 5), goblin trophy'),
    100: library_tile('roguelike_caves', 1, 2, 'Kenney Roguelike Caves & Dungeons (1, 2), monster bone'),
    101: library_tile('dungeon_diamond', 0, 0, '16x16 Dungeon Tiles `diamond_1.png`, golem core'),
    102: library_tile('puny_dungeon', 16, 10, 'Puny Dungeon `punyworld-dungeon-tileset.png` (16, 10), mana crystal'),
    103: library_tile('gold_treasure', 1, 2, 'Gold treasure icons `sheet.png` (1, 2), treasure gem'),
    104: library_tile('rpg_books', 3, 0, 'Assorted RPG Icons `books.png` (3, 0), spell tome'),
    105: library_tile('six_color_dungeon', 19, 2, '6 Color Dungeon (19, 2), dungeon skull'),
    106: library_tile('roguelike_rpg', 44, 15, 'Kenney Roguelike/RPG Pack (44, 15), ancient scroll'),
    107: library_tile('roguelike_indoors', 19, 0, 'Kenney Roguelike Indoors (19, 0), candelabra'),
    108: library_tile('animal_bee', 0, 0, 'Random animals `bee.png`, honey bee'),
    109: library_tile('pixel_animals', 0, 0, 'Pixel Animals 16x16 (0, 0), pocket cat'),
    110: library_tile('rpg_items', 2, 4, '16x16 RPG items (2, 4), runestone tablet'),
    111: library_tile('dawn_armor', 2, 7, 'DawnLike Armor (2, 7), ranger tunic'),
    112: library_tile('one_bit', 39, 10, 'Kenney 1-Bit Pack (39, 10), heart emblem'),
    113: library_tile('gold_treasure', 0, 2, 'Gold treasure icons (0, 2), boss crown'),
    114: library_tile('rpg_items', 1, 4, '16x16 RPG items (1, 4), golden skeleton key'),
    115: library_tile('trash_mobs', 12, 0, 'Fantasy RPG Trash Mobs (12, 0), blazing colossus core'),
    116: library_tile('dawn_armor', 0, 6, 'DawnLike Armor (0, 6), leather armor'),
    117: library_tile('dawn_armor', 2, 6, 'DawnLike Armor (2, 6), iron chestplate'),
    118: library_tile('dawn_boot', 1, 0, 'DawnLike Boot (1, 0), iron greaves'),
    119: library_tile('dawn_hat', 1, 0, 'DawnLike Hat (1, 0), iron helmet'),
}


COLUMNS = 24


def build(src, size):
    from pngio import Image
    town = src.sheet('town')
    sheet = Image(COLUMNS * 16, (max(OBJECTS) // COLUMNS + 1) * 16)
    for index, fn in OBJECTS.items():
        sheet.paste(fn(src, town), index % COLUMNS * 16, index // COLUMNS * 16)
    return sheet
