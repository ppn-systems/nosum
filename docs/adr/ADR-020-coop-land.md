# ADR-020 — Co-op land: a shared farm instance per co-op

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Review: harvester keeps the produce and any member may harvest (co-op trust model, every action audited); dissolving wipes the land; the Town gate stays until the M3 map work places a proper entrance.

## Context

[Pillar 1](../design/pillars.md) promises a co-op "owns shared land and a shared warehouse next to each member's own farm". The warehouse exists ([ADR-014](ADR-014-coops-and-looks.md));
the land did not ("Co-op land is not built"). Farming ([ADR-011](ADR-011-inventory-farming-data-model.md)) is already keyed by a place that has an owner id
([ADR-015](ADR-015-private-instances-and-placeables.md) D1, D5), and ADR-015 noted that "the co-op land can use the same idea with the co-op id as the owner". Numbers and rules marked
"default" are open owner questions.

## Decision

- **D1 — A co-op's land is a `MapInstance` owned by the co-op.** The map `coopfarm` (`MapSpec.IsCoop`) is hosted like any other map, but its instance is `coopfarm@<coop id>`
  (`MapInstance.OfCoop`): the owner id is the co-op's id, not a player's. Presence, the Nalix group, the farm cache and the `farm_plots` rows are all keyed by that instance, so two
  members see each other and nobody else, with no change to presence or snapshots. The map file is the early `Farm_Foraging.tmx` (an open 80 x 65 meadow with no buildings, same tilesheets
  as the farm; [ADR-007](ADR-007-own-art-16px.md)), chosen because it differs from the personal farm and needs no new tilesheet. Its file exits to the bus stop, the forest and the backwoods stay.
- **D2 — Entry is a gate in Town, and membership decides the instance.** Town tile (13, 60) (the west plaza, south of the road from the bus stop) is an exit to `coopfarm`; the way back is the land's tile
  (46, 25) to Town (12, 60). `PlayerService` never trusts a client: for a co-op map it asks `ICoopMembership` (one database lookup, no cache) and enters the *player's own co-op's* instance. A player with no co-op is
  refused: they stay on the gate tile, the exit is disarmed until they step off it (so a refused player does not cost a lookup per move). A player saved on co-op land who is no longer a member starts on the
  starting map. `IInstanceAccessPolicy` is not consulted for co-op land, because the instance is derived from the player's own membership and cannot name another co-op.
- **D3 — The same farm rules, any owner.** `FarmService` and `FarmRules` were already independent of who owns the place, so no rule is duplicated and the plot key did not change: `farm_plots.OwnerId` is
  a player's id on a private farm and a co-op's id on co-op land. The only addition: on a co-op map the service re-checks, inside the per-instance lock and for *every* action, that the player belongs to the
  co-op that owns the instance (`NotOnFarm` otherwise), so a kick takes effect on the next action even before the eviction in D4 arrives.
- **D4 — Roles and leaving.** Founder and members may do everything on the land (default): till, plant, water, and harvest crops planted by others. Leaving, being kicked, or dissolving
  ends access immediately: after the change is saved `CoopService` calls `ICoopLandEviction.EvictAsync`, and `PlayerService` warps the player to Town (12, 60) through the normal warp path (the old map is
  left, the new place is saved). A member caught mid-warp into the land is re-checked after arrival and moved out too.
- **D5 — Who gets the harvest (default; owner question).** The harvester receives the produce into their *own* inventory; seeds come out of the *planter's* inventory; both are the existing atomic inventory
  transaction. Every action writes one `CoopLogEntry` in the same transaction as the tile and the items: member, item, signed quantity for that member's inventory (plant `-1` seed, harvest `+n` produce, till/water `0` with the tool),
  reason `coop.farm.till|plant|water|harvest`. Warehouse arithmetic (`CoopRules.Withdrawable`, a member's net contribution) counts only `coop.deposit` and `coop.withdraw`, so working the land never entitles anyone to the warehouse. The panel's
  recent-log list stays warehouse-only; the farm lines are persisted, not yet shown.
- **D6 — Dissolution wipes the land.** A co-op dissolves only when its last member (the founder) leaves with an empty warehouse ([ADR-014](ADR-014-coops-and-looks.md) D7). The co-op row, its audit lines and its
  plots (crops included) are deleted in one transaction and the in-memory copy is dropped (`IFarmService.Forget`). Co-op ids are never reused.
- **D7 — Wire: unchanged.** The farm intents (`GetFarm`, `UseItem`), `MapChanged` and the existing statuses carry everything; the instance decides ownership. No opcode, field or enum value was added, and
  `NosumProtocol.Version` is untouched. No migration: plots and audit lines use existing tables.
- **D8 — Client.** Nothing changed except linking `Farm_Foraging.tmx` into the WebAssembly bundle: the warp, `GetFarm` after a map change and the farm decorator already work from the map's `Farmable` flag.

## Alternatives considered

- **Harvest into the co-op warehouse.** Makes the land communal and gives the warehouse an income, but turns every harvest into a deposit and needs a rule for taking it out (the net-contribution rule would let nobody take it). Left as the owner's option.
- **New "enter co-op land" packet and a button in the co-op panel.** An explicit request, but a new opcode for something a door already does; the gate reuses the whole map-change flow.
- **A shared `coopfarm` instance keyed by map only, with plots tagged by co-op.** Every co-op would see every other co-op's crops and players.
- **Checking membership once at entry only.** A kicked member could keep farming until they walked out; the per-action check is one indexed lookup.

## Consequences

- Each action on co-op land costs one extra membership query and one extra audit insert (inside the existing transaction).
- The Town gate has no sign or art yet (early content); a player finds it by walking to the west plaza. A refused non-member sees nothing happen.
- The land is large (about 2 000 tillable tiles). It is no longer unlimited from the start: [ADR-038](ADR-038-coop-upgrades.md) opens it in blocks that a co-op buys.
- The audit lines for tilling and watering are numerous; they are never read in bulk, but nothing prunes them yet.

## Revisit when

The harvest should go to the warehouse, members need finer roles (only the founder plants), the audit trail should show farm lines in the panel (needs an appended reason field on `CoopLogRow`, a wire change), co-op land gains buildings or furniture
(`Placeable`), a player may belong to several co-ops, or the co-op panel needs a "go to land" button.

## Open owner questions

Harvest to the harvester (built) or to the warehouse; whether members may harvest what others planted (built: yes) or only the planter; whether dissolving should pay out ripe crops instead of wiping them; land size (answered by [ADR-038](ADR-038-coop-upgrades.md): bought, not tied to membership);
whether farm lines belong in the member-visible log; where the gate should stand and how it should look.
