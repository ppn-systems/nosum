# ADR-001 — Separate, platform-free engine

**Status:** Accepted (owner decision, 2026-10-25)

## Context

Nosum draws a tile-based farm world in the browser. Blazor's component model re-renders DOM
and is built for forms and lists, not for hundreds of sprites redrawn every frame. Game logic
written directly inside Blazor components would also be untestable without a browser and
locked to one rendering technology.

## Decision

- **D1** — The engine lives in its own area, `engine/`, with its own solution
  (`engine/Nosum.Engine.slnx`).
- **D2** — `Nosum.Core` (loop, time, scene, tilemap, camera, math, input) and
  `Nosum.Rendering` (render commands, batching, culling, `IRenderBackend`) reference no
  Blazor, JS interop or Nalix assembly.
- **D3** — Platform code sits behind `IRenderBackend`
  (`engine/Nosum.Rendering/IRenderBackend.cs`). The browser implementation is
  `Nosum.Rendering.Web`, the only engine project that may reference
  `System.Runtime.InteropServices.JavaScript`.
- **D4** — Farming rules do not live in the engine; they live in `gameplay/Nosum.Gameplay`, which
  references `Nosum.Core` only.
- **D5** — The simulation advances in fixed steps (`engine/Nosum.Core/FixedTimestep.cs`);
  rendering interpolates with `FixedTimestep.Alpha`.

## Alternatives considered

- **Render the world with Blazor components** — simplest start, but every sprite becomes a
  component diff; rejected for the per-frame workload.
- **Use a JS engine (PixiJS, Phaser) and keep C# for UI only** — fast rendering, but game
  logic would be split between C# and JS and could not be shared with the server.

## Consequences

- Engine code is unit-tested in plain .NET (`tests/Nosum.UnitTests/Engine/`).
- A second backend (WebGL, desktop) is a new `Nosum.Rendering.*` project, not an engine rewrite.
- More projects to maintain than a single Blazor app.

## Revisit when

A browser API the engine needs cannot be expressed behind `IRenderBackend` or an equivalent
platform interface.
