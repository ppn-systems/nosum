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
the player leaves. While anyone is on a map the loop pushes each player a `MapSnapshot` every 100 ms that holds only the players
in their chunk interest set (see Chunks below); it stops by itself when the last player leaves. Handlers: `Nosum.Handlers/Player` (signed-in players
only). Maps come from `IMapCatalog` (`TmxMapCatalog`: every map of `GameMaps` loaded from the `.tmx` files copied next
to the host from `assets/Maps/`, walk grid and exits from `MapSpec` so server and client collide and open doors
identically; a map id from a client is only ever a key into that list, never a path).

Chunks ([ADR-022](../docs/adr/ADR-022-chunked-interest-management.md)): a chunk is 16 × 16 tiles of one instance and a player is interested in their chunk and its 8 neighbours (one chunk of hysteresis; the pure
`ChunkCoord`/`ChunkBounds`/`ChunkInterest` are in `Nosum.Gameplay/Maps`). `IChunkMap` (`Nosum.Abstractions/Presence`, implemented by `MapPresence` over `ChunkIndex`) is the per-instance index from chunk to the players
interested in it; it changes at a join, a leave and when the snapshot loop sees a player's chunk change, never per move. `IChunkPublisher` (`ChunkPublisher`) is the one door for world events: `PublishAsync(instance, chunk, packet)`
to the chunk's subscribers (the default), `PublishInstanceAsync(instance, packet)` to everybody on the instance (map-wide only). A feature with per-chunk state implements `IChunkStateContributor` (`FarmService`,
`PlaceableService`); `ChunkStreamer` sends a contributor-built `ChunkState` once when a chunk joins a player's interest set, a `ChunkLeft` when it leaves, a `LookChanged` when another player comes into sight and a
`PlayerLeft` when one goes. The snapshot loop (`PlayerService.Snapshots.cs`) builds each snapshot from the players standing in the receiver's chunks and does not allocate per tick.

Private places ([ADR-015](../docs/adr/ADR-015-private-instances-and-placeables.md)): the farm and the farmhouse are one copy per player. A place is a `MapInstance` (map id plus owner; `Key` is the map id
for a shared map), `IMapPresence` groups players by that key (`PlayerPresence.Key`; use it, not `MapId`, to reach "the players on my map"), `PlayerService` enters a player into their own
copy of a private map through `IInstanceAccessPolicy` (`VisitAccess`, [ADR-019](../docs/adr/ADR-019-visiting-private-instances.md); `OwnerOnlyAccessPolicy` is the no-visiting policy tests can use), and the per-place data (`farm_plots`, `placed_objects`) is keyed by (map, owner).
Furniture: `IPlaceableService` (`Placeables/PlaceableService`) places, moves and picks up pieces in the owner's farmhouse with `PlacedSet`/`PlaceableCatalog` from `Nosum.Gameplay`, in one transaction with the inventory change,
and pushes `PlacedChanged` to the subscribers of the object's chunk (and is an `IChunkStateContributor`); it is also the `IInstanceWorld` that gives movement validation the grid with the pieces' footprints. Handlers: `Nosum.Handlers/Placeable` (`USER`, rate limited).
Resource nodes ([ADR-021](../docs/adr/ADR-021-tools-resource-nodes-ground-items.md)): `IResourceNodeService` (`Nodes/ResourceNodeService`) owns the trees, stumps, twigs, stones, ore nodes, weeds and grass of each instance: loaded once, seeded once from the map's `Paths` layer (`node_instances` marks an instance seeded), kept in `NodeField` chunk buckets and written through when a node is created, grows, changes or breaks; hits on a standing node live in memory. It is also the node layer of `IInstanceWorld` (`Placeables/InstanceWorld` joins it with the furniture layer) so blocking nodes join the grid movement is validated on. Tools reach it through `FarmService.UseAsync` (slot, map, reach, then the 300 ms swing cooldown of `IToolState`, then the node layer, then the farm layer). A breaking node's yield leaves through the one seam `INodeYieldSink` (`InventoryYieldSink`: the breaker's inventory, in the same transaction as the node's row; ground items replace it). `IToolState` keeps the cooldown and the watering can's 40 units per player in memory. Public maps regrow a few weeds, stones and twigs and trees grow when a game day ends (`IResourceNodeService.AdvanceDayAsync`; `CalendarGameDayClock` counts game days on the server calendar and `NodeDayService` calls the seam when the day changes). Node changes go to the subscribers of the node's chunk (`IChunkPublisher`, one packet per chunk when a regrowth touches several), the chunk's nodes join `ChunkState` when the chunk enters a player's interest set (the service is an `IChunkStateContributor`), and the `GetNodes` reply holds only the interest set. Handler: `Nosum.Handlers/Node` (`GetNodes`). Tables: `resource_nodes`, `node_instances` (migration `ResourceNodes`).

Placed objects: `IPlaceableService` (`Placeables/PlaceableService`) places, moves and picks up pieces in the owner's farmhouse, farm or co-op land (members only, audited) with `PlacedSet`/`PlaceableCatalog` from `Nosum.Gameplay`, in one transaction with the inventory change,
and pushes `PlacedChanged` to the instance; it is also the `IInstanceWorld` that gives movement validation the grid with the pieces' footprints. Handlers: `Nosum.Handlers/Placeable` (`USER`, rate limited).
Crafting, chests and furnaces ([ADR-024](../docs/adr/ADR-024-crafting-and-outdoor-placeables.md)): `ICraftingService` (`Crafting/CraftingService`) checks the unlock through `ISkillLevels` (`FixedSkillLevels` answers 10 until the skills of ADR-023 exist) and makes one `ApplyAsync` (inputs out, outputs in).
`IChestService` (`Placeables/ChestService`) and `IFurnaceService` (`FurnaceService`) are `IPlacedBehaviour`s attached to the placeables: they join a pick-up (a chest with items or a furnace with a job refuses it). A chest is one more inventory owner (`ContainerIds.ForChest`, same tables, capacity 36, no starter kit): a move is one `IInventoryService.ApplyManyAsync` over the player and the chest plus a `chest_log` row.
A furnace job is a `furnace_jobs` row read against the server clock and `FurnaceRules`. `PlacedTiles` (`IPlacedLookup`) is the lock-free index of what is placed where, read by `FarmService` (no tilling under a piece), `ResourceNodeService` and the growth rule (`IsSprinklerWatered`); `IFarmTiles` and `INodeTiles` are what placement asks back. `InstanceWorld` joins the node and placed grids on the farm.
Handlers: `Nosum.Handlers/Placeable` (`CraftingHandlers`, `ChestHandlers`, `FurnaceHandlers`). Tables: `chest_log`, `furnace_jobs` (migration `CraftingChestsFurnaces`).
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
Publishing goes through one private method, `PublishAsync`, which sends to the subscribers of the item's chunk (`IChunkPublisher`, ADR-022 D4); the service is also an `IChunkStateContributor` (a chunk's items are sent when it joins a player's interest set) and the `GetGroundItems` reply covers the interest set only. `IYieldRandom` hands out seeded `GameRandom`s for yields.
Handler: `Nosum.Handlers/GroundItem` (`GetGroundItems`, `USER`, rate limited). Table: `ground_items` (migration `AddGroundItems`).

Social layer ([ADR-013](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-014](../docs/adr/ADR-014-coops-and-looks.md)):
`ITransferService` (`Infrastructure/Economy`) is the one way coins move: items and coins between players and co-op warehouses in one transaction, built on
`IInventoryService.ApplyManyAsync` (every involved player's lock, one unit of work, one save; a feature joins it with an `enlist` callback). On it sit `IMarketService`
(`Market/`: shipping bin, listings as escrow, buy, cancel, expiry; `MarketExpiryService` is the hosted sweep), `ITradeService` (`Trade/`: invitations and sessions in
memory, the pure `TradeSession` rules in `Nosum.Gameplay`), `ILookService` (`Looks/`: persisted look ids, wardrobe, announces to the players who can see the player through `IChunkPublisher`) and `ICoopService`
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
items in one transaction, publishes `FarmTilesChanged` (and the day sweep's removed rows, `FarmTilesCleared` for a pickaxe) to the subscribers of the tile's chunk and reports to quests; tilled tiles (`farm_plots`) are cached per map and written
through; only `MapSpec.Farmable` maps (the farm) accept it. `IQuestService` counts reported events, pushes `QuestUpdate` and raises `Completed`;
`RegistrationPromptOnQuestCompleted` unlocks the guest registration invitation. `IPlayerNotifier` pushes to a player's active connection. Handlers:
`InventoryHandlers`, `FarmHandlers`, `QuestHandlers` (`USER`, rate limited per address). `Program.BuildApplication(port, clock)` takes the clock crops grow by, so tests move time.
Tables: `player_inventories`, `inventory_slots`, `inventory_log`, `farm_plots`, `player_quests` (migration `InventoryFarmQuests`).

Seasons ([ADR-018](../docs/adr/ADR-018-seasons-and-server-calendar.md)): `Program` builds one `GameCalendar` (default epoch and 12 h game day, epoch override `NOSUM_CALENDAR_EPOCH`) and gives it to `FarmService` (planting by season, withered crops) and
`ShopService` (seeds only in season, `ShopStatus.OutOfSeason`); `ICalendarService` / `CalendarService` and `CalendarHandlers` (`0xD000`, USER) answer `GetCalendar` with the epoch, the day length and the server clock. No new table.

Shop, nicknames and tester grant ([ADR-013 amendments](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-009 amendments](../docs/adr/ADR-009-postgresql-persistence-and-accounts.md)):
`ICommunityGoalService` (`Community/CommunityGoalService`, [ADR-030](../docs/adr/ADR-030-community-goals-and-market-limits.md)) counts deliveries at the Town board: one transfer takes the items and bumps the goal's total and the player's contribution in the same transaction (an in-process lock per goal and the total as a concurrency token keep concurrent deliveries exact), the delivery that reaches the target also completes the goal and writes its unlock row, and each contributor is then paid once (their `PaidCoins` is still empty inside the payout transfer; the one `PayAsync` call is where a mail service would take over). Open goals of a finished season expire on the day clock (and at start) and pay half the bin price per delivered item; `ICommunityUnlocks.IsUnlocked(key)` is the flag other features ask. `IMarketService.GetPriceHistoryAsync` reads the `market_sales` rows the buy transaction writes.

`IShopService` (`Shops/ShopService`) prices a purchase with `ShopRules`, checks the player's own presence is at the shop's counter, and buys through `ITransferService`
(reason `shop.buy`); `ShopHandlers` (`0xC000`, encrypted, USER, rate limited). Every player has a nickname: `AccountService` validates and stores it (`NicknameRules`, unique index
`ix_players_normalized_nickname`, rename once per 24 h with a per-player lock; guests cannot rename, `NicknameRequiresAccount`; profanity is `NicknameProfane`), `INicknameDirectory` caches it for `LookService` (`LookRow.Nickname`), the market (`ListingRow.SellerNickname`)
and co-ops; `SetNickname` is in `AccountHandlers` and re-announces the look to the map, as does a guest upgrade that chose a nickname. Three shops (seed, clothing, furniture) are three `ShopCatalog` entries; `ShopService` requires the player to stand at a counter of the named shop. The market fee goes through `IFeeSink` (`CoinSink`: destroyed, owner decision 2026-10-25).
`DevHandlers` / `IDevGrantService` (`0xF000`) exist only when `Program.BuildApplication(port, clock, development: true)`, which `Main` sets only when `DOTNET_ENVIRONMENT` is
`Development`; run a local host for testers with `DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host`. Migration `PlayerNicknames`.

Growth in game days ([ADR-021](../docs/adr/ADR-021-tools-resource-nodes-ground-items.md) D7): `FarmPlot` stores `growth_days`, `watered_day` and `tended_day` (migration `GrowthInGameDays`) and `FarmService` evaluates growth, wetness and bare-soil reverts lazily with `FarmRules`
and the calendar. `IDayClock` (`Calendar/DayClockService`) is the day-ended seam: one timer on the calendar's day boundaries raises `DayEnded` for every day that ended; subscribers do side effects only
(`FarmService` sweeps reverted tiles). In Development `Program` wraps the clock in `DevTimeProvider`, and `DevHandlers` / `IDevClockService` (`DevAdvanceClock`, `0xF001`) skip 1 to 60 game days; Production has neither.

Chat, friends and mail ([ADR-029](../docs/adr/ADR-029-chat-friends-mail.md)): `IChatService` (`Chat/ChatService`) cleans and masks text with `ChatRules.Prepare`, applies the exact rate rule (`ChatRateLimiter`, 5 per 10 s, burst 3; `[PacketRateLimit]` is only the coarse transport guard), skips recipients who blocked the sender and keeps a 20-line ring per player that a report copies into `chat_reports`/`chat_report_lines`. Local chat goes to the sender's map instance (`ChatService.CollectLocal` is the one place to switch to the chunk interest set of ADR-022). `IBlockList` (`Chat/BlockList`, `blocked_players`) is cached per owner; `TradeService`, `CoopService.InviteAsync` and `VisitAccess` consult it. `IFriendService` (`Friends/FriendService`, `friendships` two rows per friendship, `friend_requests`, `visit_settings.friends_may_visit`) pushes `FriendStatusChanged` from `IMapPresence.PlayerJoined`/`PlayerLeft` (750 ms grace so a map change is not "offline") and raises `FriendVisitsRevoked`, which `VisitService` answers with a sweep. `IMailService` (`Mail/MailService`, `mail_messages`, `mail_attachment_items`; migration `ChatFriendsMail`) is what other features call (`SendAsync(playerId, code, args, attachments)`); an attachment is collected by one `ITransferService` call whose enlistment stamps `collected_at` (a concurrency token); `MailExpiryService` purges mail after 30 days. First producers: market sale (`MarketService`) and co-op invitation (`CoopService`). Handlers: `Nosum.Handlers/{Chat,Friends,Mail}`.

Operations ([ADR-031](../docs/adr/ADR-031-deployment-and-operations.md); runbook: [`docs/ops/`](../docs/ops/README.md)): `Program.Main` with arguments is a command line, not a server:
`admin grant|revoke|list <nickname>` (`Ops/HostCommands`) is the only way the `Admin` role (`players.role`) changes - never a packet - and `healthcheck` asks the running
server's `/readyz` (the container health check). Without arguments the host starts `Ops/OpsEndpoint` first, an `HttpListener` on `NOSUM_OPS_PORT` (default 57301, bound to
`NOSUM_OPS_HOST`, default `localhost`; the container sets `+`): `/healthz` (process up), `/readyz` (`NosumDatabase.IsReadyAsync`: database answers, no migration pending) and
`/metrics` (`Ops/PrometheusExporter`, a `MeterListener` over the `Nosum` meter, `NosumMetrics` in `Nosum.Abstractions/Observability`: players online and per map, snapshot tick duration
and bytes, handler latency per opcode via `Ops/MetricsMiddleware`, database save duration, rejected intents by area and status). Logs are JSON lines through `OpsLog` (the same
folder): fields named like a secret are redacted, e-mail addresses and token-shaped strings are masked, and `Console.Error` is routed into it. Migrations run under a PostgreSQL advisory
lock (`NosumDatabase.MigrateAsync`). Admin intents (`AdminHandlers`, `0x1100` slice, `SYSTEM_ADMINISTRATOR`, encrypted, rate limited) call `IAdminService` (`Admin/AdminService`),
which re-reads the caller's role on every call, refuses self and other admins as targets, changes the row and writes an `admin_audit` row in one transaction, then acts on the live state
(`ISessionRegistry.EndAsync` for kick and ban, the in-memory `MuteList` that `ChatService` asks, which `Program` loads from `players.muted_until` at start). `AccountService` refuses a banned
account at login and resume (`AccountStatus.Banned`, after the password check). `NOSUM_LOAD_TEST=1` (only with `DOTNET_ENVIRONMENT` Staging or Development, otherwise the host
refuses to start) leaves out `RateLimitMiddleware` so `bench/Nosum.Bench.Load` is not throttled as one address. Migration `AdminRoleBanMuteAudit`.
