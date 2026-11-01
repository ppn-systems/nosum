# Server performance: load test, database budget, hot-path allocations

This is the measured state of the server on `claude/perf-server`, before and after. It backs the numbers in the PR. The database numbers per request are in
[db-round-trips.md](db-round-trips.md) (phase 3 section); the budget rules are [ADR-035](../adr/ADR-035-database-round-trip-budget.md); the snapshot design is
[ADR-022](../adr/ADR-022-chunked-interest-management.md). No ADR was needed: nothing here changes an architecture or the wire format.

**Headline:** the 50- and 200-client runs pass the M5 tick budget (p99 under 20 ms). **The 500-client run still misses it** (p99 bucket 30 ms before, 50 ms after,
on a 4-core machine shared with the bots). The one database change lowers one hot action's reads (`Disconnect` 1 to 0) and tightens 14 budget limits to what the
code measures. The load-test numbers do not improve, because this branch does not change the snapshot or send path; the run-to-run spread is about as large as
any difference in the table.

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

### Flaky test: `MapChangeTests.Disconnect_RightAfterTheMoveOntoAnExit`

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
