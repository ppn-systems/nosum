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
