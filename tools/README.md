# tools/

Developer tools that run on a developer's machine, never in the game. Solution:
`Nosum.Tools.slnx`. `Directory.Build.props` here turns AOT/trim analysis off, and this is the
only area allowed to use an image-processing package
([ADR-004](../docs/adr/ADR-004-pixel-art-assets.md) D3).

| Project | Does |
| :--- | :--- |
| `Nosum.AssetInventory` | Lists every placeholder file the game loads and the cells it draws, and writes [`docs/art/inventory.md`](../docs/art/inventory.md) ([ADR-040](../docs/adr/ADR-040-own-art-plan.md)). Reads the maps, the catalogues and the sprite tables in `wasm/`. |
| `Nosum.AtlasPacker` | Packs each `<category>/` folder of Nosum's own art into `<category>.png` + `<category>.json` (SkiaSharp). Not used on the placeholder content in `assets/` (ADR-007). |
| `Nosum.MapReauthor` | Re-authors the placeholder `.tmx` of every hosted map: the ground layer onto Nosum's own terrain tileset by terrain kind (PixelLab corner sets plus a drawn cobble path), and paints own tilesheets, named like the placeholder sheets, for every other drawn layer ([ADR-040](../docs/adr/ADR-040-own-art-plan.md), [`art/`](../art/README.md)). |
| `art_repack/` | Python 3 standard library only: rebuilds the replaced art under `assets/` from CC0 packs and pictures drawn in code (the farmer layers: `farmer.py`, buildable alone with `--only farmer`), and writes [`docs/art/provenance.md`](../docs/art/provenance.md) ([sourcing](../docs/art/sourcing.md)). Deterministic; `--check` fails when a file on disk differs. |
| `synth_sounds.py` | Python 3 standard library only (no .NET project): synthesises Nosum's placeholder sound effects and the rain loop as 16-bit mono WAV into `wasm/Nosum.Wasm/wwwroot/audio/` ([ADR-039](../docs/adr/ADR-039-audio-hooks.md)). Deterministic; `--check` fails when the committed files differ. |

```bash
python3 tools/art_repack/repack_cc0_art.py --sources <dir with the unzipped packs> [--check] [--docs]   # packs and pins: docs/art/provenance.md
dotnet run --project tools/Nosum.AtlasPacker -- --assets <art dir> --out <atlas dir>
dotnet run --project tools/Nosum.AtlasPacker -- --assets <art dir> --out <atlas dir> --check
```

Atlas JSON (format version 1): `image`, `width`, `height`, and `sprites` mapping each sprite
name (the PNG file name) to `{ x, y, w, h }`. Sprites are padded by 2 px; atlas sides are
powers of two; the same input always produces the same atlas.

**Placeholder content.** The repository no longer holds the Stardew Valley placeholder files once M6 is done ([ADR-040](../docs/adr/ADR-040-own-art-plan.md) D9). The tools that read them read the folder named by `NOSUM_PLACEHOLDER_ROOT`: the `assets/` folder of commit `64ce88e`, for example `git archive 64ce88e assets | tar -x -C <dir>` and `NOSUM_PLACEHOLDER_ROOT=<dir>/assets`. These need it: `Nosum.AssetInventory` (write, `--check`, `--write-fingerprints`), `Nosum.MapReauthor` (`reauthor`, `preview --placeholder`). `--release` does not: it reads only `docs/art/placeholders.sha256` and the repository, and refuses any file under `assets/` that is not listed.

```bash
dotnet run --project tools/Nosum.AssetInventory                      # rewrite docs/art/inventory.md
dotnet run --project tools/Nosum.AssetInventory -- --check           # exit 1 when the committed report is stale
dotnet run --project tools/Nosum.AssetInventory -- --release         # gate: 1 while placeholders remain, 2 on a missing or resized file
dotnet run --project tools/Nosum.AssetInventory -- --write-fingerprints   # once: record hashes in docs/art/placeholders.sha256
```

The report has an *Art* column and replaced-cell counts per package: a file is `replaced` once it differs from its recorded placeholder hash and keeps its size; regenerate the report after replacing a file.
Run the report command after a change that adds a map, a sheet or a sprite table entry; `AssetInventoryTests` names what is out of date.
Never run `--write-fingerprints` after a file was replaced: it would record the replacement as the placeholder.

```bash
dotnet run --project tools/Nosum.MapReauthor -- reauthor all             # every hosted map, its report and the own sheets under art/own/Maps; add --check to fail on a difference
dotnet run --project tools/Nosum.MapReauthor -- reauthor Farm            # one map only: write art/own/Maps/Farm.tmx and art/own/reports/Farm.md from assets/Maps/Farm.tmx
dotnet run --project tools/Nosum.MapReauthor -- reauthor Farm --check    # exit 1 when the committed files differ
dotnet run --project tools/Nosum.MapReauthor -- tileset                  # rebuild art/own/Maps/own_terrain.png from art/own/source/pixellab
dotnet run --project tools/Nosum.MapReauthor -- preview Farm --out farm.png [--placeholder]       # draw a map to a PNG (animations not played)
```

A new map starts with `reauthor <Map>`: the report lists the tiles the kind table does not know yet; add them to `tools/Nosum.MapReauthor/terrain-kinds.csv`
(sheet name without `.png`, local index, kind) and run it again. Tiles of kind `other` (floors, walls, stone) keep their index and are drawn by the painter like every other layer. The own map is linked into `wasm/Nosum.Wasm` and `server/Nosum.Host` as described in `art/README.md`.
