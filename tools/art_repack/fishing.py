"""Fishing (docs/adr/ADR-071): the fishing rod, the fish stew and the ten fish of sprites/items.png.

Every picture is cut from a pack under art/source/ (no drawing here): the rod from DawnLike Tool, the stew from DawnLike Food, the fish from
Tiny Creatures and DawnLike Aquatic. OBJECTS maps the object-sheet index (icons.py) to the picture; the indices follow the last cell icons.py
used, so no existing cell moves. The eel, the golden koi and the pufferfish are the closest creatures of the packs (no pack has a true one).
"""


def tile(sheet, column, row, what, flip=False):
    """A cell of a library pack (sources.SHEETS) as an object-sheet picture, turned to face right when flip is set."""
    def fn(src, _town):
        pic = src.sheet(sheet).cell(column, row)
        return pic.flip_h() if flip else pic
    fn.note = what
    return fn


OBJECTS = {
    120: tile('dawn_tool', 7, 1, 'DawnLike Tool (7, 1), the fishing rod'),
    121: tile('dawn_food', 1, 4, 'DawnLike Food (1, 4), the bowl of stew'),
    # sea fish
    122: tile('creatures', 2, 6, 'Tiny Creatures (2, 6), the silver anchovy'),
    123: tile('creatures', 0, 6, 'Tiny Creatures (0, 6), the green sea bass'),
    124: tile('dawn_aquatic', 4, 1, 'DawnLike Aquatic (4, 1), the blue-grey mackerel, turned to face right', flip=True),
    125: tile('dawn_aquatic', 5, 1, 'DawnLike Aquatic (5, 1), the plump orange fish (pufferfish), turned to face right', flip=True),
    # river fish
    126: tile('creatures', 3, 6, 'Tiny Creatures (3, 6), the brown carp'),
    127: tile('dawn_aquatic', 3, 1, 'DawnLike Aquatic (3, 1), the cyan trout, turned to face right', flip=True),
    128: tile('creatures', 1, 6, 'Tiny Creatures (1, 6), the green and pink bass'),
    129: tile('dawn_aquatic', 1, 1, 'DawnLike Aquatic (1, 1), the long grey-cyan pike, turned to face right', flip=True),
    130: tile('dawn_aquatic', 0, 2, 'DawnLike Aquatic (0, 2), the orange eel'),
    131: tile('dawn_aquatic', 2, 1, 'DawnLike Aquatic (2, 1), the orange and blue golden koi, turned to face right', flip=True),
}
