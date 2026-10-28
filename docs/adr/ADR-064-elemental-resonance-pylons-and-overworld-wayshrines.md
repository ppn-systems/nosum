# ADR-064: Elemental Resonance Pylons & Overworld Wayshrines

## Status

Accepted — 2026-10-28

## Context

`ADR-063` introduced beneficial Dungeon Blessing Shrines (`VitalityShrine`, `ArcaneObelisk`, `EmpowermentAltar`) inside the forty mine floors (`mine1`..`mine40`). However:

1. Overworld wilderness combat maps (`Forest`, `Woods`, `Mountain`, `Backwoods`) host roaming monsters (`GreenSlime`, `SporeShroom`, `GoblinScout`, `FrostSlime`) where players expend HP and MP without any wilderness wayshrines or tactical sanctuaries.
2. Dungeon combat benefits from tactical environmental objects that not only bless the player upon proximity activation, but also unleash an **Elemental Resonance Discharge** against nearby hostile monsters when players kite monsters into range.
3. Additional unused crystalline and elemental relic sprites in `assets/Objects/springobjects.png` (such as `Solar Essence` index `768`, `Battery Pack` / Lightning Core index `787`, `Topaz` index `68`, `Aquamarine` index `62`, and `Frozen Tear` index `84`) can be brought to life in the world renderer with zero per-frame allocations.

## Decision

1. **Extend `DungeonHazardKind` (`Nosum.Gameplay.Combat`)**:
   - `ThunderPylon = 7`: Ancient Thunder Pylon — restores `+10 HP`, `+20 MP`, awards `+12 Combat XP`, grants `StatusEffectKind.Swiftness`, and discharges an arcane lightning pulse (`18` damage, `CombatSkillKind.Fireball`) to all living monsters within `3` tiles (Chebyshev distance).
   - `SolarWayshrine = 8`: Sylvan Solar Wayshrine — restores `+30 HP`, `+15 MP`, awards `+12 Combat XP`, grants `StatusEffectKind.Regeneration`, and sears all living monsters within `3` tiles (`14` damage, `CombatSkillKind.Whirlwind`).
   - `FrostMonolith = 9`: Glacial Frost Monolith — restores `+20 HP`, `+20 MP`, awards `+12 Combat XP`, grants `StatusEffectKind.Regeneration`, and blasts all living monsters within `3` tiles (`16` damage, `CombatSkillKind.Fireball`).

2. **Gameplay Rules (`CombatRules`)**:
   - Extend `CombatRules.IsShrine(DungeonHazardKind)` to include `ThunderPylon`, `SolarWayshrine`, and `FrostMonolith`.
   - Add `CombatRules.IsResonancePylon(DungeonHazardKind)` and `CombatRules.PylonDischarge(DungeonHazardKind)` returning `(int MonsterDamage, int RadiusTiles, CombatSkillKind SkillKind)`.
   - Extend `CombatRules.PlanShrines` to deterministically place:
     - **Overworld Wilderness Wayshrines & Pylons**: 2 sanctuaries per wilderness combat map (`SolarWayshrine` + `ThunderPylon` in `Forest`/`Woods`; `FrostMonolith` + `ThunderPylon` in `Mountain`/`Backwoods`).
     - **Mine Elemental Resonance Pylons**: 1 resonance pylon per mine floor (`ThunderPylon`, `SolarWayshrine`, or `FrostMonolith` rotated by floor) alongside the floor's sanctuary shrine(s).

3. **Server-Authoritative Execution (`CombatService`)**:
   - When a player triggers a resonance pylon (`CombatRules.IsResonancePylon(kind)`) off cooldown (`12,000 ms`), the server applies the player's HP/MP/XP/status blessing and simultaneously damages all living monsters within `RadiusTiles` of the pylon, broadcasting `MonsterChanged` and `MonsterHit` packets (and awarding defeat XP if a monster's HP reaches `0`).

4. **Zero-Allocation World Rendering & Localization (`MonstersDecorator`, `CombatLabels`, `Language.resx`, `Language.en.resx`)**:
   - Localize `Combat_Label_Shrine_Thunder`, `Combat_Label_Shrine_Solar`, and `Combat_Label_Shrine_Frost` in both Vietnamese (default) and English.
   - Pre-upload the labels once in `CombatLabels` and draw each pylon's stone pedestal, pulsing elemental shockwave ring, and bobbing relic core in `MonstersDecorator` without per-frame allocations.

## Consequences

- Both wilderness hunting grounds (`Forest`, `Woods`, `Mountain`, `Backwoods`) and mine dungeons (`mine1`..`mine40`) feature interactive Elemental Resonance Pylons and Wayshrines.
- Players can strategically kite monsters toward pylons to trigger AoE elemental discharges while replenishing HP/MP and earning Combat XP.
- Fully compliant with `engine-core`, `render-interop`, `blazor-ui`, `nalix-packets`, and `i18n` rules.
