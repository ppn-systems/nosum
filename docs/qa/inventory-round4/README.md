# QA captures: inventory round four

Browser checks for [ADR-123](../../adr/ADR-123-a-tool-too-weak-for-a-node-is-refused-as-its-own-status.md) and the "In hand" item that
[ADR-116](../../adr/ADR-116-window-layout-rules-from-world-pass-10.md) left open, as graded in
[critique-inventory-crafting.md](../../design/critique-inventory-crafting.md) section 8 and [critique-gathering.md](../../design/critique-gathering.md) section 7e.
Captured against a Development server and a client published with `-p:NosumDevTools=true`, as a guest, at 1280×720, 1920×1080 and 390×844 in Vietnamese
and English.

Scripts: `tests/browser/inventory-hand-capture.cjs` (the held-item strip, every hotbar item, with the clipping check) and
`tests/browser/weak-tool-capture.cjs` (the refusal line from a basic axe at a large stump in the Woods).

## "In hand" at 1920 × 1080 (ADR-116's open item)

The hand strip was measured for every hotbar item in turn. Before the change, four frames put the description under the window's scroll fade: the
Axe and the Pickaxe in English, and the watering can and the axe in Vietnamese. After the change none does. Frames for the Axe (hotbar slot 6):

| Size | Vietnamese, before | Vietnamese, after | English, before | English, after |
| :-- | :-- | :-- | :-- | :-- |
| 1280×720 | `before/vi-1280x720-hand-axe.png` | `after/vi-1280x720-hand-axe.png` | `before/en-1280x720-hand-axe.png` | `after/en-1280x720-hand-axe.png` |
| 1920×1080 | `before/vi-1920x1080-hand-axe.png` | `after/vi-1920x1080-hand-axe.png` | `before/en-1920x1080-hand-axe.png` | `after/en-1920x1080-hand-axe.png` |
| 390×844 | `before/vi-390x844-hand-axe.png` | `after/vi-390x844-hand-axe.png` | `before/en-390x844-hand-axe.png` | `after/en-390x844-hand-axe.png` |

The cause and the fix are in `InventoryPanel.razor.css` (`.hand-stats`): the stats share the name's line, so the description no longer sits one line
lower. At 1280×720 the strip is one line shorter after the change.

## The refusal from a basic axe at a large stump (ADR-123)

A large stump in the Woods needs an iron axe; the guest's basic axe swings at the first one the node list reports (10,12 or 4,25, by the run). The line above the hotbar names the tool in hand and the
blacksmith, where the line used to read "This tool does not work on that.":

| Size | Vietnamese | English |
| :-- | :-- | :-- |
| 1280×720 | `vi-1280x720-weak-tool.png` | `en-1280x720-weak-tool.png` |
| 1920×1080 | `vi-1920x1080-weak-tool.png` | `en-1920x1080-weak-tool.png` |
| 390×844 | `vi-390x844-weak-tool.png` | `en-390x844-weak-tool.png` |

The Vietnamese line reads "Rìu chưa đủ sức cho thứ này. Nâng cấp ở thợ rèn nhé." and the English "Your Axe is too weak for that. Upgrade it at the
blacksmith."

## Grading against ui-standard.md section 7

- The hand strip sits inside its window at all three sizes and in both languages (the measurement above; no clipping row).
- The refusal line sits above the hotbar frame at 1280×720 (the toast line, ADR-116), and covers no slot.
- No region overlaps another in the captured frames.

## Not captured

- **The furnace refusal.** The smelt buttons are disabled when the player holds too little, so the client never sends a start the server refuses; the
  text is pinned by `CraftingTextTests` instead.
- **The long press that splits a stack on a phone.** Headless touch does not produce it (critique-inventory-crafting.md section 8).
- **Copper ore and the tree health bar** (critique-gathering.md section 7e).
