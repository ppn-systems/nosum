# ADR-080: Economy balance targets

## Status

Accepted. Amends ADR-023 (energy costs), ADR-025 (crop prices, seed prices and yields), ADR-028 (animal produce prices) and ADR-078
(fishing cast energy and fish prices). Data only: no mechanic, opcode or wire field changes.

## Context

Balance was left as product defaults ("the numbers are product defaults, not balance"; ADR-013, ADR-025, ADR-078). Measured over the
catalogs, the economy had three outliers and one dead resource:

- **Fishing** paid about 22.6 coins per energy point for a two-energy cast, four times the best crop (eggplant, 5.9) and ten times a
  typical one. A player with a rod had no reason to tend a plot.
- **Animals** paid back their price in about half a season (chicken 1.75×, cow 2.33× their purchase price per season). Their food is free
  (fiber from the scythe), and they cost no energy, so nothing throttled them.
- **Crops** spread from 60 to 330 coins per plot per season (5.5×). Eggplant, strawberry, hot pepper and cranberries dominated the fall and
  spring; parsnip, wheat, sunflower and cauliflower earned 60 to 70.
- **Energy** barely mattered. Tools cost two, the day holds 270 energy, which covers 135 uses, and a farm of forty plots watered daily
  spends 120. Energy never limited the economy.

The owner delegated the balance pass ("upgrade what exists, no new features"). The mechanics stay; only the data changes.

## Decision

**D1 — One currency per energy point.** Each energy sink (crops, fishing) earns a coin band per energy point: **2.0 to 3.2**. Animals,
which use no energy, are banded by payback instead (D4).

**D2 — Crop season profit.** A plot tended for one 28-day season (replanted after each harvest, regrowing crops harvested as they
come back) earns **150 to 260 coins**. Every crop's first harvest repays its seed; a single-harvest seed costs 20 to 50 % of one harvest's
bin value. Prices and seeds move in `ItemCatalog` (bin price) and `ShopCatalog` (seed price); the yam yields two (`CropCatalog`).

**D3 — Energy.** A hoe, watering can, axe or pickaxe use costs **three** energy (was two). The scythe stays free. The daily refill
(270, once per game day) and the skill and tier discounts are unchanged. At three energy a use, energy covers 90 tool uses a day, so a
forty-plot farm uses 120 of 270 and energy starts to bind as the farm grows.

**D4 — Animals.** An animal's everyday product, collected on every day of a 28-day season (every third day for sheep, sunny days for
pig, counted at half of them), pays **40 to 60 %** of its purchase price. Gold products stay 1.5× the everyday product; large products
keep their bonus chance. Sheep were underpaid and now sit in the band.

**D5 — Fishing.** A cast costs **10** energy (was two), and fish bin prices fall to 60 % (for example anchovy 30 → 18, golden koi 500 → 300).
Expected coins per cast then sit in the D1 band.

**D6 — Left alone.** Market fee (5 %), the shipping bin rule, requests (bin + 30 %, +20 % early), quests, community goal pools (now about
+25 % of the bin, kept as a coin sink), tool upgrade costs, blacksmith ore prices, crafted food and the harvest bonus. Each is in
`docs/design/economy.md`.

**D7 — Locked by tests.** `tests/Nosum.UnitTests/Gameplay/EconomyBalanceTests.cs` asserts D1–D5 against the live catalogs, and
`CropRosterTests` asserts D2's seed rule for every crop. The tables in `docs/design/economy.md` are generated from the same catalogs, so
they stay true.

## Consequences

- Every existing player's money shifts at once: crops and animal produce pay more or less depending on the crop, and fishing pays about
  a seventh of what it did per cast. Tool use costs one more energy. The owner should expect a visible change at release.
- Crop bin prices rose, so requests and community goals pay less relative to the bin (goal pools unchanged).
- Some expectations in existing tests were numbers, not rules. They were updated to the new numbers (energy, prices, shop, tooltip,
  consolation). The rules they checked (gold is 1.5×, consolation is half the bin) are unchanged.
- Not done here, owner decisions: the game day length (12 real hours) and the refill rule; free fiber feed; the combat-crafted sell
  prices (grimoire, heart crystal). These need a mechanic or a combat pass, not data.

## References

- `docs/design/economy.md`: the full before-and-after tables and the reviewed list.
- ADR-013 (coins, escrow, market), ADR-018 (seasons, game day), ADR-023 (energy), ADR-025 (crops), ADR-028 (animals), ADR-078 (fishing).
