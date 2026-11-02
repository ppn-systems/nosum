# Combat and fishing, round 6: the knock-out notice and the bite wait

Critique items 6 and N3 of `docs/design/critique-combat-fishing.md`. Tester build (`-p:NosumDevTools=true`, Development server on PostgreSQL,
page `?qa=1`), one fresh guest per run, played by `capture.cjs` in this folder (`wait` and `knockout` modes; nothing in the game is changed).

## Frames

| Frame | 1280x720 | 1920x1080 | 390x844 |
| :-- | :-- | :-- | :-- |
| Wait line (`wait-01-waiting.png`), vi | `after/wait-1280x720-vi/` | `after/wait-1920x1080-vi/` | `after/wait-390x844-vi/` |
| Wait line, en | `after/wait-1280x720-en/` | `after/wait-1920x1080-en/` | `after/wait-390x844-en/` |
| Knock-out notice (`knockout-01-toast.png`), vi | `after/knockout-1280x720-vi/` | `after/knockout-1920x1080-vi/` | `after/knockout-390x844-vi/` |
| Knock-out notice, en | `after/knockout-1280x720-en/` | `after/knockout-1920x1080-en/` | `after/knockout-390x844-en/` |

Each folder has `result.json` and `run.log` from its run. The `wait` runs reached the bite in every size and language (`bite: true`), with no
console errors.

## Read from the frames

- Wait, 1280x720 vi: `Đang chờ cá cắn câu, thường chỉ mất vài giây...` in the banner; the line is out from the rod.
- Wait, 390x844 en: `Waiting for a bite. It usually takes a few seconds...` in the banner above the hotbar, clear of the joystick and buttons.
- Knock-out, 1280x720 vi: `Bạn đã kiệt sức và được đưa về Nhà nông trại, chỉ còn một phần tư máu.` on one line in the toast line; the HP tube
  shows a low-HP tag `25` (the revived HP).
- Knock-out, 390x844 en and vi: the notice wraps to two lines inside its panel, above the hotbar, clear of the HP tube.
- Each knock-out run showed the notice once (the `toastsLater` list has one entry, 1.2 s after the first reading). All six runs were knocked out
  standing still beside an ordinary mine monster, in 11 to 20 s.

## Checked against `docs/design/ui-standard.md`

| # | Check | Result |
| :-- | :-- | :-- |
| 1 | Element sits in its region | Yes: the wait line is the fishing banner; the notice is the toast line. No new element. |
| 2 | Nothing overlaps | Yes at all twelve frames. |
| 4 | Type and spacing | Same type and panels as before; only the text changed. |
| 5 | Touch and small screens | 390x844 frames above; the cast tap is on the shoreline, clear of the controls. |
| 6 | Language | Both languages from `.resx`: `Fishing_Waiting`, `Combat_KnockedOut`. |

## Not established

- The doubled notice (`×2`) was not reproduced before the fix. The removed path (the attack reply's `KnockedOut` flag) fires only when the
  farmer's own attack causes the knock-out; these runs struck nothing. The fix is the removal of that second notice, from the code.
- Touch skill targeting and the walking hint (items 7 and 8) were not played in this round.
