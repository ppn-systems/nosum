# ADR-013 — Economy: coins, one atomic transfer, shipping bin, consignment market, direct trade

**Status:** Accepted (owner approval, 2026-10-25). Amended 2026-10-25: starter kit, town shop, market fee sink; the shop split into three (see Amendments). Amended by [ADR-034](ADR-034-market-at-the-trader-counter.md): the market (D5, D8) is a counter in Town, no longer a menu tab or the `M` key.

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
  - "Same map" means the other player is in the list `IMapPresence.CopyPlayers` reports for the asking player's place (`PlayerPresence.Key`, the instance key of
    [ADR-015](ADR-015-private-instances-and-placeables.md): private farms are separate places, so players on their own farms cannot trade); the target is addressed by their
    id on the map. Nothing here builds a group name from a map id.
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

Starting coins (500), fee (5 %), listing time (48 h), listing limit (20), shipping prices, price bounds against cornering, trade invitation lifetime (30 s), the fee goes nowhere (decided 2026-10-25: a coin sink).

## Amendments

### 2026-10-25 - starter kit, the town shop, the fee sink (owner decisions)

- **A1 - Starter kit.** A new player and every guest start with the hoe, the watering can and seeds (8 parsnip, 6 radish, 4 cauliflower) and nothing else
  (`StarterKit`). The seven furniture pieces and five clothes they used to receive are gone; `LookService` no longer gives a starter wardrobe either. The default
  look still has the plain shirt and trousers (item id zero in `PlayerLook`), so the look system needs no clothing item. Players created before this change keep what they
  were given.
- **A2 - Shop.** Furniture and clothing are bought for coins at a shop in the Town (a public map), and seeds are sold there too, so coins have a use besides the market.
  - *Data.* A shop is `ShopDefinition` in `ShopCatalog` (`Nosum.Gameplay/Shops`): stable id, key (`Shop_Name_<key>` text), price list; stock is unlimited. Where it is
    served is `MapSpec.Shops` (`ShopPoint`: shop id and counter tile), so another shop is one catalogue entry and one map point. The general store's point is the doormat
    in front of Pierre's store in `Town.tmx` (tile 43, 57).
  - *Wire.* New band `0xC000-0xC0FF`: `BuyFromShop` (shop id, item id, quantity; encrypted, USER, rate limited 8 per 4 s) and `ShopResult` (`ShopStatus`, coins spent). The
    client lists the offers from the same shared catalogue, so no catalogue packet exists; the items and coins arrive as the usual inventory and wallet pushes.
  - *Rules.* `ShopRules.Price` checks the shop, that it sells the item and a quantity of 1 to a stack. `ShopService` then checks the player's own presence record is on the
    shop's map within 40 px of the counter tile, and runs one `ITransferService` transfer (coins out, items in, reason `shop.buy`): not enough coins, no room or a lost race
    change nothing. Opening the panel is client-side (the action key next to the counter); the server re-checks on every purchase.
  - *Prices (defaults, owner questions).* Seeds: parsnip 10, radish 15, cauliflower 30 (crops sell for 20 / 30 / 60 at the bin). Furniture: chair 100, plant 80, lamp 140, table 220,
    bookcase 260, armchair 300, couch 400. Clothing: shirts 60, trousers 80, straw hat 50, cap 40. The 500 starting coins buy a chair, a plant and a lamp, not the range.
- **A3 - Tester grant.** `DevGrant` (band `0xF000-0xF0FF`, `DevOpCode`) gives the sender an item stack and/or coins through the same transfer (reason `dev.grant`). The host maps
  `DevHandlers` and registers `IDevGrantService` only when `DOTNET_ENVIRONMENT` (or `ASPNETCORE_ENVIRONMENT`) is explicitly `Development`; in Production or when unset the
  opcode is unknown, so the frame is dropped (a test asserts no answer). The client can send it only from a build made with `-p:NosumDevTools=true` (`Game/DevTools`, called from
  the browser console); a normal publish contains no way to ask.
- **A4 - Market fee = coin sink (anti-inflation), owner decision 2026-10-25.** The 5 % fee stays destroyed. The sale transfer asks `IFeeSink.Collect(fee, reason)` for the legs that
  receive it; the only implementation (`CoinSink`) returns none, so the coins leave the economy. Redirecting the fee later (to a fund) means another `IFeeSink` and nothing else.
  The community-goal question is closed.
- **A5 - Names.** Market listings carry `SellerNickname` ([ADR-009 amendments](ADR-009-postgresql-persistence-and-accounts.md)); `ListingRow` gained a field (protocol version 3).

Open owner questions added: the shop prices above; whether the shop should have limited stock or daily price changes; a second shop (animals, tools) and where.

### 2026-10-25 (second amendment) - three shops instead of one (owner decision)

Amends A2: the Town's one general store is now three shops, each one catalogue entry and one map point, so the mechanism of A2 is unchanged (`ShopDefinition`, `MapSpec.Shops`,
`BuyFromShop` / `ShopResult` in band `0xC000`; the wire is unchanged and `NosumProtocol.Version` stays 3).

| Id | Key | Sells | Counter (Town.tmx tile) | Building |
| :-- | :-- | :-- | :-- | :-- |
| 1 | `seed_shop` | parsnip, radish, cauliflower seeds | (43, 57) | Pierre's store, door tiles (43,56) and (44,56) |
| 2 | `clothing_shop` | shirts, trousers, hats | (20, 89) | Emily and Haley's house, door tile (20,88) |
| 3 | `furniture_shop` | the seven furniture pieces | (95, 51) | a big-box store stand-in, door tiles (95,50) and (96,50) |

- **Why these tiles.** Each counter is the walkable doormat one tile south of a building's door tile in `Town.tmx` (the same convention as the existing one, found from the
  `LockedDoorWarp` tile data of the map); a test checks each is walkable, reachable on foot from the Town spawn (4, 54) and directly in front of a blocked door tile, and that no two
  counters are in reach of the same place. Seeds stay at Pierre's (id 1 is unchanged: it was the only shop). Clothing is at Emily and Haley's house (Emily sews, Haley follows fashion; it is
  also the nearest house to the west half of the town). Furniture belongs in the carpenter's shop, which is on the Mountain map the game does not have; **a big-box store** is the stand-in
  (and the farthest east, so the three trades are spread over the town). When a carpenter map exists, move the furniture point there and nothing else changes.
- **Rules.** Prices are unchanged (seeds 10 / 15 / 30, furniture 80-400, clothing 40-80). A shop sells only its own kind and no item is sold by two shops (tests). The server already did
  what is needed: `ShopRules.Price` answers `NotSold` for an item the named shop does not carry, and `ShopService` answers `NotAtShop` unless the player's own presence is at a counter
  **of that shop id**, so standing at the seed shop and naming the furniture shop is refused. Both are covered by service and over-the-wire tests.
- **Client.** `ShopPanel` takes a `ShopId` and shows that shop's title (`Shop_Title_<key>`, vi and en) and list; the tab bar is gone. It opens its shop on its view model itself, so a host
  sets only `Vm` and `ShopId`. The action key next to any counter opens the matching shop.
- **Open owner questions:** the big-box store stand-in for furniture (its name is early art, not Nosum branding); shop names for the final art; whether
  the clothing shop should move closer to the farm entrance.
