# Chunk baking: before and after, farm states at 1280 × 720

`before-*` is origin/master (tester build, no chunk baking). `after-*` is the merged branch with `TileChunkCache`. `side-*` puts them side by side.
Captured by `bench/wasm-perf/capture.cjs` (tilled and sown soil; a dry day; a season change; a map transition to the town), composed by `bench/wasm-perf/compose.cjs`.

Each run starts from a freshly restarted Development server with `NOSUM_CALENDAR_EPOCH=2026-03-13T15:45:00Z`, so both sides start at the same game time (the in-game clock reads spring 18, 15:28 before and 15:34 after, and the later states follow).

The earlier captures shared one server clock, so the second run began after the first had advanced the calendar; those frames were discarded.

What to look at: the tilled soil and its outline, the thirsty marker on the dry day, the season banner and the crops, and the map and minimap after the transition. The HUD and the animals match in every pair.
