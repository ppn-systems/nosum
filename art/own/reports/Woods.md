# Re-authored `Woods.tmx`

Written by `tools/Nosum.MapReauthor`; do not edit by hand.

- Ground cells (`Back` layer, 60 × 32): 1,920 place a tile.
- Cells drawn from the own terrain tileset (PixelLab corner sets): 1,862 (97.0 %).
- Ground cells that keep their placeholder tile index: 58 (9 different tiles); the own sheet of that tileset draws them (floors, walls, paths, stone).
- Every other layer (collision, logic and the drawn building, tree, fence and front layers) is copied unchanged; the tilesheets they name are served as own sheets of the same name, so the same indices draw own art.

| Layer | Cells | Own terrain tileset | Own painted sheet |
| :-- | --: | --: | --: |
| `Back` | 1,920 | 1,862 | 58 |
| `BackEdge` | 217 | 217 | 0 |
| `Buildings` | 723 | 0 | 723 |
| `Front` | 9 | 0 | 9 |
| `AlwaysFront` | 995 | 0 | 995 |

Ground tiles that keep their index (most used first):

| Sheet | Tile | Cells | Why |
| :-- | --: | --: | :-- |
| `spring_outdoorsTileSheet` | 973 | 20 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 998 | 9 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 974 | 6 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 972 | 5 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 999 | 5 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 947 | 4 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 949 | 4 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 948 | 3 | kind other (not terrain) |
| `spring_outdoorsTileSheet` | 1144 | 2 | kind other (not terrain) |
