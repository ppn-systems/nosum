"""Package 10: sprites/npcs/{key}.png (16 x 32 world sprite, two idle frames side by side, 32 x 32 file) and sprites/npcs/{key}_portrait.png (32 x 32 portrait),
for the seven merchants of the Town (docs/adr/ADR-043 D5). Drawn here, no pack involved.

Every merchant is the same build (a 16 x 32 frame whose bottom edge is the feet, the farmer's proportions) with its own outfit, so a player knows who they
are talking to from across the street: straw hat and seed apron, tape measure and beret, waistcoat and glasses, leather apron and hammer, overalls and
pencil, plumed hat and coin purse, visor and scroll. Every colour is a three-tone ramp (shadow, base, light) lit from the top left, and the whole picture gets
a 1 px outline in the palette's dark brown (ADR-040 D1), as the animals and the farmer do.

Idle: frame 0 stands; frame 1 lets the head and shoulders drop one pixel (a breath), the feet do not move. The portrait is the head and shoulders at twice
the size, with the same outfit pieces.
"""
import draw
from draw import Canvas
from pngio import Image

OUT = draw.OUT

# Ramps are (shadow, base, light).
SKIN_FAIR = ((206, 136, 112), (232, 178, 140), (250, 214, 176))
SKIN_TAN = ((176, 108, 84), (210, 150, 116), (238, 190, 152))
SKIN_BROWN = ((140, 84, 64), (180, 120, 90), (216, 160, 122))
HAIR_BLACK = ((20, 16, 28), (46, 38, 58), (88, 76, 104))
HAIR_BROWN = ((90, 52, 36), (132, 80, 50), (178, 118, 72))
HAIR_GREY = ((128, 128, 140), (178, 178, 190), (226, 226, 234))
HAIR_RED = ((140, 56, 36), (190, 90, 46), (232, 140, 70))
GREEN = ((38, 92, 66), (62, 137, 72), (99, 199, 77))
CREAM = ((196, 180, 150), (236, 224, 190), (255, 250, 226))
WHITE = ((170, 170, 190), (226, 226, 238), (255, 255, 255))
RED = ((150, 36, 40), (210, 62, 56), (244, 110, 90))
LEATHER = ((90, 50, 36), (140, 82, 50), (188, 120, 74))
BLUE = ((24, 70, 130), (40, 110, 180), (96, 170, 230))
GOLD = ((190, 120, 30), (244, 180, 52), (255, 226, 120))
PURPLE = ((84, 50, 120), (130, 84, 170), (180, 138, 214))
TEAL = ((22, 84, 96), (36, 130, 140), (90, 190, 190))
STEEL = ((82, 96, 124), (140, 156, 181), (192, 203, 220))
SLATE = ((52, 58, 78), (86, 96, 120), (130, 142, 166))
ROSE = ((140, 40, 80), (200, 70, 120), (240, 130, 170))
KHAKI = ((130, 104, 60), (180, 152, 96), (218, 196, 140))
BROWN = ((70, 44, 36), (110, 70, 48), (150, 104, 70))
ORANGE = ((170, 70, 30), (228, 120, 44), (254, 176, 80))
EYE = (46, 28, 40)
CHEEK = (240, 160, 150)

FRAME_W, FRAME_H = 16, 32
PORTRAIT = 32


def shade(c, x, y, w, h, tones, light=True, dark=True):
    """A rectangle in three tones: base, a light top-left edge, a dark bottom-right edge."""
    shadow, base, high = tones
    c.rect(x, y, w, h, base)
    if light and w > 2 and h > 2:
        c.rect(x, y, 1, h - 1, high)
        c.rect(x + 1, y, w - 2, 1, high)
    if dark and w > 2 and h > 2:
        c.rect(x + w - 1, y + 1, 1, h - 1, shadow)
        c.rect(x + 1, y + h - 1, w - 2, 1, shadow)


def ball(c, cx, cy, rx, ry, tones):
    """An ellipse in three tones, lit from the top left (trees.blob without the leaf texture)."""
    shadow, base, high = tones
    for y in range(c.height):
        for x in range(c.width):
            if ((x - cx + 0.5) / rx) ** 2 + ((y - cy + 0.5) / ry) ** 2 <= 1:
                t = -0.7 * (x - cx) / rx - 0.7 * (y - cy) / ry
                c.dot(x, y, high if t > 0.42 else shadow if t < -0.42 else base)


# ---- the seven merchants ---------------------------------------------------------------------------------------------------------------

SPECS = {
    'seed_keeper': dict(skin=SKIN_FAIR, hair=HAIR_BROWN, hair_style='braid', hat='straw', shirt=GREEN, sleeves=GREEN, pants=BROWN, apron=CREAM, apron_pocket=GREEN, held='seeds'),
    'tailor': dict(skin=SKIN_TAN, hair=HAIR_BLACK, hair_style='bun', hat='beret', shirt=ROSE, sleeves=ROSE, pants=ROSE, skirt=True, extra='tape'),
    'furnisher': dict(skin=SKIN_FAIR, hair=HAIR_GREY, hair_style='short', hat=None, shirt=WHITE, sleeves=WHITE, pants=SLATE, vest=BROWN, glasses=True, extra='bowtie'),
    'blacksmith': dict(skin=SKIN_BROWN, hair=HAIR_BLACK, hair_style='bald', hat=None, shirt=RED, sleeves=None, pants=SLATE, apron=LEATHER, beard=HAIR_BLACK, held='hammer'),
    'carpenter': dict(skin=SKIN_TAN, hair=HAIR_BROWN, hair_style='short', hat='cap', shirt=GOLD, sleeves=GOLD, pants=BLUE, overalls=BLUE, mustache=HAIR_BROWN, extra='pencil', held='plank'),
    'trader': dict(skin=SKIN_TAN, hair=HAIR_BLACK, hair_style='short', hat='plume', shirt=TEAL, sleeves=TEAL, pants=PURPLE, coat=PURPLE, extra='purse'),
    'board_keeper': dict(skin=SKIN_FAIR, hair=HAIR_GREY, hair_style='bun', hat='visor', shirt=STEEL, sleeves=STEEL, pants=SLATE, shawl=BLUE, glasses=True, held='scroll'),
}
ORDER = ['seed_keeper', 'tailor', 'furnisher', 'blacksmith', 'carpenter', 'trader', 'board_keeper']


# ---- world sprite ----------------------------------------------------------------------------------------------------------------------

def _hat(c, kind, b):
    if kind == 'straw':
        shade(c, 4, 2 + b, 8, 3, GOLD)                                  # crown
        shade(c, 1, 4 + b, 14, 2, GOLD)                                 # brim
        c.rect(4, 4 + b, 8, 1, RED[1])                                  # band
        c.rect(1, 5 + b, 14, 1, GOLD[0])
    elif kind == 'beret':
        ball(c, 8, 4 + b, 6, 2.6, RED)
        c.dot(8, 1 + b, RED[0])
    elif kind == 'cap':
        shade(c, 3, 3 + b, 10, 3, ORANGE)
        shade(c, 8, 5 + b, 7, 1, ORANGE)                                # peak to the right
        c.dot(3, 6 + b, ORANGE[0])
    elif kind == 'plume':
        shade(c, 4, 2 + b, 8, 3, TEAL)                                  # crown
        shade(c, 1, 4 + b, 14, 2, TEAL)                                 # brim
        c.rect(4, 4 + b, 8, 1, GOLD[1])
        c.line(11, 2 + b, 14, 0 + b, RED[1])                            # feather
        c.dot(14, 1 + b, RED[2])
    elif kind == 'visor':
        shade(c, 3, 4 + b, 10, 2, GREEN)
        shade(c, 2, 6 + b, 12, 1, GREEN)
        c.rect(3, 4 + b, 10, 1, GREEN[2])


def _hair(c, spec, b):
    style, tones = spec['hair_style'], spec['hair']
    hatted = spec.get('hat') is not None
    if style == 'bald':
        c.rect(4, 5 + b, 8, 1, tones[0])                                # a dark stubble line above the brow
        return
    if not hatted:
        shade(c, 3, 3 + b, 10, 4, tones)
    else:
        c.rect(3, 6 + b, 1, 4, tones[1])
        c.rect(12, 6 + b, 1, 4, tones[0])
    if style == 'short':
        c.rect(3, 7 + b, 1, 2, tones[1])
        c.rect(12, 7 + b, 1, 2, tones[0])
    elif style == 'braid':
        c.rect(12, 8 + b, 2, 9, tones[1])                               # a braid over the right shoulder
        c.rect(13, 8 + b, 1, 9, tones[0])
        c.dot(12, 17 + b, RED[1])
        c.dot(13, 17 + b, RED[1])
    elif style == 'bun':
        ball(c, 8, 2 + b, 2.4, 2, tones)


def _head(c, spec, b):
    skin = spec['skin']
    # head: 8 x 9, lit from the top left
    for y in range(5 + b, 14 + b):
        for x in range(4, 12):
            c.dot(x, y, skin[1])
    c.rect(4, 5 + b, 1, 7, skin[2])
    c.rect(5, 5 + b, 6, 1, skin[2])
    c.rect(11, 6 + b, 1, 8, skin[0])
    c.rect(5, 13 + b, 6, 1, skin[0])
    c.rect(7, 14 + b, 2, 1, skin[0])                                    # neck
    c.rect(5, 9 + b, 2, 2, EYE)
    c.rect(9, 9 + b, 2, 2, EYE)
    c.dot(5, 9 + b, (255, 255, 255))
    c.dot(9, 9 + b, (255, 255, 255))
    c.dot(4, 11 + b, CHEEK)
    c.dot(11, 11 + b, CHEEK)
    c.rect(7, 12 + b, 2, 1, skin[0])                                    # mouth
    c.dot(3, 9 + b, skin[1])                                            # ears
    c.dot(3, 10 + b, skin[1])
    c.dot(12, 9 + b, skin[0])
    c.dot(12, 10 + b, skin[0])
    if spec.get('glasses'):
        c.rect(4, 8 + b, 4, 1, STEEL[0])
        c.rect(8, 8 + b, 4, 1, STEEL[0])
        c.rect(4, 11 + b, 4, 1, STEEL[0])
        c.rect(8, 11 + b, 4, 1, STEEL[0])
        c.rect(4, 8 + b, 1, 4, STEEL[0])
        c.rect(11, 8 + b, 1, 4, STEEL[0])
        c.rect(7, 8 + b, 2, 1, STEEL[0])
    if spec.get('beard'):
        shade(c, 4, 11 + b, 8, 3, spec['beard'])
        c.rect(7, 12 + b, 2, 1, skin[0])
    if spec.get('mustache'):
        c.rect(5, 11 + b, 6, 1, spec['mustache'][0])


def _torso(c, spec, b):
    shirt = spec['shirt']
    top = 15 + b
    # shoulders and body (10 wide), then the arms on both sides
    shade(c, 3, top, 10, 10 - b, shirt)
    sleeves = spec.get('sleeves')
    for x, side in ((1, 0), (13, 1)):
        if sleeves is not None:
            shade(c, x, top + 1, 2, 7, sleeves)
            c.rect(x, top + 8 + (0 if b == 0 else 0), 2, 2, spec['skin'][1])
        else:
            c.rect(x, top + 1, 2, 3, spec['skin'][side * 2 if False else 1])    # bare arms
            c.rect(x, top + 4, 2, 5, spec['skin'][1])
            c.rect(x + (1 if side else 0), top + 2, 1, 7, spec['skin'][0] if side else spec['skin'][2])
    # outfit pieces over the shirt
    if spec.get('vest'):
        shade(c, 3, top, 3, 10 - b, spec['vest'])
        shade(c, 10, top, 3, 10 - b, spec['vest'])
        c.dot(5, top + 4, GOLD[2])
        c.dot(5, top + 7, GOLD[2])
    if spec.get('overalls'):
        shade(c, 4, top + 3, 8, 7 - b, spec['overalls'])
        c.rect(4, top, 2, 4, spec['overalls'][1])                       # straps
        c.rect(10, top, 2, 4, spec['overalls'][1])
        c.dot(5, top + 4, GOLD[2])
        c.dot(10, top + 4, GOLD[2])
    if spec.get('apron'):
        apron = spec['apron']
        shade(c, 4, top + 2, 8, 8 - b, apron)
        c.rect(5, top, 1, 3, apron[1])                                  # neck strap
        c.rect(10, top, 1, 3, apron[1])
        if spec.get('apron_pocket'):
            shade(c, 5, top + 6, 6, 3 - b if b else 3, spec['apron_pocket'])
        else:
            c.rect(6, top + 5, 4, 2, apron[0])                          # a smudge of soot on the leather
    if spec.get('coat'):
        coat = spec['coat']
        shade(c, 3, top, 4, 10 - b, coat)
        shade(c, 9, top, 4, 10 - b, coat)
        c.dot(6, top + 3, GOLD[1])
        c.dot(6, top + 6, GOLD[1])
        c.dot(9, top + 3, GOLD[1])
        c.dot(9, top + 6, GOLD[1])
    if spec.get('shawl'):
        shawl = spec['shawl']
        shade(c, 2, top, 12, 4, shawl)
        c.rect(7, top + 3, 2, 4, shawl[1])
        c.rect(6, top + 6, 4, 1, shawl[0])
    extra = spec.get('extra')
    if extra == 'tape':
        c.rect(5, top - 1, 1, 6, GOLD[1])                               # a tape measure round the neck
        c.rect(10, top - 1, 1, 6, GOLD[1])
        c.dot(5, top + 1, GOLD[0])
        c.dot(10, top + 3, GOLD[0])
    elif extra == 'bowtie':
        c.rect(6, top, 4, 1, RED[1])
        c.dot(7, top, RED[2])
    elif extra == 'pencil':
        c.rect(12, 7 + b, 1, 3, GOLD[1])                                # behind the ear
        c.dot(12, 10 + b, STEEL[0])
    elif extra == 'purse':
        shade(c, 10, top + 7 - b, 3, 3, GOLD)                           # coin purse on the belt
        c.dot(11, top + 6 - b, BROWN[1])


def _held(c, spec, b):
    held = spec.get('held')
    top = 15 + b
    if held == 'hammer':
        c.rect(14, top + 4, 1, 7, LEATHER[1])                           # handle in the right hand
        shade(c, 12, top + 2, 4, 3, STEEL)
    elif held == 'seeds':
        shade(c, 0, top + 7, 3, 4, CREAM)                               # a seed packet in the left hand
        c.rect(1, top + 8, 1, 2, GREEN[1])
    elif held == 'plank':
        shade(c, 13, top + 5, 3, 10, KHAKI)                             # a plank held at the side
        c.rect(14, top + 7, 1, 1, KHAKI[0])
    elif held == 'scroll':
        shade(c, 12, top + 6, 4, 3, CREAM)
        c.rect(12, top + 6, 1, 3, RED[1])


def _legs(c, spec):
    if spec.get('skirt'):
        shade(c, 3, 25, 10, 5, spec['pants'])
        c.rect(4, 29, 3, 1, spec['pants'][0])
        c.rect(5, 30, 2, 2, BROWN[1])                                   # shoes
        c.rect(9, 30, 2, 2, BROWN[1])
        return
    pants = spec['pants']
    shade(c, 4, 25, 4, 6, pants)
    shade(c, 8, 25, 4, 6, pants)
    c.rect(7, 25, 2, 1, pants[0])
    c.rect(4, 30, 4, 2, BROWN[1])                                       # shoes
    c.rect(8, 30, 4, 2, BROWN[1])
    c.rect(4, 31, 4, 1, BROWN[0])
    c.rect(8, 31, 4, 1, BROWN[0])


def sprite(key, frame):
    spec = SPECS[key]
    b = 1 if frame == 1 else 0
    c = Canvas(FRAME_W, FRAME_H)
    _legs(c, spec)
    _torso(c, spec, b)
    _held(c, spec, b)
    _head(c, spec, b)
    _hair(c, spec, b)
    if spec.get('hat'):
        _hat(c, spec['hat'], b)
    return c.outline(OUT)


# ---- portrait --------------------------------------------------------------------------------------------------------------------------

def _p_hat(c, kind):
    if kind == 'straw':
        ball(c, 16, 6, 7, 5, GOLD)
        shade(c, 3, 8, 26, 4, GOLD)
        c.rect(7, 8, 18, 2, RED[1])
        c.rect(3, 11, 26, 1, GOLD[0])
    elif kind == 'beret':
        ball(c, 15, 6, 11, 5, RED)
        c.rect(14, 0, 2, 2, RED[0])
    elif kind == 'cap':
        ball(c, 16, 7, 9, 5, ORANGE)
        shade(c, 16, 10, 14, 3, ORANGE)
    elif kind == 'plume':
        ball(c, 16, 6, 7, 5, TEAL)
        shade(c, 3, 8, 26, 4, TEAL)
        c.rect(7, 8, 18, 2, GOLD[1])
        c.line(22, 4, 29, 0, RED[1])
        c.line(22, 5, 29, 1, RED[1])
        c.dot(29, 2, RED[2])
    elif kind == 'visor':
        shade(c, 7, 7, 18, 4, GREEN)
        shade(c, 5, 11, 22, 2, GREEN)
        c.rect(7, 7, 18, 1, GREEN[2])


def portrait(key):
    spec = SPECS[key]
    skin = spec['skin']
    c = Canvas(PORTRAIT, PORTRAIT)
    # shoulders and the top of the outfit
    shade(c, 4, 24, 24, 8, spec['shirt'])
    ball(c, 16, 24, 12, 4, spec['shirt'])
    if spec.get('vest'):
        shade(c, 4, 24, 7, 8, spec['vest'])
        shade(c, 21, 24, 7, 8, spec['vest'])
    if spec.get('overalls'):
        c.rect(7, 22, 4, 10, spec['overalls'][1])
        c.rect(21, 22, 4, 10, spec['overalls'][1])
        c.rect(8, 28, 2, 2, GOLD[2])
        c.rect(22, 28, 2, 2, GOLD[2])
    if spec.get('apron'):
        shade(c, 8, 26, 16, 6, spec['apron'])
        c.rect(8, 22, 2, 5, spec['apron'][1])
        c.rect(22, 22, 2, 5, spec['apron'][1])
        if spec.get('apron_pocket'):
            shade(c, 11, 29, 10, 3, spec['apron_pocket'])
    if spec.get('coat'):
        shade(c, 4, 24, 9, 8, spec['coat'])
        shade(c, 19, 24, 9, 8, spec['coat'])
        c.rect(12, 28, 2, 2, GOLD[1])
        c.rect(18, 28, 2, 2, GOLD[1])
    if spec.get('shawl'):
        shade(c, 3, 24, 26, 8, spec['shawl'])
        c.rect(13, 28, 6, 4, spec['shawl'][1])
    # neck, head
    shade(c, 13, 20, 6, 6, skin)
    # hair behind the head
    hair = spec['hair']
    if spec['hair_style'] != 'bald':
        ball(c, 16, 12, 10.5, 10, hair)
    if spec['hair_style'] == 'bun':
        ball(c, 16, 3, 4, 3, hair)
    if spec['hair_style'] == 'braid':
        shade(c, 24, 14, 4, 14, hair)
        c.rect(24, 28, 4, 2, RED[1])
    ball(c, 16, 15, 8.5, 9.5, skin)
    c.rect(7, 14, 2, 4, skin[1])                                        # ears
    c.rect(23, 14, 2, 4, skin[0])
    if spec['hair_style'] != 'bald':
        # fringe over the forehead
        ball(c, 16, 7, 8.8, 3.6, hair)
    else:
        c.rect(10, 9, 12, 1, hair[0])
    # face
    c.rect(11, 14, 3, 3, EYE)
    c.rect(18, 14, 3, 3, EYE)
    c.dot(11, 14, (255, 255, 255))
    c.dot(18, 14, (255, 255, 255))
    c.rect(10, 12, 5, 1, hair[0] if spec['hair_style'] != 'bald' else skin[0])    # brows
    c.rect(17, 12, 5, 1, hair[0] if spec['hair_style'] != 'bald' else skin[0])
    c.rect(9, 18, 2, 1, CHEEK)
    c.rect(21, 18, 2, 1, CHEEK)
    c.rect(15, 17, 2, 2, skin[0])                                       # nose
    c.rect(13, 21, 6, 1, skin[0] if not spec.get('beard') else spec['beard'][2])           # mouth
    if spec.get('glasses'):
        for x in (9, 17):
            c.rect(x, 13, 6, 1, STEEL[0])
            c.rect(x, 17, 6, 1, STEEL[0])
            c.rect(x, 13, 1, 5, STEEL[0])
            c.rect(x + 5, 13, 1, 5, STEEL[0])
        c.rect(15, 14, 2, 1, STEEL[0])
    if spec.get('beard'):
        shade(c, 9, 19, 14, 6, spec['beard'])
        c.rect(13, 21, 6, 1, skin[0])
    if spec.get('mustache'):
        c.rect(11, 19, 10, 2, spec['mustache'][0])
        c.rect(11, 19, 10, 1, spec['mustache'][1])
    if spec.get('hat'):
        _p_hat(c, spec['hat'])
    if spec.get('extra') == 'tape':
        c.rect(10, 24, 2, 8, GOLD[1])
        c.rect(20, 24, 2, 8, GOLD[1])
    elif spec.get('extra') == 'bowtie':
        c.rect(12, 25, 8, 3, RED[1])
        c.rect(15, 25, 2, 3, RED[0])
    elif spec.get('extra') == 'pencil':
        c.rect(24, 12, 2, 7, GOLD[1])
        c.rect(24, 19, 2, 1, STEEL[0])
    return c.outline(OUT)


# ---- files -----------------------------------------------------------------------------------------------------------------------------

def sheet(key):
    """The world sheet of one merchant: frame 0 at x 0, frame 1 at x 16."""
    img = Image(2 * FRAME_W, FRAME_H)
    img.paste(sprite(key, 0), 0, 0)
    img.paste(sprite(key, 1), FRAME_W, 0)
    return img


def build(size=None):
    """{path under assets/: Image} for the seven world sheets and the seven portraits."""
    out = {}
    for key in ORDER:
        out[f'sprites/npcs/{key}.png'] = sheet(key)
        out[f'sprites/npcs/{key}_portrait.png'] = portrait(key)
    return out


NAMES = {
    'seed_keeper': 'Seed shopkeeper (straw hat, green top, cream apron with a seed pocket)',
    'tailor': 'Tailor (beret, rose dress, tape measure round the neck)',
    'furnisher': 'Furniture seller (grey hair, glasses, brown waistcoat, red bow tie)',
    'blacksmith': 'Blacksmith (bald, beard, bare arms, leather apron, hammer)',
    'carpenter': 'Carpenter (orange cap, moustache, blue overalls, pencil behind the ear, plank)',
    'trader': 'Trader (plumed hat, teal top, purple coat, coin purse)',
    'board_keeper': 'Board keeper (green visor, glasses, blue shawl, scroll)',
}


def provenance_entries(nosum):
    """{path: (package, [sources], text)} for provenance.py."""
    entries = {}
    for key in ORDER:
        entries[f'sprites/npcs/{key}.png'] = ('10 Merchants', [nosum], f'{NAMES[key]}; 16 x 32 idle frames side by side, frame 1 breathes one pixel lower (`npcs.sprite`)')
        entries[f'sprites/npcs/{key}_portrait.png'] = ('10 Merchants', [nosum], f'Portrait of the same merchant, 32 x 32, head and shoulders (`npcs.portrait`)')
    return entries
