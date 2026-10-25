# ADR-017 — One game menu, a UI pixel equal to the world pixel, a pixel font, a near-empty HUD

**Status:** Accepted (coordinator, 2026-10-25, under the owner's delegation; VT323 kept over Handjet — the dot-matrix look reads worse at small sizes. Follow-ups required by the review: content-sized menu window, uniform parchment fill, language switch, farmhouse zoom; tracked in the UI polish PR). Supersedes [ADR-016](ADR-016-ui-theme-and-hud-layout.md) D4, D5 and D6 and amends its D2 (owner decision, 2026-10-25: the UI was cluttered and
"ugly"; it must be tidy). D1 (one theme in the shared controls), D3 (sprites cut at start-up from the linked sheets) and D7 (forms) of ADR-016 stand.

## Context

ADR-016 gave the UI one theme, but the result was still crowded and flat: six always-visible boxes over the world, a button column, one loose panel per feature,
every panel the same orange card, a UI pixel (2 px) smaller than the world pixel (3 px at 1280 x 720), system fonts with shadows, and permanent hint paragraphs.
The engine zooms the open map by `round(canvas width / 416)` (26 tiles of 16 px, never below 2, `MapScene.ScaleFor`), so the world pixel is 3 CSS px at
1280 x 720 and 5 at 1920 x 1080; the placeholder UI art (ADR-007) is drawn in Stardew at exactly that zoom.

## Decision

- **D1 — One game menu.** Inventory, wardrobe, market, trade, co-op, profile, settings (and a shop while one is open) are the tabs of one window (`GameMenu`, fixed size
  200 x 186 UI pixels, so tabs never make it jump). The tabs are 16 x 16 icon tabs on the window's top edge; the window title is the active tab's name; the tooltip shows
  the name and the key. `Esc` closes the menu, or opens it on the tab used last; `I M C T G N` open it on that tab; `H` or `?` and the gear button on the HUD open the
  settings tab. A shop opens on its own tab (titled by the shop) when the action key is pressed at its counter, and the tab disappears with it. A new tab is **one entry**
  in `ToolbarViewModel.Items` (kind, label, key, sprite), one `case` in `Home.razor`, one sprite in `nosum-theme.js`. Tab pages are `MenuPage`s (scrolling content and a status
  line), never windows of their own. While the menu is open the HUD widgets under it are hidden.
- **D2 — The UI pixel is the world pixel.** `--u` (one UI art pixel, CSS px) is computed by `nosum-theme.js` with the engine's own formula, `round(width / 416)` clamped to 2..6,
  and capped at `floor(height / 204)` so the menu always fits (the cap only bites on very wide, short windows). Thin art (Cursors.png, own glyphs) is drawn at `sheet px * --u`, fat art
  (MenuTiles.png, textBox.png: 4 x 4 blocks) at `sheet px / 4 * --u`, `image-rendering: pixelated`, integer sizes only: 3 px at 1280 x 720, 5 px at 1920 x 1080. In a small room the
  engine zooms further in (a farmhouse is drawn at 5 at 1280 x 720), but the UI keeps the open-map size, so its layout does not change between maps.
- **D3 — Text is VT323.** The UI font is VT323 (SIL OFL 1.1, `Nosum.Wasm.Controls/wwwroot/fonts/`, licence in `OFL-VT323.txt`), a 5 x 7 pixel font with all Vietnamese diacritics (checked:
  every precomposed letter of Vietnamese, including `ẩ ợ ữ Đ`). Bundled and served with the app; nothing is fetched at run time. Alternatives checked and rejected: Pixelify Sans, Silkscreen,
  Press Start 2P, Jersey, Micro 5, DotGothic16 (no or incomplete Vietnamese); Handjet (full coverage, but a dot-matrix look and a variable font). One font pixel is `--tp` CSS px (`floor(2 u / 3)`:
  2 px at 1280 x 720, 3 px at 1920 x 1080), a size is `12.5 * --tp` px, so glyphs never blur. **Type scale** (all of the UI): title `(tp + 1) * 12.5` (window titles, the logo is larger),
  body `tp * 12.5` (everything), small `max(tp - 1, 2) * 12.5` (hotkeys, tooltip descriptions, hints; equal to body at 1280 x 720 because VT323 has no legible size between). One line height, `11 tp`.
  Only regular weight (no synthetic bold). Names above heads are drawn by the renderer (see D6), not by this font.
- **D4 — Frames, sections, slots.** Hierarchy: the window (MenuTiles `panel`, 9-slice, 4 u border), a section (MenuTiles `inset`, a recessed card, 2 u border), a slot (MenuTiles `slot` bevel, 18 u square).
  Buttons are Cursors.png sprites with hover, pressed and disabled states in the same palette (a disabled button is dimmer, never grey). Spacing is on one grid: 2 u inside a group, 4 u between groups, 4 u to the screen edge.
- **D5 — The HUD shows almost nothing.** Top-left: the quest tracker (title and one checkbox line per objective; click the title to fold it; the long hint is its tooltip). Top-right: the coin
  counter (coin and amount) and the settings gear. Bottom-centre: the hotbar (slot numbers under the slots, never over the picture; stack counts bottom-right with an outline; the selected slot has the red
  frame), a one-line message that fades after 2.5 s, and, in a furniture place, a one-line strip of key caps (`E` place, `R` rotate, `F` move, `Q` stow, the last three clickable) at the top. The connection
  badge appears only when the connection is not fine. Account, sign out, language and the key help live on the settings tab. Nothing else is on screen.
- **D6 — Inventory tab (Minecraft style).** On top: the character preview (the same farmer frames and offsets as the world sprite, one sheet pixel = one UI pixel) with hat, shirt and trousers slots (click a worn piece
  to take it off; pick a clothing stack, then click the matching slot to wear it) and a card with the item in hand. Below: the bag, a gap, then the hotbar row (the same nine slots as the HUD). Item names and
  descriptions are the shared tooltip (D7). The wardrobe tab keeps skin, hair style and dye beside the preview. Nickname labels over heads stay small (1x at every zoom) and names that would overlap are stacked
  upwards in `renderer.js`.
- **D7 — One tooltip.** Any element with `data-tip` shows a tooltip (first line the name, the rest the description), created and positioned by `nosum-theme.js`; controls use it instead of `title`.
- **D8 — The satellite resources are loaded.** `index.html` starts Blazor with `loadAllSatelliteResources`, so `Language.en.resx` is downloaded and an English account sees English (before, the runtime only loaded the
  resources of the culture it started with, so English never appeared after sign-in).

### Sprite rectangles added to or changed in `nosum-theme.js` (ADR-016 table otherwise unchanged)

| Variable | Sheet | x, y, w, h | Used for |
|:--|:--|:--|:--|
| `inset` | MenuTiles.png | 0, 320, 60, 60 | section card (9-slice, 8 px = 2 u) |
| `tab-inventory`, `tab-coop`, `tab-market`, `tab-shop` | Cursors.png | 0, 368 / 32, 368 / 80, 368 / 0, 384 (16 x 16) | menu tabs |
| `tab-wardrobe`, `tab-trade`, `tab-profile`, `tab-settings` | Cursors.png (blank tab 16, 368) + own glyph | 16, 368, 16, 16 | menu tabs and the gear button |
| `glyph-hat`, `glyph-shirt`, `glyph-pants` | own glyphs | 11 x 4, 11 x 8, 9 x 9 | faint picture in an empty equipment slot |
| backdrop | `LooseSprites/Cloudy_Ocean_BG.png` | whole file (1500 x 520) | start screen, drawn at 1 art pixel = 1 u |

The glyphs (gear, shirt, hat, trousers, person, swap arrows) are Nosum's own 1-bit pictures in the table at the top of `nosum-theme.js`; they stay when the placeholder art is replaced.

## Alternatives considered

- **Keep the panel buttons and make each panel prettier** — the clutter and the loose windows were the complaint; one window is the fix.
- **A UI pixel of 2/3 or 1/2 of the world pixel** (denser UI) — breaks the "same pixel" rule and the UI would again look foreign next to the world.
- **A system font tuned to the pixel art** — the pixel font with full Vietnamese exists (VT323), so there is no reason to compromise.
- **A cursor-held item in the inventory** (drag-like) — the move intent is click-pick, click-place, which the grid already does (blinking frame on the picked stack); a ghost icon following the mouse needs a per-frame script and was left out.

## Consequences

- Windows are 200 x 186 u: 600 x 558 CSS px at 1280 x 720 (plus the tab row), 1000 x 930 at 1920 x 1080. A window or a short browser (under about 612 px tall at u = 3) lowers `--u` by one step.
- The game's `Esc` now opens the menu when nothing is open (it used to do nothing).
- Language is shown in the settings tab but cannot be changed there: the server has `SetLanguage`, the client has no request for it yet.
- VT323 is a monospaced face: text is wider than the old sans-serif for the same number of characters; sentences were shortened where they wrapped (key help).
- `HelpPanel`, `SessionBar`, `PanelToolbar` and the left/right HUD regions are gone; `GamePanel` only draws dialogs, HUD widgets and the menu window.

## Revisit when

The art remake replaces the placeholder sheets (the rectangles above change, the controls do not), or the player needs a language switch (add the client request, then a button in the settings tab).
