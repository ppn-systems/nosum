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

## Round 5: walk grids rebuilt in place, and a position report that sleeps while idle (ADR-104)

Decision record: [ADR-104](../adr/ADR-104-browser-load-guard-and-in-place-walk-grids.md). Same harness and method as round 4 (`bench/wasm-perf/frame.cjs`, tester
build, 1280 × 720, `vi-VN`, the farm, 8 s idle then 8 s walking). Three runs of `master` and three of this branch, run one after the other on the shared container
with the same server; the medians are below, the spread in brackets. Measured on `6fc3a41` (this branch's base, before the merge of `master` at `c15f063`).

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

## Round 6: AOT measured, and the idle allocation probed (branch `claude/perf-round3`)

**Result.** AOT (`-p:NosumWebDeploy=true`) is measured and **not adopted**: it nearly doubles the bytes a player downloads for a frame that is already
under budget, and does not get the first game faster. The idle allocation (about 120 bytes a frame, whole thread) is **not removed** in this round. A probe ruled out
the JavaScript-to-.NET call as its source, so it lies in the .NET work that runs between two frames, which this round did not attribute.

The `wasm-tools` workload installed on the cloud image (`dotnet workload install wasm-tools`, .NET 10.0.403 band, about a minute).

### AOT against the interpreter (the same tree, the same harness, one container)

Both builds are the client on `claude/perf-round3` (the same sources; only `NosumWebDeploy` differs). Interpreter: `dotnet publish -c Release`. AOT:
`dotnet publish -c Release -p:NosumWebDeploy=true`. Tester builds add `-p:NosumDevTools=true`.

| Measure | Interpreter (deploy) | AOT (deploy) | Change |
|:--|--:|--:|--:|
| Publish time (wall, one container) | 81 s | 381 s | +4.7× |
| Files in `_framework`, brotli on disk | 3.85 MB | 7.33 MB | +90 % |
| `dotnet.native` brotli (the runtime) | 0.98 MB | 4.91 MB | |
| Wire bytes, first load (median of 5, brotli as served) | 5,181,146 B (4.94 MiB) | 8,659,859 B (8.26 MiB) | **+67 %** |
| Requests before playable | 195 | 195 | 0 |
| First contentful paint (median) | 64 ms | 76 ms | within noise |
| Guest button (median) | 1,704 ms | 1,781 ms | within noise |
| Playable (median of 5; interpreter range 3,961–5,119 ms) | 4,322 ms | 3,977 ms | within noise |
| Frame callback, farm idle, p50 / p95 (8 s) | 7.1 / 9.9 ms | 6.6 / 9.7 ms | −7 % / −2 % |
| Frame callback, farm walking, p50 / p95 (8 s) | 7.2 / 10.4 ms | 6.8 / 9.6 ms | −6 % / −8 % |
| Frame interval (vsync) | 16.7 ms | 16.7 ms | |
| Whole-thread allocation, idle / walking (bytes a frame) | 120 / 256 | 138 / 272 | |
| Frame body allocation, idle / walking (bytes a frame) | 0 / 21.4 | 0 / 28.6 | |

Method: `bench/wasm-perf/load.cjs` (5 runs) and `frame.cjs` (tester build, 8 s idle then 8 s walking, the same farm and walk as round 4), each build served by
`serve.cjs` on its own port, one after the other. The frame callbacks include the one-pixel readback (ADR-086).

**Why not AOT.** The frame gain is 0.4–0.5 ms at p50 and well inside the 16.7 ms vsync budget, where the frame already sits at its interval. The cost is 3.5 MB
more on the wire for every new player, and a 4.7-times longer publish. The interpreter stays the deploy build (ADR-002 D5 stands). The case for AOT returns if a
target device cannot hold the frame budget on the interpreter, which this container does not show.

### Idle allocation: what was probed

The frame body is zero when idle (ADR-099, gated). Whole-thread allocation between frames is about 120 bytes a frame on the interpreter (112 in round 5, the
same figure within the spread of the harness).

Probe (temporary, reverted; not committed): in a tester build, bare `[JSExport]` calls were made from the page, 2,000 each, and the thread's allocated bytes were
read around each loop:

| Call shape | Bytes per call |
|:--|--:|
| No arguments | 0 |
| One `double` | 0 |
| Seven arguments as `Frame` takes them (`double`, four `int`, two `double`, `int`) | 0 |
| The `allocatedBytes()` read itself | 0 |

So the JavaScript-to-.NET call does not allocate, and neither does the frame body. The idle 120 bytes a frame are on the same thread, outside both, between frames.
The candidates are the timers that run while the farm is idle (about ten of them, most at 1 Hz: the calendar, the thirst warning, chat, the trade lock tick, the
connection overlay), the socket's keep-alive, and Blazor's own work. At 1 Hz these would add about 2–4 bytes a frame each, so the 120 bytes need a
callback-level probe to attribute, not an estimate. That probe (count the bytes around each timer callback and each awaited continuation, through a tester export)
is the next step; it was not finished in this round, and nothing was removed.

Verification of this round's WASM work: the probe build and the reverted sources were published; the browser module tests (`node --test tests/browser/label-fit/*.mjs
tests/browser/modules/*.mjs`) pass 40 of 40; no page error in any load run (`pageErrors` 0).

## Round 7: busy scenes at 1920 × 1080, and the first load's maps and font compressed (branch `perf/wasm-server-round5`)

**Result.** The first load on the wire is **14 % smaller** (5,185,201 B to 4,454,046 B, median of 5 runs). The busy scenes lose their sprite-sort and
town-counter allocations: the town's idle frame body falls from 29 B to 5 B a frame. **The frame time did not move beyond the run-to-run noise (about ±0.5 ms
here), and the frame body is not zero in the mine or the town.** The gate in `frame.cjs` still passes on the farm, and the rest is listed below.

### Load: the maps and the font were served raw

The first load fetched about 0.65 MB of Tiled maps before the farm was playable, and the font uncompressed. The deploy serves the `.br` and `.gz` siblings
the publish writes (ADR-031), but the linked maps (`.tmx`, XML text) and the font (`.ttf`) had none, so they crossed the wire raw. The maps are fetched at
start on purpose (the farm's neighbours and the public world-map maps are prefetched, `MapLoader.Prefetch`); only their encoding was the cost.

| Files on the first load (encoded bytes, one run) | Before | After |
|:--|--:|--:|
| `Town.tmx` | 162,117 | 7,522 |
| `Forest.tmx` | 160,170 | 10,811 |
| `Farm.tmx` | 102,804 | 8,028 |
| `Mountain.tmx`, `Beach.tmx`, `Crossroads.tmx`, `Hillside.tmx`, `Woods.tmx` | 225,797 | 16,495 |
| `VT323-Regular.ttf` | 153,366 | 37,574 |
| Whole first load (median of 5) | 5,185,201 B (4.94 MiB), 195 requests, playable median 3,599 ms | 4,454,046 B (4.25 MiB), 195 requests, playable median 3,464 ms |

The change is `wasm/Nosum.Wasm/Nosum.Wasm.csproj`: `StaticWebAssetPublishCompressAllAssets` and `CompressionIncludePatterns` (`**/*.tmx;**/*.ttf`). The SDK compresses
only the patterns it is given, and only the files the project discovers; the linked content is `SourceType=Project`, so it needs the all-assets flag, and the
pattern list still decides what is compressed (the PNG sprites stay as they are). The playable times are within the noise of this machine (3.3 to 3.7 s in
both builds, after a first run of 5.6 s); the byte count is the gain.

**Not done, measured as candidates:**

| Candidate | Wire (brotli) | Why not now |
|:--|--:|:--|
| Drop `System.Private.Xml` (only `TmxReader` uses `XmlReader`) | about 97 KB | Replaces the map parser; needs its own round and the map tests as the safety net |
| Load the rain loop and the thunder on first use (`rain_loop.wav`, `thunder.wav`, as music already is, ADR-086 D4) | about 158 KB | Changes the audio command path in `renderer.js` and `GameAudio`; the first rain would need the load to finish first |
| Map files: `Farm`, `Crossroads` and the other neighbours load at start | about 45 KB now | Too small to matter after compression |

### Frame: busy scenes at 1920 × 1080

Tester build (`-p:NosumDevTools=true`), Chromium, the same harness on both builds, back to back: `frame.cjs --viewport 1920x1080 --scene <scene>`
(8 s idle, then 8 s walking; the busy scenes wait 9 s after the teleport so a map's one-off first frames are over before the measured phases, `--settle`).
One run of each; the callback p50 is the one-pixel readback included (ADR-086). Bytes are the frame body (`GameLoop.Frame`, ADR-099) and the whole thread.

| Scene, phase | Frame body B/frame, master → this PR | Whole thread B/frame, master → this PR | Callback p50 ms, master → this PR |
|:--|--:|--:|--:|
| Farm, idle | 0 → 0 | 114 → 113 | 10.7 → 10.9 |
| Farm, walk | 21.6 → 21.5 | 254 → 273 | 10.6 → 11.2 |
| Town, idle | 29.2 → **5.2** | 196 → 174 | 11.6 → 12.2 |
| Town, walk | 37.4 → 13.2 | 361 → 336 | 12.0 → 12.2 |
| Mine, idle | 58.8 → 29.1 | 517 → 327 | 11.6 → 12.1 |
| Mine, walk | 24.7 → 100.3 (see below) | 894 → 707 | 10.9 → 11.5 |
| Co-op land, idle | 6.0 → 5.9 | 176 → 173 | 10.9 → 10.9 |
| Co-op land, walk | 14.0 → 14.1 | 364 → 366 | 10.8 → 10.5 |

**What changed, and what was attributed.** Per-stage probes (temporary, not committed; each stage bracketed with `GC.GetAllocatedBytesForCurrentThread`,
reported outside the counted frame) found two sources:

1. **The world sprite sort** (`WorldSpriteBatch.Flush`, `Array.Sort` over a struct that implements `IComparable<T>`) allocated about 65 B a frame in the
   mine, on the interpreter build. A comparer object did not stop it; the sort is now an in-place heapsort over the same keys (a total order, so the same
   result). In the probe the `flush` stage went from 64–66 B to 0. It only shows in scenes with enough sprites for the sort's slow path, so it does not appear
   in the farm.
2. **The shop counter lookup** (`ShopRules.TryFindShop`) ran `foreach` over the map's `IReadOnlyList<ShopPoint>` every frame: an enumerator per call, 24–35 B
   in the town. It is an indexed loop now.

**Not changed, and why.**

- **Callback time did not move.** The saved bytes are tens of bytes a frame, and the frame is 10–12 ms of canvas work at 1080p. The draw calls are most of it:
  about 25–60 `drawImage` and 85–150 `fillRect` a frame. The minimap is most of the `fillRect` count: hidden with the M key (`frame.cjs --key KeyM`) the farm
  falls from 119 to 9 fills a frame.
- **Caching the minimap's colour strings** (`renderer.js` builds `rgba(...)` for every coloured rectangle, command −6) was measured in
  `bench/wasm-perf/fill-micro.cjs`: 110 rectangles cost 0.49–0.56 ms a frame with a fresh string per call and 0.49–0.58 ms cached (three rounds each, the
  median of five runs per round). No difference beyond the noise. Not worth a change.
  Baking the minimap into a render target would save the fills themselves (at most about 0.5 ms a frame); it is a candidate, not done.
- **The frame body is not zero in the mine and the town.** Probes in the mine show a steady zero after the sort fix; the 29 B idle figure in the table is
  event-driven: the first frames after a teleport build the map's decorators, and one probe window showed 56 B a frame in the emote bubble decorator (not
  confirmed as steady). The walk figures in the mine
  swing between 25 and 100 B a frame from run to run (the walk crosses chunk boundaries, which rebuild the walk grid, ADR-104), so they are not attributed. The
  co-op land keeps 6 B idle (not attributed).
- **The farm gate passes** (`--max-idle-body 0`): 0 B a frame, idle.

### Verification

- `dotnet build` of `engine/Nosum.Engine.slnx`, `server/Nosum.slnx`, `wasm/Nosum.Wasm.slnx`, `tests/Nosum.Tests.slnx`, `-c Release -warnaserror -m:2`: 0 errors, 0 warnings.
- `dotnet test tests/Nosum.UnitTests --no-build`: 3,784 passed (including `Flush_ManySpritesWithTies_DrawsInTheOrderOfAStableSort`).
- `node --test` over `tests/browser/**/*.test.mjs`: 40 passed.
- `dotnet test tests/Nosum.IntegrationTests --no-build` on PostgreSQL 16: 854 passed, 0 failed (15 min 53 s), including the two offline-push tests.
- Browser smoke (`tests/browser/smoke/boot-smoke.cjs`) against the deploy publish of this branch and the Development server: `ok`, 41 frames, 34 colours,
  no page or console error.

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
