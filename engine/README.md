# engine/

The Nosum 2D engine. It knows nothing about farming, the network or Blazor; the game in
`gameplay/` and the client in `wasm/` build on it. Decision: [ADR-001](../docs/adr/ADR-001-separate-engine.md).

| Project | Holds | May reference |
| :--- | :--- | :--- |
| `Nosum.Core` | Loop (`FixedTimestep`), time, scene, tilemap, camera, math, normalized input | nothing |
| `Nosum.Rendering` | Render commands, sprite batching, culling, `IRenderBackend` | `Nosum.Core` |
| `Nosum.Rendering.Web` | Browser backend: `WebRenderBackend` (`[JSImport]`) + `wwwroot/renderer.js` | `Nosum.Rendering` |

Rules: [`engine-core`](../.claude/skills/engine-core/SKILL.md) and, for the browser backend,
[`render-interop`](../.claude/skills/render-interop/SKILL.md).

```bash
dotnet build engine/Nosum.Engine.slnx
```

Implemented: `FixedTimestep`, `TmxReader`/`TileMap` (orthogonal, embedded tilesets, CSV layers),
`InputState`, `Facing`; `RenderCommandBuffer` (one int32 draw list per frame), `Camera2D`,
`TileLayerRenderer` (draws only visible tiles); the browser backend loads textures, runs the
`requestAnimationFrame` loop, tracks WASD/arrow keys and draws with Canvas2D (flip, cached
multiply tints).

Map and world components (generic, no farm knowledge): `TmxReader` also reads per-tileset tile
properties and `<animation>` frames, plus map and layer properties (`TileProperties`,
`TileTraits` for `Water`/`Passable`, `TileAnimation.FrameAt(ms)`, `TileMap.GetTraits(gid)`).
`TileLayerRenderer.Draw` picks the animated frame from a clock passed in (no allocation) and can
ripple tiles carrying a trait with `TileWave` (used for water, whose tiles have no frames in the
placeholder map). `WorldSpriteBatch` is the depth-sorted `world` layer of ADR-005 D4 (`Begin`,
`Add(..., sortY, sortX, id)`, `Flush`: preallocated keys, in-place sort, culled).
`PropDefinition`/`PropPlacement` (Core) describe a building as data (sheet rectangle, footprint,
roof height) and `PropLayer` (Rendering) draws any list of them: base into the world batch, roof
over everything. Prop names are keys, not display text.

Maps and scene changes: `TileLayerPlan.For(map)` sorts a map's layers into ground (below characters) and
front (`Front*`/`AlwaysFront*`, over them) and skips the logic layer `Paths`, so any map can be drawn the
same way. `RenderCommandBuffer.FillBlack(opacity, w, h)` records a black rectangle (texture id
`BlackFill`, tint = opacity); `renderer.js` fills it, which is how a scene fades without a second
interop call.
