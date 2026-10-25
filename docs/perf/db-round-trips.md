# Database round trips per client packet

Measured in phase 1 of the database budget ([ADR-035](../adr/ADR-035-database-round-trip-budget.md)). Every number is the whole cost of **one request**
served by the real host over a real WebSocket against a real PostgreSQL, including the work the server leaves running after its reply (pushes, saves).

## How it is measured

- `tests/Nosum.IntegrationTests/Server/DbBudget/DbCommandCounter.cs` is an EF Core interceptor attached to the host's database options. It exists only in
  the test project; the production host registers no interceptor.
- `DbBudgetRun` runs one scenario per client packet (and for the connection and background costs) against one host. A scenario prepares its state
  unmeasured, then measures one call: the window opens when the database has been quiet for 120 ms and closes when it has been quiet for 220 ms.
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

- Measured on `master` merged into this branch (admin tools ADR-031, account recovery ADR-032, mine and blacksmith ADR-026, market counter ADR-034 included).
- **Free:** `MoveIntent` (also into a new chunk, with another player in sight), `GetFarm`, `GetNodes`, `GetGroundItems`, `GetPlaced`, `GetCalendar`,
  local chat, the WebSocket connect and handshake: 0 commands.
- **Per connection:** `ResumeSession` 3 reads + 1 write (1 Tx); `Login` 2 / 1 / 1; `JoinMap` 9 / 2 / 2 Tx the first time, 4 / 0 afterwards; a step onto an exit (map change) 5 / 3 / 1 Tx;
  `Logout` 1 / 1; socket close of a joined player 1 / 1 (the saved place).
- **Top 10 by reads:** `VisitPlayer` 24, `WithdrawItem` 20, `DepositItem` 18, `BuyListing` 16, `RespondInvite` 16, `RequestFriend` 15, `RespondFriend` 14,
  `RemoveFriend` 12, `TradeConfirm` (swap) 12, `ChestMove` 11; then `UseItem` harvest 10 / 4 writes / 2 Tx.
- **Background (nothing due):** guest cleanup 1 write per pass, market and mail expiry 1 read each, ground item expiry 0, animal wander 0 (1 read with a
  player inside a coop); one game day ending costs 4 reads + 3 writes.

## Table

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
| 0x1101 | Kick | a connected player | Ok | 3 | 2 | 0 | 2 | 1 |
| 0x1102 | Mute | one hour | Ok | 2 | 1 | 1 | 1 | 1 |
| 0x1103 | Ban | a player | Ok | 2 | 1 | 1 | 1 | 1 |
| 0x1104 | Unban | a banned player | Ok | 3 | 1 | 1 | 1 | 1 |
| 0x1105 | GrantItem | items and coins to a player | Ok | 8 | 2 | 1 | 3 | 4 |
| 0x1106 | ListReports | one report | Ok | 4 | 0 | 0 | 0 | 2 |
| 0x1107 | ReadReport | one report with its chat lines | Ok | 3 | 0 | 0 | 0 | 0 |
| 0x11EE | ChangePassword | right current password | Ok | 3 | 1 | 1 | 1 | 0 |
| 0x11EF | ConfirmPasswordReset | wrong code (refused) | InvalidResetCode | 0 | 0 | 0 | 0 | 0 |
| 0x11F0 | RequestPasswordReset | registered address | Ok | 3 | 1 | 0 | 1 | 0 |
| 0x2000 | JoinMap | first join, no saved place | Ok | 9 | 2 | 2 | 2 | 1 |
| 0x2000 | JoinMap | returning player, saved place | Ok | 4 | 0 | 0 | 1 | 1 |
| 0x2001 | MoveIntent | one step, same chunk |  | 0 | 0 | 0 | 0 | 0 |
| 0x2001 | MoveIntent | step inside a chunk, another player in sight |  | 0 | 0 | 0 | 0 | 0 |
| 0x2001 | MoveIntent | step into a new chunk (interest set changes) |  | 0 | 0 | 0 | 0 | 0 |
| 0x2001 | MoveIntent | step onto an exit: change of map |  | 5 | 3 | 1 | 3 | 1 |
| 0x2002 | Eat | a parsnip from the hotbar | EnergyFull | 5 | 0 | 0 | 1 | 2 |
| 0x3000 | UseItem | plant on untilled soil (refused) | NotTilled | 2 | 0 | 0 | 1 | 0 |
| 0x3000 | UseItem | slot with nothing in it (refused) | InvalidSlot | 0 | 0 | 0 | 0 | 0 |
| 0x3000 | UseItem | till (hoe) | Ok | 4 | 1 | 1 | 2 | 0 |
| 0x3000 | UseItem | plant a seed | Ok | 6 | 2 | 1 | 3 | 2 |
| 0x3000 | UseItem | water | Ok | 4 | 1 | 1 | 2 | 0 |
| 0x3000 | UseItem | water on a new game day | Ok | 4 | 1 | 1 | 2 | 0 |
| 0x3000 | UseItem | harvest a mature crop | Ok | 10 | 4 | 2 | 5 | 2 |
| 0x3000 | UseItem | break a one-hit node | Ok | 8 | 2 | 2 | 3 | 2 |
| 0x3001 | GetFarm | empty farm |  | 0 | 0 | 0 | 0 | 0 |
| 0x3001 | GetFarm | empty farm, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x3002 | UseItemArea | watering can over a block of tiles | InvalidArea | 2 | 0 | 0 | 1 | 0 |
| 0x3100 | GetBuildings | own, none built |  | 1 | 0 | 0 | 0 | 0 |
| 0x3100 | GetBuildings | own, none built, asked again |  | 1 | 0 | 0 | 0 | 0 |
| 0x3100 | GetBuildings | one coop standing |  | 2 | 0 | 0 | 0 | 0 |
| 0x3101 | CommissionBuilding | a coop blueprint | Ok | 7 | 1 | 1 | 2 | 3 |
| 0x3102 | BuyAnimal | a chicken | Ok | 9 | 2 | 1 | 3 | 3 |
| 0x3103 | GetAnimals | not inside a building |  | 0 | 0 | 0 | 0 | 0 |
| 0x3103 | GetAnimals | not inside a building, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x3103 | GetAnimals | inside, one chicken |  | 2 | 0 | 0 | 0 | 0 |
| 0x3104 | FeedAnimals | hay into the trough | Ok | 9 | 1 | 1 | 2 | 5 |
| 0x3105 | PetAnimal | pet the chicken | Ok | 4 | 1 | 0 | 1 | 2 |
| 0x3106 | CollectProduce | an egg | Ok | 7 | 2 | 2 | 2 | 2 |
| 0x4000 | GetWallet | wallet |  | 3 | 1 | 1 | 1 | 0 |
| 0x4000 | GetWallet | wallet, asked again |  | 3 | 0 | 0 | 1 | 0 |
| 0x4001 | ShipItems | ship a stack at the bin | Ok | 8 | 1 | 1 | 3 | 5 |
| 0x4002 | BrowseListings | empty market, page 0 |  | 2 | 0 | 0 | 0 | 0 |
| 0x4002 | BrowseListings | empty market, page 0, asked again |  | 2 | 0 | 0 | 0 | 0 |
| 0x4002 | BrowseListings | one listing on the page |  | 2 | 0 | 0 | 0 | 0 |
| 0x4003 | CreateListing | first listing | Ok | 7 | 1 | 1 | 2 | 2 |
| 0x4003 | CreateListing | second listing | Ok | 7 | 1 | 1 | 2 | 2 |
| 0x4004 | BuyListing | buy one listing | Ok | 16 | 2 | 1 | 4 | 10 |
| 0x4005 | CancelListing | cancel own listing | Ok | 5 | 1 | 1 | 1 | 1 |
| 0x4006 | GetMyListings | none |  | 1 | 0 | 0 | 0 | 0 |
| 0x4006 | GetMyListings | none, asked again |  | 1 | 0 | 0 | 0 | 0 |
| 0x4006 | GetMyListings | one listing |  | 1 | 0 | 0 | 0 | 0 |
| 0x4007 | GetPriceHistory | no sales |  | 1 | 0 | 0 | 0 | 0 |
| 0x4007 | GetPriceHistory | no sales, asked again |  | 1 | 0 | 0 | 0 | 0 |
| 0x4100 | GetGoals | goals |  | 5 | 1 | 0 | 1 | 0 |
| 0x4100 | GetGoals | goals, asked again |  | 5 | 0 | 0 | 0 | 0 |
| 0x4100 | GetGoals | at the board |  | 5 | 0 | 0 | 0 | 0 |
| 0x4101 | DeliverToGoal | deliver parsnips | Ok | 10 | 1 | 1 | 1 | 1 |
| 0x5000 | GetInventory | starter kit |  | 2 | 0 | 0 | 1 | 0 |
| 0x5000 | GetInventory | starter kit, asked again |  | 2 | 0 | 0 | 1 | 0 |
| 0x5001 | MoveSlot | split a stack into a free slot | Ok | 4 | 1 | 1 | 2 | 2 |
| 0x5002 | SelectSlot | hotbar slot 1 | Ok | 2 | 1 | 0 | 1 | 0 |
| 0x6000 | GetQuests | new player |  | 1 | 1 | 0 | 1 | 0 |
| 0x6000 | GetQuests | new player, asked again |  | 1 | 0 | 0 | 1 | 0 |
| 0x7100 | GetNodes | farm |  | 0 | 0 | 0 | 0 | 0 |
| 0x7100 | GetNodes | farm, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x7200 | GetGroundItems | nothing on the ground |  | 0 | 0 | 0 | 0 | 0 |
| 0x7200 | GetGroundItems | nothing on the ground, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x8000 | GetLooks | own and seen players |  | 1 | 1 | 0 | 1 | 0 |
| 0x8000 | GetLooks | own and seen players, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0x8001 | SetAppearance | skin, hair, dye | Ok | 1 | 0 | 0 | 1 | 0 |
| 0x8002 | EquipItem | wear a shirt | Ok | 3 | 1 | 1 | 1 | 0 |
| 0x8003 | UnequipItem | take the shirt off | Ok | 3 | 1 | 1 | 1 | 0 |
| 0x9000 | TradeRequest | invite a neighbour | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9000 | TradeRequest | second trade | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9001 | TradeRespond | accept | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9002 | TradeSetOffer | items and coins | Ok | 5 | 0 | 0 | 2 | 2 |
| 0x9002 | TradeSetOffer | items | Ok | 2 | 0 | 0 | 1 | 0 |
| 0x9003 | TradeConfirm | first side confirms | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9003 | TradeConfirm | second side confirms: the swap | Ok | 12 | 1 | 1 | 3 | 9 |
| 0x9004 | TradeCancel | cancel an open trade | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9100 | SendChat | map channel | Ok | 1 | 0 | 0 | 0 | 0 |
| 0x9100 | SendChat | whisper by nickname | Ok | 2 | 0 | 0 | 0 | 0 |
| 0x9100 | SendChat | local channel | Ok | 0 | 0 | 0 | 0 | 0 |
| 0x9100 | SendChat | map channel, one listener has blocked the sender | Ok | 1 | 0 | 0 | 0 | 0 |
| 0x9101 | BlockPlayer | block by nickname | Ok | 5 | 1 | 0 | 1 | 0 |
| 0x9102 | UnblockPlayer | unblock | Ok | 2 | 1 | 0 | 1 | 0 |
| 0x9103 | GetBlocked | no blocks |  | 1 | 0 | 0 | 0 | 0 |
| 0x9103 | GetBlocked | no blocks, asked again |  | 1 | 0 | 0 | 0 | 0 |
| 0x9103 | GetBlocked | one block |  | 1 | 0 | 0 | 0 | 0 |
| 0x9104 | ReportChat | report a player | NothingToReport | 1 | 0 | 0 | 0 | 0 |
| 0x9200 | GetFriends | no friends |  | 5 | 0 | 0 | 0 | 0 |
| 0x9200 | GetFriends | no friends, asked again |  | 5 | 0 | 0 | 0 | 0 |
| 0x9200 | GetFriends | one request waiting |  | 5 | 0 | 0 | 0 | 0 |
| 0x9200 | GetFriends | one friend |  | 5 | 0 | 0 | 0 | 0 |
| 0x9201 | RequestFriend | by nickname | Ok | 15 | 1 | 0 | 1 | 2 |
| 0x9202 | RespondFriend | accept | Ok | 14 | 1 | 1 | 1 | 5 |
| 0x9203 | RemoveFriend | unfriend | Ok | 12 | 1 | 1 | 1 | 5 |
| 0x9204 | SetFriendsVisit | allow friends to visit | Ok | 2 | 1 | 0 | 1 | 0 |
| 0x9300 | GetMail | empty mailbox |  | 1 | 0 | 0 | 0 | 0 |
| 0x9300 | GetMail | empty mailbox, asked again |  | 1 | 0 | 0 | 0 | 0 |
| 0x9300 | GetMail | one sale notice |  | 2 | 0 | 0 | 0 | 0 |
| 0x9301 | ReadMail | mark read | Ok | 2 | 1 | 0 | 1 | 0 |
| 0x9302 | CollectMail | no attachment | NothingToCollect | 2 | 0 | 0 | 0 | 0 |
| 0x9303 | DeleteMail | delete | Ok | 3 | 1 | 0 | 1 | 0 |
| 0xA000 | CreateCoop | found a co-op | Ok | 7 | 1 | 1 | 1 | 0 |
| 0xA001 | GetCoop | no co-op |  | 2 | 0 | 0 | 0 | 0 |
| 0xA001 | GetCoop | no co-op, asked again |  | 2 | 0 | 0 | 0 | 0 |
| 0xA001 | GetCoop | an invitation waiting |  | 4 | 0 | 0 | 0 | 0 |
| 0xA001 | GetCoop | two members, stock and log |  | 6 | 0 | 0 | 0 | 0 |
| 0xA002 | InvitePlayer | invite a neighbour | Ok | 10 | 2 | 0 | 2 | 1 |
| 0xA002 | InvitePlayer | invite again | Ok | 10 | 2 | 0 | 2 | 1 |
| 0xA003 | RespondInvite | accept | Ok | 16 | 1 | 1 | 1 | 6 |
| 0xA003 | RespondInvite | accept again | Ok | 16 | 1 | 1 | 1 | 6 |
| 0xA004 | LeaveCoop | a member leaves | Ok | 10 | 1 | 0 | 1 | 1 |
| 0xA005 | KickMember | owner removes a member | Ok | 11 | 1 | 0 | 1 | 1 |
| 0xA006 | DepositItem | into the warehouse | Ok | 18 | 1 | 1 | 1 | 6 |
| 0xA007 | WithdrawItem | out of the warehouse | Ok | 20 | 1 | 1 | 1 | 6 |
| 0xB000 | GetPlaced | empty farm |  | 0 | 0 | 0 | 0 | 0 |
| 0xB000 | GetPlaced | empty farm, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0xB000 | GetPlaced | a chest and a furnace standing |  | 0 | 0 | 0 | 0 | 0 |
| 0xB001 | PlaceObject | a chest | Ok | 4 | 1 | 1 | 2 | 2 |
| 0xB001 | PlaceObject | a furnace | Ok | 4 | 1 | 1 | 2 | 2 |
| 0xB001 | PlaceObject | a fence | Ok | 4 | 1 | 1 | 2 | 2 |
| 0xB001 | PlaceObject | a coop blueprint | Ok | 7 | 2 | 1 | 2 | 2 |
| 0xB002 | MoveObject | a fence one tile over | OutOfReach | 0 | 0 | 0 | 0 | 0 |
| 0xB003 | PickUpObject | pick up a fence | Ok | 3 | 1 | 1 | 1 | 0 |
| 0xB100 | Craft | a chest from wood | Ok | 2 | 1 | 1 | 1 | 0 |
| 0xB100 | Craft | five fences | Ok | 2 | 1 | 1 | 1 | 0 |
| 0xB101 | GetRecipeBook | new player |  | 2 | 0 | 0 | 0 | 1 |
| 0xB101 | GetRecipeBook | new player, asked again |  | 2 | 0 | 0 | 0 | 1 |
| 0xB110 | OpenChest | empty chest | Ok | 1 | 1 | 0 | 1 | 0 |
| 0xB111 | CloseChest | close |  | 0 | 0 | 0 | 0 | 0 |
| 0xB112 | ChestMove | five parsnips into the chest | Ok | 11 | 1 | 1 | 4 | 8 |
| 0xB112 | ChestMove | one parsnip back out | Ok | 11 | 1 | 1 | 4 | 8 |
| 0xB120 | StartSmelting | five copper ore | Ok | 4 | 1 | 1 | 1 | 0 |
| 0xB121 | CollectFurnace | a finished batch | Ok | 4 | 1 | 1 | 1 | 1 |
| 0xB122 | GetFurnaces | no furnace |  | 0 | 0 | 0 | 0 | 0 |
| 0xB122 | GetFurnaces | no furnace, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0xB122 | GetFurnaces | one furnace smelting |  | 1 | 0 | 0 | 0 | 0 |
| 0xC000 | BuyFromShop | buy seeds | Ok | 6 | 1 | 1 | 2 | 3 |
| 0xC001 | GetBlacksmith | nothing in the forge | Ok | 1 | 0 | 0 | 0 | 0 |
| 0xC002 | UpgradeTool | hoe to copper | Ok | 11 | 1 | 1 | 3 | 6 |
| 0xC003 | CollectTool | a finished upgrade | Ok | 6 | 1 | 1 | 1 | 2 |
| 0xC100 | GetMine | in the entrance cave | Ok | 1 | 0 | 0 | 0 | 0 |
| 0xC101 | RideElevator | to the entrance, already there | AlreadyThere | 1 | 0 | 0 | 0 | 0 |
| 0xD000 | GetCalendar | calendar |  | 0 | 0 | 0 | 0 | 0 |
| 0xD000 | GetCalendar | calendar, asked again |  | 0 | 0 | 0 | 0 | 0 |
| 0xE000 | GetVisitors | no visitors |  | 3 | 0 | 0 | 0 | 0 |
| 0xE000 | GetVisitors | no visitors, asked again |  | 3 | 0 | 0 | 0 | 0 |
| 0xE000 | GetVisitors | one visitor on the farm |  | 3 | 0 | 0 | 0 | 0 |
| 0xE001 | AddVisitor | allow-list by nickname | Ok | 4 | 1 | 0 | 1 | 1 |
| 0xE002 | RemoveVisitor | take off the allow-list | Ok | 1 | 1 | 0 | 1 | 0 |
| 0xE003 | KickVisitor | send the visitor away | Ok | 3 | 2 | 0 | 2 | 1 |
| 0xE004 | SetCoopVisits | co-op members may visit | Ok | 2 | 0 | 0 | 1 | 0 |
| 0xE005 | VisitPlayer | friend visits the farm | Ok | 24 | 3 | 1 | 3 | 12 |
| 0xF000 | DevGrant | not measured | - | - | - | - | - | tester tool, Development only; its cost is test set-up |
| 0xF001 | DevAdvanceClock | not measured | - | - | - | - | - | tester tool, Development only; the day rollover it triggers is the DayClock row |
| - | DayClock | one game day ends (nodes, goals) |  | 4 | 3 | 0 | 3 | 4 |
| - | AnimalWander | tick, one player inside a coop |  | 1 | 0 | 0 | 0 | 0 |
| - | GuestCleanup | hourly pass, nothing old |  | 0 | 3 | 0 | 0 | 0 |
| - | MarketExpiry | every minute, nothing due |  | 2 | 0 | 0 | 0 | 0 |
| - | MailExpiry | hourly pass, nothing due |  | 1 | 0 | 0 | 0 | 0 |
| - | GroundItemExpiry | every 30 s, nothing due |  | 0 | 0 | 0 | 0 | 0 |
| - | AnimalWander | tick, nobody inside any building |  | 0 | 0 | 0 | 0 | 0 |
| - | Connect | WebSocket + handshake |  | 0 | 0 | 0 | 0 | 0 |
| - | Disconnect | joined player, socket closed |  | 1 | 1 | 0 | 1 | 0 |

## Where the repeats come from

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
- **Reads that write:** the first `GetQuests`, `GetLooks`, `GetWallet` and `GetGoals` insert their rows; `SelectSlot` writes on every click.
- **Cold work:** the first entry to an instance reads five tables once per process; a game day ending reads and writes for the nodes and the goals.

Not measured here: concurrency (these are single-player requests), tables with many rows (queries are indexed lookups; `BrowseListings` runs a
`count(*)` plus one page), the cost of the client's own pushes, and the Development-only tester tools.
