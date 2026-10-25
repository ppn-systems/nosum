# ADR-007 — Development uses unpacked Stardew Valley content on a 16 px grid

**Status:** Accepted (owner decision, 2026-10-25). Amends ADR-004 D1–D3, ADR-005 D1 and ADR-006 D1.

## Context

The owner chose to develop against the unpacked Stardew Valley `Content` folder (merged into
`assets/` on `master`, commit `8971108`) and to remake the art later. That content is
copyrighted by ConcernedApe; the project is non-commercial, which does not make
redistribution of it permitted. It sets the conventions the engine must follow until the
remake.

## Decision

- **D1 — 16 px grid.** Tiles are 16 × 16 px (replacing ADR-004 D1's 32 px). The canvas is drawn
  at a whole-number scale (×3 or ×4) with smoothing off.
- **D2 — Sprite sheets are used as shipped.** Content PNGs are already sheets indexed on a fixed
  grid (e.g. `assets/Characters/Farmer/farmer_base.png` is 288 × 672 in 16 × 32 frames;
  `assets/Maps/spring_outdoorsTileSheet.png` is a 16 px tile sheet). The engine addresses
  frames by sheet + index instead of the packed atlases of ADR-004 D3; `tools/Nosum.AtlasPacker`
  is kept for Nosum's own art and is not run over `assets/`.
- **D3 — Maps are Tiled `.tmx` files** (`assets/Maps/*.tmx`). The engine needs a TMX reader for
  tile layers (Back, Buildings, Front, AlwaysFront) that map onto ADR-005's layers.
- **D4 — Characters follow the content's layering**: base body, pants, shirts, hairstyles,
  hats and accessories are separate sheets on a 16 × 32 frame (ADR-006's layer stack, at this
  size instead of 64 × 64).
- **D5 — Placeholder only.** Nothing built on this content may be published, shared outside the
  team or deployed publicly. Before any public build, every file from it is replaced by
  Nosum's own art and data; ADR-004's provenance rules apply to the replacements.

## Alternatives considered

- **Nosum's own PixelLab art (32 px)** — legally clean; set aside by the owner for speed.
- **CC0 packs (Kenney, OpenGameArt)** — legally clean, less complete.

## Consequences

- The repository holds copyrighted third-party files; keep it private. A DMCA notice to the
  host can remove the repository.
- The remake must keep the conventions above (or a later ADR changes them) so code written now
  keeps working with the new art.

## Revisit when

The art remake starts, or the project is to be shown or released outside the team.
