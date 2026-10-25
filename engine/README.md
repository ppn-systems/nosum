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
