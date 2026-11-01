# Zero-allocation frame and world labels: before and after

Companion to [ADR-097](../../adr/ADR-097-zero-allocation-frame-body-and-labels-off-the-player.md) and the round 4 section of
[`docs/perf/wasm-perf.md`](../../perf/wasm-perf.md). Captures of the bus stop's direction label next to the local player at the farm door (critique finding
5), in Vietnamese and English, at 1280x720.

- Harness: [`tests/browser/bus-stop-label-capture.cjs`](../../../tests/browser/bus-stop-label-capture.cjs). A guest signs in, is teleported to the farm door
  front (64, 16), and the page is captured after 3 s. Development host on PostgreSQL, client published with `-p:NosumDevTools=true`, served with `?qa=1`.
- `before/`: the branch before ADR-097's label rule (the renderer fix is in: master does not load without it).
- `after/`: this branch.

| Capture | Before | After | Grade |
|:--|:--|:--|:--|
| `bus-stop-label-vi-1280x720.png` | "→ Trạm xe buýt" drawn on the farmer's head and hammer | the label sits in the top-right corner, clear of the sprite and of the HUD | **Pass**: no overlap with the local sprite |
| `bus-stop-label-en-1280x720.png` | "→ Bus Stop" on the head | "→ Bus Stop" in the top-right corner | **Pass** |

Notes:

- The label's anchor is the east exit, behind the top-right HUD column (the column's rectangle covers x 718–1256). Before, the renderer pushed the label to
  that column's left edge, which is the farmer's head at the door. Now the sprite's box is avoided, and the label takes the next clear spot: the top edge, where
  it points east toward the stop. It is not next to its exit; a label that points at an exit behind the HUD is a HUD layout question (ADR-097 Revisit when).
- The own name plate ("Guest …") is drawn above the farmhouse sign in both builds; the rule exempts the own plate, so it is not moved.
- The guest's number differs between runs ("Guest 1041", "Guest 0602"): a guest name is assigned per sign-in, so the two captures are not pixel-equal.
- Pixel checks of the label's box against the sprite's box are the node tests (`tests/browser/label-fit/label-fit.test.mjs`); the captures are checked by eye.
