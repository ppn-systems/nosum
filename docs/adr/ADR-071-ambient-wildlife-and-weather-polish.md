# ADR-071: Ambient wildlife and weather polish (client only)

## Status

Accepted

## Context

The outdoor maps are still. The farmer walks through a field with nothing alive in it but the farmer and the other players. The brief asks for
small creatures that make the world feel inhabited: birds that hop about and fly off when the farmer comes near, butterflies by day in spring and
summer, fireflies on summer nights, frogs by the water and squirrels in the Woods. It also asks for the weather to feel fuller: autumn leaves, the
snow that already falls, and rain that splashes where it lands.

The constraints are the project's. The wildlife is cosmetic, so it must not change the wire format (docs/adr/ADR-003, the opcodes are append-only),
the server must not know it exists, and all players should see about the same scene. The engine stays platform-free (docs/adr/ADR-001). The frame
keeps one interop crossing (docs/adr/ADR-002) and and never calls `StateHasChanged`. The art comes from the packs already in
`art/source/` (docs/adr/ADR-040), with provenance recorded (`ArtProvenanceTests`). Reduced motion (ADR-033) must be respected.

## Decision

- **D1 — The wildlife is client-only and needs no wire change.** The server sends nothing new. Each client computes the critters from the calendar
  it already has (`CalendarChannel`, the same clock the sky uses, ADR-027) and from the map it loaded. Nothing is persisted and no packet is added.
- **D2 — Spawns are a pure, seeded plan per chunk and day.** `CritterPlanner` in `Nosum.Gameplay` (the rules both sides can run) plans the critters of
  one 16 by 16 tile chunk for one game day. The inputs are a fixed world seed, a salt from the map's id (FNV-1a, stable across processes), the chunk,
  the day number, the season, the minute of the day, whether the map is the Woods and the ground. The hash is SplitMix64 over those inputs; the same
  chunk on the same day gives the same critters on every client. A chunk holds at most `MaxPerChunk` (4) critters. Each kind has its rule: birds by day
  on most days, butterflies by day in spring and summer, fireflies on summer nights (6:00 to 19:00 is day), frogs on a ground tile within two tiles of
  water, squirrels by day in the Woods. The planner allocates nothing; the caller passes the span.
- **D3 — Motion is stateless.** A critter's position and frame are a function of the frame clock, its id and the farmer's distance, not of stored
  state. A bird or squirrel flees when the farmer comes inside its radius (40 and 36 pixels), a frog jumps aside inside 22; a butterfly wanders about
  its spot; a firefly hovers and blinks. Nothing is stored per critter, so nothing allocates per frame.
- **D4 — Culling.** Chunks are visited only where they overlap the camera (one chunk of margin), and each critter is skipped unless its box, padded by
  the 24 pixels it may move, overlaps the view (`ViewCulling.Overlaps` in `Nosum.Rendering`, unit-tested). A plan is cheap enough to redo each frame,
  so no cache is kept.
- **D5 — Weather polish reuses the precipitation layer.** `Precipitation.Leaves` is a new kind: on a clear autumn day (`Season.Fall`) leaves sway
  down in orange, red and gold. Rain splashes: a drop that reaches the bottom of the view leaves a short line. Snow is unchanged. The layer's particle
  pool stays fixed at 320; the weather and calendar state already on the client (`SkyLayer`) chooses the kind.
- **D6 — Reduced motion.** `ViewOptions.ReduceMotion` (ADR-033) stops the wildlife: nothing flees, hops or wanders, butterflies hold their open
  wings, fireflies stay lit where they are. Weather particles are already left out with reduced motion.
- **D7 — Art.** The critters are six 16 by 16 cells in `assets/sprites/ambient.png`, built by `tools/art_repack/ambient.py` from the DawnLike pack
  (CC-BY 4.0, credited in `docs/art/provenance.md`) with two fireflies drawn in Nosum's own style, because no pack has a glowing insect. The cells
  are chosen by eye and are approximate; a better cut replaces the file without changing the code, as long as the cell layout holds.
- **D8 — Placement.** The planner and its rules live in `Nosum.Gameplay/Ambient` (pure, tested in `Nosum.UnitTests`). The culling test is the engine's
  (`Nosum.Rendering`). The drawing is a `CritterDecorator` (`IMapDecorator`, `wasm/Nosum.Wasm/Game`), so `MapScene` learns only one new call, and
  it is the only place the map knows the farmer's position.

## Consequences

- The wildlife is not part of the game's state: a player who does not see a critter cannot be relied on to see it, and no rule depends on one.
  Gameplay never reads it.
- Two clients on different machines compute the same critters, because the calendar and the map are the same. The motion differs slightly (the clock
  is local), which is cosmetic.
- A new critter kind or a new season rule is a change to `CritterPlanner` plus a row in the sheet. The sheet layout (`ambient.py`'s `SHEET_CELLS`)
  is the contract with the decorator.
- The frame gains a constant amount of work per visible chunk (a handful of hashes and a few sprites), with no new interop call.

## Alternatives considered

- **Server-side spawning.** Would make the scene shared by construction, but adds packets and server state for purely cosmetic life. Rejected:
  the server decides outcomes, not decoration (docs/adr/ADR-003).
- **Stored per-critter state with timers.** Would allow richer behaviour (a bird that returns after flying off), but needs allocation or a table
  per map. Rejected for now; the stateless motion covers the brief.
- **Separate particle system for critters.** Critters are depth-sorted sprites that the farmer walks in front of or behind, so they go through the
  existing `WorldSpriteBatch`, not the screen-space particle layer.
