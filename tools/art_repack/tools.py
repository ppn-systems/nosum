"""Package 5: sprites/tools.png and sprites/weapons.png (the tool and weapon icons)."""
import re

import draw
from draw import Canvas
from pngio import Image, read_png

ROW = re.compile(r'\[ItemCatalog\.(\w+)\] = (Tool|Weapon)\((\d+), (\d+)\)')

# Tool material tiers: (shadow, body, light, glint) of the metal head.
TIERS = {
    'basic': ((84, 70, 64), (136, 120, 106), (186, 170, 146), (232, 224, 204)),
    'copper': ((128, 60, 38), (208, 116, 58), (244, 170, 98), (255, 226, 160)),
    'iron': ((70, 82, 112), (128, 144, 172), (180, 194, 214), (240, 246, 255)),
}
WOOD = ((119, 56, 51), (176, 100, 66), (226, 150, 96))      # handle: shadow, body, light


def handle(c, x0, y0, n):
    """A wooden handle of n steps rising to the right from (x0, y0): light on its upper edge, shadow on its lower."""
    for i in range(n):
        x, y = x0 + i, y0 - i
        c.dot(x, y, WOOD[2])
        c.dot(x + 1, y, WOOD[1])
        c.dot(x + 1, y + 1, WOOD[0])
    return c


def head(c, rows, tier, x, y):
    """ASCII head: W glint, L light, M body, D shadow."""
    d, m, l, w = TIERS[tier]
    c.art(rows, {'W': w, 'L': l, 'M': m, 'D': d}, x, y)


def hoe(tier):
    c = Canvas()
    handle(c, 2, 13, 8)
    head(c, ['LLLLLL',
             'WLMMMMD',
             '.MMDDD.',
             '.MD....',
             '.D.....'], tier, 8, 2)
    c.rect(9, 5, 2, 2, WOOD[1])
    return c.outline()


def axe(tier):
    c = Canvas()
    handle(c, 2, 14, 9)
    head(c, ['..LL...',
             '.WLMM..',
             '.LMMMD.',
             '.LMMMMD',
             '..MMMDD',
             '..MDDD.'], tier, 7, 1)
    c.dot(10, 7, WOOD[1])
    return c.outline()


def pickaxe(tier):
    c = Canvas()
    handle(c, 2, 14, 9)
    head(c, ['..LLLLL..',
             '.WLMMMMDD',
             'LLM...MDD',
             'LM.....DD',
             'M.......D'], tier, 5, 0)
    c.rect(9, 3, 2, 2, WOOD[1])
    return c.outline()


def scythe(tier):
    c = Canvas()
    handle(c, 5, 15, 7)
    c.rect(11, 6, 2, 3, WOOD[1])
    head(c, ['..WLLLLLLL.',
             '.LLMMMMMMDD',
             'LLMDDDDDD..',
             'LMD........',
             'MD.........',
             'D..........'], tier, 1, 2)
    return c.outline()


def watering_can(tier):
    d, m, l, w = TIERS[tier]
    c = Canvas()
    c.rect(2, 7, 8, 7, m)                   # body
    c.rect(2, 7, 8, 1, l)
    c.rect(2, 8, 1, 6, l)
    c.rect(9, 8, 1, 6, d)
    c.rect(2, 13, 8, 1, d)
    c.rect(3, 9, 1, 2, w)                   # glint on the shoulder
    c.rect(4, 6, 4, 1, d)                   # rim
    c.dot(10, 11, m)                        # spout, rising to the right to its rose
    c.line(10, 11, 13, 6, m)
    c.line(10, 12, 13, 7, d)
    c.rect(12, 4, 3, 2, l)                  # rose
    c.dot(13, 4, w)
    c.dot(14, 5, d)
    c.line(2, 8, 0, 9, WOOD[2])             # handle on the left
    c.line(0, 9, 0, 12, WOOD[1])
    c.line(0, 12, 2, 13, WOOD[0])
    c.rect(4, 9, 4, 3, BLUE_D)              # water window
    c.rect(4, 9, 4, 1, BLUE)
    c.dot(5, 9, BLUE_L)
    return c.outline()


BLUE_D, BLUE, BLUE_L = (40, 100, 170), (70, 160, 230), (170, 225, 255)


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
    drawn = {'Hoe': hoe, 'WateringCan': watering_can, 'Axe': axe, 'Pickaxe': pickaxe, 'Scythe': scythe}
    cc0_tools = {}
    if src is not None:
        farm = src.sheet('farm')
        dungeon = src.sheet('dungeon')
        pail = farm.cell(124 % 12, 124 // 12)
        tier_sheets = {
            'basic': src.sheet('weapons_iron'),
            'copper': src.sheet('weapons_bronze'),
            'iron': src.sheet('weapons_steel'),
        }
        tool_cells = {'Hoe': (12, 10), 'Axe': (0, 10), 'Pickaxe': (13, 10), 'Scythe': (15, 10)}
        for tier_name, sheet in tier_sheets.items():
            for tool_key, (cx, cy) in tool_cells.items():
                cc0_tools[(tier_name, tool_key)] = sheet.cell(cx, cy).flip_h()
        fixed = {
            'MilkPail': pail,
            'CoopBlueprint': blueprint(),
            'BarnBlueprint': blueprint(),
            'WoodenSword': dungeon.cell(8, 8),
            'BronzeSword': src.sheet('weapons_bronze').cell(1, 0).flip_h(),
            'IronSword': src.sheet('weapons_iron').cell(2, 0).flip_h(),
            'SteelSword': src.sheet('weapons_steel').cell(3, 0).flip_h(),
            'GoldSword': src.sheet('weapons_gold').cell(5, 0).flip_h(),
            'ArcaneStaff': dungeon.cell(9, 10),
        }
        extra_weapons = [
            (src.sheet('roguelike_chars').cell(35, 1), 6, 0),
            (src.sheet('battle').cell(15, 10), 7, 0),
        ]
    else:
        old_tools = read_png(f'{repo}/assets/sprites/tools.png')
        old_weapons = read_png(f'{repo}/assets/sprites/weapons.png')
        cc0_tools = {
            ('basic', 'Hoe'): old_tools.crop(0 * 16, 2 * 16, 16, 16),
            ('copper', 'Hoe'): old_tools.crop(7 * 16, 2 * 16, 16, 16),
            ('iron', 'Hoe'): old_tools.crop(14 * 16, 2 * 16, 16, 16),
            ('basic', 'Axe'): old_tools.crop(5 * 16, 10 * 16, 16, 16),
            ('copper', 'Axe'): old_tools.crop(12 * 16, 10 * 16, 16, 16),
            ('iron', 'Axe'): old_tools.crop(19 * 16, 10 * 16, 16, 16),
            ('basic', 'Pickaxe'): old_tools.crop(5 * 16, 6 * 16, 16, 16),
            ('copper', 'Pickaxe'): old_tools.crop(12 * 16, 6 * 16, 16, 16),
            ('iron', 'Pickaxe'): old_tools.crop(19 * 16, 6 * 16, 16, 16),
            ('basic', 'Scythe'): old_weapons.crop(7 * 16, 5 * 16, 16, 16),
        }
        fixed = {
            'MilkPail': old_tools.crop(6 * 16, 0, 16, 16),
            'CoopBlueprint': blueprint(),
            'BarnBlueprint': blueprint(),
            'WoodenSword': old_weapons.crop(0 * 16, 0, 16, 16),
            'BronzeSword': old_weapons.crop(1 * 16, 0, 16, 16),
            'IronSword': old_weapons.crop(2 * 16, 0, 16, 16),
            'SteelSword': old_weapons.crop(3 * 16, 0, 16, 16),
            'GoldSword': old_weapons.crop(4 * 16, 0, 16, 16),
            'ArcaneStaff': old_weapons.crop(5 * 16, 0, 16, 16),
        }
        extra_weapons = [
            (old_weapons.crop(6 * 16, 0, 16, 16), 6, 0),
            (old_weapons.crop(7 * 16, 0, 16, 16), 7, 0),
        ]
    tools = Image(*tools_size)
    weapons = Image(*weapons_size)
    with open(f'{repo}/wasm/Nosum.Wasm.ViewModels/Inventory/ItemIcons.cs', encoding='utf-8') as f:
        rows = ROW.findall(f.read())
    if not rows:
        raise SystemExit('ItemIcons.cs: no Tool(...) rows found; update tools/art_repack/tools.py')
    for name, kind, a, b in rows:
        if name in fixed:
            pic = fixed[name]
        else:
            tier = 'copper' if name.startswith('Copper') else 'iron' if name.startswith('Iron') else 'basic'
            key = name.removeprefix('Copper').removeprefix('Iron')
            if (tier, key) in cc0_tools:
                pic = cc0_tools[(tier, key)]
            else:
                pic = drawn[key](tier) if key in drawn else fixed[key]
        col, row = int(a), int(b)
        if kind == 'Tool':
            tools.paste(pic, col * 16, row * 16)
        else:
            weapons.paste(pic, col * 16, row * 16)
    for pic, col, row in extra_weapons:
        weapons.paste(pic, col * 16, row * 16)
    return tools, weapons
