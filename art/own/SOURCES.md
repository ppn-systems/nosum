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
| `grass_cliff` | `b9dafdfc-6f80-4b01-970b-873082cd4212` | lush green meadow grass ↗ raised grassy plateau, transition: tall brown rocky cliff face | 2026-10-25 20:54 |

The `.json` files keep the fields the build needs (id, the full prompt text, style, base tile ids, and each tile's corners and bounding box). What the files really show:

- In `grass_dirt` the corner word `lower` is the dirt and `upper` the grass, the reverse of the description; in the other three it is as described.
- The uniform "all upper" tile of `water_grass` and `sand_grass` is a flat brown placeholder, not grass. The build never draws a uniform tile from them: fills come from the uniform tiles that are real (`grass_dirt` both, `water_grass` water, `sand_grass` sand).
- Each set has a slightly different green. The build moves the grass colour of every set to the mean of `grass_dirt`'s.

`grass_cliff` is a 25-tile set of cliff transitions (a face occupies the cell below the plateau edge), downloaded on 2026-10-26 from the same endpoints. Its brown rock tones are the palette of the painted cliff faces (`Material.Rock` in `tools/Nosum.MapReauthor/Material.cs`); its tiles are not placed on a map, because the maps' cliffs are on the gameplay-bearing `Buildings` layer, whose tile positions must not change.

Not used and not committed: the grass / tilled-soil set (`773ea46a-2e3c-4063-93c5-f32ef0dafdad`, belongs with the soil and crops package).

## `Maps/*.tmx`

The mine is written by `tools/Nosum.MapGen` (ADR-042 P3): `Mine.tmx` compiles the hand-drawn `source/layouts/Mine.txt` (`layout Mine`), and the forty floors (`Mines/*.tmx`)
are grown from fixed seeds by `mine all`, on the cave sheet (`cave.png`, drawn in code by `sheets`). Their tiles are the cave tileset's
own: floor variants, rock decor and wall edges; the generated floors name the sheet as `../cave.png`.

Every other hosted map is compiled by `tools/Nosum.MapGen layout` from its own text layout in `source/layouts/` (ADR-042 P4 and P5), on the own sheets below. No hosted map is
re-authored from a placeholder map any more.

## `Maps/cave.png`, `Maps/terrain.png`, `Maps/nature.png`, `Maps/town.png`, `Maps/interior.png`, `Maps/markers.png`

The own tilesheets of [ADR-042](../../docs/adr/ADR-042-own-maps-and-provenance-cleanup.md) D3, drawn in code by tools/Nosum.MapGen sheets from scratch: no painted sheet and no floor plan of any placeholder sheet is read. Each has a trait file in `source/tilesets/<TileSet>.tileset` (hand-written, its tile indices checked by `MapGenSheetsTests`).

| Field | Value |
| :-- | :-- |
| Made by | Drawn in code by tools/Nosum.MapGen sheets (PPN Systems; `tools/Nosum.MapGen/Sheets/`). `sheets --check` fails when a committed picture is not what the code draws |
| Palette | Nosum's own tones, `Palette.cs`: the values of the painted maps' materials (`Material.cs` in `Nosum.MapReauthor`) and the shared outline `(63, 38, 49)`; light from the top left |
| License | Nosum's own art |

| File | Tileset | Tiles | Contents |
| :-- | :-- | :-- | :-- |
| `Maps/cave.png` | `cave` | 48 | Mine floor (0) and wall (1), floor variants, wall edges by open sides, ladder-up and elevator markers, decor |
| `Maps/terrain.png` | `terrain` | 126 | Grass, dirt, sand, cobbled path, cliff, shallow and deep water in four variants each; seven transition pairs of 14 corner tiles |
| `Maps/nature.png` | `nature` | 38 | Oak, maple and pine (2 × 3 tiles each), bushes, rocks, stump, log, fences, corners, gate, hedge, sapling |
| `Maps/town.png` | `town` | 38 | Plaster and timber walls, door, windows, red roof edges, chimney, sign, shop counters, bridge planks and rail, crates, lamp posts, flower box, awnings, barrel, bench, stone step and pillar |
| `Maps/interior.png` | `interior` | 41 | Wood, rug, straw and barn floors; interior walls, window and door; farmhouse bed and table (2 × 2), kitchen, troughs, hay, benches, nest boxes, perch, gate |
| `Maps/markers.png` | `Paths` | 27 | The marks of the `Paths` layer that `NodeSites` reads (trees, weeds, stones, twig, grass, forage); the layer is never drawn |

`source/tilesets/*.tileset` are the trait files of these pictures (`Water`, `Passable`, `Impassable`, `Diggable`), read by `tools/Nosum.MapGen`.

## `source/layouts/*.txt`

Text grids of the own maps, one character per tile with a legend, compiled into `Maps/<Name>.tmx` by `tools/Nosum.MapGen layout` ([ADR-042](../../docs/adr/ADR-042-own-maps-and-provenance-cleanup.md) D4). The `.tmx` is generated; the layout is the source.

| Field | Value |
| :-- | :-- |
| Made by | Designed by PPN Systems for each map, around the gameplay points of [ADR-042](../../docs/adr/ADR-042-own-maps-and-provenance-cleanup.md) D2. Written as text, not traced, mirrored or re-tiled from any placeholder layout |
| Tiles | Only the own tilesets of `Maps/` (`terrain`, `nature`, `town`, `interior`, `cave`, `Paths`) |
| License | Nosum's own work |

| File | Map | Contents |
| :-- | :-- | :-- |
| `source/layouts/Farm.txt` | `Farm.tmx` | The personal farm: fields, a yard, a pond, a fenced field, a cave mouth and the exits to the bus stop, the forest and the backwoods (ADR-042 P5a) |
| `source/layouts/Farm_Foraging.txt` | `Farm_Foraging.tmx` | The co-op land: an open meadow with fields in four unlock bands, a gate to Town and the same exits (ADR-042 P5a) |
