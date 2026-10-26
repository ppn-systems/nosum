"""The provenance table: per replaced file its package, sources and mapping. write() renders docs/art/provenance.md from it."""
import os

import crops
import icons
import re
import sources

NOSUM = 'Nosum original (drawn in tools/art_repack, no outside source)'

# path under assets/ -> (package, [pack directories or NOSUM], what the file is and how it is made)
ENTRIES = {
    'farmer/body.png': ('1 Player farmer layers', [NOSUM], 'Body (head, neck, torso, legs; skin tint) in columns 0-2 and arms with boots (no tint) in columns 6-8; rows down, right, up, three walk frames; the step columns carry the 1 px bob the code gives shirt, hair and hat (`farmer.body`, `farmer.arms`)'),
    'farmer/pants.png': ('1 Player farmer layers', [NOSUM], 'Trousers style 0, grey-scale for the pants tint, same walk frames as the body (`farmer.pants`)'),
    'farmer/hair.png': ('1 Player farmer layers', [NOSUM], 'Eight hair styles in grey-scale for the hair dye, rows down, right, up (`farmer.hair`)'),
    'farmer/shirts.png': ('1 Player farmer layers', [NOSUM], '8 x 8 shirt pieces (front, right, back) of shirts 1 red, 3 straw, 7 blue, 11 green; the front piece is also the inventory icon (`farmer.shirt`)'),
    'farmer/hats.png': ('1 Player farmer layers', [NOSUM], '20 x 20 hats 3 (straw) and 5 (cap), rows down, right, left, up; the down frame is the inventory icon (`farmer.hat`)'),
    'sprites/crops.png': ('4 Crops and soil', ['kenney_tiny-farm', 'josehzz_farming-crops-16x16'],
                             '21 crops, 5-6 growth frames each: Tiny Farm\'s stages for the 8 crops it covers, the crops pack\'s six pictures (seed, three stages, ripe) for the rest, placed in the catalogue\'s frames at the bottom of each 16 x 32 cell; recolours in the crop table'),
    'sprites/soil.png': ('4 Crops and soil', [NOSUM], 'Tilled soil, dry and wet, 16 neighbour variants each in the cell order of FarmDecorator (`crops.soil_tile`)'),
    'sprites/tools.png': ('5 Tools and weapons', ['kenney_tiny-town', 'kenney_tiny-farm', NOSUM],
                             'Axe and pickaxe from Tiny Town (tiles 127, 115), watering can (Tiny Farm tile 84) and milk pail (Tiny Farm tile 124), in three material tiers by palette swap; hoe and blueprint drawn in the same palette'),
    'sprites/weapons.png': ('5 Tools and weapons', ['kenney_tiny-farm'], 'The scythe: Tiny Farm sickle (tile 88)'),
    'sprites/items.png': ('3 Items, 6 Resource nodes', ['kenney_tiny-farm', 'josehzz_farming-crops-16x16', 'kenney_tiny-town', NOSUM],
                               'The 92 object cells the game draws: seed bags, produce, hay, milk, eggs and stone from Tiny Farm or the CC0 crops, the fence from Tiny Town, everything else drawn here (table below); unused cells are transparent'),
    'sprites/furniture.png': ('3 Items', [NOSUM], 'Chair, armchair, couch, table, bookcase, floor lamp and house plant at the rectangles of `PlaceableCatalog`; the inventory icon is a window of the same picture (`furniture.py`)'),
    'sprites/craftables.png': ('3 Items', [NOSUM], 'Scarecrow, chest and furnace (`furniture.py`)'),
    'sprites/trees/oak.png': ('6 Resource nodes', [NOSUM], 'Oak: 48 x 96 tree, bush, sapling, seed mound and stump at the cells `NodeSprites` reads (`trees.py`)'),
    'sprites/trees/maple.png': ('6 Resource nodes', [NOSUM], 'Maple, same layout in autumn orange (`trees.py`)'),
    'sprites/trees/pine.png': ('6 Resource nodes', [NOSUM], 'Pine, same layout (`trees.py`)'),
    'sprites/buildings/farmhouse.png': ('7 Buildings and props', ['kenney_tiny-town'], 'Farmhouse, 9 x 9 tiles at (16, 0): Tiny Town roof and wall tiles (`buildings.py`)'),
    'sprites/buildings/greenhouse.png': ('7 Buildings and props', ['kenney_tiny-town', NOSUM], 'Greenhouse, 7 x 10 tiles at (0, 160): slate roof and door from Tiny Town, glass wall drawn here'),
    'sprites/buildings/barn.png': ('7 Buildings and props', ['kenney_tiny-town', 'kenney_tiny-farm'], 'Barn, 7 x 7 tiles: Tiny Town red roof, Tiny Farm cross-braced red boards (tiles 90-92, 102-104, 114-116), window (128) and two braced double doors (126)'),
    'sprites/buildings/coop.png': ('7 Buildings and props', ['kenney_tiny-town'], 'Coop, 6 x 7 tiles: slate roof, brown wall, door and windows'),
    'sprites/buildings/shipping_bin.png': ('7 Buildings and props', [NOSUM], 'Shipping bin, 2 x 2 tiles (`buildings.shipping_bin`)'),
    'sprites/animals/chicken.png': ('8 Animals', [NOSUM], 'Chicken, 16 x 16 frames: rows down, right, up, four walk frames (`animals.py`)'),
    'sprites/animals/cow.png': ('8 Animals', [NOSUM], 'Cow, 32 x 32 frames: rows down, right, up, four walk frames (`animals.py`)'),
    'ui/menu.png': ('9 UI', [NOSUM], 'Window frame (60 x 60 at 0, 256), inset (0, 320) and inventory slot (128, 128) for the CSS border-image slices (`ui.py`)'),
    'ui/cursors.png': ('9 UI', [NOSUM], 'Selection frame, buttons, close button, arrows, coin, tab pictograms and the two mouse cursors at the rectangles of `nosum-theme.js` (`ui.py`)'),
    'ui/text_box.png': ('9 UI', [NOSUM], 'Text field box (188 x 48 at x 4) (`ui.py`)'),
    'ui/start_background.png': ('9 UI', [NOSUM], 'Start screen: dithered sky, shaded clouds, sea with wave glints, horizon at y 345 (`ui.py`)'),
}

HEADER = '''# Provenance of the art under assets/

Generated by `python3 tools/art_repack/repack_cc0_art.py --docs`; do not edit by hand. Every file under `assets/` listed here is Nosum's own: drawn by
the code in `tools/art_repack`, or cut from a CC0 pack (the license texts are kept in [`licenses/`](licenses/)). `ProvenanceTests` checks that
each pack named here keeps its license text. How the candidates were chosen: [`sourcing.md`](sourcing.md).
Download the archives from the pages below, unzip each into a directory with the name in the first column, and run the script with `--sources` on their parent.

'''


def write(repo, replaced):
    lines = [HEADER, '## Sources\n\n', '| Directory | Pack | Page | License as verified | Author |\n', '| :-- | :-- | :-- | :-- | :-- |\n']
    for key, (title, page, lic, author) in sources.PACKS.items():
        lines.append(f'| `{key}` | {title} | <{page}> | {lic} | {author} |\n')
    lines.append('| `tools/art_repack` | Nosum original | - | Own work of the project: shapes drawn by the code in this folder, no outside art | PPN Systems |\n')
    lines += ['\n## Files\n\n', '| File (under `assets/`) | Package | Sources | What it is |\n', '| :-- | :-- | :-- | :-- |\n']
    for path in replaced:
        package, keys, what = ENTRIES[path]
        names = ', '.join(f'`{k}`' if k in sources.PACKS else NOSUM for k in keys)
        lines.append(f'| `{path}` | {package} | {names} | {what} |\n')
    lines += ['\n## Mapping: crops\n\n', 'A game crop uses the CC0 crop named (a Tiny Farm crop, or the crops pack with its index in the pack README); "as drawn" means no recolour. The harvest picture is reused as the produce icon.\n\n',
              '| Game crop | CC0 crop | Change |\n', '| :-- | :-- | :-- |\n']
    for name, (index, _, note) in crops.RECIPES.items():
        if name in crops.FARM:
            lines.append(f'| `{name}` | {crops.FARM[name][3]} | seed: {CC0_CROPS[index]} ({index}) of the crops pack |\n')
        else:
            lines.append(f'| `{name}` | {CC0_CROPS[index]} ({index}) | {note} |\n')
    lines += ['\n## Mapping: object sheet\n\n', '`sprites/items.png` has 24 columns of 16 px cells: index = row x 24 + column.\n\n',
              '| Index | Game item or node | How |\n', '| --: | :-- | :-- |\n']
    names = object_names(repo)
    for index, fn in sorted(icons.OBJECTS.items()):
        lines.append(f'| {index} | {names.get(index, "resource node")} | {fn.note} |\n')
    with open(os.path.join(repo, 'docs', 'art', 'provenance.md'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(''.join(lines))


CC0_CROPS = ['Turnip', 'Rose', 'Cucumber', 'Tulip', 'Tomato', 'Melon', 'Eggplant', 'Lemon', 'Pineapple', 'Rice', 'Wheat', 'Grapes',
             'Strawberry', 'Cassava', 'Potato', 'Coffee', 'Orange', 'Avocado', 'Corn', 'Sunflower']


def object_names(repo):
    """{object sheet index: the item keys that draw it}, read from the item catalogue and the icon table."""
    names = {}
    with open(os.path.join(repo, 'gameplay', 'Nosum.Gameplay', 'Items', 'ItemCatalog.cs'), encoding='utf-8') as f:
        for key, index in re.findall(r'new\(\w+, "(\w+)".*?ObjectIcon = (\d+)', f.read()):
            names.setdefault(int(index), []).append(key)
    with open(os.path.join(repo, 'wasm', 'Nosum.Wasm', 'Game', 'ItemIcons.cs'), encoding='utf-8') as f:
        for key, index in re.findall(r'\[ItemCatalog\.(\w+)\] = ObjectId\((\d+)\)', f.read()):
            names.setdefault(int(index), []).append(key)
    nodes = {290: 'iron ore node', 294: 'twig node', 295: 'twig node', 313: 'weed node', 314: 'weed node', 315: 'weed node', 343: 'stone node', 450: 'stone node', 751: 'copper ore node', 771: 'fiber / grass node'}
    return {i: ', '.join(f'`{k}`' for k in v) + (f' and {nodes[i]}' if i in nodes else '') for i, v in names.items()} | {i: n for i, n in nodes.items() if i not in names}
