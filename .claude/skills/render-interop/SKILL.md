---
name: render-interop
description: Use when touching the frame loop's browser side — engine/Nosum.Rendering.Web, wwwroot/renderer.js, requestAnimationFrame, [JSImport]/[JSExport], MemoryView, input batching — or any .NET ↔ JS call in a hot path. One crossing each way per frame, MemoryView lifetime, what IJSRuntime may still do.
---

# Render interop (.NET ↔ JS)

Decision: `docs/adr/ADR-002-batched-render-interop.md`. Facts below are from
https://learn.microsoft.com/aspnet/core/client-side/dotnet-interop/ (accessed 2026-10-25) and
from building `engine/Nosum.Rendering.Web` on .NET 10.

## Non-negotiables

- **At most one crossing each way per frame.** JS `requestAnimationFrame` → one `[JSExport]`
  frame entry (with the frame's batched input) → one `[JSImport]` `submitFrame` with the whole
  command buffer. Never a call per sprite, per tile or per input event.
- **Never `IJSRuntime`/`IJSInProcessRuntime` in the frame loop.** Both marshal through JSON.
  They are fine for startup, asset loading and UI.
- **`MemoryView` from a `Span` lives only during the call.** JS must decode and draw before
  returning and must not store the view, a `subarray` of it, or a promise that reads it later.
  If a buffer must outlive the call, use `ArraySegment<T>` and call `dispose()` on the view in
  JS (`try/finally`) — it pins the .NET array until disposed.
- **Spans that marshal as `MemoryView` on .NET 10:** `Span<byte>`, `Span<int>`,
  `Span<double>` (and the `ArraySegment` forms). `Span<float>` is listed only for .NET 11+.
  Encode floats into the byte buffer; read them in JS with a `DataView`/`Float32Array` over `view.slice()` (a copy).
- **Do not give a `[JSExport]` method a `Span` parameter or return value.** Only the .NET
  runtime can create a `MemoryView`, and a `Span` cannot outlive the call; JS → .NET data goes
  as numbers/arrays or through a shared `ArraySegment`.
- **`[JSImport]`/`[JSExport]` need `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`** and the type
  must be `partial` and `[SupportedOSPlatform("browser")]`; the project declares
  `<SupportedPlatform Include="browser" />`.
- **Load the module once** with `WebRenderBackend.ImportModuleAsync()` before the first frame;
  the module URL is `/_content/Nosum.Rendering.Web/renderer.js` (site hosted at the root).
- **Exceptions must not cross the frame boundary.** A throw inside the frame entry is an
  interop exception per frame; catch at the frame entry, record, and stop the loop.

## Shape

```csharp
[SupportedOSPlatform("browser")]
public sealed partial class WebRenderBackend : IRenderBackend
{
    public void Submit(Span<byte> commands) => SubmitFrame(commands);

    [JSImport("submitFrame", ModuleName)]
    private static partial void SubmitFrame([JSMarshalAs<JSType.MemoryView>] Span<byte> commands);
}
```

Real file: `engine/Nosum.Rendering.Web/WebRenderBackend.cs`; JS side:
`engine/Nosum.Rendering.Web/wwwroot/renderer.js`.

## Measuring

Interop claims need numbers from `bench/Nosum.Bench.Interop`, run with `-c Release` in a real
browser. Record browser, sprite count, FPS and frame time in the PR.
