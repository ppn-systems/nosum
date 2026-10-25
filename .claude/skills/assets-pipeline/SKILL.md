---
name: assets-pipeline
description: Use when touching anything under assets/ or loading art or maps in the client — sprite sheets, tile indices, Tiled .tmx maps, farmer layer sheets, the 16 px grid, the placeholder-content rules.
---

# Assets pipeline

Decisions: `docs/adr/ADR-007-placeholder-content-16px.md` (current), ADR-004/005/006 as amended.

## Non-negotiables

- **`assets/` is placeholder third-party content** (unpacked Stardew Valley). Never publish,
  deploy or share a build containing it; never copy it into another repository; never present it
  as Nosum's art. Code must not depend on anything that cannot be re-created by Nosum's own art
  later (keep conventions, not content).
- **16 × 16 px tiles**, drawn at a whole-number scale with `imageSmoothingEnabled = false`.
- **Sheets are used as shipped** — address frames by sheet + grid index; do not repack them.
  `tools/Nosum.AtlasPacker` is for Nosum's own art only (`--assets <dir>`), not for `assets/`.
- **Maps are Tiled `.tmx`.** Tile layers map to ADR-005 layers: `Back` → ground,
  `Buildings` → world (depth-sorted objects), `Front`/`AlwaysFront` → front.
- **Player characters are layered** (`assets/Characters/Farmer/`): base, pants, shirts,
  hairstyles, hats, accessories on 16 × 32 frames, composed once per appearance change (ADR-006).
- **Do not edit files in `assets/`.** Derived data goes elsewhere (e.g. a generated index under
  `wasm/`), so the folder can be swapped wholesale when the remake lands.
