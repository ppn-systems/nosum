# ADR-030 — Community goals and market limits

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M4 (Pillar 3). The board's page amended by [ADR-043](ADR-043-npc-merchants-and-the-menu-split.md): it opens from the board keeper's dialog box, as a dialog of its own, not a menu tab. Builds on ADR-018 (seasons),
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

## Implementation notes (2026-10-25)

- **Goals are rows per goal id and season number** (`GameCalendar.SeasonNumberAt`, a count without end). A goal that was completed is complete for
  the server's lifetime: its season comes round again and the board shows it completed. One that expired opens again next year with a fresh row, so the old
  contributions never carry over. Goal ids 1-4 (spring bridge, summer festival, fall harvest, winter lanterns), items parsnip, cauliflower, radish and parsnip,
  are data in `CommunityGoalCatalog`; more crops are more entries.
- **The delivery is one transfer** (`community.deliver`): the items leave the player, the goal total and the player's contribution change, and the delivery that
  reaches the target also sets the standing to completed and writes the unlock row, all in one transaction. Only what the goal still takes is taken. Deliveries
  are serialized per goal in process; the total and the standing are concurrency tokens behind that.
- **Rewards are paid after completion, once per contributor.** `PaidCoins` on the contribution is empty until paid and the payout transfer refuses to run when it
  is not (`community.reward`, `community.consolation` in the coin log). A crash between completion and payment is repaired by `SettlePendingAsync` (host start and the
  day clock). The coin pool is shared by delivered share, rounded down, capped per player; `CommunityGoalRules.RewardShare`. No mail service exists yet: `PayAsync` is the
  single call site to replace with `IMailService.SendAsync` (ADR-029).
- **Expiry.** `CloseEndedSeasonsAsync` expires every open goal of an earlier season number; the day clock calls it on each day end, the host once at start, and every
  read and delivery before it looks. The consolation is half the bin price per delivered item.
- **Unlocks.** `ICommunityUnlocks.IsUnlocked(key)` is an in-memory set loaded from `community_unlocks`; the first beneficiary (the Beach bridge, key `beach_bridge`) does not
  query it yet because the Beach map does not exist. (ADR-037: the Beach exists now and the bridge tiles of its east bank are gated by this unlock.)
- **The board counter** is `ShopPoint(1000, 42, 57)` in Town: the tile in front of the right-hand sign of the billboard on the seed shop, next to the seed shop's doormat. A
  counter id from 1000 is not a shop. Because two counters are in reach there, `ShopRules.TryFindShop` returns the nearest one (tile 42 is the board's, 43 the seed shop's).
- **Market.** A listing is refused with `MarketStatus.TooManyOfItem` when the seller's active listings of that item id plus the new one exceed 500 units (checked inside the
  listing transaction, so parallel listings cannot slip past). Every purchase writes a `market_sales` row in the buy transaction (item, quantity, price, unit price, game day);
  the history groups the last 14 game days that had sales into min, median (mean of the two middle prices when even) and max of the unit price.
- **Weak spots.** `market_sales` grows by one small row per sale and is not pruned yet. Goal progress is pushed to the Town only; players elsewhere see the totals when they open the board.

