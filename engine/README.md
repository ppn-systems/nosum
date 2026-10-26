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
early map). `WorldSpriteBatch` is the depth-sorted `world` layer of ADR-005 D4 (`Begin`,
`Add(..., sortY, sortX, id)`, `Flush`: preallocated keys, in-place sort, culled).
`PropDefinition`/`PropPlacement` (Core) describe a building as data (sheet rectangle, footprint,
roof height) and `PropLayer` (Rendering) draws any list of them: base into the world batch, roof
over everything. Prop names are keys, not display text.

Maps and scene changes: `TileLayerPlan.For(map)` sorts a map's layers into ground (below characters) and
front (`Front*`/`AlwaysFront*`, over them) and skips the logic layer `Paths`, so any map can be drawn the
same way. `RenderCommandBuffer.FillBlack(opacity, w, h)` records a black rectangle (texture id
`BlackFill`, tint = opacity); `renderer.js` fills it, which is how a scene fades without a second
interop call.

Text labels (nicknames): `RenderCommandBuffer.DrawLabel` records a label by id (texture `-3`); `LabelRegistry` interns the text and uploads it once to the backend (`WebRenderBackend.UploadLabel` -> `renderer.js registerLabel`, which renders it into a cached, outlined bitmap drawn in screen space at 1x, or 2x from world zoom 5), so a frame still crosses to the browser once with no strings and no allocation.
Sound commands (appended for [ADR-039](../docs/adr/ADR-039-audio-hooks.md)): `PlaySound(id, volume, pan)` (texture `-7`), `SetMusic(track)` (`-8`) and `SetAmbientLoop(loop, volume)` (`-9`) draw nothing; `renderer.js` forwards them to the sibling module `audio.js` (Web Audio, files decoded once at load, the `AudioContext` created on the first user gesture). `SoundCues` collects the sounds, the music track and the ambient loop between frames (per-sound minimum interval, music and loop sent only when they change) and writes them right after `Begin`, so audio costs no crossing of its own. `VolumeCurve.Gain` maps a settings slider to the gain `WebRenderBackend.SetLevels` sends when the settings change.
Sky commands (appended for [ADR-027](../docs/adr/ADR-027-weather-and-day-night.md); the older ids keep their meaning): `SetAmbient(r, g, b, a)` (texture `-4`) composites one full-canvas tint over everything drawn so far, `AddLight(x, y, radius)` (texture `-5`, at most `MaxLights` = 64 per frame, recorded before `SetAmbient`) cuts a soft hole in that tint, and `FillRect(x, y, w, h, rgba)` (texture `-6`) draws a coloured rectangle. `renderer.js` builds the tint on one offscreen layer, cuts the lights out of it and draws the layer once, so the frame still crosses once. `PrecipitationLayer` is the cosmetic rain/snow pool (fixed 320 particles, no allocation per frame) drawn with `FillRect`.
