# ADR-035 — Database round-trip budget

**Status:** Accepted (coordinator, 2026-10-25, under the authority the owner delegated; review notes on D3 writers, D4 serialization and D7 animal friendship are folded in). D3–D7 are built (phase 2, see "Phase 2: what was built" below); D8 is a statement about the future.
Builds on [ADR-009](ADR-009-postgresql-persistence-and-accounts.md) (PostgreSQL, one unit of work per operation),
[ADR-013](ADR-013-economy-coins-escrow-market-trade.md) (one atomic transfer) and
[ADR-021](ADR-021-tools-resource-nodes-ground-items.md) / [ADR-022](ADR-022-chunked-interest-management.md) (world state held in memory per instance and chunk).
Measurements: [docs/perf/db-round-trips.md](../perf/db-round-trips.md).

## Context

The owner asked to call the database as little as possible. Phase 1 measured it instead of guessing: a test-only EF Core interceptor
(`tests/Nosum.IntegrationTests/Server/DbBudget/DbCommandCounter.cs`) counts commands, explicit transactions and `SaveChanges` calls while the real host
serves every client packet over a real WebSocket against a real PostgreSQL. Each number below is the whole cost of one request, including the work the
server leaves running after its reply (pushes, saves).

What is already good, and stays:

- **The frame path is free.** `MoveIntent` costs 0 commands, also when the step crosses a chunk border and the interest set changes (ADR-022).
  `GetFarm`, `GetNodes`, `GetGroundItems`, `GetPlaced`, `GetCalendar`, `GetFurnaces` (empty), a local chat line and `GetAnimals` are answered from memory.
- **Instance state is loaded once.** Plots, placed objects, nodes, ground items and buildings of an instance are read when it is first entered
  (about 5 reads) and kept; later swings and pushes do not read them again.
- **Some lookups are already cached:** `INicknameDirectory` (account id to nickname), `BuildingDirectory` (doors per land), `BlockList` (blocked ids per
  player), `LookService` (look rows).

What costs the most (headline numbers from the table; reads / writes / explicit transactions per request):

| Request | Measured | Why |
|:--|:--|:--|
| `BuyListing` | 16 / 2 / 1 | the inventory and the wallet of both parties read twice each, the listing read twice |
| `VisitPlayer` | 24 / 3 / 1 | the access check runs three times (grants, settings, blocks, friendships each 3x), four `players` reads, cold-instance load |
| `RequestFriend` / `RespondFriend` / `RemoveFriend` | 15 / 14 / 12 reads | seven count/exists queries, three `players` reads by nickname |
| Co-op `DepositItem` / `WithdrawItem` / `RespondInvite` | 18 / 20 / 16 reads | membership, co-op row and member list re-read 2-6 times; the snapshot rebuilt from five tables after the change |
| `ChestMove` | 11 / 1 / 1, 4 `SaveChanges` | the player's inventory loaded five times, the chest's once |
| `TradeConfirm` (the swap) | 12 / 1 / 1 | the inventory and the wallet of both players read twice each |
| `UseItem` harvest | 10 / 4 / 2 | inventory twice, progress row, the pickup of the produce (a second transfer: wallet, ground items), quest, account (registration prompt) |
| `UseItem` plant / till / water | 6 / 2 / 1 &middot; 4 / 1 / 1 &middot; 4 / 1 / 1 | the swing reads the inventory twice (check, then the transaction) and the progress row |
| `ShipItems` | 8 / 1 / 1 | the inventory loaded three times (`PeekSlot`, then twice inside the transfer), the wallet twice |
| `JoinMap` | 9 / 2 / 2 first time, 4 / 0 / 0 later | saved place read twice, progress row, friendships, inventory backfill, cold instance |
| Step onto an exit (map change) | 5 / 3 / 1 | saved place read twice, access check, two position saves |
| `GetGoals` | 8 reads before this PR's fix, 5 after | one `community_goals` query per goal |

The pattern behind almost all of it: **the server re-reads data it already holds**. Every inventory operation loads the player's header and all slot rows,
even to answer "what is in slot 3?"; every tool use reads the player's progress row; every social call re-reads membership and friendship facts it read
a second ago. Only a few of these are real N+1 loops (community goals, friend counts); most are the same read repeated in different layers.

## Decision

- **D1 — A budget per packet class.** Budgets are upper bounds on database commands per request; they are the contract and are tested (D9).

  | Class | Budget |
  |:--|:--|
  | Movement, chunk joins and leaves, snapshots, `GetFarm`, `GetNodes`, `GetGroundItems`, `GetPlaced`, `GetCalendar`, `GetAnimals`, local/map/whisper chat | **0 commands** |
  | Pure reads of a player's own state (`GetInventory`, `SelectSlot`, `GetQuests`, `GetLooks`, `GetRecipeBook`, `GetCoop`, `GetFriends`, `GetMail`, `GetBlocked`, `GetVisitors`, `GetBuildings`) | **0 commands** once the player is signed in (served from the session state of D3-D5); at most 1 read for state that is shared and not cached (market pages) |
  | A tool swing on a tile held in memory (`UseItem` till, water, plant, harvest, break a node) | **no reads, 1 transaction, 1 `SaveChanges`**, at most 3 write commands (tile row, inventory/energy rows, audit rows) |
  | Inventory moves and equipment (`MoveSlot`, `EquipItem`, `UnequipItem`, `Craft`, `PlaceObject`, `PickUpObject`, `ChestMove`, `Eat`) | **no reads, at most 1 transaction** |
  | Anything that moves coins or items between parties (`BuyListing`, `CreateListing`, `CancelListing`, `ShipItems`, `BuyFromShop`, `TradeConfirm`, `DeliverToGoal`, `DepositItem`, `WithdrawItem`, mail collect) | **at most 1 transaction and 1 `SaveChanges`; reads only the rows the transfer must lock** (wallets of the parties, the listing row), at most 4 reads |
  | Social writes (`RequestFriend`, `RespondFriend`, `RemoveFriend`, `InvitePlayer`, `RespondInvite`, `KickMember`, `AddVisitor`, ...) | at most 3 reads and 1 write transaction |
  | `VisitPlayer`, a map change by exit, `JoinMap` | at most 2 reads (1 for a returning player) and 1 write (the saved place) |
  | Session start (`StartGuest`, `Login`, `Register`, `ResumeSession`) | what they are today (1-3 reads, 1 write); `Register` and `Login` are dominated by the password hash |
  | Background jobs | at most 1 read per pass when nothing is due; no work for rooms nobody is in |

  A request in a class may exceed its budget only with an amendment to this ADR.
- **D2 — The server keeps what it decided in memory and the database keeps what must survive.** State that only this process changes and that
  every request needs (inventory, energy and skill experience, co-op membership, visit lists, friendships, block lists, nicknames) is held in memory
  per player. The database stays the durable record and is written in the same transaction as before (write-through).
- **D3 — Inventory.** The cache lives in the layer that owns the transaction. Every committed change of an inventory row, whoever asked for it,
  runs through `InventoryService` (`ExecuteManyAsync`, the one place that opens the unit of work, writes `inventory_slots`/`player_inventories` and the
  `inventory_log`): the single-player operations (`ApplyAsync`, `MoveAsync`, `SelectSlotAsync`, `BackfillStarterToolsAsync`) and `ApplyManyAsync`, which is
  what `ITransferService` calls for the item legs of a shop purchase, market listing/purchase/cancel, direct trade, shipping, mail collect, community goal
  delivery and co-op deposit/withdraw, and what `GroundItemService` (pickups), `InventoryYieldSink` (node yields), `BuildingService`, `PlaceableService`,
  `ChestService`, `CraftingService`, `CoopService` and `DevGrantService` use. Writers found by grep over `server/Nosum.Infrastructure`
  (`Repository<InventorySlot>`, `Repository<PlayerInventory>`):

  | File | What it does to the tables |
  |:--|:--|
  | `Inventory/InventoryService.cs` | loads, inserts the starter kit, updates and deletes slot rows, updates the header: **the only writer of item counts** |
  | `Placeables/ChestService.cs` | reads `inventory_slots` and removes the header of a chest container when the chest is picked up |
  | `Coops/CoopService.cs` | reads `inventory_slots` and removes the header of each chest when co-op land is wiped |

  The cache keeps, per player that is signed in (and for any container it touched, with a size cap), the header and the slot grid `LoadAsync` already
  builds. Rules:
  1. The per-player locks `ExecuteManyAsync` already takes (`StripedAsyncLock.EnterAllAsync`, all parties in stripe order) cover the read, the change,
     the save **and the cache update**, so a transfer updates the cached grid of **each party** under the locks of all parties.
  2. A cache entry is changed only **after** `SaveChanges` succeeded. On a conflict, a unique violation or any exception the entries of all parties are
     dropped and the next call reloads them.
  3. Reads (`PeekSlotAsync`, `GetAsync`, `SelectSlotAsync` validation, the check half of every operation) are answered from the grid with no command.
  4. Entries leave when the player disconnects (after the position save) and by an idle timeout for containers.
  5. The two container deletions above move behind an `IInventoryService` method (`DropContainerAsync`), which drops the cache entry with the rows, before any
     cache is built. Then a test fails if any file outside `InventoryService` uses `Repository<InventorySlot>` or `Repository<PlayerInventory>`, and a
     second test fails if an implementation of `ITransferService`, `IGroundItems` or any yield sink opens its own inventory write instead of calling
     `IInventoryService`.
- **D4 — Progress.** One `player_states` row holds the saved place, the energy and the skill experience, and today two services write it
  (`PlayerStateService` for the place, `PlayerProgressService` for energy and experience). They become one cached entry per signed-in player, so a join, a
  map change and a swing read the same in-memory row and `JoinMap` stops reading it twice. The energy charge of a swing happens against the cached value and is
  written in the swing's transaction, as ADR-023 D1 requires.
  **Concurrent swings are serialized.** `PlayerProgressService` already takes one `StripedAsyncLock` per player for every charge and keeps it until the
  caller disposed the charge, after the save; the energy check and the XP grant happen under that lock against the cached value, and the cache is updated
  only after `SaveChanges`. The inventory lock of D3 is the other per-player lock; the order between them stays what the code documents today (inventory
  lock first, progress second, because the charge is enlisted in the inventory transaction, `PlayerProgressService` class comment), and a swing that needs both takes them in that order only.
  Two quick swings therefore cannot both pass a check against 1 remaining energy.
  Cache scope: the cache is per signed-in session; bounded memory. A player who has left keeps no copy (`LeaveAsync` writes the place and evicts it), so the cache holds only players who are signed in, plus the 30-minute idle sweep.
- **D5 — Social and access facts.** `CoopMembership`, `VisitAccess` (grants, settings, friendships, blocks), the friend lists and the nickname to account
  lookup (`players.normalized_nickname`) get a read-through cache with explicit invalidation in the service that writes them. Each of those services is
  already the only writer of its tables; `INicknameDirectory` is the model. `VisitAccess` is also evaluated once per decision: ADR-019 D5 re-checks access
  after the move to close a race, and that second check stays, but it reads memory.
- **D6 — What stays transactional and uncached.**
  - **Coin balances are never cached outside a transaction.** A wallet is read inside the unit of work that changes it (with its concurrency token),
    exactly as ADR-013 D1 says. `GetWallet` is answered with one read; the client already receives `WalletUpdate` after every committed change, so it asks
    once per session.
  - **ADR-013's single atomic transfer is unchanged:** every leg of an exchange (items, coins, escrow row, audit rows) is committed by one `SaveChanges`
    through `ITransferService`. Nothing in this ADR splits it or defers part of it.
  - Market listings (escrow), mail with attachments, community goal totals and co-op stock are shared state with several writers' views; they are read
    inside the transaction that changes them and are not cached.
  - Inventory is cached (D3) **only because every change is committed before the cache moves**; if a commit fails, the entry is thrown away, never patched.
- **D7 — Write-behind is allowed only for state whose loss cannot change money, items or progress.** Candidates and the data lost if the process
  crashes inside the window:

  | Write | Window | Lost on a crash |
  |:--|:--|:--|
  | `SelectSlot` (`player_inventories.selected_slot`) | 5 s coalesced, flushed on disconnect | the selected hotbar slot reverts to the previous one |
  | `ReadMail` flag | 5 s | a mail shows as unread again |
  | `last_active_at` touch on resume | 60 s | guest retention is computed from a slightly older time |
  | Position save (`player_states` x, y) | already on leave and map change only | at most the walk since the last map change (unchanged) |

  Not write-behind, ever: animal friendship (petting) - friendship sets the chance of the large produce (`AnimalRules.LargeChancePercent`), so losing it would change items; it stays
  write-through - coins, items, quests, crop and plot state, placed objects, ground items, market and trade rows, mail attachments, the
  inventory log, the coin log. A flush runs on disconnect, on a map change and at shutdown (`NetworkApplication` stop), in one unit of work per player.
- **D8 — Single process, and what changes if that ends.** These caches are correct because one process is the only writer of the tables they mirror
  (ADR-003 server authority, ADR-009 "revisit when the host runs on more than one process": the session registry and map presence are already in memory).
  If Nosum is ever scaled out: (a) route a player to one process for the life of the session (sticky by player id), which keeps D3 and D4 valid; (b)
  shared facts (D5) get a version column and `LISTEN/NOTIFY` invalidation, or move behind a single owner process per co-op/land; (c) the budget test
  stays valid, only the cache layer changes. Until then the cache classes are written behind the same interfaces handlers use today, so they can be replaced
  without touching a handler.
- **D9 — The gate.** `DbBudgetTests` (integration, real PostgreSQL) runs a scenario per client packet and fails when a hot-path request needs more
  reads, writes, transactions or `SaveChanges` calls than `DbBudgetBaseline`. The limits in this PR are today's numbers; each change below lowers its rows
  in the same commit (a ratchet). A new client packet needs a scenario (`Run_EveryClientOpcode_HasARowOrAReason` fails otherwise). The budget test runs in CI's integration job with the rest
  of `Nosum.IntegrationTests` and does not depend on timing: it counts commands, never durations, and the waits in the scenarios only let the server finish
  its own work before the count is read.

## Ranked changes

Savings are estimated from the statement counts in `docs/perf/db-round-trips.md` and are `[Assumption]` until each change is measured by the same
test; if wrong, the budget test shows by how much.

| # | Change | Rows it improves | Expected saving | Risk |
|--:|:--|:--|:--|:--|
| 1 | Inventory held in memory, write-through (D3) | every `UseItem`, `PlaceObject`, `Craft`, `ChestMove`, `Eat`, `MoveSlot`, `GetInventory`, `SelectSlot`, and the item half of market/shop/trade/co-op/mail transfers | `GetInventory`/`SelectSlot` 2 reads to 0; swings 4-10 reads to 2-6; `ChestMove` 11 to 2-3; `BuyListing`/`TradeConfirm` 16/12 to about 8/4; about one third of all hot-path reads | medium: cache coherence; mitigated by "commit first, then move the cache" and the single-writer test |
| 2 | Progress and saved place held in memory (D4) | every tool swing, `JoinMap`, map change, `GetRecipeBook`, `Eat` | 1-2 reads per swing and per join; `GetRecipeBook` 2 to 0; `Eat` refusal 5 to 0 | low |
| 3 | Access and social facts cached (D5), one evaluation per decision | `VisitPlayer`, friend and co-op calls, whisper, `GetFriends`, `GetVisitors`, co-op snapshot rebuilds | `VisitPlayer` 24 to about 6; friend calls 12-15 to about 4; co-op calls 10-20 to about 4 | medium: invalidation must follow every writer (co-op kick, unfriend, block) |
| 4 | Create rows with the account instead of on the first read | `GetQuests`, `GetLooks`, `GetWallet`, `GetGoals` (first call writes), `JoinMap` first time | removes writes from "Get" packets; first `JoinMap` 9/2/2 to about 4/1/1 | low: needs a migration-free backfill on first load for old accounts |
| 5 | Batch the remaining N+1 loops into one query | community goals (done in this PR), friend counts, co-op snapshot, `GetFriends` (5 reads) | 5 reads to 2 per call | low |
| 6 | Load header and slots of an inventory in one command (until #1 lands) | all inventory operations | 1 round trip per load | low |
| 7 | Write-behind for the rows of D7 | `SelectSlot`, `ReadMail`, resume | 1 write per click | low |
| 8 | Drop instance state of an instance nobody is in after a grace period | memory only | none for the database (it adds a cold load back); listed so the growth is a decision | - |

Phase 2 builds #1-#3 in that order, one PR each, with the budget test lowered in the same commit. #4-#7 follow. This ADR changes no code.

Coherence tests phase 2 must add with the caches (the budget test cannot see staleness):
- two parallel transfers touching the same player never leave the cache different from the database (compare after each);
- a transfer that fails its `SaveChanges` leaves no cache entry for any party;
- two quick swings with 1 energy left: exactly one passes (D4);
- inventory changed by a pickup, a node yield, a craft and a chest move is visible to the next `GetInventory` without a reload;
- picking up a chest and wiping co-op land drop the container's cache entry;
- the single-writer guard tests of D3 rule 5.

## Phase 2: what was built

Measured by the same test, `DbBudgetBaseline` now holds the new numbers ([docs/perf/db-round-trips.md](../perf/db-round-trips.md) has the table and the before/after rows). Reads over the rows
both tables measure fell from 725 to 345; writes and transactions did not change except that `SelectSlot` and `ReadMail` write in the background. How each decision was built, and where the
code differs from the text above:

- **D3, inventory.** The cache is inside `InventoryService` (`ExecuteManyAsync` is still the one place that saves). An entry is the header values and an immutable array of the slots; a change works on a
  copy of the grid, writes only the changed rows by key (`IRepository.Update/Remove` with a stub that carries the key, so no row is read), and replaces the entry after `SaveChanges`, under the locks of
  all parties. A unique violation, a concurrency conflict (a row that is no longer there), a refused enlistment or any exception drops the entries of every party. **Reads also take the player's lock**
  (an in-process semaphore, no command): a read that starts after a change has begun sees its result, as it did when every read hit the rows. `ChestService` and `CoopService` use
  `IInventoryService.DropContainerAsync` and `AnyContainerHoldsItemsAsync`; two guard tests fail if another file under `Nosum.Infrastructure` names the inventory tables or the inventory log, or
  if a `ITransferService`, `IGroundItems` or `INodeYieldSink` implementation does. Entries leave with `ISessionRegistry.SessionEnded` (a new event, raised after a player's last connection ended)
  and by a 30 minute idle sweep; containers are also capped at 256. A test that changes rows behind the service's back calls `Forget`.
- **D4, progress and place.** One `PlayerStateCache` entry per player, shared by `PlayerStateService` and `PlayerProgressService`. Both write only their own columns
  (`IRepository.Update(entity, properties)`), so a place save cannot overwrite an energy charge. The swing's check and spend happen against the entry under the progress lock; the charge stages a write
  in the caller's unit and the entry follows when the caller reports the save (`IProgressCharge.PublishAsync`, which every caller already calls after its save); a charge disposed without that report drops
  the entry. Lock order stays inventory, then progress.
- **Quests.** `QuestService` is the only writer of `player_quests` and holds a player's quests in memory. An event no active quest counts costs no command; a counting event writes the changed rows
  by key; the reward is still the one transfer that completes the row and pays the coins.
- **D5, social and access facts.** `ReadThroughCache` (an epoch keeps a load that raced a change from being kept) under four caches, each with one writer that invalidates or replaces after its save:
  player facts and the nickname lookup in `NicknameDirectory` (`AccountService` ends every change in `Remember`), co-op membership in `CoopDirectory` (`CoopService`), friends and requests in
  `FriendFacts` (`FriendService` replaces the entries of the two players it changed under one gate; a block drops them) and the allow-list and visit settings in `VisitFacts` (`VisitService` and
  `FriendService`). `VisitAccess` reads only these, so the access decision costs no command once they are warm and the ADR-019 re-check after a move reads the same memory. The co-op snapshot is built once
  per push for all online members (the stock and the log are shared state and stay uncached, D6).
  The collection journal (ADR-072) is the same kind of cache in `JournalRecorder`, owned by `InventoryService`: a player's discovered items are read once, on their first grant that
  needs them, a grant of a known item updates its count only, and the copy is dropped with the inventory copy (a failed save, a player leaving).
- **D6.** Nothing keeps a balance. `TransferService` pushes `WalletUpdate` with the balance its own transaction saved instead of reading the wallet again.
- **D7, write-behind.** `WriteBehind` holds a staged write per player and key for five seconds from the first change (constant clicking costs one write per window). Built for the two rows of the
  table that are written on a request: the selected hotbar slot and the read flag of a mail (the list and the unread count see it at once). It flushes when a player leaves a map (map change and
  disconnect), when a session ends, and at shutdown after the application stopped; one player's rows go in one unit of work and a row that cannot be written (a mail deleted meanwhile) is dropped
  and reported without costing the others. `last_active_at` on resume has no write of its own (it rides the write of the rotated session token), so there was nothing to defer; animal friendship and
  everything else the table forbids stay write-through.
- **The gate.** `DbBudgetTests` also compares, after every measured row, each copy a cache holds for a signed-in player with what a service with nothing in memory reads from the rows
  (`Run_CachedStateEqualsTheRows_AfterEveryMeasuredRow`). Unit-level coherence tests: `InventoryCacheTests`, `PlayerStateCacheTests`, `QuestCacheTests`, `PlayerFactsTests`, `WriteBehindTests`,
  the cached `CoopDirectory` in `SocialServiceTests` and the friends/visit comparison in `VisitWireTests`.

### Amendment: rows above their D1 class

Measured on the merged head. D1 says a class may be exceeded only by an amendment; these are the rows that are, with the reason and the next step. They are the limits in `DbBudgetBaseline`.

| Row | Reads | D1 class says | Why | Next step |
|:--|--:|:--|:--|:--|
| `RespondInvite` accept | 8 | at most 3 | the invite and the open invites, the new member's membership and the snapshot (stock and two logs) | cache invitations |
| `KickMember`, `LeaveCoop` | 10 | at most 3 | the mutation reads its own rows (they are tracked), then the directory reloads for every member that is pushed to and the snapshot is built | read the co-op once and mutate by key |
| `InvitePlayer` | 7 | at most 3 | invite rows, the invitee's push (invites, co-op, founder), the mail copy (count and insert) | cache invitations |
| `UpgradeCoop` | 11 | at most 4 | stock for the price check, the transfer, the co-op row inside the transfer, the snapshot | read the co-op row once |
| `DepositItem`, `WithdrawItem` | 6, 7 | at most 4 | stock and log are shared state (D6): the check, the transfer's stock row, the wallet row and the snapshot's three reads | none planned; the stock is read inside the transaction on purpose |
| `RequestFriend` | 8 | at most 3 | two players' friends and requests are read once each when cold (4), the push to the target (visit settings), the block set | warm after first use; the number is the cold cost |
| `VisitPlayer` | 8 | at most 2 | 2 for the access facts when cold; 5 load the cold instance of the farm (plots, objects, nodes, buildings, ground items) | the instance load is ADR-021 state, not a cache miss; keep |
| `JoinMap` first join | 9 | at most 2 | the instance load again, the friends the status announcement needs, the inventory and its starter-kit backfill | rank 4 of the table above (create rows with the account) |
| `BuyListing` | 6 | at most 4 | the listing is read twice (the check, then inside the transfer), the two wallets, and the unread count of the seller's mail after the sale notice | read the listing once |
| `DeliverToGoal`, `GetGoals` | 8, 5 | at most 4 | the season's goal rows are read for the board, again by goal and again by id inside the transaction, with the contributions and the wallet | read the goal rows once (rank 5) |
| `GetCoop`, `GetFriends`, `GetMail`, `GetBlocked`, `BrowseListings` | 1-3 | 0 once signed in | the stock, the log, the mailbox and the market page are shared state; the block list is read for its order and nicknames | serve `GetBlocked` and `GetFriends` from the caches |

Ranked changes #4 (rows created with the account), #5 (the remaining N+1 loops) and #6 (header and slots in one command) are not built; the cold read of an inventory is still two commands.

### Weak spots

- A cache is a gameplay bug when it is wrong. Every writer is named above and guarded by a test or by the comparison of the budget run, but a **new writer of a cached table** that does not call the invalidation is silently
  wrong; the inventory has a guard test, the other tables have only their rule in the class comment.
- A row changed by hand in the database (a migration, support) is not seen until the entry leaves (sign-out, 30 minutes idle) or the process restarts; the server is the only writer by design (D8).
- `WalletUpdate` carries the balance of the transaction that sent it. Two transfers of one player that commit within the same millisecond could push in the other order and leave the client with the older
  number until its next change; the server's own state is unaffected. Before, the push re-read the wallet, which narrowed the window but did not close it.
- A crash loses what waits in `WriteBehind` (at most five seconds of hotbar choices and mail read flags), as D7 accepts.
- The friend caches and `CoopDirectory` are keyed by player and dropped at sign-out, but the visit facts are keyed by owner and live until the idle sweep; memory is bounded by the players asked about in 30 minutes.
- Entries of a signed-out player can reappear (a write that finishes after the sign-out stores one); the idle sweep removes them.

## Alternatives considered

- **A distributed cache (Redis).** A second source of truth and a new dependency for a single-process server; the in-memory state ADR-021/022 already
  holds is the same idea without the network hop. Rejected.
- **EF Core second-level cache packages or compiled-query tricks.** New packages (not allowed without asking), and prepared statements do not remove
  round trips.
- **Cache coin balances too.** Rejected: a balance is the one number that must never be stale in a decision, and the transfer already reads it inside
  the transaction it changes.
- **Write-behind for inventory or coins (flush every few seconds).** Fastest, but a crash then loses or duplicates items and breaks ADR-013. Rejected.
- **Do nothing, buy a bigger database.** Cheap today; the repeated reads grow with players times actions per minute (a farmer swings 3 times a
  second), so it moves the problem to the first busy day.

## Consequences

- Request latency falls (fewer round trips) and the database load falls roughly with the saved reads; memory grows with signed-in players
  (an inventory grid is 36 slots, a few hundred bytes).
- A cache bug is now a gameplay bug (a stale slot) instead of an extra query; D3's rules (commit first, drop on error, single writer, test) are the
  defence, and the budget test cannot see staleness, so each cache needs its own coherence tests.
- Handlers and the wire do not change.
- Tests that build services by hand over a unit-of-work factory keep working; caches are optional constructor arguments with a null object, so unit
  tests can run uncached.

## Revisit when

A second server process is planned; the signed-in population makes the memory of D3-D5 matter; an owner decision adds a feature whose packets do not
fit a class of D1; or write-behind (D7) is asked for state that D7 forbids.

## Amendment (2026-10-31): the onboarding steps

The onboarding chain (ADR-036) gained thirteen steps. Each step is completed by a hot-path packet: pet adoption, emotes, Town requests,
placements, coop placement. A step that counts toward a quest costs one quest read when the copy is cold, one quest row write per counting
event while the step is active, and a reward transaction on completion. The quest copy is held in memory per player, so a player whose chain
is finished pays none of this (`DbBudgetRun.Finished`, rows marked "chain finished").

The raised rows below measure a fresh player with that step still **active** (not a finished chain). They are the limits now:

- `AdoptPet / adopt a cat` (player with adopt_pet active): (1, 1, 0, 1) → (2, 2, 1, 2). The packet completes the step: one quest read,
  the completed row written, the reward paid in one transaction.
- `SendEmote / wave` (player with send_emote active): (0, 0, 0, 0) → (1, 1, 1, 1). The first emote completes the step, same cost shape.
- `DeliverRequest / fill one request` (player with fill_request active): (4, 1, 1, 1) → (6, 3, 3, 3). The first filled request completes
  the step, same cost shape.
- `PlaceObject / a chest` (player with decorate active): (0, 1, 1, 1) → (0, 2, 1, 2). Every placement counts toward decorate: one quest row
  write, no read (the copy is held), no transaction.
- `PlaceObject / a fence` (player with decorate active): (0, 1, 1, 1) → (1, 2, 2, 2).
- `PlaceObject / a furnace` (player with decorate active): (0, 1, 1, 1) → (0, 2, 1, 2).
- `PlaceObject / a coop blueprint` (player with build_coop active): (3, 2, 1, 1) → (5, 4, 3, 3). The first coop completes the step.

Measured for a player whose chain is finished, these packets stay within their original limits (the test checks an upper bound), so the
steps add no database work for players who have finished them. Their rows keep the original limits:

- `PlaceObject / a chest, chain finished`: (0, 1, 1, 1)
- `SendEmote / wave, chain finished`: (0, 0, 0, 0)
- `DeliverRequest / fill one request, chain finished`: (4, 1, 1, 1)

Every other limit is unchanged.

### 2026-10-31 - paid pets: adopting pays through the transfer, and rehoming is measured (owner review, PR #258)

- **`AdoptPet` now buys.** Adopting a cat or dog at the carpenter's counter charges the price and creates the pet row in one `ITransferService` transaction
  (ADR-075 amendment). Measured: reads 5, writes 4, transactions 3, SaveChanges 4 (was 2, 2, 1, 2). A shop buy costs 4, 3, 3, 3 (`BuyFromShop`), so this is the
  cost of paying through the transfer service, not an extra query on the side. The row limit is raised to these numbers in the same commit as the change; the
  probe now runs for a farmer standing at the counter with the coins, so the row measures the purchase and not a refusal.
- **`RehomePet` is measured.** Giving the pet back costs reads 3, writes 1, transactions 1, SaveChanges 1, the coins and the row removal in one transfer. New row
  `RehomePet / give the cat back` in `DbBudgetBaseline`.
- The `GetPet` rows are unchanged; the pet read stays on the cache path described above.

## Amendment (2026-11-01): BuyListing to four reads, two wallets in one query (economy rebuild)

- **`BuyListing` is measured at 4 reads** (was 6): reads 4, writes 2, transactions 1, SaveChanges 2. The statements were: the listing twice (the read under the
  listing lock, then the transfer's own read), the two wallets once each, and the seller's mail room count and unread count. The listing is now read once: the save
  writes the row with its `Active` status as the concurrency token, so the save is a compare-and-set and a row another process closed fails it (`Conflict`; nothing
  moves). The row limit is lowered from 6 to 4 in the same change (`DbBudgetBaseline`). The two mail reads are the sale notice and stay (ADR-029 D5).
- **Every transfer reads its parties' wallets in one query.** `TransferService.JoinAsync` loads the wallets of all parties with a single `ListAsync` (a new
  player's row is created as before, with the starting coins logged). Two-party transfers read two rows in one round trip instead of two. Measured on the same run:
  `LinkExternalIdentity` 5 to 4 reads and `CommissionBuilding` 4 to 3 (both limits already allowed the new number, so they are not changed).
- **Not changed:** the write counts, the transaction count, the SaveChanges count, every other row. `GetRequests / today's board, chain finished` reads one more row
  (the friend list) on this run than on the previous one; the statement is the same and the row has no limit, so it is recorded, not blocked. `Disconnect` reads 0 (its
  limit is 0; the previous run recorded 1 and failed it).
