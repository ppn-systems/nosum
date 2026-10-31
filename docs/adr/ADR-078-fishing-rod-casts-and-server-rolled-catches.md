# ADR-078: Fishing with a rod, a timed reel-in and server-rolled catches

## Status

Accepted

## Context

The farm, the woods and the mine all reward work with an item from a tool. Water is on the map already: the Beach sea, the Woods pond and the
pond on the player's farm are `Water` tiles of the `terrain` layer in their `.tmx` files, and the walk grid already reads them (`WalkGrid.FromMap`).
Nothing is caught there. Fishing adds a new way to earn coins and food, trains a skill (the skills system of docs/adr/ADR-023 already has the
tracks, the level curve and the stored experience), and feeds the existing cooking recipes.

The brief fixes three constraints. The server decides every catch (docs/adr/ADR-003, `gameplay-rules`): the client may show a timing game but
never the result. The timing must not be a way to cheat, so the server judges the press on its own clock. And the wire is append-only
(`nalix-packets`): the new packets take a fresh opcode band and the protocol version moves on.

## Decision

- **D1 — Where a line can go.** A cast names a tile. It counts only when the map is a fishing water (`FishingWaters.Of`: the Beach, the Woods and
  the farm, `FishingWater`), the tile is water in the map's `Back` layer (`TileTraits.Water`), the player stands within reach of the tile, and the
  player holds a fishing rod in the selected slot. The server reads the walk grid of the instance, so the client cannot name a tile that is not water.
- **D2 — The rod is a tool.** `ToolKind.FishingRod` (item `40`, craftable from wood and fiber, known from the start). It trains the fishing skill
  (`SkillKind.Fishing`, value 5) and is bound to its owner like the other farming tools (`ItemDefinition.IsTradable` stays false).
  The rod never goes through the farm's `UseItem` intent: casting has its own packets, so the farm handler is unchanged.
- **D3 — Ten species, data in the rules.** `FishCatalog` holds ten species, each with a rarity (common, uncommon, rare, legendary), a weight, the
  seasons it is caught in (`SeasonSet`), its waters, a day or night rule and a weather rule. The fish are items `950` to `959` (sold to the shipping
  bin at their `SellPrice`, 30 to 500 coins). A fish stew (item `703`, recipe `30`, cooked from two anchovies and a potato) is the cooking use.
  The numbers are balance data and may change without an ADR.
- **D4 — The bite and the press are timed by the server.** `CastRod` starts a cast: the server stores it with its own clock reading, draws the bite
  delay (2 to 6 seconds) and the press window (1.0 second at level zero, 0.5 seconds at level ten) from the cast seed, and replies with both.
  `ReelIn` carries only the cast id. The server computes the elapsed time from its own clock and judges it with `FishingRules.Judge`: before the bite
  minus 300 ms it is `TooEarly` and the cast stays open; from there to the end of the window plus 400 ms it is hooked; later the fish `Escaped` and the
  cast is over. The client shows the window as a CSS bar that starts when the reply arrives: it draws no timing of its own that counts.
- **D5 — The catch is rolled on the server from a seed it made.** The seed (`FishingRules.SeedOf`) mixes a player key, the cast id, the server cast
  time and the tile, so no client can choose it or replay it. `FishingRules.Roll` takes the conditions (`FishingSpot`: water, season, weather, hour,
  fishing level) and the seed, and returns a species or nothing. The chance of nothing falls with the level (30% at level zero, 8% at the floor); a
  rare fish's weight grows with the level, faster for rarer fish. Weather and time use the server calendar (`GameCalendar`, `WeatherRules.At`). The
  roll uses `NodeRandom` (SplitMix64), so the same inputs always give the same catch: the unit tests pin that.
- **D6 — Costs and rewards.** A cast costs `FishingRules.CastEnergy` (2) energy, charged once when the cast starts through the progress charge
  of docs/adr/ADR-023 (the same transaction discipline as the tools). A catch goes to the inventory in the same unit of work; a full inventory
  refuses the cast before the charge (`NoSpace`). Experience (`CatchXp`) is 5, 10, 20 or 40 by rarity and 1 for nothing, stored as `FishingXp` on
  the player's state row and pushed through the existing skill push.
- **D7 — Open casts live in memory.** A cast belongs to one player and one map, lasts until it is reeled in, escapes or times out (a generous
  limit of a minute), and is held by the fishing service, not the database. A restart loses an open cast, never an item or coins: nothing is
  charged or added until the reel-in. A new `CastRod` replaces the player's open cast.
- **D8 — Wire.** Band `0xC300-0xC3FF` (C2S `0xC300` `CastRod`, `0xC301` `ReelIn`; S2C `0xC340` `CastResult`, `0xC341` `ReelResult`), status
  `FishingStatus`. `NosumProtocol.Version` becomes 13. The new enum values (`ToolKind.FishingRod`, `SkillKind.Fishing`, `CraftSkill.Fishing`) are
  appended and no existing packet changed.
- **D9 — The client shows, the server answers.** The client holds no rule that decides anything. It sends the cast when the held item is a rod and the
  clicked tile is water, shows the bite window from the server's delay, sends `ReelIn` on the press, and shows the reply. Text is by key
  (`docs/adr/ADR-010`): the reply carries a code, the view model maps it to a `.resx` string.

## Consequences

- Fishing is the first skill-trained activity without a tile change: the cast is a point action at water, reach-checked and energy-charged.
- The timing tolerance (300 ms early, 400 ms late) is a trade-off: a press from a slow connection within the window still counts, and a press
  that is early by more than the tolerance is refused. Because the server measures from its own cast time, a client cannot gain a window by lag
  reporting; the cost is that a slow link eats some of the window, which the tolerances cover.
- The fish need no new inventory kind (they are `ItemKind.Produce`), and the shipping bin and the market already accept them.
- Protocol 13: the server and the client ship together (`nalix-packets`).
- Out of scope: selling the rod at a shop, a bobber sprite on the water, sharing catches with co-op members.

## References

- docs/adr/ADR-003 (server-authoritative), ADR-010 (i18n), ADR-011 (inventory), ADR-021 (tools and nodes), ADR-023 (energy and skills).
- Rules: `gameplay/Nosum.Gameplay/Fishing/`. Wire: `contracts/Nosum.Contracts/Fishing/`. Protocol: `NosumProtocol.Version` 13.

## Amendments

### 2026-10-30 - fish size (protocol 21)

- The reel result carries the fish's size in centimetres: `ReelResult.SizeCm` (appended field, `NosumProtocol.Version` 21). Zero when nothing was caught.
- The server rolls the size from the same cast seed as the catch (`FishingRules.SizeCm`, its own stream), so the size is fixed by the server and repeats for the same seed.
- Ranges by rarity, inclusive, in centimetres: Common 10-30, Uncommon 20-45, Rare 40-70, Legendary 60-100. They are balance data in `FishingRules`.

### 2026-10-31 - economy balance (ADR-080)

A cast costs ten energy (was two), and fish bin prices fall to 60 %. Expected coins per cast now sit in the 2.0 to 3.2 coins-per-energy band
(ADR-080 D1, D5). The figures are in `docs/design/economy.md`.
