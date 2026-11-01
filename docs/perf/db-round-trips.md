# Database round trips per client packet

Measured in phase 1 and phase 2 of the database budget ([ADR-035](../adr/ADR-035-database-round-trip-budget.md)). Every number is the whole cost of **one request**
served by the real host over a real WebSocket against a real PostgreSQL, including the work the server leaves running after its reply (pushes, saves).

## How it is measured

- `tests/Nosum.IntegrationTests/Server/DbBudget/DbCommandCounter.cs` is an EF Core interceptor attached to the host's database options. It exists only in
  the test project; the production host registers no interceptor.
- `DbBudgetRun` runs one scenario per client packet (and for the connection and background costs) against one host. A scenario prepares its state
  unmeasured, then measures one call: the window opens when the database has been quiet for 120 ms and closes when it has been quiet for 220 ms.
- The same run proves coherence: after every measured row, and after the delayed writes were made, each copy a cache holds for a signed-in player is compared with what a service with nothing in memory reads from the rows (`DbBudgetTests.Run_CachedStateEqualsTheRows_AfterEveryMeasuredRow`).
- Re-run it and write this table with
  `NOSUM_DB_BUDGET_TABLE=/tmp/table.md NOSUM_DB_BUDGET_STATEMENTS=/tmp/statements.txt dotnet test tests/Nosum.IntegrationTests --filter FullyQualifiedName~DbBudgetTests`
  (the statements file lists every distinct SQL statement per row with its count). The run takes about five minutes because requests are paced for the
  per-address rate limits. Two runs on the merged head gave identical numbers on every hot-path row (two background rows differed by one read).

## Columns

- **Reads** - executed `SELECT` commands. **Writes** - executed `INSERT`/`UPDATE`/`DELETE` commands (EF Core batches the statements of one `SaveChanges`
  into one command). **Tx** - explicit transactions; each adds a `BEGIN` and a `COMMIT` round trip that no command counter sees.
  **SaveChanges** - calls, including calls that find nothing to write (those cost no round trip).
- Round trips on the wire are about `reads + writes + 2 x Tx`.
- **Repeated** - commands that ran a statement already run once in the same request (the same lookup again, or an N+1 loop).
- **Reply** - the status the server answered with. A refusal costs less than the real thing, so check it: `Eat` was measured with full energy,
  `ConfirmPasswordReset` with a wrong code (the real code goes out by e-mail), `RideElevator` to the entrance it is already at, `UseItemArea` refused as `InvalidArea`, `MoveObject` out of reach (rows marked so are refusal costs, not the success path).
- Rows named "asked again" are the second call of a read; the first call of several reads creates a row (`GetQuests`, `GetLooks`, `GetWallet`, `GetGoals`).
- Instance state (plots, objects, nodes, ground items, buildings) is loaded when an instance is first entered; "first join" and the first exit into a
  map include that one-off load (about 5 reads); later joins do not.

## Headline numbers

- Phase 2 on `master` merged into `feat/db-budget-2`. The numbers are the whole cost of one request after the caches of ADR-035 D3-D7 are warm for the player; "first" rows pay the cold read of a cache once per signed-in player.
- **Still free:** `MoveIntent` (also into a new chunk), `GetFarm`, `GetNodes`, `GetGroundItems`, `GetPlaced`, `GetCalendar`, local chat, the connect and handshake: 0 commands.
- **Now also free once read:** `GetInventory`, `Eat`, `GetRecipeBook`, a swing's inventory/energy/quest checks, `GetQuests` asked again, `GetFriends`, `GetVisitors` (one read for the allow-list the first time), `SelectSlot` (0 writes; one write per player and five seconds in the background), `ReadMail` (0 writes), `AddVisitor` (0 reads), `RespondFriend` and `RemoveFriend` (1 read).
- **Per connection:** a signed-in player's caches start cold and fill on first use; `JoinMap` returning is 3 reads: the saved place (the leave writes it and evicts the player's copy, so the join reads the row once; the statement dump shows one `SELECT ... FROM player_states`), then the inventory and its starter-kit backfill (2). Friends fill on their first use., the first join into a cold instance reads that instance's tables once.
- **Largest after phase 2:** `UpgradeCoop` 11 reads, `KickMember` and `LeaveCoop` 10, `JoinMap` first join 9, `RequestFriend` 8, `VisitPlayer` 8 (5 of them load the cold instance), `RespondInvite` 8, `WithdrawItem` 7, `InvitePlayer` 7, `DepositItem` 6, `BuyListing` 6.
- **Background (nothing due):** unchanged; a flush of write-behind rows is 1 write per player with something waiting (`WriteBehindFlush`).

### Before and after (phase 1 to phase 2, reads / writes / explicit transactions)

| Request | Before (reads / writes / tx) | After (reads / writes / tx) |
|:--|:--|:--|
| VisitPlayer / friend visits the farm | 24 / 3 / 1 | 8 / 3 / 1 |
| UseItem / harvest a mature crop | 17 / 4 / 3 | 5 / 4 / 3 |
| ShipItems / ship a stack at the bin | 16 / 3 / 3 | 3 / 3 / 3 |
| DepositItem / into the warehouse | 20 / 1 / 1 | 6 / 1 / 1 |
| WithdrawItem / out of the warehouse | 20 / 1 / 1 | 7 / 1 / 1 |
| BuyFromShop / buy seeds | 14 / 3 / 3 | 3 / 3 / 3 |
| UseItem / till (hoe) | 12 / 3 / 3 | 3 / 3 / 3 |
| BuyListing / buy one listing | 16 / 2 / 1 | 6 / 2 / 1 |
| RespondInvite / accept | 16 / 1 / 1 | 8 / 1 / 1 |
| RespondInvite / accept again | 16 / 1 / 1 | 8 / 1 / 1 |
| UpgradeCoop / warehouse to level 1 | 16 / 1 / 1 | 11 / 1 / 1 |
| Craft / a chest from wood | 10 / 3 / 3 | 2 / 3 / 3 |
| UseItem / water | 12 / 2 / 2 | 2 / 2 / 2 |
| UseItem / break a one-hit node | 9 / 3 / 3 | 4 / 3 / 3 |
| RespondFriend / accept | 14 / 1 / 1 | 1 / 1 / 1 |
| JoinMap / first join, no saved place | 10 / 2 / 2 | 9 / 2 / 2 |
| RequestFriend / by nickname | 15 / 1 / 0 | 8 / 1 / 0 |
| TradeConfirm / second side confirms: the swap | 12 / 1 / 1 | 2 / 1 / 1 |
| RemoveFriend / unfriend | 12 / 1 / 1 | 1 / 1 / 1 |
| ChestMove / five parsnips into the chest | 11 / 1 / 1 | 1 / 1 / 1 |
| ChestMove / one parsnip back out | 11 / 1 / 1 | 1 / 1 / 1 |
| UpgradeTool / hoe to copper | 11 / 1 / 1 | 4 / 1 / 1 |
| BuyAnimal / a chicken | 9 / 2 / 1 | 4 / 2 / 1 |
| CollectProduce / an egg | 7 / 2 / 2 | 5 / 2 / 2 |
| DeliverToGoal / deliver parsnips | 10 / 1 / 1 | 8 / 1 / 1 |
| GrantItem / items and coins to a player | 8 / 2 / 1 | 3 / 2 / 1 |
| FeedAnimals / hay into the trough | 9 / 1 / 1 | 5 / 1 / 1 |
| DeliverRequest / fill one request | 9 / 1 / 1 | 4 / 1 / 1 |
| InvitePlayer / invite a neighbour | 10 / 2 / 0 | 7 / 2 / 0 |
| InvitePlayer / invite again | 10 / 2 / 0 | 6 / 2 / 0 |
| KickMember / owner removes a member | 11 / 1 / 0 | 10 / 1 / 0 |
| PlaceObject / a coop blueprint | 7 / 2 / 1 | 3 / 2 / 1 |
| MoveIntent / step onto an exit: change of map | 5 / 3 / 1 | 2 / 3 / 1 |
| UseItem / plant a seed | 6 / 2 / 1 | 1 / 2 / 1 |
| CommissionBuilding / a coop blueprint | 7 / 1 / 1 | 2 / 1 / 1 |
| CreateListing / first listing | 7 / 1 / 1 | 3 / 1 / 1 |
| CreateListing / second listing | 7 / 1 / 1 | 3 / 1 / 1 |
| CollectTool / a finished upgrade | 6 / 1 / 1 | 4 / 1 / 1 |
| UseItem / water on a new game day | 5 / 1 / 1 | 1 / 1 / 1 |
| CancelListing / cancel own listing | 5 / 1 / 1 | 3 / 1 / 1 |
| MoveSlot / split a stack into a free slot | 4 / 1 / 1 | 0 / 1 / 1 |
| PlaceObject / a chest | 4 / 1 / 1 | 0 / 1 / 1 |
| PlaceObject / a furnace | 4 / 1 / 1 | 0 / 1 / 1 |
| PlaceObject / a fence | 4 / 1 / 1 | 0 / 1 / 1 |
| StartSmelting / five copper ore | 4 / 1 / 1 | 2 / 1 / 1 |
| CollectFurnace / a finished batch | 4 / 1 / 1 | 2 / 1 / 1 |
| Unban / a banned player | 3 / 1 / 1 | 2 / 1 / 1 |
| GetWallet / wallet | 3 / 1 / 1 | 1 / 1 / 1 |
| EquipItem / wear a shirt | 3 / 1 / 1 | 1 / 1 / 1 |
| UnequipItem / take the shirt off | 3 / 1 / 1 | 1 / 1 / 1 |
| BlockPlayer / block by nickname | 5 / 1 / 0 | 4 / 1 / 0 |
| GetCoop / two members, stock and log | 6 / 0 / 0 | 3 / 0 / 0 |
| PickUpObject / pick up a fence | 3 / 1 / 1 | 1 / 1 / 1 |
| Craft / five fences | 3 / 1 / 1 | 0 / 1 / 1 |
| Kick / a connected player | 3 / 2 / 0 | 2 / 2 / 0 |
| Eat / a parsnip from the hotbar | 5 / 0 / 0 | 0 / 0 / 0 |
| TradeSetOffer / items and coins | 5 / 0 / 0 | 1 / 0 / 0 |
| GetFriends / no friends | 5 / 0 / 0 | 3 / 0 / 0 |
| GetFriends / no friends, asked again | 5 / 0 / 0 | 0 / 0 / 0 |
| GetFriends / one request waiting | 5 / 0 / 0 | 0 / 0 / 0 |
| GetFriends / one friend | 5 / 0 / 0 | 0 / 0 / 0 |
| AddVisitor / allow-list by nickname | 4 / 1 / 0 | 0 / 1 / 0 |
| KickVisitor / send the visitor away | 3 / 2 / 0 | 0 / 2 / 0 |
| JoinMap / returning player, saved place | 4 / 0 / 0 | 3 / 0 / 0 |
| GetCoop / an invitation waiting | 4 / 0 / 0 | 3 / 0 / 0 |
| GetWallet / wallet, asked again | 3 / 0 / 0 | 1 / 0 / 0 |
| SelectSlot / hotbar slot 1 | 2 / 1 / 0 | 0 / 0 / 0 |
| UnblockPlayer / unblock | 2 / 1 / 0 | 1 / 1 / 0 |
| SetFriendsVisit / allow friends to visit | 2 / 1 / 0 | 1 / 1 / 0 |
| ReadMail / mark read | 2 / 1 / 0 | 2 / 0 / 0 |
| GetVisitors / no visitors | 3 / 0 / 0 | 1 / 0 / 0 |
| GetVisitors / no visitors, asked again | 3 / 0 / 0 | 0 / 0 / 0 |
| GetVisitors / one visitor on the farm | 3 / 0 / 0 | 0 / 0 / 0 |
| UseItem / plant on untilled soil (refused) | 2 / 0 / 0 | 0 / 0 / 0 |
| UseItemArea / watering can over a block of tiles | 2 / 0 / 0 | 0 / 0 / 0 |
| GetInventory / starter kit | 2 / 0 / 0 | 0 / 0 / 0 |
| GetInventory / starter kit, asked again | 2 / 0 / 0 | 0 / 0 / 0 |
| TradeSetOffer / items | 2 / 0 / 0 | 0 / 0 / 0 |
| GetCoop / no co-op, asked again | 2 / 0 / 0 | 1 / 0 / 0 |
| GetRecipeBook / new player | 2 / 0 / 0 | 0 / 0 / 0 |
| GetRecipeBook / new player, asked again | 2 / 0 / 0 | 0 / 0 / 0 |
| SetCoopVisits / co-op members may visit | 2 / 0 / 0 | 1 / 0 / 0 |
| GetBuildings / own, none built | 1 / 0 / 0 | 0 / 0 / 0 |
| GetBuildings / own, none built, asked again | 1 / 0 / 0 | 0 / 0 / 0 |
| GetQuests / new player, asked again | 1 / 0 / 0 | 0 / 0 / 0 |
| MarketExpiry / every minute, nothing due | 1 / 0 / 0 | 2 / 0 / 0 |

The rows not listed did not change (the free ones above, the account flows, the admin tools, the background passes). Reads fell by about half over the rows both tables measure (725 to 345). Writes and transactions are unchanged except `SelectSlot` and `ReadMail`, which write in the background now: every committed change is still one transaction (ADR-013).

## Table

Phase 2 (all of ADR-035 D3-D7 built). The table of phase 1 is in the history of this file and in the before/after table below.

| Opcode | Packet / event | Case | Reply | Reads | Writes | Tx | SaveChanges | Repeated |
|:--|:--|:--|:--|--:|--:|--:|--:|--:|
| 0x1001 | Register | new account | Ok | 2 | 1 | 1 | 1 | 0 |
| 0x1002 | Login | e-mail and password | Ok | 2 | 1 | 1 | 1 | 0 |
| 0x1003 | ResumeSession | token, replacing an open session | Ok | 3 | 1 | 1 | 1 | 0 |
| 0x1003 | ResumeSession | token, no other session | Ok | 3 | 1 | 1 | 1 | 0 |
| 0x1004 | SetLanguage | registered player | Ok | 1 | 1 | 0 | 1 | 0 |
| 0x1005 | StartGuest | new guest | Ok | 1 | 1 | 1 | 1 | 0 |
| 0x1006 | UpgradeGuest | guest to registered | Ok | 5 | 1 | 1 | 1 | 0 |
| 0x1007 | Logout | joined player | Ok | 1 | 1 | 0 | 1 | 0 |
| 0x1008 | SetNickname | first change is free | Ok | 2 | 1 | 0 | 1 | 0 |
| 0x1101 | Kick | a connected player | Ok | 2 | 2 | 0 | 2 | 1 |
| 0x1102 | Mute | one hour | Ok | 2 | 1 | 1 | 1 | 1 |
| 0x1103 | Ban | a player | Ok | 2 | 1 | 1 | 1 | 1 |
| 0x1104 | Unban | a banned player | Ok | 2 | 1 | 1 | 1 | 1 |
| 0x1105 | GrantItem | items and coins to a player | Ok | 3 | 2 | 1 | 2 | 1 |
| 0x1106 | ListReports | one report | Ok | 4 | 0 | 0 | 0 | 2 |
| 0x1107 | ReadReport | one report with its chat lines | Ok | 3 | 0 | 0 | 0 | 0 |
| 0x11EE | ChangePassword | right current password | Ok | 3 | 1 | 1 | 1 | 0 |
| 0x11EF | ConfirmPasswordReset | wrong code (refused) | InvalidResetCode | 0 | 0 | 0 | 0 | 0 |
| 0x11F0 | RequestPasswordReset | registered address | Ok | 3 | 1 | 0 | 1 | 0 |
| 0x2000 | JoinMap | first join, no saved place | Ok | 9 | 2 | 2 | 2 | 0 |
| 0x2000 | JoinMap | returning player, saved place | Ok | 3 | 0 | 0 | 0 | 0 |
| 0x2001 | MoveIntent | one step, same chunk |  | 0 | 0 | 0 | 0 | 0 |
| 0x2001 | MoveIntent | step inside a chunk, another player in sight |  | 0 | 0 | 0 | 0 | 0 |
| 0x2001 | MoveIntent | step into a new chunk (interest set changes) |  | 0 | 0 | 0 | 0 | 0 |
| 0x2001 | MoveIntent | step onto an exit: change of map |  | 2 | 3 | 1 | 3 | 1 |
| 0x2002 | Eat | a parsnip from the hotbar | EnergyFull | 0 | 0 | 0 | 0 | 0 |
| 0x3000 | UseItem | plant on untilled soil (refused) | NotTilled | 0 | 0 | 0 | 0 | 0 |
| 0x3000 | UseItem | slot with nothing in it (refused) | InvalidSlot | 0 | 0 | 0 | 0 | 0 |
| 0x3000 | UseItem | till (hoe) | Ok | 3 | 3 | 3 | 3 | 0 |
| 0x3000 | UseItem | plant a seed | Ok | 1 | 2 | 1 | 2 | 0 |
| 0x3000 | UseItem | water | Ok | 2 | 2 | 2 | 2 | 0 |
| 0x3000 | UseItem | water on a new game day | Ok | 1 | 1 | 1 | 1 | 0 |
| 0x3000 | UseItem | harvest a mature crop | Ok | 5 | 4 | 3 | 4 | 1 |
| 0x3000 | UseItem | break a one-hit node | Ok | 4 | 3 | 3 | 3 | 0 |
| 0x3001 | GetFarm | empty farm |  | 0 | 0 | 0 | 0 | 0 |
| 0x3001 | GetFarm | empty farm, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x3002 | UseItemArea | watering can over a block of tiles | InvalidArea | 0 | 0 | 0 | 0 | 0 |
| 0x3100 | GetBuildings | own, none built |  | 0 | 0 | 0 | 0 | 0 |
| 0x3100 | GetBuildings | own, none built, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x3100 | GetBuildings | one coop standing |  | 2 | 0 | 0 | 0 | 0 |
| 0x3101 | CommissionBuilding | a coop blueprint | Ok | 2 | 1 | 1 | 1 | 0 |
| 0x3102 | BuyAnimal | a chicken | Ok | 4 | 2 | 1 | 2 | 0 |
| 0x3103 | GetAnimals | not inside a building |  | 0 | 0 | 0 | 0 | 0 |
| 0x3103 | GetAnimals | not inside a building, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x3103 | GetAnimals | inside, one chicken |  | 2 | 0 | 0 | 0 | 0 |
| 0x3104 | FeedAnimals | hay into the trough | Ok | 5 | 1 | 1 | 1 | 3 |
| 0x3105 | PetAnimal | pet the chicken | Ok | 4 | 1 | 0 | 1 | 2 |
| 0x3106 | CollectProduce | an egg | Ok | 5 | 2 | 2 | 2 | 2 |
| 0x4000 | GetWallet | wallet |  | 1 | 1 | 1 | 1 | 0 |
| 0x4000 | GetWallet | wallet, asked again |  | 1 | 0 | 0 | 1 | 0 |
| 0x4001 | ShipItems | ship a stack at the bin | Ok | 3 | 3 | 3 | 3 | 1 |
| 0x4002 | BrowseListings | empty market, page 0 |  | 2 | 0 | 0 | 0 | 0 |
| 0x4002 | BrowseListings | empty market, page 0, asked again |  | 2 | 0 | 0 | 0 | 0 |
| 0x4002 | BrowseListings | one listing on the page |  | 2 | 0 | 0 | 0 | 0 |
| 0x4003 | CreateListing | first listing | Ok | 3 | 1 | 1 | 1 | 0 |
| 0x4003 | CreateListing | second listing | Ok | 3 | 1 | 1 | 1 | 0 |
| 0x4004 | BuyListing | buy one listing | Ok | 6 | 2 | 1 | 2 | 2 |
| 0x4005 | CancelListing | cancel own listing | Ok | 3 | 1 | 1 | 1 | 1 |
| 0x4006 | GetMyListings | none |  | 1 | 0 | 0 | 0 | 0 |
| 0x4006 | GetMyListings | none, asked again |  | 1 | 0 | 0 | 0 | 0 |
| 0x4006 | GetMyListings | one listing |  | 1 | 0 | 0 | 0 | 0 |
| 0x4007 | GetPriceHistory | no sales |  | 1 | 0 | 0 | 0 | 0 |
| 0x4007 | GetPriceHistory | no sales, asked again |  | 1 | 0 | 0 | 0 | 0 |
| 0x4100 | GetGoals | goals |  | 5 | 1 | 0 | 1 | 0 |
| 0x4100 | GetGoals | goals, asked again |  | 5 | 0 | 0 | 0 | 0 |
| 0x4100 | GetGoals | at the board |  | 5 | 0 | 0 | 0 | 0 |
| 0x4101 | DeliverToGoal | deliver parsnips | Ok | 8 | 1 | 1 | 1 | 1 |
| 0x5000 | GetInventory | starter kit |  | 0 | 0 | 0 | 0 | 0 |
| 0x5000 | GetInventory | starter kit, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x5001 | MoveSlot | split a stack into a free slot | Ok | 0 | 1 | 1 | 1 | 0 |
| 0x5002 | SelectSlot | hotbar slot 1 | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x6000 | GetQuests | new player |  | 1 | 1 | 1 | 1 | 0 |
| 0x6000 | GetQuests | new player, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x6100 | GetRequests | today's board |  | 3 | 0 | 0 | 0 | 0 |
| 0x6100 | GetRequests | today's board, asked again |  | 2 | 0 | 0 | 0 | 0 |
| 0x6101 | DeliverRequest | fill one request | Ok | 4 | 1 | 1 | 1 | 1 |
| 0x7100 | GetNodes | farm |  | 0 | 0 | 0 | 0 | 0 |
| 0x7100 | GetNodes | farm, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x7200 | GetGroundItems | nothing on the ground |  | 0 | 0 | 0 | 0 | 0 |
| 0x7200 | GetGroundItems | nothing on the ground, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x8000 | GetLooks | own and seen players |  | 1 | 1 | 0 | 1 | 0 |
| 0x8000 | GetLooks | own and seen players, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x8001 | SetAppearance | skin, hair, dye | Ok | 1 | 0 | 0 | 1 | 0 |
| 0x8002 | EquipItem | wear a shirt | Ok | 1 | 1 | 1 | 1 | 0 |
| 0x8003 | UnequipItem | take the shirt off | Ok | 1 | 1 | 1 | 1 | 0 |
| 0x9000 | TradeRequest | invite a neighbour | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9000 | TradeRequest | second trade | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9001 | TradeRespond | accept | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9002 | TradeSetOffer | items and coins | Ok | 1 | 0 | 0 | 1 | 0 |
| 0x9002 | TradeSetOffer | items | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9003 | TradeConfirm | first side confirms | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9003 | TradeConfirm | second side confirms: the swap | Ok | 2 | 1 | 1 | 1 | 1 |
| 0x9004 | TradeCancel | cancel an open trade | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9100 | SendChat | map channel | Ok | 1 | 0 | 0 | 0 | 0 |
| 0x9100 | SendChat | whisper by nickname | Ok | 2 | 0 | 0 | 0 | 0 |
| 0x9100 | SendChat | local channel | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9100 | SendChat | map channel, one listener has blocked the sender | Ok | 1 | 0 | 0 | 0 | 0 |
| 0x9101 | BlockPlayer | block by nickname | Ok | 4 | 1 | 0 | 1 | 0 |
| 0x9102 | UnblockPlayer | unblock | Ok | 1 | 1 | 0 | 1 | 0 |
| 0x9103 | GetBlocked | no blocks |  | 1 | 0 | 0 | 0 | 0 |
| 0x9103 | GetBlocked | no blocks, asked again |  | 1 | 0 | 0 | 0 | 0 |
| 0x9103 | GetBlocked | one block |  | 1 | 0 | 0 | 0 | 0 |
| 0x9104 | ReportChat | report a player | NothingToReport | 1 | 0 | 0 | 0 | 0 |
| 0x9200 | GetFriends | no friends |  | 3 | 0 | 0 | 0 | 0 |
| 0x9200 | GetFriends | no friends, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x9200 | GetFriends | one request waiting |  | 0 | 0 | 0 | 0 | 0 |
| 0x9200 | GetFriends | one friend |  | 0 | 0 | 0 | 0 | 0 |
| 0x9201 | RequestFriend | by nickname | Ok | 8 | 1 | 0 | 1 | 3 |
| 0x9202 | RespondFriend | accept | Ok | 1 | 1 | 1 | 1 | 0 |
| 0x9203 | RemoveFriend | unfriend | Ok | 1 | 1 | 1 | 1 | 0 |
| 0x9204 | SetFriendsVisit | allow friends to visit | Ok | 1 | 1 | 0 | 1 | 0 |
| 0x9300 | GetMail | empty mailbox |  | 1 | 0 | 0 | 0 | 0 |
| 0x9300 | GetMail | empty mailbox, asked again |  | 1 | 0 | 0 | 0 | 0 |
| 0x9300 | GetMail | one sale notice |  | 2 | 0 | 0 | 0 | 0 |
| 0x9301 | ReadMail | mark read | Ok | 2 | 0 | 0 | 0 | 0 |
| 0x9302 | CollectMail | no attachment | NothingToCollect | 2 | 0 | 0 | 0 | 0 |
| 0x9303 | DeleteMail | delete | Ok | 3 | 1 | 0 | 1 | 0 |
| 0xA000 | CreateCoop | found a co-op | Ok | 7 | 1 | 1 | 1 | 0 |
| 0xA001 | GetCoop | no co-op |  | 2 | 0 | 0 | 0 | 0 |
| 0xA001 | GetCoop | no co-op, asked again |  | 1 | 0 | 0 | 0 | 0 |
| 0xA001 | GetCoop | an invitation waiting |  | 3 | 0 | 0 | 0 | 0 |
| 0xA001 | GetCoop | two members, stock and log |  | 3 | 0 | 0 | 0 | 0 |
| 0xA002 | InvitePlayer | invite a neighbour | Ok | 7 | 2 | 0 | 2 | 0 |
| 0xA002 | InvitePlayer | invite again | Ok | 6 | 2 | 0 | 2 | 0 |
| 0xA003 | RespondInvite | accept | Ok | 8 | 1 | 1 | 1 | 0 |
| 0xA003 | RespondInvite | accept again | Ok | 8 | 1 | 1 | 1 | 0 |
| 0xA004 | LeaveCoop | a member leaves | Ok | 10 | 1 | 0 | 1 | 2 |
| 0xA005 | KickMember | owner removes a member | Ok | 10 | 1 | 0 | 1 | 1 |
| 0xA006 | DepositItem | into the warehouse | Ok | 6 | 1 | 1 | 1 | 0 |
| 0xA007 | WithdrawItem | out of the warehouse | Ok | 7 | 1 | 1 | 1 | 0 |
| 0xA008 | UpgradeCoop | warehouse to level 1 | Ok | 11 | 1 | 1 | 1 | 2 |
| 0xB000 | GetPlaced | empty farm |  | 0 | 0 | 0 | 0 | 0 |
| 0xB000 | GetPlaced | empty farm, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0xB000 | GetPlaced | a chest and a furnace standing |  | 0 | 0 | 0 | 0 | 0 |
| 0xB001 | PlaceObject | a chest | Ok | 0 | 1 | 1 | 1 | 0 |
| 0xB001 | PlaceObject | a furnace | Ok | 0 | 1 | 1 | 1 | 0 |
| 0xB001 | PlaceObject | a fence | Ok | 0 | 1 | 1 | 1 | 0 |
| 0xB001 | PlaceObject | a coop blueprint | Ok | 3 | 2 | 1 | 1 | 0 |
| 0xB002 | MoveObject | a fence one tile over | OutOfReach | 0 | 0 | 0 | 0 | 0 |
| 0xB003 | PickUpObject | pick up a fence | Ok | 1 | 1 | 1 | 1 | 0 |
| 0xB100 | Craft | a chest from wood | Ok | 2 | 3 | 3 | 3 | 0 |
| 0xB100 | Craft | five fences | Ok | 0 | 1 | 1 | 1 | 0 |
| 0xB101 | GetRecipeBook | new player |  | 0 | 0 | 0 | 0 | 0 |
| 0xB101 | GetRecipeBook | new player, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0xB110 | OpenChest | empty chest | Ok | 1 | 1 | 0 | 1 | 0 |
| 0xB111 | CloseChest | close |  | 0 | 0 | 0 | 0 | 0 |
| 0xB112 | ChestMove | five parsnips into the chest | Ok | 1 | 1 | 1 | 1 | 0 |
| 0xB112 | ChestMove | one parsnip back out | Ok | 1 | 1 | 1 | 1 | 0 |
| 0xB120 | StartSmelting | five copper ore | Ok | 2 | 1 | 1 | 1 | 0 |
| 0xB121 | CollectFurnace | a finished batch | Ok | 2 | 1 | 1 | 1 | 1 |
| 0xB122 | GetFurnaces | no furnace |  | 0 | 0 | 0 | 0 | 0 |
| 0xB122 | GetFurnaces | no furnace, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0xB122 | GetFurnaces | one furnace smelting |  | 1 | 0 | 0 | 0 | 0 |
| 0xC000 | BuyFromShop | buy seeds | Ok | 3 | 3 | 3 | 3 | 1 |
| 0xC001 | GetBlacksmith | nothing in the forge | Ok | 1 | 0 | 0 | 0 | 0 |
| 0xC002 | UpgradeTool | hoe to copper | Ok | 4 | 1 | 1 | 1 | 1 |
| 0xC003 | CollectTool | a finished upgrade | Ok | 4 | 1 | 1 | 1 | 2 |
| 0xC100 | GetMine | in the entrance cave | Ok | 1 | 0 | 0 | 0 | 0 |
| 0xC101 | RideElevator | to the entrance, already there | AlreadyThere | 1 | 0 | 0 | 0 | 0 |
| 0xD000 | GetCalendar | calendar |  | 0 | 0 | 0 | 0 | 0 |
| 0xD000 | GetCalendar | calendar, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0xE000 | GetVisitors | no visitors |  | 1 | 0 | 0 | 0 | 0 |
| 0xE000 | GetVisitors | no visitors, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0xE000 | GetVisitors | one visitor on the farm |  | 0 | 0 | 0 | 0 | 0 |
| 0xE001 | AddVisitor | allow-list by nickname | Ok | 0 | 1 | 0 | 1 | 0 |
| 0xE002 | RemoveVisitor | take off the allow-list | Ok | 1 | 1 | 0 | 1 | 0 |
| 0xE003 | KickVisitor | send the visitor away | Ok | 0 | 2 | 0 | 2 | 1 |
| 0xE004 | SetCoopVisits | co-op members may visit | Ok | 1 | 0 | 0 | 1 | 0 |
| 0xE005 | VisitPlayer | friend visits the farm | Ok | 8 | 3 | 1 | 3 | 1 |
| 0xF000 | DevGrant | not measured | - | - | - | - | - | tester tool, Development only; its cost is test set-up |
| 0xF001 | DevAdvanceClock | not measured | - | - | - | - | - | tester tool, Development only; the day rollover it triggers is the DayClock row |
| - | WriteBehindFlush | a hotbar choice waiting |  | 0 | 1 | 0 | 1 | 0 |
| - | DayClock | one game day ends (nodes, goals) |  | 6 | 4 | 1 | 4 | 4 |
| - | AnimalWander | tick, one player inside a coop |  | 1 | 0 | 0 | 0 | 0 |
| - | GuestCleanup | hourly pass, nothing old |  | 0 | 3 | 0 | 0 | 0 |
| - | MarketExpiry | every minute, nothing due |  | 2 | 0 | 0 | 0 | 0 |
| - | MailExpiry | hourly pass, nothing due |  | 1 | 0 | 0 | 0 | 0 |
| - | GroundItemExpiry | every 30 s, nothing due |  | 0 | 0 | 0 | 0 | 0 |
| - | AnimalWander | tick, nobody inside any building |  | 0 | 0 | 0 | 0 | 0 |
| - | Connect | WebSocket + handshake |  | 0 | 0 | 0 | 0 | 0 |
| - | Disconnect | joined player, socket closed |  | 0 | 1 | 0 | 1 | 0 |

## Where the repeats came from (phase 1 findings)

These are the causes found in phase 1; [ADR-035](../adr/ADR-035-database-round-trip-budget.md) lists what phase 2 did about each.

The statements file shows the cause of each repeat; the common ones:

- **The inventory is loaded for every operation and every check.** `InventoryService` reads the header row and all slot rows for each call, including
  `PeekSlotAsync` ("what is in slot 3?"), and again inside the transaction that changes it. A swing loads it twice, `ShipItems` three times, `ChestMove`
  five times, a two-party transfer (`BuyListing`, `TradeConfirm`) twice per party.
- **Wallets are read once or twice per party per transfer** (`player_wallets`), and `GetWallet` still loads the inventory first (3 reads, also when asked again).
- **The player's `player_states` row is read separately by every service that needs a field:** the saved place (join, map change), energy and experience
  (every swing, `Eat`, `GetRecipeBook` once per skill, join). `JoinMap` reads it twice.
- **Access and social facts are re-read per decision:** `VisitAccess` reads grants, settings, blocks and friendships at each of its three evaluations
  in `VisitPlayer`; friend calls run seven count/exists queries and read `players` by nickname three times; co-op calls re-read the membership, the co-op
  row and the member list two to six times and rebuild the snapshot from five tables after the change.
- **N+1:** `CommunityGoalService.GetAsync` asked once per goal for its completed row (fixed in this change: 8 to 5 reads). `GetFriends` runs 5 queries for
  a list.
- **Quest hooks grew the shop path:** `BuyFromShop` rose from 6 to 14 reads on master because the purchase now completes a tutorial-quest step (ADR-036): the quest rows are created and the coin reward is a second transfer (inventory and wallet loaded four times). It is a first-time cost per quest step.
- **Reads that write:** the first `GetQuests`, `GetLooks`, `GetWallet` and `GetGoals` insert their rows; `SelectSlot` writes on every click.
- **Cold work:** the first entry to an instance reads five tables once per process; a game day ending reads and writes for the nodes and the goals.

Not measured here: concurrency (these are single-player requests), tables with many rows (queries are indexed lookups; `BrowseListings` runs a
`count(*)` plus one page), the cost of the client's own pushes, and the Development-only tester tools.

## Phase 3: coin-only parties and tighter limits (`claude/perf-server`)

Measured with the same harness, before and after the change (`NOSUM_DB_BUDGET_DUMP`, `NOSUM_DB_BUDGET_TABLE`), on the same machine.

- **A coin-only party no longer loads its inventory.** `TransferService` used to pass every party of a transfer to `ApplyManyAsync`, so the seller of a
  listing (who receives coins only) had their inventory read for nothing. Only parties with an item operation are passed now; when no party has one, one
  empty change is kept, because `ApplyManyAsync` returns before the enlistment that carries the coin legs when it has no change. Measured: `Disconnect` of a
  joined player 1 read to 0. Other rows are unchanged, which is what the before/after diff of all 197 rows shows.
- **Baseline limits that were looser than the same code measures are tightened** (14 rows: `BuyFromShop` buy seeds, `BuyListing` 7 to 6 reads,
  `CancelListing`, `ChestMove` one back out, `Craft` a chest and five fences, `JoinMap` first join, `KickVisitor`, `LinkExternalIdentity`,
  `UseItem` break and harvest, `VisitPlayer` friend visits, `WithdrawItem`, `Disconnect`). Only the `Disconnect` row moved because of
  the code; the others were already measured on master. Each is set to the measured value, so a regression fails the test.
- **Not checked by the budget, found while doing this:** `Trade / TradeConfirm` rows measure `TooSoon` on master and on this branch, so the swap is never
  executed by the scenario and its limit (2 reads, 1 write, 1 transaction) is not tested. The scenario needs a wait between the two confirmations. Left for a
  follow-up: it changes the scenario, not the server.
