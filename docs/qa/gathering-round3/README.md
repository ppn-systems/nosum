# QA captures: gathering round 3

Browser check of the tree health bar (ADR-089 D2) and the test-suite work of this round. Build: `claude/gathering-round3` from `origin/master`
(`89d060f`), Release wasm with `NosumDevTools=true`, Development host on a local PostgreSQL, 1280×720, Chromium. Scripts were run from the scratch
folder and are not committed.

## Frames

| File | What it shows | Verdict |
| :-- | :-- | :-- |
| `tree-hpbar-hit1-en-1280x720.png` | A grown tree after one basic-axe hit (`-3` energy and `+1 XP` on the trunk). The bar is above the canopy, 11 of 12 px green. | Pass: the bar appears on the first hit and is green above half. |
| `tree-hpbar-hit5-en-1280x720.png` | The same tree after five presses of the use key. The bar is 8 of 12 px green, the rest dark. Four swings landed, so about 6 of 10 hits are left. | Pass: the fill is proportional. The swing count was read from the bar's pixels, not from the server. |

Both frames are in daylight (4 PM). The first attempt was at 4 AM and the scene's night tint made the green and the empty part hard to tell apart; the
bar itself is correct, so the night frames were replaced and are not kept.

## Not captured this round

- **Copper ore break.** The mine floor's nodes were not reached in this run, so the copper-ore frame from round 2 is still the only one.
- **Vietnamese frames.** Only English was captured for the tree bar.

## Test suite

- `ConnectionPoolTests.ConnectDisconnectCycles_DoNotGrowTheOpenConnections` counted every client backend of its database and was cleared by
  other test classes' `NpgsqlConnection.ClearAllPools()` calls while it ran. Each fixture now has its own application name and clears only its own pool
  (`NosumDatabase.WithPoolLimits` gives the pool key). The count is that application name's backends, not the connection that asks.
- `DbBudgetBaseline`: `PetPet / pet the cat` limit amended to reads 1, writes 2, transactions 1, SaveChanges 2 (ADR-092 D2: the first petting of a day
  may add a forage item in its own transaction). The probe measured exactly that cost.
