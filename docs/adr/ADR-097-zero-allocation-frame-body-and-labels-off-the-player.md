# ADR-097: The frame's own work allocates nothing when idle, the calendar text is rebuilt only on change, and world labels keep off the local sprite

## Status

Accepted

Amends [ADR-095](ADR-095-world-labels-clear-the-hud-and-touch-controls.md) (world labels keep off one more box: the local player's sprite) and adds one
render command to the contract of [ADR-002](ADR-002-batched-render-interop.md) (`PlayerBoxTexture`, -13). No wire change: `NosumProtocol.Version` does not move.

## Context

[ADR-096](ADR-096-waypoint-arrow-off-the-hud-and-the-round-three-check.md) left two open items from the round 3 check: the frame still allocated about 17 KB a
second when idle, and the bus stop's direction label sat over the farmer's head beside the farmhouse door (critique finding 5).

[`docs/perf/wasm-perf.md`](../perf/wasm-perf.md) recorded 200–430 managed bytes a frame. The per-stage probes of this round (temporary, removed) found:

- **The frame body** (`GameLoop.Frame` and what it calls) allocated about 52 bytes a frame idle, in `Walker.Step`. `Enum.HasFlag` boxes its argument on
  the interpreter build, and the walker calls it four times a step. Walking, the node grid was rebuilt on each chunk change (`ChunkState`, `ChunkLeft`), a
  few kilobytes an event, about 50 bytes a frame on average over a walk.
- **Outside the frame body** (between two frames, on the same thread) the idle thread allocated about 146 bytes a frame. The calendar tick rebuilt its
  summary and forecast strings every second (about 23 bytes a frame of that). The rest is not attributed here (see Consequences).
- **The bus stop's label** is anchored at the east exit, behind the top-right HUD column. The renderer pushes a label that hits a HUD rectangle to the
  rectangle's left edge, and that edge is the farmer's head at the farm door (`docs/qa/zero-alloc-labels/before`). The label had no rule for the sprite.
- **The farmer's own name plate** is anchored at the feet and is drawn over the sprite by design, so a rule that keeps labels off the sprite must not move it.

## Decision

- **D1 — The frame body is measured and gated.** `GameLoop.Frame` is a wrapper over `FrameWork`; the wrapper reads the thread's allocation counter at each end
  (`FrameAllocations`, two reads, no allocation). The tester build exposes the counter (`window.nosumQa.frameBodyBytes()`), and the frame benchmark
  (`bench/wasm-perf/frame.cjs`) exits 1 when the idle phase's frame body allocates more than `--max-idle-body` bytes a frame (default 0).
- **D2 — No `Enum.HasFlag` on the frame path.** `Walker.Step` tests the held directions with `&`. The results are identical; the server's `MoveValidator` uses
  the walker's constants, not `Step`, so nothing on the server changes. `WalkerAllocationTests` pins that a step allocates nothing.
- **D3 — The calendar text is rebuilt when it changes.** The one-second tick compares the game date, today's and tomorrow's weather and the UI culture with the
  values of the last build, and rebuilds the summary and forecast only when one of them moved. A language change is seen at the next tick.
- **D4 — World labels keep off the local sprite (amends ADR-095).** The scene emits one `PlayerBox` command per frame (`RenderCommandBuffer.PlayerBox`, id -13):
  the sprite's box (16 × 32 view pixels, from the feet) and the feet. The renderer reads it before the labels (`submitFrame`) and stores it after the HUD
  rectangles, so a label counts it as one more box to avoid (`labelAvoidCount` in `label-fit.js`, pure and tested in `tests/browser/label-fit/`). A label
  anchored at the player's feet is the player's own name plate and does not count the sprite. The command draws nothing.
- **D5 — The round three check's grades stand.** The name plate lift (ADR-096 D4) and the waypoint arrow are unchanged.
- **D6 — A fix on master.** `renderer.js` declared its own `fitLabel` next to the imported one (ADR-095 moved the function to `label-fit.js` and left the old
  copy). The module failed to parse, so no frame rendered on master. The local copy is removed and `LABEL_EDGE` is imported where the arrow uses it.

## Measurements

Tester build, Chromium 1280 × 720, `vi-VN`, 8 s idle then 8 s walking, the same farm and walk on both builds (`bench/wasm-perf/frame.cjs`), bytes a frame:

| Measure | Before (master, `renderer.js` fixed) | After |
|:--|--:|--:|
| Frame body, idle (`GameLoop.Frame`) | about 52 (per-stage probes) | **0** (gate passes) |
| Frame body, walking | about 102 (per-stage probes) | 50 (node-grid rebuilds on chunk changes) |
| Whole thread, idle | 192 | 116 |
| Whole thread, walking | 325 | 237 |

The "before" frame-body figures come from the same probes on a build with the walker and the calendar unchanged, so they are an estimate, not a gated run.

## Consequences

- **The frame is allocation-free when idle.** The gate in `frame.cjs` holds it.
- **Walking still allocates about 50 bytes a frame, on average, from node-grid rebuilds** when the farmer crosses a chunk (`CompositeWalkGrid.NodeGridOf`, the
  collision grid, and the joined grid). That is an event, not a per-frame cost, and it is not changed here. The next step is to keep the node grid and rebuild only
  the changed chunk.
- **The whole thread still allocates about 116 bytes a frame when idle**, outside the frame body: the per-second timers, the 10 Hz position reporter
  (`PlayerChannel.ReportLoopAsync`, a `PeriodicTimer` that wakes even when the position did not change), the connection overlay's 1 Hz tick, the socket, and Blazor.
  Only the calendar part (about 23 bytes a frame) was attributed and fixed. The rest needs per-source probes (not done here).
- **The bus stop's label moves to the top-right corner** when the HUD column covers its anchor. It points east, is clear of the sprite and of the HUD, and is
  readable in both languages (`docs/qa/zero-alloc-labels/after/bus-stop-label-{vi,en}-1280x720.png`). Its anchor stays behind the HUD column; a label that
  points at an exit behind the HUD is a placement question for the HUD layout, not for the label rule.
- **The render contract gains one id.** `RenderCommandBuffer` and `renderer.js` change in the same commit (ADR-002). An older `renderer.js` ignores -13 by its
  unknown texture (no draw), so the two would still run side by side, but the label rule needs the new one.
- **The farmer's own name plate may stay over the sprite**, as before (the rule exempts the feet anchor). Other players' plates, drawn on the local sprite, are
  moved off it like any other label.

## Tests

- `WalkerAllocationTests.Step_HeldDiagonalAndIdle_AllocatesNothing` (unit), `RenderingTests.PlayerBox_RecordsOneCommandWithTheBoxAndTheFeet_AndDrawsNothing` (unit).
- `tests/browser/label-fit/label-fit.test.mjs`: `labelAvoidCount` (other label, own name plate, no player), `fitLabel` over the sprite moves off it, and the own
  name plate stays on its sprite (22 tests, `node --test tests/browser/label-fit/label-fit.test.mjs`).
- Browser: `bench/wasm-perf/frame.cjs` (the gate) and `tests/browser/bus-stop-label-capture.cjs` (the captures in `docs/qa/zero-alloc-labels/`).

## Revisit when

- The whole thread allocates nothing when idle (the gate in `frame.cjs` reads the whole thread then, not only the frame body).
- The node grid is kept and only the changed chunk is rebuilt, so walking allocates nothing either.
- A label must point at an exit behind the HUD column: the HUD layout decides where that is, and a label rule cannot.
