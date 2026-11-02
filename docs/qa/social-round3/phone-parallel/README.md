# Social flow on a phone (390×844, vi), three runs in parallel

`tests/browser/social-round3-play.cjs` run three times at once against one Development server (three browsers, six players):

    BASE=http://localhost:5080/ LABEL=runN OUT_DIR=docs/qa/social-round3/phone-parallel/runN LANGS=vi SIZE=390x844 node tests/browser/social-round3-play.cjs

Result: all three runs pass every step (friends online, chat, trade, visit farm, co-op deposit, co-op invite). Logs are in
`run1/capture-log-390x844.txt`, `run2/…`, `run3/…`.

## What made the harness fail under load

- Fixed sleeps. A teleport, an invite or a panel could arrive after the sleep, and the next click then found nothing.
  Every wait is now on DOM state: a control is visible, a picker lists the nickname, the accept button is up.
- An invite could be lost. The invite picker waits for the friend's name in reach; the invitation waits for Accept; each stage is
  retried, the friend is put back in reach between tries.
- The co-op board. A teleport that lands late leaves the player out of reach of the board. The action key is retried, and the
  player is put back on the board tile after two misses.
- On a phone the co-op shows one part at a time (see `docs/qa/coop-phone/README.md`). The harness taps the Members tab for the
  invite picker and the Stock tab for the deposit.
- Two harness bugs found while fixing it: the invite label was built from a RegExp's text (`/Mời/`), and the earlier run clicked
  with the pointer where a DOM click is what the game needs.

Isolation: each guest and each signed-in player is its own browser context (its own storage and session), so parallel runs share the
server only.

## Not done here

- A pre-change run of this harness on the same server was not repeated; the earlier vi 390x844 failure was recorded in
  `docs/qa/mines-coop-social/README.md`.
- The harness is not part of CI (`ci.yml` does not run browser flows).
