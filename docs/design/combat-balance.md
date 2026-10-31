# Combat balance: mine bands, reference player and the numbers behind them

Status: current balance, decided by [ADR-045 amendment D4–D5](../adr/ADR-045-mmorpg-combat-hp-mp-and-monster-assets.md). The numbers
here are the catalog values in `gameplay/Nosum.Gameplay`; the lock tests are `tests/Nosum.UnitTests/Gameplay/CombatBalanceTests.cs`.
If a value here and the code disagree, the code wins and this table is stale.

## How the tables are computed

The tables use the server's own rules, with these assumptions. Each one is a worst or typical case; none is measured in play.

- **Reference player** per band (below). Combo streak is 1 (no combo bonus): the combo adds up to 45% damage once it reaches 10 and makes fights faster.
- **Sword swing** is 440 ms from the client pacing (`ToolPacing.SwingMs` 360 + `RecoveryMs` 80). One hit per swing.
- **Counter-attack:** each non-lethal hit made within the monster's reach (adjacent) draws one hit, mitigated by armor
  (`CombatRules.MitigateDamage`, minimum 1).
- **Contact:** an adjacent monster strikes once per AI tick (1.5 s, `CombatService.DefaultAiInterval`) during the fight.
- **Hits to kill** = ceil(monster HP / weapon damage). **Time to kill** = hits × 0.44 s.
- **Damage taken per kill** = (hits − 1 + contacts) × mitigated damage, where contacts = floor(time to kill / 1.5 s).
- **Hits to die** = ceil(player max HP / mitigated damage) for one monster.
- **Mine floors** are per game day: monsters are planned once per player per day and are not replaced during the day (there is no in-day
  respawn timer; none was added here).

## Reference player per band

The reference player carries the best weapon and armor that band's floors can give at the expected Combat level. Iron ore comes from
floor 20 (`MineRules.IronMinFloor`), so band 2 is played with bronze until floor 20; the table uses iron for the whole band.

| Band (floors) | Biome (ADR-070) | Weapon | Combat level | Max HP | Armor set | Defense |
|:--|:--|:--|:--|:--|:--|:--|
| 1 (1–10) | Earth | Bronze sword (20) | 2 | 120 | Leather armor | 2 |
| 2 (11–20) | Frost | Iron sword (32) | 4 | 140 | Iron helmet, chestplate, greaves | 10 |
| 3 (21–30) | Magma | Steel sword (48) | 5 | 150 | Iron helmet, chestplate, greaves | 10 |
| 4 (31–40) | Ruins | Gold sword (68) | 7 | 170 | Boss crown, chestplate, greaves | 14 |

## Progression table per band

### Normal monsters (reference player)

Band species are picked by spawn slot (`MonsterSpawnPlanner.SpeciesForMineFloor`). Elite numbers (below) are separate.

| Band | Species (HP / attack / XP) | Hits | Time to kill | Taken per kill | Hits to die | Primary drop (sell) |
|:--|:--|:--|:--|:--|:--|:--|
| 1 | Green slime (24 / 5 / 6) | 1 | 0.44 s | 0 (0%) | 40 | Slime gel (4) |
| 1 | Cave bat (30 / 7 / 8) | 2 | 0.88 s | 5 (4%) | 24 | Bat wing (8) |
| 1 | Spore shroom (40 / 8 / 10) | 2 | 0.88 s | 6 (5%) | 20 | Spore cap (8) |
| 2 | Frost slime (45 / 13 / 11) | 2 | 0.88 s | 3 (2%) | 47 | Slime gel (4) |
| 2 | Goblin scout (60 / 16 / 15) | 2 | 0.88 s | 6 (4%) | 24 | Goblin ear (12) |
| 2 | Spore shroom (40 / 8 / 10) | 1 | 0.44 s | 0 (0%) | 140 | Spore cap (8) |
| 3 | Magma slime (75 / 18 / 18) | 2 | 0.88 s | 8 (5%) | 19 | Slime gel (4) |
| 3 | Skeleton warrior (90 / 20 / 22) | 2 | 0.88 s | 10 (7%) | 15 | Monster bone (12) |
| 3 | Goblin scout (60 / 16 / 15) | 2 | 0.88 s | 6 (4%) | 25 | Goblin ear (12) |
| 4 | Stone golem (140 / 24 / 35) | 2 | 0.88 s | 10 (6%) | 17 | Golem core (35) |
| 4 | Skeleton warrior (90 / 20 / 22) | 2 | 0.88 s | 6 (4%) | 29 | Monster bone (12) |
| 4 | Magma slime (75 / 18 / 18) | 1 | 0.44 s | 0 (0%) | 43 | Slime gel (4) |

Elites (35% of normal kills, `MonsterSpawnPlanner.EliteChancePercent`) have 1.0 to 1.5 times the HP by modifier (Flame aura 1.25,
Swift 1.0, Shielded 1.5, Vampiric 1.2) and 2.5 times the XP. They drop their primary item and a treasure gem (35 coins).

### Bosses (reference player)

| Floor | Boss (HP / attack / XP) | Hits | Time to kill | Taken (share of max HP) | Hits to die | Drops (sell) |
|:--|:--|:--|:--|:--|:--|:--|
| 10 | Cave broodmother (180 / 10 / 75) | 8 | 3.5 s | 72 (60%) | 15 | Ancient scroll (800), necromancer skull |
| 20 | Goblin chieftain (280 / 17 / 125) | 7 | 3.1 s | 56 (40%) | 20 | Golden key (1200), goblin ear |
| 30 | Magma colossus (420 / 19 / 200) | 8 | 3.5 s | 81 (54%) | 17 | Colossus core (1800), treasure gem |
| 40 | Golem king (600 / 24 / 300) | 8 | 3.5 s | 90 (53%) | 17 | Boss crown (2500), runestone tablet |

Bosses enrage at 30% HP and slam for twice their attack (`CombatRules.GroundSlamDamage`); the table is for the basic attacks only.

### Spawns per floor

`MonsterSpawnPlanner.PlanMonsters` places 3 + floor ÷ 8 monsters, clamped to 3–8, and the boss takes slot 0 on boss floors.

| Band | Monsters per floor | Normal kills in a full clear (bosses excluded) |
|:--|:--|:--|
| 1 | 3–4 | 32 |
| 2 | 4–5 | 44 |
| 3 | 5–6 | 56 |
| 4 | 7–8 | 69 |

### Damage taken and healing needed per band

These are the normal kills only (elites add roughly a fifth). The reference player has no natural HP regeneration: damage stays
until a potion, a shrine or a knockout (revive at 25% HP) restores it.

| Band | Normal-fight damage for a full clear | Health potions to cover it (50 HP) | Max HP |
|:--|:--|:--|:--|
| 1 | 110 | about 3 | 120 |
| 2 | 147 | about 3 | 140 |
| 3 | 454 | about 9 | 150 |
| 4 | 406 | about 8 | 170 |

Band 3 is the open problem: it costs about nine potions and so about 18 slime gel, which is roughly all the gel its slimes drop.
See Known gaps.

## Weapons

Damage is the weapon's base plus 2 per Combat level (`CombatRules.CalculateDamage`, no combo).

| Weapon (item) | Base | Level 0 | Level 2 | Level 4 | Level 6 | Level 8 | Level 10 | Ratio to the tier below |
|:--|:--|:--|:--|:--|:--|:--|:--|:--|
| Wooden sword | 12 | 12 | 16 | 20 | 24 | 28 | 32 | — |
| Bronze sword | 20 | 20 | 24 | 28 | 32 | 36 | 40 | 1.67 |
| Iron sword | 32 | 32 | 36 | 40 | 44 | 48 | 52 | 1.60 |
| Steel sword | 48 | 48 | 52 | 56 | 60 | 64 | 68 | 1.50 |
| Gold sword | 68 | 68 | 72 | 76 | 80 | 84 | 88 | 1.42 |
| Arcane staff (10 MP a cast, range 4; no counter from range) | 45 | 45 | 49 | 53 | 57 | 61 | 65 | — |

Every melee tier is at least 40% stronger than the one below it (locked). The staff sits between iron and steel; it costs MP, so
its use is limited by the 50 base MP.

## Armor

Defense is per worn piece; the hat, shirt and pants slots each hold one piece (`CombatRules.ArmorDefense`).

| Piece (slot) | Defense | Recipe (Combat level) |
|:--|:--|:--|
| Leather armor (shirt) | 2 | 4 slime gel, 10 fiber (1) |
| Ranger tunic (shirt) | 3 | leather armor, 2 bat wing, 5 fiber (2) |
| Iron chestplate (shirt) | 5 | 5 iron bar, 2 coal (3) |
| Iron helmet (hat) | 2 | 3 iron bar, 2 monster bone (2) |
| Boss crown (hat) | 6 | drop from the Golem King |
| Iron greaves (pants) | 3 | 4 iron bar, 2 copper bar (2) |

A full set is 10 (iron) or 14 (crown). Mitigation is attack minus defense, minimum 1:

| Monster attack | Against 0 | Against leather (2) | Against iron set (10) | Against crown set (14) |
|:--|:--|:--|:--|:--|
| 8 (spore shroom) | 8 | 6 | 1 | 1 |
| 16 (goblin scout) | 16 | 14 | 6 | 2 |
| 20 (skeleton warrior) | 20 | 18 | 10 | 6 |
| 24 (stone golem) | 24 | 22 | 14 | 10 |

## Potions

| Item | Restores | Share of max HP, bands 1–4 | Recipe |
|:--|:--|:--|:--|
| Health potion | 50 HP | 42%, 36%, 33%, 29% | 2 slime gel, 1 fiber |
| Mana potion | 35 MP | — | spore cap, mana crystal |
| Elixir | 100 HP, 50 MP, Regeneration (5 HP a tick, 15 ticks) | 100 HP plus 75 over the ticks, about 1.5 times band 1 max HP | health + mana potion, treasure gem |
| Heart crystal | 80 HP, 25 MP, Regeneration (8 HP a tick, 20 ticks) | 80 HP plus 160 over the ticks | health potion, golem core, 2 slime gel |

## Experience

Combat XP is awarded per kill; an elite gives 2.5 times the species XP. The cumulative table (SkillRules, unchanged) reads:
380 (level 2), 770 (3), 1300 (4), 2150 (5), 3300 (6), 4800 (7), 6900 (8), 10000 (9), 15000 (10).

| Band | Full clear XP (expected, with elites) | Cumulative | Level reached | Reference level |
|:--|:--|:--|:--|:--|
| 1 | 459 | 459 | 2 | 2 |
| 2 | 939 | 1398 | 4 | 4 |
| 3 | 1720 | 3118 | 5 | 5 |
| 4 | 2551 | 5669 | 7 | 7 |

One full clear of the mine is about 5,700 XP, so level 10 (15,000) takes about 2.6 clears.

## Ore, gems and other drops

The shipping bin pays for produce only. Ore, bars and drops are sold on the market; the blacksmith sells ore to players, at 75 for copper ore and 150 for iron ore. Ore nodes are planned per floor (`MineFloorPlanner`: 3 + reachable ÷ 120 copper nodes and 2 + reachable ÷ 120 + (floor − 20) ÷ 5
iron nodes), each giving 1 to 3 ore. A furnace turns 5 ore into 1 bar.

| Item | Source | Sell price (coins) |
|:--|:--|:--|
| Copper ore | copper nodes (floors 1–39) | 2 |
| Iron ore | iron nodes (floors 20–40) | 4 |
| Coal | stones (1 in 2 at a stone) | 4 |
| Copper bar | furnace, 5 copper ore | 10 |
| Iron bar | furnace, 5 iron ore | 20 |
| Slime gel | slimes (primary) | 4 |
| Bat wing | cave bats (primary) | 8 |
| Spore cap | spore shrooms (primary) | 8 |
| Goblin ear | goblin scouts (primary) | 12 |
| Monster bone | skeleton warriors (primary) | 12 |
| Golem core | stone golems (primary) | 35 |
| Mana crystal | frost slimes and spore shrooms (rare) | 30 |
| Treasure gem | elites (always) and boss 30 (rare) | 35 |

Before this change, ore, gems, bones and gel had no sell price, so the market could not list them, and the goblin ear had no recipe either.

## Before and after

| Measure | Before | After |
|:--|:--|:--|
| Floors 26–40 species break | 25 (not a biome tier) | 20 and 30 (ADR-070 tiers) |
| Iron set vs floor 11+ monsters | 1 damage each (armor 16) | 1 to 6 damage (armor 10) |
| Cave broodmother (floor 10), reference player | 90% of max HP (bronze, level 2) | 60% |
| Golem king (floor 40), reference player | dies (100% of max HP, gold, level 8) | 53% |
| Colossus (floor 30), reference player | 70% (steel, level 6) | 54% (steel, level 5) |
| Full clear of the mine | about 12,400 XP (level 10 in 1.2 clears) | about 5,700 XP (2.6 clears) |
| Goblin ear | no recipe, not sellable | sells for 12 |
| Ore, gems, bones, gel | not sellable | sell for 2 to 35 |

## Known gaps

- **No natural HP regeneration.** A full band clear costs 110 to 454 HP of normal fights. Healing is potions (2 slime gel each), shrines
  and the 25% knockout revive. Band 3 needs about nine potions. A regeneration rule, or a rest rule, is a new mechanic and needs its own
  ADR; it is not part of this balance.
- **Band 2 before floor 20** is played with bronze, since iron ore comes from floor 20. The table does not show the bronze numbers for floors
  11 to 19; they are a bronze-iron mix of the same monsters (2 to 3 hits).
- **Elite fights** are not in the reference table. An elite has 1.0 to 1.5 times the HP, so it takes about that many times the hits, and its Vampiric and Shielded modifiers change the fight.
- **Combo** is left out of the table on purpose: sustained combo (10 hits) multiplies damage by 1.45, which cuts hits to kill by about a third.
- **Doc drift in ADR-047** (the Golem King's drops) was corrected by the amendment there; the catalog is the source of truth.
