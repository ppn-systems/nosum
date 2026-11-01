# QA: account settings, round 2 (mail delete, sound label, Profile tile)

Captured with `tests/browser/account-round2/capture.js` (Playwright, Chromium) against the Development server
(PostgreSQL). Each run is one viewport and one language on a fresh guest. The script seeds one unread mail for that
guest (code `something.new`, no gift) and captures the game menu, the open mail, the delete step and the Sound tab.

- `before/`: `origin/master` at `f59e94c` (#292), the client before this change.
- `after/`: this branch.
- Viewports: 1280x720, 1920x1080, 390x844 (touch). Languages: `vi` (default) and `en`.
- `report.json` per side: the line count of the Profile tile and of the sound-effects label, whether a delete asks first, and
  browser console errors (zero on every run).

## What changed on screen

| Frame | Before | After |
| :-- | :-- | :-- |
| `*-mail-open` | The open mail shows Delete. | Unchanged. |
| `*-mail-after-delete` (before) / `*-mail-confirm` (after) | Delete removes the mail at once; nothing to confirm. | A centre confirm box names the mail ("Xóa thư "Thông báo"?") with Delete and Keep. Nothing is sent until Delete. Keep leaves the mail in place (checked in every run). |
| `*-settings-sound` (vi) | "Hiệu ứng âm thanh" wraps to two lines (`Hiệu ứng âm` / `thanh`) at 1280x720 and 390x844. | "Hiệu ứng" on one line at every size. |
| `*-settings-sound` (en) | "Sound effects" on one line. | Unchanged. |
| `*-menu` (Profile tile) | "Hồ sơ" on one line at every size (measured). | Unchanged: it already fits. Checked, not changed. |

Measured sound-effects label lines: after (vi) 1 line at every size, in `after/report.json`. The before run looks for the new text, so it does not measure the old vi label; the old wrap at 1280x720 and 390x844 is in `before/*-vi-settings-sound.png`, and was measured at 2 lines at 390x844 in a first trial run whose report was not kept.

## Grading against `docs/design/ui-standard.md` section 7 (after)

| # | Check | Result |
| :-- | :-- | :-- |
| 1 | Each element in its region | Pass. The confirm box is a centre `ConfirmDialog` (section 5, Confirm), the same overlay as the pet give-back. |
| 2 | Nothing overlaps | Pass in the captures. The confirm box covers the mail page by design (modal). |
| 3 | Menu column | n/a (not touched). |
| 4 | Edges line up | n/a. |
| 5 | `GamePanel` with S/M/L/XL | Pass. The confirm uses the shared `GamePanel` and `ConfirmDialog`; no new size. |
| 6 | Three font sizes, no word-by-word wrap | Pass for the two labels checked (Profile tile and sound effects, measured at all sizes). The confirm sentence wraps as a sentence, not word by word. |
| 7 | No inner scrollbar at 1280x720 and up | Not re-measured for these frames; the mail and settings panels did not change size. |
| 8 | Whole, centred item icons | n/a. |
| 9 | `--u` spacing and gap tokens | Pass: no new spacing or sizes. |
| 10 | Vietnamese and English both fit | Pass: the confirm and the sound label fit in both languages at all three sizes. |

Buttons (section 8): the confirm uses `primary` for Delete (the question's yes) and `secondary` for Keep. Delete removes
the mail, so it is a `primary` answer here, not `danger`, because the question itself is the danger step.

## Not in this change (critique items left open)

- Hair-style names in the wardrobe (bare numbers 1–8); needs the art for each style to name it correctly.
- Guest sign-out: what a guest loses is not stated, because the server behaviour for a signed-out guest is not checked here.
- Guest to account linking that keeps progress is unchanged (ADR-069); this round does not change the upgrade flow.
- Remembered-session card and Google / Microsoft sign-in need client ids, which this container does not have.
