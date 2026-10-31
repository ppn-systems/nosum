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

## Amendments

### 2026-10-31 - cooldowns are taken with the cast; refused casts show nothing (D5)

Reported as skills that could be pressed over and over and "looked fired but did nothing".

- **D5, server.** `CombatService.CastSkillAsync` checks the cooldown again inside the vitals lock that applies the effect, and takes it there
  (`SkillCooldownRules.IsReady` and `ReadyAfterCast`, `Nosum.Gameplay.Combat`). Two casts that race therefore cannot both land; before this the check and the
  write were on different sides of the awaits. Every refusal (`OnCooldown`, `NotEnoughMp`, `OutOfRange`, `AlreadyFull`, `UnknownMonster`) returns before the
  cooldown is written, so it spends nothing and starts no cooldown. `OnCooldown` carries the remaining milliseconds. No wire change.
- **D5, client (`CombatChannel`, `PlayerActions.Combat`).** A press the client's own rules already see as refused starts no swing: the cooldown, the MP, a
  reply still pending, a Fireball or Whirlwind with no monster in range, Divine Heal at full health. A cast is pending from the send until its reply, and a
  second press meanwhile does nothing. The skill effect (`TriggerSkillEffect`) plays on `CombatStatus.Ok` only. A refused cast stops its wind-up swing
  (`OnSkillCompleted`), so no skill animation shows for a cast the server did not accept.
- **D5, overlay.** The hotbar's skill button shows a cooldown sweep (`.skill-cooldown`, `--cd-ms` from `ProgressViewModel.CooldownStyle`) and stays
  disabled until the cooldown ends. The sweep runs over the cooldown the server started: the full cooldown after a cast, the time left after a refusal
  as `OnCooldown` (`CombatChannel.GetSkillCooldownSpanMs`). Before this a refusal swept the full cooldown, so the overlay outlasted the server's.
  Reduced motion keeps the veil without the animation.
- **D5, messages.** Every refusal a cast can get has a message (`SkillRefusalText.KeyFor`, `Combat_Refused_*` and `Combat_NotEnoughMp` in `.resx`, Vietnamese
  and English), shown through the game messages when the reply arrives. Before this only `NotEnoughMp` had a key and nothing showed it.
- **D5, line of sight.** `LineOfSight.IsClear` returned false for a target on the attacker's own tile, so a skill could not hit a monster standing on the
  player's tile (reported as `OutOfRange`). A tile has nothing strictly between it and itself, so it is clear.
- **Tests.** `SkillCooldownRulesTests` (unit: readiness at the instant, the length of a cooldown, remaining time), `SkillRefusalTests` (unit: every refusal has
  a message in both languages, the sweep span after a refusal and after a cast), and `CombatWireTests` (`tests/Nosum.IntegrationTests/Server`, over the real
  WebSocket: `OutOfRange`, `AlreadyFull`, `NotEnoughMp`, `OnCooldown` with the time left, and two casts sent together of which only one lands).
