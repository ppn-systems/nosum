# ADR-050: Monster Defeat Death Poof Animations and Particles

## Status
Accepted

## Context
When monsters in the wilderness or dungeon floors are slain, their instantaneous removal without a visual transition feels abrupt. Classic 16-bit RPGs deliver satisfying visual feedback upon monster defeat through an animated puff of smoke/dust or elemental explosion that disperses over several hundred milliseconds.

In Round 1, CC0 sprite sheets were integrated from `art/source/dawnlike/Objects/Effect0.png`. Rows 6, 9, and 10 contain 4-frame animation sequences for fire/spark bursts, smoke clouds, and expanding dust poofs.

## Decision
1. **Sprite Atlas Extension**:
   - Extended `tools/art_repack/monsters.py` and `sprites/monsters.png` with Block 12 (`y = 576..623`, 64 x 48 px), packing three 4-frame defeat sequences:
     - Row 0: Smoke poof (Row 9 in `Effect0.png`) for standard slimes, bats, and goblins.
     - Row 1: Dense dust burst (Row 10 in `Effect0.png`) for earth/skeleton creatures.
     - Row 2: Fire/spark explosion (Row 6 in `Effect0.png`) for magma creatures and milestone floor bosses.
2. **Client-Side Effect Buffer**:
   - Added `DeathEffectClientRow` in `CombatChannel.cs` tracking `(X, Y, StartTime, DurationMs = 400f, Kind)`.
   - Populated automatically in `OnMonsterChanged` and `UpdateMonsterHp` whenever a monster's remaining HP transitions to 0.
   - Cleaned up without GC allocations via `CopyDeathEffects(List<DeathEffectClientRow>, long now)`.
3. **Rendering in MonstersDecorator**:
   - In `MonstersDecorator.AddToWorld(WorldSpriteBatch world)`, animated death poofs interpolate over 4 frames using `Math.Clamp((int)(elapsed / 100f), 0, 3)`.
   - Rendered at the monster's tile position in the world sprite layer above terrain with unique sorting keys.

## Consequences
- Defeated monsters burst into animated smoke/explosion puffs rather than vanishing instantaneously.
- Zero allocations in the render loop or network packet handling.
- Asset provenance strictly maintained using DawnLike CC0 assets.
