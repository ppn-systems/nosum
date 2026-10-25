# assets/

**Temporary development content** — the unpacked Stardew Valley `Content` folder, used under
[ADR-007](../docs/adr/ADR-007-placeholder-content-16px.md) until Nosum's own art replaces it.
It is copyrighted by ConcernedApe: keep this repository private and never ship, deploy or share
a build that contains it.

| Folder | Holds |
| :--- | :--- |
| `Maps/` | Tiled maps (`*.tmx`) and their 16 px tile sheets |
| `Characters/Farmer/` | Layered player sheets (base, pants, shirts, hairstyles, hats, accessories), 16 × 32 frames |
| `Characters/`, `Portraits/`, `Animals/` | NPC and animal sprite sheets |
| `TileSheets/`, `TerrainFeatures/`, `Buildings/`, `LooseSprites/` | Crops, tools, objects, trees, buildings, UI pieces |
| `Data/` | Game data (JSON) — reference only; Nosum's rules live in `gameplay/` |

Conventions: 16 px tiles, sheets addressed by index on their fixed grid. Rules:
[`assets-pipeline`](../.claude/skills/assets-pipeline/SKILL.md).
