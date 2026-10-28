# ADR-048 — Arcane Magic Projectiles, Trajectory Interpolation, and Impact Sparkles

- **Status**: Decided
- **Date**: 2026-10-28
- **Author**: Antigravity Agent & phcnguyen
- **Area**: `wasm/Nosum.Wasm`, `wasm/Nosum.Wasm.Socket`, `gameplay/Nosum.Gameplay`
- **Decides**: How ranged magic weapons (e.g. Arcane Staff) and monster spells spawn client-side in-flight projectiles with zero-allocation trajectory interpolation and impact sparkle bursts.

---

## 1. Context and Problem Statement

ADR-045 and ADR-047 introduced the `ArcaneStaff` weapon with a range of 4 tiles and 10 MP consumption, as well as dungeon floor bosses with ranged threat capabilities.

Previously, attacking with the `ArcaneStaff` immediately resolved damage on the server without showing a flying projectile. In an MMORPG, ranged spellcasting requires visual feedback:
1. An arcane bolt launching from the player's coordinates towards the monster.
2. Smooth frame-rate independent interpolation along the flight trajectory.
3. Bursting impact sparkle particles upon hitting the target.
4. Audio feedback accompanying projectile launch.

## 2. Decision Drivers

- **Zero Allocation Hot Path**: Projectile animation and drawing within `AddToWorld` must allocate zero heap objects per frame.
- **Client Prediction with Server Authority**: Projectile flight is visually simulated on the client during the attack packet round-trip, aligning seamlessly with the server-authoritative `AttackMonsterResult`.
- **Lightweight Structure**: Projectiles are stored in pooled structs (`ProjectileClientRow`) with automatic time-to-live pruning.

## 3. Decision Outcome

1. **`ProjectileClientRow` Model**:
   - `StartX`, `StartY`, `TargetX`, `TargetY` (float tile coordinates).
   - `StartTime` (Environment.TickCount64 timestamp).
   - `DurationMs` (flight duration, default 260ms).
   - `Kind` (1 = Arcane Bolt, 2 = Fiery Blast, 3 = Spore Orb).
2. **`CombatChannel` Management**:
   - `SpawnProjectile(fromX, fromY, toX, toY, kind, durationMs)` registers active projectiles in a pre-allocated bounded ring buffer.
   - `CopyProjectiles(destination, now)` copies active or recently impacted projectiles without GC pressure.
3. **`MonstersDecorator` World Rendering**:
   - Renders active projectile sprites interpolated along the trajectory line.
   - Upon impact (`progress >= 1.0f`), generates 4 outward-scattering sparkle particles for 180ms.
4. **`PlayerActions` Integration**:
   - When triggering `Attack` with `ItemCatalog.ArcaneStaff`, spawns an arcane projectile directed at `targetMonster`.
   - Triggers magic sound effect via `_audio?.Play(SoundIds.ToolScythe)`.

## 4. Consequences

### Positive
- Ranged spell combat feels responsive, visceral, and visually rewarding.
- High-fidelity magical combat without adding network overhead or server CPU load.
- Zero allocations in the render loop.
