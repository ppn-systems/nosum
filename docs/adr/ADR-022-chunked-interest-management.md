# ADR-022 — Chunks: interest management and per-chunk world state

**Status:** Accepted (coordinator decision delegated by the owner, 2026-10-25). Amends
[ADR-008](ADR-008-snapshot-movement-sync.md) (who receives a snapshot) and [ADR-015](ADR-015-private-instances-and-placeables.md) D3.

## Context

Today every player on a map instance receives every other player's state at 10 Hz, and every farm or placeable
change is multicast to the whole instance. Town is 130 × 110 tiles and is shared by everybody: with N players the
server sends N² entries ten times a second, most of them for players far off screen. Resource nodes and ground
items (ADR-021) add many more changing things per map. The client already culls drawing to the camera
(`TileLayerRenderer`), and maps are small enough (≤ 130 × 130 tiles, a few hundred KB) that streaming map files in
pieces would cost more than it saves.

## Decision

- **D1 — A chunk is 16 × 16 tiles** of one map instance, addressed `(cx, cy) = (tileX >> 4, tileY >> 4)`.
- **D2 — Interest set = the player's chunk and its 8 neighbours** (48 × 48 tiles, larger than the largest
  viewport at the minimum zoom). The server recomputes it when a player crosses a chunk border, with one chunk of
  hysteresis so walking along a border does not flap.
- **D3 — Snapshots carry only players inside the receiver's interest set.** A player who leaves the set gets one
  "despawn" entry; one who enters gets a full entry (look ids and nickname included, as on join).
- **D4 — World events go to the chunk's subscribers.** Farm plot changes, placed objects, resource nodes and ground
  items are published to the players whose interest set contains the chunk. When a chunk enters a player's set the
  server sends that chunk's full state once (`ChunkState`), then deltas. Nalix groups stay per instance (map-wide
  things: announcements, warps, chat); per-chunk fan-out iterates a chunk → subscriber index kept in memory.
- **D5 — Per-chunk storage in memory.** Instance state (plots, placed objects, nodes, ground items) is held in
  per-chunk buckets so "everything in chunk (cx, cy)" is O(chunk) not O(map). Persistence stays row-per-thing in
  PostgreSQL with indexes on (map id, owner id); an instance still loads once on first use.
- **D6 — The client keeps per-chunk mirrors** and drops a chunk's mirror when it leaves the interest set (plus
  hysteresis). Map tiles are not chunk-streamed (see Context).
- **D7 — Wire.** `0x7300–0x73FF` (World band sub-range) for chunk enter/leave/state. Snapshot entries keep their
  layout; only who is included changes. Append-only; the coordinator bumps `NosumProtocol.Version` at merge.

## Alternatives considered

- **Radius per player instead of chunks.** Exact, but O(N²) distance checks each tick and no natural unit for state
  hand-off. Rejected.
- **Splitting Town into several instances when crowded.** Still wanted later (pillars open question) and composes
  with chunks; not a substitute for them.
- **Chunk-streaming map files.** Rejected for now: maps are small; revisit for very large maps.

## Consequences

- Fan-out cost per tick is O(players × neighbours in 9 chunks), independent of map size.
- Every feature that broadcasts "to the map" must choose: instance-wide (rare) or chunk (default). The
  `gameplay-rules`/`nalix-handlers` skills state the rule.
- Integration tests need two players far apart on one map to prove they do not see each other.

## Revisit when

A single chunk can hold more players than one snapshot packet carries, or maps larger than 256 × 256 tiles appear.

## Implementation notes

How the decisions above were built, and where the build departs from or fills in the text. The wire changes are listed last.

- **D1/D2 — pure chunk math in `Nosum.Gameplay/Maps`.** `ChunkCoord` (tile or pixel to chunk, chebyshev distance, a sortable key), `ChunkBounds` (the chunks a map has: a chunk outside never joins a set, so a
  corner player has 4 chunks, not 9) and `ChunkInterest` (the set: a 5 × 5 bit window around the player's chunk). Hysteresis is *enter within 1, leave beyond 2*: after a border crossing the chunks behind the player
  are still two away and stay, so walking along a border adds and drops nothing; the set holds up to 12 chunks after a straight step (a 3 × 4 block; 16 after a diagonal one) and shrinks back to 9 only when the player has moved on. The set is per receiver, so
  visibility is **asymmetric** while a player stands two chunks from where they crossed: B may still see A while A no longer sees B.
- **D2 — recompute on border crossings only.** `PlayerService.MoveAsync` is untouched. The snapshot loop reads each player's position once per tick, finds the chunk (one shift) and moves the interest set
  only when it differs from the set's centre. The chunk → subscribers index (`ChunkIndex`, behind `IChunkMap`, implemented by `MapPresence`) changes then, at a join and at a leave, never per move.
  The consequence is a delay of up to one tick (100 ms, a quarter of a chunk at walking speed) between crossing and the new chunks' state arriving, far inside the 16-tile margin the 3 × 3 block has over the viewport.
- **D3 — snapshots.** One tick of one instance: read every player once into reused `PlayerState` rows, sort the player indices by chunk key (`Array.Sort` on two preallocated arrays), then each receiver's snapshot is the
  rows found by binary search in the chunks of their interest set. Nothing allocates once the arrays have grown to the instance size. A receiver's snapshot still contains the receiver (the client uses it for the
  clock; entries are unchanged). **Spawn** = a `LookChanged` (look ids + nickname, the existing push) sent *before* the snapshot that first contains the player; **despawn** = a `PlayerLeft` (the existing push)
  *before* the first snapshot without them, plus the client drops anyone missing from a snapshot as before. Which players a receiver was shown is `ViewerState.Shown` (id → tick stamp), diffed in the same pass.
  `PlayerLeft` is also published at once to the chunk's subscribers when a player leaves the instance; the next tick's diff would send a second one to anyone it missed, which is harmless.
- **D4 — publishing.** `IChunkPublisher` (`Nosum.Abstractions/Presence`): `PublishAsync(instance, chunk, packet, except, ct)` and `PublishInstanceAsync(instance, packet, except, ct)`; the argument order mirrors
  `IMapPresence.MulticastAsync(key, packet, except, ct)`, so converting a call is one line. The **rule** (also in the `nalix-handlers` and `gameplay-rules` skills): *chunk by default, instance when map-wide.*
  Converted: farm tile changes (`FarmTilesChanged`), placed objects (`PlacedChanged`), a player's look/nickname change (`LookChanged`, at the player's chunk) and `PlayerLeft`. Kept instance-wide because they are map-wide or
  addressed to one player: `MapChanged`, `MoveCorrection`, replies, inventory/wallet/quest pushes (per player, not through the presence), and chat/announcements when they arrive (`PublishInstanceAsync`).
  Trade and co-op invitations still find their target with `IMapPresence.CopyPlayers` (the instance), not in sight; the client's invitation picker (`NearbyPlayers`) is filled from snapshots and so lists the
  players in sight, which is the intended reach. `IChunkStateContributor` is how a feature supplies the state of one chunk; `FarmService` and `PlaceableService` implement it, and resource nodes and ground items add
  theirs the same way (and publish their deltas through `IChunkPublisher`).
- **D5 — per-chunk buckets.** `FarmService` keeps an instance's tiles as `chunk → tiles`; `PlaceableService` keeps `ByChunk` beside the `PlacedSet` (rebuilt with the collision grid, under the instance lock; an object
  belongs to the chunk of its first, top-left tile). Persistence is unchanged: row per thing, indexed by (map, owner); an instance still loads once on first use. A placed object moved across a chunk border is published as a
  removal to the old chunk's subscribers and an upsert to the new one's, removal first.
- **D6 — client.** `FarmChannel` and `PlaceableChannel` keep one flat mirror (the frame loop's API is unchanged) in which every entry belongs to the chunk its coordinates fall in: a `ChunkState` replaces the chunk's entries,
  a `ChunkLeft` removes them. `Load()` (the join-time request, `GetFarm`/`GetPlaced`) stays as the resync that also names the map; the server now answers it with the tiles/objects of the interest set only. A `ChunkState` or
  `ChunkLeft` for another map than the mirror's is ignored. `LookChannel` forgets a player's look on `PlayerLeft`. `Nosum.Wasm.Socket` gained a project reference to `Nosum.Gameplay` for the chunk math.
- **D7 — wire.** World sub-range `0x7300–0x73FF`: C2S `0x7300–0x733F` (none), S2C `0x7340–0x737F`: `ChunkState` (`0x7340`: map id, chunk x/y, server clock, farm tiles, placed objects) and `ChunkLeft` (`0x7341`: map id, chunk x/y).
  A chunk with nothing in it is not sent. `ChunkState` grows by appending lists (nodes, ground items). No existing packet or opcode changed; `NosumProtocol.Version` is not bumped here.

### Measured (20 players on Town, 10 Hz)

The same test (`ChunkLoadTests`, real WebSocket clients, every frame the client receives counted, 30 ticks after the joins settled) run on `master` and on this branch:

| Layout | Entries / snapshot, before → after | Bytes per tick, all 20 receivers, before → after |
|:--|:--|:--|
| Evenly spread (farthest-point) | 20 → 2.9 | 5 960 → 1 172 (−80 %) |
| Random (seed 22) | 20 → 4.2 | 5 960 → 1 536 (−74 %) |
| All in one chunk | 20 → 20 | 5 960 → 5 960 (no change, by design) |

### Known limits

- A `ChunkState` is read after the subscription and sent a moment later; a delta committed in between can reach the client before the (older) state and be overwritten by it until the next change of that tile. The window is one
  packet send; the player's own actions are re-read by `GetFarm`/`GetPlaced`.
- One snapshot per receiver is serialized and encrypted per receiver (as the multicast did per member); chunks bound the *size* of each, not the number of sends.
- The first `ChunkState` of an instance's farm triggers its one load from PostgreSQL inside the snapshot tick.
