# tools/

Developer tools that run on a developer's machine, never in the game. Solution:
`Nosum.Tools.slnx`. `Directory.Build.props` here turns AOT/trim analysis off, and this is the
only area allowed to use an image-processing package
([ADR-004](../docs/adr/ADR-004-pixel-art-assets.md) D3).

| Project | Does |
| :--- | :--- |
| `Nosum.MapGen` | Compiles Nosum's own map layouts into `.tmx` files and generates the forty mine floors; reads the own tileset data files. Section [Own maps](#own-maps-nosummapgen) below ([ADR-042](../docs/adr/ADR-042-own-maps-and-provenance-cleanup.md) D3, D4). |
| `Nosum.AtlasPacker` | Packs each `<category>/` folder of Nosum's own art into `<category>.png` + `<category>.json` (SkiaSharp). Not used on the early content in `assets/` (ADR-007). |
| `art_repack/` | Python 3 standard library only: rebuilds the replaced art under `assets/` from CC0 packs and pictures drawn in code (the farmer layers: `farmer.py` and the merchants: `npcs.py`, buildable alone with `--only farmer` or `--only npcs`), and writes [`docs/art/provenance.md`](../docs/art/provenance.md) ([sourcing](../docs/art/sourcing.md)). Deterministic; `--check` fails when a file on disk differs. |
| `synth_sounds.py` | Python 3 standard library only (no .NET project): synthesises Nosum's early sound effects and the rain loop as 16-bit mono WAV into `wasm/Nosum.Wasm/wwwroot/audio/` ([ADR-039](../docs/adr/ADR-039-audio-hooks.md)). Deterministic; `--check` fails when the committed files differ. |
| `synth_music.py` | Python 3 standard library only (no .NET project): composes Nosum's own music, eight seamless loops (one per season, day and night), as 8-bit mono WAV into `wasm/Nosum.Wasm/wwwroot/audio/music_*.wav` ([ADR-039](../docs/adr/ADR-039-audio-hooks.md)). Deterministic; `--check` fails when the committed files differ. |

```bash
python3 tools/art_repack/repack_cc0_art.py --sources <dir with the unzipped packs> [--check] [--docs]   # packs and pins: docs/art/provenance.md
dotnet run --project tools/Nosum.AtlasPacker -- --assets <art dir> --out <atlas dir>
dotnet run --project tools/Nosum.AtlasPacker -- --assets <art dir> --out <atlas dir> --check
```

Atlas JSON (format version 1): `image`, `width`, `height`, and `sprites` mapping each sprite
name (the PNG file name) to `{ x, y, w, h }`. Sprites are padded by 2 px; atlas sides are
powers of two; the same input always produces the same atlas.

## Own maps (Nosum.MapGen)

`Nosum.MapGen` writes Nosum's own `.tmx` maps under `art/own/Maps` ([ADR-042](../docs/adr/ADR-042-own-maps-and-provenance-cleanup.md) D3, D4). Nothing in the game
references it. Every layout also goes through the terrain pass (`AutoTerrain.cs`): grass and dirt fills take one of their four variants by a hash of the cell position, and a ground layer `Back2` gets the transition tile of each cell whose corners border a terrain the terrain sheet has a pair for (upper terrain wins a corner; a one-tile run keeps its fill; deep water against land uses the shallows pair). `Back2` carries no traits, so the walk rules do not change. A `.tmx` it writes is generated: edit the source, never the output.

```bash
dotnet run --project tools/Nosum.MapGen -- layout <Name> [--check]   # art/own/source/layouts/<Name>.txt → art/own/Maps/<Name>.tmx
dotnet run --project tools/Nosum.MapGen -- layout all [--check]
dotnet run --project tools/Nosum.MapGen -- mine <floor | all> [--check]   # → art/own/Maps/Mines/<file>.tmx, one to forty
```

`--check` writes nothing and exits 1 when a committed file differs from what the tool writes. Exit 2 means a usage or source error, and the
message names the file and the line. The output depends only on the inputs, so the same input always gives the same bytes.

**Own sheets** (`sheets`, [ADR-042](../docs/adr/ADR-042-own-maps-and-provenance-cleanup.md) P2). The tilesheets of the own maps are drawn in code, deterministically, from `tools/Nosum.MapGen/Sheets/`: `cave`, `terrain`, `nature`, `town`, `interior` and `markers` (the `Paths` node-site marks).

```bash
dotnet run --project tools/Nosum.MapGen -- sheets [--check]                 # → art/own/Maps/<name>.png, one per sheet
dotnet run --project tools/Nosum.MapGen -- sheets --contact <file.png>      # also a picture of every sheet, for judging the art
```

The drawings share `Palette.cs` (tones and five-tone ramps) and `Shading.cs` (tileable smooth noise, mixing, bevels, wood grain, lobed canopies); noise that fills a tile repeats every 16 pixels, so tiles join without a seam. A new sheet is a drawing in `Sheets/` listed in `SheetCatalog.All`, its `.tileset` trait file, and a line in `art/own/SOURCES.md`. The trait files are hand-written; `MapGenSheetsTests` checks that every index they name exists in the picture.

**Tileset data** is `art/own/source/tilesets/<name>.tileset`: one keyword per line, `//` starts a comment, and the name line equals the file name.

```text
name terrain
image terrain.png
tilewidth 16
tileheight 16
columns 16
tilecount 256
tile 0 Diggable
tile 1-3 Passable
tile 4 Water
tile 9 Impassable
```

`image` is the sheet's file, relative to the map's folder, as the `.tmx` names it. A `tile` line is `tile <index or first-last> <trait>...`.
The traits are the ones the rules read: `Water` (`WalkGrid`), `Passable` and `Impassable` (`WalkGrid`: a non-empty `Buildings` tile blocks unless it is
`Passable`; a tile cannot be both) and `Diggable` (`TillGrid`). The compiler writes them into each `<tileset>` of the `.tmx` as the properties
`Water` (`T`), `Passable` (`T` or `F`) and `Diggable` (`T`), the form `TileProperties` reads.

**Layout** is `art/own/source/layouts/<Name>.txt`: one character per tile, a legend, and layered sections.

```text
// a room: Back is the floor and Buildings the walls
legend
a terrain 0
b terrain 1
property Warp 3 5 Town 10 4
layer Back
aaaaaaaa
abbbbbba
layer Buildings
........
.bbbbbb.
```

- A legend line is `<character> <tileset> <tile index>`. `.` is the empty tile and cannot be in the legend. `property <Name> <value>` lines
  (map properties) come before the first `layer`; `layer <Name>` starts a section.
- Each row is one word of characters. Every layer has the same number of rows, and every row the same number of tiles.
- A tileset takes its global ids in the order the legend first names it, so the ids are fixed by the file.
- A layer's rows are read top to bottom; a row may not be spelled like a keyword (`legend`, `layer`, `property`).

**Mine floors** (`mine`). Each floor is grown from a fixed seed as a cave of `Size` × `Size` tiles around its arrival tile in `MineLayouts`: the
arrival and its neighbours are open, the cave is smoothed, and only the part connected to the arrival is kept. The floor is then checked the way the
game reads it, and a seed that fails is tried again, so the tool never writes a floor that fails: the arrival is walkable and lies in a cave of at least
200 tiles; the way up is the one exit, on the arrival tile; the elevator stands on the arrival; and on three days `MineFloorPlanner` places at least six
stones, copper and iron exactly where `MineRules` says, and a ladder under a stone when the floor has one. The floor is dressed from the same seed:
each open cell takes one of the cave tileset's floor variants (tiles 0, 2, 3, 4) or, on about one cell in sixteen, a rock decor tile (35, 39, 45, 46);
the arrival tile carries the way up (the ladder-up tile, or the lit elevator pad on an elevator floor). Each closed cell is `MineGenerator.WallTile`
(index 1) or, next to open ground, the wall edge of its open sides (16 to 31); every wall must not be `Passable`. The floors name the sheet `../cave.png`,
because they live in `Maps/Mines/`. The cave tileset `art/own/source/tilesets/cave.tileset` and its picture `art/own/Maps/cave.png` are drawn by `sheets`
(below); `mine` reads the trait file.
