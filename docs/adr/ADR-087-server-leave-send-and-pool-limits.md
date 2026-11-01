# ADR-087: The leave saves before the record goes, a viewer's snapshot send has a deadline, and the database pool is capped

## Status

Accepted

## Context

The perf work on `claude/perf-server` (`docs/perf/server-perf.md`, PR #280 and #283) found three server risks that are not performance numbers but can
lose data, freeze the world, or exhaust the database.

- **Leave race.** `MapPresence.LeaveAsync` removed the player's presence record first, and the `PlayerLeft` handler saved the place afterwards. A player
  who disconnected right after stepping onto an exit was therefore gone from presence while the save was still in flight. A reconnect in that window
  read the database copy, which did not yet hold the place (the in-memory copy, `RecentPlaces`, was only filled after the record was removed).
- **Snapshot send without a deadline.** The snapshot tick awaited every viewer's send with only the server's stop token. One connection that stopped
  taking data held the tick, and so the snapshots of every map, for as long as the send did not complete. The lanes candidate in `server-perf.md` froze
  this way in two of three long series; the cause there was not shown, but the hazard is in master.
- **Database connections.** The server's pool had no size limit, so Npgsql's default of 100 applied. Each burst of concurrent database work left its
  connections open in the pool (27 after one 50-client run), and PostgreSQL's `max_connections` (100 here) was exhausted across runs. Every unit of work
  in `Nosum.Infrastructure` is disposed (`await using`), so the growth is the pool's size, not a missing dispose.

The rules that stay fixed: no wire format change (`nalix-packets`, append-only); the server decides and clients get the same packets; no new dependency.

## Decision

- **D1 — The leave handlers run while the record is still registered, and the record goes after them.** `MapPresence.LeaveAsync` claims the connection
  (a second close returns at once, as it did), runs the `PlayerLeft` handlers, and removes the record, the account and map entries, the chunk
  subscription and the group membership in a `finally`. The saved place is therefore written before presence says the player is gone: a reader that
  sees a count of zero also sees the saved place. The leave state is remembered (`RecentPlaces`) before the write, so a reconnect that overtakes the
  write still resumes where the player stood (issue #172). While a leave is in progress the membership queries (`CopyPlayers`, `CopyActiveMapIds`, `TryGetPlayer`) leave the
  player out, because the instance-empty checks of the mine and combat leave handlers count those queries; `TryGet` and `PlayerCount` still see the
  player until the record goes, so the save and the test that waits for the count read the same record. Without that exclusion the mine floor was
  never dropped (`MineWireTests.Floor_ComesBackTheSameSeed_AndAnewTheNextGameDay_WithNothingLeftOnTheGround` failed).
- **D2 — Each viewer's snapshot send has a deadline, and a viewer that misses it is dropped.** One linked `CancellationTokenSource` with
  `CancelAfter(ViewerSendDeadline)` covers everything that viewer is owed in a tick (despawns, spawns, the snapshot). The default is 250 ms (2.5
  ticks); the constructor takes another value for tests. When the deadline fires, the viewer is marked closed and its connection is disconnected, so
  the next tick does not wait for it again. The tick waits for a stalled client at most once and never blocks on it afterwards.
  The bounded wait is what the brief asked for. A send-queue with drop-on-overflow was considered and not built: the snapshot packet is one pooled
  packet shared by all viewers in a tick, so per-viewer queues would need a copy per viewer and a change to the packet's lifetime. That is a larger
  change, and it does not hold the tick any less than a deadline does.
- **D3 — The pool is capped and idle connections are released.** `NosumDatabase.CreateOptions` adds `Maximum Pool Size = 20` and
  `Connection Idle Lifetime = 60 s` to the connection string, unless the string sets them itself (checked on the string, because the builder reports
  defaults as present). 20 leaves PostgreSQL's 100 connections room for migrations, operators and a second host. The host and the tests both build
  their options through `CreateOptions`.

## Consequences

- A leaving player's place is saved before the player is gone from presence. The save still takes its time, but the presence count no longer runs
  ahead of it. A slow save now delays the removal of the record, not the reader.
- A viewer whose connection stops taking snapshots is disconnected after 250 ms. A client on a very slow network that takes longer than that to
  take one snapshot is also disconnected, and reconnects like any dropped client. This is the trade the brief asked for.
- The pool holds at most 20 connections per process. Database work beyond that queues behind the pool instead of opening connections. The effect on
  the 500-client tick was not measured here; the load test is the place to check it.
- A cancelled send may still hold the pooled snapshot packet inside the transport until the transport lets it go. The viewer is disconnected when
  this happens, so the packet is not reused for that connection. If the transport keeps a reference past the cancellation, a later tick may reuse
  the packet while the transport still reads it. This was not seen in the tests; it is a point to check with the transport owners.
- No wire, packet, opcode or schema change. No migration.

## Alternatives considered

- **Keep the removal first and wait for the save in the reader.** This is what the flaky test did. It hides the race from the test but not from a
  reconnect, so it was rejected.
- **A per-viewer outbound queue with drop on overflow.** Described under D2. Deferred.
- **Leave the pool at the default and reduce the number of concurrent operations.** The number is set by the handlers and the snapshot path, not by one
  place, so a cap is the one place that bounds it.

## Amendment (2026-11-01): a second leave waits for the first

D1 said a second close of the same connection "returns at once". That let the session end run while the first leave was still saving: a socket's close
event and the session's release both leave the same connection, and the release's session-end handlers (ADR-035 amendment 2026-11-01) ran while the
close event's save was in flight. The second leave now waits until the claimed leave has saved and removed the record, then returns. The handlers still run
once. `MapPresence.LeaveAsync` is the only place this changes; no wire, packet or schema change.
