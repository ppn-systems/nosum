# Farming loop critique (rebuild-farming)

Review of the farming loop on `master` (035d354), played in a browser against a Development server and a tester client
(`-p:NosumDevTools=true`). The questions are the ones `docs/design/research-hud-and-loops.md` asks of a loop: what the player sees, how many
inputs a result costs, and whether a refusal says why. Captures: `docs/qa/rebuild-farming/README.md`. Decision: `docs/adr/ADR-088-*.md`.

## Scope

- Base: `master`. A local commit 384cb1d (38 commits ahead of master, on no remote branch: withering, sprinklers, protocol 28) was left out,
  as the brief asks for a branch from master. Everything below describes master.
- Play: `tests/browser/farm-loop-play.cjs`. The farmer stands on one tile: till, sow, water, water again the same day, water on each of four game
  days, harvest, then a wide swing with a copper hoe over the bare line. Each step is one key press; the number of presses is counted from the script.
- The weather is rolled per run. Refusals that depend on rain differ between runs, and this review says which ones.

## What feels bad or confusing (frames in `docs/qa/rebuild-farming/before/`)

1. **Watering wet soil is told it "does nothing here".** The soil was watered earlier that day, or it rained. The reason is the soil, and the
   text points at the item. The rain runs show it on days 1 to 3: `1280x720-vi/08-day1-watered.png` toast "Dùng vật phẩm này ở đây không có
   tác dụng."; `1280x720-en/05-water-day0.png` "That does nothing here." Fixed: `AlreadyWet` (ADR-088 D3), now "Đất đã đủ nước hôm nay." /
   "The soil is already wet today." (`after/1280x720-en/08-day1-watered.png`).
2. **Success is silent.** Till, sow and water work with no text. The only line above the hotbar is for refusals (`GameMessagesViewModel`
   returns text for non-Ok statuses only). The player learns a step worked from the tile changing and the quest card ticking
   (`02-till.png`, `04-sow.png`, `05-water-day0.png` have no toast).
3. **The action key harvests a ripe crop whatever is held.** `FacesCrop` works the crop, so E with the can in hand harvested the crop on day 4
   (`08-day4-watered.png` has no toast; `09-grown-full.png` shows the quest moved to "Sell produce" and a crop in slot 9). The player pressed
   "water" and got a harvest with no message. By design (the action key works the crop), but it is surprising and silent.
4. **Growth stages cannot be read in the field.** The four stages are sprites with no label. In the zoomed crops (`07-day*`, `08-day*`) a
   sown tile and a bare tilled tile look close at native size. A player cannot read "day 2 of 4" from the farm.
5. **A seed in hand on an unripe crop is told "This spot already has a crop."** The player wanted to harvest, not plant. The server answers
   `HasCrop` by design: `FarmWireTests` pins it ("a young crop cannot be harvested; a second seed cannot be planted on it"). The wording is the
   problem, and it needs the days left, which the client does not compute. Not reproduced in the scripted run; backlog.
6. **A wide swing gives one answer for the whole line.** Before: one status, no count, no energy label (`before/1280x720-vi/11-wide-hoe.png`).
   Fixed for the count and the energy (ADR-088 D1, D2). The reticle still previews one tile.
7. **The top-right HUD is two columns of unlabelled icons (about 14).** Frames `01-facing-down.png` and `after/1280x720-en/09-grown-full.png`.
   The UI standard asks for one column and at most eight buttons (`docs/design/ui-standard.md` section 7, item 3). Not a farming rule, but a new player
   meets it before the first till.
8. **Phones and narrow windows cannot play.** `before/390x844-*/device-block.png`: "Window too narrow. Please widen your browser window to at least
   1024 px." The device policy blocks touch devices and anything below 1024 px (`DevicePolicy.DefaultMinimumWidth`). The UI standard section 6 describes
   a 390×844 layout (one "Menu" button, full-width panels) that the player never reaches. Contradiction for the owner to settle; not changed here.
9. **English is not selected by the browser language.** The UI language is a client setting (`lang`, default Vietnamese). A visitor on an English
   browser sees Vietnamese until they change it. The first QA pass assumed the locale and captured Vietnamese; it was re-run with the setting.

## Rules that disagree between client and server

- **Energy for a wide use.** Before: the client predicted no energy for any line longer than one (`_queuedCost = tiles == 1 ? cost : 0`), while
  the server charged every tile that worked. Fixed: the label is the cost of one tile times the worked count (ADR-088 D2).
- **Wet soil is decided in three places.** `FarmService` refuses a can on wet soil (`IsWet`). `PlayerActions.TargetWorks` repeats the same check
  for the reticle (`PlayerActions.cs`, the `ToolKind.WateringCan` arm). `FarmRules.Water` returns `Ok` on wet soil and never refuses, so the pure
  rule module the tests read does not encode the refusal. Fixed for the reply (ADR-088 D3). Open: move the check into `FarmRules` and let the
  client call it.
- **Area use is a hoe or a can only.** `ToolRules.ValidateArea` admits those two; the client's `IsFarmTool` admits seeds, hoe and can. The scythe
  works one tile, so harvesting along a line is not possible.
- **Phones.** The UI standard describes a 390×844 layout; `DevicePolicy` blocks it.

## What is tedious (presses per action)

From the script, one planted tile, standing still after the first step:

| Action | Presses | Notes |
|:--|:--|:--|
| Step onto the row | 1 move | Walking is the only way to change the tile in front. |
| Till | 1 digit (hoe) + 1 use | The hotbar digit is needed each time the tool changes. |
| Sow | 1 digit (seeds) + 1 use | |
| Water | 1 digit (can) + 1 use | |
| Water again the same day | 1 use | Refused; no change. |
| Each game day | 1 use (plus the day advance, tester only) | In play the player must come back to the tile each day. |
| Harvest | 1 digit (scythe) + 1 use | Or E with any tool on a ripe crop (see 3). |

A 3×3 plot is about 27 presses before walking. Drag-to-sow already exists for seeds, hoe and can (ADR-071 D3). The wide tools cut the till and
water part to one swing per line, but the harvest still needs one press per tile.

## The QA hook

- Round two (backlog item 8): `window.nosumQa` (`wasm/Nosum.Wasm/Game/QaEntry.cs`) exposes `grant`, `advanceClock`, `teleport`, `season`, `levelUp`,
  `fishingBite`, `allocatedBytes`, `springReset` and `teleportToMonster`. Every member the browser scripts call exists; the earlier note that
  `teleport` was missing was out of date. `tests/browser/farm-feel-capture.cjs` runs on master (12 frames). `season-fix-capture.cjs` was fixed to click
  only a visible button at 390 px. The non-farming scripts were not run. No script was retired.
- Round two captures: `docs/qa/farming-round2/before` (master) and `docs/qa/farming-round2/after` (this branch), vi and en at 1280x720, from
  `tests/browser/farm-round2-capture.cjs`. In the after frames the unripe line reads "Not ready, 1 day left" in en; the vi unripe frame has no line, and
  no frame shows the harvest-with-item-in-hand line (the toast had likely faded). Re-check those two before trusting them.

## Changes in this pass (ADR-088, protocol 29)

| Finding | Change | Where |
|:--|:--|:--|
| 1 wet soil says "nothing" | `AlreadyWet` status and verdict, text in vi and en | `FarmService.cs`, `FarmVerdict`, `FarmStatus`, `Language*.resx` |
| 6 wide use gives no count | `UseItemResult.Worked` (byte, appended), counted in `FarmService.Area.cs` | contracts, handlers |
| 6 energy for a line | cost of one tile times the worked count | `PlayerActions.Swing.cs`, `FarmChannel.cs` |

Clients built for 21 misread the reply and the status, so they ship with the server.

## Backlog, ranked by how much play it would change

Round two (branch `claude/farming-round2`, ADR-094, protocol 31):

1. **Done.** Harvest along a line with the scythe: `ToolRules.ScytheAreaLength` (three at basic) and a trailing harvest for the scythe only (ADR-094 D1).
2. **Done.** An unripe crop names the days left: `UseItemResult.DaysLeft`, the words in `Farm_Status_NotMatureDays` / `…OneDay` (ADR-094 D2).
3. **Done.** The action key's harvest with another item in hand says so on the message line (`Farm_ActionKeyHarvested`).
4. **Done.** Hovering a crop names its growth stage on the message line (`Farm_Hover_Stage`, `FarmRules.StageAt`). Sprinklers beside the tile are not counted in the hover, so a sprinkler-watered crop can show one stage behind.
5. **Done.** The reticle draws the whole line of a wide hoe, can or scythe, engine-drawn (`FarmDecorator`).
6. **Done.** The watering check is `FarmRules.WaterVerdict`, called by the server and the reticle (ADR-094 D3).
7. **Open, an owner decision.** Phones: the device policy or ui-standard section 6 (see the finding above).
8. **Browser scripts**: see the note under "The QA hook".

## Round three addendum (ADR-100)

Replayed on `claude/farming-round3` (from master 7800a2e), 1280x720 and 390x844 (a width-override test copy, the product still gates phones at 1024 px),
vi and en. Captures and grades: `docs/qa/farming-round3/` (README there). Script: `tests/browser/farm-round3-capture.cjs`.

**Played:** the copper hoe's wide swing over the row, parsnip seeds on two tiles, four days of watering with the wet-soil refusal, growth to stage 5 of 5,
E with the scythe on an unripe tile, E with seeds on a ripe tile, and a tap with the can on a ripe tile. **Not played this round:** the sprinkler and the
shipping bin. The quest card reached "Sell your produce", but no crop was carried to the bin, so the sale and the coin pop were not seen. Those two
steps are not graded here.

**Round-two capture gaps, closed:** the vi unripe line ("Chưa chín, còn 2 ngày") and the harvest-with-item line now appear in the frames. The cause was the
script, not the game: a fixed wait of 600 to 700 ms missed a message that is shown for 3 s only when the line had not yet appeared. The script now polls the
line and captures the first frame that shows it (about 300 ms after the press).

### What still feels slow, confusing or silent (after this round)

1. **A scythe tap on an unripe crop is silent.** The E key says "Not ready, N days left"; a tap with the scythe on the same tile shows no line in any run
   (vi and en, 1280x720, the poll ran 2.5 s). Not fixed: the cause is not yet found. Candidates to check: the scythe's three-tile line on a tap (the
   area path in `FarmService.Area.cs` keeps the first tile's refusal, so the reply should carry it), and whether the client sends the tap's use at all
   (`PlayerActions.TargetWorks` marks the reticle red and nothing else reads it). Next step: log the reply of one scythe tap on an unripe crop.
2. **The message line is crowded.** At one moment it can hold the onboarding hint, the hover stage ("Growth stage 1 of 5") and two success toasts
   (`en-03-sown-row-1280x720.png` in `docs/qa/farming-round3/after/`). The toast stack is capped at two; the hover line is a separate spot above it,
   and the hint shares the toast line without counting toward that cap. Owner decision: move the hover stage to the reticle, or drop the hint once the
   first step is done.
3. **The onboarding hint sits over the work area.** At 1280x720 the hint card covers the row being worked (`docs/qa/farming-round3/before/vi-07-harvest-with-item-in-hand-1280x720.png`,
   y 460 to 525). Not changed: the hint is ADR-093 D5's design; moving it is a layout decision for the owner.
4. **The growth stage is text only, on hover.** Still open from round one (finding 4): the sprite gives no stage, and the stage appears only while the farmer
   faces the tile.
5. **Planting is one press per tile.** Seeds are placed tile by tile: a digit once, then a press and a walk per tile. The round-two wide tools cover till and
   water, and drag-to-sow covers seeds (ADR-071 D3), but this replay did not use drag. Open: check the drag path on a line of three.
6. **A tap with the can harvests a ripe crop.** Now named (below), but the mapping is surprising: a watering tap takes the crop. Owner decision: should a
   can or a hoe harvest a ripe crop by a tap, or only the action key and the scythe?
7. **Phones: the frame loop stopped.** A fractional pointer position reached an `int` parameter of the frame export and stopped the loop (seen in one
   phone run). Fixed (ADR-100 D1). The device gate for phones is still open (round one, finding 8).
8. **Phone layout (observed, not measured).** The quest card covers about a fifth of the height at the top left; the hotbar shows nine slots with no
   Z X V keys; the slot counts are small. Owner decision with finding 7.

### Changed in this round (ADR-100)

| Finding | Change | Where |
|:--|:--|:--|
| 7 phone loop stops | The pointer is rounded to a whole pixel before the frame export (`PointerPixel.Of`); the frame takes `double` | `GameLoop.Frame`, `wasm/Nosum.Wasm.ViewModels/Input/PointerPixel.cs` |
| 6 tap harvest is silent | A single-tile harvest with another item in hand is named, from E or a tap; text no longer names the key | `FarmHarvestNotice`, `Farm_HarvestedWithOtherItem` |
| round one 2 success is silent | A successful till, sow, water or harvest gives one short line | `FarmDoneNotice`, `Farm_Done_*` |

No wire change: protocol 32 stays. The tests: `PointerPixelTests`, `FarmHarvestNoticeTests`, `FarmDoneNoticeTests` (the key sets are checked in both languages).

### Backlog after round three, ranked by how much play it would change

1. Finding 1: the scythe tap on an unripe crop (the silent refusal), once its cause is found.
2. Finding 2 and 3: the message line and the hint over the work area (layout, owner).
3. Finding 5: check that drag-to-sow covers a line of three, and make it the taught way to plant.
4. Finding 4: the growth stage readable without hover (a sprite or a label on the tile).
