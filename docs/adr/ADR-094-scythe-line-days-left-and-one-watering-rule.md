# ADR-094: The scythe works a line, an unripe crop says how many days are left, and one rule decides a watering

## Status

Accepted

## Context

The farming critique (`docs/design/critique-farming.md`, backlog items 1, 2 and 6) found three gaps after ADR-088.

- A scythe works one tile. Harvesting a row of ripe crops costs one press per tile, while a hoe or a watering can already works a line with one swing
  (ADR-026 D1, ADR-088 D1). `ToolRules.ValidateArea` admits only those two tools.
- An unripe crop refused the action with a sentence that did not say how long it had left (`FarmStatus.NotMature`, "The crop is not ready yet."). The
  days left come from the crop's growth, which only `FarmRules` knows, so the client could not state them without guessing.
- The wet-soil refusal lived in `FarmService` and was repeated in `PlayerActions.TargetWorks` for the reticle. `FarmRules.Water` returned `Ok` on wet
  soil, so the pure rule module the tests read did not encode the refusal (ADR-088 D3).

## Decision

- **D1 - The scythe works a line.** `ToolRules.ValidateArea` admits the scythe. It has its own curve, `ToolRules.ScytheAreaLength(tier)`: three tiles at
  basic, five at copper and iron. The scythe has a single tier today (`ItemCatalog`, no blacksmith upgrade, `ToolRules.IsUpgradable`), so the line is
  three tiles; the copper and iron rows exist so that a later scythe upgrade needs no rule change. A basic scythe therefore gains a three-tile line.
  The hoe and the watering can keep the curve of ADR-026 D1 (1, 3, 5).
  - A trailing tile of a line harvests a ripe crop **for the scythe only**. The hoe and the can still skip a ripe crop on a trailing tile, so a
    line of tilling never harvests by accident. A trailing tile with a withered crop is cut by the scythe (`FarmAction.CutWithered`), as on the first tile.
  - Every tile is charged its own energy and reports `Worked` (ADR-088 D1, D2). The reticle shows the whole line (the client, backlog item 5).
- **D2 - An unripe crop names the days left.** `FarmRules.DaysUntilMature` returns the watered game days still needed (the crop's total growth days less
  `GrowthDaysAt`, never below zero). The reply `UseItemResult` gains `DaysLeft` (a byte, appended as `[SerializeOrder(3)]`), set on
  `FarmStatus.NotMature` and zero otherwise. The server sends the code and the number; the client writes "Not ready, 2 days left." in each language
  from its `.resx` (no display text on the wire). "Days left" counts the watered days still needed, so it can be one day before the crop is ripe
  if the day of watering has not ended yet.
- **D3 - One function decides a watering.** `FarmRules.WaterVerdict(current, nowMs, calendar, outdoors, sprinklerSinceMs)` returns `NotTilled`,
  `AlreadyWet` (soil wet already, ADR-088 D3), `Withered` or `Ok`, in that order. `FarmService` and `PlayerActions.TargetWorks` call it; the client
  predicts the same verdict the server gives. `FarmRules.Water` keeps its behaviour and is called after an `Ok`.
- **D4 - Wire.** Protocol version 31 (master took 30 for market sorting). `UseItemResult.DaysLeft` is appended; no opcode, no existing field and no existing enum value changed. Clients built
  for 29 misread the reply, so the server and the clients ship together.

## Consequences

- A basic scythe harvests a row of three ripe crops with one swing; the number of presses for a row of harvests drops from three to one.
- The scythe's line is new balance: each tile it works is charged the scythe's energy as any tile is, so a line of three costs three tiles of energy.
  No price, crop, growth or energy number changed.
- The unripe refusal now carries a number; the server runs no extra query, since the days come from the stored tile and the calendar.
- The three watering checks agree by construction: the server, the reticle and the tests read one function.

## References

- docs/adr/ADR-026 D1 (wide use and tiers), ADR-088 (wide-use count, wet-soil refusal), ADR-021 D7 (growth by watered days), ADR-018 (the scythe cuts
  a withered crop), ADR-023 D1 (energy with the effect).
