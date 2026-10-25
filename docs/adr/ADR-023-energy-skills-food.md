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
