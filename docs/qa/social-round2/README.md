# social-round2: before and after captures, graded

Captures for the second social pass ([`docs/design/critique-social.md`](../../design/critique-social.md)). `before/` is the client
built from `origin/master` (8316fbb); `after/` is this branch. Both were taken with `tests/browser/social-round2-capture.cjs` at
1280×720, 1920×1080 and 390×844 (iPhone 14 profile), in vi and en. The script's own log is `capture-log.txt`; stdout is `capture-stdout.txt`.

Frames per size and language: `chat-open` (Enter on desktop, the Chat button on a phone), `trade-invite-receiver`, `trade-open-sender`.

## What changed

- Guest chat notice: the placeholder is now "Local chat only" (vi: "Chỉ kênh Gần"). The old text was cut in the input.
- Phone walk hint: hidden while the chat input is open, so the open chat box no longer covers it.
- Trade and co-op windows: size L (220 `--u`), so the 9-column bag and warehouse grid fit (they were cut at both edges at M).

## Grading (after)

Checklist from `ui-standard.md` section 7. ✔ pass, ✘ fail, — not applicable.

| Screen | 1 region | 2 overlap | 3 column | 6 text clipped | 8 icons whole | 9 spacing | 10 vi/en fit |
| :-- | :-- | :-- | :-- | :-- | :-- | :-- | :-- |
| Chat line, desktop, all sizes | ✔ | ✔ | — | ✔ "Local chat only" fits | — | ✔ | ✔ |
| Chat line, phone (`after/en-390x844-chat-open`) | ✔ | ✔ walk hint hidden while open | — | ✔ | — | ✔ | ✔ |
| Trade bag, 9 columns (`after/en-1280x720-trade-open-sender`) | ✔ | ✔ | ✔ | ✔ all nine columns whole | ✔ | ✔ | — |
| Trade bag, 1920×1080 and 390×844 | — | — | — | — | — | — | — |

Before/after for the trade bag: `before/en-1280x720-trade-open-sender.png` shows the first and last columns cut in half;
`after/` shows all nine whole.

## Not in this set

- The Dev QA hook for a second registered player, befriending, and Visit farm end to end (critique finding 8, "not browser-played"):
  not done in this pass. It needs a new tester opcode, a protocol bump, DbBudget rows and a Development-only account path.
- Trade confirm clarity and the critique backlog items (co-op entry, invite by nickname, phone door for Trade and Friends): not taken here.
