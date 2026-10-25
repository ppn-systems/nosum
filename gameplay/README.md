# gameplay/

Farm rules shared by the server and the client: plots, crops, items, time-based growth,
prices. Pure, deterministic logic — no I/O, no clock reads (time is passed in), no network,
no rendering. The server runs it to decide; the client runs it to predict and display.

References `engine/Nosum.Core` only. Rules: [`gameplay-rules`](../.claude/skills/gameplay-rules/SKILL.md).

```bash
dotnet build gameplay/Nosum.Gameplay.slnx
```

Implemented: `Movement/WalkGrid` (`FromMap`: `Buildings` tiles block unless `Passable`; water or
`Passable=F` ground on `Back` blocks; `BlockFootprints` adds building footprints) and
`Movement/Walker` (fixed-step walking with a feet footprint, sliding along walls).

World maps (`Maps/`, [ADR-012](../docs/adr/ADR-012-maps-and-warps.md)): `GameMaps` is the catalogue of
hosted maps (`MapSpec`: id, `.tmx` file name, spawn tile, building props, opened doorsteps, extra exits);
`WarpParser` reads the Tiled `Warp` property (`fromX fromY targetMap targetX targetY` groups) and
`MapSpec.ResolveWarps` turns it into exits whose targets are catalogue ids (exits to unhosted maps are
dropped); `WarpRule.TryFind` says when a player standing at a position uses an exit (on the tile; an edge
exit needs walking outwards) and `WarpRule.Arrival` where they appear. `MapSpec.BuildWalkGrid` is the one
grid both sides use. Adding a map: add a `MapSpec` to `GameMaps`, link its `.tmx` and tilesheets in
`wasm/Nosum.Wasm/Nosum.Wasm.csproj` and `server/Nosum.Host/Nosum.Host.csproj` (`GameMapsTests` checks).
