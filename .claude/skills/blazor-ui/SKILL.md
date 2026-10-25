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
  re-validates everything (`gameplay-rules`).
- **Every visible string through `.resx`** (`vi` default + `en`) via `IStringLocalizer` — see
  the `i18n` skill.
- **Trimmed, no AOT** (`wasm/Directory.Build.props`). Changing this needs `bench/` numbers and
  an ADR-002 amendment.

## Files

- `wasm/Nosum.Wasm/Program.cs` — root components and DI registration.
- `wasm/Nosum.Wasm/App.razor`, `Layout/`, `Pages/{Domain}/` — UI.
- `wasm/Nosum.Wasm/wwwroot/index.html` — host page; `<base href="/">` must match the deploy path
  because the render module is imported by absolute URL.

## Theme and layout (ADR-016)

- **Use the shared controls; never hand-roll UI.** Frames, buttons, fields, slots, tabs, swatches, key caps, notices, badges and spinners are the controls in
  `Nosum.Wasm.Controls/Common` (`GamePanel`, `GameButton`, `GameInput` in a `GameField`, `ItemGrid`/`ItemSlotButton`, `GameSwatch`, `GameKey`, `GameIcon`,
  `GameNotice`, `GameBadge`, `GameSpinner`). No raw `<button>`, `<input>` or styled `<div>` panel in a page or panel. Need something new? Add a shared control.
- **No colours, fonts or borders in panel or page CSS.** Panel CSS is layout only, in UI pixels: `calc(var(--u) * n)`. Colours and sizes come from the `--nosum-*`
  variables in `wwwroot/nosum-theme.css`; sprites from `--nosum-img-*` (the rectangle table is in `wwwroot/nosum-theme.js`, art is linked from `assets/`, ADR-007).
- **Integer scale.** `--u` is one UI pixel (2, 3 or 4 CSS px); art is `image-rendering: pixelated`; never use a fractional multiple.
- **Dock regions, not `position: fixed`.** In-game UI goes into a `GameHud` region (top-left, top-center, top-right, left, center, right, bottom-center, overlay).
  The center holds one modal panel (`PanelKind`); `Esc` closes it, `H` toggles help. A control never positions itself.
- **Every state.** Hover, press, disabled and focus come from the shared CSS. In the game, buttons are not focusable (the canvas keeps the keyboard); on forms use `Focusable`.
- **Text via `.resx`**, both languages, same keys (a test checks it). Key caps (`Esc`, `E`) are the only literal text.
- Check a UI change in a browser at 1280 x 720 and 1920 x 1080 (publish, serve `wwwroot`, run the host on 57300, screenshot with Playwright).
