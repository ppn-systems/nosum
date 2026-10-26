# ADR-038 — Co-op upgrades bought with the warehouse

**Status:** Accepted (coordinator, 2026-10-25). Costs, capacities and land rectangles are defaults (open owner questions).

## Context

[Pillar 1](../design/pillars.md) says a co-op "unlocks shared buildings and tools no single player can afford", and roadmap M4 lists "co-op upgrades bought with the
warehouse (bigger warehouse, co-op land size)". The warehouse ([ADR-014](ADR-014-coops-and-looks.md)) is stock by item kind with no capacity and no coins (a transfer party of kind
`Coop` holds items only, [ADR-013](ADR-013-economy-coins-escrow-market-trade.md)); the co-op land ([ADR-020](ADR-020-coop-land.md)) is the whole 80 x 65 map, unlimited. Upgrades need
both to have a size, and a price the co-op already holds.

## Decision

- **D1 — Two tracks, levels 0 to 3.** `CoopTrack.Warehouse` and `CoopTrack.Land`, each a level on the `coops` row (`warehouse_level`, `land_level`, default 0 - one migration, two columns). Every existing co-op starts at 0.
- **D2 — Warehouse capacity counts item kinds ("slots").** The warehouse has no stacks, so a slot is one item kind: 20 / 30 / 40 / 60 at levels 0 to 3 (`CoopUpgrades.WarehouseSlots`). A deposit of a kind the warehouse already holds always fits; a new
  kind needs a free slot, else `CoopStatus.WarehouseFull`. Quantity per kind stays unlimited. A warehouse already above its capacity (filled before this ADR) keeps its stock and only refuses new kinds. The check runs under the co-op gate, in
  `DepositAsync`, before the transfer.
- **D3 — Price: items taken from the warehouse stock** (there is no treasury and ADR-013 forbids coins on a co-op party): wood, stone and bars.

  | Level | Warehouse | Land |
  |:--|:--|:--|
  | 1 | 50 wood, 25 stone | 100 wood, 50 stone |
  | 2 | 150 wood, 100 stone, 5 copper bars | 250 wood, 150 stone, 8 copper bars |
  | 3 | 300 wood, 250 stone, 10 copper bars, 5 iron bars | 500 wood, 400 stone, 10 iron bars |

  The price is one `ITransferService` transfer whose legs take items from the co-op party and give them to nobody (a sink, like a market fee); the new level and one `CoopLogEntry` per item (negative delta, reason `coop.upgrade.warehouse|land`,
  the buyer's membership id) are enlisted in the same transaction. All or nothing: too little stock changes nothing. Upgrade lines are not warehouse arithmetic (`CoopLand.IsWarehouseReason` is unchanged), so they never change what a member may withdraw, and the
  panel's recent-log list stays deposits and withdrawals (as for farm lines in ADR-020 D5; showing them needs a reason field on `CoopLogRow`, a wire change).
- **D4 — Land size.** Level 0 is the northern meadow of `CoopFarm.tmx` around the arrival and exit tiles (tiles x 19-60, y 8-36); level 1 adds the southern block (x 19-60, y 37-60), level 2 the east strip (x 61-79), level 3 the rest (the whole map).
  Locked ground stays walkable. The server refuses **tilling** and **placing or moving an object** (the whole footprint must be unlocked) on locked tiles with `FarmStatus.LockedLand` / `PlaceStatus.LockedLand`. Soil tilled and objects placed before are never taken away, and watering, harvesting, picking up,
  chopping and mining are not restricted (a level never goes down). The land level is read with the membership (`CoopMembershipInfo.LandLevel`, one extra primary-key lookup in the lookup that already runs for every action), so a purchase applies to the next action without a cache to invalidate.
  Existing co-ops that tilled outside level 0 keep their soil.
- **D5 — Who buys.** The founder only (`CoopStatus.NotFounder` otherwise): ADR-014 D8 gives the founder the spending of shared stock. The founder's role passes to the longest-standing member when they leave, and the buyer's role is re-read in the service, never trusted from the client.
- **D6 — One purchase, once.** `UpgradeCoop { Track, TargetLevel }` (opcode `0xA008`, encrypted, rate-limited like warehouse moves) names the level it buys, which must be exactly the current level + 1. Under the co-op gate the service reads the level and the stock, checks `CoopUpgrades.CanBuy`,
  and the enlisted work re-checks the level inside the transaction. Two concurrent requests for the same level: the first pays, the second gets `AlreadyUpgraded` and nothing moves. `MaxLevel` when the track is complete; `NotEnough` when the stock is short.
- **D7 — Everyone sees it at once.** `CoopSnapshot` gained `WarehouseLevel` and `LandLevel` (appended, `NosumProtocol.Version` 4); after a purchase every online member gets their own snapshot as for any warehouse change.
- **D8 — Client.** The panel shows both levels, the capacity ("kinds held / slots"), the price of the next level with what the warehouse holds, and a Buy button for the founder only; `FarmStatus.LockedLand` and `PlaceStatus.LockedLand` have messages. The co-op land is drawn shaded over the locked tiles
  (`LockedLandDecorator`, from the same `CoopUpgrades` rectangles, a black fill through the existing `BlackFill` texture), driven by the level in the snapshot.

## Alternatives considered

- **A coin treasury.** The pillars and ADR-013 keep coins out of warehouses; a treasury needs a new ledger and a rule for who may fill it. Items are what a co-op holds today.
- **Capacity as total quantity.** The warehouse is not slotted; a cap on quantity punishes the crops the pillars ask members to contribute, while a cap on kinds is a simple, visible limit.
- **Hide or wall off locked land.** Walls need map edits (`.tmx`, early art); a rule plus a shade changes no asset and the land can grow without touching the map.
- **Any member may buy.** A member could spend stock they never put in; the founder already holds that right.
- **"Buy next level" without a target level.** A double click would buy two levels.

## Consequences

- Every co-op action that already looked up membership now reads the `coops` row too.
- Taking the price out of the warehouse can remove an item kind from it (stock row at zero), freeing a slot.
- The land rectangles are tied to `CoopFarm.tmx`; a different map needs its own table.
- Ground a co-op tilled before this ADR, outside level 0, keeps working.

## Revisit when

A co-op treasury exists, upgrade lines belong in the member-visible log, co-op land gets its final map, a co-op may have several leaders, or levels beyond 3 are wanted.

## Open owner questions

Prices, capacities and land blocks above; whether the founder alone should buy; whether locked land should be sold in tile-sized steps instead of blocks.
