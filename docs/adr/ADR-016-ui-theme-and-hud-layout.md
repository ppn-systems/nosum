# ADR-016 — One pixel-art UI theme and one HUD layout system

**Status:** Proposed. D2, D4, D5 and D6 are superseded by [ADR-017](ADR-017-game-menu-and-world-pixel-ui.md) (one game menu, UI pixel = world pixel, VT323, a near-empty HUD); D1, D3 and D7 stand.

## Context

The UI overlay (Blazor, [ADR-002](ADR-002-batched-render-interop.md)) was plain white or brown HTML boxes, each control and panel placing itself with
`position: fixed` and its own colours. Panels overlapped (the furniture arrange panel covered the session bar), the account form was unstyled, and
nothing matched the pixel-art world. The placeholder content ([ADR-007](ADR-007-placeholder-content-16px.md)) ships UI art: `Maps/MenuTiles.png`
(wooden window frames), `LooseSprites/Cursors.png` (buttons, icons, cursors) and `LooseSprites/textBox.png` (text field). Those sheets mix two kinds
of art: "fat" 4 x 4 pixel blocks (MenuTiles, textBox) and ordinary 1:1 pixels (most of Cursors).

## Decision

- **D1 — One theme, in the shared controls.** Colours, fonts and sizes are CSS variables in `Nosum.Wasm.Controls/wwwroot/nosum-theme.css`; the
  frames, buttons, fields, slots and icons are drawn by the shared controls (`GamePanel`, `GameButton`, `GameInput`, `ItemSlotButton`, `GameSwatch`,
  `GameKey`, `GameIcon`, `GameNotice`, `GameBadge`, `GameSpinner`, `GameField`). A panel or page uses those controls and the variables and adds no colour,
  border or font of its own; panel CSS is layout only (widths, gaps) in UI-pixel units. There is one game theme (no light/dark).
- **D2 — Integer pixel scale.** `--u` is one UI pixel in CSS pixels: 2 px by default, 3 px from 1500 x 850, 4 px from 2400 x 1300, 1 px on tiny screens.
  Thin art is drawn at `sheet px * --u`; fat art at `sheet px / 4 * --u` (one block = one UI pixel), so every art pixel is a whole number of CSS pixels.
  Everything is `image-rendering: pixelated`; sizes are `calc(var(--u) * n)`.
- **D3 — Sprites are cut at start-up, not copied.** The three sheets are linked from `assets/` into the Controls package (`Content Link`, like the map
  assets) and `wwwroot/nosum-theme.js` cuts the listed rectangles on a canvas and publishes them as CSS custom properties (`--nosum-img-*`, usable as
  `border-image` for 9-slice frames). That script's table is the single list of rectangles (see the table below). Without the sprites (script failed)
  the controls fall back to plain CSS borders in the same colours. States (hover, press, disabled, danger) are filters on the frame, not extra sprites.
- **D4 — Text is a system font, not the bitmap fonts.** `font_bold.png` and `font_colored.png` have no Vietnamese diacritics, which the game's default
  language needs. Text uses a clean system sans-serif stack at fixed sizes, bold, with hard one-pixel shadows; the frames carry the pixel look. No remote font.
- **D5 — Dock regions.** `GameHud` is the only layout of the in-game UI: top-left, top-center, top-right, left (panel buttons), center (one big panel at a time),
  right (contextual panels such as the arrange panel), bottom-center (message line, hotbar) and an overlay for dialogs. A control never positions itself;
  the page puts it in a region, so regions cannot overlap. `GamePanel.Dock` and `Scale` parameters of the old API are accepted and ignored.
- **D6 — One modal panel, closed by Escape.** The center region holds at most one of inventory, market, wardrobe, trade, co-op or help
  (`PanelKind`); `Esc` closes it (and the upgrade form), `H` or `?` toggles the help card. The keys are `InputEdges.Close` and `InputEdges.Help`, batched
  like the other keys.
- **D7 — Forms.** Fields and buttons on the account form are `Focusable` (keyboard reachable, visible focus ring); in the game buttons stay unfocusable so the
  canvas keeps the keyboard.

### Sprite rectangles (`nosum-theme.js`)

| Variable | Sheet | x, y, w, h | Used for |
|:--|:--|:--|:--|
| `panel` | MenuTiles.png | 0, 256, 60, 60 | window frame (9-slice, 16 px corners) |
| `slot` | MenuTiles.png | 128, 128, 64, 64 | item slot bevel (4 px) |
| `select` | Cursors.png | 448, 128, 64, 64 | red selection frame (8 px) |
| `field` | textBox.png | 4, 0, 188, 48 | text field and badge (12 px) |
| `button`, `button-hot`, `button-danger` | Cursors.png | 145, 338, 13, 9 / 159, 338, 13, 9 / 137, 338, 7, 9 | buttons (3 px) |
| `close` | Cursors.png | 337, 494, 12, 12 | panel close button |
| `arrow-left`, `arrow-right` | Cursors.png | 352, 495, 12, 11 / 365, 495, 12, 11 | pager |
| `coin` | Cursors.png | 193, 373, 9, 9 | coin counter |
| cursors | Cursors.png | 0, 0, 8, 10 (arrow) / 32, 0, 10, 10 (hand) | mouse cursors |

## Alternatives considered

- **Pre-cut PNGs in the repository** — derived copies of third-party art that would have to be replaced again; cutting at runtime keeps `assets/` the single copy.
- **A bitmap-font web font** — no diacritics (D4).
- **Per-panel CSS with shared variables only** — leaves each panel free to drift; D1 puts the look inside the controls.
- **CSS Grid areas in each page** — the layout would be re-invented per page; D5 makes it a control.

## Consequences

- Replacing the placeholder art means changing the three linked files and the rectangle table; no control changes.
- The sprite script runs before Blazor starts; for a moment the plain-border fallback may show.
- The pre-boot screen and Blazor's error bar in `index.html` are static English text (they exist before any localization can load).
- Key legends (`Esc`, `Space`, `WASD`) are shown as printed on keyboards and are not translated; their descriptions are.

## Revisit when

The art remake replaces the placeholder sheets, or a second theme (for example high contrast) is wanted.
