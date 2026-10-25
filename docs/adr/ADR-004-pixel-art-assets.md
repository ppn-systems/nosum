# ADR-004 — Pixel-art assets: 32 px grid, source art in `assets/`, atlases built by a repo tool

**Status:** Proposed (owner choices recorded 2026-10-25: pixel art, 32 px tiles, own atlas tool)
**Amended by:** [ADR-007](ADR-007-placeholder-content-16px.md) — 16 px grid, content sheets used as shipped, placeholder only.

## Context

The engine draws sprites from images held on the JS side; C# refers to them by id and region
only ([ADR-002](ADR-002-batched-render-interop.md)). The game needs terrain, crops at several
growth stages, characters with directions and animations, objects and UI icons. Art is
produced with PixelLab (AI pixel-art generation) and must be stored, packed and loaded in a
way that keeps the frame loop free of per-sprite interop.

## Decision

- **D1 — Style and grid.** Pixel art on a 32 × 32 px tile grid. The canvas is scaled by whole
  numbers only and drawn with `imageSmoothingEnabled = false`.
- **D2 — Source art lives in `assets/`.** One PNG per sprite or per animation frame, grouped by
  category (`terrain/`, `crops/`, `characters/`, `objects/`, `ui/`). Each category folder keeps
  a `SOURCES.md` recording, per file: generator (PixelLab tool and mode), prompt, date, and
  PixelLab asset id when there is one. Source PNGs are the source of truth; atlases are derived.
- **D3 — Atlases are built by a repo tool.** A C# tool under `tools/` packs `assets/` into a
  few atlas PNGs plus a JSON region map per atlas (`name → x, y, w, h`, frame lists for
  animations, corner data for Wang tiles). Its output goes to
  `wasm/Nosum.Wasm/wwwroot/assets/` and is committed, so the client builds without running the
  tool. The tool is `tools/Nosum.AtlasPacker` and uses SkiaSharp (MIT); an image-processing
  package is allowed in `tools/` only — never in `engine/`, `gameplay/`, `server/` or `wasm/`.
  `--check` exits 1 when a committed atlas differs from a fresh pack.
- **D4 — Runtime.** At startup `renderer.js` loads each atlas into an `ImageBitmap`; the C# side
  loads the JSON map once and resolves sprite names to integer ids. Render commands carry
  `(textureId, region)` — names are never looked up per frame.
- **D5 — Terrain is a Wang (corner) tileset.** Terrain pairs (e.g. soil ↔ grass, grass ↔
  water) are generated as PixelLab top-down tilesets; their per-tile corner metadata drives
  autotiling in `Nosum.Core`'s tilemap. Static ground is drawn once into an off-screen canvas and
  redrawn only when the map changes.

## Alternatives considered

- **ImageSharp instead of SkiaSharp** — pure managed, but 4.x needs a license key and 3.x has
  open security advisories (fixed only in 4.x).
- **Pack atlases with an external tool (TexturePacker, free-tex-packer)** — no code to
  maintain, but a manual step outside the repo and no control over the JSON format.
- **Build atlases in the browser at load time** — no tool, but slower first load and the
  packing work repeats on every client.
- **Commit only source PNGs and pack in CI** — keeps the repo smaller, but the client then
  cannot build from a clean checkout without the tool.

## Consequences

- Adding art = add PNG + `SOURCES.md` line, run the tool, commit both source and atlas.
- Generated atlases in the repo can go stale; `Nosum.AtlasPacker --check` detects it and should
  run in CI once CI exists.
- PixelLab output is subject to PixelLab's Terms of Service (https://pixellab.ai/termsofservice,
  accessed 2026-10-25); its licence terms must be confirmed before a public release.

## Revisit when

Asset volume makes committed atlases a repository-size problem, or the renderer moves to
WebGL and needs a different texture layout.
