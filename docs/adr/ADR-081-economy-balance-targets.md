# ADR-081: Economy balance targets

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

## Amendment (2026-11-01): hay is a ration that pays (economy rebuild)

The simulation (`tests/Nosum.UnitTests/Gameplay/EconomyReportTests.cs`, see docs/design/critique-economy.md) found that feeding was a loss: a hay ration cost
**30** coins, one night of feed for one animal, and the everyday product of a chicken (15), a duck (20) and a cow (27) is less than that. A fed chicken lost
420 coins a season and a fed cow 84, so no player who had fiber (free from the scythe) would buy hay, and the purchase was a dead option.

- **D8 - Hay 30 → 10** (`ShopCatalog`, carpenter). A fed season is now positive for every animal: chicken +140, duck +280, cow +476, goat +616, sheep +440, pig +700
  (the everyday product less 28 rations). A ration still costs less than the chicken's egg (15), so the rule is the same as before: feed is a choice that pays.
- **Locked by tests.** `EconomyBalanceTests.HayRation_EveryAnimal_SeasonProductPaysForAFedSeason` and `HayRation_CostsLessThanTheDailyEgg` read the live carpenter price.
- **Left alone:** fiber stays free (the owner's open question on free feed); the animal prices, the crop and fish bands (D1, D2, D5), the energy rules (D3), and the milk pail (300).
- **Consequence.** Players with a herd and no scythe grass can now keep their animals fed profitably; the coin sink from feeding is smaller than the sink in the 30-coin version
  (a fed herd of six pays about 1,700 coins a season for hay, where it paid about 5,000).

## Amendment (2026-11-01, round 2): the herd pays for its feed, the late tiers cost more, the backpack step is retuned

The second economy pass (docs/design/critique-economy.md, sections 2 and 7) read the simulation's numbers again. Data only; no mechanic, opcode or wire
field changes, and `NosumProtocol.Version` stays 30.

- **D9 - Feed is the recurring sink (energy-free herds).** Animals use no energy, so a herd earned 156 coins a game day with none (critique section 2). Feed is the
  only coin cost that grows with the herd, so it is the lever the data has: **hay 10 → 12** a ration (`ShopCatalog`, carpenter). A fed season is still positive for every
  animal (chicken +84, duck +224, cow +420, goat +560, sheep +384, pig +644), and a ration still costs less than the chicken's egg (15).
  The herd is still uncapped and fiber is still free: capping herds, or tying animals to energy, is a mechanic and stays an owner decision.
- **D10 - Backpack step (ADR-083).** The second step 1 500 → **1 000** coins (54 slots). The step prices are now 600 for nine slots (67 each) and 1 000 for nine (111 each),
  a ratio of 1.67 where it was 2.5. `BackpackRules`, locked by `BackpackRulesTests`.
- **D11 - Late tool tier (ADR-026).** The iron upgrade 5 000 → **8 000** coins (plus five iron bars, unchanged); copper stays 2 000. `BlacksmithRules.IronCoins`,
  locked by `BlacksmithRulesTests`.
- **What this does to the sink.** One-time sinks move from about 27 000 to about 29 500 coins (−500 backpack, +3 000 iron). Feed adds about 20 % to the recurring sink.
  This is not the fix the critique asked for: a late-game sink that scales with a season's income (about 19 000) needs a recurring cost (upkeep, a building, a town project),
  which is a new system, so it stays open for the owner.
- **Locked by tests.** `EconomyBalanceTests.HayRation_*` (season positive for every animal, ration below the egg), `BackpackRulesTests`, `BlacksmithRulesTests`.
- **Left alone:** the crop and fish bands (D1, D2, D5), animal prices, the energy rules (D3), the milk pail, the market fee.

## Amendment (2026-11-01, round 4): the chicken pays back in about four seasons

The round-4 review (docs/design/critique-animals-pets.md, finding 7) measured a chicken's payback net of its feed. The simulation
(`tests/Nosum.UnitTests/Gameplay/EconomyReportTests.cs`, now with a `fed_payback_seasons` column; report in
[`docs/qa/animals-round4/economy/`](../qa/animals-round4/economy/economy-report-after.txt)) gave a chicken **9.5 seasons** fed: 800 coins, an egg of 15 and a
ration of 12 a night leave 84 coins a season. The other animals were 3.1 to 4.9. Data only; no mechanic, opcode or wire field changes, and
`NosumProtocol.Version` stays 30.

- **D12 - The egg sells for 20** (`ItemCatalog.Egg`, was 15). The duck's egg already sells for 20. The golden egg is 1.5× the everyday egg (D4), so it
  follows to **30** (`ItemCatalog.GoldenEgg`, was 22); `AnimalRulesTests.GoldProduce_SellsForHalfMoreThanTheEverydayKind` checks it.
- **D13 - The chicken costs 950** (`AnimalCatalog`, was 800). Its everyday product is 560 a season, 58.9 % of the price, inside the D4 band (40 to 60 %).
- **D14 - Fed payback limit: five seasons, every animal** (`EconomyBalanceTests.Animal_FedPaybackOverASeason_IsWithinFiveSeasons`). A fed season's net
  must pay the price back in at most five seasons.

| Animal | Price (before → after) | Fed season net (before → after) | Fed payback in seasons (before → after) |
|:--|--:|--:|--:|
| Chicken | 800 → **950** | 84 → **224** | 9.5 → **4.2** |
| Duck | 1,100 | 224 | 4.9 |
| Cow | 1,500 | 420 | 3.6 |
| Goat | 1,800 | 560 | 3.2 |
| Sheep | 1,600 | 384 | 4.2 |
| Pig | 2,000 | 644 | 3.1 |

- **What the herd does.** Eight thousand eight hundred coins of animals (six kinds, one each) earned 156 a game day before; now 8,950 coins earn 160.7 a day.
  Herd payback is 56 game days (unchanged to the day).
- **Still open, for the owner.** The duck is the slowest animal now (4.9 seasons); it is the one the owner may want to lower. The late-game sink and the energy-free
  herd (ADR-081 round 2, D9) are unchanged.
- **Tests.** `EconomyBalanceTests` (the D4 band and the new D14 limit), `CarpenterViewModelTests` (the chicken's label reads 950), and the integration tests that grant
  coins for a chicken (`BuildingServiceTests`, 950 each).
- **Consequence for players.** A new player's first chicken costs 150 coins more, and a chicken's eggs pay 33 % more. Fed chickens break even in about 4 seasons, not 9.5.
