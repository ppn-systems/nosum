# WASM client performance: before and after

Branch `claude/perf-wasm`. Decision record: [ADR-086](../adr/ADR-086-static-tile-chunks-and-lazy-music.md). Tools: [`bench/wasm-perf/`](../../bench/wasm-perf/README.md).
All numbers come from one shared container (headless Chromium from Playwright, the Development server on the same host). They are medians of the runs
named, and the spread is given where it matters. Treat the frame numbers as a relative comparison on this machine, not as device figures.

## Headline

| Measure | Before (`67e9920`) | After | Change |
|:--|--:|--:|--:|
| First load on the wire (5 runs, brotli as deployed) | 6.30 MiB (6,605,477 B) | 4.90 MiB (5,143,139 B) | −22% |
| Requests before playable | 201 | 194 | −7 |
| Frame callback, farm, idle, p50 / p95 (8 s) | 14.7 / 17.9 ms | 10.5 / 13.5 ms | −29% / −25% |
| Frame callback, farm, walking, p50 / p95 (8 s) | 14.3 / 17.7 ms | 11.0 / 13.9 ms | −23% / −21% |
| `drawImage` calls per frame, idle / walking | 475 / 484 | 50 / 62 | −89% |
| Managed allocation per frame, idle / walking | 202 / 428 B | 195 / 330 B | not zero (see below) |
| Frame body allocation per frame, idle / walking (ADR-099) | about 52 / 102 B (per-stage probes) | **0** / 50 B | idle zero, gated in `frame.cjs` |
| Time to playable (median, 5 runs) | 5,271 ms | 5,361 ms | within noise (runs 5,072–5,615 ms) |

Time to playable does not move beyond the noise of this machine: the runs of every build fall between about 4.6 and 5.6 s. The byte count is the
reliable load gain. The frame gain is real on this measure: the same script, the same farm and the same walk, measured on both builds back to back.

## Method

**Environment.** Headless Chromium 1243 from Playwright, 1280 × 720, `vi-VN`, service workers blocked, cold context per run. PostgreSQL 16 and the
Development host (`dotnet run --project server/Nosum.Host`) on `localhost`. The client talks to `ws://localhost:57300/ws/` as it does in development.
The static server (`bench/wasm-perf/serve.cjs`) serves the publish output and, like the deploy, the `.br` or `.gz` sibling the publish writes when the
browser accepts it.

**Load** (`load.cjs`, 5 runs). The number of bytes is the CDP `Network.loadingFinished` encoded size of every response the page fetches before the
HUD stamina meter appears after guest sign-in. Times are the navigation start to the guest button, to the meter, and the first contentful paint. The
sizes of the files on disk are printed too, for reference.

**Frame** (`frame.cjs`, tester build `-p:NosumDevTools=true`, 8 s idle then 8 s walking). The harness signs in as a guest, teleports to the farm
spawn (64, 16), grants a hoe and parsnip seeds, and tills and sows a serpentine block with the action key (the drag-to-sow only reaches tiles beside
the farmer). It then times every `requestAnimationFrame` callback in the page while measuring is on. Two details matter:

- **The readback.** Canvas2D records draw calls and rasterizes them later, outside the callback. Without a flush the callback time does not contain
  the raster, and the first version of this harness reported 3.6–4.8 ms for both builds, which hid the change. The harness now reads one pixel of the
  game canvas at the end of each callback, so the raster is inside the timed number. Both builds are measured the same way.
- **Counts.** `drawImage` and `fillRect` calls are counted by a wrapper on the canvas prototype while measuring is on. Managed allocation is
  `GC.GetAllocatedBytesForCurrentThread()` read through `window.nosumQa.allocatedBytes()`, an export of the tester build only (`DevTools.cs`).

**Profile** (`profile.cjs`). The Chromium CPU profiler over 6 s of walking. It gives the split between the .NET runtime (wasm), the renderer and the
native work. It is a split, not a benchmark.

**Canvas micro-benchmark** (`canvas-micro.cjs`). The same 1296 × 720 screen drawn as 405 tile calls or as six 256 px chunk calls, each frame
followed by the same readback. It shows what a canvas call costs.

**Caveats.** The harness builds its block by walking, and the walk is not deterministic: the clock, the weather and the NPC positions move between
runs, so the screenshots are not pixel-comparable across runs. The number of plots and crops on the farm is not counted; the block is small, so this
is not the "many entities" case the brief asked for. A larger fixture needs a deterministic entity set (see the remaining ideas).

## Results

### Load

| Build | Wire bytes | Requests | First contentful paint | Guest button | Playable (median) |
|:--|--:|--:|--:|--:|--:|
| Baseline `67e9920` | 6.30 MiB | 201 | 80 ms | 2,100 ms | 5,271 ms |
| Audio change only | 4.90 MiB | 194 | 100 ms | 2,023 ms | 4,926 ms |
| Final (audio + chunk cache) | 4.90 MiB | 194 | 100 ms | 2,160 ms | 5,361 ms |

The audio change removes the 11 MB of WAV that the page fetched and decoded before the game started, except the sound effects and loops it still
loads: the eight music tracks wait for their first cue. The chunk cache is a drawing change and sends no bytes. Its build differs only in
JavaScript that does not load before playable.

The music behaviour was checked against the baseline with `tests/browser/music-check.js` on the same server: both builds start the same track
sequence (`unknown(121276)`, then `spring_night`) and both are silent before the first gesture. The check's expected track (`spring_day`) did not
match the server clock at the time of the run, so its `FAIL` is the expectation, not the behaviour; the baseline fails it the same way.

### Frame

Farm, tester build, 1280 × 720, forced readback inside the callback (8 s per phase):

| Build | Phase | Callback p50 | p95 | Max | `drawImage`/frame | Allocation/frame |
|:--|:--|--:|--:|--:|--:|--:|
| Baseline | idle | 14.7 ms | 17.9 ms | 25.5 ms | 475 | 202 B |
| Baseline | walk | 14.3 ms | 17.7 ms | 33.1 ms | 484 | 428 B |
| Chunk cache | idle | 10.5 ms | 13.5 ms | 21.8 ms | 50 | 195 B |
| Chunk cache | walk | 11.0 ms | 13.9 ms | 36.4 ms | 62 | 330 B |

The maximum of the walk is a frame that bakes chunks as the view moves onto new ones (at most four per frame, ADR-086 D3).

Without the readback the same runs read 3.8–4.8 ms (baseline) and 3.6–3.9 ms (chunk cache): the raster was outside the callback, so that number is
not the frame's cost and is not used above.

Why the calls matter: the canvas micro-benchmark draws the same screen as 405 tile calls in 6.6 ms per frame and as six chunk calls in 1.0 ms. The
cost follows the number of canvas calls, not only the pixels they fill. A profile of the baseline (6 s walking, sampling, no readback) puts
`drawImage` at 16% of the samples and the .NET runtime (wasm) at 9%; the chunk-cache build puts `drawImage` at 12% and the runtime at 10%, with
idle time rising because the frame ends sooner.

### What changed

1. **Static tile chunks** (ADR-086 D1–D3). `TileChunkCache` bakes the static tiles of each 16 × 16 chunk into a render target and draws the
   chunk as one image; animated tiles and water ripples are drawn live. Two render commands (`BeginTarget`, `EndTarget`) and the
   `renderer.js` support for them. A pool of 64 targets, at most four bakes a frame, and a tile-by-tile fallback when the budget is spent.
2. **Music on demand** (ADR-086 D4). Music tracks are fetched and decoded the first time a music cue names them; sound effects and loops load at
   start, as before.
3. **Measurement only.** `bench/wasm-perf/` (the scripts above) and an allocation counter in the tester build (`DevTools.AllocatedBytes`, not in
   production).

The first version of the chunk cache was measured with the callback-only timing and showed no gain. Only the readback number showed it; the change
is kept on that basis, and the tests in `tests/Nosum.UnitTests/Engine/TileChunkCacheTests.cs` pin what each frame records.

### Trimming, compression and AOT

| Setting | Framework on disk, brotli | Decision |
|:--|--:|:--|
| Baseline publish | 3,817,740 B (64 files) | — |
| `EventSourceSupport=false` | 3,819,129 B | Not set: +1.4 KB, no gain |
| `EventSourceSupport=false` + `UseSystemResourceKeys=true` | 3,819,129 B | Not set: no gain, and `UseSystemResourceKeys` replaces the framework's exception messages with keys; `Home.razor` shows one of them when a start fails |
| Compression | The publish writes `.br` and `.gz`; the measured 4.90 MiB is what the browser receives as brotli | Already on; unchanged |
| AOT (`NosumWebDeploy=true`) | Not measured here: the `wasm-tools` workload is not installed. ADR-002 D5 holds the earlier measurement: brotli `.wasm` 2.0 MB interpreter against 4.9 MB AOT | Unchanged |
| `InvariantGlobalization` | Not measured: it would remove about 2.6 MB of the ICU shards from the boot set, but the client formats Vietnamese and English through the culture (ADR-010) | Not changed |

A note on method: the first trimming comparison used a publish directory that already held the previous build's fingerprinted files, which inflated
the baseline's total by about 118 KB. Fresh directories give the table above; the change was reverted once that was found.

### Rules checked on the frame path

- **One interop crossing each way per frame.** The frame enters through the `[JSExport]` `Frame` and leaves through one `submitFrame` call. The one
  exception is `UploadLabel`, which uploads a nickname or a label the first time it is drawn (rare, not per frame).
- **`StateHasChanged` on the frame path: none.** The game code does not call it. The view models that raise change events (fishing, map loading,
  the connection badge, the inventory) raise them on a transition, not per frame, and the UI re-renders only on those events.
- **Zero allocation.** The frame body (`GameLoop.Frame`) allocates nothing when idle, from ADR-099 on; walking allocates about 50 bytes a frame on
  average, from node-grid rebuilds on a chunk change. The whole thread still allocates about 116 bytes a frame when idle, outside the frame body (see
  the round 4 section).

## Verification

- `dotnet build engine/Nosum.Engine.slnx -c Release`: 0 warnings, 0 errors. `dotnet build wasm/Nosum.Wasm.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test tests/Nosum.UnitTests`: 3,361 passed, 0 failed, including the new `TileChunkCacheTests` (the static and live parts of a layer together
  cover every tile; a first frame bakes then blits; a later frame only blits; the budget falls back to tiles; an animated tile is live every frame;
  a new epoch bakes again).
- Browser: no page errors on any run of any build (`pageErrors` 0); the farm renders with its terrain, trees and HUD (screenshot checked by eye).
  A pixel-for-pixel comparison of two live frames is not possible, because the clock and the weather move between runs. The chunk path draws the same
  tiles in the same places by construction and by the unit tests; the visual check is the only browser check of the drawing.

## Remaining ideas

Ranked by the evidence they have.

1. **Rain and snow are one canvas call per drop.** About 300 `fillRect` calls a frame while it rains (`PrecipitationLayer`), at roughly the cost of the
   tile calls (about 16 µs each in the micro-benchmark), so likely 3–5 ms of a rainy frame. Drawing all the drops of a frame as one path, with the same
   integer rectangles, keeps the pixels and cuts the calls to one. Not yet measured; needs a rainy run.
2. **Water ripples are drawn live.** A water tile draws its base and up to four strips every frame. Baking the base and drawing only the strips halves
   the calls on a lake. Needs the water share of a real map measured first.
3. **Full-canvas passes.** Every frame fills the whole canvas black, and at dusk and night the ambient pass clears, fills and composites a
   full-canvas layer, then draws the lights. Each is a per-pixel pass. Skipping the black fill where the map covers the canvas changes nothing
   visible, but the cover has to be proven first.
4. **Attribute the remaining allocation, outside the frame body (round 5: the reporter's timer was measured, about 4 bytes; the rest is open).** About 112 bytes a frame when idle, on the same thread between two frames: the
   1 Hz and 10 Hz timers (`PlayerChannel`'s position reporter wakes even when nothing changed), the connection overlay, the socket and Blazor.
   The frame body itself is at zero when idle (ADR-099). The `[JSExport]` entry allocates nothing (measured: 0 bytes a call).
5. **A deterministic farm fixture for "many entities".** The harness's block is small and uncounted. A fixed set of plots, crops and placed objects
   (a bench seed on the server, or a fixture in the tester build) would make the frame numbers comparable across runs and across the next changes.
6. **AOT measured again.** The runtime share in the profile is about 10%; the frame gain from AOT is therefore bounded by that share, but the load
   cost is the larger part (2.0 MB against 4.9 MB brotli). Install `wasm-tools` and measure the frame and the load on the same harness.
7. **Lazy sprite sheets per map.** The farmer and item sheets load at start on every map. Measure what the first map needs before moving them.
8. **Start the runtime earlier.** The guest button appears about 2.1 s after navigation, and the runtime and the boot set are most of the first
   load. A preload hint for `dotnet.native` and the boot assembly is a candidate; not tried.
9. **Plan a WebGL backend.** The per-call cost measured above is the case for a backend that batches draws on the GPU. The command buffer is the
   contract that allows it (ADR-002 D6). A separate ADR.

## Round 4: the frame body allocates nothing when idle (ADR-099)

Decision record: [ADR-099](../adr/ADR-099-zero-allocation-frame-body-and-labels-off-the-player.md). Captures: [`docs/qa/zero-alloc-labels/`](../qa/zero-alloc-labels/README.md).

The frame body is `GameLoop.Frame`, the code the browser runs once per animation frame. A counter around it (`FrameAllocations`, tester build) and the
gate in `bench/wasm-perf/frame.cjs` (`--max-idle-body`, default 0) now measure it. Three causes were found with per-stage probes (temporary, removed):

| Cause | Where | Fix |
|:--|:--|:--|
| `Enum.HasFlag` boxes its argument on the interpreter build; four calls a step | `Walker.Step` (idle: about 52 B a frame) | bit tests (`input & InputState.Right`), `WalkerAllocationTests` |
| The node collision grid rebuilt on a chunk change | `CompositeWalkGrid.NodeGridOf` (walking: about 50 B a frame on average) | not changed: an event, not a per-frame cost (open) |
| The calendar tick rebuilt its strings every second | `CalendarViewModel.Refresh` (about 23 B a frame, between frames) | rebuilt only when the date, the weather or the culture changes |

Also fixed on the way: `renderer.js` declared a second `fitLabel` next to the imported one, so the module did not parse and no frame rendered on master
(ADR-099 D6).

| Measure (bytes a frame; farm, 1280 × 720, `vi-VN`, 8 s idle then 8 s walking) | Before | After |
|:--|--:|--:|
| Frame body, idle | about 52 (probes) | **0** (gate passes) |
| Frame body, walking | about 102 (probes) | 50 |
| Whole thread, idle (`allocatedBytes`) | 192 | 116 |
| Whole thread, walking | 325 | 237 |
| Draw calls, idle / walking | 52 / 60 | 50 / 59 |

The "before" frame-body figures come from the probes on the same farm and walk, before the walker and calendar changes. The whole-thread numbers are gated runs
of the same harness on master (with the `renderer.js` fix only) and on this branch. Measured on a shared container: relative, not device figures.

**Outside the frame body** the thread still allocates about 116 bytes a frame when idle. The calendar part was the one attributed and fixed (about 23).
The rest is on the same thread between two frames (timers, the socket, Blazor), and needs per-source probes (open, item 4 above).

## Round 5: walk grids rebuilt in place, and a position report that sleeps while idle (ADR-100)

Decision record: [ADR-100](../adr/ADR-100-browser-load-guard-and-in-place-walk-grids.md). Same harness and method as round 4 (`bench/wasm-perf/frame.cjs`, tester
build, 1280 × 720, `vi-VN`, the farm, 8 s idle then 8 s walking). Three runs of `master` and three of this branch, run one after the other on the shared container
with the same server; the medians are below, the spread in brackets.

| Measure (bytes a frame) | `master` | This branch | Change |
|:--|--:|--:|--:|
| Whole thread, idle | 116 (116–118) | 112 (112–112) | −4 (−3%) |
| Frame body, idle | 0 | 0 | gate passes |
| Whole thread, walking | 276 (236–286) | 269 (266–282) | within noise |
| Frame body, walking | 91 (51–92) | 25 (22–33) | −73% |
| Callback p50, idle / walking (ms) | 4.1 / 4.1 | 4.1 / 4.0 | within noise |

What changed: the node collision grid and the placed-furniture grid are rebuilt into buffers the client owns instead of a fresh copy of the map's grid on
every chunk change (`NodeField.BuildGridInto`, `PlacedSet.BuildGridInto`, `CompositeWalkGrid`); and the position report waits for a change instead of
ticking at 10 Hz (`PlayerChannel`).

**The idle target is not met.** The whole thread still allocates about 112 bytes a frame when idle; the reporter's timer was about 4 of them. No JavaScript
timer calls into .NET on a schedule, so the rest is on the .NET side (the one-second timers, Blazor, the socket). The next step is per-source probes in the
tester build, not another guess. The walk's remaining frame-body allocation (about 25 bytes a frame) is not the grids and is not attributed.

Verification: `dotnet build` of every solution (engine, gameplay, contracts, server, wasm, bench, tools): 0 warnings, 0 errors. `dotnet test
tests/Nosum.UnitTests`: 3,644 passed, 0 failed (including the new `BuildGridInto` tests). `node --test tests/browser/label-fit/*.mjs
tests/browser/modules/*.mjs`: 40 passed. Browser smoke (`tests/browser/smoke/boot-smoke.cjs`) against the published client and the Development server: no
page or console error, 49 frames, 33 colours on the canvas at the first frame check. The frame gate (`--max-idle-body 0`) passed on every run.

## Reproduce

```bash
export PATH=/root/.dotnet:$PATH
dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -o out/pub            # the deploy build
dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true -o out/qa   # the tester build (frame harness)
DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host -c Release   # PostgreSQL 16 with the dev database
node bench/wasm-perf/serve.cjs out/pub/wwwroot 5080 &  node bench/wasm-perf/serve.cjs out/qa/wwwroot 5081 &
node bench/wasm-perf/load.cjs out/pub/wwwroot http://localhost:5080/ 5 --json load.json
node bench/wasm-perf/frame.cjs "http://localhost:5081/?qa=1" --seconds 8 --json frame.json
node bench/wasm-perf/profile.cjs "http://localhost:5081/?qa=1" --seconds 6
node bench/wasm-perf/canvas-micro.cjs
```
