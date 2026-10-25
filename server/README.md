# server/

The Nosum realtime server: one Nalix WebSocket listener (`/ws/`, port 57300) that every client
connects to. Decision: [ADR-003](../docs/adr/ADR-003-server-authoritative-websocket.md).

| Project | Holds | May reference |
| :--- | :--- | :--- |
| `Nosum.Domain` | Persisted entities and enums | `Nosum.Gameplay` |
| `Nosum.Abstractions` | Service interfaces, DTO rows | `Nosum.Domain`, `Nosum.Contracts` |
| `Nosum.Infrastructure` | Persistence, service implementations | `Nosum.Abstractions` |
| `Nosum.Handlers` | Stateless packet handlers, by domain folder | `Nosum.Abstractions`, `Nalix.Runtime` — **never** Infrastructure |
| `Nosum.Host` | Composition root (`Program.cs`) | `Nosum.Handlers`, `Nosum.Infrastructure`, `Nalix.Hosting` |

Persistence and accounts ([ADR-009](../docs/adr/ADR-009-postgresql-persistence-and-accounts.md)):
PostgreSQL through EF Core in `Nosum.Infrastructure/Persistence` (`NosumDbContext`, one
`IEntityTypeConfiguration` per entity, migrations in `Persistence/Migrations`). Features reach data
through `IRepository<T>` / `IUnitOfWork` (`Nosum.Abstractions/Data`) inside a service. Account
handlers are in `Nosum.Handlers/Account`; `connection.GetPlayer()` (`Nosum.Handlers/Session`)
returns the signed-in player of a connection.

Players on maps (movement sync, [ADR-008](../docs/adr/ADR-008-snapshot-movement-sync.md)):
`IMapPresence` (`Nosum.Abstractions/Presence`, implemented by `MapPresence` in Infrastructure) is the
reusable "who is on which map" service: one Nalix connection group per map (`MapGroup.Name`),
cleanup on disconnect, `MulticastAsync(mapId, packet)` for any feature. `IPlayerService`
(`PlayerService`) places players (the saved map and position from `IPlayerStateService`, else the farm
spawn), validates `MoveIntent`s with `MoveValidator` from `Nosum.Gameplay`, moves a player to another map
when an accepted position is on an exit (`WarpRule`; leave the old map group, push `MapChanged`, join the
new group, save map and place; [ADR-012](../docs/adr/ADR-012-maps-and-warps.md)) and saves the position when
the player leaves. While anyone is on a map it pushes a `MapSnapshot` to that map every 100 ms; the loop
stops by itself when the last player leaves. Handlers: `Nosum.Handlers/Player` (signed-in players
only). Maps come from `IMapCatalog` (`TmxMapCatalog`: every map of `GameMaps` loaded from the `.tmx` files copied next
to the host from `assets/Maps/`, walk grid and exits from `MapSpec` so server and client collide and open doors
identically; a map id from a client is only ever a key into that list, never a path).

Private places ([ADR-015](../docs/adr/ADR-015-private-instances-and-placeables.md)): the farm and the farmhouse are one copy per player. A place is a `MapInstance` (map id plus owner; `Key` is the map id
for a shared map), `IMapPresence` groups players by that key (`PlayerPresence.Key`; use it, not `MapId`, to reach "the players on my map"), `PlayerService` enters a player into their own
copy of a private map through `IInstanceAccessPolicy` (`VisitAccess`, [ADR-019](../docs/adr/ADR-019-visiting-private-instances.md); `OwnerOnlyAccessPolicy` is the no-visiting policy tests can use), and the per-place data (`farm_plots`, `placed_objects`) is keyed by (map, owner).
Furniture: `IPlaceableService` (`Placeables/PlaceableService`) places, moves and picks up pieces in the owner's farmhouse with `PlacedSet`/`PlaceableCatalog` from `Nosum.Gameplay`, in one transaction with the inventory change,
and pushes `PlacedChanged` to the instance; it is also the `IInstanceWorld` that gives movement validation the grid with the pieces' footprints. Handlers: `Nosum.Handlers/Placeable` (`USER`, rate limited).
Resource nodes ([ADR-021](../docs/adr/ADR-021-tools-resource-nodes-ground-items.md)): `IResourceNodeService` (`Nodes/ResourceNodeService`) owns the trees, stumps, twigs, stones, ore nodes, weeds and grass of each instance: loaded once, seeded once from the map's `Paths` layer (`node_instances` marks an instance seeded), kept in `NodeField` chunk buckets and written through when a node is created, grows, changes or breaks; hits on a standing node live in memory. It is also the node layer of `IInstanceWorld` (`Placeables/InstanceWorld` joins it with the furniture layer) so blocking nodes join the grid movement is validated on. Tools reach it through `FarmService.UseAsync` (slot, map, reach, then the 300 ms swing cooldown of `IToolState`, then the node layer, then the farm layer). A breaking node's yield leaves through the one seam `INodeYieldSink` (`InventoryYieldSink`: the breaker's inventory, in the same transaction as the node's row; ground items replace it). `IToolState` keeps the cooldown and the watering can's 40 units per player in memory. Public maps regrow a few weeds, stones and twigs and trees grow when a game day ends (`IResourceNodeService.AdvanceDayAsync`; `CalendarGameDayClock` counts game days on the server calendar and `NodeDayService` calls the seam when the day changes). Handler: `Nosum.Handlers/Node` (`GetNodes`). Tables: `resource_nodes`, `node_instances` (migration `ResourceNodes`).
Tables: `placed_objects` and the owner column of `farm_plots` (migration `PrivateInstancesAndPlacedObjects`, which gives existing plots to the player who last worked them).

Visiting ([ADR-019](../docs/adr/ADR-019-visiting-private-instances.md)): `IVisitService` (`Visits/VisitService`) keeps the owner's allow-list and co-op setting (`visit_grants`, `visit_settings`, migration `VisitingAllowList`) and carries out
`VisitPlayer` through `IPlayerService.TravelAsync`; `VisitAccess` is the `IInstanceAccessPolicy` (reads PostgreSQL on every entry, `VisitRules.MayEnter` decides). Removing a player, turning the co-op setting off and `ICoopService.MembershipEnded`
sweep the visitors who may no longer stay to the Town. `FarmService`, `PlaceableService` and `MarketService.ShipAsync` ask `VisitRules.MayDo`, so a visitor can only walk and water. Handlers: `Nosum.Handlers/Visit` (`USER`, rate limited).

Ground items ([ADR-021](../docs/adr/ADR-021-tools-resource-nodes-ground-items.md) D5-D6): `IGroundItems` (`Infrastructure/GroundItems/GroundItemService`) is where anything that "pops" an item drops it
(`DropAsync`, or `Stage` + `CompleteAsync` to save the drop in the caller's own transaction: a harvest resets the plot and leaves the produce in one commit). An instance's items are loaded once and kept in
16 x 16 tile chunk buckets; `PlayerService.MoveAsync` calls `OnPlayerMovedAsync` after every accepted move, which looks only at the nine chunks around the player, claims what is in range and allowed
(`GroundItemRules`), and moves it into the inventory with one `ITransferService` transfer that deletes the row in the same transaction (reason `ground.pickup`); a full inventory leaves the item (retry pause 2 s),
an in-memory claim makes two players racing for one item a clean win for one. The pickup runs off the movement path. `GroundItemExpiryService` (hosted, every 30 s) removes items past their expiry.
Publishing goes through one private method, `PublishAsync`, which today multicasts to the instance (chunk interest, ADR-022 D4, replaces its body). `IYieldRandom` hands out seeded `GameRandom`s for yields.
Handler: `Nosum.Handlers/GroundItem` (`GetGroundItems`, `USER`, rate limited). Table: `ground_items` (migration `AddGroundItems`).

Social layer ([ADR-013](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-014](../docs/adr/ADR-014-coops-and-looks.md)):
`ITransferService` (`Infrastructure/Economy`) is the one way coins move: items and coins between players and co-op warehouses in one transaction, built on
`IInventoryService.ApplyManyAsync` (every involved player's lock, one unit of work, one save; a feature joins it with an `enlist` callback). On it sit `IMarketService`
(`Market/`: shipping bin, listings as escrow, buy, cancel, expiry; `MarketExpiryService` is the hosted sweep), `ITradeService` (`Trade/`: invitations and sessions in
memory, the pure `TradeSession` rules in `Nosum.Gameplay`), `ILookService` (`Looks/`: persisted look ids, wardrobe, announces through `IMapPresence`) and `ICoopService`
(`Coops/`: membership, invitations, the shared warehouse with its audit trail). New entities: wallets and coin log, market listings, appearances, co-ops, members, invites, stock and log
(migration `SocialLayer`). Handlers: `Nosum.Handlers/{Market,Look,Trade,Coop}`; everything that moves coins or items is `[PacketEncryption(true)]`.

Co-op land ([ADR-020](../docs/adr/ADR-020-coop-land.md)): the map `coopfarm` is an instance owned by a co-op (`MapInstance.OfCoop`). `PlayerService` finds the instance from `ICoopMembership`
(`Coops/CoopMembership`, a database lookup per call, never cached), `FarmService` re-checks membership on every action and writes a `coop.farm.*` `CoopLogEntry` in the action's transaction, and
`CoopService` calls `ICoopLandEviction` (implemented by `PlayerService`) after a leave or kick and wipes the plots when a co-op dissolves. No new table, migration or packet.

Skills: [`data-model-ef-core`](../.claude/skills/data-model-ef-core/SKILL.md),
[`nalix-server-hosting`](../.claude/skills/nalix-server-hosting/SKILL.md),
[`nalix-handlers`](../.claude/skills/nalix-handlers/SKILL.md),
[`gameplay-rules`](../.claude/skills/gameplay-rules/SKILL.md).

```bash
dotnet build server/Nosum.slnx
dotnet run --project server/Nosum.Host
```

## Running locally

1. Start PostgreSQL and create a role and database matching the development fallback
   (`NosumDatabase.DevelopmentConnectionString`: user `nosum`, database `nosum_dev`), or set
   `ConnectionStrings__Default` to your own connection string.
2. `dotnet run --project server/Nosum.Host` applies pending migrations, then listens on `:57300/ws/`.
3. In Production (`DOTNET_ENVIRONMENT=Production`) `ConnectionStrings__Default` is mandatory; the
   host refuses to start without it.

Integration tests need a PostgreSQL server that may create databases: `NOSUM_TEST_POSTGRES`
(admin connection string), default the local development server.

Current state: time sync, system control, `UseGroups()`, `UseSecureConnections()` (X25519 handshake;
the server key is generated on first start in Nalix's data directory, outside the repository),
account and player handlers behind `PermissionMiddleware` / `RateLimitMiddleware`, and the WebSocket
listener. Account handlers that carry a password or token are `[PacketEncryption(true)]`: plaintext
frames are refused, so a client must complete the handshake. Keep TLS at the proxy as well; pin
`ServerPublicKey` on clients before production (ADR-009 D11). Guests idle for 30 days are deleted
hourly (`GuestCleanupService`).

Items, farming and quests ([ADR-011](../docs/adr/ADR-011-inventory-farming-data-model.md)): `IInventoryService` (`Inventory/InventoryService`) is the one atomic,
audited, per-player-serialized way to change items (`ApplyAsync` with optional `enlist` work in the same transaction; starter kit on first use; pushes
`InventoryUpdate`). `IFarmService` (`Farm/FarmService`) validates `UseItem` (hotbar slot, tile, reach, tillable, held item) with `FarmRules`, changes the tile and the
items in one transaction, multicasts `FarmTilesChanged` to the map group and reports to quests; tilled tiles (`farm_plots`) are cached per map and written
through; only `MapSpec.Farmable` maps (the farm) accept it. `IQuestService` counts reported events, pushes `QuestUpdate` and raises `Completed`;
`RegistrationPromptOnQuestCompleted` unlocks the guest registration invitation. `IPlayerNotifier` pushes to a player's active connection. Handlers:
`InventoryHandlers`, `FarmHandlers`, `QuestHandlers` (`USER`, rate limited per address). `Program.BuildApplication(port, clock)` takes the clock crops grow by, so tests move time.
Tables: `player_inventories`, `inventory_slots`, `inventory_log`, `farm_plots`, `player_quests` (migration `InventoryFarmQuests`).

Seasons ([ADR-018](../docs/adr/ADR-018-seasons-and-server-calendar.md)): `Program` builds one `GameCalendar` (default epoch and 12 h game day, epoch override `NOSUM_CALENDAR_EPOCH`) and gives it to `FarmService` (planting by season, withered crops) and
`ShopService` (seeds only in season, `ShopStatus.OutOfSeason`); `ICalendarService` / `CalendarService` and `CalendarHandlers` (`0xD000`, USER) answer `GetCalendar` with the epoch, the day length and the server clock. No new table.

Shop, nicknames and tester grant ([ADR-013 amendments](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-009 amendments](../docs/adr/ADR-009-postgresql-persistence-and-accounts.md)):
`IShopService` (`Shops/ShopService`) prices a purchase with `ShopRules`, checks the player's own presence is at the shop's counter, and buys through `ITransferService`
(reason `shop.buy`); `ShopHandlers` (`0xC000`, encrypted, USER, rate limited). Every player has a nickname: `AccountService` validates and stores it (`NicknameRules`, unique index
`ix_players_normalized_nickname`, rename once per 24 h with a per-player lock; guests cannot rename, `NicknameRequiresAccount`; profanity is `NicknameProfane`), `INicknameDirectory` caches it for `LookService` (`LookRow.Nickname`), the market (`ListingRow.SellerNickname`)
and co-ops; `SetNickname` is in `AccountHandlers` and re-announces the look to the map, as does a guest upgrade that chose a nickname. Three shops (seed, clothing, furniture) are three `ShopCatalog` entries; `ShopService` requires the player to stand at a counter of the named shop. The market fee goes through `IFeeSink` (`CoinSink`: destroyed, owner decision 2026-10-25).
`DevHandlers` / `IDevGrantService` (`0xF000`) exist only when `Program.BuildApplication(port, clock, development: true)`, which `Main` sets only when `DOTNET_ENVIRONMENT` is
`Development`; run a local host for testers with `DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host`. Migration `PlayerNicknames`.
