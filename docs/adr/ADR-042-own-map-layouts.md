# ADR-042 — Own map layouts: replace every Stardew level layout, and a gate that finds Stardew content by its hash

**Status:** Proposed (written by an agent; awaits the coordinator's review). Roadmap M6, the map part of ADR-040. Amends ADR-040 D9 (layouts stay Stardew's
until this ADR is accepted). Builds on ADR-007 (placeholder content), ADR-012 (maps and warps), ADR-015 and ADR-019 (private instances), ADR-020 (co-op land),
ADR-022 (chunks), ADR-026 (mine floors), ADR-028 (building interiors), ADR-034 (trader counter), ADR-037 (outdoor maps), ADR-040 (own art plan).

## Context

The owner is making the repository public. Under ADR-040 D9 the level layouts of the hosted maps are Stardew Valley's: the 53 `.tmx` files under
`art/own/Maps` keep Stardew's objects, exits, building footprints, mine floors and node sites. Stardew's map designs are creative work, so a public
release must not carry them. The coordinator has decided that every map gets an own layout before release. This ADR is the plan.

### What the repository holds today (measured)

Measured on `master` (commit `0005e14`). Reference: the unpacked Stardew Valley content is `assets/` at commit `8971108`
("feat(assets): add unpacked Stardew Valley content assets (#2)"), 2,844 files with 2,805 distinct SHA-256 values. The brief named the branch
`origin/feat/add-unpacked-assets`; that branch does not exist on `origin` or in this clone, so the commit is the reference. Commands to repeat
the measurement: `git ls-tree -r 8971108 -- assets` (blob ids), `git archive 8971108 assets | tar -x -C <dir>` and `sha256sum` (hashes).

- **53 `.tmx` files** under `art/own/Maps` (not 70): 40 mine floors (`Mines/1` to `Mines/41` without `30`), `Mine` and 12 others. Every one is loaded by
  `GameMaps` (`gameplay/Nosum.Gameplay/Maps/GameMaps.cs:98-153`); the mine floors through `MineLayouts.FileOf` (`gameplay/Nosum.Gameplay/Mines/MineLayouts.cs:82`).
- **43 are byte-identical** to the Stardew map of the same name (blob ids equal): `Barn`, `Coop`, `FarmHouse`, `Mine` and 39 of the 40 mine floors (all except `Mines/20`).
- **10 are not byte-identical**: `Backwoods`, `Beach`, `BusStop`, `Farm`, `Farm_Foraging`, `Forest`, `Mines/20`, `Mountain`, `Town`, `Woods`. Their `Back` layer
  was re-authored onto `own_terrain` tile ids and a `BackEdge` layer was added (ADR-040 phase 3, `tools/Nosum.MapReauthor`). Every other layer of these 10 files is
  Stardew's, cell for cell. So the statement in ADR-040 phase 3 that "layers stay the placeholder's, character for character" holds for 43 files, and for the other
  10 only except `Back`.
- **Exact-hash scan.** Every tracked file of the repository hashed against the 2,805 fingerprints gives 43 hits, and they are the 43 byte-identical maps. No other
  file (no sheet, sprite, code file or document) matches. The 95 files of `docs/art/placeholders.sha256` (under `assets/`) are all replaced.
- **The release gate does not see the maps.** `deploy/check-placeholder-art.sh public` exits 0 today with the 43 Stardew maps in the tree: the gate checks only the 95
  listed paths under `assets/` (ADR-040 D8). This is the gap the gate in D5 closes.

### Inventory

Each row: whether the file is byte-identical to Stardew's, which layers differ, the map size in tiles, the tilesets the file names (their pixels are
Nosum's painted sheets of the same names, ADR-040 phase 3), and the role the code gives it. Coordinates are tile `(x,y)`.

| Map file | Byte-identical to Stardew | Layers that differ | Size (tiles) | Tilesets used | Role |
|:--|:--:|:--|:--|:--|:--|
| `Backwoods.tmx` | no | `Back` re-authored onto `own_terrain` ids; `BackEdge` added | 50 x 40 | `monsterGraveTiles`, `outdoor`, `Paths`, `v16_outdoorsTileSheet2`, `v16_Shadows`, `own_terrain` | Shared backwoods north of the farm. Spawn (14,38). Node sites from the Paths layer. |
| `Barn.tmx` | yes | none | 18 x 15 | `barn`, `p` | Barn interior (private, one per barn, ADR-028). Spawn (11,13); door (11,14); animals on tiles 6..13 x 4..10 (code). |
| `Beach.tmx` | no | `Back` re-authored onto `own_terrain` ids; `BackEdge` added | 104 x 50 | `Paths`, `untitled tile sheet`, `v16_Shadows`, `own_terrain` | Shared beach south of Town. Spawn (39,2); bridge tiles (58..61,13) unlocked by the community goal; forage and node scatter (code). |
| `BusStop.tmx` | no | `Back` re-authored onto `own_terrain` ids; `BackEdge` added | 65 x 30 | `outdoors`, `Paths`, `spring_town`, `v16_outdoorsTileSheet2`, `own_terrain` | Shared bus stop east of the farm. Spawn (11,23). |
| `Coop.tmx` | yes | none | 12 x 10 | `Coop` | Coop interior (private, one per coop, ADR-028). Spawn (2,8); door (2,9); animals on tiles 2..9 x 4..7 (code). |
| `Farm.tmx` | no | `Back` re-authored onto `own_terrain` ids; `BackEdge` added | 80 x 65 | `extra_tiles`, `Paths`, `untitled tile sheet`, `v16_Outdoors2`, `own_terrain` | Each player's farm (private, ADR-015). Spawn (64,16) in front of the farmhouse door (64,15); exits to BusStop, Forest and Backwoods from the file's Warp property, and to FarmHouse through the door; code-drawn buildings at (59,10), (25,10), (71,14). |
| `FarmHouse.tmx` | yes | none | 12 x 12 | `indoor`, `untitled tile sheet`, `walls_and_floors` | Farmhouse interior (private, one per player). Spawn (3,11), the tile in front of the exit. |
| `Farm_Foraging.tmx` | no | `Back` re-authored onto `own_terrain` ids; `BackEdge` added | 80 x 65 | `Paths`, `spring_outdoorTileSheet_extra`, `untitled tile sheet`, `v16_Outdoors2`, `own_terrain` | Co-op land (CoopFarm, ADR-020): one shared farm per co-op. Arrival (46,22), gate (46,25); land unlocked by rectangles in code. |
| `Forest.tmx` | no | `Back` re-authored onto `own_terrain` ids; `BackEdge` added | 120 x 120 | `outdoors`, `Paths`, `untitled tile sheet2`, `v16_Shadows`, `v16_Waterfalls`, `own_terrain` | Shared forest south of the farm. Spawn (68,0). Node sites from the Paths layer. |
| `Mine.tmx` | yes | none | 77 x 20 | `paths`, `untitled tile sheet` | Mine entrance cave (shared, indoors). Spawn (23,10) in front of the shaft (23,9); exit to the Mountain (18,13); elevator (17,3); arrival from the Mountain (18,11). |
| `Mountain.tmx` | no | `Back` re-authored onto `own_terrain` ids; `BackEdge` added | 135 x 41 | `outdoors`, `Paths`, `untitled tile sheet2`, `v16_Shadows`, `v16_Waterfalls`, `own_terrain` | Shared mountain north-east of Town. Spawn (15,38); Mine gate (54,4), arrival (54,5); bridges (46..49,7); node scatter (code). |
| `Town.tmx` | no | `Back` re-authored onto `own_terrain` ids; `BackEdge` added | 130 x 110 | `Landscape`, `Paths`, `Town`, `v16_landscape2`, `v16_Shadows`, `v16_Waterfalls`, `own_terrain` | Shared town. Spawn (4,54); Town gate to co-op land (13,60); shop doormats (43,57), (20,89), (95,51); blacksmith (94,82); carpenter (10,86); community board (42,57); trader (45,71). The Mine is not reached from here. |
| `Woods.tmx` | no | `Back` re-authored onto `own_terrain` ids; `BackEdge` added | 60 x 32 | `Paths`, `untitled tile sheet`, `v16_Outdoors2`, `own_terrain` | Shared secret woods west of the Forest. Spawn (58,15); large stump sites (code); node scatter (code). |
| `Mines/1.tmx` | yes | none | 20 x 20 | `mine` | Mine floor 1 (private, one per player, ADR-026). Arrival (10,11). Ladder under one stone. |
| `Mines/10.tmx` | yes | none | 20 x 16 | `mine` | Mine floor 10 (private, one per player, ADR-026). Arrival (10,9). Ladder under one stone. Elevator stop at the arrival tile. |
| `Mines/11.tmx` | yes | none | 45 x 30 | `mine` | Mine floor 11 (private, one per player, ADR-026). Arrival (20,15). Ladder under one stone. |
| `Mines/12.tmx` | yes | none | 80 x 15 | `mine` | Mine floor 12 (private, one per player, ADR-026). Arrival (37,9). Ladder under one stone. |
| `Mines/13.tmx` | yes | none | 35 x 35 | `mine` | Mine floor 13 (private, one per player, ADR-026). Arrival (17,19). Ladder under one stone. |
| `Mines/14.tmx` | yes | none | 50 x 40 | `mine` | Mine floor 14 (private, one per player, ADR-026). Arrival (23,21). Ladder under one stone. |
| `Mines/15.tmx` | yes | none | 40 x 35 | `mine` | Mine floor 15 (private, one per player, ADR-026). Arrival (19,19). Ladder under one stone. Elevator stop at the arrival tile. |
| `Mines/16.tmx` | yes | none | 50 x 30 | `mine` | Mine floor 16 (private, one per player, ADR-026). Arrival (25,14). Ladder under one stone. |
| `Mines/17.tmx` | yes | none | 40 x 50 | `mine` | Mine floor 17 (private, one per player, ADR-026). Arrival (15,26). Ladder under one stone. |
| `Mines/18.tmx` | yes | none | 50 x 40 | `mine` | Mine floor 18 (private, one per player, ADR-026). Arrival (20,23). Ladder under one stone. |
| `Mines/19.tmx` | yes | none | 50 x 50 | `mine` | Mine floor 19 (private, one per player, ADR-026). Arrival (25,27). Ladder under one stone. |
| `Mines/2.tmx` | yes | none | 35 x 35 | `mine` | Mine floor 2 (private, one per player, ADR-026). Arrival (23,18). Ladder under one stone. |
| `Mines/20.tmx` | no | `Back` re-authored onto `own_terrain` ids; `BackEdge` added | 50 x 22 | `mine`, `own_terrain` | Mine floor 20 (private, one per player, ADR-026). Arrival (13,13). Ladder under one stone. Elevator stop at the arrival tile. |
| `Mines/21.tmx` | yes | none | 30 x 25 | `mine` | Mine floor 21 (private, one per player, ADR-026). Arrival (13,17). Ladder under one stone. |
| `Mines/22.tmx` | yes | none | 30 x 45 | `mine` | Mine floor 22 (private, one per player, ADR-026). Arrival (21,16). Ladder under one stone. |
| `Mines/23.tmx` | yes | none | 40 x 40 | `mine` | Mine floor 23 (private, one per player, ADR-026). Arrival (20,28). Ladder under one stone. |
| `Mines/24.tmx` | yes | none | 55 x 40 | `mine` | Mine floor 24 (private, one per player, ADR-026). Arrival (33,21). Ladder under one stone. |
| `Mines/25.tmx` | yes | none | 45 x 25 | `mine` | Mine floor 25 (private, one per player, ADR-026). Arrival (25,16). Ladder under one stone. Elevator stop at the arrival tile. |
| `Mines/26.tmx` | yes | none | 40 x 35 | `mine` | Mine floor 26 (private, one per player, ADR-026). Arrival (15,16). Ladder under one stone. |
| `Mines/27.tmx` | yes | none | 55 x 25 | `mine` | Mine floor 27 (private, one per player, ADR-026). Arrival (26,14). Ladder under one stone. |
| `Mines/28.tmx` | yes | none | 55 x 50 | `mine` | Mine floor 28 (private, one per player, ADR-026). Arrival (26,27). Ladder under one stone. |
| `Mines/29.tmx` | yes | none | 55 x 50 | `mine` | Mine floor 29 (private, one per player, ADR-026). Arrival (27,38). Ladder under one stone. |
| `Mines/3.tmx` | yes | none | 35 x 35 | `mine` | Mine floor 3 (private, one per player, ADR-026). Arrival (19,20). Ladder under one stone. |
| `Mines/31.tmx` | yes | none | 35 x 30 | `mine` | Mine floor 31 (private, one per player, ADR-026). Arrival (15,21). Ladder under one stone. |
| `Mines/32.tmx` | yes | none | 40 x 40 | `mine` | Mine floor 32 (private, one per player, ADR-026). Arrival (22,33). Ladder under one stone. |
| `Mines/33.tmx` | yes | none | 30 x 30 | `mine` | Mine floor 33 (private, one per player, ADR-026). Arrival (14,16). Ladder under one stone. |
| `Mines/34.tmx` | yes | none | 45 x 30 | `mine` | Mine floor 34 (private, one per player, ADR-026). Arrival (21,15). Ladder under one stone. |
| `Mines/35.tmx` | yes | none | 35 x 20 | `mine` | Mine floor 35 (private, one per player, ADR-026). Arrival (18,8). Ladder under one stone. Elevator stop at the arrival tile. |
| `Mines/36.tmx` | yes | none | 40 x 35 | `mine` | Mine floor 36 (private, one per player, ADR-026). Arrival (18,19). Ladder under one stone. |
| `Mines/37.tmx` | yes | none | 50 x 25 | `mine` | Mine floor 37 (private, one per player, ADR-026). Arrival (25,14). Ladder under one stone. |
| `Mines/38.tmx` | yes | none | 40 x 30 | `mine` | Mine floor 38 (private, one per player, ADR-026). Arrival (18,17). Ladder under one stone. |
| `Mines/39.tmx` | yes | none | 20 x 60 | `mine` | Mine floor 39 (private, one per player, ADR-026). Arrival (5,31). Ladder under one stone. |
| `Mines/4.tmx` | yes | none | 35 x 35 | `mine` | Mine floor 4 (private, one per player, ADR-026). Arrival (16,18). Ladder under one stone. |
| `Mines/40.tmx` | yes | none | 40 x 31 | `mine` | Mine floor 40 (private, one per player, ADR-026). Arrival (20,18). Elevator stop at the arrival tile. |
| `Mines/41.tmx` | yes | none | 40 x 40 | `mine` | Mine floor 30 (private, one per player, ADR-026). Arrival (19,21). Ladder under one stone. Elevator stop at the arrival tile. |
| `Mines/5.tmx` | yes | none | 35 x 35 | `mine` | Mine floor 5 (private, one per player, ADR-026). Arrival (19,19). Ladder under one stone. Elevator stop at the arrival tile. |
| `Mines/6.tmx` | yes | none | 35 x 45 | `mine` | Mine floor 6 (private, one per player, ADR-026). Arrival (21,23). Ladder under one stone. |
| `Mines/7.tmx` | yes | none | 45 x 35 | `mine` | Mine floor 7 (private, one per player, ADR-026). Arrival (27,25). Ladder under one stone. |
| `Mines/8.tmx` | yes | none | 45 x 35 | `mine` | Mine floor 8 (private, one per player, ADR-026). Arrival (28,11). Ladder under one stone. |
| `Mines/9.tmx` | yes | none | 45 x 35 | `mine` | Mine floor 9 (private, one per player, ADR-026). Arrival (22,20). Ladder under one stone. |

### Dependencies: where code and data depend on a map's coordinates or contents

Every item below must keep working after a replacement, or its code and tests change in the same PR (D2).

**Catalogue and warps**
- `gameplay/Nosum.Gameplay/Maps/GameMaps.cs:27-70` ids; `:79-96` shop and counter tiles in Town (seed `(43,57)`, clothing `(20,89)`, furniture `(95,51)`,
  board `(42,57)`, carpenter `(10,86)`, blacksmith `(94,82)` via `MineWorld.BlacksmithCounter`, trader `(45,71)`); `:98-153` the 13 maps other than the mine floors (the cave included) with spawn,
  extra warps and drop lists (`dropFileWarps` at `:115`, `:128`, `:138`, `:142`); `:203-227` the 40 mine floors.
- `gameplay/Nosum.Gameplay/Maps/MapSpec.cs:43-107` spawn tile per map; `:256-262` `ResolveWarps` (the file's `Warp` property minus dropped exits, plus extra warps).
- `gameplay/Nosum.Gameplay/Maps/WarpParser.cs:13` reads the `Warp` property text of the `.tmx` (the exit list is data inside the file).
- `gameplay/Nosum.Gameplay/Mines/MineWorld.cs:12-33`: Mountain gate `(54,4)`, arrival `(54,5)`, cave arrival `(18,11)`, cave exit `(18,13)`, shaft `(23,9)`,
  shaft front `(23,10)`, cave elevator `(17,3)`, blacksmith counter `(94,82)`.
- `gameplay/Nosum.Gameplay/Maps/OutdoorWorld.cs:16-28` the bridge `(58..61,13)` and its banks; `:31` the Beach gate; `:34-53` forage areas, node scatter and
  the Mountain bridges `(46..49,7)`; `:67` Woods stump sites; `:78` Woods node scatter. These are code, not map data.
- `gameplay/Nosum.Gameplay/Coops/CoopLand.cs:12-21`: Town gate `(13,60)`, Town arrival `(12,60)`, co-op land arrival `(46,22)`, co-op land gate `(46,25)`.
- `gameplay/Nosum.Gameplay/Coops/CoopUpgrades.cs:95-101` the co-op land unlock rectangles `(19,8,42,29)`, `(19,37,42,24)`, `(61,0,19,65)`, `(0,0,80,65)`, read
  by `:188-196` (`IsLandUnlocked`) and by the server (`server/Nosum.Infrastructure/Farm/FarmService.cs:269`, `server/Nosum.Infrastructure/Placeables/PlaceableService.cs:529`)
  and the client (`wasm/Nosum.Wasm/Game/LockedLandDecorator.cs:64,71`). The co-op land map is 80 x 65 because of the last rectangle; changing its size changes code.

**Farm**
- `gameplay/Nosum.Gameplay/Farm/FarmProps.cs:25` farmhouse door front `(64,16)` (the Farm spawn); `:28-41` the code-drawn farmhouse `(59,10)` 9 x 6, greenhouse
  `(25,10)` 7 x 6 and shipping bin `(71,14)` 2 x 1; `:50` the walk grid is the map plus these footprints.
- `gameplay/Nosum.Gameplay/Farm/TillGrid.cs:42-55`: a tile is tillable when its `Back` tile has the `Diggable` trait and it is not blocked. Plots are player choices
  on tillable ground; no plot list exists in the code.
- `gameplay/Nosum.Gameplay/Movement/WalkGrid.cs:78`: walls are the `Buildings` layer's tiles, water and impassable ground the `Back` layer's, both by tile traits.

**Nodes and forage**
- `gameplay/Nosum.Gameplay/Nodes/NodeSite.cs:40-48` the `Paths` layer marks node sites and forage (indices 24 to 26); `:93` `Read`, `:138` `ReadForage`.
- `gameplay/Nosum.Gameplay/Nodes/NodePlacement.cs` and `NodeScatter.cs`: scattered nodes use the walk grid and the scatter lists in `OutdoorWorld`.

**Mines**
- `gameplay/Nosum.Gameplay/Mines/MineLayouts.cs:21-63` the 40 floor plans: file number and arrival tile per floor (the table is code).
- `gameplay/Nosum.Gameplay/Mines/MineRules.cs:17` 40 floors (`MaxFloor`), `:20` elevator every 5th floor, `:23` copper up to floor 39, `:26` iron from floor 20,
  `:39` `HasElevator`, `:54` `HasLadder` (floors 1 to 39).
- `gameplay/Nosum.Gameplay/Mines/MineFloorPlanner.cs:33` `Build` (candidates from the walk grid, reserved tiles, arrival), `:56-82` stone, copper and iron
  counts, `:85-104` the ladder under one of the stones farthest from the arrival.
- `server/Nosum.Infrastructure/Mines/MineService.cs:97` `CanRide`; `:142` the elevator comes from `MapSpec.Elevator`.
- `server/Nosum.Handlers/Mine/MineHandlers.cs:40-48` the ride request carries a floor (`contracts/Nosum.Contracts/Mine/C2S/RideElevator.cs:14-17`).

**Interiors**
- `gameplay/Nosum.Gameplay/Buildings/BuildingCatalog.cs:177-197`: the coop and barn interiors' door tiles `(2,9)` and `(11,14)`, animal areas `2..9 x 4..7` and
  `6..13 x 4..10`, and the bench row, all written as comments about `Coop.tmx` and `Barn.tmx`. Spawns `(2,8)` and `(11,13)` come from `GameMaps.cs:151-152`.
- The farmhouse spawn `(3,11)` is `GameMaps.cs:101`; the door from the Farm is `(64,15)` (`GameMaps.cs:100`).

**Contracts and server spawn**
- `contracts/Nosum.Contracts/Player/S2C/JoinMapResult.cs:25-31` carries a spawn position and a map id, not map data. `contracts/Nosum.Contracts/Mine/C2S/RideElevator.cs`
  carries a floor number. Neither changes if the coordinates and floor count stay.
- `server/Nosum.Infrastructure/Maps/TmxMapCatalog.cs:36-45` loads every map once; `:66-82` reserves the spawn and warp tiles from placement.
- `server/Nosum.Infrastructure/Players/PlayerService.cs:376` warps back to the Town spawn; `server/Nosum.Domain/Accounts/PlayerState.cs:15` a new player starts on `Farm`.
- `wasm/Nosum.Wasm/Game/MapLoader.cs:21,138` loads the file and builds the walk grid, exits and tile sets from `MapSpec`; `wasm/Nosum.Wasm/Game/GameLoop.cs:232`
  falls back to the spawn tile; `wasm/Nosum.Wasm/Game/MapScene.cs:188-191` finds shops and the elevator by `MapSpec`.
- `wasm/Nosum.Wasm/Game/BridgeDecorator.cs:18` relies on the Beach's own tile sheet: the bridge end plank is gid 141, local tile 76.
- `server/Nosum.Host/Nosum.Host.csproj:21-26` and `wasm/Nosum.Wasm/Nosum.Wasm.csproj:20-26` link each map and its sheets by path. A new file with the same name needs no link change.

**Tests that pin tiles or contents** (these change with the layouts; each PR states which pins it changes)
- `tests/Nosum.UnitTests/Maps/GameMapsTests.cs:98` the Farm's `Warp` property text; `:143-151` farmhouse door `(64,15)` walkable, walls `(63,15)` and `(64,12)`;
  `:70-90` every exit lands on walkable ground; `:156` every map and sheet is linked.
- `tests/Nosum.UnitTests/Maps/MineMapsTests.cs:27-38` Mountain gate `(54,4)`, arrival `(54,5)`; `:41-60` cave spawn, shaft `(23,9)` open, elevator `(17,3)` blocked;
  `:74-100` one way up per floor and an elevator every fifth floor.
- `tests/Nosum.UnitTests/Maps/OutdoorMapsTests.cs:119-135` Mountain to Mine warp `(54,4)` to `(18,11)` and the dropped exits `(67,-1)`, `(94,110)`;
  `:138-150` bridge tiles `y = 13` blocked until the unlock; `:188` reads `Beach.tmx`.
- `tests/Nosum.UnitTests/Maps/CoopLandMapTests.cs:49-76` Town gate `(13,60)`, arrival `(12,60)`, co-op land arrival `(46,22)` and gate `(46,25)`.
- `tests/Nosum.UnitTests/Gameplay/MineRulesTests.cs:37-49` the floor count and elevator constants; `:64` ore and ladder by floor; `:120` arrival walkable and in a useful
  cave; `:130` one hidden ladder on reachable stone; `:170` the same seed gives the same floor; `:186` the ladder is among the farthest stones; `:201` ore scales with depth.
- `tests/Nosum.UnitTests/Gameplay/NodeSitesTests.cs:62-66` pinned node-site counts per map (Farm, Forest, Backwoods, Town, BusStop); `:95`, `:108`, `:115`.
- `tests/Nosum.UnitTests/Gameplay/ForageTests.cs:123-127` pinned forage counts; `:145` the farmhouse has no forage.
- `tests/Nosum.UnitTests/Gameplay/TillGridTests.cs:65` reads `Farm.tmx`.
- `tests/Nosum.UnitTests/Gameplay/ShopRulesTests.cs:138` only Town has shops.
- `tests/Nosum.IntegrationTests/Server/MapChangeTests.cs:296` farmhouse door `(64,15)` and spawn `(64,16)`.
- `tests/Nosum.IntegrationTests/Server/CoopUpgradeTests.cs:155-157,188` co-op land tiles `(25,45)`, `(65,30)`, `(25,24)`; `CoopLandTests.cs:100,300-301` the co-op
  land tiles `(25,24)` and furnace and chest rows at `x = 40, 42`.
- `tests/Nosum.IntegrationTests/Server/ChunkInterestTests.cs:175,193` and `ChunkWorldTests.cs:94` plots in farm chunks `(4,0)`, `(4,1)`, `(4,2)`.
- `tests/Nosum.IntegrationTests/Server/VisitWireTests.cs:200` visitor placement around the Farm spawn.
- `tests/Nosum.UnitTests/Tools/MapReauthorTests.cs:24-43,87,111-112,227-239,364` the re-author tool reads the Stardew placeholder content (they return early without `NOSUM_PLACEHOLDER_ROOT`).

### Gaps the plan must not miss

- Sheets. The painted sheets in `art/own/Maps/*.png` have own pixels, but their floor plans were read from the Stardew sheets (`art/own/SOURCES.md`,
  ADR-040 phase 3). They are not byte-identical, so the hash gate does not see them; the derivation audit (D5) covers them, and whether they are kept is the owner's
  decision (ADR-040 D4).

## Decision

- **D1 — Scope.** All 53 `.tmx` files of `art/own/Maps` are replaced. A replacement keeps the file name (the code, the links and the mine table name files),
  the map id and the gameplay points of D2. "Byte-identical" is not the criterion: a replacement must have no layer that derives from its Stardew map
  (D5 defines the audit). The 10 maps that are not byte-identical are in scope in full, not only their `Back` layer.

- **D2 — The gameplay contract.** A replacement keeps, unless the same PR changes the code and the tests named above:
  1. the spawn tile, the exits (the `Warp` property and the extra and dropped warps), and the counter tiles of D1's dependency list;
  2. the walk and till traits of every tile that the code reads (`Water`, `Impassable`, `Diggable`, `Passable`): a replacement tile carries the same traits from the own tileset;
  3. the `Paths` node-site and forage indices, and the counts pinned by `NodeSitesTests` and `ForageTests` (a count may change only with the test update in the same PR);
  4. the mine contract: 40 floors; a ladder under one stone on floors 1 to 39; an elevator point on floors 5, 10, ... 40 at the arrival tile; stones, copper (floors 1 to 39)
     and iron (floors 20 to 40) from `MineFloorPlanner`, reachable from the arrival;
  5. the co-op land size (80 x 65) and the unlock rectangles of `CoopUpgrades.cs:95-101`; the Farm's three building footprints (`FarmProps.cs:37-41`).
  A named point may move only when code and tests move with it. Every such move is listed in the PR that makes it.

- **D3 — Mines: one seeded generator.** `tools/Nosum.MapGen` (new, in `tools/`, never referenced by the game) generates the 40 floor files and the
  cave's layout from a fixed seed per floor. The floor's arrival tile comes from `MineLayouts.For(floor)` (so the table and the file cannot disagree), its
  walls, stones and ore are drawn with the own terrain set (`art/own/Maps/own_terrain.png`, whose tiles carry the `Water`, `Impassable`, `Diggable`, `Passable` traits),
  and the generator guarantees the D2 contract by construction and then checks it. The seed is recorded per file in the file's comment and in
  `art/own/reports/Mines.md`; the output is byte-stable, so `Nosum.MapGen mine --check` can fail a stale file, as `Nosum.MapReauthor reauthor --check` does today.
  - Code changes: none expected. If a generated floor needs a new arrival tile, `MineLayouts.cs` changes in the same PR, and `MineMapsTests` and `MineRulesTests` update.
  - Data changes: the 40 files and `Mine.tmx` (the cave is hand-authored, D4).
  - Test strategy: the existing `MineRulesTests` (every floor: arrival walkable and in a useful cave, one hidden ladder reachable, ore by depth, same seed same plan)
    and `MineMapsTests` (way up, elevator, cave exits) run unchanged on the generated files; a new test regenerates each floor in memory and compares it with the
    committed file; a new test checks the floor's walk traits against the own tileset (not the Stardew one).

- **D4 — Interiors and outdoor maps: hand-authored layout sources.** Each layout is written as a text grid of its tiles, in `art/own/source/layouts/<Map>.txt`
  (one character per tile, a legend in the file), and compiled into the `.tmx` by `tools/Nosum.MapGen layout`. The `.tmx` is generated, never edited (AGENTS.md:
  generated maps are regenerated with the tool). The grids are drawn by the owner or the artist; the generator fills the own sheets' gids and the traits from the
  legend. The rule of D2 applies to every map here.
  - **Interiors** (FarmHouse 12 x 12, Coop 12 x 10, Barn 18 x 15). Keep the spawn and door tiles, the animal areas and the bench row (`BuildingCatalog.cs:194-197`).
    Code changes: none if the layouts keep those tiles. Data changes: three files. Test strategy: the spawn reaches the door through walkable tiles;
    every animal area tile is walkable; `GameMapsTests` (`:70-90`) and `BuildingRulesTests` pass unchanged.
  - **Shops.** No shop has its own map: the shops and counters are in `Town.tmx` (`GameMaps.cs:79-96`). The shop counters are part of the Town group.
  - **Outdoor maps** (Farm, Farm_Foraging, Town, BusStop, Forest, Backwoods, Mountain, Beach, Woods). Each keeps the spawn, the exits, the counters and gates,
    the bridge tiles, the node sites and forage from the `Paths` layer, the code-scattered sites and the co-op land size. Code changes: none when D2 holds.
    Data changes: nine files. Test changes: the pinned counts in `NodeSitesTests` and `ForageTests`, which the PR states.
  - **Farm** keeps its three building footprints free of its own drawing.
    [Assumption: the Stardew Farm draws no house, greenhouse or bin in those footprints; if it does, the replacement must not draw them and the test below decides.]
    Test strategy: `GameMapsTests` (door, walls, exits), `OutdoorMapsTests` (every exit pair both ways, `:41-57`; spawns `:88-100`; new-map warps `:102-117`),
    `CoopLandMapTests`, `NodeSitesTests`, `ForageTests`, plus one new test: every tile under `FarmProps.Placements` is free of the own layers' blocking tiles.

- **D5 — The Stardew gate.** Three layers, each a separate step:
  1. **Hash scan (implemented in this PR).** `docs/art/stardew-fingerprints.sha256` lists the SHA-256 of every file of `assets/` at `8971108`: 2,805 distinct
    hashes, hashes only, no path and no content. The header says how to regenerate it. `Nosum.AssetInventory --stardew-scan` hashes every file of the repository
    except `.git`, `bin`, `obj`, `artifacts`, `node_modules` and `TestResults`, skips empty files, and lists each file whose hash is listed (exit 1 when any
    matches, 2 when the list is missing or malformed). It is opt-in: no script or CI job calls it yet.
  2. **Allow-list (implemented in this PR).** `tests/Nosum.UnitTests/Tools/StardewScanTests.cs` records the 43 current hits (the byte-identical maps) as
    `s_knownHits`. The repository test asserts that the scan's hits equal that list, so a new hit fails it and an entry that stops matching fails it too.
    Each replacement PR removes its entries (the 39 mine floors, `Mine`, `Barn`, `Coop`, `FarmHouse`, in phases P2 and P3).
  3. **Release gate (phase P5).** The allow-list is deleted in the final phase, and `Nosum.AssetInventory --release` runs the scan too; a hit fails the release
    (`deploy/check-placeholder-art.sh public` exits 1). The release gate must stay green after P5 with no allow-list.
  - **Limits, stated.** The hash scan matches exact bytes only: a re-saved, cropped or recoloured copy is not found. It also cannot see a map that derives from
    Stardew's layout without being a copy. Those are the derivation audit's job.
  - **Derivation audit (phase P6, one-time, owner-run).** `Nosum.MapGen audit` (with `NOSUM_PLACEHOLDER_ROOT`, see `tools/README.md`) compares each own layer,
    cell by cell, with the Stardew layer of the same name and writes `docs/art/derivation.md` with one line per layer: the share of cells with the same
    tile index at the same position, the share of blocking cells that match, and whether the layer is identical. The owner sets the threshold in the ADR
    before the audit runs; the audit's default is that no layer is identical and no layer's blocking cells match in more than the threshold. [Assumption: the
    threshold is the owner's; it is not set here.] The audit also reads the painted sheets (`art/own/Maps/*.png`, sheet by sheet against the Stardew sheet).

- **D6 — Phasing: independent PRs, in this order.** Each PR is one concern and small enough for one session.
  - **P0 — this PR.** This ADR; `docs/art/stardew-fingerprints.sha256`; `--stardew-scan`; `StardewScanTests` with the 43-entry allow-list; a line in `tools/README.md`.
    No map or game code changes. Gates: the 8 solutions build with 0 warnings, `tests/Nosum.Tests.slnx` passes, `deploy/check-placeholder-art.sh public` exits 0.
  - **P1 — tool skeleton.** `tools/Nosum.MapGen` with the layout compiler, the mine generator in `--check` mode, and the audit mode; unit tests for each. No map changes.
  - **P2 — interiors.** FarmHouse, Coop and Barn layouts from `art/own/source/layouts/`; the scan allow-list loses 3 entries.
  - **P3 — mines and the cave.** The 40 floor files and `Mine.tmx`; the allow-list loses 40 entries; any arrival-tile change is in `MineLayouts.cs`.
  - **P4a — Farm and co-op land.** `Farm.tmx`, `Farm_Foraging.tmx`, with the placement test (D4).
  - **P4b — Town, BusStop, Backwoods.** Town with its counters and gates.
  - **P4c — Forest, Mountain, Beach, Woods.** The bridge and the stump and node sites; `BridgeDecorator.cs:18` checked by a test.
  - **P5 — the gate on.** `--release` runs the scan without an allow-list; the test list is deleted; `deploy/check-placeholder-art.sh public` runs the same check.
  - **P6 — derivation audit.** The owner sets the threshold; the audit runs in the private repository, and `docs/art/derivation.md` records the result.
  - **Definition of done** (all four): no file in the repository matches a Stardew fingerprint (P5 green); no own map layer derives from a Stardew map (P6 audit within the
    owner's threshold, including the sheets); the full gate is green (the 8 solutions build with 0 warnings, `tests/Nosum.Tests.slnx` passes twice,
    `deploy/check-placeholder-art.sh public` exits 0); and this ADR is `Accepted` by the owner.

- **D7 — History.** After P6 the coordinator publishes a new repository with the Stardew blobs stripped from history. The tools that read the Stardew content
  (`write`, `--check`, `--write-fingerprints`, `reauthor`, `audit`) need `NOSUM_PLACEHOLDER_ROOT`, so they stop working in the new repository and their tests return
  early there. The release gate (`--release`, `--stardew-scan`) needs no Stardew content and keeps working. The coordinator decides the timing.

## Alternatives considered

- **Keep the layouts and rely on the placeholder gate** — the gate reads only the 95 listed paths under `assets/` and passes with 43 Stardew maps in the tree (measured).
- **Only a byte-level scan, no layout replacement** — misses the 10 re-authored maps, whose other layers are Stardew's; and the coordinator's decision is a new layout for every map.
- **Redraw each map by hand in Tiled** — no record of how it was made, and nothing checks the D2 contract; the layout source with a compiler keeps the review in text.
- **Random generation for every map** — the towns, the farm and the interiors have named points that the code and the players rely on (D2), and a random town has no design.
- **Mirror or rotate Stardew's layouts** — still derived; the audit would find it.
- **Move the named points into the data and let the layouts float** — code and data move together in D2; a separate data model is a larger change that this ADR does not need.

## Consequences

- **Scope is fixed and measured:** 53 files, 43 byte-identical, 10 re-authored; 2,805 Stardew fingerprints; 43 hits today, all maps.
- **The hash gate is necessary, not sufficient.** The 10 re-authored maps are caught only by the derivation audit, which needs the owner's threshold.
- **Test pins change.** Node-site and forage counts, the co-op land tiles, and the mine floor plans are pinned in tests; each phase lists the pins it changes.
- **The mine generator is deterministic, so a floor can be reproduced from its seed** and checked in CI (`mine --check`) like the other generated files.
- **Tools that read the Stardew content stop after D7** (history rewrite); the release gate is unaffected.
- **Open for the owner:** the derivation threshold (D5); whether the painted sheets are replaced by PixelLab or hand art (ADR-040 D4); the PixelLab terms (ADR-040 D4).

## Revisit when

The owner sets the derivation threshold; a replacement shows the D2 contract is too strict or too loose; the painted sheets are replaced; the coordinator publishes
the repository without history (D7).
