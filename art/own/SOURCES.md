# Sources of the own art

One entry per file or group of files, as [ADR-004](../../docs/adr/ADR-004-pixel-art-assets.md) D2 asks.

## `source/pixellab/*.png`, `*.json`

Corner (Wang) tilesets of two terrains each: 16 tiles of 16 × 16 px in a 64 × 64 sheet, with the metadata that names the corners of every tile.

| Field | Value |
| :-- | :-- |
| Made by | PixelLab (`create_topdown_tileset`), generated for Nosum by the project coordinator on 2026-10-25; downloaded from `https://api.pixellab.ai/mcp/tilesets/<id>/image?inline=true` and `.../<id>/metadata` on the same day |
| License | Nosum's own art. PixelLab's terms apply to generated output (https://pixellab.ai/termsofservice); their ownership and commercial-use terms are still to be confirmed before a public release (ADR-004, ADR-040 Consequences). |
| View, style | `high top-down`, 16 px tiles, medium detail, basic shading, no outline |

| File | Tileset id | Prompt (metadata `name`) | Created (UTC) |
| :-- | :-- | :-- | :-- |
| `grass_dirt` | `ba7e9bbf-2b29-4432-94e5-be615f09df4b` | lush green meadow grass, cozy farming game ↗ packed brown dirt path | 2026-10-25 20:48 |
| `water_grass` | `f0cb8cbc-5ef5-4364-9736-f1e3005e8f70` | clear blue pond water ↗ lush green meadow grass | 2026-10-25 20:49 |
| `water_sand` | `d92e4494-3edc-4e69-a040-ef09ae28c5ad` | clear blue pond water ↗ warm pale beach sand | 2026-10-25 20:52 |
| `sand_grass` | `5391cab4-df07-4f86-ad8a-cf5601e07d0a` | warm pale beach sand ↗ lush green meadow grass | 2026-10-25 20:53 |

The `.json` files keep the fields the build needs (id, the full prompt text, style, base tile ids, and each tile's corners and bounding box). What the files really show:

- In `grass_dirt` the corner word `lower` is the dirt and `upper` the grass, the reverse of the description; in the other three it is as described.
- The uniform "all upper" tile of `water_grass` and `sand_grass` is a flat brown placeholder, not grass. The build never draws a uniform tile from them: fills come from the uniform tiles that are real (`grass_dirt` both, `water_grass` water, `sand_grass` sand).
- Each set has a slightly different green. The build moves the grass colour of every set to the mean of `grass_dirt`'s.

Not used yet and not committed: the grass / tilled-soil set (`773ea46a-2e3c-4063-93c5-f32ef0dafdad`, belongs with the soil and crops package) and the grass / plateau-with-rocky-cliff set (`b9dafdfc-6f80-4b01-970b-873082cd4212`, 25 tiles, for the cliff kind: the Farm's cliffs are on its `Buildings` layer, which stays as it is).

## `Maps/own_terrain.png`

Composed by `dotnet run --project tools/Nosum.MapReauthor -- tileset` from the four sets above (`TilesetBuilder.cs` says what is taken from where): fills are the uniform tiles with mirrored variants (half of the water variants without the swirl), the corner tiles are copied unchanged apart from the grass colour. `MapReauthorTests` fails when the committed file is not what the tool writes.

## `Maps/*.tmx`

Own maps are written by `tools/Nosum.MapReauthor` from the placeholder maps in `assets/Maps/`. They keep the placeholder's layout (every layer except the ground, and every
object group, character for character) and name the placeholder sheets for the tiles that are not terrain yet. They are derived from placeholder content, so they are
not releasable until the layers that still draw placeholder tiles are replaced as well (ADR-040 D8). `reports/<Map>.md` lists what still draws a placeholder in the ground layer.
