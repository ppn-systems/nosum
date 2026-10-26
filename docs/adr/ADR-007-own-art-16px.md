# ADR-007 — Nosum's own art pipeline on a 16 px grid

**Status:** Accepted (owner decision, 2026-10-25). Amends ADR-004 D1–D3, ADR-005 D1 and ADR-006 D1. Condensed on 2026-10-26 (M6): the repository
holds only Nosum's own art, and the early art is gone.

## Context

The art has to be drawn on one fixed grid so that the engine can address every frame by sheet and index. Each file must also have a recorded source,
so that nothing in the repository depends on third-party art that Nosum cannot show it has the right to use.

## Decision

- **D1 — 16 px grid.** Tiles are 16 × 16 px. Characters and tall objects use 16 × 32 frames (ADR-006). The canvas is drawn at a whole-number scale
  (×3 or ×4) with smoothing off.
- **D2 — Sprite sheets under `assets/`.** Each file is Nosum's own art or CC0 art. Its source is recorded in
  [`docs/art/provenance.md`](../art/provenance.md). A replacement keeps the sheet's path, pixel size and grid, so the code needs no change. The
  engine addresses frames by sheet and index.
- **D3 — Own maps.** Maps are Tiled `.tmx` files. Their tilesheets are in `art/own/Maps/`, and every one is listed in
  [`art/own/SOURCES.md`](../../art/own/SOURCES.md) with its source.
- **D4 — Build tools.**
  - `tools/art_repack` builds the sheets under `assets/` from CC0 packs and from pictures drawn in code, and writes `provenance.md`.
  - `tools/Nosum.MapGen` writes the own maps: it compiles the text layouts into `.tmx` files, generates the mine floors, and draws the own tilesheets.
  - The PixelLab corner tilesets used for map terrain are kept under `art/own/source/pixellab` with their metadata.
- **D5 — Provenance.** Every file has a recorded source. AI-generated art (PixelLab) is recorded with its prompt, seed and date. PixelLab's
  ownership terms are confirmed before a public release.

## Alternatives considered

- **Own pixel art at 32 px** (PixelLab, drawn by hand): doubles the cell count and changes every table in the code. Rejected for the grid; the
  generators remain sources of art.
- **CC0 packs only:** legally clean but less complete. Used where a pack fits a sheet; not the whole set.

## Consequences

- Any replacement that keeps the sheet's size and grid needs no code change.
- AI-generated art needs a provenance record per file, and its terms are checked before a public release.
- The grid is a hard constraint: a different size needs a new ADR.

## Revisit when

A sheet needs a different grid, PixelLab's terms change, or the owner decides on a public release.
