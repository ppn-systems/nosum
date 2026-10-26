"""Package 5: TileSheets/tools.png and TileSheets/weapons.png (the tool and weapon icons)."""
import re

import draw
from draw import Canvas
from pngio import Image

ROW = re.compile(r'\[ItemCatalog\.(\w+)\] = (Tool|Weapon)\((\d+), (\d+)\)')

# Tool material tiers: the steel greys of the source art are swapped for the tier's colours.
TIERS = {
    'basic': {draw.STEEL_D: (94, 76, 66), draw.STEEL: (150, 128, 108), draw.STEEL_L: (190, 168, 140), (139, 155, 180): (150, 128, 108), (193, 204, 221): (190, 168, 140)},
    'copper': {draw.STEEL_D: (140, 70, 40), draw.STEEL: (216, 128, 64), draw.STEEL_L: (244, 180, 110), (139, 155, 180): (216, 128, 64), (193, 204, 221): (244, 180, 110)},
    'iron': {},
}


def hoe():
    c = Canvas()
    c.line(2, 14, 11, 4, draw.WOOD)         # diagonal handle
    c.line(3, 14, 12, 4, draw.WOOD)
    c.line(2, 14, 10, 5, draw.WOOD_L)
    c.rect(9, 2, 6, 3, draw.STEEL)          # blade bar at the top
    c.rect(9, 2, 6, 1, draw.STEEL_L)
    c.rect(12, 5, 3, 4, draw.STEEL)         # blade tip bending down
    c.rect(12, 5, 1, 4, draw.STEEL_L)
    c.dot(14, 4, draw.STEEL_D)
    c.dot(14, 8, draw.STEEL_D)
    c.dot(13, 8, draw.STEEL_D)
    return c.outline()


def watering_can():
    c = Canvas()
    c.rect(3, 6, 8, 8, draw.STEEL)          # body
    c.rect(3, 6, 8, 1, draw.STEEL_L)
    c.rect(3, 12, 8, 2, draw.STEEL_D)
    c.rect(4, 5, 6, 1, draw.STEEL_L)        # rim
    c.line(11, 11, 14, 5, draw.STEEL)       # spout
    c.line(12, 11, 15, 5, draw.STEEL)
    c.rect(13, 3, 3, 2, draw.STEEL_L)       # rose
    c.dot(14, 4, draw.BLUE)
    c.dot(15, 4, draw.BLUE_L)
    c.line(3, 7, 1, 8, draw.WOOD)           # handle on the left
    c.line(1, 8, 1, 12, draw.WOOD)
    c.line(1, 12, 3, 13, draw.WOOD)
    c.rect(5, 8, 4, 2, draw.BLUE)           # water window
    c.dot(5, 8, draw.BLUE_L)
    return c.outline()


def blueprint():
    c = Canvas()
    c.rect(2, 3, 12, 10, (60, 110, 190))
    c.rect(2, 3, 12, 1, (110, 160, 230))
    c.rect(4, 6, 8, 1, draw.CREAM)          # a plan: a roof line and a box with a door
    c.rect(4, 7, 1, 4, draw.CREAM)
    c.rect(11, 7, 1, 4, draw.CREAM)
    c.rect(4, 10, 8, 1, draw.CREAM)
    c.rect(7, 8, 2, 2, draw.CREAM)
    return c.outline()


def build(src, repo, tools_size, weapons_size):
    town = src.sheet('town')

    def tile(i):
        return town.cell(i % 12, i // 12)
    from draw import swap
    base = {
        'Hoe': hoe(), 'WateringCan': watering_can(), 'Axe': tile(127), 'Pickaxe': tile(115),
        'Scythe': tile(129), 'MilkPail': tile(130), 'CoopBlueprint': blueprint(), 'BarnBlueprint': blueprint(),
    }
    tools = Image(*tools_size)
    weapons = Image(*weapons_size)
    with open(f'{repo}/wasm/Nosum.Wasm/Game/ItemIcons.cs', encoding='utf-8') as f:
        rows = ROW.findall(f.read())
    if not rows:
        raise SystemExit('ItemIcons.cs: no Tool(...) rows found; update tools/art_repack/tools.py')
    for name, kind, a, b in rows:
        tier = 'copper' if name.startswith('Copper') else 'iron' if name.startswith('Iron') else 'basic'
        key = name.removeprefix('Copper').removeprefix('Iron')
        pic = swap(base[key], TIERS[tier]) if key not in ('MilkPail', 'CoopBlueprint', 'BarnBlueprint') else base[key]
        (tools if kind == 'Tool' else weapons).paste(pic, int(a) * 16, int(b) * 16)
    return tools, weapons
