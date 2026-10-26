# ADR-024 — Crafting, chests and placeables outdoors

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M2. Amends ADR-015 D2 (`MapSpec.Placeable`
now includes the farm and co-op land) and builds on ADR-021 (materials from nodes) and ADR-023 (skill unlocks).

## Context

Materials (wood, stone, fiber, coal, ore) exist once ADR-021 lands but have no use besides selling. Crafting turns
them into the objects that make a farm grow: storage, fences, paths, sprinklers, a furnace. Placed furniture
(ADR-015) already gives the server a per-instance placed-object store, footprints in the walk grid, and a client
mirror; outdoor objects reuse it.

## Decision

- **D1 — Recipes are data** in `Nosum.Gameplay` (`RecipeCatalog`): output item, quantity, inputs, unlock (skill +
  level, or "known from start"). Crafting is one intent; the server checks the unlock and does one atomic transfer
  (ADR-013) removing inputs and adding outputs. Crafting is possible anywhere from the inventory (no workbench in M2).
- **D2 — First recipes:** chest (wood 50), wood fence (wood 2), stone path (stone 1), scarecrow (wood 50, coal 1,
  fiber 20; farming 1), sprinkler (copper bar 1, iron bar 1; farming 2), furnace (copper ore 20, stone 25; mining 1),
  and three cooking recipes with `ItemKind.Food` outputs (salad, baked potato, pumpkin soup) through the same mechanism.
- **D3 — Outdoor placement.** The farm, co-op land and the farmhouse are placeable; public maps are not. Outdoor
  placement requires a free, walkable, non-tilled tile with no node; the same intents as furniture (place, move,
  pick up), so the client's arrange mode works outdoors too.
- **D4 — Behaviours** live in the server, keyed by item: chest (36-slot storage per placed chest; open/close intents;
  moves are atomic transfers between the player and the chest, audited like the co-op warehouse; only the owner and
  co-op members on co-op land may open), sprinkler (waters the 4 orthogonal tiles at day start — runs in the day
  rollover), scarecrow (no crow mechanic yet: decorative until M3 adds crows), furnace (5 ore + 1 coal → 1 bar,
  ready after 30 game minutes; collect intent), fence/path (blocking / decorative).
- **D5 — Ids:** materials and bars 500–599, crafted placeables 600–699 (food 700–799 per ADR-023).
- **D6 — Wire.** A new sub-range of the Placeable band (`0xB100–0xB1FF`) for craft, chest and furnace intents and
  pushes. Chest contents are sent when opened, not with the chunk state. Coordinator bumps the protocol version.
- **D7 — Client.** A crafting page in the game menu (grid of recipes, locked ones greyed with the unlock text, inputs
  with have/need counts); a chest page that shows the chest grid above the player's inventory (the Minecraft layout
  of ADR-017).

## Alternatives considered

- **Workbench-only crafting.** Adds a walk for no gain at this size. Revisit when recipes number more than ~40.
- **Chest contents in the inventory table with a container id.** Chosen variant of the existing inventory store: a
  chest is one more inventory owner (player, co-op warehouse, chest) — implementations should generalise the owner
  key rather than add a second store.

## Consequences

The day rollover gains work (sprinklers, furnaces); it must stay O(objects changed), batched per instance.

## Implementation notes

Implemented 2026-10-25 on `feat/crafting`. Where the code says more than the text above, or differs:

- **D1, recipes and the skills seam.** `Nosum.Gameplay.Crafting`: `RecipeCatalog` (ids 1-9, append-only), `RecipeDefinition`, `RecipeUnlock` (a `CraftSkill` and a level, or known from the start), `RecipeRules` (`Check`, `IsUnlocked`, `CanCraft`).
  `CraftingService` is one `IInventoryService.ApplyAsync` (the inputs are removed first, so room freed by them counts for the output), audited with the reason `craft.<key>`. A batch is 1 to 99 crafts.
  Unlocks ask `ISkillLevels` (`Nosum.Abstractions/Skills`); until ADR-023's skills exist `FixedSkillLevels` answers level 10 for every skill, so every recipe is unlocked. **Replace the registration in `Program.cs`** when the skills land; `CraftSkill`
  (Farming, Foraging, Mining) is the enum the energy work can reuse or map. The client learns what is unlocked from `GetRecipeBook` (the server decides; the client only greys a row).
- **D2, recipes.** The six placeable recipes are as written. The cooking recipes use the produce that exists today because potato and pumpkin belong to the crop roster (ADR-025): salad = radish + cauliflower, baked potato = 2 parsnip + wood, pumpkin soup = 2 cauliflower +
  radish + wood. Swap the inputs when ADR-025 lands; the outputs (ids 700-702, `ItemKind.Food` = 8, appended) and the recipe ids stay. Iron ore (505), copper bar (506) and iron bar (507) were added to the 500-599 band; nothing yields iron ore before the mine (ADR-026),
  so the sprinkler cannot be crafted in practice yet. Stacks: fence and path 99, the other crafted placeables 20.
- **D3, outdoor placement.** `MapSpec.Placeable` is now the farm, the farmhouse and co-op land. `PlacedSet.Check` takes an `IGroundLayers` (tilled soil, nodes) and returns `Tilled` / `Node`; the server fills it from `IFarmTiles` (`FarmService`) and
  `INodeTiles` (`ResourceNodeService`) before taking the instance lock. The other direction (nothing is tilled, no tree seed planted and no debris regrown under a piece) goes through `PlacedTiles` (`IPlacedLookup`), a singleton index of every loaded
  instance's pieces that `PlaceableService` republishes after each change; it is a separate object so the farm, node and placeable services need no reference cycle. `InstanceWorld` joins the node grid and the placed grid on the farm (`WalkGrid.BlockWhereBlocked`,
  cached, rebuilt when either layer replaces its grid); the client's `MapScene` does the same. `PlacementLimits.MaxPerInstance` went from 200 to 400 (a fenced field takes dozens of pieces). On co-op land the placer must be a member *now* (read from `ICoopMembership` on every change) and every place
  and pick-up writes a co-op audit line (`coop.place`, `coop.pickup`); a visitor to a private farm is refused as before (`NotYourPlace`).
- **D4, behaviours.**
  - **Inventory owners.** There is still one store: `player_inventories` and `inventory_slots` are keyed by an owner id, which is a player's account id or, for a chest, `ContainerIds.ForChest(objectId)` (a Guid with a fixed marker, so it is recognised without a lookup and can never be an account id). The foreign key from
    `player_inventories` to `players` was dropped for that reason. A container's inventory is created on first use with `ChestRules.Capacity` (36) slots and no starter kit, writes no `inventory_log` rows (there is no player to log against; `chest_log` is its trail) and pushes nothing.
    `InventoryOperation.AddToSlot` (and `InventoryGrid.AddToSlot`) name a target slot.
  - **Chest.** `ChestService`: open (owner, or a member on co-op land: `PlacedAccess.MayUse`; reach 32 px like farming), `ChestMove` between the bag and the chest (one `ApplyManyAsync` over both owners, so both inventories are locked together, both change or neither does; the chest's `ChestLogEntry`
    is written in the same transaction; two players taking one stack race on the slot and the second finds it gone), a part of a stack never swaps with another item, tools never enter a chest. Opening makes the player a viewer; every change pushes `ChestUpdate` (all 36 slots) to the viewers still in the instance, and a player who leaves
    or whose chest is picked up stops watching (`ChestClosed`). A chest can be picked up only when empty (`PlaceStatus.NotEmpty`); the pick-up locks the chest's inventory together with the player's (`IPlacedBehaviour.OwnersOf`) so a deposit cannot slip in between the check and the delete, and the chest's own rows go with it.
  - **Sprinkler.** The rule is pure: `SprinklerRules.Waters` / `IsWatered(placedItems, x, y)` (the four orthogonal neighbours, not its own tile). The server exposes it as `IPlacedLookup.IsSprinklerWatered(instance, x, y)` (a lock-free read), so the growth rule can ask "is this tile watered on day N" with no tick and no stored state.
    **Not wired yet**: crop growth in game days (ADR-021 D7, `feat/growth-days`) is the consumer and owns `FarmService`'s growth; when both are merged, its day evaluation should treat a covered tile as watered.
  - **Scarecrow** is decoration (`PlaceableKind.Decoration`). **Fence** blocks walking; **path** is a piece that does not (`PlaceableDefinition.BlocksWalking = false`) but still takes its tile.
  - **Furnace.** A job is one `furnace_jobs` row (ore item, start time in Unix ms) inserted in the same transaction that removes 5 ore and 1 coal, and deleted in the one that adds the bar; ready-ness is `FurnaceRules.StateOf(start, now, calendar)`, so 30 game minutes are `30 * DayLengthMs / 1440` (15 real minutes at the default day length) and nothing ticks.
    One job per furnace; a furnace with a job or an uncollected bar cannot be picked up (`PlaceStatus.InUse`); collecting needs room in the bag (`NoSpace` keeps the bar inside). Start and collect take the instance lock, so they cannot interleave with the furnace being moved or picked up. `FurnaceChanged` is pushed on start and collect; the ready moment is not pushed (the client counts `RemainingMs` down).
    Smelting copper ore and iron ore are the two jobs. On co-op land they write `coop.smelt` / `coop.collect` audit lines.
- **D6, wire.** Placeable band slice `0xB100-0xB1FF`: C2S `Craft` 0xB100, `GetRecipeBook` 0xB101, `OpenChest` 0xB110, `CloseChest` 0xB111, `ChestMove` 0xB112, `StartSmelting` 0xB120, `CollectFurnace` 0xB121, `GetFurnaces` 0xB122; S2C `CraftResult` 0xB140, `RecipeBook` 0xB141,
  `ChestSnapshot` 0xB150, `ChestResult` 0xB151, `ChestUpdate` 0xB152, `ChestClosed` 0xB153, `FurnaceResult` 0xB160, `FurnaceSnapshot` 0xB161, `FurnaceChanged` 0xB162. New enums `CraftStatus`, `ChestStatus`, `ChestSide`, `FurnaceStatus`, `FurnaceStateCode`; `PlaceStatus` gained `Tilled` 15, `NodeInTheWay` 16, `NotEmpty` 17, `InUse` 18.
  Everything is new packets and appended values; no existing opcode or `[SerializeOrder]` changed. All handlers are `USER`-only and rate limited, take the identity from the connection, and are not encrypted (they move a player's own items, like `MoveSlot` and `PlaceObject`).
- **D7, client.** `CraftingChannel`, `ChestChannel`, `FurnaceChannel`; `CraftingViewModel`, `ChestViewModel`, `FurnaceViewModel`; `CraftingPanel` (a menu tab), `ChestPanel` (chest grid above the bag, click a stack then a slot to move, merge or swap; shift-click sends it to the other side) and `FurnacePanel` (opened with the action key facing the piece).
  Placed objects are drawn from their own sheets (`assets/sprites/craftables.png`, `assets/sprites/items.png`, `assets/sprites/furniture.png`); a path is drawn flat; a working furnace uses the lit sprite. Early art only (ADR-007).
- **Persistence.** One migration, `CraftingChestsFurnaces`: drops `fk_player_inventories_players_player_id`, adds `chest_log` and `furnace_jobs` (cascade from `placed_objects`).
- **Known gaps.** Sprinklers are not yet read by crop growth (above). The client's placement ghost knows only the placed set and the map's own grid, so over a tree or tilled soil it can look valid; the server refuses with `Place_Status_NodeInTheWay` / `Place_Status_Tilled`.
- **Co-op land.** A lone founder cannot dissolve a co-op while a chest on its land holds items or a furnace holds a job (`CoopStatus.CannotLeave`, like a non-empty warehouse); otherwise the land's placed objects and the empty chests' inventories are deleted with the co-op (furnace jobs cascade).
