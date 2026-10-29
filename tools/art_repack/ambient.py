"""Package 12: sprites/ambient.png (96 x 32, six 16 x 16 cells), the wildlife that moves about the outdoor maps (docs/adr/ADR-071).

Layout (CritterDecorator, wasm/Nosum.Wasm/Game): row 0 holds the bird (cells 0-1: perched, flying), the frog (2-3: sitting, hopping)
and the firefly (4-5: lit, dim); row 1 holds the squirrel (0-1: standing, running) and the butterfly (2-3: wings open, wings closed).
Cells 4-5 of row 1 are spare and transparent. Birds, frogs, squirrels and butterflies are cut from the DawnLike pack (CC-BY 4.0) with
the cells named in SHEET_CELLS; the firefly has no picture in any pack, so it is drawn here.
"""
import draw
from draw import Canvas
from pngio import Image
import sources

# sheet cell (column, row) -> (source sheet key, source column, source row)
SHEET_CELLS = {
    (0, 0): ('dawn_avian', 0, 0),
    (1, 0): ('dawn_avian', 1, 0),
    (2, 0): ('dawn_reptile', 0, 9),
    (3, 0): ('dawn_reptile', 1, 9),
    (0, 1): ('dawn_rodent', 0, 1),
    (1, 1): ('dawn_rodent', 1, 1),
    (2, 1): ('dawn_pest', 0, 8),
    (3, 1): ('dawn_pest', 1, 8),
}

LIT = (255, 246, 150)
GLOW = (220, 230, 120)
DIM = (96, 92, 40)
BODY = (58, 50, 34)


def firefly(lit):
    """One firefly: a dark body with a glowing abdomen, the glow lit or dim."""
    c = Canvas(16, 16)
    c.rect(7, 6, 2, 4, BODY)
    c.rect(7, 9, 2, 2, LIT if lit else DIM)
    if lit:
        c.dot(7, 10, GLOW)
        c.dot(8, 10, GLOW)
        c.dot(6, 9, GLOW)
        c.dot(9, 9, GLOW)
    c.dot(6, 5, BODY)
    c.dot(9, 5, BODY)
    return c.outline()


def build(src):
    """The sheet as an Image; `src` is sources.Sources (the DawnLike sheets are read from it)."""
    out = Image(96, 32)
    for (column, row), (key, sx, sy) in SHEET_CELLS.items():
        cell = src.sheet(key).cell(sx, sy)
        out.paste(cell, column * 16, row * 16)
    out.paste(firefly(True), 4 * 16, 0)
    out.paste(firefly(False), 5 * 16, 0)
    return out
