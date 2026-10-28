# ADR-053: Combat Audio Sound Effects Synthesis and Event Wiring

## Status
Accepted

## Context
Prior to Round 9, swinging a sword or staff reused basic agricultural tool sounds (`ToolScythe`), and monster strikes, damage reactions, defeat poofs, and potion drinks lacked distinct audio cues. Audio in Nosum must follow ADR-039: zero runtime interop cost (batched into frame render command buffers), deterministic synthesizer generation via Python standard library, and zero third-party copyright entanglements.

## Decision
1. **Deterministic Sound Synthesis**:
   - Extended `tools/synth_sounds.py` with 5 new 16-bit mono 22,050 Hz PCM sound effects:
     - `sword_slash.wav`: Metallic swoosh and fast blade cutting air (high-frequency filtered noise + down-gliding sine).
     - `staff_cast.wav`: Arcane spell charge and resonant chime burst.
     - `monster_hit.wav`: Heavy low-pitch impact and creature grunt.
     - `monster_defeat.wav`: Dispersing low-end rumble and crackling poof.
     - `potion_drink.wav`: Resonant two-note fluid gulp.
2. **Audio Catalog & Sound IDs**:
   - Added IDs 15..19 (`SwordSlash`, `StaffCast`, `MonsterHit`, `MonsterDefeat`, `PotionDrink`) to `SoundIds` and `AudioCatalog.Files`.
   - Updated `AudioCatalog.ToolSound`:
     - `ToolKind.Sword` -> `SoundIds.SwordSlash`.
     - `ToolKind.Staff` -> `SoundIds.StaffCast`.
3. **Reactive Audio Hooks in AudioWiring**:
   - In `AudioWiring.Connect`, subscribed `GameAudio` to:
     - `CombatChannel.AttackCompleted`: Plays `MonsterHit` (on damage) or `MonsterDefeat` (on lethal kill).
     - `CombatChannel.PotionCompleted`: Plays `PotionDrink`.

## Consequences
- Every strike, spell cast, monster hit, death poof, and potion drink is paired with responsive audio cues.
- Zero extra GC allocations or interop round-trips.
- 100% compliant with deterministic build pipeline and testing gates.
