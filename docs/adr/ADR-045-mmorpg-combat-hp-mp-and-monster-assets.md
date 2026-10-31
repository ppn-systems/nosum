# ADR-045: MMORPG Combat, HP & MP Vitals, Monster Encounters, and Full Art Library Repack

- **Status:** Accepted
- **Date:** 2026-10-27
- **Amends:** [ADR-021](ADR-021-tools-resource-nodes-ground-items.md), [ADR-023](ADR-023-energy-skills-food.md), [ADR-026](ADR-026-mine-tool-tiers-blacksmith.md), [ADR-040](ADR-040-own-art-plan.md)

## Context

1. **Art library utilization:** Twenty-seven open-licensed 16 px packs sit in `art/source/` (`docs/art/library.md`), but nineteen of them were marked `"Not used yet"`, including animated monster packs (`oga_animated-slimes`, `oga_trash-mobs`), RPG weapon and item sets (`oga_weapon-rpg-icons`, `oga_assorted-rpg-icons`, `oga_16x16-rpg-items`, `oga_gold-treasure-icons`), dungeon sets (`oga_puny-dungeon`, `oga_16x16-dungeon-tiles`, `oga_6-color-dungeon`, `kenney_roguelike-caves-dungeons`, `kenney_roguelike-indoors`, `kenney_roguelike-rpg-pack`, `kenney_roguelike-characters`, `kenney_tiny-battle`, `tiny16`), urban/1-bit sets (`kenney_roguelike-modern-city`, `kenney_rpg-urban-pack`, `kenney_1-bit-pack`), and animal sets (`oga_random-animals`, `oga_pixel-animals-16x16`).
2. **MMORPG combat & vitals:** The Mine (`mine1`–`mine40`) and wilderness maps (`Woods`, `Mountain`) only contained static rocks and trees. Players lacked HP (health points), MP (mana points), weapons, potions, monster loot, a Combat skill, and real-time monster encounters.

## Decision

### D1 — Repack all 27 packs in `art/source/` into `assets/`

- Register all 27 packs and their source sheets (with pinned SHA-256 hashes) in `tools/art_repack/sources.py`, copy their license texts to `docs/art/licenses/<pack>.txt`, and record every cell mapping in `tools/art_repack/provenance.py` so `docs/art/provenance.md` and `ArtCredits.g.cs` list and credit every pack.
- Build `assets/sprites/monsters.png` (`64 x 384`, eight 4-column x 3-row directional animation blocks of 16 x 16 frames: Green Slime, Frost Slime, Magma Slime, Cave Bat, Spore Shroom, Goblin Scout, Skeleton Warrior, Stone Golem) via `tools/art_repack/monsters.py` from `oga_animated-slimes`, `oga_trash-mobs`, `oga_tiny-creatures`, and `kenney_tiny-dungeon`.
- Populate `assets/sprites/weapons.png` with melee swords and arcane staves from `oga_weapon-rpg-icons`, `oga_assorted-rpg-icons`, `kenney_tiny-dungeon`, and `kenney_roguelike-characters`.
- Populate the remaining cells of `assets/sprites/items.png` (`384 x 80`, 120 cells) with potions, monster drops, gems, shields, books, and relics from `oga_assorted-rpg-icons`, `oga_gold-treasure-icons`, `oga_16x16-rpg-items`, `oga_puny-dungeon`, `oga_16x16-dungeon-tiles`, `oga_random-animals`, `oga_pixel-animals-16x16`, `kenney_tiny-battle`, `kenney_roguelike-caves-dungeons`, `kenney_roguelike-indoors`, `kenney_roguelike-rpg-pack`, `kenney_roguelike-modern-city`, `kenney_rpg-urban-pack`, `kenney_1-bit-pack`, `tiny16`, and `oga_6-color-dungeon`.

### D2 — Player HP, MP, Combat skill, weapons, potions, and monster drops

- **Vitals (`CombatRules`):**
  - Base `MaxHp = 100` (`+10` per Combat level, up to `200` at level 10).
  - Base `MaxMp = 50` (`+5` per Combat level, up to `100` at level 10).
  - `SkillKind.Combat = 4` uses the same 10-level XP table (`SkillRules`) as Farming, Foraging, and Mining.
- **Weapons (`ItemKind.Tool`, `ToolKind.Sword = 7`, `ToolKind.Staff = 8`):**
  - `WoodenSword` (`30`, 12 dmg, included in `StarterKit` slot 8), `BronzeSword` (`31`, 20 dmg), `IronSword` (`32`, 32 dmg), `SteelSword` (`33`, 48 dmg), `GoldSword` (`34`, 68 dmg), and `ArcaneStaff` (`35`, 45 magic dmg, costs 10 MP, range 3 tiles).
- **Potions (`ItemKind.Food`, IDs `710–712`) & Monster Drops (`ItemKind.Resource`, IDs `510–517`):**
  - `HealthPotion` (`710`, restores 50 HP + 15 Energy), `ManaPotion` (`711`, restores 35 MP + 15 Energy), `Elixir` (`712`, restores 100 HP + 50 MP + 60 Energy).
  - Monster drops: `SlimeGel` (`510`), `BatWing` (`511`), `SporeCap` (`512`), `GoblinEar` (`513`), `MonsterBone` (`514`), `GolemCore` (`515`), `ManaCrystal` (`516`), `TreasureGem` (`517`).
  - Crafting recipes (`RecipeCatalog`) allow crafting upgraded weapons and potions from bars, forage, and monster drops.

### D3 — Server-authoritative monsters and combat (`Nosum.Contracts.Combat`, `ICombatService`)

- **Spawning:** Deterministic floor/map monster rosters (`MonsterCatalog`, `MonsterFloorPlanner`) spawn monsters on Mine floors (`mine1`–`mine40`, scaling from Green Slimes and Cave Bats on floors 1–10 to Frost Slimes, Spore Shrooms, and Goblins on floors 11–25, and Magma Slimes, Skeleton Warriors, and Stone Golems on floors 26–40) as well as wilderness maps (`Woods`, `Mountain`).
- **Combat authority:**
  - Client sends `AttackMonster` (`MonsterId`, `Slot`) when swinging a weapon or casting a staff within reach.
  - Server validates map, distance, weapon/staff in `Slot`, and MP cost (for staves), applies damage (`CombatRules.CalculateDamage`), awards `CombatXp` and spawns ground-item loot (`IGroundItemService`) when a monster is defeated, or applies monster counter-attack damage to player `Hp`.
  - If player `Hp` reaches `0`, the player is knocked out and revived at `50%` HP without losing tools or inventory items.
  - `DrinkPotion` (`Slot`) consumes a potion from the hotbar/inventory and restores `Hp` and `Mp`.
- **Rendering & HUD:**
  - `MonstersDecorator` (`IMapDecorator`) renders live monsters with directional walk/idle frames and floating pixel HP bars above injured monsters with zero per-frame allocations.
  - `EnergyBar.razor` renders three side-by-side vertical pixel gauges (`HP` crimson, `MP` sapphire, `Energy` emerald) next to the hotbar, and `SkillsPanel.razor` displays Vitals (`HP`, `MP`, `Energy`) and the `Combat` skill progression.

## Amendment — combat balance targets (2026-10-31)

- **Status:** Proposed. Owner decision pending on acceptance.
- **Amends:** D3 (the species breaks in the spawning paragraph are now floors 1–10, 11–20, 21–30 and 31–40, not 1–10, 11–25 and 26–40), and the catalog values of D2 and D3.
- **Full tables:** [`docs/design/combat-balance.md`](../design/combat-balance.md). Locked by `tests/Nosum.UnitTests/Gameplay/CombatBalanceTests.cs`.

### Context

An audit of the mine and the combat loop against its own numbers found: full iron armor left every monster from floor 11 dealing one damage;
the boss on floor 10 cost the reference player 90% of HP and the Golem King on floor 40 killed them; the species break at floor 25 did not
match the biome tiers (ADR-070); the Goblin Ear had no recipe and no sell price; ore, gems, bones and gel could not be sold; and one full clear
of the mine took a Combat level 10 player about 1.2 days of XP. No new mechanic, monster, item id or packet is needed to fix these.

### D4 — Balance targets

Each mine band has a reference player: the weapon, Combat level and armor it is expected to carry (band 1 bronze, level 2, leather; band 2
iron, level 4, iron set; band 3 steel, level 5, iron set; band 4 gold, level 7, crown set). Against that player:

- Normal monsters take 3–5 hits to kill (elites 5–8), and one kill costs 10–20% of max HP without dodging. Band 2's spore shroom is the
  exception (1.4%): it is one species in bands 1 and 2, and no single HP and attack meets both.
- A boss takes 20–40 s to kill and costs 40–70% of max HP when its slams are not dodged. The tables use 22 s and 53–59%.
- The armor adds at least 5 hits of survival against the band's strongest normal monster, which takes at least 10 hits to kill a wearer.
  The 6–10 hits-to-die target is not met: with 3-hit kills, 10–20% per kill implies 10–20 hits to die.
- Each melee weapon tier is at least 40% stronger than the tier below.
- Every monster's primary drop has a sell price. The mean primary-drop value per band stays within 4–10 (band 1), 4–12 (band 2),
  6–12 (band 3) and 10–20 (band 4) coins.
- One full clear of a band leaves the reference player at that band's Combat level. One full clear of the mine is 2 to 3.5 clears to Combat level 10.
- A health potion restores 25–40% of each band's max HP.

These are locked by `tests/Nosum.UnitTests/Gameplay/CombatBalanceTests.cs`. The 3-potions-per-trip and 3-to-5-trips-of-gear targets are not
met; see `docs/design/combat-balance.md`, Known gaps.

### D5 — Catalog changes

- **Species** (`MonsterSpawnPlanner.SpeciesForMineFloor`): the breaks follow the biome tiers (ADR-070): 1–10, 11–20, 21–30, 31–40.
  Band 3 is magma slime, skeleton warrior and goblin scout; band 4 is stone golem, skeleton warrior and magma slime.
- **Monster HP and attack** (`MonsterCatalog`): green slime 24/5 → 60/8; cave bat 30/7 → 60/8; spore shroom 40/8 → 82/5;
  frost slime 45/13 → 82/17; goblin scout 60/16 → 118/18; magma slime 75/18 → 166/23; skeleton warrior 90/20 → 166/23;
  stone golem 140/24 → 166/23.
- **Boss HP and attack:** cave broodmother 180/10 → 1200/2; goblin chieftain 280/17 → 2000/7; magma colossus 420/19 → 2900/7;
  golem king 600/24 → 4100/11.
- **Monster and boss XP** halved in the first retune and unchanged since: green slime 6, cave bat 8, spore shroom 10, frost slime 11,
  goblin scout 15, magma slime 18, skeleton warrior 22, stone golem 35, bosses 75/125/200/300.
- **Armor defense** (`CombatRules.ArmorDefense`): leather armor 2, ranger tunic 3, iron chestplate 5, iron greaves 3, iron helmet 2,
  boss crown 6.
- **Health potion** (`CombatRules.PotionRestore`): 50 HP → 45 HP.
- **Sell prices:** slime gel 4, bat wing 8, spore cap 8, goblin ear 12, monster bone 12, golem core 35, mana crystal 30, treasure gem 35,
  coal 4, copper ore 2, iron ore 4, copper bar 10 and iron bar 20 (a furnace turns 5 ore into 1 bar, so a bar sells for five times its ore).

Unchanged: weapon damage, mana and elixir potions, spawn counts, the XP curve (`SkillRules`), the armor formula, the sword pacing,
the elite rules and the boss floors.

### Consequences

- Positive: normal fights are no longer trivial (3 hits, 10–20% of HP per kill), no boss is lethal, bands follow the biome tiers, every drop has a use or a value, and the XP curve matches the reference levels.
- Known gap, not changed here: there is no natural HP regeneration. A full band clear of normal fights costs 384 to 1286 HP against a reference
  player with 120 to 170 max HP, and band 3 needs about 29 health potions. Healing is potions, shrines and the 25% knockout revive. A
  regeneration or rest rule is a new mechanic and needs its own ADR.
- Negative: market listings for ore and gems now exist, which the economy must watch (the market fee and the listing caps stay as they are).

### Revisit when

The reference player changes (a new weapon tier, a new armor set or an XP change), a regeneration rule is decided, or the playtest shows that
band 3 healing is too tight.
