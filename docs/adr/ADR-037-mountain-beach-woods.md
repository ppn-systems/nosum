# ADR-037 — Mountain, Beach and the Secret Woods

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M3 ("More public maps"). Builds on ADR-007
(placeholder content), ADR-012 (maps and warps), ADR-021 (resource nodes and regrowth), ADR-022 (chunks), ADR-025
(forage), ADR-026 (the mine cave), ADR-027 (weather), ADR-030 (community goals and unlocks). Amends ADR-026 D3: the
cave is entered from the Mountain, not from Town.

## Context

Town, Forest and Backwoods are the only public outdoor maps. The placeholder files for the Mountain, the Beach and the
Secret Woods are already under `assets/Maps/` and name their exits to the maps the world hosts. ADR-026 sent the mine
cave to Town's north road because the Mountain did not exist, and ADR-030 left the Beach's bridge as the first unlock
target without a map to use it on.

## Decision

- **D1 — Three shared maps, wired like the original.** `Mountain.tmx`, `Beach.tmx` and `Woods.tmx` become shared public
  outdoor maps (`GameMaps.Mountain`, `Beach`, `Woods`; not private, not farmable, not placeable, not `indoors`, so
  ADR-027 weather falls on them). Their exits come from the files' own `Warp` property; an exit to a map the world does
  not host (the Tent, the Railroad, the quarry) is dropped by `MapSpec.ResolveWarps`, as before. Where the file and the
  world disagree a `MapSpec` drops the file's exit (`dropFileWarps`) and adds its own. Every exit that can be walked to:

  | From | Source tiles | To | Arrival tile |
  |:--|:--|:--|:--|
  | Town | (79..83, −1), the north road | Mountain | (15, 40) |
  | Mountain | (14..16, 41), south end | Town | (81, 0) |
  | Town | (53..55, 110), south road | Beach | (38, 0) |
  | Beach | (38..40, −1), north end | Town | (54, 108) |
  | Forest | (−1, 6), (−1, 7), west edge | Woods | (59, 15) |
  | Woods | (60, 15), (60, 16), east edge | Forest | (0, 7) |
  | Mountain | (−1, 12..14), west edge | Backwoods | (49, 14) |
  | Backwoods | (50, 10..17), east edge | Mountain | (0, 13) |
  | Mountain | (54, 4), the cave mouth | Mine cave | (18, 11) |
  | Mine cave | (18, 13), the south passage | Mountain | (54, 5) |

  The Town's mine gate of ADR-026 (`MineWorld.TownGateFromX`…) is gone, and the file's exits of `Mine.tmx` at
  (18, 14) and (67, 18) and of the Mountain at (103, 15) (the quarry's second way in) are dropped: one way in, one way
  out. Four exits the files list have a wall for a source tile and can never be used: Town (90, −1) and (98, −1)
  (Town's north edge is open only at x 79–83) and the Mountain's (57, 41) and (85, 41). Two more are dropped because they
  would strand a player: Town (94, 110) and the Beach's (67, −1) lead to and from the Beach's east bank, which is
  reached over the bridge only (D4).

  The Mountain's stream is crossed by a four-tile plank bridge, (46..49, 7), that `WalkGrid` treats as water
  (water of the `Back` layer blocks whatever stands on it); the mine path is behind it, so `MapSpec.OpenTiles` opens
  the planks, as ADR-026 opened the shaft. The quarry in the east of the Mountain, the pocket south of the stream and
  other parts the files seal with water or cliffs stay unreachable; nothing is placed there on purpose.
- **D2 — Resource nodes on every new map, with regrowth.** Nodes come from the `Paths` layer as before (ADR-021 D4a),
  from sites a `MapSpec` adds, and from scatters (`NodeScatter`: N open tiles with open neighbours in a rectangle, picked
  from a fixed seed so the sites never change). All of it is thinned at the first fill and regrown on public maps by the
  day rollover (`NodePlacements`); every change goes through `IChunkPublisher` (ADR-022) as for any node.
  - **Mountain:** the eight trees the file marks, stones, twigs and weeds on the west plateau and the mine path
    (`OutdoorWorld.MountainNodes`).
  - **Beach:** the file marks nothing, so twigs (driftwood) and stones are scattered on the sand west and east of the
    river (`OutdoorWorld.BeachNodes`).
  - **Woods:** no hardwood exists in the item catalogue, so the Woods get **large stumps**: `NodeKind.LargeStump = 11`
    (append-only), axe, 6 hits, 8–12 wood, blocks walking, regrows on public maps like a stone. Six stand at fixed tiles
    (`OutdoorWorld.WoodsStumps`), all placed at the first fill, plus scattered twigs, weeds and stones. When hardwood is
    added the large stump is where it drops.
- **D3 — Shore forage.** A forage spot carries its pool in `NodeSite.Variant` and a `ForageEntry` its `Pool`
  (`ForagePools.Woodland = 0`, `Shore = 1`); `ForageCatalog.Roll` draws only from the pool of the spot. The six shore
  entries are appended after the 14 woodland ones (variant numbers are stored): clam 813, cockle 814 (every season),
  coral 815 (spring, summer, fall), sea urchin 816 (summer, fall), nautilus shell 817 (fall, winter), rainbow shell 818
  (summer). They are `Produce` the bin buys, in the forage id range 800–899. The Beach's spots are the walkable tiles of
  two sand rectangles (`MapSpec.ForageAreas`, `OutdoorWorld.BeachForage`); the Mountain and the Woods use the glyphs of
  their `Paths` layers and the woodland pool.
- **D4 — The Beach bridge.** `CommunityUnlockKeys.BeachBridge` already exists and the spring goal "Bridge" sets it
  (ADR-030), so no new goal is added. The river has a four-tile gap at (58..61, 13) (the file's `BrokenBeachBridge`
  action is on its first tile) and the east bank, everything with x ≥ 62, is reachable over it only. `MapSpec.Gates`
  lists those tiles; `WalkGrid` keeps them blocked in its flags and a `WalkGate` releases them while the unlock is set,
  asking at every test. Every copy of the grid (the node and placement layers clone it) shares the gate, so nothing is
  rebuilt when the unlock arrives.
  - **Server:** `TmxMapCatalog` takes the unlock source (`CommunityUnlocks.IsUnlocked`); a closed gate is a wall to
    movement validation, nodes and spawns alike.
  - **Client:** the walk grid is the same code, so it needs the same answer. The unlocks that change a grid travel as
    one byte (`CommunityUnlockSet`, bit 0 the bridge): appended to `JoinMapResult` (`SerializeOrder` 6) and `MapChanged`
    (5), and pushed as `UnlocksChanged` (`0x4143`, the goal band) to Town and the Beach when a goal completes.
    `PlayerChannel.IsUnlocked` feeds the client grid. Planks are drawn by `BridgeDecorator` once the unlock is known.
    No opcode or existing order changed, so `NosumProtocol.Version` stays.
- **D5 — i18n.** `Friends_Map_mountain`, `_beach` and `_woods` in both `Language` files; six shell items in both
  `Catalogue` files. Nothing visible travels as text.
- **D6 — The carpenter stays in Town.** ADR-028 placed the carpenter's counter there; moving it would be churn without
  gameplay value, so the roadmap line no longer says it moves.

## Alternatives considered

- **Open the bridge tiles in a shared mutable grid when the unlock arrives.** Rejected: the node layer keeps its own
  clone per instance, so a changed base grid would be stale until each clone is rebuilt.
- **Hide the bridge with a node.** Rejected: nodes are per instance and persisted; a permanent server-wide fact belongs
  to the map.
- **Send the unlock set in the board packet only.** Rejected: a player who never opens the board would predict a wall
  where the server lets them walk.
- **A hardwood item and a hardwood node now.** Rejected: it is a new item tier with its own sinks; the large stump is
  the node it will use.

## Consequences

Three more public maps to keep in chunk and presence memory; the Beach is the first map whose grid depends on server
state. `MapSpec` gains `dropFileWarps`, `gates`, `nodeSites`, `scatter` and `forageAreas`. A player who is not in Town
or on the Beach when the bridge is built learns it at their next map change.

## Revisit when

Hardwood exists; the art pass (M6) replaces the placeholder planks and stumps; a second gated tile set is added (the gate
list is the only place to change).

## Implementation notes

- Nodes of the new maps are stored like any public map's (`world_nodes`); no migration. The node kind is a number, `11`.
- The large stump's placeholder art is the oak stump drawn twice, and the planks are the Beach sheet's end plank
  repeated (ADR-007).
- Weak spots: parts of the Mountain's files (the quarry, the south pocket) are sealed and unreachable; the Beach's east
  bank has no exit of its own besides the bridge; players on other maps learn of the bridge at their next map change; the
  Beach's shore spots are every walkable tile of two rectangles, so a few land on grass at the edges.
