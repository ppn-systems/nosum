---
name: gameplay-rules
description: Use when writing farm rules in gameplay/Nosum.Gameplay or server code that validates a player action — plots, crops, growth timers, items, inventory, prices, currency. Server-authoritative rule, time-from-timestamps, determinism, what the client may compute.
---

# Gameplay rules

Decision: `docs/adr/ADR-003-server-authoritative-websocket.md`. Game identity (co-ops, shared
market, server-wide seasons): `docs/design/pillars.md`.

## Non-negotiables

- **The server decides.** A client packet is an intent ("plant seed X in plot Y"), never a
  result ("I now have 500 coins"). The handler loads state, runs the rule from `Nosum.Gameplay`,
  persists and replies. Coins, items, time and growth are never taken from the client.
- **Pure rules.** `Nosum.Gameplay` has no I/O, database, network, logging, randomness source or
  clock read. Time (`DateTimeOffset now`) and random values are parameters, so the same input
  always gives the same output on server and client.
- **Timers from timestamps.** Growth and cooldowns are computed from stored timestamps
  (`plantedAt`, `now`) on demand. No per-tick simulation of offline farms on the server.
- **The client may predict, never commit.** The client can run a rule to show the expected
  result immediately; the server's reply overwrites it.
- **Validate every input.** Plot ownership, coordinates in range, item present in inventory,
  enough currency, cooldown elapsed — each rule returns a result the handler maps to a reply
  status, not an exception for normal refusals.
- **Rate-limit spam-able actions** (harvest, market posts) on the handler (`nalix-handlers`).

## Chunks (docs/adr/ADR-022)

- The world is cut into 16 × 16 tile chunks (`Nosum.Gameplay/Maps/ChunkCoord`); a player is interested in their
  chunk and its 8 neighbours (`ChunkInterest`, one chunk of hysteresis). Chunk math is pure rules and lives here.
- **Chunk by default, instance when map-wide.** A rule that changes the world at a place (till, place, chop,
  drop an item) names the chunk of the tile it changed (`ChunkCoord.OfTile`) and the server publishes the change to
  that chunk's subscribers; only a truly map-wide effect is published to the instance. State is kept in per-chunk
  buckets so "everything in chunk c" is cheap.
- Rules that look at neighbours (reach, collision) are not chunk-bound: they read the whole instance state.

## Placement

- Rule code: `gameplay/Nosum.Gameplay/{Domain}/`.
- Persisted shape: `server/Nosum.Domain/{Domain}/` (may use `Nosum.Gameplay` types).
- Wire shape: `contracts/Nosum.Contracts/{Domain}/` — contracts do not reference `Nosum.Gameplay`.

## Tests

Every rule gets unit tests in `tests/Nosum.UnitTests/Gameplay/` covering the accepted path and each
refusal (`testing-style`).
