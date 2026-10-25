# Nosum — game pillars

What makes Nosum different from single-player farming games: **farming is a shared,
server-wide activity.** The 3/4 top-down view ([ADR-005](../adr/ADR-005-three-quarter-top-down-view.md))
is the genre's camera; these pillars are Nosum's identity. A feature that serves none of them
needs a reason.

Status: direction agreed by the owner (2026-10-25). The mechanics below are the starting
design; numbers and details are open and are settled per feature.

## Pillar 1 — Cooperatives

Players form a cooperative (co-op): a small group that owns shared land and a shared
warehouse next to each member's own farm.

- Members contribute crops and items to the co-op warehouse; the co-op unlocks shared
  buildings and tools no single player can afford.
- No limit on co-op size.
- Roles inside a co-op (founder, member) decide who may spend shared stock.
- Every contribution and withdrawal is recorded, so members can see who did what.

## Pillar 2 — Shared market and player trade

Players trade with each other in two ways:

- **Consignment market.** A player lists items at a price; the server takes the items into
  escrow so they cannot be used or sold twice. Any player can buy a listing; the seller gets
  the coins (minus a market fee) even while offline. A listing that expires or is withdrawn
  returns the items to the seller.
- **Direct trade.** Two players open a trade, each puts in items and coins, both confirm, and
  the server swaps everything at once. Changing an offer clears both confirmations.

Prices come from what players ask and pay, not from a fixed shop table (a fixed shop may
still exist for seeds and basic tools). Every purchase and every direct trade is one atomic
server transaction: items and coins move together or not at all.

## Pillar 3 — Server-wide seasons and goals

Everyone on a server lives in the same calendar.

- The season and day are computed from server time, identical for every player.
- Each season has community goals (deliver N of a crop, build a bridge) that the whole server
  contributes to; finishing one changes the shared world for everybody.
- Seasonal crops make the market move: what grows now is cheap, what is out of season is not.

## World: many maps

The world is made of many maps, not one: each player's farm, co-op land, the town and market,
and other areas reached through exits. A player is on exactly one map at a time; the server
sends each player only what happens on that map.

## What this asks of the architecture

| Pillar | Needs | Where |
| :--- | :--- | :--- |
| Co-ops | Co-op entity, membership, shared inventory with an audit trail | `server/Nosum.Domain`, `gameplay/Nosum.Gameplay` (rules), `contracts/` (`Coop` domain) |
| Co-ops | Push to all online members | Nalix connection groups: `UseGroups()` on the host, `MulticastAsync` to the co-op's group |
| Market | Consignment listings with escrow, expiry, fees; direct trade sessions with two-sided confirm | Server only; one database transaction per purchase or swap; `[PacketRateLimit]` on listing and trade handlers |
| Many maps | Map definitions, player-to-map assignment, map transitions | One Nalix connection group per active map so updates reach only players on it; map data loaded per map, not per world |
| Seasons | Calendar from server time, community goal progress | `gameplay/` computes the calendar from a passed-in time; server persists goal progress and broadcasts milestones |

Every pillar is server-authoritative ([ADR-003](../adr/ADR-003-server-authoritative-websocket.md)):
the client never decides a contribution, a trade or a goal outcome.

## Open questions

- Whether a player may belong to more than one co-op.
- Market fee rate, listing duration, and limits that stop one player cornering a crop.
- Season length in real time.
- How many players share one instance of a public map (town) before it splits into copies.
