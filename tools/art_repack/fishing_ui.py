"""The fishing visuals (docs/adr/ADR-078 polish): assets/ui/fishing_bobber.png and assets/ui/fish_bite.png.

No CC0 or CC-BY pack in art/source has a bobber or a bite mark (DawnLike Tool, the nearest fishing picture, is the rod, and its sheet has no float),
so both pictures are drawn here with draw.Canvas in the Kenney palette and get the same 1 px dark outline as the other drawn pictures.

Bobber (ui/fishing_bobber.png, 16 x 16): one classic float in cell (0, 0): a red top half, a white bottom half, a thin grey stem on top,
centred on the cell (x 3-12, y 1-14 with the outline). The water ripple is left out (the splash uses the code's own line).

Bite bubble (ui/fish_bite.png, 24 x 24): the speech bubble of ui/emote_bubble.png (pets_emotes.bubble, so the same white box, 1 px outline and
bottom tail) with a dark red exclamation mark at its centre: a bold 2 px wide, 5 px tall mark (x 11-12, y 8-12, the stroke width of the emote's exclamation mark), centred on the bubble.
"""
from draw import Canvas, RED, STEEL_D
import pets_emotes

FLOAT_RED = RED
FLOAT_WHITE = (255, 255, 255)
FLOAT_SHINE = (255, 150, 140)     # a glint on the red top, as on the emote heart
MARK = (150, 24, 36)              # dark red exclamation mark

BOBBER = [                        # the float's body, 8 x 8 (x 4-11, y 6-13)
    "..RRRR..",
    ".RRRRRRR",
    "RRRRRRRR",
    "RRRRRRRR",
    "WWWWWWWW",
    "WWWWWWWW",
    ".WWWWWW.",
    "..WWWW..",
]

MARK_ROWS = [                     # the exclamation mark: a 2 x 5 stroke (x 11-12, y 8-12) and a gap for the dot
    "#",
    "#",
    "#",
    ".",
    "#",
]


def bobber():
    """16 x 16: the float in cell (0, 0)."""
    c = Canvas(16, 16)
    palette = {'R': FLOAT_RED, 'W': FLOAT_WHITE}
    c.art(BOBBER, palette, 4, 6)
    c.dot(6, 8, FLOAT_SHINE)                       # glint on the red half
    c.line(7, 2, 7, 5, STEEL_D)                    # the thin stem
    return c.outline()


def bite():
    """24 x 24: the speech bubble with a dark red exclamation mark at its centre."""
    c = pets_emotes.bubble()
    for j, row in enumerate(MARK_ROWS):
        for i, ch in enumerate(row):
            if ch == '#':
                c.dot(11, 8 + j, MARK)
                c.dot(12, 8 + j, MARK)
    return c


def build():
    """{path under assets/: Image} for the bobber and the bite bubble."""
    return {
        'ui/fishing_bobber.png': bobber(),
        'ui/fish_bite.png': bite(),
    }


def provenance_entries(nosum):
    """{path: (package, [sources], text)} for provenance.py."""
    return {
        'ui/fishing_bobber.png': ('9 UI', [nosum], '16 x 16 fishing float in cell (0, 0): red top half, white bottom half, thin grey stem, 1 px dark outline (`fishing_ui.py`)'),
        'ui/fish_bite.png': ('9 UI', [nosum], '24 x 24 bite bubble: the white speech bubble of ui/emote_bubble.png with a dark red exclamation mark at its centre (`fishing_ui.py`)'),
    }
