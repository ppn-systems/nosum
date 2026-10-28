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
- `wasm/Nosum.Wasm/App.razor`, `Pages/Home.razor`, `Components/` — host page and start/guard screens.
- `wasm/Nosum.Wasm.Controls/{Domain}/`, `Controls/Common/`, `Controls/Layout/` — reusable UI controls, HUD dock layout and standalone panel windows.
- `wasm/Nosum.Wasm.ViewModels/{Domain}/` — MVVM view models and `.resx` localization resources.
- `wasm/Nosum.Wasm/wwwroot/index.html` — host page; `<base href="/">` must match the deploy path
  because the render module is imported by absolute URL.

## Theme and layout (ADR-016, amended by ADR-017 and ADR-044)

- **Use the shared controls; never hand-roll UI.** Frames, buttons, fields, slots, tabs, sections, swatches, key caps, notices, badges, icons and spinners are the controls in
  `Nosum.Wasm.Controls/Common` (`GamePanel`, `TradeDialog`, `GameButton`, `GameInput` in a `GameField`, `GameSection` (a recessed card), `ItemGrid`/`ItemSlotButton`, `GameSwatch`, `GameKey`, `GameIcon`, `GameIconButton`,
  `GameNotice`, `GameBadge`, `GameSpinner`) and `Controls/Menu` (`MenuPage`). No raw `<button>`, `<input>` or styled `<div>` panel in a page or panel. Need something new? Add a shared control.
- **Standalone windows.** Every feature opens in `Center` as its own standalone `TradeDialog` window wrapping a `MenuPage` (content + status line), never crammed into a shared multi-tab menu (`ADR-044`). A new panel = one entry in `ToolbarViewModel.Items`
  (kind, label, key, `tab-*` sprite), one `case` in `Home.razor`, one sprite in `nosum-theme.js`.
- **No colours, fonts or borders in panel or page CSS.** Panel CSS is layout only, in UI pixels: `calc(var(--u) * n)`; spacing on the grid (2 u inside a group, 4 u between groups). Colours and sizes come from the `--nosum-*`
  variables in `wwwroot/nosum-theme.css`; sprites from `--nosum-img-*` (the rectangle table is in `wwwroot/nosum-theme.js`, art is linked from `assets/`, ADR-007).
- **Pixel rule.** `--u` = one world pixel (`round(width / 416)`, set by `nosum-theme.js`), never a fractional multiple; art is `image-rendering: pixelated`. Text is VT323 at `12.5 * --tp` px (title `tp + 1`, body `tp`, small),
  regular weight only; text never gets `font-weight`, `title` attributes or shadows of its own: use `data-tip` for tooltips.
- **Dock regions, not `position: fixed`.** In-game UI goes into a `GameHud` region (top-left, top-center, top-right, center = the menu, bottom-center, overlay). A control never positions itself.
- **Every state.** Hover, press, disabled and focus come from the shared CSS. In the game, buttons are not focusable (the canvas keeps the keyboard); on forms use `Focusable`.
- **Text via `.resx`**, both languages, same keys (a test checks it). Key caps (`Esc`, `E`) are the only literal text. Keep descriptions short: VT323 is monospaced and wraps early.
- Check a UI change in a browser at 1280 x 720 and 1920 x 1080, in vi and en (publish, serve `wwwroot`, run the host on 57300, screenshot with Playwright).
