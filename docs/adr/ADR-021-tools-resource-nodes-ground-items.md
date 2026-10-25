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
- **D4 — Where nodes come from.** (a) Initial placement from the map itself: Stardew maps mark trees and debris spawn
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

## Alternatives considered

- **Harvest into the inventory (today).** Simple, but a full bag blocks harvesting, and drops are needed anyway for
  trees and stones. Rejected.
- **Client-side node simulation.** Faster to build, but lets clients invent wood; violates ADR-003. Rejected.
- **Real-time crop seconds kept alongside game days.** Two clocks for one rule; rejected — the dev clock covers tests.

## Consequences

- Farm, placeables and nodes all live in the per-instance world (`IInstanceWorld`); one lock per instance remains.
- Two new tables (`resource_nodes`, `ground_items`), generated migrations.
- Seasons (ADR-018) must merge before D7 can switch the units; until then D7 is implemented behind the calendar seam.

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
