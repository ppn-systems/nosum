# assets/

The art the client and server load for the sprites and the menu, which replaces the Stardew Valley placeholder content
([ADR-040](../docs/adr/ADR-040-own-art-plan.md)). Every file here is Nosum's own art or CC0, with its source recorded in
[`docs/art/provenance.md`](../docs/art/provenance.md). No file from the Stardew Valley `Content` folder remains ([ADR-007](../docs/adr/ADR-007-placeholder-content-16px.md)).

| Folder | Holds |
| :--- | :--- |
| `Characters/Farmer/` | Layered player sheets (base, hairstyles, hats, pants, shirts), 16 × 32 frames |
| `Animals/`, `Buildings/`, `LooseSprites/`, `TerrainFeatures/`, `TileSheets/` | Animals, buildings, UI pieces, trees and soil, crops, tools and furniture |
| `Maps/` | The menu tiles and the object sheet. The maps (`*.tmx`) and the other tilesheets are in [`art/own/Maps/`](../art/own/Maps/), linked to the paths the game loads |

`docs/art/inventory.md` lists every file the game loads. The release gate (`deploy/check-placeholder-art.sh public`) refuses a file here that is not
listed, so add a file only through the sprite or map code that loads it and the regenerated inventory (`tools/Nosum.AssetInventory`).

Conventions: 16 px tiles, sheets addressed by index on their fixed grid ([`assets-pipeline`](../.claude/skills/assets-pipeline/SKILL.md)).
