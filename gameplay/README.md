# gameplay/

Farm rules shared by the server and the client: plots, crops, items, time-based growth,
prices. Pure, deterministic logic — no I/O, no clock reads (time is passed in), no network,
no rendering. The server runs it to decide; the client runs it to predict and display.

References `engine/Nosum.Core` only. Rules: [`gameplay-rules`](../.claude/skills/gameplay-rules/SKILL.md).

```bash
dotnet build gameplay/Nosum.Gameplay.slnx
```

Implemented: `Movement/WalkGrid` (`FromMap`: `Buildings` tiles block unless `Passable`; water or
`Passable=F` ground on `Back` blocks; `BlockFootprints` adds building footprints) and
`Movement/Walker` (fixed-step walking with a feet footprint, sliding along walls).

World maps (`Maps/`, [ADR-012](../docs/adr/ADR-012-maps-and-warps.md)): `GameMaps` is the catalogue of
hosted maps (`MapSpec`: id, `.tmx` file name, spawn tile, building props, opened doorsteps, extra exits);
`WarpParser` reads the Tiled `Warp` property (`fromX fromY targetMap targetX targetY` groups) and
`MapSpec.ResolveWarps` turns it into exits whose targets are catalogue ids (exits to unhosted maps are
dropped); `WarpRule.TryFind` says when a player standing at a position uses an exit (on the tile; an edge
exit needs walking outwards) and `WarpRule.Arrival` where they appear. `MapSpec.BuildWalkGrid` is the one
grid both sides use. Adding a map: add a `MapSpec` to `GameMaps`, link its `.tmx` and tilesheets in
`wasm/Nosum.Wasm/Nosum.Wasm.csproj` and `server/Nosum.Host/Nosum.Host.csproj` (`GameMapsTests` checks). Hosted maps: `farm`, `farmhouse`, `busstop`, `forest`, `town`, `backwoods`.

Instances and placeables ([ADR-015](../docs/adr/ADR-015-private-instances-and-placeables.md)): `MapSpec.IsPrivate` (farm, farmhouse) makes one copy of a map per player; `Maps/MapInstance` (map id + owner, `Key`) names a copy.
`MapSpec.Placeable` (farmhouse) allows furniture. `Placeables/PlaceableCatalog` maps a furniture item (`ItemKind.Furniture`, ids 300-399) to a `PlaceableDefinition` (views per rotation, sheet rectangle,
footprint); `PlacedSet` holds the pieces of one place and is the single rule set (`Check`: rotation, inside the map, free floor, off reserved tiles, no overlap; `CoversFeet`; `BuildGrid` for collision)
that the server decides with and the client predicts with.

Visiting ([ADR-019](../docs/adr/ADR-019-visiting-private-instances.md)): `Visits/VisitRules` is the pure rule set for private places: `MayDo(instance, player, action)` (the owner does everything; a visitor only walks and waters; a shared map has no owner),
`IsVisitor`, `MayEnter` (the owner, a listed player, or a co-op member while the owner's setting is on; never a guest's place) and the limits (`MaxListed` = 50, co-op members may visit by default). The server asks it in `FarmService`, `PlaceableService`, `MarketService` and `VisitAccess`.

Ground items ([ADR-021](../docs/adr/ADR-021-tools-resource-nodes-ground-items.md) D5-D6, `GroundItems/`): `GroundItemRules` holds the numbers and rules of an item lying in the world (pickup radius 1.5 tiles, `CanPickUp`: the cause's
5 s priority on shared maps, owner-and-cause only on a private farm; `Timing`: 10 minute expiry on shared maps, never on a private one; `PopPosition`: where a harvest lands so the harvester can take it at once; `ChunkOf`:
the 16 x 16 tile bucket). `GameRandom` is the seeded generator (SplitMix64) and `ItemYield` (item, quantity range, chance) the data type for yields: rules take the generator as a parameter, the server supplies the seed
(`IYieldRandom`), the client never rolls.

Items, inventory, farming and quests ([ADR-011](../docs/adr/ADR-011-inventory-farming-data-model.md)):

- `Items/ItemCatalog` (tools, seeds, produce; stable id + key + stack size) and `Farm/CropCatalog` (stage durations in watered game days, harvest item).
- `Inventory/InventoryGrid`: fixed slots of stacks; `Add`, `Remove`, `RemoveFromSlot`, `Move` (move, split, merge, swap) are all-or-nothing and track the changed
  slots. `StarterKit` is what a new player holds: tools and seeds only. `InventoryLimits`: 36 slots, 9 in the hotbar.
- `Farm/FarmRules`: `Till`, `Plant`, `Water`, `Harvest` on a `FarmTile` (`GrowthDays`, `WateredDay`, `TendedDay`: game day numbers from `GameCalendar.DayNumberAt`) and `GrowthDaysAt` / `StageAt` / `IsMature` / `IsWet` / `IsReverted`;
  a crop grows one day for every game day its soil was watered, counted when that day ends ([ADR-021](../docs/adr/ADR-021-tools-resource-nodes-ground-items.md) D7); bare tilled soil reverts with 30 % per dry day by the pure hash `RevertRoll`.
  `InReach` is the 32 px rule. `TillGrid.FromMap` marks `Diggable` ground that is not blocked. Time and the calendar are always parameters.
- Seasons ([ADR-018](../docs/adr/ADR-018-seasons-and-server-calendar.md)): `Calendar/GameCalendar` (pure: UTC instant + epoch + game-day length to `GameDate` year/season/day/minute; 28 days a season; `WitherAtMs`),
  `Season`, `SeasonSet`. `CropDefinition.Seasons` lists where a crop grows; `FarmRules.Plant` / `Harvest` take the calendar (`OutOfSeason`, `Withered` verdicts) and `FarmRules.IsWithered` is computed from `PlantedAtMs`.
  `ShopRules.IsInSeason` gates seeds in the shop.
- Social layer ([ADR-013](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-014](../docs/adr/ADR-014-coops-and-looks.md)): `Economy/CoinRules` (starting coins, market fee, listing limits), `ShippingPrices`, `ShippingBinRules`, `MarketRules`;
  `Trade/TradeOffer` and `TradeSession` (two-party confirm: a different offer clears both confirmations); `Looks/LookCatalog` and `PlayerLook`; `Coops/CoopRules` (names, who may withdraw how much); `Coops/CoopLand` (the Town gate and land tiles, the `coop.farm.*` audit reason codes; co-op land is the map `coopfarm`, one instance per co-op: [ADR-020](../docs/adr/ADR-020-coop-land.md));
  `Text/LocalizedText` (validates and resolves `i18n{` envelopes). Clothing items have a `WearSlot`; tools are not tradable (`ItemDefinition.IsTradable`).
- Shops and nicknames ([ADR-013 amendments](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-009 amendments](../docs/adr/ADR-009-postgresql-persistence-and-accounts.md)): `Shops/ShopCatalog` (shop id, key, price list; three shops: seed 1, clothing 2, furniture 3), `Shops/ShopRules` (`Price`, `InReach`, `TryFindShop`), `Maps/ShopPoint` (`MapSpec.Shops`: where a shop is served; the Town has three, doormats at (43,57), (20,89), (95,51)); `Players/NicknameRules` (3-16 characters, unaccented ASCII letters and digits, reserved list, unique `Key`, neutral guest names) and `Players/ProfanityFilter` with its data in `ProfanityTerms` (substring terms, whole-word abbreviations, leet and accent folding).
- Tools and resource nodes ([ADR-021](../docs/adr/ADR-021-tools-resource-nodes-ground-items.md) D1-D4, D8): five tools (`ToolKind`: hoe, watering can, axe, pickaxe, scythe; the starter kit holds all five, the new three last so slots 0-4 never move); `Nodes/NodeCatalog` (hits, tool, blocking and yield per `NodeKind`: tree, stump, twig, stone, copper ore, weed, grass; trees are `TreeVariant` oak/maple/pine at `TreeStage` seed to grown),
  `NodeRules` (`Hit`, `RollYield`, `Age`), `NodeField` (one instance's nodes in 16 x 16 chunk buckets; `BuildGrid` adds the blocking ones to the walk grid), `NodeSites.Read` (the `Paths` layer index table, documented and tested against the real maps), `NodePlacements` (first fill and public-map regrowth, deterministic from a seed), `NodeRandom` (SplitMix64). `Farm/WaterGrid` marks water tiles; `FarmRules.Clear` is the pickaxe on empty tilled soil.
- `Quests/QuestDefinition` / `QuestCatalog`: objectives count `QuestEvent`s (`CropPlanted`, `CropHarvested`); `first_harvest` is the tutorial.

Chat, friends and mail ([ADR-029](../docs/adr/ADR-029-chat-friends-mail.md)): `Chat/ChatRules` (`Sanitize`: NFC, controls/format/bidi dropped, whitespace collapsed, 160 characters; `Prepare` adds `ProfanityFilter.Mask`), `Chat/ChatRateLimiter` (token bucket, 5 per 10 s, burst 3), `Friends/FriendRules`, `Mail/MailRules` (30 days, system mail codes). `VisitRules.MayEnter` gained `isFriend`, `friendsMayVisit` and `blocked`.
