# ADR-089: Gathering review — tier energy, node health, level-up perks and tool numbers

## Status

Accepted (2026-11-01). Amends ADR-023 (energy costs, D1), ADR-026 (D1, tier effects: energy per use is now a tier effect), and ADR-081 (D3, the
"tier discounts are unchanged" line now has a live rule behind it). No wire change: `NosumProtocol.Version` stays as it is. Evidence and the full
list of findings: [docs/design/critique-gathering.md](../design/critique-gathering.md).

## Context

A review of woodcutting, mining and foraging, the blacksmith and the skill levels (critique-gathering.md) found four gaps in existing systems:

1. **Tool tier gave no energy benefit.** `EnergyRules.Cost(tool, level, tier)` already encoded a tier discount, but no production path called it: the
   server (`PlayerProgressService`) and the client prediction (`PlayerActions`, `ProgressViewModel`) both used the two-argument overload. A copper
   or iron axe cost three energy a swing, the same as a basic one, so the blacksmith's upgrade bought damage only.
2. **A damaged node looked the same as a fresh one.** Hits left were on the wire (`NodeInfo.Hits`) but the sprite only shook for 200 ms. A player
   could not see how far a tree was from falling.
3. **A level-up named the level and not what it changed.** The energy discount of a gathering skill arrives at level 4 and level 8 (`EnergyRules`,
   `LevelsPerDiscount`), and the banner said nothing about it.
4. **The tool tooltip hid the numbers that decide a choice.** Area of a hoe or can and energy per use were not shown, so the tier choice could not be
   weighed from the tooltip.

## Decision

- **D1 — The tier discount is live, for axes and pickaxes.** `EnergyRules.TierDiscount(tool, tier)`: a copper axe or pickaxe saves **one** energy
  per use, an iron one saves **two** (`(int)tier`). Other tools get none: the hoe and the watering can buy a longer line, not less energy, and the
  scythe is free. The discount stacks with the skill discount and never goes below one energy. The server passes the held item's tier to
  `IPlayerProgress.ChargeAsync` and `TryChargeAsync` (new optional parameter, default `Basic`); the client predicts with the same rule.
  Effect at level 0: a basic tree costs 30 energy (10 swings × 3), a copper one 10 (5 × 2), an iron one 4 (4 × 1).
- **D2 — Damaged nodes show their health.** A node that takes more than one hit in total and stands with some hits taken draws a two-pixel bar
  above it: twelve pixels wide, filled in proportion to the hits left (rounded up, never empty), green above half, amber above a quarter, red
  below. A full node and a one-hit node show nothing. The rule is `NodeHealthRules` (gameplay, pure); the colours and sizes are the sprite code's.
  The bar uses the existing solid-fill sprite primitive: no art is added (ADR-040).
- **D3 — The level-up banner names the perk.** A gathering level (Farming, Foraging, Mining) that is a multiple of `LevelsPerDiscount` adds a second
  banner line: "Mining tools: 1 less energy per use" (the discount the level now gives). Combat and fishing levels, and the other levels, add none.
  The text is a resource key in both languages (`Skill_Perk`).
- **D4 — The tool tooltip shows area and energy.** A hoe or a watering can shows its line length (`Tooltip_Area`: 1, 3 or 5 tiles). A tool that costs
  energy shows its energy per use at level zero, tier included (`Tooltip_ToolEnergy`; "before the skill bonus" says the skill discount is not in
  the number).
- **D5 — Numbers.** No base number changes in this ADR. The tier energy values in D1 are new data and are the only retune. Node hit counts,
  respawn counts, XP values and the energy base stay as they are until a playtest on the new build shows a reason (critique-gathering.md records the
  measured values and the open questions for a follow-up).

## Consequences

- An upgrade to copper or iron now shows up in the energy meter and in every tool's "uses left" estimate, which is the change the blacksmith's price
  was made for. At level 8 an iron axe and a basic axe both cost one energy (the skill discount reaches the floor), so the tier's energy value is
  largest for players below level 8; damage and the line length keep the tier useful after that.
- Energy income is unchanged (270 a day); a copper axe spends a third of the energy a basic one did on the same tree, which makes wood cheaper
  per energy and raises the foraging XP per energy point (basic 0.7, copper 1.6, iron 3.75 per energy on a tree at level zero).
- Old clients show the old banner and tooltip; the server rule is the same for them, so no client can spend less than the server charges.

## Tests

- `EnergyTierCostTests` (unit): the discount per tier and tool, the floor of one, the hoe and scythe unchanged, the copper tree at ten energy.
- `NodeHealthRulesTests` (unit): the bar's visibility rule (full, one-hit, broken), the pixel rounding, the three levels.
- `LevelUpPerkTests` (unit): the perk at levels four and eight for the gathering skills, nothing at other levels or for combat and fishing.
- `ProgressTests` (unit, existing): the uses-left estimate, with the tier added.
- Wire: no packet, opcode or status code changed, so the wire tests are unchanged. The server charge uses the same rule as the client prediction;
  `EnergyWireTests` and the node tests keep the basic-tier numbers.

## Alternatives considered

- **Keep the two-argument cost and delete the unused overload.** Simpler, but it leaves the upgrade without an energy value and the ADR-081 text
  false. Rejected.
- **A hit bar drawn as text or a crack sprite.** Text is a second font on the map, and a crack needs new art. The bar uses an existing primitive.
- **A tier discount for the hoe and the can as well.** Their tier is a longer line, and the line is already the benefit; a second benefit would be a
  retune with no data behind it. Left for a later ADR.
