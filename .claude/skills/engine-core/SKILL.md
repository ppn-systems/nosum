---
name: engine-core
description: Use when creating or changing code in engine/Nosum.Core or engine/Nosum.Rendering — game loop, FixedTimestep, scene/entities, tilemap, camera, math, input, render commands, batching, culling, IRenderBackend. Platform-free rule, zero-allocation frame, fixed timestep.
---

# Engine core and rendering front end

Decision: `docs/adr/ADR-001-separate-engine.md`. Browser backend and the JS side:
`render-interop`.

## Non-negotiables

- **No platform references.** `Nosum.Core` and `Nosum.Rendering` never reference Blazor,
  `Microsoft.JSInterop`, `System.Runtime.InteropServices.JavaScript`, Nalix, or `game/`. If
  something needs the browser, define an interface here and implement it in a
  `Nosum.Rendering.*` (or future `Nosum.Platform.*`) project.
- **No farming in the engine.** Anything that only makes sense for this game goes to
  `game/Nosum.Game` (`game-rules`).
- **Fixed timestep.** Simulation advances only through `FixedTimestep.Advance`
  (`engine/Nosum.Core/FixedTimestep.cs`): run the returned number of steps, then render with
  `Alpha` to interpolate. Never scale movement by the raw frame delta.
- **Zero allocation per frame.** Update, culling and command encoding reuse preallocated
  arrays and structs. No LINQ, lambdas capturing state, `foreach` over interfaces, string
  formatting, or `new` of reference types on the frame path. WASM garbage collection stops
  the world and appears as dropped frames.
- **Synchronous frame.** No `async`, `await`, `Task` or locks on the frame path; WASM runs the
  frame on one thread.
- **Determinism.** Engine code takes time as a parameter; it never reads `DateTime.Now`,
  `Stopwatch` or `Environment.TickCount` itself, so it can be unit-tested step by step.

## Render front end contract

- A frame's draws are encoded into one byte buffer and handed to
  `IRenderBackend.Submit(Span<byte>)` once (`engine/Nosum.Rendering/IRenderBackend.cs`). The
  buffer is valid only during that call.
- The encoding is a contract with `renderer.js`; changing it means changing both sides in the
  same commit (`render-interop`).
- Cull before encoding: only what intersects the camera is encoded.

## Checklist

- [ ] No forbidden reference (`dotnet list engine/Nosum.Core package` / `.csproj` diff).
- [ ] Frame path allocation-free; new types are `struct` or preallocated.
- [ ] Time passed in, not read.
- [ ] Unit test in `tests/Nosum.UnitTests/Engine/` (`testing-style`).
