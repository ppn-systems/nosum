# QA: animals and pets (rebuild pass)

Captured with `tests/browser/animals-pets-capture.cjs` on a Development server (PostgreSQL) and a client built with
`-p:NosumDevTools=true`, opened at `/?qa=1` as a guest. Each run is one language and one viewport on a fresh guest; the script
grants coins and the coop blueprint, places it, waits two game days, buys a chicken, walks into the coop, and captures.

- `before/`: `master` at `384cb1d`, the client before this change.
- `after/`: this branch.
- `critique/`: the play-through frames that the critique cites ([`docs/design/critique-animals-pets.md`](../../design/critique-animals-pets.md)).
- Viewports: 1280x720 and 390x844 (touch emulation). Languages: `en` and `vi` (the game's default).

## What changed on screen

| Frame | Before | After |
| :-- | :-- | :-- |
| `coop-empty-bench` | An empty bench looks like any bench: nothing says the animals will not eat tonight. | An exclamation mark (emote sheet, engine-drawn) over the trough while the bench is empty. |
| `coop-fed` | One press put in four hay (30 → 26). | One press put in as much as the bench holds (30 → 6, 24 rations), and a hay sheaf shows over the trough. |
| `carpenter-bought` (en) | "Bought, and on its way to the barn." for a coop chicken. | "Bought, and on its way to its new home." |
| `carpenter-bought` (vi) | Unchanged: "Đã mua vật nuôi và đưa về chuồng." | Unchanged. |

The 390x844 `before/390x844-en/coop-fed.png` shows hay still at 30: the press did not reach the trough in that run, so that pair
shows no feed. The `after` run at the same size does feed (hay 30 → 6). The walk to the trough depends on timing at that size.

## Grading against `docs/design/ui-standard.md` section 7

Graded for the screens this change touches. Items that do not apply are marked n/a.

**Carpenter, Buildings tab, animal section (1280x720 and 390x844, en and vi)**

| # | Item | Result |
| :-- | :-- | :-- |
| 1 | Element sits in its region | Yes: a modal in the centre region. |
| 2 | Nothing overlaps | **No.** The purchase notice runs under the panel's bottom edge at 1280x720 (both languages). Not fixed here; follow-up. |
| 3 | Menu column | n/a |
| 4 | Edges line up | n/a |
| 5 | `GamePanel`, S/M/L width | Yes: shared frame and title bar. |
| 6 | Three font sizes, no word wrap, nothing clipped | **No.** The notice is clipped (same cause as item 2). |
| 7 | No inner scrollbar at 1280x720 | Yes, no scrollbar; but the content is cut instead (see item 6). |
| 8 | Item icons whole and centred | n/a |
| 9 | Spacing in `--u` multiples | Yes. |
| 10 | Vietnamese and English both fit | Yes for the text. Content issue: in vi, both the coop's rows read "Chuồng gà (1/4)", the duck's included. |

**Coop interior, the bench (world-anchored, 1280x720 and 390x844)**

| # | Item | Result |
| :-- | :-- | :-- |
| 1 | Region | Yes: world-anchored, drawn by the engine (ui-standard section 4), like the emote and bite marks. |
| 2 | Nothing overlaps | Yes: above the trough, clear of the hotbar and the touch controls. |
| 8 | Item icon whole and centred | Yes: the hay sheaf and the exclamation are centred on the four-tile bench. |
| 10 | Languages | n/a: no text. |

## Not covered

- 1920x1080 was not captured: the brief asked for 1280x720 and 390x844.
- The pet panel and the menu were not recaptured: nothing on them changed in this pass.
