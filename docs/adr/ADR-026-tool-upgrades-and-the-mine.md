# ADR-026 — Tool upgrades, the blacksmith and the mine

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M2. Builds on ADR-021 (tools, nodes),
ADR-023 (mining skill), ADR-024 (bars from the furnace), ADR-012/015 (maps and instances). Amended by ADR-037: the entrance cave is entered from the
Mountain, not from Town (D3 and the "Maps" note below).

## Context

Ore and bars need a sink and a source that scales with progress; the genre's answer is tool upgrades paid in bars and
coins, and a mine whose deeper floors hold better ore.

## Decision

- **D1 — Tiers.** Basic → copper → iron (gold/iridium later). `ToolTier` on the tool item (separate item ids per tier,
  appended in 1–99). Effects: axe/pickaxe damage per hit = tier + 1; hoe and watering can affect a line of
  1/3/5 tiles when the use is held (charged use: the client sends the tier's area intent, the server validates it);
  watering can capacity 40/55/70. Energy cost per tile (ADR-023).
- **D2 — Blacksmith counter** in Town (the `Blacksmith` building door, tile recorded in `MapSpec.Shops`): upgrade
  copper = 2 000 coins + 5 copper bars, iron = 5 000 + 5 iron bars; also sells coal and ore at high prices. The
  upgrade takes two game days (the tool is held by the blacksmith; collect intent) — keeps the genre's trade-off.
- **D3 — The mine.** A shared public map `Mine` (entrance from Town or Mountain when it exists; Town edge for now)
  leading to floors built from `Maps/Mines/*.tmx` layouts. Floors are **per player instances** (`mine<N>@<player>`) so
  nobody's stones are taken by others; floors 1–40 in M2. Each floor spawns stones, copper (1–39) and iron (20–40)
  nodes and one ladder hidden under a stone (breaking it reveals the ladder to the next floor). Floor layouts are
  rebuilt every game day. An elevator every 5 floors remembers the deepest floor reached (persisted per player).
- **D4 — No combat in M2.** Monsters are an M3+ topic with their own ADR.
- **D5 — Wire.** Mine floors reuse warps, nodes and chunks; blacksmith uses the shop band (0xC000) append-only.

## Consequences

Per-player floor instances multiply node state; floors are kept only in memory, created on entry and dropped
after the player leaves (only the deepest floor is persisted). The chunk system (ADR-022) applies unchanged.

## Implementation notes

Built 2026-10-25. Where the code differs from the text above, or the text left a choice open:

### D1 — tiers

- **Items.** Tier is data on the item: `ItemDefinition.Tier` (`ToolTier` basic / copper / iron). Copper tools are ids 10–13 (hoe, can, axe, pickaxe), iron 20–23; the five basic ids are unchanged. The scythe has one tier. `ToolRules` holds the numbers: damage per hit = tier + 1, line 1 / 3 / 5, can 40 / 55 / 70.
- **Damage.** `NodeRules.Hit(node, tool, damage)`; the node layer passes `ToolRules.DamagePerHit` for an axe or pickaxe (a copper pickaxe breaks copper ore, 3 hits, in 2 swings; an iron one breaks iron ore, 4 hits, in 2). The 300 ms cooldown is untouched.
- **Can.** `IToolState` gained `CapacityOf` and `RefillAsync(player, capacity)`: the capacity is that of the can last filled and is pushed in `ToolState` and in the node snapshot, so the water meter shows it. A can refilled at water takes the capacity of the held can.
- **Wide use is a new packet, not a field.** `UseItemArea` (`0x3002`: slot, first tile, `Tiles` 1/3/5) so `UseItem` and `NosumProtocol.Version` stay as they are. The server checks the claim against the item in the slot (`ToolRules.ValidateArea`: only a hoe or can, only up to its tier; refusal `FarmStatus.InvalidArea`), takes the direction from the facing it keeps (never from the packet), starts **one** swing for the whole line and requires only the first tile to be within reach; every tile then goes through the ordinary single-tile path (so energy, nodes, visitors and co-op checks apply per tile, as ADR-023 expects). `FarmService` is now `partial` (`FarmService.Area.cs`). A ladder at the end of a line is skipped.
- **Deviation: the client key is R, not "held".** The input path carries one-shot edges and held directions only, and `InputEdges` / `renderer.js` have other owners in this wave, so the wide swing is the rotate key (`R`) with a hoe or can of tier above basic in hand; it is inert elsewhere (R rotates furniture only where furniture is placed, never with a tool map's `mapAllowsTools`). Making it a hold needs a held-action bit in the input path.

### D2 — blacksmith

- The counter is the doormat (94, 82) in front of the Blacksmith door (94, 81) of `Town.tmx` (`MapSpec.Shops`, shop id `ShopCatalog.Blacksmith = 5` (4 is the carpenter's, ADR-028; renumbered when the two branches met), which also sells coal 150, copper ore 75 and iron ore 150 through the ordinary `BuyFromShop`).
- Bars: `CopperBar` 506 and `IronBar` 507, ore `IronOre` 505 — the same ids and keys the crafting branch defines, so the two merge without a renumbering.
- Upgrade is **one transfer**: the old tool, the bars and the coins leave the player and a `tool_upgrades` row (one per player: tool item to give back, ready game day, started at) is written in the same transaction (`TransferEnlistment`); a second request while a row exists is `Busy` and takes nothing. The ready day is `GameCalendar.DayNumberAt(clock) + 2`, so the dev clock (`DevAdvanceClock`) skips it in tests and a restart keeps it. Collect deletes the row and adds the tool in one transfer; a collected can arrives full to its new capacity.
- Wire (shop band, append-only): `GetBlacksmith` `0xC001`, `UpgradeTool` `0xC002` (item id of the tool handed in), `CollectTool` `0xC003`, reply `BlacksmithState` `0xC041` (status, tool being made, ready day, today).

### D3 — the mine

- **Maps.** `Mine` (`Mine.tmx`, shared) is entered from the five-tile Town north road x 79–83 (the file's own exits to the unhosted Mountain are dropped; ours replace them), arrival (18, 11) in the cave, way back (18, 13) → Town (81, 2). The shaft is the file's `Mine` action tile (23, 9), opened so the farmer can step into it; stepping on it warps to floor 1, and the way up from floor 1 arrives at (23, 10) in front of it. The elevator is the file's `MineElevator` tile (17, 3). Floors are `mine1` … `mine40`, one private `MapSpec` each (`MapSpec.MineFloor`), plans `Maps/Mines/<n>.tmx` (1–29 and 41 for floors 1–30, the dark 31–39, the desert 40 for floor 40: `MineLayouts`). A floor's way up is its arrival tile; floors 5, 10 … 40 have the elevator at the arrival tile. `MapSpec.TmxFile` may now name one sub-folder (`Mines/17.tmx`); a sheet is named relative to its map's folder (client `MapLoader`, test of the links).
- **A floor keeps its plan; what stands on it is rebuilt.** The layout of a floor is fixed (the map catalogue is static); the nodes are `MineFloorPlanner.Build(floor, seed)` with `seed = MineRules.FloorSeed(player, game day, floor)`, so the same player on the same day always gets the same stones and ladder, nobody shares a floor, and the next game day builds another. Nodes exist only where a player can walk from the arrival tile; the arrival and its surroundings stay clear. Stones: a tenth of the reachable tiles (6–120); copper ore 3 + reachable/120 on floors 1–39; iron ore 2 + reachable/120 + (floor − 20)/5 on floors 20–40; one stone on every floor but 40 hides the ladder, chosen among the farthest quarter by walking distance.
- **Memory only.** The floor's nodes are built in `ResourceNodeService` when the first player enters (nothing is read or written in `world_nodes` / `node_instances`), ids come from a counter, and `AdvanceDayAsync` skips them. `MineService` listens to the new `IMapPresence.PlayerJoined` and to `PlayerLeft`: when the last player leaves a floor it tells the node service and `IGroundItems.ForgetAsync` (which also deletes the floor's ground rows) to forget it; the first player on a floor also clears stale ground rows a crash left. Ground drops use the ordinary ground-item tables while the floor exists.
- **The hidden ladder.** The stone carries `NodeVariants.LadderStone`; the server never sends that variant (`ResourceNodeService.Public`), so a client cannot find the ladder by reading nodes. `NodeRules.Hit` turns the broken stone into a `NodeKind.Ladder` node with the same id and tile, which blocks walking like a stone. Using it is a tool use on its tile with anything in hand: the node layer answers `FarmAction.Descended`, and `MineLadderFarmService` (an `IFarmService` decorator) then takes the player down through `IMineService`. No new packet.
- **Elevator and depth.** `player_mines` (player, deepest floor) is the one persisted thing, written when a player arrives on a deeper floor. `RideElevator` (`0xC101`) checks the player stands within reach of an elevator point (the cave's, or a floor 5k's arrival tile) and has reached the stop; stops are the entrance and every fifth floor down to the deepest (`MineRules.ElevatorStops`). `GetMine` `0xC100`, reply `MineState` `0xC140`. A player who logs out on a floor returns to it, standing where they were unless that tile now holds a node, then at the arrival tile.
- **Privacy.** A floor is a private instance owned by its player and `VisitAccess` refuses anybody else for any mine floor, even somebody allowed on the owner's farm; its presence group, nodes, drops and pushes are keyed by `mine<N>@<player>`.
- **Wire and storage summary.** New: `UseItemArea`, `GetBlacksmith`, `UpgradeTool`, `CollectTool`, `BlacksmithState`, `GetMine`, `RideElevator`, `MineState`; appended enum values `FarmStatus.InvalidArea = 22`, `FarmAction.Descended = 10`, `NodeKind.IronOre = 9`, `NodeKind.Ladder = 10`; one migration `MineAndBlacksmith` (`player_mines`, `tool_upgrades`). `NosumProtocol.Version` unchanged.

### D4 / client

No combat. The blacksmith page is the lower part of the blacksmith's shop tab (`ShopPanel` shows `BlacksmithSection` under the price list when the shop is the blacksmith's); the elevator picker is a small dialog (`ElevatorDialog`) opened with the action key next to an elevator, not a menu tab. Nodes of a mine floor are drawn by the ordinary node decorator (iron ore `springobjects` 290, the ladder cell (3, 4) of `Mines/mine.png`).

### Merged with the chunk, weather and social waves

- **Chunks (ADR-022).** Floor nodes go out through `ResourceNodeService.PublishAsync` (`IChunkPublisher`, one `NodeChanged` per chunk) and a floor's nodes arrive as `ChunkState` rows from `FillAsync`, like any instance's; the hidden-ladder variant is stripped by `Public` in both paths. Ground drops use `GroundItemService`'s chunk path. `MineService` keeps listening to `IMapPresence.PlayerJoined` (the one event the presence now has, shared with the friends list) and `PlayerLeft`. Nothing on a floor is multicast to the instance.
- **Weather (ADR-027).** The cave and every floor are `MapSpec.Indoors`: no precipitation, no ambient tint from the sky, no rain watering.
- **Ids that had to stay clear of master.** `NodeKind.Forage = 8`, so `IronOre = 9` and `Ladder = 10` stay as built; `FarmStatus` ends at 21 on master, so `InvalidArea = 22`; `FarmAction.Descended = 10` follows master's `PlantedTree = 9`; shop ids are 1–3, the carpenter's 4 (ADR-028), then the blacksmith's 5, and the community board's counter id is 1000; item ids 10–13/20–23 and 505–507 are free in the merged catalogue (`CatalogTests` asserts unique ids). `OpcodeUniquenessTests` asserts that no two packet classes or opcode constants of `Nosum.Contracts` share a value.
- **Interiors (ADR-028).** The coop and barn interiors are master's own `indoors` private maps (`MapSpec.InteriorOf`); the mine adds `MineFloor`/`Elevator` beside them. In the client, the action key checks the shop, then the elevator, then the building interior.
- **Migration.** `MineAndBlacksmith` was regenerated on top of master's snapshot: it creates `player_mines` and `tool_upgrades` only.
- **Forage and the ladder.** A ladder is not broken by `NodeRules.Hit` (it answers `WrongTool`); an empty hand still picks up forage.

### Known weak spots

- Leaving a floor drops it, and coming back the same day rebuilds it from the same seed, so broken stones return: a player can re-farm a floor by stepping off and on. The cost is the energy of the swings (ADR-023); a stored per-day state would fix it.
- A stone or ore node can stand in a one-tile corridor and wall off the rest of a cave. Everything is breakable with the pickaxe the starter kit gives, so the player is never stuck, but a floor can need clearing.
- The layouts are Stardew's own and have void tiles outside the caves that the walk grid treats as walkable if a gap connects them; the planner only places nodes on tiles reachable from the arrival tile.
- Tool art is cut from the placeholder sheets (ADR-007) and the ladder reuses the mine sheet's ladder top; both are replaced with the rest of the art in M6.
