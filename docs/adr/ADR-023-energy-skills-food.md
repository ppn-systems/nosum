# ADR-023 — Energy, skills and food

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M2. Builds on ADR-018 (calendar) and ADR-021 (tools).

## Context

With five tools and resource nodes (ADR-021) nothing limits how much a player does in a game day except the 300 ms
swing cooldown, and nothing grows with the player. The genre's two answers are an energy budget per day and skills
that level with use. Both must be server state; the client only displays them.

## Decision

- **D1 — Energy.** Every player has `Energy` (max 270 at level 0) and `MaxEnergy`. Each successful tool use costs
  energy by tool (hoe 2, watering can 2, axe 2, pickaxe 2, scythe 0) minus one per relevant skill level step of 4
  (minimum 1, scythe stays 0). At 0 energy tool uses are refused with a status code (no fainting). Energy refills to
  max at each game-day rollover (ADR-018 "day ended" seam) for every player, online or not (computed lazily on load:
  `days since last refill > 0 → full`).
- **D2 — Food.** Items of `ItemKind.Food` restore energy when eaten (an "eat" intent from the hotbar; server removes
  one and adds energy, capped at max). Raw crops are edible at a small value (data per item); cooked dishes (ADR-024
  recipes) restore more. Item ids 700–799 are food.
- **D3 — Skills.** Farming (till, water, harvest), foraging (chop, scythe, pick forage), mining (break stones and
  ore). XP per action is data; levels 1–10 at cumulative XP 100, 380, 770, 1300, 2150, 3300, 4800, 6900, 10000, 15000.
  A level-up is pushed to the client (toast) and unlocks recipes (ADR-024) and the energy discount in D1.
- **D4 — Persistence.** Columns on the player state: energy, energy refilled day, three XP totals (levels derive from
  XP; never stored twice). One generated migration.
- **D5 — Wire.** Player band (`0x2000`) append-only: `EnergyChanged`, `SkillXpChanged`, `LevelUp`, `Eat` intent. The
  coordinator bumps `NosumProtocol.Version` at merge.
- **D6 — Client.** An energy bar next to the hotbar (HUD stays tidy: a thin vertical bar), a skills page in the game
  menu (ADR-017: one tab entry).

## Alternatives considered

- **Real-time stamina regeneration.** Pushes players to stay online; the daily budget suits a two-week season and
  offline days. Rejected.
- **Storing levels.** Derivable from XP; storing both invites drift. Rejected.

## Consequences

Every tool handler must call one `IEnergy.TrySpend` inside the same transaction as its effect. Balancing numbers are
data and may change without an ADR.

## Implementation notes

Implemented 2026-10-25 (D1-D6):

- **Where the state lives.** The five columns are on `player_states` (`energy`, `energy_refilled_day`, `farming_xp`, `foraging_xp`, `mining_xp`, migration `EnergySkills`). A player who uses a tool before
  any place was saved gets a state row with an empty map name, which the player service already reads as "no saved place". `energy_refilled_day` defaults to `-1` (never refilled) for rows that existed before the
  column; the model writes the value explicitly (`ValueGeneratedNever`) so game day zero is stored as zero.
- **One spend.** `IPlayerProgress.ChargeAsync(unit, ...)` is called inside the unit of work that carries the tool's effect: the farm's tile write or pickaxe clear, the node break (through the yield sink's enlistment) or,
  for a hit that does not break a node, `TryChargeAsync` in a unit of its own. A refused charge (`FarmStatus.NoEnergy`) rolls the effect back. Lock order is inventory, then progress. Planting a seed and watering at a
  water tile are free; the scythe costs nothing, so it still works at zero energy.
- **Experience** (data in `SkillRules`): till 3, water 2, harvest 10 (farming); clear tilled soil 1 (mining); a hit that does not break a node 1, a break by node kind (tree 12, stump 4, twig 2, weed and grass 1,
  stone 4, copper ore 10). Foraging covers trees, stumps, twigs, weeds and grass; mining covers stone and ore. XP stops counting at the level-10 threshold.
- **Food.** `ItemKind.Food` is appended for dishes (ids 700-799, ADR-024); `ItemDefinition.FoodEnergy` makes any item edible, and parsnip, radish and cauliflower give 10, 8 and 20. The action key eats the held item
  when it is edible and the player is not full of energy.
- **Wire.** Player band, append-only: `Eat` 0x2002, `EatResult` 0x2044, `EnergyChanged` 0x2045, `SkillXpChanged` 0x2046 (cumulative totals), `LevelUp` 0x2047; `FarmStatus.NoEnergy` = 21. The server pushes the energy
  and experience once after a join. No day-rollover push exists yet: a client sees the refilled energy at its next action or join.
- **Starter tools.** `IInventoryService.BackfillStarterToolsAsync` grants a missing starter tool once on join (audit reason `starter.backfill`); a full bag keeps the tool for the next join.

