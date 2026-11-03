# Combat and fishing, round 7: the fishing banner takes the press

Critique N4 (and N5, N6) of `docs/design/critique-combat-fishing.md`. Tester build (`-p:NosumDevTools=true`), Development server on PostgreSQL,
page `?qa=1`, one fresh guest per run, played by `capture.cjs` in this folder (`reel` mode: wait for the bite, press, capture the frame after the
press, then read the result). Nothing in the game is changed by the capture; it only plays the game.

## Frames (after the fix)

| Frame | 1280x720 | 1920x1080 | 390x844 |
| :-- | :-- | :-- | :-- |
| Pressed at the bite (`reel-01-pressed.png`), vi | `after/reel-1280x720-vi/` | `after/reel-1920x1080-vi/` | `after/reel-390x844-vi/` |
| After the press (`reel-02-after-press.png`), en | `after/reel-1280x720-en/` | `after/reel-1920x1080-en/` | `after/reel-390x844-en/` |

Each folder has `result.json` and `run.log`. Every run registered the press: the banner moved off the bite line within 300 ms in all six. The
results were a catch at 1280 and 1920 and weeds at 390 vi (one run each); the frames show what each run reached.

## Before the fix

The first runs at 1280x720 vi (before the `pointer-events` fix) showed the banner still on the bite line 300 ms after a click on it, and the line
timed out: `Cá đã thoát mất!`. The press was taken by the banner, not the water.

## Not established

- The frozen gauge lasts only the round trip; on localhost that is a few milliseconds, so no frame shows it. It is a feel change for a slow link.
- The catch ring at the bobber is too faint in a still frame to judge; it is judged from the code.
- The combat fixes (the killing blow's number and the Fireball's aim) were not played in the browser: a kill needs a fight with a monster that dies on
  the farmer's hit, which these runs did not make. They are covered by `CombatHitNumberTests`.
