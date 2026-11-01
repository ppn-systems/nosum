# Combat round 5: strike cue frame sequences

Tester build (`-p:NosumDevTools=true`, Development server, page `?qa=1`). The farmer is placed one tile below an ordinary
monster (`TG_DX=0 TG_DY=1`) by `teleportToMonster` twice, then stands still; frames run from about 800 ms to 1650 ms after the
first landing. The second landing is the strike at the end of the wind-up (400 ms).

- `before-fix/`: the cue with ARGB tints, which decode to a near-transparent colour. Nothing visible.
- `after/`: the cue with RGBA tints (`0xRRGGBBAA`, renderer.js command -6). Four sets.

## Checked

| Set | Frame checked | Result |
| :-- | :-- | :-- |
| `after/tg-1280x720-vi` | `tg-08-at1353ms.png` | Amber "!" above the farmer's head; the monster is drawn under the farmer. Visible. |
| `after/tg-390x844-vi` | `tg-07-at1213ms.png`, `tg-08-at1263ms.png` | **Not visible** in these frames. The monster under the farmer carries a gold aura; the cue is not seen above it. Not explained. |
| `after/tg-1280x720-en`, `after/tg-390x844-en` | not checked | Frames captured; not reviewed. |

Console errors: none in any run (`errors: []`).

Not established: why the cue is absent at 390x844 in the frames checked, and whether those frames show the monster that is winding up.
