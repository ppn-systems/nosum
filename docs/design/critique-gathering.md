# Critique: gathering (woodcutting, mining, foraging, tools, skill levels)

Review for the `claude/rebuild-gathering` wave. Scope: resource nodes (ADR-021), tool tiers and the blacksmith (ADR-026), energy and skill XP
(ADR-023, ADR-081), level-ups (ADR-062), the skills page. Numbers come from the code on master at `384cb1d` unless marked **measured**. The
decisions taken from this review are in [ADR-089](../adr/ADR-089-gathering-review-tier-energy-and-feedback.md).

## 1. Hits per node

Source: `NodeCatalog` (`HitsFor`, `Hits` per kind), `ToolRules.DamagePerHit` (tier + 1 per swing), `NodeRules.Hit`.

| Node | Hits | Basic | Copper | Iron |
| :-- | --: | --: | --: | --: |
| Tree, grown | 10 | 10 axe swings | 5 | 4 |
| Stump | 2 | 2 | 1 | 1 |
| Large stump (iron only) | 6 | — | — | 2 |
| Twig | 1 | 1 | 1 | 1 |
| Stone | 1 | 1 pickaxe swing | 1 | 1 |
| Copper ore | 3 | 3 | 2 | 1 |
| Iron ore (iron only) | 4 | — | — | 2 |
| Weed, grass | 1 | scythe, 1 | | |
| Tree, sapling and younger | 1 | 1 | | |

A grown tree takes ten swings with a basic axe, which is the longest single-tool chore on the farm. Copper halves it and iron leaves four swings.

## 2. Energy per node (level 0, before and after ADR-089)

Energy is charged on every swing: a damaging hit and the breaking hit both cost the tool's cost. Tool base cost is 3 (ADR-081 D3); the scythe is free.

| Node and tool | Swings | Energy before | Energy after | XP gained | XP per energy after |
| :-- | --: | --: | --: | --: | --: |
| Tree, basic axe | 10 | 30 | 30 | 21 (9 hits × 1 + break 12) | 0.70 |
| Tree, copper axe | 5 | 15 | **10** | 16 (4 hits + break 12) | 1.60 |
| Tree, iron axe | 4 | 12 | **4** | 15 (3 hits + break 12) | 3.75 |
| Stone, basic pickaxe | 1 | 3 | 3 | 4 | 1.33 |
| Copper ore, basic pickaxe | 3 | 9 | 9 | 12 (2 hits + break 10) | 1.33 |
| Copper ore, copper pickaxe | 2 | 6 | **4** | 11 | 2.75 |
| Iron ore, iron pickaxe | 2 | 6 | **2** | 16 | 8.00 |

Reading the table: at level 0 a basic tree is 11 % of the 270 daily energy. Foraging XP per energy is best on iron ore and the iron axe; stone is the
cheapest energy-per-swing action and gives the least XP per energy. The tier discount was missing before (finding G2): the copper and iron columns
cost the same as the basic one.

## 3. Respawn pacing

Source: `NodeLimits` (`RegrowPerDay` 12, `ForagePerDay` 6, `MaxForageStanding` 24, `DaysPerTreeStage` 2) and `GameCalendar.DefaultDayLengthMs`.

- A game day is **12 real hours** (`DefaultDayLengthMs`), so one real day is two game days.
- Public maps regrow at most 12 debris nodes per instance per game day (24 per real day, spread over the instance). Forage: at most 6 per day,
  24 standing at once.
- A planted tree seed needs three stages of two game days each to grow (`DaysPerTreeStage`): six game days, about **three real days**, before it can
  be chopped for the full yield. That is slow for a player who wants wood now; it is correct for a farm that is played daily.
- Private farms do not regrow debris (only weeds, at a season change). Mine floors are rebuilt each game day from the day's seed.
- **Measured in play:** see section 7. The respawn counts are from the code; the time a player actually waits is not yet measured.

## 4. Tool tier value

| Tier | Axe and pickaxe, damage per swing | Hoe and can, line length | Can capacity | Energy per use, before | Energy per use, after |
| :-- | --: | --: | --: | --: | --: |
| Basic | 1 | 1 | 40 | 3 | 3 |
| Copper | 2 | 3 | 55 | 3 | 2 |
| Iron | 3 | 5 | 70 | 3 | 1 |

Before this wave, the tier paid in damage, line and capacity only (finding G2: the energy discount was written but never charged). The tooltip
showed damage and capacity but not the line or the energy per use, so a player could not weigh an upgrade from the tooltip (finding G4).

A flag for the next balance pass: at skill level 8 (the second energy discount) an iron axe and a basic axe both cost one energy, because the
discounts stop at one. Past level 8 the tier's energy value is zero; its damage and line are the benefit that remains.

## 5. XP pacing per skill

Source: `SkillRules` (thresholds, `BreakXp`, `HitXp` 1, `TillXp` 3, `WaterXp` 2, `HarvestXp` 10).

- Thresholds, cumulative XP: L1 100, L2 380, L3 770, L4 1300, L5 2150, L6 3300, L7 4800, L8 6900, L9 10000, L10 15000.
- Foraging to L10 with basic axes is about 715 tree breaks (15000 / 21), or about 21 000 energy at level zero. At 270 energy a day that is over
  seventy game days, before the discounts. With copper axes the same XP costs about a third of the energy.
- Mining is the fastest skill to start: a stone is 4 XP for 3 energy and needs no tier.
- Farming XP is per action, not per energy: a harvest is 10 XP and a watered tile is 2 XP for 3 energy.

Pacing verdict: the early levels (L1 to L4) come within the first real day for an active player; L10 is a long goal, which suits the genre. The
gap is in the feel, not the sums: nothing on the screen shows XP arriving (finding G3, not yet implemented).

## 6. Findings

| ID | Finding | Evidence | Status in this wave |
| :-- | :-- | :-- | :-- |
| G1 | A damaged node looks the same as a fresh one; only a 200 ms shake shows a hit. | `NodeSprites.Add`, `NodeInfo.Hits` on the wire, unused for drawing. | **Fixed**: HP bar over damaged standing nodes (ADR-089 D2). |
| G2 | The tier energy discount was written (`EnergyRules.Cost(tool, level, tier)`) but never charged; ADR-081 D3 says tier discounts apply. | No production caller of the three-argument overload before this change. | **Fixed**: axe and pickaxe save 1 (copper) or 2 (iron) per use, server and client (ADR-089 D1). |
| G3 | XP is invisible when it arrives; a level-up names the level and not its effect. | `LevelUpBannerViewModel` message had no perk; no XP floater on gathering. | **Partly fixed**: the level-up banner names the energy perk at levels 4 and 8 (ADR-089 D3). XP floaters: not done. |
| G4 | The tool tooltip hides the line length and the energy per use. | `ItemTooltip` tool branch. | **Fixed**: area for hoe and can, energy per use for tools that cost energy (ADR-089 D4). |
| G5 | Respawn is slow to the player: a tree seed takes about three real days to grow. | `DaysPerTreeStage`, `DefaultDayLengthMs`. | Not changed. Needs a playtest of the farm rhythm before any number moves. |
| G6 | The hoe and can have a tier value only in the line length; the energy discount is not applied to them. | `EnergyRules.TierDiscount` (axe and pickaxe only). | Not changed; recorded as a choice in ADR-089. |
| G7 | The wide swing (line of tiles) uses the R key, not a hold. | ADR-026 deviation. | Not changed (input path owned elsewhere). |

## 7. Measured in play

Playtest on master (`384cb1d`, unmodified worktree), Dev QA hook for grants, level and day advances. Chromium, 1280×720. Source of each row is the
frame or server log named in the playtest notes; the swing counts come from the XP and energy deltas, since the damage of a damaged node lives in memory
and the database keeps the full value until the node breaks.

| Item | Measured (before) | Code (before) |
| :-- | :-- | :-- |
| Twig, basic axe | 1 swing, −3 energy, +2 Foraging XP, row removed | 1 hit |
| Stone, basic pickaxe | 1 swing, −3 energy, +4 Mining XP | 1 hit |
| Grown tree, basic axe | 12 accepted swings (10 to fell, 2 for the stump), −36 energy, +26 XP, 13 wood | 10 hits; stump 2 |
| Grown tree, copper axe | 6 swings (5 + 1 stump), −18 energy, +20 XP | 5 + 1 |
| Weed, scythe | 1 swing, 0 energy, +1 XP | 1 hit, free |
| Copper ore, basic pickaxe | not measured (no ore reached on floor 1) | 3 hits, 10 XP |
| Energy | 270 a day; at 0 the server refuses with the out-of-energy message | refill at the first charge after the day changes |
| XP to levels | L1 at 109 XP (the fourth copper tree) | thresholds 100, 380, 770, 1300 |
| Level-up banner | "LEVEL UP!" and "Foraging reached level 2!" with no effect line | no perk text in the view model |
| Public-map respawn, one game day (Town) | twigs 3→5, stones 3→7, weeds 7→13, forage +6, trees 16→16; total 12 = cap | `RegrowPerDay` 12, trees never return |
| Blacksmith copper upgrade | 2 000 coins and 5 copper bars; ready in two game days, collected on day 16 | ADR-026 D2 |
| Damaged node, visual | no HP bar and no crack; the trunk is identical after one hit | 200 ms shake only |
| Tooltip, basic axe | "Resource damage: 1 per swing" | — |
| Tooltip, copper axe | "Resource damage: 2 per swing" | — |

Findings from the playtest that the table adds to section 6:

- **G8, the phone gate.** At 390×844 the game shows "Window too narrow — widen to at least 1024 px to play" (`Device.MinimumWidth` = 1024 in
  `wwwroot/appsettings.json`). `DevicePolicy.cs` says phones play by default, so the code and its comment disagree. The 390 frames of this review were
  taken with the minimum overridden to 320 in a scratch copy; the gate frames are kept without the override. This is outside the gathering rules, so
  this wave does not change it; it is raised for the owner.
- **G9, the tooltip's swing count.** The basic axe tooltip says a full-grown tree "takes about ten swings". Measured: 12 accepted swings with the stump.
  The number is close enough to leave; noted so the next text pass knows.
- **G10, stale energy.** After the day changes, the HUD energy reads 0 until the first charge refills it; the refill is server-side at the first use.
  Cosmetic, but it reads as "out of energy" at the start of a day.

### 7b. Measured after (branch commit `c0dfdd6`)

| Item | After | Before | Note |
| :-- | :-- | :-- | :-- |
| Grown tree, copper axe | 6 swings (5 + 1 stump), −12 energy (2 per swing), +20 XP | −18 (3 per swing) | the stump's extra swing is in the count |
| Grown tree, iron axe | 5 swings, −5 energy (1 per swing), +19 XP | not available | |
| Twig, stone, weed, basic tree | unchanged | same | the tier change does not touch basic tools |
| Health bar | 12 × 2 px; 11 of 12 px after one hit on a 10-hit tree, 6 of 12 at about five hits, 3 of 12 at about eight | none | colours match the tiers |
| Stump, first hit | bar red, 3 of 12 px | predicted amber, 6 of 12 | **open**, see the QA README |
| Tooltip, basic / copper / iron axe | energy per use 3 / 2 / 1 | — | "before the skill bonus" |
| Hoe and can tooltip | "Area: 1 tiles in a line" plus "Energy per use: 3" | — | the plural reads "1 tiles"; fix in the text pass |
| Level-4 banner | "Foraging tools: 1 less energy per use" | none | Vietnamese: "Công cụ Hái lượm: tốn ít hơn 1 năng lượng mỗi lần dùng" |
| Respawn, Town, one game day | twigs +2, stones +2, weeds +8, forage +6, trees 0; total 12 = cap | +2 / +4 / +6 | the mix moved; the cap held |

Not re-verified after the change: zero-energy refusal, the blacksmith hand-in, copper ore and forage.

## 8. Captures

Before and after, English and Vietnamese, at 1280×720 and 390×844, are in [docs/qa/rebuild-gathering/](../qa/rebuild-gathering/). Each set is graded
against [ui-standard.md](ui-standard.md) section 7 in the folder's README.
