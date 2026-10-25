# assets/

Source art for Nosum: pixel art on a 32 × 32 px grid
([ADR-004](../docs/adr/ADR-004-pixel-art-assets.md)). These PNGs are the source of truth; the
atlases the client loads are generated from them into `wasm/Nosum.Wasm/wwwroot/assets/`.

| Folder | Holds |
| :--- | :--- |
| `terrain/` | Wang tilesets for ground pairs (soil ↔ grass, grass ↔ water, …) |
| `crops/` | One PNG per crop per growth stage |
| `characters/` | Player and NPC sprites: one PNG per direction and animation frame |
| `objects/` | Fences, buildings, tools, decorations |
| `ui/` | Item and HUD icons used by the Blazor overlay |

Each folder keeps a `SOURCES.md` with one line per file: generator (PixelLab tool and mode),
prompt, date, PixelLab asset id. Rules: [`assets-pipeline`](../.claude/skills/assets-pipeline/SKILL.md).

Current art: terrain (soil ↔ grass, water ↔ grass) and a placeholder player character.
