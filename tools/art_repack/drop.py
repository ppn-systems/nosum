"""sprites/drop.png (8 x 8): the water drop the farm draws above a tile that needs water (FarmDecorator, docs/adr/ADR-021 D7).

Drawn here: no CC0 pack in the library has a water drop in the Nosum palette. The drop is the BLUE family of the UI, so it reads on soil and grass
as the water it means; the highlight on the left keeps its shape at 8 px.
"""
import draw
from draw import Canvas

PALETTE = {'D': draw.BLUE_D, 'B': draw.BLUE, 'L': draw.BLUE_L}

# The drop, eight by eight; '.' is empty. It touches the bottom and sides so the outline sits on the shape, not on the grass.
ROWS = [
    '...D....',
    '..DB....',
    '..DBB...',
    '.DBLBB..',
    '.DBLLBB.',
    'DBLLLLBD',
    'DBLLLBBD',
    '.DDDDDD.',
]


def build():
    c = Canvas(8, 8)
    c.art(ROWS, PALETTE)
    return {'sprites/drop.png': c.outline()}
