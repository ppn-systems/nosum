# ADR-060: Combat Combo Streaks and Critical Damage Scaling

## Status
Accepted

## Context
In fast-paced MMORPG combat, player responsiveness and rhythmic engagement are substantially enhanced when consecutive strikes reward timing with escalation. Previously, attack and skill damage calculations were flat based strictly on weapon strength, combat skill level, and monster defense mitigation. Players lacked feedback encouraging dynamic, relentless offensive chains.

## Decision
1. **Server-Authoritative Combo Tracker**:
   - `CombatRules.ComboTimeoutMs = 3500`: Strikes landing within 3.5 seconds increment the combo count.
   - `CombatRules.MaxComboCount = 10`: Capped at a 10-hit streak.
   - `CombatRules.ComboBonusPercent(comboCount)`: Returns a +5% damage multiplier per hit (`(combo - 1) * 5%`), up to +45% bonus damage at maximum streak.
   - `CombatRules.CalculateDamage(itemId, combatLevel, comboCount)`: Incorporates the combo bonus into base weapon damage calculations before monster mitigation.
2. **Wire Format Append-Only Extension**:
   - `AttackMonsterResult`: Appended `[SerializeOrder(11)] public byte ComboCount { get; set; }`.
   - `CastSkillResult`: Appended `[SerializeOrder(11)] public byte ComboCount { get; set; }`.
   - Preserves binary backwards compatibility with existing serialized buffers.
3. **Client-Side Reactive HUD & Floating Numbers**:
   - `CombatChannel`: Tracks `CurrentCombo` and fires `ComboChanged`.
   - Floating Combat Text automatically promotes damage numbers to Style 1 (Golden Critical Text) when `ComboCount >= 3`.
   - `Hotbar.razor`: Dynamic `.combat-combo-badge` appears above the player hotbar dock, glowing and pulsing with flame gradients (`🔥 xN COMBO (+M%)`) and fading when the streak resets.

## Consequences
- Engaged combat rewards rhythmic pacing and active skill weaving.
- Zero heap allocations in server hot paths using thread-safe state dictionaries.
- Full parity between basic weapon attacks and special skill casts.
