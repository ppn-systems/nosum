# tools/

Developer tools that run on a developer's machine, never in the game. Solution:
`Nosum.Tools.slnx`. `Directory.Build.props` here turns AOT/trim analysis off, and this is the
only area allowed to use an image-processing package
([ADR-004](../docs/adr/ADR-004-pixel-art-assets.md) D3).

| Project | Does |
| :--- | :--- |
| `Nosum.AssetInventory` | Lists every placeholder file the game loads and the cells it draws, and writes [`docs/art/inventory.md`](../docs/art/inventory.md) ([ADR-040](../docs/adr/ADR-040-own-art-plan.md)). Reads the maps, the catalogues and the sprite tables in `wasm/`. |
| `Nosum.AtlasPacker` | Packs each `<category>/` folder of Nosum's own art into `<category>.png` + `<category>.json` (SkiaSharp). Not used on the placeholder content in `assets/` (ADR-007). |

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
