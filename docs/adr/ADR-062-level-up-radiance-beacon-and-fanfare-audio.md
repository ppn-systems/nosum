# ADR-062: Level-Up Radiance Beacon, Full Vitality Surge & Celebratory Fanfare Audio

- **Status:** Accepted
- **Date:** 2026-10-28
- **Amends:** ADR-039, ADR-045, ADR-053

## Context

When a player reaches a new Combat level (or gathering skill level), the transition deserves unmistakable audiovisual feedback and a rewarding gameplay boost:

1. **Full Vitality Restoration on Level-Up**: Previously, advancing a Combat level in `AttackAsync` only added the delta (`+10` HP / `+5` MP), and `CastSkillAsync` did not recalculate `MaxHp`/`MaxMp` on level-up.
2. **World Radiance Beacon & Overhead Banner**: A zero-allocation golden pillar of light, expanding ground halo, ascending starlight sparkles, and rising localized banner (`Combat_Label_LevelUp`) in `MonstersDecorator`.
3. **Celebratory Fanfare Audio Cue (`SoundIds.LevelUp`)**: A deterministic 4-note ascending major arpeggio (`C5–E5–G5–C6`) synthesized by `tools/synth_sounds.py` (`level_up.wav`).
4. **Strict Blazor UI & i18n Compliance**: All combat HUD strips, skill buttons, combo/level badges, bestiary entries, and world labels use shared `Nosum.Wasm.Controls/Common` components (`GamePanel`, `GameRow`, `GameButton`, `GameKey`, `GameBadge`, `GameSection`, `GameStack`) and `.resx` localization (`Language.resx` / `Language.en.resx` and `Catalogue.resx` / `Catalogue.en.resx`).

## Decisions

### D1. Server-Authoritative Vitality Surge (`CombatService`)

When `SkillRules.LevelOf(vitals.CombatXp)` advances in either `AttackAsync` or `CastSkillAsync`:
- `MaxHp` and `MaxMp` are updated via `CombatRules.MaxHpForLevel(newLevel)` and `CombatRules.MaxMpForLevel(newLevel)`.
- `Hp` and `Mp` are restored to `100%` of the new maximums.
- Non-fatal skill hits (`Whirlwind`, `Fireball`) award `SkillRules.HitXp`, and fatal skill hits drop monster loot onto the map instance via `IGroundItemService`.

### D2. Synthesized Fanfare Audio (`tools/synth_sounds.py` & `AudioCatalog`)

- `tools/synth_sounds.py` synthesizes `level_up.wav` (`SoundIds.LevelUp = 20`).
- `AudioWiring.Connect` plays `SoundIds.LevelUp` when `CombatChannel.LevelledUp` or `ProgressChannel.LevelledUp` fires.

### D3. Zero-Allocation World Radiance Beacon (`MonstersDecorator`)

- `CombatChannel.TryGetActiveLevelUp` exposes the 3.8-second celebration window without heap allocation.
- `MonstersDecorator.Append` renders a pulsing golden ground halo (`0xfacc15`), outer/inner celestial pillars of light (`0xfde047` / `0xfef9c3`), 4 spiraling starlight sparkles, and the pre-interned localized `LevelUpLabelId` banner.

## Consequences

- Reaching a new level during combat or gathering triggers an immediate full HP/MP surge, a 4-note fanfare chime, a golden world radiance pillar, and a localized HUD badge.
- Every UI control and localized string across `wasm/` strictly follows `blazor-ui` and `i18n`.
