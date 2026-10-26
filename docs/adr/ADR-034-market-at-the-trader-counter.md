# ADR-034 — The market is a counter in Town

**Status:** Accepted (coordinator, 2026-10-25, owner decision). Amends [ADR-013](ADR-013-economy-coins-escrow-market-trade.md) D5 and D8;
follows the counter model of ADR-013 amendment A2 and [ADR-030](ADR-030-community-goals-and-market-limits.md) (the board counter).

## Context

The consignment market was a tab of the game menu, opened with `M` from anywhere on any map, while the world says a trader runs it. The owner wants
the trader to stand at a counter in Town: no market from the farm, the mine or the menu.

## Decision

- **D1 — One market counter.** The market is a `ShopPoint` of the Town with the id `MarketRules.MarketCounterId = 1001` (`Nosum.Gameplay/Economy`). Ids 1 to 99 are
  shops and 1000 is the community board (`CommunityGoalCatalog.BoardCounterId`), so `ShopRules.TryFindShop` returns it like any counter.
- **D2 — The tile.** The counter is **(45, 71)** in `Town.tmx`: the walkable doormat one tile south of the tavern's door tile (45, 70), the same convention as every other
  counter (ADR-013 amendment, ADR-030). The tavern is where traders meet, and the tile is far from every other counter (the nearest is the seed shop at (43, 57), 14 tiles
  away), so no two counters are ever in reach of one place and "nearest counter wins" never has to decide. A test checks the tile, that it is walkable, reachable on foot from
  the Town spawn (4, 54), in front of a blocked door tile, and that it shares no reach with another counter.
- **D3 — Server.** `IMarketService.CreateListingAsync`, `BuyAsync` and `CancelAsync` take the caller's `IConnection` and answer the new
  `MarketStatus.NotAtCounter = 20` (appended; no value changed) unless the player's own presence record is in Town within shop reach of the market counter
  (`ShopRules.IsAtCounter`, the check `ShopService` makes for shops). The check runs first, so a refusal changes nothing. Position is read from the server's presence, never from
  the packet. Unchanged: browsing, one's own listings and the price history (reads), the shipping bin (the farm's own prop, ADR-013 D4), the expiry sweep (the server returns expired
  stacks wherever the seller is) and the payment of a sale (the seller is paid wherever they are, ADR-013 D5).
- **D4 — Client.** The Market tab leaves `ToolbarViewModel.Items`, and `M` no longer opens it (`InputEdges.Market` keeps its value; `PlayerActions` stops reading it). The
  menu shows a `MarketItem` tab only while the market is open, like `ShopItem` and `GoalsItem`. The action key next to the counter opens it; walking out of reach closes it:
  `PlayerActions.CounterLeft` fires when the farmer leaves a counter, and the page closes the panel that counter opened (market, board or shop). The key help drops `M` and says
  that the action key at a counter opens it (`Help_Counters`); `Market_Status_NotAtCounter` (vi and en) explains a refusal.
- **D5 — Wire.** No packet changed. Only `MarketStatus` gained a value; `NosumProtocol.Version` stays.

## Alternatives considered

- **Keep the tab and refuse on the server only** — the client would offer a panel whose every action fails.
- **Make the market a map of its own** — a building interior the early art does not have; a counter reuses the existing mechanism.
- **Hide the tab but keep the server open** — a modified client could still trade from anywhere, and the owner's rule is a rule of the world, not of the menu.

## Consequences

- A player with a full inventory whose listing expires is still repaid by the sweep; cancelling one early needs a trip to Town.
- Trading (ADR-013 D6, `T`) is a separate feature and is unchanged.
- Anything that lists, buys or cancels (a test, a future bot) must stand at (45, 71) in Town.

## Revisit when

A second market (a farm stall, a co-op stand) is wanted, or the final Town art moves the tavern: move the point, nothing else changes.
