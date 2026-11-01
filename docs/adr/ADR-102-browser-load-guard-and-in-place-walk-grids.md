# ADR-102: Browser load guard, in-place walk grids, and a position report that sleeps while the farmer stands still

## Status

Accepted

No wire change: `NosumProtocol.Version` does not move. Builds on [ADR-086](ADR-086-static-tile-chunks-and-lazy-music.md) (the measurement method) and
[ADR-099](ADR-099-zero-allocation-frame-body-and-labels-off-the-player.md) (the zero-allocation frame body, whose "revisit when" items this closes in part).

## Context

On one day `master` shipped a `renderer.js` that did not load in the browser twice: first a use of `LABEL_EDGE` without its import (ADR-098), then a second
`fitLabel` declared next to the imported one (ADR-099 D6). The unit tests passed both times. A module that fails to load leaves a blank game with no
failing test.

ADR-099 left two allocation items open: the node collision grid rebuilt on each chunk change while walking (about 50 bytes a frame on average), and about
116 bytes a frame on the whole thread while idle, outside the frame body.

## Decision

- **D1 — The module load guard is a unit test.** `tests/browser/modules/wwwroot-modules.test.mjs` imports every ES module of
  `engine/Nosum.Rendering.Web/wwwroot` with minimal browser stubs, so a link error (a name not exported) or a parse error (a duplicate declaration) fails
  it. The web host's scripts (`wasm/Nosum.Wasm/wwwroot`) are parsed with `vm.Script`, or imported when they are modules. A text check flags any ALL_CAPS
  constant that a module uses without declaring or importing it: the `LABEL_EDGE` class of failure, which an import cannot see because it fails only when
  its code runs. The check is textual, not a parser, and is tested on its own.
- **D2 — The browser smoke test boots the published client.** `tests/browser/smoke/boot-smoke.cjs` (headless Chromium from Playwright, preinstalled)
  signs in as a guest against the Development server, waits for the HUD meter, and passes when at least three animation frames ran, the canvas holds at
  least two colours, and there was no page error and no console error (a plain resource 404 is ignored). It is run by hand: CI runs no tests today
  (`.github/workflows` holds the deploy and security workflows). `review-gates` and `testing-style` name the commands.
- **D3 — The walk grids are rebuilt in place.** `NodeField.BuildGridInto` and `PlacedSet.BuildGridInto` write the grid of the nodes or of the placed pieces
  into a buffer that `CompositeWalkGrid` owns (one per map size, allocated once). `WalkGrid.CopyBlockedFrom` copies the blocked flags and shares the gates,
  as `Clone` does. The joined grid (nodes and placed pieces together) is keyed on the rebuild counts, not on object identity, because the buffers are reused.
  The map's shared grid is never changed. The copying `BuildGrid` and `BuildGrid(mapGrid)` stay for the server and the tests.
- **D4 — The position report sleeps while the farmer stands still.** `PlayerChannel.ReportLoopAsync` waits on a `SemaphoreSlim` that `Report` releases when the
  state turns dirty, then waits one `ReportInterval` (100 ms) and sends. A standing farmer wakes nothing; a walk still sends at most ten times a second.
  A send while the socket is not ready keeps the pending state and retries each interval. The semaphore is not disposed (a report can release it after
  `Dispose`); the analyzer suppression says why.

## Measurements

Tester build (`-p:NosumDevTools=true`), Chromium 1280 × 720, `vi-VN`, farm, 8 s idle then 8 s walking (`bench/wasm-perf/frame.cjs`), on the shared
container, the same server for every run. Three runs of `master` and three of this branch, one run after the other; medians (the spread is in
[`docs/perf/wasm-perf.md`](../perf/wasm-perf.md), round 5).

| Measure (bytes a frame) | `master` | This branch | Change |
|:--|--:|--:|--:|
| Whole thread, idle | 116 (116–118) | 112 (112–112) | −4 (−3%) |
| Frame body, idle | 0 | 0 | gate passes |
| Whole thread, walking | 276 (236–286) | 269 (266–282) | within noise |
| Frame body, walking | 91 (51–92) | 25 (22–33) | −73% |
| Callback p50, walking (ms) | 4.1 | 4.0 | within noise |

The target of this round was to reduce the idle whole-thread figure of about 117 bytes. It is not met: the cause is not attributed. The measured parts are
small (the reporter's timer about 4 bytes, the rest within noise). No JavaScript timer calls into .NET on a schedule, so the idle allocation is on the
.NET side: the one-second timers (`ChatViewModel`, `ThirstWarning`, `CalendarViewModel`, the connection overlay), Blazor, and the socket. Finding it needs
per-source probes in the tester build, which this round did not add.

## Consequences

- **The load guard catches link and parse failures and the undeclared-constant class.** A name used only on a code path the smoke test never runs (for
  example the quest arrow at boot) is still caught by the textual check, but a runtime error in an undeclared non-constant name is caught only when that
  path runs. The smoke test covers the first frames of a new game.
- **The walk's remaining frame-body allocation (about 25 bytes a frame) is not the grids.** It is elsewhere on the walk; not attributed here.
- **The joined grid is no longer reallocated per rebuild**: its buffer is reused and rebuilt on the same stamps. Whether that is visible in the walk numbers
  depends on the map: the harness's farm has nodes and no furniture, so the joined grid's share is not measured separately.

## Tests

- `tests/browser/modules/wwwroot-modules.test.mjs`: the import and parse tests, the static check on each module, and a self-test of the check. Reintroduced
  into `renderer.js`: a duplicate `fitLabel` declaration fails the import test; a dropped `LABEL_EDGE` import passes the import test and fails the static
  check (a link-time check cannot see a name used only at run time).
- `tests/browser/smoke/boot-smoke.cjs`, against the published client and the Development server: passes; with the duplicate declaration it fails with the
  page's console error.
- `NodeFieldTests.BuildGridInto_*`, `PlacedSetTests.BuildGridInto_MatchesBuildGrid_AndAllocatesNothingOnARebuild`: the in-place build matches the copying
  build tile for tile, and a rebuild allocates nothing.

## Revisit when

- The whole thread allocates nothing when idle (the gate in `frame.cjs` reads the whole thread then).
- The idle allocation is attributed by source, with the one-second timers' share measured.
- The joined grid and the walk's remaining 25 bytes are attributed.
