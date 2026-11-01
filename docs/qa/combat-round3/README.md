# Combat round 3: browser QA and captures

Captures for branch `claude/combat-round3` (commit `cac556c`, ADR-094), against `origin/master` `1a2119f` (before) and `b95fba5` (the toast before PR #298).
Script: `docs/qa/combat-round3/capture.cjs` (modes `telegraph`, `stepaway`, `toast`, `fishing`, `touchskill`). Build logs: `build-*.log`. Server logs: `server-after.log`, `server-before.log`.

## What changed

1. **Monster strike telegraph** (ADR-094 D1 to D4). An ordinary monster adjacent to a player winds up for 400 ms; it rises up to 3 px and flashes white every 80 ms. The strike lands only if the player is still in reach.
2. **Touch fishing prompt** (ADR-094 D6). On touch the bite prompt reads "Chạm để kéo cần!" / "Tap to reel in!"; keyboard and mouse keep the old text.
3. **Toast lift** (PR #298, in `1a2119f`). The toast line is lifted clear of the combo chip. Compared against `b95fba5`.
4. **Touch Fireball** (existing code, checked). X or the hotbar Fireball button casts at the nearest monster within 6 tiles.

## Setup and deviations (read first)

- Builds: `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true` for three trees (`after` = `cac556c`, `before` = `1a2119f`, `prepr` = `b95fba5`). Each server build used its own tree (`dotnet build server/Nosum.Host -c Debug`); the server changed between `1a2119f` and `cac556c` (strike telegraph), so the before captures ran the before server. `prepr` and `before` have the same server code (only a CSS change differs), so the prepr toast captures ran on the before server. All builds: 0 warnings, 0 errors (`build-*.log`).
- **Test-copy override**: in each published `wwwroot/appsettings.json` (the test copy only, not the repo) `Device:MinimumWidth` was set to `320`, and the precompressed `appsettings.json.br/.gz` were removed so the static server serves the edited file. Repo source is unchanged.
- **Database**: the shared `nosum` role did not accept the documented password, and `nosum_dev` does not exist. A separate role `nosum_qa` and database `nosum_qa` were created locally (the same approach as `docs/qa/clock-run/README.md`). Nothing else in Postgres was changed.
- Touch runs (390x844, `hasTouch` and `isMobile`) use the same keyboard keys for `9`, `Space`, `Z`, `X` as the desktop runs, plus taps for the bite and the Fireball button.
- Captures are from random fights. Monsters move and several can be adjacent at once; every run logs its landings. Run labels below are the folder names.

## Results

### (a) Telegraph frames

Frames: `after/telegraph-1280x720-vi/` (final run), `after/telegraph-1280x720-vi-run0-exemplar/` (smoke run, same script version, used as the exemplar), and the same folder names under `before/`.

- Stand-still landings (the control), ms after the first landing, damage in HP:
  - After, 1280 vi (final): 0 (92→84), 1538 (84→76).
  - After, 1280 vi exemplar: 0 (92→84), 1511 (84→76).
  - Before, 1280 en: 0, 1505. Before, 390 en: 0, 1501. Before, 390 vi: 0, 1504.
  - Some runs show a second monster landing 15 to 400 ms off phase (`landings` in each `result.json`); those are other monsters, not the telegraph.
- In the exemplar frames, the slime east of the farmer rises between `tg-03` (+1153 ms) and `tg-05` (+1311 ms), turns white at `tg-06` (+1385 ms), and the -8 number shows at `tg-08` (+1537 ms). The rise and flash were checked by eye on a crop of these frames.
- The baseline frames show the monsters at the same cadence but I could not identify a rise or flash by eye, since the monsters overlap in those frames. Treat the baseline as "lands on the tick with no warning" based on the landing timing, not on a frame.
- Limitation: in the final after run at 1280 vi, the frame at +1137 ms shows **no monster next to the farmer**, so that run's frames do not show the telegraph. 390x844 frames were not viewed; grade them after the frame check below.

### (b) Step-away (after build only)

Protocol (`capture.cjs stepaway`): new guest per attempt (full HP). The farmer stands still until the second strike (L2) lands as the control, then steps one tile out 50 ms into the windup of the third strike (L2 + 1100 to 1500), held 260 ms (walk speed 80 px/s, 16 px tile, `Walker.cs`). Attempts cycle through Up, Left, Down, Right, because the monster's side is not visible from the DOM (the world is on the canvas). Success means no drop in HP from L2 + 1000 to L2 + 2300.

| Run (folder) | Attempts used | Successful | Winning key | Control: landing 1 / landing 2 (HP) | HP before third strike → at end |
| :-- | :-: | :-: | :-- | :-- | :-- |
| `after/stepaway-1280x720-vi` | 3 | 1 | Down | 92→84, 84→76 (gap 1495 ms) | 76 → 76 |
| `after/stepaway-1280x720-en` | 6 | 1 | Left (attempt 6) | 92→84, 84→76 (gap 1504 ms) | 76 → 76 |
| `after/stepaway-390x844-vi` | 1 | 1 | Up | 92→84, 84→76 (gap 1494 ms) | 76 → 76 |
| `after/stepaway-390x844-en` | 8 | 0 | none | multi-monster; every attempt took a hit after the step | not established |

- The control is in every attempt: a stand-still strike landed at L1 and L2, 8 damage each. Result: **the hit lands when the farmer stands still.**
- The step-away: in each success, the next strike (+1500) did not land, and HP stayed at 76 through +2300 ms. Failed attempts took the third-strike hit at about +1500 ms. The failed directions match the geometry (for example, Up into the monster, Left diagonal and still in reach).
- Attempts used: **18 in total, 3 successes.** The 390 en run had several monsters adjacent with out-of-phase strikes, so it used all 8 attempts without a clean success. That run does not establish the rule at 390 en.
- Superseded: `after/stepaway-1280x720-vi-run0-noclean/` is an early run with a 150 ms hold (the farmer moved about three quarters of a tile, so the step never left reach) and no control landing. Not used for any conclusion.

### (c) Toast lift with the combo chip

Protocol: Space six times to build a combo, then Z, then Z again (the second is refused on cooldown, `Combat_Refused_OnCooldown`). The toast and chip boxes are read from the DOM.

| Size, locale | Pre-#298 `b95fba5` (`prepr/`) | Baseline `1a2119f` (`before/`) | After `cac556c` (`after/`) |
| :-- | :-- | :-- | :-- |
| 1280x720 vi | overlap 82 x 40 px | 77 x 4 px (different toast text) | 82 x 4 px |
| 1280x720 en | no overlap | no overlap | no overlap |
| 390x844 vi | overlap 86 x 29 px | 86 x 5 px | 86 x 5 px |
| 390x844 en | overlap 25 x 29 px | 25 x 5 px | 25 x 5 px |

- **Verdict: fail.** #298 cut the overlap from about 30 to 40 px down to 4 to 5 px but did not clear it. The toast's bottom edge sits over the chip's top edge at 1280 vi and at both 390 sizes. At 1280 en the boxes do not overlap horizontally.
- Frames: `after/toast-1280x720-vi/toast-with-chip-try1.png`, `prepr/toast-1280x720-vi/toast-with-chip-try1.png`, `before/toast-1280x720-vi/toast-with-chip-try1.png`, plus the 390 frames in the same folder names. I looked by eye at the same-layout frame of a smoke run (not kept in the repo): the chip's top-left corner tucks under the toast's bottom edge, while the text is clear.

### (d) Fishing bite prompt

| Build, input | Size, locale | Bite prompt text (read from the DOM) |
| :-- | :-- | :-- |
| after, keyboard | 1280 vi | "Cá đang cắn câu! Nhấn Space hoặc bấm chuột để kéo cần!" |
| after, keyboard | 1280 en | "Something is biting! Press Space or click to reel in!" |
| after, touch | 390 vi | "Cá đang cắn câu! Chạm để kéo cần!" |
| after, touch | 390 en | "Something is biting! Tap to reel in!" |
| before, keyboard | 1280 vi, en | same as after keyboard (old text) |
| before, touch | 390 vi, en | old keyboard text ("Nhấn Space…" / "Press Space…") |

- **Verdict: pass.** Touch shows the tap text; keyboard keeps the old text; the before build on touch shows the old text, so the change is the one under test.
- Frames: `after/fishing-*/fish-02-bite.png`, `before/fishing-*/fish-02-bite.png`. The reel press caught a fish in each run (catch text recorded in `result.json`).

### (e) Touch Fireball at 390x844

- **Button size: 16 x 28 CSS px** (`page.locator` via `getByRole('button', {name: /Cầu lửa|Fireball/})`, the same on before and after). That is well below the 14 `--u` minimum (42 px at `--u: 3px`, `docs/design/ui-standard.md` section 6). **Fail: the target is too small to tap reliably.**
- The X key with a monster in reach started a cast (cooldown overlay on the X key). The hotbar button tap also produced a cooldown overlay. In `wasm/Nosum.Wasm.Socket/Combat/CombatChannel.cs` `CastSkill`, when skill 2 has target 0 the client substitutes the nearest monster in 6 tiles, so the button path auto-targets too. I first suspected a bug (target 0 refused by the server) and that was wrong.
- Not verified: the damage number. Frames: `after/touchskill-390x844-vi/ts-*.png`. The two cooldown overlays are the only DOM signal; the frame check is not done.

## Frame grades (`docs/design/ui-standard.md` section 7)

Only frames I looked at are graded. Others are graded from the recorded numbers only.

| Frame | 2 nothing overlaps | 6 text fits, no clipping | Others | Verdict |
| :-- | :-- | :-- | :-- | :-- |
| `after/toast-1280x720-vi/toast-with-chip-try1.png` | **fail**: toast and chip overlap 82 x 4 px | pass | n/a | **fail** |
| `after/telegraph-1280x720-vi-run0-exemplar/tg-06-at1385ms.png` (white flash; `tg-03` to `tg-05` show the rise) | pass | pass | item 1: telegraph is in the world, pass | pass (exemplar, checked by eye on a crop) |
| `after/telegraph-1280x720-vi/tg-03-at1137ms.png` | n/a | n/a | no monster in frame | **does not show the telegraph** |
| `after/touchskill-390x844-vi/ts-00-before.png` | item 10 (vi): not graded | | | not graded |

Not graded by eye: 390x844 telegraph frames, 390x844 toast frames, fishing frames.

## Console and page errors

Every run logs `errors: []` (`run.log` in each folder). No console or page errors in any run.

## Not captured, and why

- 1920x1080: not in this brief (1280x720 and 390x844 only).
- Telegraph frames that show the monster in the final run at 1280 vi: the monster was not in frame in that run, so the telegraph in the final run is only shown by the exemplar.
- The damage number from the touch Fireball: not checked on the frame.
- Step-away at 390 en: no clean success in 8 attempts (see (b)).

## Commit state

Commit `2474c60` ("qa(combat): round 3 capture work in progress") appeared on `claude/combat-round3` during the run and holds an earlier copy of `capture.cjs`. The working tree has later uncommitted changes to that file (the step-away control protocol, fishing setup, hold timing). Nothing was committed or reverted by this QA run. The lead should decide what to keep.
