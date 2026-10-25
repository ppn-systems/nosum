---
name: blazor-ui
description: Use when creating or changing anything under wasm/ — .razor pages, layouts, ViewModels, Program.cs, wwwroot, socket wiring into the UI. Canvas vs UI split, MVVM, no StateHasChanged per frame, no socket calls from .razor, WASM bundle exposure.
---

# Blazor UI (wasm/)

Frame loop and drawing: `render-interop`. Transport internals: `nalix-client-sdk`.

## Non-negotiables

- **Blazor renders UI, not the world.** The farm, crops and characters are drawn by the engine
  on one `<canvas>`. Inventory, shop, chat, quests, dialogs are Razor components overlaid on it.
- **No `StateHasChanged` per frame.** The engine raises events when UI-visible state changes
  (item picked up, coins changed); the view model updates and the component re-renders then.
- **MVVM.** A page binds to a view model in `Nosum.Wasm.ViewModels/{Domain}/`; the view model
  calls the socket layer. A `.razor` file never touches `WebSocketSession` or
  `Nosum.Wasm.Socket` directly.
- **Network callbacks are not on the render context.** Marshal back with
  `InvokeAsync(StateHasChanged)` (or the view model's change notification) — never mutate
  component state from a socket callback directly.
- **Unsubscribe.** Every subscription a component or view model takes is disposed in
  `Dispose`/`DisposeAsync`.
- **The bundle is public.** Everything in the published `wwwroot/_framework` is downloadable:
  no secrets, keys, admin endpoints or server-only rules in `wasm/` projects. The server
  re-validates everything (`game-rules`).
- **Trimmed, no AOT** (`wasm/Directory.Build.props`). Changing this needs `bench/` numbers and
  an ADR-002 amendment.

## Files

- `wasm/Nosum.Wasm/Program.cs` — root components and DI registration.
- `wasm/Nosum.Wasm/App.razor`, `Layout/`, `Pages/{Domain}/` — UI.
- `wasm/Nosum.Wasm/wwwroot/index.html` — host page; `<base href="/">` must match the deploy path
  because the render module is imported by absolute URL.
