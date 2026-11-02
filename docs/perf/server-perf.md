# Server performance: load test, database budget, hot-path allocations

This is the measured state of the server on `claude/perf-server`, before and after. It backs the numbers in the PR. The database numbers per request are in
[db-round-trips.md](db-round-trips.md) (phase 3 section); the budget rules are [ADR-035](../adr/ADR-035-database-round-trip-budget.md); the snapshot design is
[ADR-022](../adr/ADR-022-chunked-interest-management.md). No ADR was needed: nothing here changes an architecture or the wire format.

**Headline:** the 50- and 200-client runs pass the M5 tick budget (p99 under 20 ms). **The 500-client run still misses it** (p99 bucket 30 ms before, 50 ms after,
on a 4-core machine shared with the bots). The one database change lowers one hot action's reads (`Disconnect` 1 to 0) and tightens 14 budget limits to what the
code measures. The load-test numbers do not improve, because this branch does not change the snapshot or send path; the run-to-run spread is about as large as
any difference in the table.

## Round four (branch `perf/server-send-round4`): the single-push deadline, and what the 500-client tick costs now

**Result: the 500-client tick budget is still not met, and no server change in this round moved it.** Three items were asked for. Item 3 (the send deadline
on single pushes) is done and tested. Item 1 (the tick) was measured and its top cost is inside Nalix's per-send path; no change in this repository was
found that lowers it without a client-visible or Nalix-side change. Item 2 (the idle WASM frame allocation) was attributed and not removed.

### Item 3: the 250 ms deadline on single-recipient pushes (ADR-087 D7)

`PlayerNotifier.TrySendAsync` ran under the caller's token only. It now runs under a 250 ms deadline; a player who misses it is disconnected and the call
returns `false`. Test: `PlayerNotifierDeadlineTests` (stalled player returns false at the deadline and is disconnected; a healthy player stays connected;
the caller's own cancellation does not disconnect). The full unit suite passes (3748/3748). The integration suite could not run locally: the `nosum` role
here may not create databases, and the fixture needs that; CI runs it on a superuser service container.

### Item 1: the 500-client tick (baseline and branch, same machine, same bench)

Bench: `bench/Nosum.Bench.Load`, 500 clients, 60 s ramp, 60 s steady, server and bots on the same 4-core box (the box also ran a WASM benchmark for part of
the session, so the numbers carry that noise). Maps: `art/own/Maps` (the bench's `assets/Maps` default is not in this tree).

| Run | Build | Tick mean | Tick p99 bucket | Allocated per tick | Notes |
|:--|:--|:--|:--|:--|:--|
| 1 | `origin/master` 38296c3 | 12.69 ms | ≤ 30 ms | 1147 KiB | FAIL (budget 20 ms) |
| 1 | this branch | 13.81 ms | ≤ 50 ms | 1165 KiB | FAIL |
| 2 | `origin/master` 38296c3 | 12.38 ms | ≤ 30 ms | 1147 KiB | FAIL |
| 2 | this branch | not reported | not reported | 584 MiB/tick (no tick counted) | see below |
| 3 | `origin/master` 38296c3 | not reported | not reported | 704 MiB/tick (no tick counted) | see below |
| 3 | this branch | not reported | not reported | 578 MiB/tick (no tick counted) | see below |

Runs 2 and 3 (both builds) reported "no snapshot tick ran during the steady phase": the bench's tick count was zero in the window, so the per-tick
allocation there is meaningless. The server logs show no `snapshot.tick_failed` or `loop_faulted`. The cause was not isolated in this session. The one
clean pair (run 1 and run 2 on master) differs by 0.3 ms; the branch run 1 is 1.1 ms higher. Item 3 is not on the snapshot path, so this is not evidence
of a regression, and it is not evidence of none: the spread is larger than the difference this round could show. The budget is not met on either build.

### Item 1: where the tick goes, and what was rejected

- **The send is most of the tick.** From round three's probe: about 63 % of the 500-client tick is each viewer's `SendAsync` (Nalix frames, encrypts, writes),
  about 20 % is per-viewer bookkeeping, about 17 % is preparation. The per-viewer deadline timer costs about 12 % of the allocation and, measured in round
  three, did not lower the tick when removed.
- **Rejected: skip or throttle the 10 Hz snapshot for unchanged viewers.** The client interpolates from `ServerTimeMs` and estimates its clock from arrival
  times (`RemotePlayers.OnSnapshot`, `_clock.Observe`). A gap in the stream changes how remote players are drawn. This is a client-visible behaviour change,
  and it needs an ADR and playtesting, not a server-only patch.
- **Rejected: send in parallel lanes.** Round three measured about 11 % less tick, with two of three long series freezing the tick; the freeze was not
  explained. Its per-lane scratch state is not yet separated from the shared scratch arrays, so it needs its own ADR.
- **Not available: one framed buffer for viewers that share an interest set.** Nalix's public send takes a packet and encrypts per call; a shared buffer
  needs a Nalix API that does not exist yet.

The next lever is a Nalix-side one (a send path that takes pre-framed, pre-encrypted bytes), or an ADR for snapshot cadence. Neither is in this branch.

### Item 2: the idle WASM frame allocation (about 113 B per frame)

Measured with the `allocatedBytes` QA hook and Chromium (idle farm, 8 s, 475 frames): 113 B per frame; the frame body itself is zero when idle. The bytes are
on the same thread between frames. Attribution: the server's 10 Hz snapshot arrives as an inbound WebSocket message, and the Nalix receive, decode and
dispatch allocate about 670 B per message (about 52 KB over 8 s, 160 `setTimeout` callbacks). Making `NearbyPlayers.OnSnapshot` and
`RemotePlayers.OnSnapshot` return immediately left the idle figure at 114 B, so the Nosum handlers are not the source. `MapSnapshot.Deserialize` itself
allocates about 176 B per call (the packet, one `PlayerState`, the list); that part is in Nosum code but is small. Reaching zero needs either an upstream
Nalix change to pool inbound packets, or a server decision to change the snapshot cadence for idle viewers (wire-visible, see ADR-008). Neither was made here.

## Round three: the current master, 3 runs each, and what the send deadline and the pool cap cost (branch `claude/perf-round3`)

Base: `origin/master` at `fc98de0` (no logic change in this branch). Same machine and method as below (4 vCPU shared with the bots, PostgreSQL 16 on
the same host, .NET 10.0.403, Release). Each row is three runs; the tick is the server's own measurement (`nosum_snapshot_tick_seconds` window), the spread is
max minus min. The bots ramp in 15 s (50), 30 s (200) and 60 s (500), then a 60 s steady phase.

**Headline.** The 50 and 200 runs meet the tick budget (p99 bucket ≤ 10 ms). The 500 run does not (p99 bucket 30 ms, mean 14.4 ms). The send deadline and
the pool cap are not what limits the tick (see the table). A watchdog that takes the deadline off the per-send path cut allocations by about 13 %, but made
the tick about 11 % slower in interleaved runs, so it is **not merged**. The per-send cost is inside Nalix, and this repository cannot change it.

**Verdicts are not the same as the tick budget.** The harness passes a run only when 95 % of the bots join and the tick p99 is within budget. At 50 and 200
clients the tick passes and the run still reads FAIL, because only 86 % (50) and 93 % (200) of the bots joined. See "Join failures" below.

| Clients | Tick mean, 3 runs | Mean ± spread | Tick p99 bucket | Server CPU (of 1 core) | Allocated per tick | GC pause | Bots joined | Verdict |
|--:|:--|:--|:--|:--|:--|:--|:--|:--|
| 50 | 1.38, 1.41, 1.19 ms | 1.33 ± 0.22 ms | ≤ 2.5 ms (3/3) | 14, 14, 15 % | 102 ± 3 KiB | 8, 16, 7 ms | 43, 44, 43 of 50 | FAIL (join < 95 %; tick PASS) |
| 200 | 5.31, 5.77, 5.62 ms | 5.57 ± 0.46 ms | ≤ 10 ms (3/3) | 35, 36, 37 % | 395 ± 8 KiB | 55, 69, 60 ms | 186, 188, 187 of 200 | FAIL (join < 95 %; tick PASS) |
| 500 | 14.32, 14.17, 14.65 ms | 14.38 ± 0.48 ms | ≤ 30 ms (3/3) | 65, 66, 71 % | 1,092 ± 19 KiB | 314, 316, 295 ms | 482, 483, 482 of 500 | **FAIL (tick)** |

The 500 run's tick means are 14.38 ± 0.48 ms here, and 13.78 ms (13.18 to 14.20) when the same master was re-run after the other experiments, so the
spread across sessions is about 1 ms. The alloc figure is the server's allocations per snapshot tick (`nosum_gc_allocated_bytes_total` over the window).

### Measurements (each row is a change to one thing against the master runs above)

| Experiment | Clients | Runs | Tick mean | Allocated per tick | Result |
|:--|--:|--:|:--|:--|:--|
| **Pool cap 20 (master)** | 200 | 3 | 5.57 ± 0.46 ms | 395 KiB | baseline |
| Pool cap off (`Maximum Pool Size=100`, Npgsql's default) | 200 | 3 | 6.10 ± 0.64 ms | 394 KiB | no tick difference beyond the spread |
| **Pool cap 20 (master)** | 500 | 3 | 14.38 ± 0.48 ms | 1,092 KiB | baseline |
| Pool cap off | 500 | 3 | 14.95 ± 1.09 ms | 1,100 KiB | no tick difference beyond the spread |
| **Deadline per viewer (master)** | 200 | 3 | 5.57 ± 0.46 ms | 395 KiB | baseline |
| No deadline (no linked source, no timer) | 200 | 3 | 5.95 ± 0.16 ms | 339 KiB | −14 % allocation, tick not lower |
| **Deadline per viewer (master)** | 500 | 3 | 14.38 ± 0.48 ms | 1,092 KiB | baseline |
| No deadline | 500 | 3 | 15.91 ± 1.80 ms | 958 KiB | −12 % allocation; the tick was higher, not interleaved (see below) |
| **Watchdog** (one task checks the viewer in flight every 25 ms; no per-viewer source or timer) | 50 | 3 | 1.37 ± 0.01 ms | 91 KiB | −11 % |
| Watchdog | 200 | 3 | 6.28 ± 1.87 ms | 336 KiB | −15 % |
| Watchdog | 500 | 3 | 16.67 ± 0.30 ms | 949 KiB | −13 % allocation, **tick higher** |
| **Interleaved A/B at 500**: master, watchdog, master, watchdog, master, watchdog | 500 | 3 + 3 | master 13.96 ms (13.64, 13.57, 14.68); watchdog 15.46 ms (15.38, 16.04, 14.96) | 1,089 / 943 KiB | watchdog **~1.5 ms (11 %) slower** per tick |

What this says:

- **The pool cap does not move the tick.** Its job is to bound the connections (ADR-087 D3), and this round did not count `pg_stat_activity` across runs, so
  the connection effect is not measured here. The cap stays.
- **The 250 ms deadline never fired** in any of the 43 load runs of this session (`0 dropped by the server` in every one). Its whole cost is the per-viewer source
  and timer: about 134 KiB of the 1,092 KiB per tick at 500 clients (12 %) and about 56 KiB of 395 KiB at 200 clients.
- **The watchdog is not merged.** It removes the per-viewer allocation and keeps the deadline behaviour (a stalled viewer's send is cancelled and the
  viewer is disconnected; a stalled-send test passes). But the interleaved runs show the tick about 1.5 ms slower, a regression in the number the
  budget is about. The cause was not found. Suspects, none tested: the watchdog's wake-ups and thread-pool scheduling on a 4-core box shared with the bots; the
  cancellation token that every send now shares. The change is reverted from this branch.
- **The no-deadline run was higher as well (15.9 ms), but was not interleaved with master.** It is not evidence either way on its own.

### What the remaining tick is

At 500 clients about 950 KiB is still allocated per tick after the deadline's share is removed: about 1.9 KiB per viewer per tick, against about 260 bytes on the
wire. The code outside `connection.SendAsync` (the interest update, the spawn and despawn lists, the snapshot rows, the `Shown` table) is reused across ticks, so the
allocation is inside the send: Nalix frames, serializes and encrypts each packet per connection. This is an attribution by elimination, not a profile (no profiler is
installed). Earlier timing of the tick (the probe in the section below) put about 63 % of the 500-client tick in the sends, about 20 % in per-viewer
bookkeeping and 17 % in preparation, so the send is the next cost after the sends themselves, and the send is Nalix's.

Two levers exist, and neither is a small change:

1. **Send to several viewers in parallel** (the lanes candidate in the section below). It gave about 11 % less tick in its runs and did not hold the 20 ms
   budget reliably. Now that a stalled viewer is bounded by the deadline, the stall that stopped it earlier is less dangerous, but it still needs its own ADR.
2. **Send one pre-framed, pre-encrypted buffer to viewers that share an interest set.** Viewers in the same chunk see the same snapshot rows. The public Nalix API
   sends a packet, not bytes, and it serializes and encrypts per call (`IPacket.Serialize()` returns a new array), so this needs a Nalix change or an API that does
   not exist yet. Not attempted.

### Join failures

About 12 to 14 % of the bots at 50 clients (15 s ramp), 6 to 7 % at 200 (30 s ramp) and 3 to 4 % at 500 (60 s ramp) failed at the WebSocket connect with
`NetworkException: WebSocket Connection failed: Unable to connect to the remote server`. The failures fall as the ramp gets slower: 50 clients over 60 s joined 48 of 50.
They are not the connection quota: raising `NOSUM_MAX_CONNECTIONS_PER_IP`, `..._PER_WINDOW`, `..._PER_SUBNET` and `..._SUBNET_CONNECTIONS_PER_WINDOW` to 1000
left the 50-client run at 43 of 50. The cause was not isolated (a listen backlog or the handshake accept rate in Nalix are the candidates). The bench now prints one
sample per distinct failure with the frame that threw (`sample` lines in the report), so the next run shows where a bot failed.

### Method for this round

- Load harness: `bench/Nosum.Bench.Load` (unchanged except the failure samples above). Server: `dotnet artifacts/bin/Nosum.Host/release/Nosum.Host.dll` with
  `DOTNET_ENVIRONMENT=Staging NOSUM_LOAD_TEST=1 NOSUM_OPS_HOST=+`, a copy of the binary per variant so a rebuild cannot change a running series.
- The database: PostgreSQL 16 on the same host; a `nosum` role and `nosum_dev` database created for the run (the development password of
  `NosumDatabase.DevelopmentConnectionString`).
- **Rent and return on a cancelled send: not confirmed against Nalix.** The Nalix packages here are binaries with XML docs; its send internals are not
  in the repository and no source was available offline. What this repository does: every snapshot packet is rented in `PushInstanceAsync` with a `using`
  lease (`PacketScope`), so it is returned on every path, including a cancelled or dropped send. Whether Nalix releases its own send buffer on cancellation
  is open; a test would need a real socket and the Nalix pool statistics, and neither is wired in the test suite.
- Machine noise: the same master was re-run after the variants and gave the same mean within 0.6 ms, so the drift across the session is smaller than the
  watchdog difference, which the interleaved pairs confirm.

## Round three: what the pool cap bounds (not measured across runs)

Connection counts across runs were not measured in this round (the server is stopped before the count can be read). The database numbers of ADR-087 D3
stand as they were measured on `claude/perf-server`.

## Server robustness (branch `claude/server-robustness`)

Three server risks found during the perf work, each fixed in code and each with a test that fails without the fix and passes with it. The decisions are
in [ADR-087](../adr/ADR-087-server-leave-send-and-pool-limits.md). No wire change.

| Risk | Before | Fix | Test (fails before, passes after) |
|:--|:--|:--|:--|
| Leave race: the presence record went before the place was saved | `MapPresence.LeaveAsync` removed the record, then the `PlayerLeft` handler saved the place. A reader that saw zero players could read the place before the save landed. | The leave handlers run while the record is still registered; the record is removed in a `finally` after them (ADR-087 D1). | `MapChangeTests.Disconnect_WithASlowSave_TheRecordLeavesPresenceOnlyAfterThePlaceIsSaved`: the save is delayed 300 ms; when the count reaches zero, the saved place must already be there. Failed with "saved not to be null"; passes now. |
| Snapshot send with no deadline: one stalled viewer froze every map's tick | Each viewer's send awaited the server's stop token only. | Each viewer's despawns, spawns and snapshot share one 250 ms deadline; a viewer that misses it is disconnected (ADR-087 D2). | `SnapshotSendDeadlineTests.StalledViewer_DoesNotStopTheSnapshotsOfTheOthers_AndIsDropped`: one viewer's snapshot send never completes. Failed with the test timeout (no snapshot reached the healthy viewer for 20 s); passes now, and the stalled viewer is dropped. |
| Pool growth toward PostgreSQL's `max_connections` = 100 | No pool limit: Npgsql's default of 100 applied. A burst of concurrent work left its connections open. | `Maximum Pool Size = 20` and `Connection Idle Lifetime = 60 s` in `NosumDatabase.CreateOptions`, unless the string sets them (ADR-087 D3). | `ConnectionPoolTests.ConcurrentBurst_LeavesNoMoreOpenConnectionsThanThePoolCap` (60 concurrent queries; counted in `pg_stat_activity`): found 60 open before; at most 20 now. `ConnectDisconnectCycles_DoNotGrowTheOpenConnections`: 100 connect and query cycles add no connections after a burst. |

**The connection leak was not an undisposed context.** Every unit of work created in `Nosum.Infrastructure` is disposed (`await using`); `EfUnitOfWorkFactory.Create()` makes
one context per unit and the callers dispose it. The growth was the pool's size: connections the pool keeps for reuse, with no cap. The cap makes the pool
bounded, and the idle lifetime lets it give connections back.

**What was not measured.** The 20-connection cap is not measured against the 500-client tick: a database call that now queues behind the pool could move the tick,
and the load test is where that shows. The 250 ms deadline is a judgment (2.5 snapshot ticks), not a measured value; a client on a slow network that takes
longer than that to take one snapshot is dropped. The earlier stall of the lanes candidate (above) was not explained: the deadline removes the indefinite
freeze that the code allowed, but it does not show that this was the cause of that stall.

**Caveat on the cancelled send.** A send cancelled at the deadline may still hold the pooled snapshot packet inside the transport. The viewer is disconnected, so the
packet is not reused for that connection; whether the transport releases the packet on cancellation was not checked against Nalix's source.

**Test run.** `dotnet test tests/Nosum.Tests.slnx`: `Nosum.UnitTests` 3417 passed; `Nosum.IntegrationTests` 824 passed, 0 failed (one run earlier failed `MineWireTests` until the membership rule was added, see ADR-087 D1). Server build with `--no-incremental`: 0 warnings, 0 errors.

## Snapshot send at 500 clients (branch `claude/perf-server-send`)

**Result: the 500-client tick p99 is still over the 20 ms budget, and this branch changes no snapshot code.** Three things were measured:
where the 500-client tick goes, the same runs repeated three times at 50, 200 and 500 clients (mean and spread), and one parallel-send candidate
(lanes) that was measured, showed a gain, and was **not** merged because two of its three long series froze the snapshot tick and the cause is not known.
The one shipped change is the fix for a flaky integration test (below).

### Where the 500-client tick goes

A temporary probe (timers around the tick's phases and around each viewer's send, not committed) ran one 500-client steady phase on the same
machine. Mean tick 13.3 ms, about 420 sends per tick:

| Part of the tick | Time per tick | Share |
|:--|--:|--:|
| Each viewer's `_presence.SendAsync` (Nalix: frame, encrypt, socket write) | about 8.3 ms (about 20 µs per send) | about 63 % |
| Per-viewer bookkeeping (interest set, `Shown` table, spawn and despawn lists) | about 2.7 ms | about 20 % |
| Preparation (read every position, chunk moves, sort by chunk) | about 2.2 ms | about 17 % |

The payload is small: 131 KiB per tick over 500 viewers, about 260 bytes each. So the cost is per send, not per byte. Each frame is encrypted with
its connection's key and written to that connection's socket, so the send cannot be shared between viewers. This is why the brief's "serialize
each chunk once and share the bytes" is not the lever: the encoding of about 14 rows per viewer is part of the 20 µs, not most of it. The
public Nalix API offers no way to send bytes that are already framed and encrypted (`IPacket.Serialize()` returns a new array per call).
The per-viewer packet is already one per tick, not one per viewer (`PacketFactory` rents once per tick).

### Repeated runs: before (master, this branch's base), three runs each

Same machine as the table above. Mean tick is the server's own measurement; the spread is max minus min over the three runs.

| Clients | Tick mean (3 runs) | Mean ± spread | Tick p99 buckets (3 runs) | Server CPU (3 runs) | Allocated per tick (3 runs) | GC pause (3 runs) | Verdict |
|--:|:--|:--|:--|:--|:--|:--|:--|
| 50 | 1.19, 1.56, 1.42 ms | 1.39 ± 0.37 ms | ≤2.5, ≤10, ≤5 ms | 14, 14, 13 % | 94, 98, 92 KiB | 0, 0, 19 ms | PASS (3/3) |
| 200 | 4.42, 5.06, 5.23 ms | 4.90 ± 0.81 ms | ≤10, ≤10, ≤10 ms | 24, 37, 38 % | 350, 348, 358 KiB | 8, 16, 19 ms | PASS (3/3) |
| 500 | 13.55, 15.92, 13.29 ms | 14.25 ± 2.63 ms | ≤50, ≤50, ≤30 ms | 69, 78, 70 % | 962, 956, 959 KiB | 92, 94, 257 ms | **FAIL (0/3)** |

### Lanes: a parallel-send candidate, measured but not merged

The candidate split one tick's viewers into up to four lanes (one lane per 32 viewers, never more than the cores), each lane on its own task with its
own snapshot packet, and the lanes sent in parallel. The sends of different viewers are independent (each has its own connection and send lock), and
the per-viewer order (despawns, spawns, snapshot) was unchanged. Its results:

- Nine 500-client runs on the candidate without a stall (two of them were warm-up runs) gave tick means of 11.8 to 13.96 ms, about 12.7 ms on
  average, against 14.25 ms for the base. The p99 bucket was ≤ 20 ms in 2 of the 9 runs and ≤ 30 ms in the rest. **The 20 ms p99 target was not
  reached reliably.**
- The 50-client path is unchanged (below 64 viewers the tick sends inline).
- **A stall.** Two of the three long candidate series (each on one server process, runs in a row) stopped sending snapshots: the snapshot tick
  stopped advancing after the third run in one series and after the seventh in the other, and stayed stopped for the rest of that process's life.
  No snapshots went out at any size while the handlers still answered. The baseline's long series (nine runs on one process) did not stall, which
  is one sample, not a rate. The cause was not found. What the code shows is that a snapshot tick awaits each send with no deadline, so one send
  that never completes freezes every map's snapshots. That hazard is in master too; the candidate may make it more likely, but this was not shown.

The lanes change is therefore **not in this branch**. Before it is retried it needs: a per-send deadline (and a disconnect for a viewer whose send
misses it), a tick watchdog that logs the stuck viewer, and a reproduction of the stall.

### Database connections (a separate finding, not caused by the lanes)

A leak check started one server per binary and ran 50-client runs back to back. Both binaries leave connections in the server's Npgsql pool after a
run: 27 `nosum` connections after the first run on master, 35 after the first run with the candidate. PostgreSQL's `max_connections` is 100 here, and
on master the second run's connection count query was refused with "too many clients already". The server does not cap its pool below that limit.
In one stalled series the PostgreSQL log shows the same refusal at the time of the stall; in the other stalled series it does not, so the exhaustion
is not the only trigger. Whether the exhaustion is what stalls the snapshot tick was not shown; it is a
candidate cause for the stall above, and the first step is to cap the pool (`Maximum Pool Size`) below the server's connection limit and watch the
connection count across runs.

### Flaky test: `MapChangeTests.Disconnect_RightAfterTheMoveOntoAnExit` (the test wait; superseded by the server fix in ADR-087 D1)

The test waited until `PlayerCount` was zero and then read the saved place. `MapPresence.LeaveAsync` removes the player from presence **before** the
`PlayerLeft` handler saves the place (`PlayerService.OnPlayerLeftAsync`). So the count reached zero while the save was still in flight, and for an account
with no saved row the read returned null: "Expected saved not to be null".

- **Reproduced on demand.** With a temporary 300 ms delay before the save (not committed), the original test failed in all four cases
  (`closeAfterMs` 0, 10, 40, 120) with that message. With the same delay, the fixed test passed in all four.
- **Fix (test wait, not a skip).** The test now polls the saved place until the save has landed (`WaitForSavedPlaceAsync`). The cache is updated after
  the commit (`PlayerStateService.WriteAsync`), so the poll sees the row once the save is done. The server is unchanged.
- **Proof of stability.** The fixed test passed in 20 consecutive repeated runs (80 cases), see the PR.

## Method

**Environment.** One container: 4 vCPU, 16 GB RAM, PostgreSQL 16 on the same machine, .NET 10.0.403, Release builds. The server and the bots share the four cores, so
server CPU figures are pessimistic for the server and the tick times include bot load. Single run per row unless said otherwise; no repeats were averaged.

**Load test (`bench/Nosum.Bench.Load`).** N guests join over real WebSockets (ramped over 15 s for 50, 30 s for 200, 60 s for 500), then a 60 s steady phase.
Each bot behaves like a player: about 60% travel to a public map over the exits (the server moves them with its own walker on the maps' walk grids), the rest farm at
home (till, plant, water, harvest with the starter kit), all chat now and then, and every bot reports its feet position 10 times a second. Bots are `move`, `farm`, `chat`
and map travel. **Not in the bot yet: shop purchases and market actions** (the brief asked for shop too; the bot has no shop step, so the load test does not
cover the shop path).

Server-side numbers come from the server's own `/metrics` before and after the steady phase, so they are window deltas:

- snapshot tick time (histogram, bucket upper bounds, so a quantile never understates);
- bytes per snapshot tick and per client per second (before encryption);
- every handler's latency across all opcodes (new: p95 added and buckets summed over opcodes);
- server process CPU, bytes allocated, GC collections by generation, GC pause time and working set (new in this branch: observable instruments in
  `NosumMetrics`, read only when scraped, so they cost nothing on the hot path).

Client-side numbers are the bots' own round trips for `farm` and `chat`.

Commands used (server started with `DOTNET_ENVIRONMENT=Staging NOSUM_LOAD_TEST=1 NOSUM_OPS_HOST=+`, which turns the per-address rate limits off):

```bash
dotnet artifacts/bin/Nosum.Bench.Load/release/Nosum.Bench.Load.dll --clients 50  --ramp 15 --duration 60 --host localhost --maps art/own/Maps
dotnet artifacts/bin/Nosum.Bench.Load/release/Nosum.Bench.Load.dll --clients 200 --ramp 30 --duration 60 --host localhost --maps art/own/Maps
dotnet artifacts/bin/Nosum.Bench.Load/release/Nosum.Bench.Load.dll --clients 500 --ramp 60 --duration 60 --host localhost --maps art/own/Maps
```

**"Before"** is `master` plus only the new observability instruments (no logic change), so the before/after difference is the change in this branch, not the metrics.
**"After"** is this branch.

**Database budget (`DbBudgetTests`).** The integration suite counts every database command per request. Before/after tables come from the same suite with
`NOSUM_DB_BUDGET_DUMP` and `NOSUM_DB_BUDGET_TABLE` set, once with the transfer change stashed and once with it applied. Full test runs: `Nosum.UnitTests` 3011 passed,
`Nosum.IntegrationTests` 762 passed, 0 failed.

**Allocations.** No profiler was available in the container (no `dotnet-trace`/`dotnet-counters`), so allocation work was measured as totals per snapshot tick
(`nosum_gc_allocated_bytes_total` over the window divided by the tick count) and attributed by reading the code. Attribution in the findings below is a reading,
not a profile.

## Load test: before and after

Steady phase 60 s. Tick = one snapshot round over every map instance. Budget: p99 under 20 ms (M5, `bench/README.md`).

| Clients | Run | Tick mean | Tick p50 / p99 (bucket) | Budget | Server CPU (of 1 core) | Allocated / tick | GC gen0 / gen1 per s | GC paused | Snapshot bytes per client per s | Handler p99 (all opcodes) | Bot `farm` p99 | Bot `chat` p99 | Verdict |
|--:|:--|--:|:--|:--|--:|--:|--:|--:|--:|--:|--:|--:|:--|
| 50 | before | 1.40 ms | ≤2.5 / ≤5 | 20 ms | 18 % | 93 KiB | 0.05 / 0.02 | 14 ms | 0.64 KiB | ≤1 ms | 20.4 ms | 4.4 ms | PASS |
| 50 | after | 1.85 ms | ≤2.5 / ≤5 | 20 ms | 19 % | 106 KiB | 0.07 / 0.02 | 21 ms | 0.61 KiB | ≤1 ms | 25.6 ms | 5.0 ms | PASS |
| 200 | before | 6.38 ms | ≤10 / ≤15 | 20 ms | 46 % | 355 KiB | 0.20 / 0.02 | 92 ms | 1.30 KiB | ≤1 ms | 17.7 ms | 5.9 ms | PASS |
| 200 | after | 6.91 ms | ≤10 / ≤15 | 20 ms | 46 % | 405 KiB | 0.23 / 0.02 | 98 ms | 1.43 KiB | ≤1 ms | 16.7 ms | 5.5 ms | PASS |
| 500 | before | 16.58 ms | ≤20 / ≤30 | 20 ms | 82 % | 949 KiB | 0.57 / 0.05 | 401 ms | 2.62 KiB | ≤2.5 ms | 24.8 ms | 12.2 ms | **FAIL** |
| 500 | after | 19.00 ms | ≤20 / ≤50 | 20 ms | 88 % | 1083 KiB | 0.65 / 0.05 | 427 ms | 2.48 KiB | ≤2.5 ms | 23.6 ms | 12.2 ms | **FAIL** |

Other figures from the same runs: all 50, 200 and 500 clients joined with 0 refused and 0 dropped by the server. Snapshots went out at 10 per second per
client in every run. Tick time is the server's own measurement; the bot round trips include the network and the bots' own CPU.

**Reading the table.** The before/after difference is within the spread seen between runs of the same code: three 50-client runs gave tick means of 1.48,
1.40 and 1.85 ms, and the bots' move refusals ranged from 106 to 1,287 per run, because their random walks depend on timing. This branch changes nothing on the
snapshot, move or chat path, so **no load-test improvement is claimed**. The honest result is: the tick at 500 is still over budget, with the same cost profile
as before.

**Where the tick time goes (500 clients, reading the code and the numbers).** Tick time grows with clients times players in view: 1.4 ms at 50, 6.4 ms at 200,
16.6 ms at 500. Bytes per snapshot tick are 131 KiB, about 260 bytes per viewer, so the wire is not the cost. The cost is the per-viewer work in
`PlayerService.PushToViewerAsync`: a snapshot is built and sent to each viewer on its own, one send each, awaited in turn. About 1.9 KiB is allocated per viewer per
tick (948 KiB over 500 viewers), which is roughly seven times the bytes sent. The per-send allocation is inside the Nalix transport (`connection.SendAsync`, with
encryption on), which this repository does not contain, so it is not fixed here (see remaining ideas).

## Database: what this branch changes

Measured with the DbBudget suite (same host, same PostgreSQL, same scenarios):

| Row | Before (reads / writes / tx / SaveChanges) | After | Limit before | Limit after |
|:--|:--|:--|:--|:--|
| `Disconnect` / joined player, socket closed | 1 / 1 / 0 / 1 | **0** / 1 / 0 / 1 | 1 / 1 / 0 / 1 | 0 / 1 / 0 / 1 |
| `BuyListing` / buy one listing | 6 / 2 / 1 / 2 | 6 / 2 / 1 / 2 | 7 / 2 / 1 / 2 | 6 / 2 / 1 / 2 |

The other 13 limits lowered in this branch were already at their measured values on master, so the code did not change them. The full list is in
[db-round-trips.md](db-round-trips.md#phase-3-coin-only-parties-and-tighter-limits-claudeperf-server).

What changed in the code: `TransferService` no longer puts a party with coins and no item operation into the inventory batch, so its inventory is not read.
That is the seller of a listing or a shop sale. When every party is coins only, one empty change is kept, because `ApplyManyAsync` returns before the enlistment
that carries the coin legs whenever it has no change. The budget suite and all 3773 unit and integration tests pass.

Not changed, and why:

- **`BuyListing` at 6 reads** still exceeds ADR-035 D1's "at most 4 reads". Its listing row is read twice (once before the transaction for the price, once inside it
  for the lock). Removing the first read needs the transfer to take the price from the locked row, which changes the transfer's contract. Left as an idea.
- **`TradeConfirm` is not measured.** Both of its rows measure `TooSoon` on master and on this branch, so the swap is never executed by the scenario and its limit
  is not checked. The scenario needs a wait between the confirmations. This is a gap in the test, not a slow path.

## Hot-path allocations

Findings (read from the code, measured by the totals above):

1. **Per-send allocation dominates the snapshot tick.** About 1.9 KiB per viewer per tick at 500 clients, against about 260 bytes on the wire. Confirmed as a
   total, not as a call site: no profiler was available. The send goes to Nalix (`MapPresence.SendAsync` → `connection.SendAsync`, encryption on).
2. **`ChunkStreamer.SendSpawnAsync` allocates a `LookRow` for every spawn** (`LookRow.Create()`), with a comment saying a pooled packet can come back with its nested
   row reset. Spawns happen when a player enters another's view, not every tick. Left as is, because the comment records a real reset bug.
3. **Snapshot scratch arrays are reused** (`_chunkKeys`, `_order`, `_rows`, `_spawns`, `_despawns`, the snapshot packet) and the presence lookups are sorted
   binary searches: no per-tick allocation was found in our own code, apart from the per-viewer `ValueTask` state machines, which are boxed only when a send
   really suspends. Not verified.

No allocation was removed in this branch: the three call sites above are either in Nalix or are deliberate. That is the honest state; the numbers in the table are
the measurement that would show a change.

## Interest management and chunk cost

Interest management (ADR-022) is cheap per movement: `MoveIntent` costs 0 database commands, including across chunk borders (DbBudget), and a chunk join sends only
the chunks that are new to the viewer. The cost that grows is the snapshot fan-out itself, roughly clients times players in view, as above. Not measured here:
a 500-client run where everyone stands in one chunk (the bots spread over the public maps, so the worst case is not in the table).

## Remaining ideas (ranked by expected effect on the 500-client budget)

1. **Reduce the snapshot fan-out.** Options, each needs its own decision: skip the snapshot for a viewer whose view did not change in a tick (but bots move at 10 Hz,
   so this helps less than it sounds); send far players at a lower rate (a wire-visible change, needs an ADR and an append-only check on `MapSnapshot`); share the
   encoded body between viewers in the same chunk set. Measure with the bench at 500 before each.
2. **Find and remove the per-send allocation.** Run the server under `dotnet-trace` (EventPipe, allocation tick) with the 500-client bench to attribute the
   1.9 KiB per send. If it is in Nalix, raise it upstream or pool the send buffers in a packet wrapper here.
3. **Send in parallel across viewers.** The sends of one tick are independent per viewer and currently awaited in turn. Parallel sends use more cores, but the
   allocation of the extra tasks has to be measured against the gain on a shared 4-core box.
4. **`BuyListing` to 4 reads** (ADR-035 D1): take the price from the locked listing row inside the transaction, not from a read before it.
5. **Extend the bench** with shop purchases (`BuyFromShop`) and market listing and buying, so the load test covers the economy paths it does not reach today.
6. **Fix the `TradeConfirm` scenario** to wait out the confirmation cooldown, so its budget is checked.
7. **Exporter type for observable counters.** `PrometheusExporter` types every observable instrument as a gauge that is re-read per scrape. The new
   `nosum_process_cpu_seconds_total`, `nosum_gc_allocated_bytes_total`, `nosum_gc_collections_total` and `nosum_gc_pause_seconds_total` are therefore exposed with
   `# TYPE gauge` although they are counters. Their values are correct; the type label is not. Changing it means giving observable counters their own family
   type, which the exporter does not yet support.
8. **GC settings.** Gen0 collections run at 0.6 per second at 500 clients with 427 ms of pause in 60 s (0.7 % of the time). Server GC or a larger gen0 budget
   were not tried; the pause is small against a 20 ms budget, so this is not the first lever.

## Environment notes (for re-running)

- The test PostgreSQL needs a `nosum` role with the test password, and the host needs a `nosum_dev` database (created with `CREATE ROLE nosum LOGIN SUPERUSER`
  and `CREATE DATABASE nosum_dev OWNER nosum` in the container; not repository changes).
- `dotnet build` puts the host and the bench under `artifacts/bin/<project>/release/`, not under the project's `bin/`.
- The bench needs the maps directory explicitly: `--maps art/own/Maps` (the `assets/Maps` default does not exist in this checkout).

## Round four: three 500-client runs back to back on one process (the snapshot freeze, ADR-087 amendment 2026-11-02)

The report was that the snapshot tick stopped in runs 2 and 3 of back-to-back 500-client runs against one server process. Same machine and bench
settings as round three (`Nosum.Bench.Load --clients 500 --ramp 60 --duration 60 --maps art/own/Maps`, Release, 4 vCPU shared with the bots, PostgreSQL
on the same host). The tick is the server's own count from `nosum_snapshot_tick_duration_ms`; "steady ticks" is the change in that count over the steady phase.

| Build | Run | Steady ticks | Tick mean | Tick p99 bucket | Clients dropped by the server | Snapshots per client |
|:--|:--|:--|:--|:--|:--|:--|
| master (before) | 1 | 600 | 14.19 ms | ≤ 30 ms | 0 | 10.0/s |
| master (before) | 2 | 601 | 15.57 ms | ≤ 30 ms | 0 | 10.0/s |
| master (before) | 3 | 600 | 15.74 ms | ≤ 30 ms | 0 | 10.0/s |
| this branch (after) | 1 | 600 | 14.47 ms | ≤ 30 ms | 0 | 10.0/s |
| this branch (after) | 2 | 600 | 15.12 ms | ≤ 30 ms | 0 | 10.0/s |
| this branch (after) | 3 | 600 | 13.87 ms | ≤ 30 ms | 0 | 10.0/s |

**The freeze did not reproduce with the bench in this environment**: every steady phase ticked 10 times a second before and after the change. The
p99 bucket of 30 ms misses the 20 ms budget in every run, as it did in round three; that is the tick budget, not this change.

**What was reproduced, and where.** A client whose chunk notices or chunk state are never taken froze every map's snapshots. The tick sent those
(`UpdateInterestAsync`) with the server's stop token only, so the send never ended. `SnapshotChurnTests.StuckClientThatNeverTakesItsChunkNotices_DoesNotStopTheSnapshotsOfAnother_AndIsDropped`
fails on master with the other client's snapshots stopped the moment the stuck client joined (`5 snapshots did not arrive within 00:00:10 after the stuck client joined`).
With the change the stuck client is disconnected at its deadline and the other client keeps its snapshots. Whether the bench's freeze is this path was not
shown: the bench's clients read everything they are sent, so this exact stall needs a client that stops reading.

Also seen while running the bench here: its default `--maps` is `assets/Maps`, which does not exist in this repository; the maps are in `art/own/Maps`.
