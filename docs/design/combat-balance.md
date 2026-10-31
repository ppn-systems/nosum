# Combat balance: mine bands, reference player and the numbers behind them

Status: current balance, decided by [ADR-045 amendment D4–D5](../adr/ADR-045-mmorpg-combat-hp-mp-and-monster-assets.md). The numbers
here are the catalog values in `gameplay/Nosum.Gameplay`; the lock tests are `tests/Nosum.UnitTests/Gameplay/CombatBalanceTests.cs`.
If a value here and the code disagree, the code wins and this table is stale.

## How the tables are computed

The tables use the server's own rules, with these assumptions. None is measured in play.

- **Reference player** per band (below). Combo streak is 1: the combo adds up to 45% damage at 10 hits and makes fights shorter.
- **Sword swing** is 440 ms from the client pacing (`ToolPacing.SwingMs` 360 + `RecoveryMs` 80). One hit per swing.
- **Counter-attack:** each non-lethal hit made within the monster's reach (adjacent) draws one hit, mitigated by armor
  (`CombatRules.MitigateDamage`, minimum 1).
- **Contact:** an adjacent monster strikes once per AI tick (1.5 s, `CombatService.DefaultAiInterval`) during the fight.
- **Ground slams (bosses):** one telegraphed slam at the start of a fight and one per 7 s (1 s telegraph + 6 s cooldown),
  for twice the boss attack, mitigated by armor. The player does **not** dodge in these tables.
- **Hits to kill** = ceil(monster HP / weapon damage). **Time to kill** = hits × 0.44 s.
- **Damage taken per kill** = (hits − 1 + contacts) × mitigated attack, where contacts = floor(time to kill / 1.5 s); bosses add the slams.
- **Hits to die** = ceil(player max HP / mitigated attack) for one monster.
- **Mine floors** are per game day: monsters are planned once per player per day and are not replaced during the day. There is no
  in-day respawn timer, and none was added.

## Reference player per band

| Band (floors) | Biome (ADR-070) | Weapon | Combat level | Max HP | Armor set | Defense |
|:--|:--|:--|:--|:--|:--|:--|
| 1 (1–10) | Earth | Bronze sword (20) | 2 | 120 | Leather armor | 2 |
| 2 (11–20) | Frost | Iron sword (32) | 4 | 140 | Iron helmet, chestplate, greaves | 10 |
| 3 (21–30) | Magma | Steel sword (48) | 5 | 150 | Iron helmet, chestplate, greaves | 10 |
| 4 (31–40) | Ruins | Gold sword (68) | 7 | 170 | Boss crown, chestplate, greaves | 14 |

Iron ore comes from floor 20 (`MineRules.IronMinFloor`), so band 2 is played with bronze until floor 20; the table uses iron for the band.

## Progression table per band

### Normal monsters

Band species are picked by spawn slot (`MonsterSpawnPlanner.SpeciesForMineFloor`). Elites are below.

| Band | Species (HP / attack / XP) | Hits | Time to kill | Taken per kill | Hits to die | Primary drop (sell) |
|:--|:--|:--|:--|:--|:--|:--|
| 1 | Green slime (60 / 8 / 6) | 3 | 1.32 s | 12 (10.0%) | 20 | Slime gel (4) |
| 1 | Cave bat (60 / 8 / 8) | 3 | 1.32 s | 12 (10.0%) | 20 | Bat wing (8) |
| 1 | Spore shroom (82 / 5 / 10) | 4 | 1.76 s | 12 (10.0%) | 40 | Spore cap (8) |
| 2 | Frost slime (82 / 17 / 11) | 3 | 1.32 s | 14 (10.0%) | 20 | Slime gel (4) |
| 2 | Goblin scout (118 / 18 / 15) | 3 | 1.32 s | 16 (11.4%) | 18 | Goblin ear (12) |
| 2 | Spore shroom (82 / 5 / 10) | 3 | 1.32 s | 2 (1.4%) | 140 | Spore cap (8) |
| 3 | Magma slime (166 / 23 / 18) | 3 | 1.32 s | 26 (17.3%) | 12 | Slime gel (4) |
| 3 | Skeleton warrior (166 / 23 / 22) | 3 | 1.32 s | 26 (17.3%) | 12 | Monster bone (12) |
| 3 | Goblin scout (118 / 18 / 15) | 3 | 1.32 s | 16 (10.7%) | 19 | Goblin ear (12) |
| 4 | Stone golem (166 / 23 / 35) | 3 | 1.32 s | 18 (10.6%) | 19 | Golem core (35) |
| 4 | Skeleton warrior (166 / 23 / 22) | 3 | 1.32 s | 18 (10.6%) | 19 | Monster bone (12) |
| 4 | Magma slime (166 / 23 / 18) | 3 | 1.32 s | 18 (10.6%) | 19 | Slime gel (4) |

Elites (35% of normal kills) have 1.0 to 1.5 times the HP by modifier and 2.5 times the XP, so they take about 5 to 8 hits.
They drop their primary item and a treasure gem (35 coins).

Known exception: the spore shroom is one species in bands 1 and 2, and no single HP and attack gives both bands a 10% floor
(band 1 needs an attack near 5, band 2 near 20). Band 2's shroom takes 1.4%. The test exempts it.

### Bosses

Each boss has 50 hits to kill at its band's reference weapon, so 22 s, inside the 20–40 s target. The damage is the counters and
contacts, plus the slams, none dodged.

| Floor | Boss (HP / attack / XP) | Hits | Time to kill | Taken (share of max HP) | Hits to die | Drops (sell) |
|:--|:--|:--|:--|:--|:--|:--|
| 10 | Cave broodmother (1200 / 2 / 75) | 50 | 22.0 s | 71 (59%) | 120 | Ancient scroll (800), necromancer skull |
| 20 | Goblin chieftain (2000 / 7 / 125) | 50 | 22.0 s | 79 (56%) | 140 | Golden key (1200), goblin ear |
| 30 | Magma colossus (2900 / 7 / 200) | 50 | 22.0 s | 79 (53%) | 150 | Colossus core (1800), treasure gem |
| 40 | Golem king (4100 / 11 / 300) | 50 | 22.0 s | 95 (56%) | 170 | Boss crown (2500), runestone tablet |

A boss's attack is low against its band's armor because the boss is meant to be a long fight. Its danger is the slams, and dodging
them is the player's lever. Bosses enrage at 30% HP.

### Spawns per floor

`MonsterSpawnPlanner.PlanMonsters` places 3 + floor ÷ 8 monsters, clamped to 3–8, and the boss takes slot 0 on boss floors.

| Band | Monsters per floor | Normal kills in a full clear (bosses excluded) |
|:--|:--|:--|
| 1 | 3–4 | 32 |
| 2 | 4–5 | 44 |
| 3 | 5–6 | 56 |
| 4 | 7–8 | 69 |

### Damage taken and healing needed per band

Normal kills only. There is no natural HP regeneration: damage stays until a potion, a shrine or a knockout (revive at 25% HP) restores it.

| Band | Normal-fight damage for a full clear | Health potions (45 HP) to cover it | Per floor |
|:--|:--|:--|:--|
| 1 | 384 | about 9 | about 1 |
| 2 | 526 | about 12 | about 1 |
| 3 | 1286 | about 29 | about 3 |
| 4 | 1242 | about 28 | about 3 |

This is the open problem. Band 3 needs about 29 potions, which is about 58 slime gel, and its slimes supply roughly 19. See Known gaps.

## Weapons

Damage is the weapon's base plus 2 per Combat level (`CombatRules.CalculateDamage`, no combo). Unchanged by this retune.

| Weapon (item) | Base | Level 0 | Level 2 | Level 4 | Level 6 | Level 8 | Level 10 | Ratio to the tier below |
|:--|:--|:--|:--|:--|:--|:--|:--|:--|
| Wooden sword | 12 | 12 | 16 | 20 | 24 | 28 | 32 | — |
| Bronze sword | 20 | 20 | 24 | 28 | 32 | 36 | 40 | 1.67 |
| Iron sword | 32 | 32 | 36 | 40 | 44 | 48 | 52 | 1.60 |
| Steel sword | 48 | 48 | 52 | 56 | 60 | 64 | 68 | 1.50 |
| Gold sword | 68 | 68 | 72 | 76 | 80 | 84 | 88 | 1.42 |
| Arcane staff (10 MP a cast, range 4; no counter from range) | 45 | 45 | 49 | 53 | 57 | 61 | 65 | — |

Every melee tier is at least 40% stronger than the one below it (locked).

## Armor

Defense is per worn piece; the hat, shirt and pants slots each hold one piece (`CombatRules.ArmorDefense`). Unchanged by this retune.

| Piece (slot) | Defense | Recipe (Combat level) |
|:--|:--|:--|
| Leather armor (shirt) | 2 | 4 slime gel, 10 fiber (1) |
| Ranger tunic (shirt) | 3 | leather armor, 2 bat wing, 5 fiber (2) |
| Iron chestplate (shirt) | 5 | 5 iron bar, 2 coal (3) |
| Iron helmet (hat) | 2 | 3 iron bar, 2 monster bone (2) |
| Boss crown (hat) | 6 | drop from the Golem King |
| Iron greaves (pants) | 3 | 4 iron bar, 2 copper bar (2) |

A full iron set is 10 and a crown set is 14. Mitigation is attack minus defense, minimum 1:

| Monster attack | Against 0 | Against leather (2) | Against iron set (10) | Against crown set (14) |
|:--|:--|:--|:--|:--|
| 5 (spore shroom) | 5 | 3 | 1 | 1 |
| 18 (goblin scout) | 18 | 16 | 8 | 4 |
| 23 (skeleton, magma, golem) | 23 | 21 | 13 | 9 |

## Potions

| Item | Restores | Share of max HP, bands 1–4 | Recipe |
|:--|:--|:--|:--|
| Health potion | 45 HP | 38%, 32%, 30%, 26% | 2 slime gel, 1 fiber |
| Mana potion | 35 MP | — | spore cap, mana crystal |
| Elixir | 100 HP, 50 MP, Regeneration (5 HP a tick, 15 ticks) | 100 HP plus 75 over the ticks | health + mana potion, treasure gem |
| Heart crystal | 80 HP, 25 MP, Regeneration (8 HP a tick, 20 ticks) | 80 HP plus 160 over the ticks | health potion, golem core, 2 slime gel |

The health potion's share is 26–38%. The target was 25–40%, and the test locks it.

## Experience

Combat XP per kill is unchanged by this retune; an elite gives 2.5 times the species XP. The cumulative table (SkillRules, unchanged) reads:
380 (level 2), 770 (3), 1300 (4), 2150 (5), 3300 (6), 4800 (7), 6900 (8), 10000 (9), 15000 (10).

| Band | Full clear XP (expected, with elites) | Cumulative | Level reached | Reference level |
|:--|:--|:--|:--|:--|
| 1 | 459 | 459 | 2 | 2 |
| 2 | 939 | 1398 | 4 | 4 |
| 3 | 1720 | 3118 | 5 | 5 |
| 4 | 2551 | 5669 | 7 | 7 |

Monsters now take 3 to 4 times as long to kill, so XP per hour falls. This is not modelled; see Known gaps.

## Ore, gems and other drops

Sell prices are unchanged by this retune. The shipping bin pays for produce only. Ore, bars and drops are sold on the market; the
blacksmith sells ore to players, at 75 for copper ore and 150 for iron ore. Ore nodes are planned per floor
(`MineFloorPlanner`: 3 + reachable ÷ 120 copper nodes and 2 + reachable ÷ 120 + (floor − 20) ÷ 5 iron nodes), each giving 1 to 3 ore.
A furnace turns 5 ore into 1 bar.

| Item | Source | Sell price (coins) |
|:--|:--|:--|
| Copper ore | copper nodes (floors 1–39) | 2 |
| Iron ore | iron nodes (floors 20–40) | 4 |
| Coal | stones (about half of them) | 4 |
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

## Before and after

| Measure | Before (first retune) | After |
|:--|:--|:--|
| Normal monsters, hits to kill (reference weapon) | 1–2 | 3–4 (3 except the spore shroom) |
| Normal monsters, taken per kill | 0–7% | 10–17% (spore shroom band 2: 1.4%) |
| Normal monsters, hits to die | 15–140 | 12–20 (spore shroom band 2: 140) |
| Normal monster time to kill | 0.44–0.88 s | 1.32 s (1.76 s for the shroom) |
| Bosses, time to kill | 3.1–3.5 s | 22 s |
| Bosses, taken (slams included) | 40–60% without slams | 53–59% with slams |
| Health potion | 50 HP, 29–42% | 45 HP, 26–38% |
| Species break | 25 (not a biome tier) | 20 and 30 (ADR-070 tiers) |
| Full clear of the mine | about 5,700 XP (2.6 clears to level 10) | unchanged, but kills take longer |
| Drops, sell prices | ore, gems and bones sell for 2 to 35 | unchanged |

## Known gaps

The coordinator's targets are not all reachable at once. These are the conflicts and what the tables do about them.

- **Hits to die (6–10) conflicts with taken per kill (10–20%).** With 3-hit kills there are two counters, so taken per kill is about 2 ÷ hits to die.
  At 6–10 hits to die, a kill costs 20% to 33%. At 10–20%, hits to die is 10 to 20. The tables lock 10–20% per kill, which gives 12–20 hits
  to die, and the test asserts at least 10.
- **Spore shroom in bands 1 and 2.** One species, two armor and weapon sets. No HP and attack meets both bands' 10% floor. Band 2 takes 1.4%.
- **Healing is too expensive at 10–20% per kill.** A band 3 clear costs about 29 health potions, about 58 slime gel; band 3's slimes supply about 19. Either
  potions must get cheaper, or the regeneration policy must change. A regeneration or rest rule is a new mechanic and needs its own ADR.
- **Drops do not pay for the next band's gear in 3 to 5 trips.** Not changed in this retune. A band 1 clear's drops (primary items, elite gems, ore)
  are worth roughly 600 coins; band 2's gear costs about 400 at sell value. That is under one trip, not 3 to 5. Cutting the numbers to hit 3–5 trips
  means gems near 3 coins and ore near 0, which the integer price model can't reach cleanly. This needs a design decision.
- **Bosses are long fights, not dangerous hits.** The boss attack is low (2 to 11) because 22 s of counters and slams would otherwise be lethal.
  The slams carry the damage. Dodging the telegraph is not modelled here.
- **Combo** is left out on purpose: the sustained combo of 10 hits multiplies damage by 1.45, so fights are about a third shorter.
- **Elite fights** are not in the reference table. An elite has 1.0 to 1.5 times the HP, so it takes about that many times the hits.
