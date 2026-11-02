# QA captures: gathering round 3

Browser check for [ADR-114](../../adr/ADR-114-energy-shows-the-day-rollover-and-the-axe-text.md) and the open items of
[critique-gathering.md](../../design/critique-gathering.md) section 7d. Captured by `tests/browser/energy-rollover-capture.cjs` against a Development server
and a client published with `-p:NosumDevTools=true` (branch `work/critique-gathering-inventory`).

## What the frames show

The guest is granted a copper hoe and tills four farm tiles (four uses at 3 energy each). The HUD energy then reads **258 / 270** at every size.

| Frame | Vietnamese | English |
| :-- | :-- | :-- |
| 1280×720 | `vi-1280x720-energy-spent.png` (Sức lực 258 / 270) | `en-1280x720-energy-spent.png` (Energy 258 / 270) |
| 1920×1080 | `vi-1920x1080-energy-spent.png` | `en-1920x1080-energy-spent.png` |
| 390×844 | `vi-390x844-energy-spent.png` | `en-390x844-energy-spent.png` |

## Grading against ui-standard.md section 7

- The energy bar and the hotbar keep their places at all three sizes; the phone frame has no overlap with the touch controls (checked on the 390×844 frame).
- The frames show the spent state, not the rollover. The rollover (G10) changes what the bar shows after the game day changes, which the QA hook cannot reach:
  it moves the server's clock, while the client reads its clock offset once at sign-in. The rule is pinned by `EnergyShownTests`.

## Not captured

- **The rollover itself.** Needs a QA hook that moves the client's calendar with the server's (ADR-079). Open, listed in ADR-114.
- **The axe tooltip** with the new text ("about a dozen swings"). Not framed; the text is in both `.resx` files and is tested by the resource build.
- **Before frames.** The spent-energy frame reads the same on master, because the change is in what the bar shows after midnight.
