# ADR-046 — Monster AI Roaming, Aggro, Real-time Combat Effects and Boss Encounters

## Context

ADR-045 established the foundation for MMORPG combat: player vitals (HP, MP, Energy), the Combat skill, an arsenal of craftable weapons and consumable potions, and live server-authoritative monsters. However, monsters spawned at static positions and only reacted when struck. For an engaging MMORPG experience, monsters must possess active behavioral AI (wandering, aggro detection, pursuit, and contact attacks), real-time visual feedback (floating damage indicators and monster HP bars), and challenging boss encounters with high-value rewards.

## Decisions

### D1: Server Monster AI Tick & Behavioral States

1. **AI Loop**: `CombatService` runs a tick (every 1.5 seconds) for map instances that currently contain players.
2. **Behavioral States**:
   - `Idle`: When no players are nearby, monsters idle or occasionally wander 1 step within their leash radius (3 tiles of spawn origin).
   - `Aggro`: If a player enters Chebyshev distance $\le 4$ tiles, the monster locks onto the player and steps along the walk grid toward them.
   - `Attack`: When adjacent (distance $\le 1$ tile), the monster strikes the player, dealing contact damage. If the player's HP reaches 0, the player is knocked out, loses a fraction of energy, and revives with 50% HP.
3. **Synchronization**: Monster movement broadcasts `MonsterChanged` (or a batch snapshot) to players in the instance so the client interpolates positions seamlessly.

### D2: Real-time Floating Combat Text & Monster HP Gauges

1. **Monster Health Bars**: `MonstersDecorator` renders a mini pixel health bar (green/red) above every monster whose HP is below maximum.
2. **Floating Damage & Heals**: When an attack or potion event occurs, a floating numeric indicator appears:
   - Gold/White: Damage dealt to monsters.
   - Crimson: Damage taken by the player.
   - Emerald (+HP) / Sapphire (+MP): Potion restoration numbers.
3. **Zero Allocation**: Floating combat text items are stored in a preallocated fixed-size ring buffer on the client.

### D3: Dungeon Boss Encounters & Guaranteed Loot

1. **Boss Floors**: Mine floors 10, 20, 30, and 40 feature enhanced Dungeon Bosses:
   - Floor 10: Cave Broodmother (Bat/Shroom hybrid with 120 HP).
   - Floor 20: Goblin Chieftain (180 HP, high defense).
   - Floor 30: Magma Colossus (250 HP, fiery contact damage).
   - Floor 40: Golem King (400 HP, massive defense, drops `TreasureGem` and `GolemCore`).
2. **Boss Rewards**: Defeating a floor boss grants high Combat XP (50–150 XP) and guaranteed high-tier materials required for endgame weapons (`GoldSword`, `ArcaneStaff`, `Elixir`).

## Consequences

- Monsters dynamically pursue players, creating tension and excitement in the wilderness and mines.
- Real-time combat feedback clearly communicates damage dealt and taken without cluttering chat logs.
- The 1.5-second AI tick runs only on populated instances, preserving server performance and zero-allocation frame loops.
