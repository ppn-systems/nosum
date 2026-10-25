---
name: assets-pipeline
description: Use when adding, generating (PixelLab), naming or packing art — anything under assets/, wwwroot/assets atlases, sprite names and regions, Wang terrain tiles, loading images in renderer.js. 32 px pixel-art grid, provenance in SOURCES.md, atlases derived from source PNGs.
---

# Assets pipeline

Decision: `docs/adr/ADR-004-pixel-art-assets.md`. Drawing side: `render-interop`.

## Non-negotiables

- **32 × 32 px grid, pixel art.** Tiles are exactly 32 × 32; sprites are multiples of 32 where
  possible (a character may be taller, e.g. 32 × 48, anchored at its feet). Never resize art by a
  non-integer factor; the renderer draws with `imageSmoothingEnabled = false`.
- **Source PNGs go in `assets/<category>/`**, lowercase `snake_case` names:
  `crops/carrot_stage_0.png`, `characters/farmer_walk_south_3.png`. The file name (without
  `.png`) is the sprite name used everywhere else.
- **Every new file gets a line in its folder's `SOURCES.md`**: file, generator (PixelLab tool +
  mode), prompt, date, PixelLab asset id. No file without provenance.
- **Atlases are generated, never hand-edited.** Change the source PNG and re-run the pack tool;
  commit source and atlas together.
- **One look per game.** When generating with PixelLab, pass the same anchor sprite as the style
  reference (for example the farmer's south-facing sprite) so every asset shares palette,
  outline and shading.
- **Names are resolved once.** C# maps sprite names to integer ids at load time; render commands
  carry ids, never strings.
- **No image-processing package outside `tools/`.**

## Generating with PixelLab (MCP)

| Need | Tool |
|:--|:--|
| Terrain pair with autotiling | `create_topdown_tileset` (`tile_size` 32; chain sets with `lower_base_tile_id`) |
| Player / NPC with directions | `create_character` (then `animate_character` for walk, idle, actions) |
| Crop stages, props, items | `create_1_direction_object` or `create_image_pro_flash` with a style image |
| UI icons | `create_ui_asset` |

Generation costs account credits: state what will be generated and roughly how many
generations before starting a batch. Download results into `assets/`, never leave art only
in the PixelLab account.
