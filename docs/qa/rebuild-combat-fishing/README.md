# Combat and fishing rebuild: before and after captures

Captures for `docs/design/critique-combat-fishing.md` and the change on `claude/rebuild-combat-fishing`.

- `before/`: the client as it was on `master` (384cb1d). `after/`: the client with this change. The same server (Development host, PostgreSQL
  `nosum_dev`) served both; the server did not change.
- Viewports: 1280x720 and 390x844 (touch). Locales: `vi` (default) and `en`. Each folder is `<scenario>-<width>x<height>-<locale>/`.
- Scenarios, one script each (in `tests/browser/`):
  - `combat-*`: `rebuild-combat-capture.cjs`. Teleport to the first mine floor, stand beside a monster, strike with Space, cast `X` and `Z`.
  - `death-*`: `rebuild-combat-death-capture.cjs`. Strike until the farmer is knocked out, then the beach.
  - `fish-*`: `rebuild-fishing-capture.cjs`. Teleport to the beach, rod in slot 9, cast at the sea, press at the bite. The sea click is at y=512
    at 1280 and y=560 at 390.
- `after/lowhp-*`: `rebuild-lowhp-check.cjs`. Fights until the low-HP note shows, then records its box and the HP meter. Measured on the final build:
  - 1280x720 (vi): HP 20/100. Tag box x 924, y 590, 38x21 px; HP tube x 928, y 602, 30x96 px. Tag centred on the tube.
  - 390x844 (vi): HP 23/100. Tag box x 310, y 742, 22x14 px; HP tube x 311, y 750, 20x64 px. Tag centred on the tube.
  - `after/after-lowhp-390.log` holds the 390 run's output. The first 1280 attempt in `after/after-lowhp-1280.log` did not reach low HP (`NOTE null`);
    the 1280 numbers above come from a later run in the same build.
- Every run logs `errors: []` (console and page errors). Random fight and fish outcomes differ between runs, so a frame that shows a state
  (a low-HP tag, a catch) is from the run that produced it, not a fixed script step.

## What changed on screen

Only one visible change: the low-HP tag over the HP tube (`EnergyBar.razor`, `.vitals-hp-note`). Compare `before/combat-390x844-vi/07-later.png`
with `after/death-390x844-vi/fight-09.png` (the tag shows `24`), and `after/lowhp-1280x720-vi/lowhp-note.png` (the tag shows `20`).
The elevator and the damage-number changes have no visible UI of their own; their evidence is in the frames named in the critique.

## Grading against `docs/design/ui-standard.md` section 7

Graded on the low-HP tag, the only visible change. Frames: `after/lowhp-1280x720-vi/lowhp-note.png`, `after/death-390x844-vi/fight-09.png`.

| # | Check | Result |
| :-- | :-- | :-- |
| 1 | Element sits in its region | Yes. It is part of the vitals column in the bottom HUD row beside the hotbar; no floating card. |
| 2 | Nothing overlaps | Yes at both sizes. At 1280 the tag (y 590 to 611) sits above the tube and below the combo chip (ends at y 590), measured as above. At 390 it sits over its own tube, clear of the heart button. A first version (a full label above the column) overlapped the heart at 390 and was replaced. |
| 3 | Menu column | n/a |
| 4 | Edges line up | n/a (the tag is centred on its tube) |
| 5 | GamePanel and widths | n/a (no panel) |
| 6 | Three font sizes, no clipping | Yes. It uses `--nosum-fs-small`, one number, no wrap. |
| 7 | No inner scrollbar | n/a |
| 8 | Icons whole | n/a |
| 9 | Spacing in `--u` and gap tokens | Yes. Padding, border and offset are `--u` multiples; the colours are theme tokens (`--nosum-ink`, `--nosum-parchment-light`). |
| 10 | vi and en both fit | Yes. The tag is the number only, so it is the same text in both locales. The tooltip keeps the localised `HP 20 / 100`. |

Not captured: 1920x1080. The brief asked for 1280x720 and 390x844, so the 1920 frame in the checklist is not in this folder.
