# ADR-052: Co-op Party Combat and Proximity XP Sharing

## Status
Accepted

## Context
Multiplayer co-op farming and mining is a core pillar of Nosum (docs/design/pillars.md). In MMORPG wilderness maps (Crossroads, Backwoods, Mountain, Woods) and shared dungeon floors, players adventuring together in parties or encountering monsters collaboratively should share in the rewards of victory rather than racing for kill steals.

## Decision
1. **Proximity Party Sharing Policy**:
   - Defined `CombatRules.CoopShareRadius = 12` tiles (Chebyshev distance).
   - When a monster or milestone floor boss is slain, the killing player receives standard full XP (`def.XpReward`).
   - All other active players present on the same map instance within `CoopShareRadius` automatically receive party assistance XP (`def.XpReward / 2`).
2. **Server-Authoritative Synchronization**:
   - In `CombatService.AttackMonsterAsync`, when `defeated == true`:
     - Queries `_presence.CopyPlayers(player.Key, coPlayers)`.
     - Calculates tile distance between each party member and the monster's defeat position.
     - Atomically updates `allyVitals.CombatXp`, recalculating Max HP / MP upon level up.
     - Dispatches updated `VitalsState` packet to each party member via `IPlayerNotifier`.
3. **Zero-Allocation Hot Path**:
   - Reuses pre-allocated presence lists and packet scopes.
   - Requires zero client prediction hacks; server enforces proximity and outcome.

## Consequences
- Cooperative hunting and party dungeon crawling naturally incentivizes team play.
- No kill stealing or griefing; all players nearby benefit from defeating threats.
- Verified in live multi-client headless browser tests with 2 simultaneous players.
