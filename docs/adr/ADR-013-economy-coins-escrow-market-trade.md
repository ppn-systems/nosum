# ADR-013 — Economy: coins, one atomic transfer, shipping bin, consignment market, direct trade

**Status:** Proposed

## Context

[Pillar 2](../design/pillars.md) asks for a player-driven economy: a consignment market with escrow and a fee, and direct trade with
two-sided confirmation. The owner's rules are non-negotiable: the server decides everything, coins and items move only inside
server transactions, every purchase and trade is ONE atomic database transaction, and duplication, double-spend and races (two
buyers, cancel versus buy) must be impossible. [ADR-011](ADR-011-inventory-farming-data-model.md) already made inventory changes atomic,
audited and serialized per player, and gave them an `enlist` callback; [ADR-010](ADR-010-internationalization.md) forbids display text on
the wire. Every number below (starting coins, fee, listing time, prices) is a default chosen by the implementing agent and an open owner
question.

## Decision

- **D1 — Coins are a persisted wallet.** `PlayerWallet` (one row per player, `Coins` as `long`, a concurrency token) and `CoinLogEntry` (player,
  signed delta, stable reason code, time; the audit trail). A wallet is created on first touch with 500 starting coins (logged as `starter`) by
  whichever transfer or read comes first, so a player who was never online can still be paid. A balance cannot go negative (database check) or past
  1 000 000 000 000 (`CoinRules.MaxBalance`). Coins reach the client as `WalletSnapshot` / `WalletUpdate` (ids and numbers only).
- **D2 — One generic transfer.** `ITransferService.ExecuteAsync(legs, reason, enlist)` moves items and coins between players and co-op
  warehouses ([ADR-014](ADR-014-coops-and-looks.md)) in one database transaction. A leg is (party, item, items ± , coins ±); the service moves
  exactly what the legs say, so a fee is a leg nobody receives. It builds on `IInventoryService.ApplyManyAsync`, a new multi-player form of
  `ApplyAsync`: it takes the locks of every involved player together (always in stripe order, so overlapping callers cannot deadlock), loads all
  inventories in one unit of work, applies removals before additions (room freed by giving counts), writes the audit rows, runs the enlisted work
  (wallets, warehouse stock, then the caller's own: a listing row, a log line) and saves once. Any refusal, or a concurrency/unique conflict on save, rolls
  everything back. The market, trades, the shipping bin and the warehouse are all transfers; nothing else writes coins.
- **D3 — Tradable items.** Tools are bound to their owner (`ItemDefinition.IsTradable`), so nobody can sell the hoe and lock themselves out of
  farming. Seeds, produce and clothing trade freely.
- **D4 — Shipping bin.** The farm's existing bin prop buys produce at a fixed server price table (`ShippingPrices`: parsnip 20, radish 30,
  cauliflower 60 coins; development values). `ShipItems(slot, itemId, quantity)` is an intent: the server checks the item against the slot, the
  price table, and that the player's own presence record is within 40 px of the bin's footprint on a farmable map (`ShippingBinRules`), then removes
  the items and pays in one transfer. The client sends it when the action key is pressed facing the bin with a crop held.
- **D5 — Consignment market.** `MarketListing` rows are the escrow: listing removes the stack from the seller's inventory and inserts an
  `Active` row in one transfer; a listing is paid out of, or returned from, escrow only by the transfer that closes it.
  - Fee 5 % of the price, rounded down, taken from the seller's proceeds (`CoinRules.MarketFee`); the fee leaves the economy.
    Price is for the whole stack (1 to 1 000 000 000 coins); a seller has at most 20 active listings; a listing lasts 48 hours.
  - **Buy** = buyer coins out, seller coins in (minus the fee), items to the buyer, listing `Sold`, in one transfer. The seller is paid whether
    online or not. A buyer with too few coins or no room changes nothing and the listing stays for sale.
  - **Races.** Each listing has an in-process lock; buy, cancel and expiry take it first, then the players' locks (a fixed order). The listing
    `Status` is also a database concurrency token, so even a lost in-process race fails the second writer instead of selling twice. Two buyers: exactly
    one succeeds and the other gets `ListingGone`. Cancel versus buy: exactly one wins; the items end in one place.
  - **Cancel and expiry** return the stack through the same transfer (`Cancelled`, `Expired`). The hosted `MarketExpiryService` sweeps every minute. A seller
    whose inventory is full is refused (`NoSpace`) and the listing stays in escrow, retried by the next sweep; a seller may also cancel at any time.
  - **Browse** is paged (10 per page, newest first). Search is by item id: names are localized client data, so the client turns the typed text into the ids
    whose localized name matches and sends the ids; the server never sees text.
  - Listings are not shown with a seller name: players have no public name yet. A row says only whether the viewer is the seller.
- **D6 — Direct trade.** `ITradeService` keeps invitations (30 s) and open sessions in memory; the confirm rules are the pure `TradeSession`
  (`Nosum.Gameplay/Trade`): each side holds an offer (up to 9 item lines and coins) and a confirmation, any *different* offer from either side clears
  both confirmations, and the swap is ready only when both confirmed the offers as they now stand.
  - "Same map" means the other player is in the list `IMapPresence.CopyPlayers` reports for the asking player's map; the target is addressed by their
    id on the map. Nothing is keyed by a map id string.
  - Offers are **claims, not escrow**: they are checked when set (so the window is honest) and re-checked authoritatively inside the swap transaction. A side
    that no longer has what it offered, or no room, makes the swap fail with nothing moved and the trade ends with a code. This keeps the invariant simple (an item
    is in exactly one place) and makes "return everything on abort" trivially true.
  - Leaving the map (disconnect, session replaced or a map change; `IMapPresence.PlayerLeft`) ends the player's invitation or trade and tells the other side.
- **D7 — Wire.** Market band `0x4000-0x40FF` (wallet, shipping, listings; `MarketStatus` codes) and Trade band `0x9000-0x90FF` (`TradeStatus`,
  `TradeEndReason`). Every request that moves coins or items is `[PacketEncryption(true)]`, `[PacketPermission(USER)]` and rate limited; identity is the
  connection's. Packets carry ids, enums and numbers only.
- **D8 — Client.** `WalletChannel`, `MarketChannel`, `TradeChannel` mirror the server; `MarketViewModel`, `TradeViewModel` localize. The panels are built from shared
  controls in `Nosum.Wasm.Controls` (`GamePanel`, `GameButton`, `GameInput`, `ItemGrid`, `ConfirmDialog`, `PlayerPicker`, `PriceInput`, `Pager`, `TabBar`), so the
  look of every panel changes in one place. Keys: `M` market, `T` trade.

## Alternatives considered

- **Escrow the trade offers** (move items into a holding row while the trade is open) — returns need cleanup after a crash, and an offer would block the
  items; claims re-checked in the swap give the same safety with no extra state.
- **A database-level `SELECT ... FOR UPDATE` per row** instead of in-process locks — correct on several processes, but the server is one process
  ([ADR-009](ADR-009-postgresql-persistence-and-accounts.md)) and the concurrency tokens already protect the data if that changes.
- **Server-side text search** — would need item names on the server ([ADR-011](ADR-011-inventory-farming-data-model.md) kept them on the client).
- **A mailbox for returned items** — avoids the full-inventory retry loop but is a feature of its own.

## Consequences

- Every feature that gives or takes coins must go through `ITransferService` or the audit trail has holes.
- A transfer holds the locks of all its players while it saves; two trades touching one player queue behind each other.
- Trade and invitation state, like sessions and presence, is in one process. A restart ends open trades (nothing was moved).
- Expired listings with a full-inventory seller stay in escrow until there is room or the seller cancels.

## Revisit when

The server runs on several processes (locks, trade state), a mailbox is wanted, prices should be bounded to stop cornering a crop, listings need seller
names, or the fee should be a sink with a recipient (a town fund).

## Open owner questions

Starting coins (500), fee (5 %), listing time (48 h), listing limit (20), shipping prices, price bounds against cornering, trade invitation lifetime (30 s), whether the
fee goes to a community goal ([pillar 3](../design/pillars.md)).
