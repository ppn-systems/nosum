# tools/

Developer tools that run on a developer's machine, never in the game. Solution:
`Nosum.Tools.slnx`. `Directory.Build.props` here turns AOT/trim analysis off, and this is the
only area allowed to use an image-processing package
([ADR-004](../docs/adr/ADR-004-pixel-art-assets.md) D3).

| Project | Does |
| :--- | :--- |
| `Nosum.AtlasPacker` | Packs each `<category>/` folder of Nosum's own art into `<category>.png` + `<category>.json` (SkiaSharp). Not used on the placeholder content in `assets/` (ADR-007). |

```bash
dotnet run --project tools/Nosum.AtlasPacker -- --assets <art dir> --out <atlas dir>
dotnet run --project tools/Nosum.AtlasPacker -- --assets <art dir> --out <atlas dir> --check
```

Atlas JSON (format version 1): `image`, `width`, `height`, and `sprites` mapping each sprite
name (the PNG file name) to `{ x, y, w, h }`. Sprites are padded by 2 px; atlas sides are
powers of two; the same input always produces the same atlas.
