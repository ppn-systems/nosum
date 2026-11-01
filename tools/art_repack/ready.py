"""sprites/ready.png (8 x 8): the ready marker the farm draws above a ripe crop (FarmDecorator, docs/adr/ADR-021).

Drawn here: no CC0 pack in the library has a sparkle in the Nosum palette. It is the GOLD family of the UI, so it reads as "ready to
harvest" next to the blue water drop; the bright centre keeps its four points at 8 px.
"""
import draw
from draw import Canvas

PALETTE = {'G': draw.GOLD, 'L': draw.GOLD_L}

# A four-point sparkle, eight by eight; '.' is empty. The points touch the sides so the outline sits on the shape.
ROWS = [
    '...GG...',
    '..GLLG..',
    '.GLLLLG.',
    'GGLLLLGG',
    'GLLLLLLG',
    '.GLLLLG.',
    '..GLLG..',
    '...GG...',
]


def build():
    c = Canvas(8, 8)
    c.art(ROWS, PALETTE)
    return {'sprites/ready.png': c.outline()}
