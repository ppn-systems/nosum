# ADR-028 — Farm buildings and animals

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M3. The carpenter's page amended by [ADR-043](ADR-043-npc-merchants-and-the-menu-split.md): it opens from the carpenter's dialog box, as a dialog of its own, not a menu tab. Builds on ADR-015 (instances,
placeables), ADR-024 (outdoor placement), ADR-023 (energy), ADR-018 (days).

## Context

Crops alone make a thin farm. Coops and barns add daily care and steady produce. The early content has the
buildings (`assets/sprites/buildings/coop.png`, `barn.png`), interiors (`art/own/Maps/Coop.tmx`, `Barn.tmx`) and animals
(`assets/sprites/animals/chicken.png`, `cow.png`).

## Decision

- **D1 — Buildings are large placeables** on the player's farm or co-op land only, built at a carpenter counter
  (Town or Mountain, ADR-026 style counter) for coins + wood + stone: coop (4 000, wood 300, stone 100), barn (6 000,
  wood 350, stone 150). Construction completes after two game days. Footprint, door tile and interior map are data.
- **D2 — Interiors are private instances** (`coop@<building id>`), entered through the building's door; access follows
  the owner of the land (player, or co-op members on co-op land; visitors per ADR-019).
- **D3 — Animals** are bought at the same counter (chicken 800, cow 1 500), named by the player (nickname rules of
  ADR-009 apply), live in a building with capacity (coop 4, barn 4). Each game day an animal that was fed (hay in the
  feeding bench, auto-filled from a silo later; for now the player puts fiber/hay in) produces: chicken → egg, cow →
  milk (milking with a pail tool). Petting once a day raises friendship (0–1000), which raises the chance of large
  produce.
- **D4 — Animals wander** inside their building on a server-chosen path updated every few seconds (cheap, per chunk
  subscribers only); outside grazing is out of scope.
- **D5 — Persistence.** Tables for buildings and animals; generated migrations. Daily production happens in the day
  rollover hook, lazily evaluated on read where possible.
- **D6 — Wire.** A new band slice `0x3100–0x31FF` (Farm band) for build/animal intents and pushes; animals use the
  chunk publisher.

## Consequences

New item kinds: animal products (egg, milk; ids 900–949) and tools (milk pail). Shipping prices are data.

## Implementation notes

How the decisions above were built, and where the build fills in the text.

- **D1 — a building is a blueprint item, placed like any large piece.** The carpenter does not place anything: `CommissionBuilding` takes coins, wood and stone and hands over a blueprint (`coop_blueprint` 610, `barn_blueprint` 611, `ItemKind.Placeable`, stack 1) in one
  `ITransferService` transaction. The blueprint is set down with the usual place intent on the player's farm or on co-op land (members only; the farmhouse and every public map refuse it: `PlaceStatus.NotPlaceable`), so footprint validation, reach, tilled soil and nodes, the ghost and the
  client mirror are the ones of ADR-024. Placing adds a `farm_buildings` row in the same transaction (`IPlacedBehaviour.JoinPlace`); the construction clock starts at the placement (`completes_at_ms` = placement time + two game days) and is read from that stored number, never ticked. A land holds at most 8
  buildings. A building cannot be moved or picked up (`PlaceStatus.Fixed`, appended). Coop: 4 000 coins, 300 wood, 100 stone, footprint 6 × 3 tiles (sprite 96 × 112 from `assets/sprites/buildings/coop.png`; the strip below the body is door animation and unused), door at the second tile of the bottom row. Barn: 6 000, 350, 150, 7 × 3 (112 × 112).
  The door tile is inside the footprint but opened for walking (`PlaceableDefinition.Door`), and the tile in front of a door must stay free: `PlacedSet.Check` refuses a blocking piece there, and a building whose own front tile is blocked.
- **D2 — interiors.** `MapSpec.InteriorOf` marks `coop` (`Coop.tmx`, 12 × 10) and `barn` (`Barn.tmx`, 18 × 15) as private maps whose instance owner is the **building's id**, not a player (`coop@<id>`). Walking onto the door tile warps the player in (`PlayerService`, from the movement path: `IBuildingDirectory.TryFindDoor` reads an immutable per-land list,
  nothing allocates); the interior's exit tile leads back to the tile in front of the door, found from the building, not from the map file's `Warp`. `BuildingAccessPolicy` (a decorator of `VisitAccess`) admits whoever may enter the land, once the building is finished. A player who left the game inside restarts on the farm, because the saved place of an interior is not the player's.
  A visitor swept out of a place, or a co-op member who left, is sent out of the interiors too; a visitor whose permission lapsed while inside is sent to Town when they walk out.
- **D3 — animals.** Chicken 800 coins (coop), cow 1 500 (barn), capacity 4; the name goes through `NicknameRules` (ADR-009) with the profanity filter. Feeding: hay (508, 30 coins at the carpenter) or fiber put into the bench (`FeedAnimals`, at most 24 rations, one per animal per night). **Feeding is settled lazily**:
  the first time anybody touches a building on a new game day, `AnimalRules.Settle` runs one night per day since the last settlement (each animal eats one ration if the bench has one) and the animals that ate get `produce_day = today` ("fed yesterday, produce today"); nothing ticks and an unvisited building costs nothing. Produce does not pile up: it is there on the day it was made and is lost after.
  Collecting is `CollectProduce`: a chicken gives an egg, a cow milk only with the **milk pail** (a tool, `ToolKind.MilkPail`, item 6, sold at the carpenter for 300; not part of the starter kit, which would shift every slot) in the named slot. It re-reads the animal inside the inventory transaction under the building's lock, so the produce is handed over exactly once however many players ask, and into the inventory (never
  the ground; a full bag refuses). Petting (`PetAnimal`) adds 40 friendship once a day (0-1 000). The large kind (large egg 901, large milk 903) is decided by `GameRandom` seeded from the animal and the day only, so asking again cannot re-roll it, with a chance of `5 + friendship / 20` percent; more friendship can only turn a small produce large. Eggs (900) and milk (902) are edible and ship for 50 / 125 coins (large 95 / 190).
  Only the land's owner, or the members of a co-op on its land (audited `coop.feed` and `coop.collect`), may feed, pet and collect; a visitor may look.
- **D4 — wandering.** `AnimalWanderService` keeps, per animal, one straight walk (start, end, start time, duration) and the time it decides again. A tick (once a second, from `Main`) visits only the interiors with a player inside; an animal that is due (two to five seconds after it arrived) picks a random tile of its rectangle (coop 2-9 × 4-7, barn 6-13 × 4-10, from `ProduceArea` of the map files),
  not the bench, walkable in a straight line from where it stands, at most six tiles away, and the walks are published per chunk (`IChunkPublisher`, ADR-022; a barn is two chunks wide) as `AnimalsMoved`. The client takes positions from the start time; a player coming in reads the room (`GetAnimals`) with every animal's position and walk.
- **D5 — persistence.** `farm_buildings` (Guid id, FK to `placed_objects` with cascade, unique; land map and owner; kind; `completes_at_ms`; rations; `settled_day`) and `farm_animals` (Guid id, FK to the building; kind, name, friendship, `petted_day`, `produce_day`, `collected_day`, `bought_at`). One migration, `FarmBuildingsAndAnimals`. Days are `GameCalendar.DayNumberAt`. The day-ended hook is not used: lazy settlement covers it.
- **D6 — wire.** `Nosum.Contracts.Building`, opcodes `0x3100-0x3106` (C2S) and `0x3140-0x3145` (S2C), one `BuildingStatus` for every refusal; `PlaceStatus.Fixed = 19` is appended. Nothing is renumbered and `NosumProtocol.Version` is not changed. The list of a land's buildings (`BuildingsSnapshot`) is also pushed to everybody on the land when a building is placed (instance-wide: it is small and replaces the client's list).
- **Client.** The carpenter is the Town counter at the blue house in the south-west of the map (door tile (10, 85), doormat (10, 86)) and its page is the shop page with the building and animal sections under the supplies. Buildings under construction are drawn dull. Front and back of a building are one depth-sorted sprite anchored at the bottom of its footprint, so the farmer is behind the roof and in front of the door without
  the extra front layer the farmhouse needs. The early art (ADR-007) was replaced by own pictures in `assets/sprites/buildings/` and `assets/sprites/animals/`.
