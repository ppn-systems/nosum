"""The provenance table: per replaced file its package, sources and mapping. write() renders docs/art/provenance.md from it."""
import os

import crops
import icons
import npcs
import re
import sources

NOSUM = 'Nosum original (drawn in tools/art_repack, no outside source)'

# path under assets/ -> (package, [pack directories or NOSUM], what the file is and how it is made)
ENTRIES = {
    'farmer/body.png': ('1 Player farmer layers', [NOSUM], 'Body (head, neck, torso, legs; skin tint) in columns 0-5 (idle, walk steps and swing stances) and arms with boots (no tint) in columns 6-11; rows down, right, up (`farmer.body`, `farmer.arms`)'),
    'farmer/pants.png': ('1 Player farmer layers', [NOSUM], 'Trousers style 0, grey-scale for the pants tint, same walk and swing columns as the body (`farmer.pants`)'),
    'farmer/hair.png': ('1 Player farmer layers', [NOSUM], 'Eight hair styles in grey-scale for the hair dye, rows down, right, up (`farmer.hair`)'),
    'farmer/shirts.png': ('1 Player farmer layers', [NOSUM], '8 x 8 shirt pieces (front, right, back) of shirts 1 red, 3 straw, 7 blue, 11 green; the front piece is also the inventory icon (`farmer.shirt`)'),
    'farmer/hats.png': ('1 Player farmer layers', [NOSUM], '20 x 20 hats 3 (straw) and 5 (cap), rows down, right, left, up; the down frame is the inventory icon (`farmer.hat`)'),
    'sprites/crops.png': ('4 Crops and soil', ['kenney_tiny-farm', 'josehzz_farming-crops-16x16'],
                             '21 crops, 5-6 growth frames each: Tiny Farm\'s stages for the 8 crops it covers, the crops pack\'s six pictures (seed, three stages, ripe) for the rest, placed in the catalogue\'s frames at the bottom of each 16 x 32 cell; recolours in the crop table'),
    'sprites/soil.png': ('4 Crops and soil', [NOSUM], 'Tilled soil, dry and wet, 16 neighbour variants each in the cell order of FarmDecorator: lit ridges, furrows, clods and a dark lip (`crops.soil_tile`)'),
    'sprites/tools.png': ('5 Tools and weapons', ['oga_weapon-rpg-icons', 'kenney_tiny-farm', NOSUM],
                             'Hoe, axe and pickaxe in three material tiers (iron, bronze/copper, steel) from Weapon RPG Icons (row 10, cells 12, 0 and 13, flipped so the blade faces right); watering can and blueprints drawn here (`tools.py`); the milk pail is Tiny Farm tile 124'),
    'sprites/weapons.png': ('5 Tools and weapons', ['kenney_tiny-dungeon', 'oga_weapon-rpg-icons', 'kenney_roguelike-characters', 'kenney_tiny-battle', NOSUM],
                             'The scythe from Weapon RPG Icons (cell 15, 10, flipped), cell (7, 5); wooden sword and arcane staff from Tiny Dungeon (8, 8 and 9, 10); bronze, iron, steel and gold swords from Weapon RPG Icons (flipped); round shield from Kenney Roguelike Characters (35, 1); battle emblem from Kenney Tiny Battle (15, 10)'),
    'sprites/monsters.png': ('11 Monsters', ['oga_animated-slimes', 'oga_trash-mobs', 'oga_tiny-creatures'],
                              'Twelve 4-column x 3-row (Down, Right, Up) monster and boss animation blocks (64 x 576): GreenSlime, FrostSlime, MagmaSlime from 16x16 Animated Slimes; CaveBat and SporeShroom from Fantasy RPG Trash Mobs; GoblinScout, SkeletonWarrior, StoneGolem from Tiny Creatures; BossBroodmother, BossGoblinChieftain, BossMagmaColossus, BossGolemKing (`monsters.py`, ADR-047)'),
    'sprites/items.png': ('3 Items, 6 Resource nodes', [
        'kenney_tiny-farm', 'josehzz_farming-crops-16x16', 'kenney_tiny-town', 'dawnlike',
        'oga_assorted-rpg-icons', 'oga_animated-slimes', 'tiny16', 'oga_trash-mobs',
        'oga_16x16-rpg-items', 'kenney_roguelike-caves-dungeons', 'oga_16x16-dungeon-tiles',
        'oga_puny-dungeon', 'oga_gold-treasure-icons', 'oga_6-color-dungeon',
        'kenney_roguelike-rpg-pack', 'kenney_roguelike-indoors', 'oga_random-animals',
        'oga_pixel-animals-16x16', 'kenney_roguelike-modern-city', 'kenney_rpg-urban-pack',
        'kenney_1-bit-pack', 'oga_tiny-creatures', NOSUM],
        'The 144 object cells the game draws (120 to 131 are the duck and goat milk, duck eggs, wool and truffles, drawn here; 132 to 143 are the fishing rod, the fish stew and the ten fish cut from DawnLike Tool, Food and Aquatic and Tiny Creatures, ADR-078): seed bags, produce, hay, milk, eggs and stone from Tiny Farm or the CC0 crops, the fence from Tiny Town, mushroom, hazelnut, berries, leek, soup, copper and steel ore and the copper boulder from DawnLike (CC-BY), potions and spell tome from Assorted RPG Icons, monster drops and RPG relics from Animated Slimes, Tiny 16 (CC-BY), Trash Mobs, 16x16 RPG Items, Roguelike Caves & Dungeons, 16x16 Dungeon Tiles, Puny Dungeon, Gold Treasure Icons, 6 Color Dungeon, Roguelike/RPG Pack, Roguelike Indoors, Random Animals, Pixel Animals 16x16, Roguelike Modern City, RPG Urban Pack and 1-Bit Pack, everything else drawn here (table below); unused cells are transparent'),
    'sprites/furniture.png': ('3 Items', ['kenney_roguelike-indoors', NOSUM], 'Chair, armchair, couch, table, bookcase, floor lamp and house plant at the rectangles of `PlaceableCatalog`; the inventory icon is a window of the same picture (`furniture.py`). Below y 1488, the farmhouse band of 32 pieces (`NEW_PIECES`): the chest of drawers, kitchen cabinet, wall shelf, candelabra, landscape and portrait paintings and mirror are stitched from Roguelike Indoors cells (the candelabra and mirror are cut as shipped); the other pieces are drawn here (the green armchair is the blue armchair\'s drawing in green fabric)'),
    'sprites/craftables.png': ('3 Items', ['kenney_tiny-dungeon', NOSUM], 'Scarecrow and furnace drawn here (`furniture.py`); the chest is Tiny Dungeon\'s closed chest (tile 5, 7), standing at the foot of its 16 x 32 cell'),
    'sprites/trees/oak.png': ('6 Resource nodes', [NOSUM], 'Oak: 48 x 96 tree, bush, sapling, seed mound and stump at the cells `NodeSprites` reads (`trees.py`)'),
    'sprites/trees/maple.png': ('6 Resource nodes', [NOSUM], 'Maple, same layout in autumn orange (`trees.py`)'),
    'sprites/trees/pine.png': ('6 Resource nodes', [NOSUM], 'Pine, same layout (`trees.py`)'),
    'sprites/buildings/farmhouse.png': ('7 Buildings and props', ['kenney_tiny-town'], 'Farmhouse, 9 x 9 tiles at (16, 0): Tiny Town roof and wall tiles (`buildings.py`)'),
    'sprites/buildings/greenhouse.png': ('7 Buildings and props', ['kenney_tiny-town', NOSUM], 'Greenhouse, 7 x 10 tiles at (0, 160): slate roof and door from Tiny Town, glass wall drawn here'),
    'sprites/buildings/barn.png': ('7 Buildings and props', ['kenney_tiny-town', 'kenney_tiny-farm'], 'Barn, 7 x 7 tiles: Tiny Town red roof, Tiny Farm cross-braced red boards (tiles 90-92, 102-104, 114-116), window (128) and two braced double doors (126)'),
    'sprites/buildings/coop.png': ('7 Buildings and props', ['kenney_tiny-town', 'kenney_tiny-farm'], 'Coop, 6 x 7 tiles: Tiny Town slate roof, Tiny Farm boards (tiles 102-104, 114-116) turned to pine, windows (128) and door (127)'),
    'sprites/buildings/shipping_bin.png': ('7 Buildings and props', [NOSUM], 'Shipping bin, 2 x 2 tiles (`buildings.shipping_bin`)'),
    'sprites/animals/chicken.png': ('8 Animals', ['oga_tiny-creatures'], 'Chicken, 16 x 16 frames: Tiny Creatures chicken (column 0, row 15), flipped for the right row, lifted 1 px on walk frames 1 and 3; rows down, right, up (`animals.py`)'),
    'sprites/hearts.png': ('8 Animals', [NOSUM], 'Affection hearts, 8 x 8 each: filled (cell 0, 0) and empty (cell 8, 0), drawn in the Kenney palette (`hearts.py`)'),
    'sprites/ambient.png': ('12 Ambient wildlife', ['dawnlike', NOSUM],
                              'Six 16 x 16 cells: bird (perched, flying), frog (sitting, hopping), squirrel (standing, running) and butterfly (wings open, closed) cut from DawnLike Avian0, Reptile0, Rodent0 and Pest0 (cells in `ambient.SHEET_CELLS`); two fireflies (lit, dim) drawn here (`ambient.py`, ADR-071)'),
    'sprites/animals/goat.png': ('8 Animals', ['oga_tiny-creatures'], 'Goat, 16 x 16 frames: Tiny Creatures goat (column 2, row 15), flipped for the right row, lifted 1 px on walk frames 1 and 3; rows down, right, up (`animals.py`)'),
    'sprites/animals/sheep.png': ('8 Animals', ['oga_tiny-creatures'], 'Sheep, 16 x 16 frames: Tiny Creatures sheep (column 3, row 15), flipped for the right row, lifted 1 px on walk frames 1 and 3; rows down, right, up (`animals.py`)'),
    'sprites/animals/duck.png': ('8 Animals', [NOSUM], 'Duck, 16 x 16 frames: white duck with a yellow cast and an orange bill, drawn with the Canvas helpers; rows down, right, up, the chicken\'s walk (`animals.py`)'),
    'sprites/animals/pig.png': ('8 Animals', [NOSUM], 'Pig, 16 x 16 frames: pink pig with a snout, small ears and four legs, drawn with the Canvas helpers; rows down, right, up, the chicken\'s walk (`animals.py`)'),
    'sprites/animals/cow.png': ('8 Animals', [NOSUM], 'Cow, 32 x 32 frames: rows down, right, up, four walk frames (`animals.py`); Tiny Creatures\' 16 x 16 cow was tried and rejected because it needs upscaling'),
    'ui/menu.png': ('9 UI', ['kenney_ui-pack-pixel-adventure', NOSUM], 'Window frame (16 x 16 at 0, 256, 5 px slice) and the small plaque frame (16 x 16 at 32, 256, 4 px slice): Pixel Adventure small tiles (15, 1) and (10, 0), three transparent edge pixels of the first filled in; the 3 x 3 slot bevel at (128, 128) is drawn here (`ui.py`)'),
    'ui/cursors.png': ('9 UI', ['kenney_pixel-ui-pack', NOSUM], 'From the Pixel UI pack: the eight block-button frames (9 x 11, normal and pressed; Ancient brown, grey, Colored yellow and red 9-slice buttons assembled to a 3 / 3 / 5 / 3 slice, the wood, stone and red faces darkened for text contrast, at y 432 and 444), the twelve tab plates (Ancient tan), the close button (cell 21, 6) and the arrow and hand cursors (cells 3, 32 and 0, 25). Drawn here: the gold selection frame with glow (80 x 80 at 448, 128), title strip, gauge frame, tab pictograms, pager arrows and coin (`ui.py`)'),
    'ui/text_box.png': ('9 UI', [NOSUM], 'Sunk text field box (188 x 48 at x 4), drawn at art pixels and enlarged 4 x in the palette of the UI packs (`ui.py`)'),
    'ui/start_background.png': ('9 UI', [NOSUM], 'Start screen: dithered sky, shaded clouds, sea with wave glints, horizon at y 345 (`ui.py`)'),
}

ENTRIES.update(npcs.provenance_entries(NOSUM))

HEADER = '''# Provenance of the art under assets/

Generated by `python3 tools/art_repack/repack_cc0_art.py --docs`; do not edit by hand. Every file under `assets/` listed here is Nosum's own: drawn by
the code in `tools/art_repack`, or cut from a pack of the library ([`library.md`](library.md), the packs themselves in `art/source/`): CC0, except DawnLike, which is
CC-BY 4.0 and must be credited (see Credits below). The license texts are kept in [`licenses/`](licenses/). `ProvenanceTests` checks that
each pack named here keeps its license text. How the candidates were chosen: [`sourcing.md`](sourcing.md).
The packs are committed under `art/source/<directory>`; run the script with `--sources art/source` (the pinned SHA-256 of every sheet it reads is in `sources.py`).
The two Kenney UI packs' files are also committed under [`art/source/ui/`](../../art/source/ui/README.md) (CC0), so `--only ui` needs no other download.

'''


MAP_SHEETS = '''
## Map tilesheets (art/own/Maps)

The six tilesheets of the own maps are not under `assets/`: they are drawn in code, deterministically, by `tools/Nosum.MapGen sheets` from
`tools/Nosum.MapGen/Sheets/` (palette, texture helpers and one drawing per sheet), and `sheets --check` fails when a committed picture differs from
what the code draws. No outside art is read or copied. The sheets were redrawn on 2026-10-26 with smooth tileable noise, five-tone ramps, shore
foam and lip edges on the transitions, lobed tree canopies and bevelled wood and stone, keeping every sheet's grid and tile indices. The open-licensed
packs that were checked for them, and why none is used, are in [`sourcing-2026-10-26.md`](sourcing-2026-10-26.md). Per-file entries: [`art/own/SOURCES.md`](../../art/own/SOURCES.md).

| File | Tileset | Source | License |
| :-- | :-- | :-- | :-- |
| `art/own/Maps/terrain.png` | `terrain` | Nosum original, drawn by `tools/Nosum.MapGen/Sheets/TerrainSheet.cs` | Nosum's own art |
| `art/own/Maps/nature.png` | `nature` | Nosum original, drawn by `NatureSheet.cs` | Nosum's own art |
| `art/own/Maps/town.png` | `town` | Nosum original, drawn by `TownSheet.cs` | Nosum's own art |
| `art/own/Maps/interior.png` | `interior` | Nosum original, drawn by `InteriorSheet.cs` | Nosum's own art |
| `art/own/Maps/cave.png` | `cave` | Nosum original, drawn by `CaveSheet.cs` | Nosum's own art |
| `art/own/Maps/markers.png` | `Paths` | Nosum original, drawn by `MarkerSheet.cs` (never drawn in game) | Nosum's own art |
'''


def write(repo, replaced):
    lines = [HEADER, '## Sources\n\n', '| Directory | Pack | Page | License as verified | Author |\n', '| :-- | :-- | :-- | :-- | :-- |\n']
    for key, (title, page, lic, author) in sources.PACKS.items():
        lines.append(f'| `{key}` | {title} | <{page}> | {lic} | {author} |\n')
    lines.append('| `tools/art_repack` | Nosum original | - | Own work of the project: shapes drawn by the code in this folder, no outside art | PPN Systems |\n')
    lines += ['\n## Credits\n\n', 'DawnLike (CC-BY 4.0) is used for some item icons: "DawnLike tileset by DragonDePlatino, palette by DawnBringer, CC-BY 4.0", <https://opengameart.org/content/dawnlike-16x16-universal-rogue-like-tileset-v181>. Tiny 16 (CC-BY 4.0) is used for the bat wing icon: "Tiny 16 tileset by Sharm (Lanea Zimmerman), CC-BY 4.0", <https://opengameart.org/content/tiny-16-basic>. CC0 packs need no credit; Kenney, Clint Bellanger, Shade, stealthix, Emcee Flesher, Bonsaiheldin, Jetrel, ETTiNGRiNDER, HorusKDI, hdst and GrumpyDiamond are named as a courtesy.\n']
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
    lines.append(MAP_SHEETS)
    with open(os.path.join(repo, 'docs', 'art', 'provenance.md'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(''.join(lines))
    write_credits(repo, replaced)


def short_license(text):
    """The license id of a PACKS entry, language-neutral: 'CC-BY 4.0' or 'CC0 1.0'."""
    if text.startswith('CC-BY 4.0'):
        return 'CC-BY 4.0'
    if text.startswith('CC0'):
        return 'CC0 1.0'
    raise SystemExit('unknown license for the credits: ' + text)


def write_credits(repo, replaced):
    """wasm/Nosum.Wasm.ViewModels/Settings/ArtCredits.g.cs: the packs the files under assets/ use, for the credits shown to players."""
    used = {k for path in replaced for k in ENTRIES[path][1] if k in sources.PACKS}
    rows = ''.join(
        f'        new("{key}", "{title}", "{author}", "{short_license(lic)}", "{page}"),\n'
        for key, (title, page, lic, author) in sources.PACKS.items() if key in used)
    text = ('// <auto-generated>Written by tools/art_repack/repack_cc0_art.py --docs; do not edit by hand.</auto-generated>\n\n'
            'using System.Collections.Generic;\n\nnamespace Nosum.Wasm.ViewModels.Settings;\n\n'
            '/// <summary>One art pack the game uses, for the credits page. The data is language-neutral; the labels around it are localized.</summary>\n'
            '/// <param name="Pack">Directory of the pack under art/source/.</param>\n/// <param name="Title">Name of the pack.</param>\n'
            '/// <param name="Author">Who made it.</param>\n/// <param name="License">Short license id.</param>\n/// <param name="Url">The pack\'s page.</param>\n'
            'public sealed record ArtCredit(string Pack, string Title, string Author, string License, string Url);\n\n'
            '/// <summary>The packs the files under assets/ use (generated from docs/art/provenance.md\'s sources).</summary>\n'
            'public static class ArtCredits\n{\n'
            '    /// <summary>Gets every pack in use.</summary>\n'
            '    public static IReadOnlyList<ArtCredit> All { get; } =\n    [\n' + rows + '    ];\n}\n')
    with open(os.path.join(repo, 'wasm', 'Nosum.Wasm.ViewModels', 'Settings', 'ArtCredits.g.cs'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)


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
