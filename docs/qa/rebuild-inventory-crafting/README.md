# Rebuild inventory and crafting: captures and grading

Captures for the critique in `docs/design/critique-inventory-crafting.md` and the changes that follow it. Each screen is captured at
1280×720, 1920×1080 and 390×844 (touch), in Vietnamese (`vi`) and English (`en`), before and after the change.

| Folder | What |
| :-- | :-- |
| `before/{vi,en}/{viewport}/` | `master` at the start of the branch: HUD, bag, crafting. |
| `after/{vi,en}/{viewport}/` | This branch. |
| `audit-before-{vi,en}.txt`, `audit-after-{vi,en}.txt` | Per-run log: the window boxes, whether the craft column button was found, page errors. |
| `interact-en.txt`, `interact-vi.txt` | Interaction checks against the real server: split, shift-click, hint text. |

Produced by `tests/browser/inventory-crafting-capture.cjs` and `tests/browser/inventory-interact-check.cjs`.

Not captured: the chest page. Reaching it needs a placed chest on the farm, which the capture does not do. Its shift-click and hint are
unchanged by this branch.

## Grading against `docs/design/ui-standard.md` §7

The checklist from §7, applied to the bag and the crafting page (after this branch). "Pass" means the frames show it; "not measured" means
the capture did not check it.

| # | Item | Bag | Crafting | Note |
| :-- | :-- | :-- | :-- | :-- |
| 1 | Every element in its region | Pass | Pass | Both are modal windows in the centre region. |
| 2 | Nothing overlaps | Pass | Pass | At 1280 and 390 the windows clear the top-right column and the top row. |
| 3 | Menu column: one column, at most 8 buttons | **Fail (pre-existing)** | **Fail (pre-existing)** | 13 buttons in two columns; see critique F5. Not changed here. |
| 4 | Edges line up | Not measured | Not measured | |
| 5 | `GamePanel` with an S/M/L/XL width | Not measured | Not measured | The capture reads boxes, not the panel size token. |
| 6 | Three font sizes, no word-by-word wrap, nothing clipped | Pass | Pass | The English hint wraps to two lines at 390 px, at a word boundary. |
| 7 | No inner scrollbar at 1280 and up | Not measured | Not measured | `ui-pass-capture.cjs` measures scroll boxes; this capture does not. |
| 8 | Item icons whole and centred | Pass | Pass | |
| 9 | Spacing uses `--u` and the gap tokens | Pass | Pass | The new hint line uses `--u` padding and the body size. |
| 10 | Vietnamese and English both fit | Pass | Pass | The Vietnamese hint fits one line at 1280; the English one wraps at 390. |

Rows 3 fails for both pages because of the menu column, which this branch does not touch. Rows 4, 5 and 7 are not measured by this capture;
the existing `ui-pass` audit covers 7 for the panels.

## Changes that the captures show

- **Bag:** a hint line under the toolbar (`Inventory_Hint`, `Inventory_HintPicked`). Shift-click and right-click work on the slots; the
  interaction check confirms the split and the quick move.
- **Crafting:** a third button, "Craft max xN" (`Craft_ButtonMax`), beside Craft and Craft x5. The ×5 button is now always shown.
