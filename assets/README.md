# assets/

The sprite sheets the client and server load: the farm's objects, crops and tools, the layered farmer, the animals and buildings, and the menu
pieces. Every file here is Nosum's own art or CC0, with its source recorded in [`docs/art/provenance.md`](../docs/art/provenance.md) (CC0 packs and
the code in `tools/art_repack`). The CC0 packs are Kenney Tiny Town and Tiny Farm and josehzz's Farming crops 16x16; candidates checked: [`docs/art/sourcing-sprites-2026-10-26.md`](../docs/art/sourcing-sprites-2026-10-26.md). The maps are own too ([`art/`](../art/README.md)).

| Folder | Holds | Examples |
| :--- | :--- | :--- |
| `sprites/` | Items and objects, crops, soil, tools, weapons, furniture, craftables. CC0 from Tiny Farm: crops (tomato, corn, wheat, carrot, turnip, cabbage), seed bags, produce, hay, milk, watering can, scythe | `sprites/items.png`, `sprites/crops.png` |
| `sprites/trees/` | Resource-node trees: oak, maple, pine | `sprites/trees/oak.png` |
| `sprites/buildings/` | Farm buildings and props (the barn walls are Tiny Farm's) | `sprites/buildings/coop.png` |
| `sprites/animals/` | Animal sheets | `sprites/animals/chicken.png` |
| `farmer/` | Layered player sheets: body, pants, shirts, hair, hats (16 × 32 frames) | `farmer/body.png` |
| `ui/` | Menu frame, cursors, text box, start background; linked into the Controls package | `ui/menu.png` |

## Naming scheme

- File and folder names are lowercase, with underscores, and name what a file shows: `items`, `shipping_bin`, `hair`.
- A folder says what kind of art it holds. A sheet's name in code is its path under `sprites/` without the extension: `buildings/coop` is
  `sprites/buildings/coop.png` (`PlaceableDefinition.Sheet`, `PropDefinition.Sheet`, `AnimalDefinition.Sheet`).
- The farmer's layers are `farmer/body.png`, `pants.png`, `shirts.png`, `hair.png` and `hats.png`; the client composes them per appearance.
- Maps (`*.tmx`) and the sheets they draw are not here. They live in [`art/own/Maps/`](../art/own/Maps/) and the client and server link them
  to the paths the game loads.

## Conventions

- 16 px tiles. Sheets are used as shipped: a sprite is addressed by its sheet and its index on the sheet's fixed grid, so a grid change means
  changing the index tables that read the sheet (`ItemDefinition.ObjectIcon`, `CropDefinition`, `PlaceableCatalog`, `BuildingCatalog`,
  the farmer layers) and their tests.
- A new file needs its source recorded: [`docs/art/provenance.md`](../docs/art/provenance.md) for a CC0 pack or a file drawn by `tools/art_repack`,
  or [`art/own/SOURCES.md`](../art/own/SOURCES.md) for a file Nosum makes. `ProvenanceTests` fails when one is missing.
- The sheet rules and the grids are in [`assets-pipeline`](../.claude/skills/assets-pipeline/SKILL.md).
