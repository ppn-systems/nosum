# ADR-086: Static tile chunks baked in the browser, and music fetched on first request

## Status

Accepted

## Context

`bench/wasm-perf` and `docs/perf/wasm-perf.md` measured the client on the farm map in a headless Chromium. Two costs stood out.

- **Frame.** A frame costs about 14.5 ms of main-thread time (p50) with the canvas raster included, against a 16.7 ms budget, and about 480 `drawImage`
  calls. Most of them are the static ground tiles of the visible map, identical from one frame to the next. A micro-benchmark of the same
  screen drawn as 405 tile calls or as six 256 px chunk calls takes 6.6 ms and 1.0 ms: the cost of a canvas call is large, so the number of calls
  matters, not only the pixels.
- **Load.** The client fetches every sound *and every music track* before the game starts. The eight music tracks are uncompressed WAV of about
  0.7 MB each: 5.8 MB of the first load, fetched by a player who hears one or two of them in a session.

The rules that stay fixed: the engine references no browser code (`engine-core`, ADR-001); one interop crossing each way per frame
(`render-interop`, ADR-002 D1–D2); the command buffer is a contract with `renderer.js` that changes on both sides in one commit (ADR-002 D2);
no architecture or stack change without an ADR.

## Decision

- **D1 — A tile layer is drawn in 16 × 16-tile chunks, and the static tiles of a chunk are baked once.** `TileChunkCache`
  (`engine/Nosum.Rendering`) bakes the tiles that never change (no animation, no water ripple) of a chunk into a render target, then draws the
  chunk as one image each frame. The animated tiles and the water ripples of the chunk are still drawn every frame, over it
  (`TileLayerPart.Live`). Ground and front layers both use the cache.
- **D2 — Two new render commands, and the backend creates the bitmaps.** `BeginTarget` (texture id `-10`: target id, width, height) opens a
  render target; the draws until `EndTarget` (`-11`) go into its bitmap, unscaled. A target is then drawn like any texture by its id.
  `renderer.js` creates the bitmap (an `OffscreenCanvas`) on the first begin and resizes it when the size changes; no crossing is added for it. The
  ids come from the same counter as the texture ids (`TextureCache.ReserveId`), so they never collide.
- **D3 — The pool is bounded and the bakes are budgeted.** The scene reserves 64 targets (at most 16 MB of 256 × 256 bitmaps). A frame bakes at
  most four chunks; a chunk that waits for its turn is drawn tile by tile that frame, so the picture is the same and a map change does not spend
  one long frame baking. A bake starts only when the command buffer has room for the whole chunk and its end command, so a target is never left
  open. Bitmaps are keyed by the map's epoch (a change on every content swap), the layer and the tint, and reclaimed least recently used.
- **D4 — Music is fetched when the game first asks for it.** `audio.js` remembers each track's URL at load and fetches and decodes a track the first
  time a music command names it. Until the buffer is decoded the layer is silent, and `syncLayer` starts it on arrival, as it already did for a
  late buffer. Sound effects and ambient loops (small files) still load at start. A failed track is not retried every frame.
- **D5 — No wire change, no new dependency, no runtime setting changed.** The packets and opcodes are untouched. No NuGet or npm package is added.
  The two trimming settings measured in `docs/perf/wasm-perf.md` (`EventSourceSupport`, `UseSystemResourceKeys`) reduced nothing and are not set;
  the publish settings are unchanged.

## Consequences

- Measured in `docs/perf/wasm-perf.md`: the frame's `drawImage` calls fall from about 480 to about 50 on the farm, and the frame's p50 from 14.7 ms
  to 10.5 ms when idle (p95 17.9 to 13.5 ms); the first load carries 4.90 MiB on the wire instead of 6.30 MiB, in 194 requests instead of 201.
- Time to playable is within the run-to-run noise of the shared test machine (about 5.1–5.6 s in every build); the byte count is the reliable gain.
- The first frame on a new map, and each frame that brings new chunks into view, still bakes up to four chunks; the cost is bounded, and a chunk
  that waits is drawn tile by tile for that frame.
- A player's first music cue after start fetches its track. On a slow connection the first music starts a fraction of a second late, and the next
  time the same track is wanted it plays at once. The existing `music-check.js` run gives the same track sequence as the baseline build.
- The render command contract gains two ids. `RenderCommandBuffer` and `renderer.js` change in the same commit; an older `renderer.js` would not
  know the target ids, so the two must ship together, as before (ADR-002).
- Canvas memory grows with the visited map area, bounded by the pool of 64 targets. The pool is one constant in `GameLoop`; on a phone it is the
  value to watch.
- The browser output of the cache is checked by unit tests of the commands it records and by a screenshot; a pixel-for-pixel comparison of two
  live frames is not possible, because the clock and the weather move between runs.

## Alternatives considered

- **Bake the whole map per layer.** Simpler, but the largest map (Town, 130 × 110 tiles) would need a 2080 × 1760 bitmap, about 14.6 MB per
  layer, and the bake would land in one frame. Rejected for chunks.
- **Batch the tile draws in C#.** The cost is per canvas call, and adjacent tiles rarely share a source rectangle, so a C# batch would still be one
  call per tile. Only a baked bitmap removes the calls, which is D1.
- **A WebGL backend.** The right long-term answer for draw calls, and the command buffer is the contract that allows it (ADR-002 D6). Too large for
  this change; it stays a future decision with its own ADR.
- **`InvariantGlobalization` to cut the ICU data.** Would remove about 2.6 MB of the boot set, but the client formats Vietnamese and English numbers
  and dates through the culture (ADR-010). Rejected until every culture-sensitive call is checked.
- **`UseSystemResourceKeys`.** Measured, no reduction; also replaces the framework's exception messages with keys, and `Home.razor` shows one of
  them to the player on a failed start. Rejected.
- **Ahead-of-time compilation.** ADR-002 D5 holds the measurements (brotli `.wasm` 2.0 MB interpreter against 4.9 MB AOT). The `wasm-tools`
  workload is not installed in this environment, so AOT was not re-measured here; this change does not alter that decision.

## References

- ADR-001 (engine separation), ADR-002 (batched render interop, the command contract, AOT measurements), ADR-010 (internationalization), ADR-022
  (chunks of 16 × 16 tiles, the interest unit the chunk size follows), ADR-039 (audio hooks), ADR-070 (mine tints, the layer tint this cache keys on).
- `engine/Nosum.Rendering/TileChunkCache.cs`, `engine/Nosum.Rendering/TileLayerRenderer.cs`, `engine/Nosum.Rendering/RenderCommandBuffer.cs`,
  `engine/Nosum.Rendering.Web/wwwroot/renderer.js` (`submitFrame`, `targetContext`), `engine/Nosum.Rendering.Web/wwwroot/audio.js` (`requestTrack`),
  `wasm/Nosum.Wasm/Game/MapScene.cs` (`DrawTileLayers`), `wasm/Nosum.Wasm/Game/GameLoop.cs`.
- `tests/Nosum.UnitTests/Engine/TileChunkCacheTests.cs`; measurements and method in `docs/perf/wasm-perf.md`.
