# ADR-002 — Batched render interop: one JS crossing per frame

**Status:** Proposed — to be confirmed by `bench/Nosum.Bench.Interop` numbers.

## Context

In Blazor WebAssembly, .NET cannot draw on a canvas directly; every draw goes through JS
interop. `IJSRuntime.InvokeAsync` serializes arguments to JSON and is asynchronous.
`[JSImport]`/`[JSExport]` (`System.Runtime.InteropServices.JavaScript`) is synchronous and can
pass a `Span<byte>`, `Span<int>` or `Span<double>` as a `MemoryView` without copying; in
.NET 10 `Span<float>` is not in that list (it appears for .NET 11+). A `MemoryView` built from
a `Span` is valid only during the call. Source:
https://learn.microsoft.com/aspnet/core/client-side/dotnet-interop/ (accessed 2026-10-25).

## Decision

- **D1** — The frame is driven from JS `requestAnimationFrame`, which calls one `[JSExport]`
  frame entry in .NET, passing that frame's batched input events.
- **D2** — .NET encodes every draw of the frame into one preallocated byte buffer and hands it
  to JS in one `[JSImport]` call with `[JSMarshalAs<JSType.MemoryView>] Span<byte>`
  (`engine/Nosum.Rendering.Web/WebRenderBackend.cs`). The build of that file confirms the
  generator accepts this signature on .NET 10.
- **D3** — JS decodes and draws synchronously inside that call and never keeps the view.
- **D4** — `IJSRuntime` is allowed only outside the frame loop (startup, asset loading, UI).
- **D5** — The WASM client is trimmed. Development builds and publishes use the interpreter
  (fastest to build). The web deploy publishes with WebAssembly AOT
  (`dotnet publish wasm/Nosum.Wasm -c Release -p:NosumWebDeploy=true`, `wasm-tools` workload;
  owner decision 2026-10-25). Measured on 2026-10-25: brotli `.wasm` download 2.0 MB
  (interpreter) vs 4.9 MB (AOT); AOT publish ≈ 4 min. The server is not AOT-compiled (EF Core
  needs reflection; JIT is fine server-side).
- **D6** — The first backend draws with Canvas2D. A WebGL backend may replace it later
  without changing the C# side, because the command buffer is the contract.

## Alternatives considered

- **`IJSRuntime` per sprite** — simplest; expected to be far too slow at hundreds of sprites
  per frame (JSON plus a crossing per call). The benchmark measures it as the baseline.
- **`IJSInProcessRuntime.Invoke`** — synchronous, but still JSON; same per-call cost class.
- **Long-lived `ArraySegment` buffer shared with JS** — avoids rebuilding the view each frame
  but needs explicit `dispose()` and pinning management; kept as an option if the benchmark
  shows view creation matters.

## Consequences

- The command-buffer encoding becomes a contract between `Nosum.Rendering` and `renderer.js`
  and must be versioned with both sides.
- No allocation inside the frame: WASM garbage collection stops the world and shows up as
  dropped frames.

## Revisit when

The benchmark is run (Accept or amend D2/D5 with its numbers), or the .NET runtime adds a
cheaper path (for example `Span<float>` marshaling once the project moves to .NET 11).
