# ADR-055: Floating Combat Text and Monster Overhead Mini-Health Bars

## Status
Accepted

## Context
In fast-paced MMORPG combat, players need immediate, tactile feedback on their actions. Previously, combat damage and heal quantities were reflected only in the bottom vitals HUD and server state changes, without visual cues directly over combat targets. Furthermore, engaging multiple monsters in caves or dungeons made it difficult to determine which monster had taken damage without checking logs.

## Decision

### 1. Zero-Allocation Label Interning & Colored Text Styles (`CombatLabels`)
- Extended `LABEL_FILL` in `engine/Nosum.Rendering.Web/wwwroot/renderer.js` to support 5 combat text styles:
  - Style 0: `#ffffff` (White - Normal monster strike)
  - Style 1: `#ffe08a` (Gold - Critical hit >= 18 damage)
  - Style 2: `#ff5757` (Red - Player damage taken from monster)
  - Style 3: `#63c74d` (Green - Healing / regeneration ticks)
  - Style 4: `#c084fc` (Purple - Poison damage tick)
- Created `CombatLabels` in `wasm/Nosum.Wasm/Game/CombatLabels.cs` which pre-interns standard combat quantities 1..50 across all styles during initialization using `LabelRegistry`, eliminating heap allocation in the 60 FPS frame loop.

### 2. Rising Trajectory & Expiration (`FloatingTextClientRow`)
- In `wasm/Nosum.Wasm.Socket/Combat/CombatChannel.cs`:
  - `FloatingTextClientRow(int WorldX, int WorldY, int Amount, byte Style, long StartTime, float DurationMs)` tracks transient combat indicators.
  - Spawns upon successful player weapon/staff attacks (`AttackAsync`), monster retaliations, potion consumption (`DrinkPotionAsync`), and periodic vitals changes (`OnVitals`).
  - `CopyFloatingTexts` collects active texts without allocation.
- In `wasm/Nosum.Wasm/Game/MonstersDecorator.cs`:
  - For each floating text, computes upward vertical rise: `floatY = (int)(f.WorldY - (progress * 18f))`.
  - Dispatches via `RenderCommandBuffer.LabelTexture` (`-3`) with screen-space crisp pixel font rendering.

### 3. Monster Overhead Mini-Health Bars (`RenderCommandBuffer.FillTexture`)
- In `wasm/Nosum.Wasm/Game/MonstersDecorator.cs`:
  - Whenever a monster is damaged (`m.Hp < m.MaxHp`) or is a dungeon floor boss (`m.Kind >= 9`), an overhead mini-health bar is rendered 6 pixels above its head.
  - Renders a 14x3 dark slate background/border via `RenderCommandBuffer.FillTexture` (`-6`) with `tint: 0x111111ee`.
  - Computes inner fill width proportional to remaining HP: `fillW = Math.Clamp((m.Hp * 12) / m.MaxHp, 1, 12)`.
  - Dynamically colors health fill based on percentage:
    - > 50% HP: `#44cc44` (Green)
    - > 25% HP: `#e5b82e` (Yellow)
    - <= 25% HP: `#e53935` (Red)

## Consequences
- Immediate visual and spatial combat clarity for attacks, crits, heals, and poison.
- Monsters dynamically display overhead health bars when damaged or boss-tier.
- Zero allocations in the render frame loop and packet handling paths.
- Verified in live browser sessions with Playwright screenshots in `docs/qa/`.

## Amendments

### 2026-11-01 - a monster hit shows one number on the farmer (combat critique)

- A monster hit reached the client twice: in the attack reply (`AttackMonsterResult.DamageTaken`, shown as a red number) and in the vitals push
  that carries the new HP (`VitalsState`, shown as a red number from the HP drop). Both were shown, so the farmer saw `-8` twice for one hit, and the
  order of the two packets decided how often.
- `CombatChannel.AnnouncePlayerDamage` is now the one place that shows the farmer's damage. A hit is keyed by the HP after it and its amount. The second
  report of the same key within 1.5 seconds is dropped, whichever packet arrived first. A heal (a vitals push that raises HP, or a potion) starts a new
  run, so the same hit after a heal is shown again.
- A monster's damage on the monster itself is unchanged. No wire change: the server still sends both packets and `NosumProtocol.Version` is unchanged.
  Covered by `CombatHitNumberTests`.
