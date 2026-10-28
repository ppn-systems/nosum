# ADR-057: Wilderness Ancient Dungeon Rift Portal & Boss Room Warps

## Status

Accepted

## Context

Nosum incorporates high-tier milestone dungeon floors (`mine10`, `mine20`, `mine30`, `mine40`) featuring enraging boss encounters and exclusive drop tables (ADR-047). Players transitioning between outdoor exploration and multiplayer dungeon raids previously had to navigate through sequential subterranean mine ladders or elevators. To enhance accessibility and provide an iconic MMORPG world landmark, a mystical wilderness rift portal was requested at the crossroads (`BusStop`), granting direct rift teleportation into the high-level Ancient Dungeon Broodmother lair (`mine10`).

## Decision

1. **Map Warp Topology & Server Authority**:
   - `GameMaps.cs` extends `BusStop` with an extra authoritative `MapWarp` at coordinate `(22, 14)`, routing to `mine10` at coordinate `(18, 13)`.
   - All warp transitions remain strictly server-authoritative via standard `WarpRule` validation, maintaining instant spatial consistency across all connected multiplayer sessions.

2. **Zero-Allocation Mystical Portal Rendering**:
   - Implemented in `Nosum.Wasm.Game.MonstersDecorator`:
     - Rendered on the `BusStop` map around coordinate `(22, 14)`.
     - Multi-layer visual effect composed of a deep cosmic purple swirling background aura (`0x6b21a8cc`), a pulsating cyan dimensional core (`0x06b6d4ee`), and 4 orbiting sparkle particles computed using integer trigonometry without heap allocation.
     - Overhead golden landmark label (`HẦM NGỤC CỔ ĐẠI (TẦNG 10)`) pre-interned in `CombatLabels.cs` (`PortalLabelId`) utilizing standard label fill style 1.

3. **Performance & Reliability**:
   - Hot-path frame loops allocate zero garbage collector objects.
   - Fully covered by unit tests verifying map warp resolution and browser E2E test verifying vortex visual presence and successful dungeon floor transition.

## Consequences

- Players can immediately discover and embark on high-tier dungeon boss encounters from the central wilderness crossroads.
- Visual clarity is heightened with pulsating magical vortexes rendered natively in the 2D canvas pipeline.
