# ADR-061: Dungeon Environmental Hazards and Elemental Traps

## Status
Accepted

## Context
Dungeon floors (`mine1`–`mine40`) previously relied exclusively on roaming monsters and milestone floor bosses for combat danger. To elevate dungeon exploration into a richer MMORPG tactical experience, subterranean floors need environmental hazards and elemental traps that scale by depth tier and challenge player positioning and movement timing.

## Decision
1. **Tiered Dungeon Hazard Taxonomy (`DungeonHazardKind` & `DungeonHazard`)**:
   - `SporeVent` (`1`): Fungal toxic vents on shallow cavern floors (`mine1`–`mine10`). Inflicts 4 base damage on contact and applies `StatusEffectKind.Poison` (3 damage per tick over 4 ticks).
   - `SpikeTrap` (`2`): Ancient mechanical spike plates on mid-tier floors (`mine11`–`mine20`). Cycles on a 2.4-second period, thrusting sharp steel spikes during the active window for 8 base damage.
   - `MagmaFissure` (`3`): Boiling lava cracks and magma geysers in deep subterranean floors (`mine21`–`mine40`). Inflicts 14 base fire damage on contact.
2. **Deterministic Floor Planning (`CombatRules.PlanHazards`)**:
   - Uses `NodeRandom.Mix(seed, 0x48415A44, floor)` to deterministically place `2..6` hazards per mine floor on open walkable tiles at least 4 tiles away from the player's arrival point (`spawnX`, `spawnY`).
3. **Append-Only Wire Protocol Extension**:
   - Added `HazardRow` (`HazardId`, `Kind`, `X`, `Y`, `Damage`) in `Nosum.Contracts.Combat.S2C`.
   - Extended `MonstersSnapshot` (`0xC241`) append-only with `[SerializeOrder(2)] public List<HazardRow> Hazards { get; set; } = []`.
4. **Server-Authoritative Contact Evaluation**:
   - `CombatService` stores planned hazards per floor instance and evaluates player tile contact in the periodic AI/environment tick loop (`RunAiAsync`), applying armor mitigation (`CombatRules.MitigateDamage`), status effects, knock-out revival, and pushing `VitalsState` updates.
5. **Zero-Allocation Client Rendering**:
   - `CombatChannel` caches `HazardClientRow` entries from `MonstersSnapshot` and exposes `CopyHazards` without heap allocations.
   - `MonstersDecorator` renders pulsing emerald spore clouds, cycling steel spike plates, and boiling crimson/gold magma fissures directly into the `WorldSpriteBatch` using pre-allocated scratch buffers.

## Consequences
- Every dungeon floor tier features distinct visual and mechanical environmental threats.
- Wire compatibility is preserved via append-only `[SerializeOrder(2)]` on `MonstersSnapshot`.
- Zero heap allocations in both server tick loops and client frame rendering.
