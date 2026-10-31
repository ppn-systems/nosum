# ADR-021 — Farming basics: tools, resource nodes, ground items, growth in game days

**Status:** Accepted (coordinator decision delegated by the owner, 2026-10-25: "research, critique, approve
yourself, then hand to agents"). Amends [ADR-011](ADR-011-inventory-farming-data-model.md) (harvest goes to the
ground, growth unit). Builds on [ADR-015](ADR-015-private-instances-and-placeables.md) (everything is keyed by
`MapInstance`) and on the season calendar of ADR-018 (in progress).

## Context

The first farming slice (ADR-011) has two tools (hoe, watering can), three crops whose stages last seconds, and a
harvest that goes straight into the inventory. The maps are empty of anything to clear: no trees, stones, weeds or
twigs, no axe, pickaxe or scythe, and nothing ever lies on the ground. These are the basic loops of the genre
(clear land → farm → sell → buy), and every later feature (crafting, quests, seasons, co-op land) needs them.
Review of the current code found:

- Crop stages are `stageSeconds` (about one minute per crop): fine for a demo, meaningless once seasons last two real
  weeks (owner decision 2026-10-25).
- Harvest calls the inventory directly; a full inventory refuses the harvest instead of leaving the produce.
- The world has no state besides tilled plots and placed furniture, so a cleared tree cannot exist.

## Decision

- **D1 — Five tools, one intent.** Hoe (till), watering can (water), axe (trees, stumps, twigs), pickaxe (stones, ore
  nodes; also clears an empty tilled tile), scythe (weeds, grass; also harvests a mature crop). All are `ItemKind.Tool`
  with a `ToolKind`; the client sends one "use tool at tile" intent (the existing farm intent, extended
  append-only), and the server decides what happens from the tool, the target tile and its contents. The starter kit
  gets the axe, pickaxe and scythe too (the owner's rule is only "no furniture or clothes"; land cannot be cleared
  without them). Tool tiers and upgrades are
  out of scope; `ToolKind` plus a tier byte leave room for them.
- **D2 — Watering can capacity.** The can holds 40 uses, shown on the slot; refilled by using it on a water tile
  (map tile property or the `Water` layer) or the farm's well/pond. Kept server-side per player.
- **D3 — Resource nodes are server state per instance.** A node is (instance, tile x/y, kind, hit points, stage,
  spawned at). Kinds: tree (oak, maple, pine; stages seed → sapling → bush → tree, chopped into a stump), stump, twig,
  stone, ore node (copper only for now), weed, grass. Hits to break are data (twig 1, stone 1, weed 1, stump 2, tree
  10, ore 3). A node blocks walking (except grass and weeds, which only slow nothing) and is part of
  `IInstanceWorld.GridFor` like placed objects, so client prediction and server agree.
- **D4 — Where nodes come from.** (a) Initial placement from the map itself: early maps mark trees and debris spawn
  points in the `Paths` layer (tile index → kind; the exact index table is verified against the map files and recorded
  in the code); the reader turns those into nodes when an instance is first created and
  never again. (b) Public maps (Forest, Backwoods, Town edges) regrow: at each game-day rollover a bounded number of
  twigs, stones and weeds respawn on free spawnable tiles. (c) A player can plant a tree seed (from chopping) that
  grows by game days. Private farms do not respawn debris except weeds at season change.
- **D5 — Yields drop on the ground.** Breaking a node, harvesting a crop and (later) anything that "pops" an item
  creates **ground items**: (instance, id, item id, quantity, position in pixels, created at, owner-priority player id,
  priority until, expires at). Yields are data per node kind (tree → wood ×6–10 + chance of seed; stone → stone ×1–2,
  rare coal; ore → copper ore ×1–3; weed → fiber ×1; twig → wood ×1; crop → its produce). Randomness uses a seeded
  RNG on the server; the client never rolls.
- **D6 — Picking up.** A player walking within 1.5 tiles of a ground item picks it up automatically (magnet), the
  server checks the distance against the authoritative position and moves the item into the inventory in one
  atomic transfer (ADR-013 `ITransferService`). Full inventory: the item stays. On shared maps the player who caused
  the drop has priority for 5 s, then anyone may take it. Ground items expire after 10 real minutes on shared maps
  and never on the owner's private farm (they are persisted so a crash does not lose a harvest).
- **D7 — Crop growth counts watered game days.** `CropDefinition` stages are in game days (parsnip 1/1/1/1 = 4 days,
  radish 1/2/1/2 = 6, cauliflower 1/2/4/4/1 = 12), using the ADR-018 calendar. A tile watered during a game day
  advances one day at the next rollover; watering resets at rollover; rain (later) waters everything. Tilled soil
  without a crop reverts with 30 % chance per dry day. Growth stays computed from timestamps — no per-tile timer.
  The dev grant may fast-forward an instance's clock for tests.
- **D8 — Rate.** Every tool use is a 300 ms server-side cooldown per player (one swing animation); no stamina in
  this slice (energy is a later ADR because it couples to food and days).
- **D9 — Wire.** Node and ground-item state is sent per chunk (ADR-022): full state when a chunk enters a player's
  interest set, deltas (`NodeChanged`, `NodeRemoved`, `GroundItemSpawned`, `GroundItemTaken`) after. World band
  sub-ranges: `0x7100–0x71FF` resource nodes, `0x7200–0x72FF` ground items. Append-only; `NosumProtocol.Version`
  is bumped once by the coordinator when the work merges.

## Implementation notes

### D7 — growth in watered game days (implemented)

- **Representation.** `FarmTile(CropId, PlantedAtMs, GrowthDays, WateredDay, TendedDay)`; a day number is the 1-based count of game days since the
  calendar epoch (`GameCalendar.DayNumberAt`; instants before the epoch are day 1; zero means "never"). `GrowthDays` is the settled count of watered days
  and does **not** include `WateredDay`. When `WateredDay` is before today it adds one on read (`FarmRules.GrowthDaysAt`, capped at the crop's total), so a tile
  watered on day N grows at the rollover into N + 1. Watering settles an ended `WateredDay` into `GrowthDays`, then records today; watering twice on a day
  counts once; a day without water adds nothing; wetness is `WateredDay == today`, so it resets at the rollover with no work. Nothing is ticked or stored per
  tile timer; the client computes the same stage from the same numbers and the calendar.
- **Plant and harvest.** Planting keeps water poured earlier the same day (it counts) and drops water from an earlier day (it belonged to the old crop).
  Harvest leaves bare soil with `TendedDay` = today.
- **Revert.** Tilled soil with no crop reverts to untilled if any game day that ended after `max(TendedDay, WateredDay)` rolls below 30
  (`FarmRules.RevertRoll(instanceKey, x, y, day)`: FNV-1a of the instance key, then SplitMix64 rounds over tile and day; no stored random state, the same answer on
  every call, pinned by a test). The day of tilling or harvest is a day of grace, and a watered day is not a dry day. Reads treat a reverted tile as untilled
  (`FarmService.LiveTile`), so correctness never depends on a sweep. Rows from before day counting (no history) start their dry days when the farm loads.
- **Day-ended seam.** `IDayClock.DayEnded` (`Nosum.Abstractions.Calendar`, implemented by `DayClockService`): one timer aimed at the next day boundary, re-armed
  after every tick; each tick raises the event for every day since the last one (a late tick or a clock jump loses no day, at most 400 per tick); a throwing
  subscriber is swallowed and reported. Subscribers do side effects only. First subscriber: `FarmService` sweeps the loaded farms, deleting reverted tiles from
  memory and PostgreSQL and publishing them as removed to the subscribers of each tile's chunk (ADR-022). Resource-node regrowth and sprinklers subscribe the same way.
- **Dev clock.** `DevAdvanceClock` (`0xF001`, reply `0xF041` with the new server clock) in the Dev band, Development only like `DevGrant`. In Development the host
  wraps its clock in `DevTimeProvider` (system time plus an offset) and every service reads time through it; `DevClockService` adds whole days (1 to 60) and calls
  `DayClockService.Reconcile`, because timers measure real intervals and do not notice a jump. Production registers neither the service nor the handler.
- **Wire (append-only).** `FarmTileRow` keeps orders 0 to 6 (4 to 6, the old millisecond fields, are now always zero) and appends `GrowthDays` (7), `WateredDay`
  (8) and `Removed` (9, the client drops the tile). `NosumProtocol.Version` is not bumped; the coordinator may drop fields 4 to 6 at the bump. The client
  mirror (`FarmTileInfo`, `FarmDecorator`) uses `FarmRules.StageAt` / `IsWet` with the calendar it already loads; until the calendar arrives no crop is drawn.
- **Storage.** Migration `GrowthInGameDays`: `farm_plots` loses `growth_ms`, `settled_at_ms`, `wet_until_ms` and gains `growth_days`, `watered_day`, `tended_day`.
  Crops planted before it keep their crop and planting time but restart with zero growth.
- **Balance.** Parsnip 1/1/1/1 (4 days), radish 1/2/1/2 (6), cauliflower 1/2/4/4/1 (12) watered days. Cauliflower's 12 days need 12 watered days inside
  its 28-day spring or summer, so a gap of more than 16 dry days fails it.

## Alternatives considered

- **Harvest into the inventory (today).** Simple, but a full bag blocks harvesting, and drops are needed anyway for
  trees and stones. Rejected.
- **Client-side node simulation.** Faster to build, but lets clients invent wood; violates ADR-003. Rejected.
- **Real-time crop seconds kept alongside game days.** Two clocks for one rule; rejected — the dev clock covers tests.

## Consequences

- Farm, placeables and nodes all live in the per-instance world (`IInstanceWorld`); one lock per instance remains.
- Two new tables (`resource_nodes`, `ground_items`), generated migrations.
- Seasons (ADR-018) are on `master`; D7 is implemented on top of its calendar (see Implementation notes).

## Implementation notes

Ground items (D5, D6, D9 ground-item part), implemented 2026-10-25:

- **One transaction for a harvest.** `IGroundItems.Stage` adds the `ground_items` rows to the caller's unit of work, so `FarmService` resets the plot and leaves the produce in one commit (a crash cannot lose a harvest);
  `CompleteAsync` files and announces the drop after the save. `DropAsync` is the same in its own transaction, for callers with nothing else to save.
- **Pickup runs on server position changes only**, after every accepted `MoveIntent` (`PlayerService` calls `OnPlayerMovedAsync`, not awaited), looking at the nine chunks around the player. A player who stands still next to a
  fresh drop takes it with their next move report; there is no timer scanning items. A harvest lands at the plot centre nudged toward the harvester (`GroundItemRules.PopPosition`) so it is within the 1.5 tile radius at once.
- **Atomicity.** An in-memory claim (per item) stops two players from both trying; the transfer deletes the row in the same transaction as the inventory add, and a missing row or a lost save race rolls everything back.
  A full inventory is `NoSpace`: the claim is released and the same player is not retried for 2 s (no database hit every 100 ms). Pickup is all-or-nothing per stack (no partial take).
- **Private instances (deviation).** D6 only says "on shared maps the cause has priority for 5 s". On a private farm there is no window and no expiry, but only the instance owner and the player who caused the drop may take an item,
  so a future visitor cannot walk off with a harvest. Revisit when the visiting/co-op rules (their own ADRs) say who may take what.
- **Stacks.** A drop larger than an item's stack size is split into several ground items (at most 8 per drop; larger drops are refused). Quantities are therefore always one inventory stack or less.
- **Wire** (`0x7200-0x72FF`, `Nosum.Contracts/GroundItem`): `GetGroundItems` (C2S), `GroundItemsSnapshot`, `GroundItemSpawned`, `GroundItemTaken` (S2C). `Taken` carries a reason (`PickedUp`, `Expired`) and the taker's position so the
  client can fly the item to them. Until ADR-022 lands, the snapshot is the whole instance and pushes go to the whole instance (`GroundItemService.PublishAsync` is the single place to change).
- **Table** `ground_items` (bigint identity id, map, owner, item, quantity, pixel x/y, created, cause, priority-until, expires-at). Times are Unix milliseconds from the server clock.

## Revisit when

Energy/stamina, tool upgrades, crafting or weather are designed; or a shared map holds so many nodes that the per-chunk
state exceeds one packet.

## Implementation notes

Resource nodes and the three tools (D1–D4, D8, the node part of D9) are implemented; ground items (D5 drops, D6), D7 and the node part of ADR-022 are not. Where the code differs from the text above:

- **Items.** Axe 3, pickaxe 4, scythe 5 (tools); oak/maple/pine seeds 190–192 (`ItemKind.TreeSeed`, the end of the seed band); wood 500, stone 501, fiber 502, coal 503, copper ore 504 (`ItemKind.Resource`, a new 500–599 band). The starter kit appends the three tools after the seeds, so hotbar slots 0–4 are unchanged and the first free slot is now 8. Players who already have an inventory do not get the new tools (the dev grant does).
- **D2, water.** The can's 40 units are kept per player in memory (`IToolState`); after a server restart a can is full again. Water is a tile of the `Back` layer with the `Water` property; there is no separate well. The can is pushed as `ToolState` and sent with the node snapshot.
- **D3, hits.** A hit on a standing node changes memory and is pushed; the row changes when the node is created, grows, becomes a stump or breaks, so a restart heals a half-chopped tree. A tree younger than grown (seed, sapling, bush) falls in one hit and leaves no stump; a grown tree takes ten and leaves a stump with the same id and species. Only one node per tile (unique index).
- **D4a, the `Paths` index table.** Read off the early paths sheet and checked against `Farm`, `Forest`, `Town`, `Backwoods`, `BusStop` (see `NodeSites`): 9/10/11 oak, maple, pine; 13–15 weed; 16–17 stone; 18 twig; 22 grass. Everything else (forage, palms, logs, spawn areas) is ignored. The maps mark hundreds of debris sites (the farm over 1000), so debris sites are thinned when an instance is first filled (weed 40 %, stone 35 %, twig 40 %, grass 15 %; trees always), skipping blocked tiles and the tiles around arrival points and exits. The fill happens once per instance (`node_instances` row), never again, even if every node is later cleared.
- **D4b, regrowth.** `IResourceNodeService.AdvanceDayAsync(day, seasonChanged)` is the seam. The server calendar (ADR-018) computes dates from the clock and has no "day ended" event, so `CalendarGameDayClock` (`IGameDayClock`) gives the game day number and `NodeDayService` watches it (a few times per game day) and calls the seam when the day changes; `seasonChanged` is true when the new day starts a new season. A public instance regrows at most 12 weeds, stones or twigs per day on free marked sites (never under a player); a private or co-op instance regrows weeds only at a season change. An instance that was not loaded catches up on its first load (trees age by the days missed, a public place regrows once).
- **D4c, tree seeds.** A tree seed planted on free, walkable, unreserved ground (not tilled soil, not under the player) becomes a seed-stage tree that grows a stage every two game days (`NodeLimits.DaysPerTreeStage`).
- **D5, yields.** Data per kind in `NodeCatalog`; rolled with a deterministic seeded generator (`NodeRandom`). The yield leaves through `INodeYieldSink`. Today `InventoryYieldSink` puts it into the breaker's inventory (transfer service, same transaction as the node row); a full inventory refuses the breaking hit and the node stays. Ground items replace the sink.
- **D8, cooldown.** 300 ms per player on real time (not the injected game clock), started by every swing of a `Tool` item that passes the slot, map and reach checks, including one the farm then refuses. The existing farm wire test waits 330 ms between uses accordingly.
- **D9, wire.** `0x7100` get nodes (the client asks after joining a map, like the farm), `0x7140` node snapshot, `0x7141` node changed, `0x7142` tool state, `0x7143` farm tiles cleared (new packets only; `FarmStatus` and `FarmAction` gained enum values; `NosumProtocol.Version` unchanged). Nodes are published to the whole instance through one private method (`ResourceNodeService.PublishAsync`); ADR-022 moves it to the chunk's subscribers. A snapshot carries every node of the instance (at most 1500) in one packet until chunks exist.
- **Farm layer.** The pickaxe clears an empty tilled tile (`FarmAction.Cleared`); the scythe harvests a mature crop, as does any held item (unchanged: a mature crop is cut whatever is held).

### Coordinator merge notes (2026-10-25)

- Node yields now go through `GroundYieldSink` (drops at the tile, same transaction as the node change, then the breaker
  in reach picks them up at once); `InventoryYieldSink` stays for tests that need no ground layer. A full bag no longer
  blocks breaking.
- Visitors (ADR-019) are refused every tool use except the watering can before the node layer runs, so they cannot
  chop, break or scythe on someone else's land.
- `FarmStatus` values of the node work were renumbered to 17–20 at merge (16 is `NotYourFarm`, already on master).
- Follow-ups: give existing accounts the three new tools; a wire test for "visitor cannot chop"; co-op audit lines for
  node yields on co-op land.

## Amendments

### 2026-10-31 - dry days revert bare soil and wither crops (D7 amended, farm feel)

Supersedes the random revert of D7 above. Soil and crops now decay on a fixed count of dry game days, decided on the server (issue: farm feel,
`docs/qa/farm-feel-2/`).

- **Revert (replaces the 30 % roll).** Tilled soil with no crop reverts to untilled after `FarmRules.DryDaysToRevert` (3) game days in a row ended without
  water: no hand watering, no rain on an outdoor tile, no sprinkler beside it (`FarmRules.IsWateredOn`). The run starts after `max(TendedDay, WateredDay)`, so
  the day of tilling or harvest is a day of grace. `FarmRules.RevertRoll` and `RevertChancePercent` are removed. The rule is pure, so reads
  (`FarmService.LiveTile`) and the day sweep agree and a reverted tile never comes back.
- **Wither (new, beside the season rule of ADR-018).** A planted crop withers after `FarmRules.DryDaysToWither` (2) game days in a row ended without water, counted
  after `max(planting day, WateredDay)`. Rain outdoors and sprinklers count as water. A ripe crop that is not harvested is subject to the same run, as it is
  to the season. Watering a withered crop is refused with `FarmStatus.Withered` (`FarmRules.Water`), so the dry run stays a fact of the past. A withered crop
  is cleared with the scythe (`CutWithered`) or replanted over, as before.
- **Day sweep.** `FarmService.SweepFarmAsync` still deletes reverted rows and publishes them as removed (ADR-022 D4). A crop that withered at the day's end
  is published again in its chunk's batch, so every client redraws it withered at once.
- **Limits.** The runs are computed from the stored days, the calendar, the weather and the sprinklers standing now. Removing a sprinkler later can make
  past days dry and so decay a tile retroactively. The client's action-key check (`PlayerActions.FacesCrop`) knows whether the map is outdoors but not the
  sprinklers beside a tile; a crop that only a sprinkler keeps alive may read as withered there, and the server still decides what the use does.
- **Tests.** `FarmDryDayRulesTests` (unit: revert, wither, rain and sprinkler breaking the runs, watering refused, replant, cut); `FarmWireTests.Decay_*`
  (integration: the wither and its refusal over the wire, the revert after three dry days, a crop watered daily never withers).

### 2026-10-31 (2) - ripe crops wait dry; thirsty warning; the server sends the withered flag (D7 amended)

Amends the 2026-10-31 section above in three places; the rest stands.

- **Ripe crops are exempt from the dry run.** A crop that has ripened (`IsMature`) is not dried out by later dry days, so it waits for its harvest. A wither
  that came before the ripening stands, and rain that ripens a dead crop later does not revive it: `FarmRules.IsWithered` walks the days once (`WalkCrop`), the
  same count as `GrowthDaysAt`, and stops at the first answer that cannot change. The season rule (ADR-018) still applies to ripe crops.
- **The client takes the withered state from the server.** `FarmTileRow` appends `Withered` (order 11); the protocol moves to version 28. `PlayerActions.FacesCrop`
  and the decorator read the flag and no longer recompute it, so a sprinkler-watered crop is never shown or refused as withered on a guess. The server sets the
  flag wherever it sends a tile (the sweep, the chunk snapshot, the action push and `GetFarm`). The flag is as fresh as the last push of that tile: a withering
  at a day's end is pushed by the sweep, and a sprinkler placed mid-day takes effect on the next push.
- **Thirsty warning (new).** `FarmRules.IsAtRisk`: a planted crop one dry day from withering, or bare soil one dry day from reverting, on a tile not watered
  today. The engine tints such tiles (`FarmDecorator`); when a game day begins `ThirstWarning` shows "N crops need water" on the message line once, counting the
  planted crops that are at risk and not yet withered. Nothing is stored and the server decides nothing from it.
- **Also fixed.** `GetFarm` now sends `TendedDay`, which it had left out, so a reloaded client decides bare soil's dry days as the sweep does.
- **Tests.** `FarmDryDayRulesTests` (ripe exemption, sticky wither, thirsty crop and bare soil, sprinkler and watering); `FarmChannelTests` (the flag reaches
  the mirror); `FarmWireTests.Decay_*` asserts the flag over the wire.
