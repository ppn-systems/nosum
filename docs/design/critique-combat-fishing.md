# Critique: combat and fishing, played in the browser

Status: audit of combat and fishing on `master` (d3b933b) and the open items of the first review, which was written on a rebuild branch
(`claude/rebuild-combat-fishing`, base `master` 384cb1d) that is not in the repository. Each item below is checked against the code on
`master` and, where a frame is cited, against that frame. The first review's frames are under `docs/qa/rebuild-combat-fishing/`.

Method and limits: the tester build (`-p:NosumDevTools=true`, `?qa=1`, Development server on PostgreSQL, new guest accounts) was played at
1280x720 and 390x844 with the keyboard and the mouse, and the quest banner was captured at 1920x1080 too (`docs/qa/quest-banner-coins/`). The
combat and fishing items that were already fixed were not replayed in this pass; their status is taken from the code and the QA rounds named.
Touch was not played. Fights and fish vary from run to run.

## What works

- **The strike reads as a hit.** A landed strike shows the monster's damage, a combo chip, a finisher label and the monster's name in red for an
  elite (`docs/qa/combat-round5/`).
- **Strikes telegraph.** An ordinary monster rises and flashes for the wind-up before it lands (ADR-097, `MonstersDecorator.cs`); a boss ground
  slam warns on the ground first (ADR-047).
- **The fishing bite cue is clear.** The `!` bubble, the banner prompt (`Nhấn Space hoặc bấm chuột để kéo cần!`) and the reel gauge appear
  together at the bite (`docs/qa/combat-round5/`).
- **The catch is readable.** The catch card names the fish, its size and its rarity, and the banner says the next step (`Cast again`).
- **A completed quest now says what it paid** (item 11 below).

## What was unfair or unclear, and where it stands

"Fixed" is on `master` now. "Open" is recorded for a follow-up.

1. **A monster hit showed the farmer's damage twice (fixed).** One damage number per hit, one dedupe path (ADR-055 amendment,
   `CombatHitNumberTests`).

2. **The HP warning was a colour and a tooltip (fixed).** At 25 % or less the current HP is a tag over the HP tube (ADR-045 amendment,
   `EnergyBar.razor`). The tag sits over its own tube, so it does not cover the heart button at 390 px.

3. **The mine elevator picker stayed open after a map change (fixed).** `ClientWiring` closes the picker on every map join, so a teleport,
   a warp or a knock-out closes it (ADR-026 amendment).

4. **A monster's strike had no warning (fixed on master).** The windup (ADR-097) rises, flashes and lands after the wind-up. The first review
   predates it.

5. **The skill keys did not show when they were ready (fixed on master).** The skill buttons carry a cooldown sweep from the server's cooldown
   span (`Hotbar.razor`, `skill-cooldown`), and the buttons are 42 px (combat round 4).

6. **The knock-out message was cut off by the combo chip (addressed).** The combo chip has its own strip above the hotbar, so it no longer sits
   under the toast line (combat round 4, `Hotbar.razor`). Not re-captured in this pass. **Still open:** the revival HP is only in the tooltip
   after a knock-out, and the attack reply comment says "revived at 50 % HP" while the toast says "low HP". The HP on screen after a revival is
   not confirmed.

7. **The walking hint covers the scene during a cast (open, onboarding).** `Đi lại bằng W A S D hoặc cần điều khiển bên trái.` stays on the
   scene while the line is out (`docs/qa/quest-banner-coins/after/quest-banner-1920x1080-vi.png`). ADR-108 moved first-run hints into the toast
   line; this one still sits on the scene. Belongs to onboarding.

8. **Skill targeting on touch is in place (not changed).** `X` targets the nearest monster within six tiles on the server path
   (`PlayerActions.Combat.cs`); `Z` needs no target. A press with no target shows a toast (ADR-056 D8). Touch was not played in this pass.

9. **The reel window was short, and only the bar showed it (fixed on master).** The gauge's track is the press window of this cast, every press
   inside it reels the fish in, and the marker moves toward the end (`FishingDecorator.DrawGauge`). The server's early and late tolerances
   (ADR-078 D4) are not drawn on purpose: they absorb network delay, and the player should not aim at them.

10. **The result said the catch twice (fixed).** The banner asks for the next cast after a catch; the card carries the result (combat round 5,
    `FishingViewModel.ApplyReel`).

11. **Coins went up on the catch, and nothing said why (fixed here).** The 50 coins are the reward of the tutorial quest `catch_fish`, paid by
    the server with the completion (ADR-036 D2). The fish is not sold. The completion banner now names the coins: `Quest complete: <title>. You
    received <n> coins.` (`docs/qa/quest-banner-coins/`, ADR-036 amendment).

## New findings

- **N1. The completion banner did not show for any step but the last (fixed here).** The tracker showed a finished quest only when every quest
  was finished, so the completion of each tutorial step was silent. It now shows the quest that just finished for five seconds, whatever comes
  next. A login does not replay old completions. Trace before the fix: the panel went from `Till the soil` to `Water the soil` 121 ms after the
  press, with no banner (`docs/qa/quest-banner-coins/README.md`). Tests: `QuestTrackerViewModelTests` (two new cases).
- **N2. Quest and farm reports fail silently (open).** `FarmService.ReportAsync` and `QuestService` catch every exception and log nothing, so a
  failed quest write would look like a missing step. The path worked in this pass (the completion and the coins were in the database). Open: a
  log line in those catches, and a test that makes the write fail.
- **N3. The wait says nothing about how long a bite takes (open, minor).** The banner says `Đang chờ cá cắn câu...` for the whole wait (2 to 6 s,
  `FishingRules.BiteMinMs`). A player who casts and waits may reel in too early or give up. Open: say the wait is normal, or show the wait's
  progress.

## Reward pacing

The tutorial pays a small reward per step (10 to 80 coins, ADR-036). A first catch pays 50 coins with the quest, and a cast costs ten energy
(ADR-078 amendment). The pacing of a whole session was not played in this pass; it is for the economy critique.

## What this change does

- **Fixed:** item 11 and N1 (the banner names the reward and shows for every finished quest). ADR-036 amendment, `QuestTrackerViewModel`,
  `Language.resx` and `Language.en.resx` (`Quest_BannerReward`), two new `QuestTrackerViewModelTests` cases, and the frames in
  `docs/qa/quest-banner-coins/`.
- **Checked and recorded as fixed on master:** items 1 to 5, 9 and 10, with the ADR or the round that fixed each.
- **Still open:** items 6 (revival HP), 7 (walking hint), 8 (touch not played), N2 (silent report failures) and N3 (wait message).
- **Wire:** no change, so `NosumProtocol.Version` stays at 34. No `DbBudget` row changes. No new ADR: the change is the tracker's display rule, and
  the rule it amends is ADR-036's.
