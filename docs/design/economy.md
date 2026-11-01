# Economy balance

**Status:** Accepted with [ADR-081](../adr/ADR-081-economy-balance-targets.md). The numbers below are generated from the catalogs
and checked by `tests/Nosum.UnitTests/Gameplay/EconomyBalanceTests.cs`; a data change that breaks a band fails the build.

## The clock and the budget

- A game day is 12 real hours; a season is 28 game days, so **a season is 14 real days** (`GameCalendar`, ADR-018).
- Energy is 270 and refills once per game day (ADR-023). Tools cost **3** energy per use (hoe, watering can, axe, pickaxe); the
  scythe stays free. Planting and harvesting cost no energy.
- At 3 energy a use, energy covers **90 tool uses a day** (60 at the least). A forty-plot farm watered every day spends 120 of
  270; tilling and watering the whole farm and fishing compete for the same budget.

## Principle: one currency per energy point

The energy budget is the one throttle every active income shares. The targets make a coin per energy point roughly the same for
each energy sink, so a player picks an activity by what they have (plots, a rod, time), not because one sink dominates:

| Sink | Coins per energy point (after) | Band |
|:--|--:|:--|
| Crops (best season, replanted and regrown) | 2.1 – 3.1 | 2.0 – 3.2 |
| Fishing, expected value per cast | 2.7 | 2.0 – 3.2 |

Animals cost no energy, so their band is the payback of the purchase price instead: an everyday product returns **40–60 %** of the
animal's price in one 28-day season, so an animal pays for itself in about two seasons.

## Crops: profit per plot per season

A plot is replanted after every harvest and tended by hand every growth day, so the per-season figure counts each harvest a crop
gives inside one 28-day season. A crop is worth one season's profit per plot: `harvests × yield × bin price − seed` for a regrowing
crop, `⌊28 ÷ days⌋ × (yield × bin price − seed)` for a single-harvest one. Energy per season is `3 + 3 × days watered`, counting
every watered day until the last harvest.

Targets: season profit per plot **150 – 260 coins** for every crop; every first harvest pays back its seed; seeds of single-harvest
crops cost 20–50 % of one harvest's bin value; coins per energy **2.0 – 3.2**.

| Crop | Season | Seed (before → after) | Bin price (before → after) | Harvests a season | Profit per plot-season (before → after) | Coins per game day (after) | Energy per season (after) | Coins per energy (before → after) |
|:--|:--|--:|--:|--:|--:|--:|--:|--:|
| cranberries | Fall | 90 → 55 | 30 → 30 (×2 yield) | 5 | 210 → 245 | 8.8 | 78 | 4.04 → 3.14 |
| eggplant | Fall | 30 → 20 | 30 → 22 | 12 | 330 → 244 | 8.7 | 84 | 5.89 → 2.90 |
| strawberry | Spring | 70 → 40 | 60 → 45 | 6 | 290 → 230 | 8.2 | 87 | 5.00 → 2.64 |
| grape | Fall | 60 → 35 | 35 → 37 | 7 | 185 → 224 | 8.0 | 87 | 3.19 → 2.57 |
| hot_pepper | Summer | 35 → 25 | 30 → 30 | 8 | 205 → 215 | 7.7 | 84 | 3.66 → 2.56 |
| wheat | Summer + Fall | 10 → 10 | 20 → 40 | 7 | 70 → 210 | 7.5 | 87 | 1.21 → 2.41 |
| bok_choy | Fall | 20 → 20 | 40 → 50 | 7 | 140 → 210 | 7.5 | 87 | 2.41 → 2.41 |
| sunflower | Summer | 20 → 22 | 40 → 90 | 3 | 60 → 204 | 7.3 | 75 | 1.20 → 2.72 |
| garlic | Spring | 12 → 16 | 25 → 45 | 7 | 91 → 203 | 7.2 | 87 | 1.57 → 2.33 |
| tomato | Summer | 40 → 40 | 25 → 48 | 5 | 85 → 200 | 7.1 | 84 | 1.52 → 2.38 |
| pumpkin | Fall | 75 → 80 | 150 → 180 | 2 | 150 → 200 | 7.1 | 81 | 2.78 → 2.47 |
| melon | Summer | 55 → 50 | 110 → 150 | 2 | 110 → 200 | 7.1 | 75 | 2.20 → 2.67 |
| parsnip | Spring + Fall + Winter | 10 → 12 | 20 → 40 | 7 | 70 → 196 | 7.0 | 87 | 1.21 → 2.25 |
| blueberry | Summer | 50 → 50 | 15 → 20 (×3 yield) | 4 | 130 → 190 | 6.8 | 78 | 2.50 → 2.44 |
| green_bean | Spring | 30 → 25 | 15 → 30 | 7 | 75 → 185 | 6.6 | 87 | 1.29 → 2.13 |
| corn | Summer + Fall | 60 → 55 | 35 → 60 | 4 | 80 → 185 | 6.6 | 81 | 1.48 → 2.28 |
| yam | Fall | 30 → 30 | 60 → 60 (×2 yield) | 2 | 60 → 180 | 6.4 | 63 | 1.43 → 2.86 |
| cauliflower | Spring + Summer | 30 → 30 | 60 → 120 | 2 | 60 → 180 | 6.4 | 75 | 1.20 → 2.40 |
| kale | Spring | 22 → 22 | 45 → 65 | 4 | 92 → 172 | 6.1 | 75 | 1.84 → 2.29 |
| potato | Spring | 18 → 18 | 35 → 60 | 4 | 68 → 168 | 6.0 | 75 | 1.36 → 2.24 |
| radish | Spring + Summer + Fall | 15 → 20 | 30 → 60 | 4 | 60 → 160 | 5.7 | 75 | 1.20 → 2.13 |

Before the change the spread was 60 to 330 coins per plot-season (5.5×). Eggplant (330), strawberry (290), hot pepper (205) and
cranberries (210) dominated the fall and spring, while wheat, cauliflower, sunflower and parsnip earned 60 to 70. After the change
the spread is 160 to 245 (1.5×). The crops still differ: fast crops (radish, potato) earn less per day than long, regrowing or
multi-yield ones, and a crop out of season earns nothing.

## Animals: everyday product per season against the purchase price

| Animal | Price | Everyday product (before → after) | Season value (before → after) | Share of price (before → after) |
|:--|--:|:--|--:|--:|
| Chicken | 800 | egg 50 → 15 (daily) | 1,400 → 420 | 1.75× → 0.53× |
| Duck | 1,100 | duck egg 70 → 20 (daily) | 1,960 → 560 | 1.78× → 0.51× |
| Cow | 1,500 | milk 125 → 27 (daily) | 3,500 → 756 | 2.33× → 0.50× |
| Goat | 1,800 | goat milk 150 → 32 (daily) | 4,200 → 896 | 2.33× → 0.50× |
| Sheep | 1,600 | wool 60 → 80 (every third day, 9 a season) | 540 → 720 | 0.34× → 0.45× |
| Pig | 2,000 | truffle 250 → 70 (sunny days, about 14 a season) | 3,500 → 980 | 1.75× → 0.49× |

The large and gold tiers stay above their everyday product: a gold product is 1.5× the everyday kind (asserted by
`AnimalRulesTests.GoldProduce_SellsForHalfMoreThanTheEverydayKind`), and the large tier comes on top at up to 55 % of days. The
figures use the everyday product, so they are a floor. Sheep were underpaid before and now sit in the band.

Feeding is one ration a night (hay, or fiber from the scythe, which is free). Hay costs 12 coins at the carpenter (was 30, then 10; docs/adr/ADR-081
amendment 2026-11-01 and its round-2 amendment): a fed animal costs at most 336 coins a season in hay, and every animal's season product covers it. Fiber makes feed free.

## Fishing: expected coins per cast

Expected value per cast = (1 − 30 % nothing) × the weight-averaged bin price of the ten species. Energy per cast was 2 and is 10.

| | Average catch (coins) | Expected coins per cast | Cast energy | Coins per energy |
|:--|--:|--:|--:|--:|
| Before | 64.6 | 45.2 | 2 | 22.6 |
| After | 38.8 | 27.1 | 10 | 2.7 |

Species prices fell to 60 % of their old value (anchovy 30 → 18, golden koi 500 → 300), and the cast costs five times the energy.
Before the change a fishing cast earned 22.6 coins per energy point: about four times the best crop (eggplant, 5.9) and ten times a typical one, so the rod was the dominant income.

## Energy

| Action | Energy (before → after) |
|:--|:--|
| Hoe, watering can, axe, pickaxe | 2 → 3 |
| Scythe | 0 (unchanged, grass and weeds are the free feed) |
| Fishing cast | 2 → 10 |
| Planting, harvesting, animals, selling, crafting | 0 (unchanged) |

Energy per game day: 270, unchanged. A tool use costs 3 at the basic tier, 2 with a copper tool or four skill levels.

## Reviewed and left as they are

| Source | Current value | Verdict |
|:--|:--|:--|
| Market fee | 5 % of the price, rounded up | Fine: a sink on every player-to-player sale |
| Shipping bin | item `SellPrice` | Set by the crop and animal changes above; the one price every other reward reads |
| Requests board | bin price + 30 %, +20 % in the first 4 hours | Fine: about a 1.3–1.6× premium on any item the bin buys |
| Quests | 10 – 50 coins once | Fine: a progression nudge, not an income |
| Community goal pools | 10 000 – 50 000 coins, shared, capped per player | Kept. The pool is now about +25 % of the bin price (was +50 %), because the bin rose with the crops. This keeps coins in circulation down |
| Tool upgrades | copper 2 000 coins, iron 8 000 coins, plus bars | Fine: a copper tool is a quarter of a season for a forty-plot farm |
| Blacksmith ore | coal 150, copper ore 75, iron ore 150 coins | Kept as a sink: a few ore short of a bar, not a way around the mine |
| Crafted food | fish stew sells 120; ingredients now cost about 96 at the bin | Fine: a small margin |
| Dungeon crafts | arcane grimoire 650, heart crystal 750 | Not changed here. Their ingredients are monster drops with no bin price, so they cannot be compared yet; the combat balance pass owns them |
| Harvest bonus | 20 % chance of a second yield | Not changed: it is a constant, and the seasons above assume a base yield |
| Footwear and the speed tonic (ADR-084) | Tailor: leather boots 150 (sell 60), traveler boots 380 (sell 150). Crafted: swift boots 300, winged boots 900, speed tonic 40 (sell prices) | Set against the clothing shop and the armour, not the energy bands: boots and the tonic cost no energy to use, so a coin-per-energy band does not apply. The entry pairs cost about two shirts and about two and a half pairs of pants; the crafted pairs are a combat reward, like the armour, and the tonic is a cheap draught for a long walk |

## Open decisions for the owner

- **Game day length.** At 12 real hours a day, energy refills twice a real day. The coordinator's note that energy refills by the
  clock rather than by rest stands; changing the refill is a new mechanic and needs a new decision.
- **Free feed.** The scythe makes fiber free, so fed animals cost nothing to keep. Making fiber scarcer is a mechanic, not data.
- **Season length.** A season is 28 game days, which caps the crop cycle at 28 days. The crop bands assume that length.
- **Animal payback.** Two seasons is the target. If the owner wants a faster start, the first animal's price is the lever, not the product.
