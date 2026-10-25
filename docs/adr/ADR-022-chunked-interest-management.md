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
