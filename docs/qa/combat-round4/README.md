# Combat round 4: browser QA and captures

Fixes for the four items that failed in the round 3 verdict table (PR #310, `docs/qa/combat-round3/README.md`). Branch `claude/combat-round4`
from `origin/master` `b0b5da3`. Captures: `before/` is that master build, `after/` is this branch. Script: `capture.cjs` (round 3 script with the
chip and skill-button measurements added). Each folder holds `result.json` (the numbers), `run.log` (the console) and PNG frames.

## What changed

1. **Combo chip out of the toast line.** The chip (and the level-up badge) now sit in their own 14 u strip directly above the hotbar, right-aligned
   over it (`Hotbar.razor`, `.hotbar-badges`). The toast line stands above the strip, so the two do not share a region. The strip is always
   reserved, so the toast line does not jump when a badge shows. The old rule that lifted the toast by 12 u (`GameHud.razor.css`) is removed.
2. **Touch skill buttons are 42 px.** Z, X and V are at least `max(14 u, 42 px)` square on a phone (`max-width: 640px`) and on any coarse pointer
   (`Hotbar.razor.css`). On a phone `--u` is 2 px, so 14 u alone would be 28 px; the 42 px floor is the touch minimum the item asks for.
3. **Wind-up resolver integration tests** (`tests/Nosum.IntegrationTests/Server/CombatWireTests.cs`, two tests):
   - `StrikeWindup_PlayerStaysAdjacent_TakesTheHitWhenTheWindupEnds`: a player beside an isolated ordinary monster takes the hit when the 400 ms
     wind-up ends.
   - `StrikeWindup_PlayerStepsOutOfReachBeforeItEnds_TakesNoHit`: the same player steps two tiles out after the telegraph arrives; the move is
     accepted (no correction), and no HP is lost through the wind-up and 300 ms past it.
   - Support: `SocialClient` records the `StrikeTelegraph` push (`Telegraphs`). The host starts the monster AI loop outside
     `Program.BuildApplication`, so `Server.StartAsync(database, monsterAi: true)` runs the same `CombatService.RunAiAsync` at
     `DefaultAiInterval` for these two tests only.
4. **Telegraph on screen** at 1280x720 and 390x844, both locales. See below for what the frames show.

No protocol, packet, ADR or database change. No ADR was written: the layout change and the tests carry no architecture decision (ADR-097 already
covers the telegraph).

## Results

### (1) Toast vs combo chip, `toast` mode (DOM boxes, CSS px; overlap in px)

| Size, locale | Before (`before/`, master) | After (`after/`) |
| :-- | :-- | :-- |
| 1280x720 vi | toast and chip overlap **82 x 4** | 0 (toast bottom 552, chip top 559) |
| 1280x720 en | no overlap | 0 |
| 390x844 vi | overlap **86 x 5** | 0 (toast bottom 688, strip top 692) |
| 390x844 en | overlap **25 x 5** | 0 (toast bottom 688, strip top 692) |

Frames: `after/toast-*/toast-with-chip-try1.png`, `before/toast-*/toast-with-chip-try1.png`.

### (2) Touch skill buttons, `touchskill` mode at 390x844 (DOM boxes, CSS px)

| Build, locale | Z / X / V button boxes |
| :-- | :-- |
| before vi and en | 16 x 28 each (the Fireball button measured 16 x 28, as in the verdict) |
| after vi | 42 x 42 each (`Xoay kiếm`, `Cầu lửa`, `Hồi phục`) |
| after en | 42 x 42 each (`Whirlwind`, `Fireball`, `Divine heal`) |

Frames: `after/touchskill-390x844-*/ts-*.png`. The X key and the button tap both start a cast (one cooldown overlay each, no error).

### (3) Wind-up resolver: integration tests

Run on the local PostgreSQL (`CombatWireTests`, the two `StrikeWindup_*` tests). See the test-run line in the PR description for the full suite.
The stays-adjacent test passed on every run. The step-out test passed on 16 of 17 runs after the AI-loop fix. One run failed with the player losing 5 HP (100 to 95)
in the step-out test. I did not reproduce it after that, and I did not establish its cause (a monster that was not in the isolation check, or the
timing of the accepted move against the strike; I did not instrument either). Treat the step-out test as **not fully stable yet**: it is the first
thing to look at if it fails in CI.

### (4) Telegraph on screen, `telegraph` mode (the frame sequence)

Frames are every ~60 to 120 ms from 800 to 1650 ms after the first landing (the control landing). Each run stands the farmer on the nearest
monster (`teleportToMonster`), which puts the farmer over the monster's tile. Frame sequence: `after/telegraph-<size>-<loc>/tg-NN-atMSms.png`.

| Frame set | Monster in frame | Telegraph visible (flash, rise) | Landings (ms after first, HP) |
| :-- | :-- | :-- | :-- |
| 1280x720 vi | yes, below the farmer's hip (green slime) | flash: the slime goes pale in `tg-05` (1204 ms) and `tg-07` (1352 ms); rise not measurable by eye | 0 (92 to 84), 1493 (84 to 76) |
| 1280x720 en | yes, below the farmer's feet | not clearly visible at this size | 0 (92 to 84), 1501 (84 to 76) |
| 390x844 vi | yes, at the farmer's hip | flash visible in a few frames; rise not measurable | 0 (84 to 81, another monster), 398 (81 to 73), 1497 (73 to 70) |
| 390x844 en | **only its top shows, above the farmer's head (mostly hidden by the sprite)** | not verifiable | 0 (100 to 92), 1499 (92 to 84) |

What this does and does not show: the wind-up runs (the second landing follows the telegraph cadence at about 1.5 s, and the first landing is
the control). The telegraph is **partly verified**: the monster is on screen during the wind-up at 1280 vi, 1280 en and 390 vi, and the white flash
is visible in some frames. It is **not** verified at 390 en, and the rise is not verified at any size, because the capture stands the farmer on top
of the monster. A clean check needs the farmer one tile beside the monster; `DevTools.TeleportToMonster` does not do that yet (follow-up).

## Setup and deviations (read first)

- Builds: `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true` for the after tree and for a worktree of `origin/master`
  (`before`). Server: `dotnet build server/Nosum.Host -c Debug` (the server is unchanged between the two builds; only the client differs).
- Test-copy override as in round 3: `Device:MinimumWidth` set to `320` in the published `wwwroot/appsettings.json` (test copy only), and the
  precompressed `.br/.gz` removed.
- Database: the `nosum` role did not accept the password in `server/README.md`. For this run the role was altered (local sandbox only) so the
  integration tests and the server could connect; `nosum_qa` was created as in round 3.
- The after set was captured after the final CSS (skill buttons at 42 px); the earlier runs were discarded.
- Captures use random fights, so the landings and the monsters differ between runs (see `landings` in each `result.json`).

## Frame grades (`docs/design/ui-standard.md` section 7)

| Frame | 2 nothing overlaps | 6 text fits, no clipping | Verdict |
| :-- | :-- | :-- | :-- |
| `after/toast-1280x720-vi/toast-with-chip-try1.png` | pass (0 px) | pass | pass |
| `after/toast-390x844-vi/toast-with-chip-try1.png` | pass (0 px) | pass | pass |
| `after/touchskill-390x844-vi/ts-00-before.png` | pass | pass | pass (42 px buttons, labels fit) |
| `after/telegraph-1280x720-vi/tg-05-at1204ms.png` | pass | pass | pass; monster visible, flash shown |
| `after/telegraph-390x844-en/tg-06-at1129ms.png` | pass | pass | **not verified**: the monster is hidden by the farmer's sprite |

## Console and page errors

`errors: []` in every `result.json` (after and before).

## Not captured, and why

- 1920x1080: not in this brief.
- The rise of the telegraph (the monster's 3 px lift) is not checked by eye at any size (see (4)).
- The damage number from the touch Fireball: not checked on the frame (the cooldown overlay is the only DOM signal).
