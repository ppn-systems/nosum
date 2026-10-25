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
- Roles inside a co-op (founder, member) decide who may spend shared stock.
- Every contribution and withdrawal is recorded, so members can see who did what.

## Pillar 2 — Shared market

One market per server where players buy and sell to each other.

- Players post sell and buy orders; matching happens on the server.
- Prices come from player supply and demand, not from a fixed shop table (a fixed shop may
  still exist for seeds and basic tools).
- A trade is one atomic server transaction: items and coins move together or not at all.

## Pillar 3 — Server-wide seasons and goals

Everyone on a server lives in the same calendar.

- The season and day are computed from server time, identical for every player.
- Each season has community goals (deliver N of a crop, build a bridge) that the whole server
  contributes to; finishing one changes the shared world for everybody.
- Seasonal crops make the market move: what grows now is cheap, what is out of season is not.

## What this asks of the architecture

| Pillar | Needs | Where |
| :--- | :--- | :--- |
| Co-ops | Co-op entity, membership, shared inventory with an audit trail | `server/Nosum.Domain`, `gameplay/Nosum.Gameplay` (rules), `contracts/` (`Coop` domain) |
| Co-ops | Push to all online members | Nalix connection groups: `UseGroups()` on the host, `MulticastAsync` to the co-op's group |
| Market | Order book, matching, atomic trades, anti-spam | Server only; one database transaction per trade; `[PacketRateLimit]` on order handlers |
| Seasons | Calendar from server time, community goal progress | `gameplay/` computes the calendar from a passed-in time; server persists goal progress and broadcasts milestones |

Every pillar is server-authoritative ([ADR-003](../adr/ADR-003-server-authoritative-websocket.md)):
the client never decides a contribution, a trade or a goal outcome.

## Open questions

- Co-op size limit, and whether a player may belong to more than one.
- One shared world map per server, or private farms plus shared town and co-op land.
- Market fees, order expiry, and limits that stop one player cornering a crop.
- Season length in real time.
