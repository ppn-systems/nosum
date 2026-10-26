---
name: assets-pipeline
description: Use when touching anything under assets/ or loading art or maps in the client — sprite sheets, tile indices, Tiled .tmx maps, farmer layer sheets, the 16 px grid, the asset rules.
---

# Assets pipeline

Decisions: `docs/adr/ADR-007-own-art-16px.md` (current), ADR-004/005/006 as amended.

## Non-negotiables

- **`assets/` holds only Nosum's own or CC0 sprite sheets**, at the paths the code loads, each with its
  source in `docs/art/provenance.md`. The early art is gone from the tree
  (ADR-042): never restore it, copy it into another repository or present it as Nosum's art.
  It is in git history only. Code must not depend on anything that cannot be re-created by Nosum's
  own art later (keep conventions, not content).
- **16 × 16 px tiles**, drawn at a whole-number scale with `imageSmoothingEnabled = false`.
- **Sheets are used as shipped** — address frames by sheet + grid index; do not repack them.
  `tools/Nosum.AtlasPacker` is for Nosum's own art only (`--assets <dir>`), not for `assets/`.
- **Maps are Tiled `.tmx`.** Tile layers map to ADR-005 layers: `Back` → ground,
  `Buildings` → world (depth-sorted objects), `Front`/`AlwaysFront` → front.
- **Player characters are layered** (`assets/farmer/`): base, pants, shirts,
  hairstyles, hats, accessories on 16 × 32 frames, composed once per appearance change (ADR-006).
- **Own maps and their tilesheets live in `art/own/`.** Maps are compiled by `tools/Nosum.MapGen` (`layout` for the hosted maps, `mine` for the mine floors) on the own tilesheets that `sheets` draws, and linked to the path the game loads (`art/README.md`); regenerate them with the tool, never edit them by hand. The sprite sheets in `assets/` sit at their own paths and are listed in `docs/art/provenance.md`.
- **Do not edit files in `assets/`.** Derived data goes elsewhere (e.g. a generated index under
  `wasm/`), so the folder can be swapped wholesale when the remake lands.
- **Adding or changing what the game loads from `assets/`** (a map, sheet, sprite-table entry, catalogue sheet) means listing its
  source in `docs/art/provenance.md`; `ArtProvenanceTests` fails otherwise.
