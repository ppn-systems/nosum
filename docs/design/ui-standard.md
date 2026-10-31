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

## 3. Windows (panels)

Every panel is a `GamePanel` opened in the center region:

- One frame (`assets/ui/menu.png` 9-slice), one title bar with the title at `--nosum-fs-title` on the left and the close button on the
  right. No panel draws its own frame, title or close button.
- Width is one of three sizes: **S** = 100 `--u`, **M** = 160 `--u`, **L** = 220 `--u` (clamped to the viewport minus the edge margin).
  Height fits the content; the panel body scrolls only when the viewport is shorter than the content (phones), never on desktop sizes
  for the content the panel normally shows (a full bag, a recipe list of one page).
- Inner layout uses `GameStack`/`GameRow`/`GameSection` with `--nosum-gap` / `--nosum-gap-group`. A detail strip (selected item, recipe
  cost) spans the full width at the bottom of the panel.
- Only one panel is open at a time; opening another closes the first. Esc and the close button close it.
- Item icons always use the shared icon control, whole sprite, centered, scaled down to fit the slot, never cropped.

## 4. Things anchored to the world

Only these may float over the world canvas, and all are drawn by the engine, not by Blazor:

- Name plates, health bars, damage numbers, emote and speech bubbles, the interaction reticle, the fishing line, bobber and bite "!".
- They are anchored to an entity or tile, follow the camera, and never cover the hotbar or a HUD region.

Blazor text cards (toasts, hints, catch results) never appear in the middle of the map. They go to the toast line (bottom center) or a
banner (top center).

## 5. Notices, toasts, banners and popups

| Kind | Where | Lifetime | Example |
| :-- | :-- | :-- | :-- |
| Toast | Bottom center, above the hotbar, max 2 stacked | 3 s, then fades | "Hãy xới đất trước.", "+1 Cá chép" |
| Hint | Same toast line, styled as a hint, one at a time | until done | onboarding steps |
| Banner | Top center, one at a time, queued | 3–4 s | level up, season change |
| Confirm | `ConfirmDialog`, center, modal | until answered | selling 50 items |
| Context card | Inside the open panel only | while the panel is open | item tooltip |

A feature does not invent a new kind of popup. Toasts never overlap each other, the hotbar or the touch controls.

## 6. Touch and small screens (390×844)

- The menu column collapses into one "Menu" button that opens `GameMenuWindow`; the minimap collapses into a button.
- Panels take the full width minus the edge margin; their content stacks vertically.
- Touch targets are at least 14 `--u` square.

## 7. Review checklist (every UI PR)

A PR that changes anything visible attaches screenshots at **1280×720, 1920×1080 and 390×844** (touch) of every screen it touches,
before and after. It is merged only if every answer is yes:

1. Every element sits in its region from section 2; no new floating Blazor card over the world.
2. Nothing overlaps: HUD regions, panels, toasts, hotbar, touch controls.
3. The menu column has one column and at most 8 buttons; all buttons are the same size and frame.
4. Edges line up: cards in one region share left/right edges; the top-right row and the minimap card share their right edge.
5. Panels use `GamePanel` with one of the S/M/L widths and the shared title bar and close button.
6. Only the three font sizes are used; no text wraps word by word; nothing is clipped or cut off.
7. No inner scrollbar at 1280×720 and above for normal content.
8. Item icons are whole and centered.
9. Spacing uses `--u` multiples and the gap tokens only.
10. Vietnamese and English both fit (check the longer of the two).
