# Re-authored `Farm.tmx`

Written by `tools/Nosum.MapReauthor`; do not edit by hand.

- Ground cells (`Back` layer, 80 × 65): 5,189 place a tile.
- Cells drawn from the own terrain tileset (PixelLab corner sets): 5,176 (99.7 %).
- Ground cells that keep their placeholder tile index: 13 (5 different tiles); the own sheet of that tileset draws them (floors, walls, paths, stone).
- Every other layer (collision, logic and the drawn building, tree, fence and front layers) is copied unchanged; the tilesheets they name are served as own sheets of the same name, so the same indices draw own art.

| Layer | Cells | Own terrain tileset | Own painted sheet |
| :-- | --: | --: | --: |
| `Back` | 5,189 | 5,176 | 13 |
| `BackEdge` | 493 | 493 | 0 |
| `Buildings` | 1,347 | 0 | 1,347 |
| `Front` | 504 | 0 | 504 |
| `AlwaysFront` | 414 | 0 | 414 |
| `AlwaysFront2` | 16 | 0 | 16 |

Ground tiles that keep their index (most used first):

| Sheet | Tile | Cells | Why |
| :-- | --: | --: | :-- |
| `spring_outdoorsTileSheet` | 217 | 5 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 460 | 3 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 838 | 3 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 509 | 1 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 598 | 1 | kind other (not terrain) |
