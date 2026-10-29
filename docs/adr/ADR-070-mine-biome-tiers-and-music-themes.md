# ADR-070: Mine biome tiers, tinted cave palettes and music themes

## Status

Accepted

## Context

The mine is forty floors deep and the floors look alike. Every floor is a cave drawn from the same cave sheet, and every floor plays the same
season music as the surface. Issue #180 asks for four depth tiers, each with its own look and sound: floors 1 to 10 earth, 11 to 20 frost, 21 to 30
magma and 31 to 40 ancient ruins.

The brief fixes three constraints. It must reuse the existing mine maps and assets: no new art licences, so the tiles are tinted or varied, not
redrawn (docs/adr/ADR-040, ADR-042). It must reuse the existing music hook ids (docs/adr/ADR-039 D4 and the implementation notes): there are eight
tracks and no new ones. And it must leave combat alone; monster species already vary by floor band in `MonsterSpawnPlanner` and are not changed here.

The biome is cosmetic. The server never reads it: it decides nothing about money, items, time or monsters from it. The client already knows the floor it
shows (`MapSpec.MineFloor`), so the tier can be derived on both sides from the floor alone.

## Decision

- **D1 — The tier is a pure rule in `Nosum.Gameplay`.** `MineBiome` (Earth, Frost, Magma, Ruins) and `MineBiomeRules.Of(floor)` put every ten floors in
  one biome. `Of` refuses a floor outside 1 to 40, as the planner does. The entrance cave (floor 0) is not a mine floor and keeps the surface music.
- **D2 — Tiles are tinted, not redrawn.** Each biome has a multiply tint (`MineBiomeLook`, `0xRRGGBBAA`) applied to every tile of the floor's
  ground and front layers. Earth is untinted. The tint is an argument of `TileLayerRenderer.Draw` (optional, `RenderCommandBuffer.NoTint` by default,
  passed to the existing `Draw` command), so the frame's command buffer gains no new command and the interop budget (docs/adr/ADR-002) is unchanged. The
  `.tmx` maps and the cave sheet are not edited and `AutoTerrain` does not change; the palette is a draw-time choice.
- **D3 — Music uses existing track ids.** `MusicTrackIds.ForMine(biome)` maps each tier to one of the eight tracks: Earth to spring day (1), Frost to
  winter day (7), Magma to summer night (4), Ruins to fall night (6). These four are the most unlike one another among the eight. A mine floor plays its
  biome's track in place of the season's, whatever the calendar says, because the floors are underground. The music cross-fades as before
  (docs/adr/ADR-039 D3).
- **D4 — No wire change.** Nothing is added to a packet or an opcode, so `NosumProtocol.Version` stays at 8. The client reads `MapSpec.MineFloor`
  from the map it already loads.

## Alternatives considered

- **New tile art per tier (a frost sheet, a magma sheet).** The clearest result, but it needs new art and licences that the brief rules out for now;
  it can come later under `assets-pipeline` without changing this design.
- **Biome in the map file or in `AutoTerrain`.** That would regenerate every floor `.tmx` and tie the look to the generator. A tint at draw time changes
  nothing on disk and is reversed by deleting one table.
- **Biome sent by the server.** It would cost a wire change and a protocol bump for a value the client already derives from the floor. Rejected.
- **New music tracks per tier.** Eight tracks already cover every season and half-day, so the mine reuses them. Four of the eight are shared with the
  surface: a frost floor can sound like a winter day above ground. This is the cost of keeping the existing hook ids.

## Consequences

- The mine is visibly and audibly graded by depth without new assets, and the rule is testable: `MineBiomeRulesTests` pins every tier boundary.
- `TileLayerRenderer.Draw` gains an optional parameter; existing callers are unchanged.
- Four of the eight music tracks now play in the mine as well as above ground. The mine tracks are the same loops, so a player going up a ladder
  hears the same theme they heard below.
- Monster species, boss floors (docs/adr/ADR-047), the planner and the combat service are untouched.

## Revisit when

Players want the tiers to differ in more than tint and music (new tiles, ambient loops, a biome name on the floor), which needs own art (M6) and
new `.resx` keys (the `i18n` skill). Or the surface and the mine should not share a track: that needs more music tracks.
