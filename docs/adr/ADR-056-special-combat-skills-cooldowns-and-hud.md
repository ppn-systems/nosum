# ADR-056: Special Combat Skills, Server-Authoritative Cooldowns, and Skills HUD

## Status
Accepted

## Context
In addition to basic weapon swings and staff projectiles, classic MMORPG gameplay revolves around distinctive tactical skills with varied MP costs, tactical areas of effect, and cooldown timings. Players need a visible skill bar dock on the in-game HUD with quick keyboard triggers [Z, X, V] and touch/mouse click actions, complete with server authority on cooldown tracking and MP validation.

## Decision

### 1. Pure Skill Catalog (`CombatSkillKind`, `SkillDefinition`, `SkillCatalog`)
- Defined three core combat skills in `gameplay/Nosum.Gameplay/Combat/SkillCatalog.cs`:
  - **Whirlwind Cleave (`SkillKind.Whirlwind = 1`)**:
    - MP Cost: 15
    - Cooldown: 3000 ms
    - Base Power: 18 (physical weapon strike)
    - Range: 2 tiles radius (Area of Effect)
  - **Fireball Pyromancy (`SkillKind.Fireball = 2`)**:
    - MP Cost: 25
    - Cooldown: 4000 ms
    - Base Power: 32 (high magical fire burst)
    - Range: 6 tiles (single target projectile)
  - **Divine Heal (`SkillKind.DivineHeal = 3`)**:
    - MP Cost: 20
    - Cooldown: 6000 ms
    - Base Power: 35 (holy restoration)
    - Range: Self

### 2. Append-Only Wire Packets (`CastSkill`, `CastSkillResult`)
- Added opcodes in `Nosum.Contracts.Combat.CombatOpCode`:
  - `CastSkill`: `0xC204` (C2S)
  - `CastSkillResult`: `0xC245` (S2C)
- Added status codes in `CombatStatus`:
  - `OnCooldown = 7`
  - `UnknownSkill = 8`
- `CastSkillResult` returns updated `DamageDealt`, `TargetsHit`, `HealAmount`, `Hp`, `MaxHp`, `Mp`, `MaxMp`, `CombatXp`, and `CooldownMs`.

### 3. Server-Authoritative Execution (`CombatService.CastSkillAsync`)
- Cooldowns are tracked in a thread-safe registry: `ConcurrentDictionary<(Guid PlayerId, byte SkillId), long> _skillCooldowns`.
- Rejects skill requests if the player is on cooldown (`Status = OnCooldown`) or lacks necessary MP (`Status = NotEnoughMp`).
- For Whirlwind: damages all live monsters within a Chebyshev distance of 2 tiles, broadcasting updates and awarding Combat XP.
- For Fireball: launches a pyromancy burst at the target monster within 6 tiles, triggering a projectile trajectory.
- For Divine Heal: recovers player HP capped at `MaxHp`.

### 4. Client SkillBar HUD & Keyboard Binding
- Added `KeyZ` (1 << 21), `KeyX` (1 << 22), `KeyV` (1 << 23) in `engine/Nosum.Core/InputEdges.cs` and `renderer.js`.
- Added clickable `.combat-skills-dock` beside the hotbar in `wasm/Nosum.Wasm.Controls/Inventory/Hotbar.razor` with visual glyphs (⚔️ Whirlwind, 🔥 Fireball, ✨ Divine Heal) and MP indicators.
- In `PlayerActions.cs`, pressing Z/X/V routes through `CombatChannel.CastSkill` without heap allocation.
- In `CombatChannel.cs`, skill executions spawn floating combat damage/crit/heal indicators (ADR-055) and projectile animations (ADR-048).

## Consequences
- Fast, responsive MMORPG tactical skill rotations with complete server authority.
- Keyboard [Z, X, V] and mouse/touch clicks both trigger combat skills seamlessly.
- Visual clarity reinforced by floating combat text and animated trajectories.
- 100% test coverage maintained across 2,379 tests.
- Visual evidence captured in `docs/qa/mmorpg-round12-*.png`.
