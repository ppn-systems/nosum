# ADR-058: Elite Monster Modifiers & Affixes

## Status

Accepted

## Context

To provide depth, tactical variety, and high-stakes risk/reward typical of classic MMORPGs, monsters on mine floors and dangerous wilderness areas required dynamic variations beyond static species stats. Players engaging monsters needed visual clarity regarding enemy threat levels, distinct combat mechanics (such as damage reflection, life leech, and damage mitigation), and augmented rewards for vanquishing champion-tier foes.

## Decision

1. **Affix Modifier Taxonomy (`MonsterModifier`)**:
   - `None = 0`: Standard creature.
   - `FlameAura = 1` ("★ CUỒNG HỎA ★"): Fiery aura with melee damage recoil and +25% bonus HP.
   - `Swift = 2` ("★ TỐC BIẾN ★"): Rapid pursuit and heightened responsiveness.
   - `Shielded = 3` ("★ HỘ GIÁP ★"): Hardened defense mitigating 25% of incoming player attack/skill damage with +50% bonus HP.
   - `Vampiric = 4` ("★ HUYẾT THỆ ★"): Crimson aura with life-stealing contact attack that restores monster health on hit with +20% bonus HP.

2. **Deterministic Placement & Server Authority**:
   - Integrated into `CombatRules.PlanMonsters`: deterministic roll based on map instance seed ensuring consistent multiplayer synchronization across instances.
   - `MonsterSpawn` record extended with `Modifier` (defaulting to `None` for backward compatibility).
   - Server-authoritative damage calculation, life leech, and skill resolution implemented in `CombatService`.
   - Slain Elite monsters award 2.5x Combat XP, guaranteed rare drops, and bonus treasure gems (`ItemCatalog.TreasureGem`).

3. **Zero-Allocation Client Aesthetics**:
   - Wire format extended append-only: `MonsterRow` (`SerializeOrder(6)`) and `MonsterChanged` (`SerializeOrder(6)`).
   - Pre-interned floating affix titles in `CombatLabels` with zero garbage collection allocations.
   - Dynamic pulsing elemental aura rings rendered beneath Elite monsters (`MonstersDecorator`) with distinctive hue signatures: fiery orange-red for FlameAura, cyan for Swift, golden crystalline for Shielded, and deep crimson for Vampiric.
   - Overhead mini-health bars always visible for Elite monsters.

## Consequences

- Combat encounters feature immediate visual recognition of hazardous elite threats.
- High-level dungeon diving and farming rewards skilled players with substantial XP and valuable crafting gems.
- The wire format and rendering loops strictly preserve zero allocation and backward compatibility.
