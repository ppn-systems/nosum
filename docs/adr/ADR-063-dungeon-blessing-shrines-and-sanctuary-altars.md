# ADR-063: Dungeon Blessing Shrines and Ancient Sanctuary Altars

- **Status**: Accepted
- **Date**: 2026-10-28
- **Amends**: [ADR-045](ADR-045-mmorpg-combat-hp-mp-and-monster-assets.md), [ADR-061](ADR-061-dungeon-environmental-hazards-and-elemental-traps.md)

## Context

Rounds 1 through 18 established the core MMORPG combat loop, 12 monster and boss species, projectiles, armor defense mitigation, status effects, co-op shared XP, combat skills, the Ancient Dungeon Rift Portal, Elite monster affixes, the Monster Bestiary, combo streaks, dungeon hazards, and celebratory level-up radiance.

While dungeon floors now feature dangerous environmental traps (`SporeVent`, `SpikeTrap`, `MagmaFissure`) and tough Elite/Boss encounters, delve progression benefits from positive tactical landmarks—**Ancient Dungeon Blessing Shrines and Sanctuary Altars**—that reward positioning and sustain adventurers during prolonged subterranean boss fights without requiring extra UI dialogs or breaking wire compatibility.

## Decisions

### D1 — Shrine Kinds in `DungeonHazardKind`

`DungeonHazardKind` (`gameplay/Nosum.Gameplay/Combat/DungeonHazardKind.cs`) is extended with three beneficial sanctuary shrine kinds (`Damage = 0` on `DungeonHazard` / `HazardRow`, reusing the existing `MonstersSnapshot.Hazards` wire list without altering opcodes or `[SerializeOrder]`):

1. `VitalityShrine = 4` — **Ancient Vitality Fountain**: Restores `+25 HP` and grants `StatusEffectKind.Regeneration` (`10s`) to nearby players.
2. `ArcaneObelisk = 5` — **Ancient Arcane Obelisk**: Restores `+20 MP` and grants `+10 Combat XP` to nearby players.
3. `EmpowermentAltar = 6` — **Ancient Sanctuary Altar**: Spawns on boss/milestone floors (`floor % 10 == 0` or `floor >= 20`), restoring `+25 HP` and `+20 MP`, granting `StatusEffectKind.Swiftness` (`10s`), and awarding `+15 Combat XP`.

### D2 — Deterministic Shrine Placement (`CombatRules.PlanShrines`)

`CombatRules.PlanShrines(string mapId, ulong seed, WalkGrid grid, int spawnX, int spawnY, IReadOnlySet<(int X, int Y)>? occupied = null)` deterministically places `1` shrine on regular mine floors (`mine1`..`mine40`) and `2` shrines on milestone boss floors (`floor % 10 == 0`), selecting unblocked, unoccupied chamber tiles near the floor's central arena so they never collide with ore nodes, monsters, or damaging hazards.

### D3 — Server-Authoritative Proximity Blessing & Cooldown (`CombatService`)

During the server world tick (`CombatService.cs`), when a player stands on or adjacent to (`Chebyshev distance <= 1`) a shrine and their per-player per-shrine cooldown (`12,000 ms`) has elapsed:
- The server applies the HP/MP restoration, status effect buff (`Regeneration` or `Swiftness`), and Combat XP reward inside the player's `PlayerVitalsState` lock.
- If the Combat XP reward crosses a skill level threshold, full vitality surge and `LevelUp` notification fire automatically.
- Updated `VitalsState` is sent to the player, triggering floating heal/mana numbers on the client canvas.

### D4 — Zero-Allocation World Rendering & Localization (`MonstersDecorator`, `CombatLabels`)

- `MonstersDecorator` renders each shrine kind with a carved stone/golden altar base, a pulsing aura (`emerald` for `VitalityShrine`, `cyan` for `ArcaneObelisk`, `gold` for `EmpowermentAltar`), a levitating crystal/star icon from `assets/sprites/items.png`, and an overhead pre-interned localized title label.
- `Language.resx` (Vietnamese) and `Language.en.resx` (English) define `Combat_Label_Shrine_Vitality`, `Combat_Label_Shrine_Arcane`, and `Combat_Label_Shrine_Empowerment`.
