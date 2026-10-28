# ADR-051: Status Effects (Regeneration, Poison, and Swiftness)

## Status
Accepted

## Context
Beyond instant healing potions and raw weapon strikes, MMORPG combat relies on over-time status effects (buffs and debuffs). Magical consumables like Golden Elixirs provide sustained health regeneration, while hazardous cave denizens like Spore Shrooms and Cave Bats inflict lingering poison damage that depletes player vitals.

## Decision
1. **Status Effect Domain Enumeration**:
   - Defined `StatusEffectKind` (`None = 0`, `Regeneration = 1`, `Poison = 2`, `Swiftness = 3`) in `Nosum.Gameplay.Combat`.
   - Mapped consumable buffs in `CombatRules.PotionEffect(itemId)`:
     - `ItemCatalog.Elixir`: Regeneration for 30s (+5 HP per tick).
     - `ItemCatalog.PumpkinSoup`: Swiftness for 20s.
   - Mapped creature poison debuffs in `CombatRules.MonsterStatusOnHit(monsterKind)`:
     - `MonsterKind.CaveBat`: Poison for 6s (-2 HP per tick).
     - `MonsterKind.SporeShroom`: Poison for 10s (-3 HP per tick).
     - `MonsterKind.BossBroodmother`: Deadly Venom for 12s (-4 HP per tick).
2. **Server-Side Periodic Ticks**:
   - Extended `PlayerVitalsState` in `CombatService` with `ActiveEffect`, `EffectRemainingTicks`, and `EffectMagnitude`.
   - During the 1.5s AI tick loop (`RunAiAsync`), ticks decrement and HP updates (clamped between 1 and MaxHp).
   - If player HP reaches 0 from poison, standard knockout/revive triggers.
   - Pushed to client via append-only `StatusEffect` byte in `VitalsState` wire packet (`[SerializeOrder(5)]`).
3. **Client HUD Visualization**:
   - Exposed `StatusEffect` and `StatusEffectBadge` in `ProgressViewModel.cs`.
   - `EnergyBar.razor` applies dynamic visual aura styling:
     - `.regen`: Pulsing glowing emerald aura on the HP gauge.
     - `.poison`: Toxic ultraviolet/green hue shift and shadow on the HP gauge.
     - `.swift`: Golden glow on stamina gauge.

## Consequences
- Authentic MMORPG buff/debuff mechanics with server-side validation.
- Zero extra allocations: tracked via primitive fields in existing session state.
- Wire protocol preserved via append-only serialization order.
