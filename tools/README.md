# tools/

Developer tools that run on a developer's machine, never in the game. Solution:
`Nosum.Tools.slnx`. `Directory.Build.props` here turns AOT/trim analysis off, and this is the
only area allowed to use an image-processing package
([ADR-004](../docs/adr/ADR-004-pixel-art-assets.md) D3).

| Project | Does |
| :--- | :--- |
| `Nosum.AtlasPacker` | Packs each `assets/<category>/` folder into `wasm/Nosum.Wasm/wwwroot/assets/<category>.png` + `<category>.json` (SkiaSharp) |

```bash
dotnet run --project tools/Nosum.AtlasPacker              # pack every category
dotnet run --project tools/Nosum.AtlasPacker -- --check   # exit 1 if a committed atlas is stale
```

Atlas JSON (format version 1): `image`, `width`, `height`, and `sprites` mapping each sprite
name (the PNG file name) to `{ x, y, w, h }`. Sprites are padded by 2 px; atlas sides are
powers of two; the same input always produces the same atlas.
