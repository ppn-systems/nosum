# ADR-005 — 3/4 top-down view ("2.5D")

**Status:** Accepted (owner decision, 2026-10-25)
**Amended by:** [ADR-007](ADR-007-own-art-16px.md) — 16 px grid, content sheets used as shipped, early content only.

## Context

The game uses the 3/4 top-down view common to farming games: a flat tile grid seen from above at a slant, where characters, crops, trees and
buildings are drawn upright and taller than one tile, so things nearer the bottom of the screen
hide things behind them. This is a 2D technique (no 3D camera, no isometric diamond grid), and
it decides how art is drawn and how the renderer orders sprites.

Only the camera technique is shared with that genre. Nosum's own art direction, setting,
characters, palette and UI must be distinct; no asset, character, location or UI element is
modelled on another game.

## Decision

- **D1 — Projection.** Orthogonal square grid (32 px tiles, [ADR-004](ADR-004-pixel-art-assets.md)),
  camera looking down at a slant. All art is generated with PixelLab's `high top-down` view
  (tilesets in standard mode always use it; characters and objects pass `view: high top-down`).
  No isometric tiles.
- **D2 — Upright sprites anchored at the feet.** Each sprite has an anchor point at its base
  (feet, trunk, building footprint). Its world position is the anchor; the image extends upward
  past its tile.
- **D3 — Layers.** A map is drawn in this order: `ground` (terrain tiles), `ground_decor`
  (tilled soil marks, paths, flat decorations), `world` (characters, crops, trees, objects,
  buildings — depth-sorted), `front` (roof tops, tree crowns and anything always drawn over the
  player), then the UI overlay (Blazor).
- **D4 — Depth sorting by anchor y.** Within `world`, sprites are drawn in ascending anchor y
  (ties broken by x, then by a stable id), so a sprite lower on screen covers one above it.
  Sorting is done in `Nosum.Rendering` without allocation (in-place sort of a preallocated
  array of keys).
- **D5 — Facing.** Characters use 4 movement directions (south, east, north, west); 8-direction rotations are kept as art but only 4 are animated at first.
- **D6 — Collision footprint is separate from the image.** Gameplay collision uses a small
  footprint at the anchor (for a character, part of one tile), not the sprite's bounds.

## Alternatives considered

- **Isometric (diamond) grid** — more "3D" look, but harder tile authoring, picking and
  pathfinding, and not the requested style.
- **Pure top-down (straight down)** — simplest ordering, but characters and buildings read
  poorly and it is not the requested style.

## Consequences

- Every renderable needs an anchor; the atlas JSON or the sprite catalogue must carry it
  (default: bottom-centre of the image).
- Tall objects need a `front` part when the player can walk behind them (tree crown, roof).
- The render front end gains a per-frame depth sort over visible `world` sprites.

## Revisit when

A feature needs real elevation (cliffs the player climbs, multi-storey interiors) that layers
and y-sorting cannot express.
