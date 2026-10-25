# Re-authored `FarmHouse.tmx`

Written by `tools/Nosum.MapReauthor`; do not edit by hand.

- Ground cells (`Back` layer, 12 × 12): 144 place a tile.
- Cells drawn from the own terrain tileset (PixelLab corner sets): 0 (0.0 %).
- Ground cells that keep their placeholder tile index: 144 (9 different tiles); the own sheet of that tileset draws them (floors, walls, paths, stone).
- Every other layer (collision, logic and the drawn building, tree, fence and front layers) is copied unchanged; the tilesheets they name are served as own sheets of the same name, so the same indices draw own art.

| Layer | Cells | Own terrain tileset | Own painted sheet |
| :-- | --: | --: | --: |
| `Back` | 144 | 0 | 144 |
| `Buildings` | 53 | 0 | 53 |
| `Front` | 63 | 0 | 63 |

Ground tiles that keep their index (most used first):

| Sheet | Tile | Cells | Why |
| :-- | --: | --: | :-- |
| `walls_and_floors` | 352 | 25 | kind other (not terrain) |
| `walls_and_floors` | 353 | 25 | kind other (not terrain) |
| `walls_and_floors` | 336 | 20 | kind other (not terrain) |
| `walls_and_floors` | 337 | 20 | kind other (not terrain) |
| `farmhouse_tiles` | 0 | 16 | kind other (not terrain) |
| `walls_and_floors` | 0 | 10 | kind other (not terrain) |
| `walls_and_floors` | 16 | 10 | kind other (not terrain) |
| `farmhouse_tiles` | 64 | 9 | kind other (not terrain) |
| `walls_and_floors` | 470 | 9 | kind other (not terrain) |
