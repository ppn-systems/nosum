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

## Placement

- Rule code: `gameplay/Nosum.Gameplay/{Domain}/`.
- Persisted shape: `server/Nosum.Domain/{Domain}/` (may use `Nosum.Gameplay` types).
- Wire shape: `contracts/Nosum.Contracts/{Domain}/` — contracts do not reference `Nosum.Gameplay`.

## Tests

Every rule gets unit tests in `tests/Nosum.UnitTests/Gameplay/` covering the accepted path and each
refusal (`testing-style`).
