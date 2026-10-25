# ADR-030 — Community goals and market limits

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M4 (Pillar 3). Builds on ADR-018 (seasons),
ADR-013 (market, transfer), ADR-029 (mail).

## Context

Pillar 3 promises server-wide goals per season; the market has no guard against one player cornering an item.

## Decision

- **D1 — Goals are data per season** (`CommunityGoalCatalog`): e.g. spring "deliver 5 000 parsnips → the bridge to the
  Beach opens", summer "deliver 2 000 melons → a festival stall sells a rare seed". Each goal has a target, a delivery
  point (a Town board counter), and a reward that is server state (a warp unlocked, a shop entry unlocked, a mail with
  coins to every contributor proportional to share, capped).
- **D2 — Delivering** moves items from the player into the goal atomically (audited); progress is one row per goal with
  a total and one row per contributor. Progress is pushed to everyone on the Town instance and shown on the board page.
- **D3 — Completion is permanent** for the server's lifetime; unfinished goals expire at season end with a consolation
  mail (coins per item at half price).
- **D4 — Market cornering limit:** at most 20 active listings per player (exists) and at most 500 units of one item id
  per player listed at once; per-item price history (daily min/median/max of sales) for the market page.
- **D5 — Wire.** Goals `0x4100–0x41FF` (Market band slice); price history in the market band append-only.

## Consequences

Rewards that unlock content need content: the Beach map (M3) is the first unlock target.
