# Re-authored `BusStop.tmx`

Written by `tools/Nosum.MapReauthor`; do not edit by hand.

- Ground cells (`Back` layer, 65 × 30): 1,943 place a tile.
- Cells drawn from the own terrain tileset (PixelLab corner sets): 1,752 (90.2 %).
- Ground cells that keep their placeholder tile index: 191 (6 different tiles); the own sheet of that tileset draws them (floors, walls, paths, stone).
- Every other layer (collision, logic and the drawn building, tree, fence and front layers) is copied unchanged; the tilesheets they name are served as own sheets of the same name, so the same indices draw own art.

| Layer | Cells | Own terrain tileset | Own painted sheet |
| :-- | --: | --: | --: |
| `Back` | 1,943 | 1,752 | 191 |
| `BackEdge` | 231 | 231 | 0 |
| `Buildings` | 1,334 | 0 | 1,334 |
| `Buildings2` | 18 | 0 | 18 |
| `Front` | 415 | 0 | 415 |
| `AlwaysFront` | 488 | 0 | 488 |
| `AlwaysFront2` | 255 | 0 | 255 |
| `AlwaysFront3` | 102 | 0 | 102 |

Ground tiles that keep their index (most used first):

| Sheet | Tile | Cells | Why |
| :-- | --: | --: | :-- |
| `spring_outdoorsTileSheet` | 1054 | 115 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 1056 | 57 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 1051 | 14 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 1030 | 3 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 1050 | 1 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 1052 | 1 | kind other (not terrain) |
