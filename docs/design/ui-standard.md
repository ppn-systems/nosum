# Nosum UI standard (HUD and panels)

The one layout contract for every screen the player sees. Every change under `wasm/` that adds or moves something visible follows this
file; a PR that breaks a rule here is not merged. The rules are written against the existing theme (`nosum-theme.css`) and shared
controls (`wasm/Nosum.Wasm.Controls`); nothing here needs a new control or a new colour.

## 1. Units, type and spacing

| Token | Value | Use |
| :-- | :-- | :-- |
| `--u` | 3 px × UI scale | The only length unit. Every margin, padding, gap, border and size is a whole multiple of `--u`. No raw `px`, `rem` or `%` for spacing. |
| `--nosum-fs-title` | theme | Window titles only. |
| `--nosum-fs-body` | theme | All other text in windows and HUD panels. |
| `--nosum-fs-small` | theme | Badges, hotkey hints, slot counts, minimap labels. Never for sentences. |
| `--nosum-gap` | `--u` × 2 | Gap between items inside one group. |
| `--nosum-gap-group` | `--u` × 4 | Gap between groups, and the HUD edge margin. |

- Three font sizes exist; a fourth is a bug. No inline `font-size`.
- **Phone exception (390 px wide and narrower, `max-width: 640px`).** Body text is 14 px on a line of 17 px and small text is 12.5 px, set in
  `nosum-theme.css`. The UI pixel stays 2 px, so the phone layouts keep the fit they were measured at. A UI pixel of 3 (body 25 px, which is
  the 12.5 × tp rule) was tried and does not fit: the 8-column bag, the HUD regions and the touch controls on a 390 px screen all overflow.
  Tooltip descriptions use the body size; the line height always follows the size (an 11 px line under 14 px text overprints).
- Text never wraps word by word: a text block is at least 24 characters wide. If it does not fit, the layout is wrong, not the text.
- Colours come from the theme tokens only.

## 2. HUD regions

The HUD (`GameHud.razor`) has nine fixed regions. Each region owns a fixed list of elements; nothing is positioned with absolute
coordinates outside its region, and nothing floats over the world except the world-anchored items in section 4.

| Region | Holds (top to bottom / left to right) | Max |
| :-- | :-- | :-- |
| Top left | Quest tracker (one card) | 1 card, max width 30 `--u` × 8 |
| Top center | Banners (level up, season change, boss) — one at a time, queued | 1 |
| Top right, row 1 | Calendar bar, coin badge, Mail, Settings | 4 items, one row |
| Top right, column below | Minimap card (fixed size), then the **menu column** | — |
| Menu column | Square `GameIconButton`s, one column, right-aligned under the minimap, same size and frame | **8 buttons** |
| Center | Modal windows only (section 3) | 1 open |
| Bottom left | Touch joystick (touch only) | 1 |
| Bottom center | Toast line above the hotbar, then the hotbar, then the gauges to its right | toasts: 2 lines |
| Bottom right | Touch action buttons (touch only), emote button | 3 |

- The menu column holds at most 8 buttons. Anything beyond that goes into the game menu window (`GameMenuWindow`) as a tile, opened by
  the last button ("Menu"). A feature never adds a ninth column button or a second column.
- No element of one region may overlap another region at 1280×720, 1920×1080 or 390×844. Regions shrink by hiding content, not by
  overlapping.
- The minimap is one fixed-size card (same frame as the calendar bar). It never sits in the bottom row.

### 2.1 Who opens what (decision table)

Every panel has one door, chosen here. A panel that fits a merchant opens from that merchant's dialog box (`NpcActions`, ADR-081); the keyboard
shortcut stays for everything. The column is for panels a player opens from anywhere.

| Panel | Door | Why |
| :-- | :-- | :-- |
| Inventory | Column, `I` | Used all the time. |
| Map | Column, minimap click, `Tab` | One click (owner decision). |
| Journal | Column, `J` | One click (owner decision). |
| Skills | Column | Progress is checked often. |
| Crafting | Column | No merchant; made anywhere. |
| Trade | Column | Starts from another player, not a counter. |
| Friends | Column | Social, started from anywhere. |
| Mail | Top row, unread badge | One click (ADR-043 D4). |
| Settings | Top row, gear | One click (ADR-043 D4). |
| Wardrobe | Tailor's dialog box, `C` | Clothes are bought and worn at the tailor. Not in the column or the menu. |
| Backpack | Tailor's dialog box | Bought at the tailor's counter for coins. |
| Co-op | Board keeper's dialog box, `G` | Co-ops are formed around the town's shared goals on the board (owner decision). Not in the column or the menu. |
| Market | Trader's dialog box | A counter (ADR-034). |
| Goals | Board keeper's dialog box | A counter (ADR-030). |
| Pet | Game menu tile | A companion of the own farm, not a counter. |
| Visitors, Profile, Help | Game menu tile | Rarely used. |

The column holds seven buttons and the Menu button (eight in total, the most section 2 allows). Adding a panel means adding a row here first.

## 3. Windows (panels)

Every panel is a `GamePanel` opened in the center region:

- One frame (`assets/ui/menu.png` 9-slice), one title bar with the title at `--nosum-fs-title` on the left and the close button on the
  right. No panel draws its own frame, title or close button.
- Width is one of four sizes: **S** = 100 `--u`, **M** = 160 `--u`, **L** = 220 `--u`, and **XL** = 340 `--u` (clamped to the viewport minus the edge margin). XL is only for a panel with two grids side by side (the chest with the bag); such a panel stacks its two grids on a phone (640 px wide and under).
  Height fits the content; the panel body scrolls only when the viewport is shorter than the content (phones), never on desktop sizes
  for the content the panel normally shows (a full bag, a recipe list of one page).
- Inner layout uses `GameStack`/`GameRow`/`GameSection` with `--nosum-gap` / `--nosum-gap-group`. A detail strip (selected item, recipe
  cost) spans the full width at the bottom of the panel.
- Only one panel is open at a time; opening another closes the first. Esc and the close button close it.
- Item icons always use the shared icon control, whole sprite, centered, scaled down to fit the slot, never cropped.
- **Exception (ADR-101):** a dense list (the market listings) may use the compact slot, 12 UI pixels with an 8 px picture, `ItemSlotButton Compact="true"`. The picture rule is the same. No other list uses it.

## 4. Things anchored to the world

Only these may float over the world canvas, and all are drawn by the engine, not by Blazor:

- Name plates, health bars, damage numbers, emote and speech bubbles, the interaction reticle, the fishing line, bobber and bite "!".
- The quest waypoint: an arrow at the edge of the view while the place is off screen, kept 24 px in from the edge (docs/adr/ADR-093 D2). It is drawn by the
  engine; the minimap shows the same place as a dot.
- A name plate lifts above an exit's direction label when the farmer stands by the exit (docs/adr/ADR-093 D4).
- They are anchored to an entity or tile, follow the camera, and never cover the hotbar or a HUD region.

Blazor text cards (toasts, hints, catch results) never appear in the middle of the map. They go to the toast line (bottom center) or a
banner (top center).

## 5. Notices, toasts, banners and popups

| Kind | Where | Lifetime | Example |
| :-- | :-- | :-- | :-- |
| Toast | Bottom center, above the hotbar, max 2 stacked | 3 s, then fades | "Hãy xới đất trước.", "+1 Cá chép" |
| Hint | Same toast line, styled as a hint, one at a time; a new one waits until the player is idle for 2 s | until done | onboarding steps (ADR-093 D5) |
| Banner | Top center, one at a time, queued | 3–4 s | level up, season change |
| Confirm | `ConfirmDialog`, center, modal | until answered | selling 50 items |
| Context card | Inside the open panel only | while the panel is open | item tooltip |

A feature does not invent a new kind of popup. Toasts never overlap each other, the hotbar or the touch controls.

## 6. Touch and small screens (390×844)

- The menu column collapses into one "Menu" button that opens `GameMenuWindow`, and that is the only button of the column. The minimap has no
  button of its own on a phone: the world map is a tile of the Menu window, like every column panel. The Menu button shows its name beside it.
- Panels take the full width minus the edge margin; their content stacks vertically.
- Touch targets are at least 42 px square (`--nosum-touch`, which is 14 `--u` on a desktop at 3 px). Text buttons and the icon buttons of a window
  take this size on a phone; the HUD row keeps its own size.

## 7. Review checklist (every UI PR)

A PR that changes anything visible attaches screenshots at **1280×720, 1920×1080 and 390×844** (touch) of every screen it touches,
before and after. It is merged only if every answer is yes:

1. Every element sits in its region from section 2; no new floating Blazor card over the world.
2. Nothing overlaps: HUD regions, panels, toasts, hotbar, touch controls.
3. The menu column has one column and at most 8 buttons; all buttons are the same size and frame.
4. Edges line up: cards in one region share left/right edges; the top-right row and the minimap card share their right edge.
5. Panels use `GamePanel` with one of the S/M/L/XL widths and the shared title bar and close button.
6. Only the three font sizes are used; no text wraps word by word; nothing is clipped or cut off.
7. No inner scrollbar at 1280×720 and above for normal content.
8. Item icons are whole and centered.
9. Spacing uses `--u` multiples and the gap tokens only.
10. Vietnamese and English both fit (check the longer of the two).

## 8. Buttons

Every button is one of five kinds. A new button uses one of them; a new kind, or a per-screen override of a kind's size, face or state, needs this section changed first (and the checklist in section 7 run again).

| Kind | Control | Face | Use it for |
| :-- | :-- | :-- | :-- |
| `primary` | `GameButton` (the default) | wood | The one main action of a panel, dialog or form: Save, Confirm, Buy, Collect, Place, Sell. |
| `secondary` | `GameButton Variant="secondary"` | stone | Any other action: Cancel, Search, Sort, Decline, pager arrows, a toggle (it shows gold while on). |
| `danger` | `GameButton Variant="danger"` | red | An action that removes, ends or gives up something: Remove, Block, Leave, Kick, Sign out, Cancel a trade. |
| `tab` | `GameButton Variant="tab"` | stone, gold when active | One item of a tab bar or a choice of one value out of several (language, UI scale, pet kind). Exactly one is active. |
| `icon` | `GameIconButton` | the theme sprite, square | A sprite-only button: the HUD panel buttons, the settings gear, mail, emote, minimap. Its `Tip` is the tooltip and the accessible name. |

**Size and padding (tokens in `nosum-theme.css`).** Every text button (the four text kinds) is the same height and padding; the kind changes the face only.

| Token | Value | Use |
| :-- | :-- | :-- |
| `--nosum-button-h` | `--u` × 14 | The height of every text button. It is also the touch minimum (section 6). |
| `--nosum-button-pad-top` / `-bottom` | `--u` × 2 / × 4 | Vertical padding; the bottom is the 5 u foot of the frame, so the label sits over the face. |
| `--nosum-button-pad-x` | `--u` × 5 | Horizontal padding. The width comes from the label. |
| `--nosum-button-icon` | `--u` × 16 | The side of an `icon` button. |
| `--nosum-button-sink` | `--u` × 2 | How far the label or sprite sinks while pressed. |

A label is one line and never wraps (`white-space: nowrap`). A label that does not fit is shortened in its `.resx`, not wrapped.

**States.** Every kind implements the same states, from the shared CSS only; a panel adds none of its own.

| State | Look (every kind, unless the row says otherwise) | Token |
| :-- | :-- | :-- |
| Default | The kind's face and the cream label with its shadow. | the frame sprite |
| Hover (enabled) | The face lightens and a thin highlight sits just inside the bevel. The icon lightens its art and gets the same highlight. | `--nosum-button-hover`, `--nosum-button-highlight` |
| Pressed | The frame without its shadow; the face darkens and the label (the sprite, for `icon`) sinks 2 u. | `--nosum-button-pressed`, `--nosum-button-sink` |
| Disabled | Greyed and darker. The label keeps its colour and shadow, so it stays readable (issue #207). No hover and no press. | `--nosum-button-disabled` |
| Focus-visible | A focus ring of 1 u, 1 u outside the button. Reached with Tab; in the game the canvas keeps the keyboard (section 3). | `--nosum-focus` |
| Active | Gold face and dark ink. Allowed on every text kind; used by `tab` and by toggles. `icon` shows a gold outline. | the gold frame sprite |

**Icon buttons.** A button with no words that shows a theme sprite is a `GameIconButton`: one size (`--nosum-button-icon`), one sprite, the same states as the table above. A text button never draws its own sprite as its face. A small icon inside a text button (a pager arrow, a `GameIcon`) is fine.

**Not buttons of this kind.** Some controls are clickable but are not one of the five kinds and keep their own look: the colour `GameSwatch`, the inventory `ItemSlotButton`, the window close control in `GamePanel`, the minimap, and the menu tiles of `GameMenuTiles`. A new clickable control of that sort is a new control and goes through this section first.

**Text and keyboard.** Labels come from `.resx` (section 1). A button that is not reached by Tab in the game (`tabindex="-1"`) is a deliberate choice of the screen, not a style.
