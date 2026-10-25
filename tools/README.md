# tools/

Developer tools that run on a developer's machine, never in the game. Solution:
`Nosum.Tools.slnx`. `Directory.Build.props` here turns AOT/trim analysis off, and this is the
only area allowed to use an image-processing package
([ADR-004](../docs/adr/ADR-004-pixel-art-assets.md) D3).

| Project | Does |
| :--- | :--- |
| `Nosum.AssetInventory` | Lists every placeholder file the game loads and the cells it draws, and writes [`docs/art/inventory.md`](../docs/art/inventory.md) ([ADR-040](../docs/adr/ADR-040-own-art-plan.md)). Reads the maps, the catalogues and the sprite tables in `wasm/`. |
| `Nosum.AtlasPacker` | Packs each `<category>/` folder of Nosum's own art into `<category>.png` + `<category>.json` (SkiaSharp). Not used on the placeholder content in `assets/` (ADR-007). |

| `Nosum.MapReauthor` | Re-authors a placeholder `.tmx` onto Nosum's own terrain tileset by terrain kind, builds that tileset from the own PixelLab corner sets, and reports what still draws a placeholder ([ADR-040](../docs/adr/ADR-040-own-art-plan.md), [`art/`](../art/README.md)). |

| `synth_sounds.py` | Python 3 standard library only (no .NET project): synthesises Nosum's placeholder sound effects and the rain loop as 16-bit mono WAV into `wasm/Nosum.Wasm/wwwroot/audio/` ([ADR-039](../docs/adr/ADR-039-audio-hooks.md)). Deterministic; `--check` fails when the committed files differ. |

```bash
dotnet run --project tools/Nosum.AtlasPacker -- --assets <art dir> --out <atlas dir>
dotnet run --project tools/Nosum.AtlasPacker -- --assets <art dir> --out <atlas dir> --check
```

Atlas JSON (format version 1): `image`, `width`, `height`, and `sprites` mapping each sprite
name (the PNG file name) to `{ x, y, w, h }`. Sprites are padded by 2 px; atlas sides are
powers of two; the same input always produces the same atlas.

```bash
dotnet run --project tools/Nosum.AssetInventory                      # rewrite docs/art/inventory.md
dotnet run --project tools/Nosum.AssetInventory -- --check           # exit 1 when the committed report is stale
dotnet run --project tools/Nosum.AssetInventory -- --release         # gate: 1 while placeholders remain, 2 on a missing or resized file
dotnet run --project tools/Nosum.AssetInventory -- --write-fingerprints   # once: record hashes in docs/art/placeholders.sha256
```

Run the report command after a change that adds a map, a sheet or a sprite table entry; `AssetInventoryTests` names what is out of date.
Never run `--write-fingerprints` after a file was replaced: it would record the replacement as the placeholder.

```bash
dotnet run --project tools/Nosum.MapReauthor -- reauthor Farm            # write art/own/Maps/Farm.tmx and art/own/reports/Farm.md from assets/Maps/Farm.tmx
dotnet run --project tools/Nosum.MapReauthor -- reauthor Farm --check    # exit 1 when the committed files differ
dotnet run --project tools/Nosum.MapReauthor -- tileset                  # rebuild art/own/Maps/own_terrain.png from art/own/source/pixellab
dotnet run --project tools/Nosum.MapReauthor -- preview Farm --out farm.png [--placeholder]       # draw a map to a PNG (animations not played)
```

A new map starts with `reauthor <Map>`: the report lists the tiles the kind table does not know yet; add them to `tools/Nosum.MapReauthor/terrain-kinds.csv`
(sheet name without `.png`, local index, kind) and run it again. The own map is linked into `wasm/Nosum.Wasm` and `server/Nosum.Host` as described in `art/README.md`.
