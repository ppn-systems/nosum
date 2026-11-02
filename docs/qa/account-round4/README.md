# QA: account and settings, round 4 (wardrobe quick choices, settings reload)

Captured with `tests/browser/account-round4/capture.js` (Playwright, Chromium) against the Development server (PostgreSQL, `nosum_dev`)
and the client run with `dotnet run` in Development. Each run is one fresh guest at 1280x720, 1920x1080 and 390x844 (touch), in
Vietnamese (default) and English. Decision record: [ADR-109](../../adr/ADR-109-wardrobe-appearance-requests-in-order.md).

- `before/`: the client built from `master` at `9bc6f97`, before this change.
- `after/`: this branch.
- `report.json` in each folder: what the run found on the page, and the page-error count (zero on every run).

## What the run checks

| Step | What it does | Before (master) | After |
|:--|:--|:--|:--|
| `quick-choices-both-kept` | Clicks the second skin swatch and the third hair style in the same tick, waits 2.5 s, reads which swatch and style are selected. | `no` in all six: the skin reverts to the first swatch, the hair style sticks. | `yes` in all six: the second skin and the third style are both kept. |
| `wardrobe-preview-box` | Measures the character preview. | 48x96 at 1280x720, 80x160 at 1920x1080, 32x64 at 390x844. | Unchanged (the preview is not part of the change). |
| `names-off-before-reload` / `names-off-after-reload` | Turns off "Show names" on Display, reloads, signs in again, opens Display. | Stored value `names=0`; the switch is off after the reload in all six. | Same: `yes` in all six. |
| `page-errors` | Counts console and page errors during the run. | 0 | 0 |

## Frames

| Frame | What it shows |
|:--|:--|
| `*-wardrobe` | The Look tab on arrival (skin 1, hair style 1). |
| `*-wardrobe-after-quick` | After the quick choices: skin 2 and hair style 3 are selected, and the caption reads the new style. |
| `*-settings-display-after-reload` | The Display tab after a reload, with "Show names" off. |

The visible change is small by design: the chosen swatch and style now show at once, and the server's answer keeps them. The layout is
the one on master, so the frames look the same apart from the selected marks.

## Grading against `docs/design/ui-standard.md` section 7 (after)

Grades: **P** pass, **N/A** not applicable, **NM** not measured in this round.

| # | Check | 1280x720 | 1920x1080 | 390x844 | Note |
|:--|:--|:--|:--|:--|:--|
| 1 | Each element in its region | P | P | P | Centred `GamePanel`; no new element. |
| 2 | Nothing overlaps | P | P | P | Checked by eye on the frames (vi, 1280x720 and 390x844). The English frames were not graded by eye. |
| 3 | Menu column | N/A | N/A | N/A | Not touched. |
| 4 | Edges line up | N/A | N/A | N/A | Not touched. |
| 5 | `GamePanel` size | P | P | P | The wardrobe panel's size is unchanged. |
| 6 | Three font sizes, no word-by-word wrap | P | P | P | No new text. |
| 7 | No inner scrollbar | NM | NM | NM | Not measured in this round; the layout is unchanged from master. |
| 8 | Whole, centred item icons | N/A | N/A | N/A | No new icon. |
| 9 | `--u` spacing and gap tokens | P | P | P | No new spacing values. |
| 10 | Vietnamese and English both fit | P (vi) | P (vi) | P (vi) | English captured; not graded by eye. |

Buttons (section 8): no button is added or restyled.
