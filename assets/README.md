# assets/

The sprite sheets the client and server load: the farm's objects, crops and tools, the layered farmer, the animals and buildings, and the menu
pieces. Every file here is Nosum's own art or CC0, with its source recorded in [`docs/art/provenance.md`](../docs/art/provenance.md). The early
placeholder content is gone ([ADR-007](../docs/adr/ADR-007-placeholder-content-16px.md), [ADR-042](../docs/adr/ADR-042-own-maps-and-provenance-cleanup.md)).

| Folder | Holds | Examples |
| :--- | :--- | :--- |
| `sprites/` | Items and objects, crops, soil, tools, weapons, furniture, craftables | `sprites/items.png`, `sprites/crops.png` |
| `sprites/trees/` | Resource-node trees: oak, maple, pine | `sprites/trees/oak.png` |
| `sprites/buildings/` | Farm buildings and props | `sprites/buildings/coop.png` |
| `sprites/animals/` | Animal sheets | `sprites/animals/chicken.png` |
| `farmer/` | Layered player sheets: body, pants, shirts, hair, hats (16 × 32 frames) | `farmer/body.png` |
| `ui/` | Menu frame, cursors, text box, start background; linked into the Controls package | `ui/menu.png` |

## Naming scheme

- File and folder names are lowercase, with underscores, and name what a file shows: `items`, `shipping_bin`, `hair`. A name never reuses the
  name of a former placeholder file or folder.
- A folder says what kind of art it holds. A sheet's name in code is its path under `sprites/` without the extension: `buildings/coop` is
  `sprites/buildings/coop.png` (`PlaceableDefinition.Sheet`, `PropDefinition.Sheet`, `AnimalDefinition.Sheet`).
- The farmer's layers are `farmer/body.png`, `pants.png`, `shirts.png`, `hair.png` and `hats.png`; the client composes them per appearance.
- Maps (`*.tmx`) and the sheets they draw are not here. They live in [`art/own/Maps/`](../art/own/Maps/) and the client and server link them
  to the paths the game loads.

## Conventions

- 16 px tiles. Sheets are used as shipped: a sprite is addressed by its sheet and its index on the sheet's fixed grid, so a grid change means
  changing the index tables that read the sheet (`ItemDefinition.ObjectIcon`, `CropDefinition`, `PlaceableCatalog`, `BuildingCatalog`,
  the farmer layers) and their tests.
- Until ADR-042 P7 the release gate (`deploy/check-placeholder-art.sh public`) checks every file here against the list in
  [`docs/art/placeholders.sha256`](../docs/art/placeholders.sha256), and refuses a file that is not listed. A new file needs its entry there.
- The sheet rules and the grids are in [`assets-pipeline`](../.claude/skills/assets-pipeline/SKILL.md).
