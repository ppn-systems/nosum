# ADR-011 — Inventory, farming and quests: data model and who decides what

**Status:** Proposed

## Context

Farming is the first feature that gives players things (seeds, harvests) and changes shared world
state (tilled soil, crops). The market, co-op warehouse and wardrobe will build on the same items, so
the inventory must be a foundation, not a farming detail. The rules of [ADR-003](ADR-003-server-authoritative-websocket.md)
(server decides, growth from timestamps) and [ADR-010](ADR-010-internationalization.md) (no display text on the wire,
game data by stable key) apply. Product numbers below are defaults chosen by the implementing agent and are open owner questions.

## Decision

- **D1 — Catalogues are data in `Nosum.Gameplay`.** Items (`ItemCatalog`), crops (`CropCatalog`) and quests (`QuestCatalog`) each have a
  stable numeric id (wire and database) and a stable key (localization). Ids are append-only. Names, descriptions and quest text live in
  `Catalogue.resx` (`vi` default, `en`) in `Nosum.Wasm.ViewModels`, found by `Item_{key}_Name` and so on; the server never needs the text. A test
  checks that every catalogue entry has text in both languages.
- **D2 — Inventory.** One `PlayerInventory` header per player (capacity 36 = 4 rows of 9, held hotbar slot) and one `InventorySlot` row per
  occupied slot, keyed (player, slot). The stacking rules are the pure `InventoryGrid` (add tops up stacks then fills empty slots, remove, move =
  move / split / merge / swap, all-or-nothing). The first nine slots are the hotbar. A new inventory is created, with the starter kit
  (hoe, watering can, 8 parsnip, 6 radish, 4 cauliflower seeds), on the first read or change.
- **D3 — `IInventoryService` is the only way to change items.** `ApplyAsync` applies a list of operations atomically, serialized per player
  (striped in-process lock; the primary key is the backstop and a lost race is `InventoryStatus.Conflict`), writes one `InventoryLogEntry`
  (player, item, delta, stable reason code such as `farm.harvest`, time) per item in the same transaction, and pushes `InventoryUpdate` with only the
  changed slots. An optional `enlist` callback runs inside the same transaction, so a feature changes its own data and the items together
  (plant = one seed leaves the slot and the tile gets its crop, or neither). The market, trades and the co-op warehouse call the same method.
- **D4 — Farm tiles.** `FarmPlot` rows, keyed (map id, x, y), exist once the soil is tilled. They hold `FarmTile` values: crop id, planted-at, watered
  growth settled so far, the moment it was settled, and the moment the soil stops being wet (Unix ms). Tiles are per map, not per player; only
  maps with `MapSpec.Farmable` (today the farm) accept farming, and tillable tiles are the `Diggable` ground that nothing blocks (`TillGrid`).
- **D5 — Growth is computed, never ticked.** A crop grows only while the soil is wet; watering wets it for 90 s (`FarmRules.WetDurationMs`).
  Stage = the crop's stage table applied to `settled growth + wet time since the settle`. Watering settles the growth first. The same pure
  function runs on the server (harvest check) and on the client (the wire carries the stored timestamps and the server clock, not stages), so a
  crop grows on screen without a push per stage. Durations are short dev values (parsnip 60 s, radish 70 s, cauliflower 110 s) kept as data.
- **D6 — Use item on tile.** The client sends `UseItem(hotbarSlot, tileX, tileY)`. The server reads the held item from its own inventory, the player's
  position from its own presence record (reach 32 px to the tile centre), the tile from its own state, and decides: a mature crop is harvested whatever is
  held; otherwise hoe tills, watering can waters, a seed plants. Refusals are `FarmStatus` codes. Tile changes are multicast to the map group as `FarmTilesChanged`;
  a player who enters a map reads the whole farm with `GetFarm`.
- **D7 — Quests.** `IQuestService.ReportAsync(player, QuestEvent)` counts what players do toward their quests (progress per objective, persisted as
  `PlayerQuest`), pushes `QuestUpdate` and raises `Completed`. Features report; quests never call features. The tutorial quest (`first_harvest`: plant
  once, harvest once) unlocks the guest registration invitation through `IRegistrationPromptService` and the `RegistrationPromptUnlocked` push.
- **D8 — Wire.** Bands: Farm `0x3000`, Inventory `0x5000`, Quest `0x6000`; the registration push is `0x1085` in the Account band.
  Handlers are `USER` only, identity from `connection.GetPlayer()`, rate limited per address.
- **D9 — Client.** Channels in `Nosum.Wasm.Socket` mirror inventory, farm and quests; view models in `Nosum.Wasm.ViewModels` localize them; reusable dumb
  controls (hotbar, slot, inventory grid, quest tracker, message line) live in `Nosum.Wasm.Controls`. The frame loop draws soil and crops through a
  `FarmDecorator` and reads keys as batched edges (`InputEdges`): E or Space uses the held item on the tile in front, 1-9 select, I opens the inventory.

## Alternatives considered

- **Growth ticked on the server** — violates ADR-003 D4 and costs work for offline farms.
- **Crop stage on the wire** — needs a push for every stage change, or stale crops.
- **Farm state per player** — the current single shared farm map matches the pillars (shared, server-wide); a private farm per player is a later map-level decision.
- **Inventory as one JSON column** — cannot be queried for the market and loses the per-slot constraint.

## Consequences

- Every feature that touches items must go through `IInventoryService` (and add its reason code), or the audit trail has holes.
- Farm tiles and the quest/inventory services are cached or locked in process; like sessions and presence they assume one server process.
- Changing a crop's durations changes growth of crops already planted (the stored values are time, not stage).

## Revisit when

Several server processes run; farms become per player or per co-op; crops need seasons, quality, fertilizer or regrowth; the server needs item text (mail,
market notes), which moves the catalogue resources into a shared project.
