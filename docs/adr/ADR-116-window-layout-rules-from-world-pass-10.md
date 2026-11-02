# ADR-116: Window layout rules from world QA pass 10

## Status

Accepted

Amends [ADR-044](ADR-044-standalone-panels-and-hud-separation.md) (the standalone window's place in the centre region) and the HUD rules of
[ui-standard.md](../design/ui-standard.md) sections 2, 3 and 5. No wire change: `NosumProtocol.Version` (34) and every opcode stay as they are.

## Context

World QA pass 10 ([docs/qa/world-pass10/README.md](../qa/world-pass10/README.md)) graded every window and toast at 1280x720, 1920x1080 and
390x844 in Vietnamese and English. Its ranked issues 3, 5, 6, 7 and 9 were layout faults: a toast or hint floated over the map with an empty
strip under it, the co-op window covered the minimap and the hotbar was missing, the furniture panel was docked top-left with its tab row cut
off, two panels clipped their bodies at 1920, and some text wrapped a single word onto its last line.

## Decision

- **D1 - The toast line sits on the hotbar's frame.** The badge strip (combo chip and level-up badge, 14 u tall) moves out of the hotbar and
  stands above the toast line (`HotbarBadges`, rendered first in the bottom centre). The toasts and the first-run hint now sit directly above
  the hotbar's frame, so no card floats over the world with an empty band beneath it. The strip keeps its fixed height, so a badge never
  moves the toast line.
- **D2 - The hotbar stays drawn under a window only for the co-op.** From 1600 px (where the menu column also stays), the hotbar is drawn
  under the co-op window, display-only (no pointer events): the co-op is about 400 px tall and clears the hotbar's top edge at 1920 x 1080.
  Every other window hides the hotbar at every width, because the profile and the arrange panel are taller than the room above the hotbar.
  Below 1600 px the hotbar is hidden under any window, as before (the column rule of ui-standard section 2).
- **D3 - An XL window starts below the minimap card from 1600 px.** The centre region's top moves to 72 u when an XL window is open at 1600 px
  and up, so the widest window (the co-op, 340 u) does not cover the minimap card (checklist item 2). The narrower windows keep the centre.
- **D4 - The arrange panel is a centred window.** The furniture panel moves from the top-left dock to the centre region, a standard M window
  (section 3). Its category tabs wrap onto further rows instead of scrolling sideways, so no tab is cut off (checklist item 6). The panel is
  taller than the room above the hotbar, so the hotbar is hidden under it at every width. **Trade-off:** a centred M window covers the middle of
  the room while the player arranges furniture. The previous top-left dock kept the room open; the owner asked for the window to be centred.
- **D5 - The profile is an L window.** Its Vietnamese text runs to five lines at M and clipped the sign-out row at 1920; at L it fits without
  an inner scroll (checklist item 7).
- **D6 - Orphan words are joined.** Where a string's last line held one word, the last two words are joined with a no-break space in the
  `.resx` value (the keys are unchanged, so the resource pairing holds). This is typography, not new text.
- **D7 - A confirm has no close header.** A `ConfirmDialog` is a question with its own answers (`Cancel` is its close). Section 3's close
  button applies to windows, so the sign-out confirm keeps its three buttons and no header.

Not decided here: issue 4 of pass 10 (at 1280 x 720 a window hides the minimap, the menu column and the hotbar). It needs an owner decision on
whether a desktop-size hide is intended; this ADR does not change it.

## Consequences

- The inventory at 1920 x 1080 still clips its "In hand" description under a fade: its body is about 880 px and the window has about 895 px.
  Fixing it needs a layout choice (for example, the sort row and the hint line in one row), left for a later change.
- The menu column, the minimap and the hotbar keep their regions (ui-standard section 2). No region gained an element.
- Harness: `tests/browser/world-new-player.cjs` walks off the farm's east edge (the bus stop exit, `WarpRule.TryEdge`) instead of pressing at
  the door, and opens the map from the Menu window on a phone.
