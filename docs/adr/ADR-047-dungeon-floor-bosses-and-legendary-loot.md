# ADR-047 — Dungeon Floor Bosses, Multi-Phase Health, and Legendary Drops

- **Status**: Decided
- **Date**: 2026-10-28
- **Author**: Antigravity Agent & phcnguyen
- **Area**: `gameplay/Nosum.Gameplay`, `server/Nosum.Infrastructure`, `engine/Nosum.Rendering`
- **Decides**: How milestone mine floors (10, 20, 30, 40) spawn deterministic dungeon floor bosses with scaled stats, enrage behaviors, and unique legendary trophy loot.

---

## 1. Context and Problem Statement

Following ADR-045 (MMORPG combat & vitals) and ADR-046 (Monster AI roaming and contact aggro), players exploring the 40-floor mine progression require climactic milestone challenges at elevator checkpoint floors (10, 20, 30, 40).

The mine currently generates identical trash mobs across tiers. Players reaching milestone elevator floors (Floors 10, 20, 30, 40) need distinct, memorable Boss encounters with:
1. Server-authoritative multi-phase health pools and high contact damage.
2. Extended aggro pursuit range and enraged speed.
3. Exclusive rare boss trophy drops (`AncientScroll`, `GoldenKey`, `ColossusCore`, `BossCrown`).
4. Distinct animated sprites sourced from the open-licensed CC0/CC-BY library (`oga_trash-mobs`, `oga_tiny-creatures`, `oga_gold-treasure-icons`).

## 2. Decision Drivers

- **Zero Allocations**: Game loop and packet handlers must remain allocation-free.
- **Server Authority**: Monster spawns, boss HP, aggro tracking, combat damage, and drops are computed entirely on the server.
- **Milestone Checkpoints**: Floors 10, 20, 30, 40 correspond to mine elevator milestones (ADR-026).
- **Art Provenance**: All boss art and drop icons utilize the 27 downloaded CC0/CC-BY packs in `art/source/`.

## 3. Considered Options

1. **Option A (Separate boss entity class & database table)**: Heavy database persistence and distinct entity hierarchies.
2. **Option B (Pure functional species scaling on existing MonsterKind & MonsterSpawn)**: Extend `MonsterKind` enum with 4 Boss species (9..12), define boss stats in `MonsterCatalog`, deterministic placement in `CombatRules.PlanMonsters`, and update `sprites/monsters.png` to 12 blocks.

## 4. Decision Outcome

Chosen **Option B**:
- **Boss Species**:
  - Floor 10: `BossBroodmother` (Giant Arachnid/Bat Matriarch, HP: 180, Contact Damage: 15, Drops `AncientScroll` & `BatWing`).
  - Floor 20: `BossGoblinChieftain` (Armored Goblin Warlord, HP: 280, Contact Damage: 22, Drops `GoldenKey` & `GoblinEar`).
  - Floor 30: `BossMagmaColossus` (Molten Fire Colossus, HP: 420, Contact Damage: 30, Drops `ColossusCore` & `TreasureGem`).
  - Floor 40: `BossGolemKing` (Ancient Golem Sovereign, HP: 600, Contact Damage: 40, Drops `BossCrown` & `GolemCore`).
- **Boss AI**:
  - `CombatRules.BossAggroRadius = 6` (vs standard 4 tiles).
  - Enraged state when HP drops below 50%: aggressive double-step pursuit.
- **Loot Table & Items**:
  - `BossCrown` (ID 518, sell: 2500g, ObjectIcon: 113 from `gold_treasure`).
  - `AncientScroll` (ID 519, sell: 800g, ObjectIcon: 106 from `roguelike_rpg`).
  - `GoldenKey` (ID 520, sell: 1200g, ObjectIcon: 114 from `gold_treasure`).
  - `ColossusCore` (ID 521, sell: 1800g, ObjectIcon: 115 from `trash_mobs`).
- **Repack Pipeline**:
  - `tools/art_repack/monsters.py` expanded from 8 to 12 blocks (64 x 576 px).
  - `tools/art_repack/icons.py` updated with icons 113, 114, 115.

## 5. Consequences

### Positive
- Milestone elevator floors offer thrilling boss fights and valuable progression rewards.
- Seamless compatibility with existing client networking packets (`MonsterRow`, `MonsterChanged`, `AttackMonster`).
- Rich utilization of downloaded CC0 asset sheets.

### Negative / Trade-offs
- `sprites/monsters.png` texture height increases from 384px to 576px (+48% size, ~1.5 KB payload).

## Amendment — boss balance (2026-10-31)

- **Amends:** the boss HP and contact damage in the Decision list. Cave broodmother 180 HP / 15 contact → 1200 HP / 2 contact; Goblin
  chieftain 280 / 22 → 2000 / 7; Magma colossus 420 / 30 → 2900 / 7; Golem king 600 / 40 → 4100 / 11. A boss now takes 20–40 s to
  kill and its slams carry the damage. Boss XP is 75, 125, 200 and 300. Enrage thresholds are unchanged.
- **Reason:** with the reference player of each band, the broodmother took 90% of HP and the Golem king killed the player; the table and
  the targets are in [ADR-045, D4–D5](ADR-045-mmorpg-combat-hp-mp-and-monster-assets.md) and `docs/design/combat-balance.md`.
- **Drops:** the catalog is the source of truth. The floor-10 boss drops the Ancient Scroll and a Necromancer Skull, and the floor-40 boss
  drops the Boss Crown and a Runestone Tablet, not the Bat Wing and Golem Core this ADR lists.
