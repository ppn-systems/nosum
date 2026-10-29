"""sprites/hearts.png (16 x 8): the affection hearts AnimalsDecorator draws over a petted animal, filled (cell 0, 0) and empty (cell 8, 0), 8 x 8 each.

Drawn here: no CC0 pack has a heart in the Nosum palette. The filled heart is the red of the Kenney frames with a highlight on the left lobe; the empty one is the steel tone of the UI, so both read on grass and on the wood floor of a barn.
"""
import draw
from draw import Canvas
from pngio import Image

FILLED = {'R': draw.RED, 'H': (255, 150, 140)}
EMPTY = {'R': draw.STEEL_L, 'H': draw.STEEL_L}

# The heart's shape, eight by eight; '.' is empty. Both cells use the same shape, only the palette differs.
ROWS = [
    '.RR..RR.',
    'RHRRRRRR',
    'RRRRRRRR',
    '.RRRRRR.',
    '..RRRR..',
    '...RR...',
    '........',
    '........',
]


def heart(palette):
    c = Canvas(8, 8)
    c.art(ROWS, palette)
    return c.outline()


def build():
    sheet = Image(16, 8)
    sheet.paste(heart(FILLED), 0, 0)
    sheet.paste(heart(EMPTY), 8, 0)
    return {'sprites/hearts.png': sheet}
