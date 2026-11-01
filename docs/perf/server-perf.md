# Server performance: load test, database budget, hot-path allocations

This is the measured state of the server on `claude/perf-server`, before and after. It backs the numbers in the PR. The database numbers per request are in
[db-round-trips.md](db-round-trips.md) (phase 3 section); the budget rules are [ADR-035](../adr/ADR-035-database-round-trip-budget.md); the snapshot design is
[ADR-022](../adr/ADR-022-chunked-interest-management.md). No ADR was needed: nothing here changes an architecture or the wire format.

**Headline:** the 50- and 200-client runs pass the M5 tick budget (p99 under 20 ms). **The 500-client run still misses it** (p99 bucket 30 ms before, 50 ms after,
on a 4-core machine shared with the bots). The one database change lowers one hot action's reads (`Disconnect` 1 to 0) and tightens 14 budget limits to what the
code measures. The load-test numbers do not improve, because this branch does not change the snapshot or send path; the run-to-run spread is about as large as
any difference in the table.

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
