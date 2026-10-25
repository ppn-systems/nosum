# Re-authored `Coop.tmx`

Written by `tools/Nosum.MapReauthor`; do not edit by hand.

- Ground cells (`Back` layer, 12 × 10): 120 place a tile.
- Cells drawn from the own terrain tileset (PixelLab corner sets): 0 (0.0 %).
- Ground cells that keep their placeholder tile index: 120 (19 different tiles); the own sheet of that tileset draws them (floors, walls, paths, stone).
- Every other layer (collision, logic and the drawn building, tree, fence and front layers) is copied unchanged; the tilesheets they name are served as own sheets of the same name, so the same indices draw own art.

| Layer | Cells | Own terrain tileset | Own painted sheet |
| :-- | --: | --: | --: |
| `Back` | 120 | 0 | 120 |
| `Buildings` | 65 | 0 | 65 |
| `Front` | 50 | 0 | 50 |

Ground tiles that keep their index (most used first):

| Sheet | Tile | Cells | Why |
| :-- | --: | --: | :-- |
| `coopTiles` | 12 | 63 | kind other (not terrain) |
| `coopTiles` | 53 | 13 | kind other (not terrain) |
| `coopTiles` | 54 | 12 | kind other (not terrain) |
| `coopTiles` | 16 | 4 | kind other (not terrain) |
| `coopTiles` | 33 | 4 | kind other (not terrain) |
| `coopTiles` | 13 | 3 | kind other (not terrain) |
| `coopTiles` | 52 | 3 | kind other (not terrain) |
| `coopTiles` | 15 | 2 | kind other (not terrain) |
| `coopTiles` | 20 | 2 | kind other (not terrain) |
| `coopTiles` | 21 | 2 | kind other (not terrain) |
| `coopTiles` | 23 | 2 | kind other (not terrain) |
| `coopTiles` | 46 | 2 | kind other (not terrain) |
| `coopTiles` | 56 | 2 | kind other (not terrain) |
| `coopTiles` | 18 | 1 | kind other (not terrain) |
| `coopTiles` | 19 | 1 | kind other (not terrain) |
| `coopTiles` | 22 | 1 | kind other (not terrain) |
| `coopTiles` | 25 | 1 | kind other (not terrain) |
| `coopTiles` | 36 | 1 | kind other (not terrain) |
| `coopTiles` | 55 | 1 | kind other (not terrain) |
