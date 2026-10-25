# Sources of the own art

One entry per file or group of files, as [ADR-004](../../docs/adr/ADR-004-pixel-art-assets.md) D2 asks.

## `Maps/own_terrain.png`

| Field | Value |
| :-- | :-- |
| Made by | `dotnet run --project tools/Nosum.MapReauthor -- tileset --pack <roguelikeSheet_transparent.png>` (deterministic; `tools/Nosum.MapReauthor/TilesetBuilder.cs` names every piece used) |
| Source | Kenney, "Roguelike/RPG Pack", `Spritesheet/roguelikeSheet_transparent.png` (968 × 526, 16 px tiles, 1 px margin) |
| Page | https://kenney.nl/assets/roguelike-rpg-pack (accessed 2026-10-25) |
| Archive | `kenney_roguelike-rpg-pack.zip`, SHA-256 `8e7d2378f8f794245645f6d7dc7aeeb246791410a7e512293c594b46a5a9524b` |
| Sheet | SHA-256 `808c8d734480bc949b284f3bcc252e87e1ee53b24178791a6838cce6a319c439` |
| License | CC0 1.0 (public domain dedication); the archive's `License.txt` is copied to [`licenses/kenney-roguelike-rpg-pack-CC0.txt`](licenses/kenney-roguelike-rpg-pack-CC0.txt). Credit is not required. |
| Changes | Tiles are copied, mirrored (texture variants) and, for the rounded edge pieces, combined by the tool; no pixel is redrawn by hand. |

The pack is not committed; only the tileset composed from it is. Nothing else was taken from the archive, and nothing in it was run.

## `Maps/*.tmx`

Own maps are written by `tools/Nosum.MapReauthor` from the placeholder maps in `assets/Maps/`. They keep the placeholder's layout (every layer except the
ground, and every object group, character for character) and name the placeholder sheets for the tiles that are not terrain yet. They are derived
from placeholder content, so they are not releasable until the layers that still draw placeholder tiles are replaced as well (ADR-040 D8).
`reports/<Map>.md` lists what still draws a placeholder in the ground layer.
