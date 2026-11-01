# Critique: combat and fishing, played in the browser

Status: review of the rebuild on `claude/rebuild-combat-fishing` (base: `master` 384cb1d). Written from play in the tester build
(`-p:NosumDevTools=true`, `?qa=1`, a Development server, new guest accounts). Frames: `docs/qa/rebuild-combat-fishing/before/` (the client as on
`master`) and `docs/qa/rebuild-combat-fishing/after/` (this change). The folder README grades the visible change against `docs/design/ui-standard.md`.

Method and limits: the mine (`mine1`), the beach and the farmhouse were reached with the QA hook (`teleport`, `teleportToMonster`, `grant`) and the
keyboard (Space, `X`, `Z`, digit keys). Fights and fish vary from run to run, so each frame below is from the run that produced it. The fishing pass is
a scripted cast, not a long session. Touch was not played; touch findings come from the layout and the code, and are marked as such.

## What works

- **The strike reads as a hit.** A landed strike shows the monster's damage, a combo chip (`Chuỗi đòn 5x (+20%)`), a finisher label (`KẾT LIỄU!`) and the
  monster's name in red when it is an elite (`before/combat-1280x720-vi/03-strike-0.png`, `after/combat-1280x720-vi/07-later.png`).
- **Boss slams telegraph.** A boss ground slam warns on the ground for a second before it lands (ADR-047).
- **The fishing bite cue is clear.** At the bite the farmer shows a `!` bubble, the banner says `Cá đang cắn câu!` with the prompt
  `Nhấn Space hoặc bấm chuột để kéo cần!`, and a window bar appears over the farmer (`before/fish-1280x720-vi/fish-02-bite.png`). The bite came about
  4 s after the cast, inside the server's 2 to 6 s delay.
- **The catch is readable.** The catch card names the fish, its size and its rarity (`Cá vược, Dài 27 cm, Thường gặp`), and the banner repeats it
  (`before/fish-1280x720-vi/fish-04-result.png`). A cast with no fish says so (`The line came up with nothing but weeds.`, `before/fish-1280x720-en.log`).

## What feels unfair or unclear

"Fixed" means this change addresses it. "Open" means it is recorded for a follow-up.

1. **A monster hit shows the farmer's damage twice (fixed).** In `before/combat-1280x720-vi/03-strike-0.png` three `-8` numbers are stacked over the
   farmer's head, and a fourth `-2`: one hit was shown two or three times. The cause: the attack reply and the vitals push both spawn a red number, and
   which packet came first decided whether a duplicate appeared. The numbers are how a player learns what hurt them, so a stack reads as several hits.
   In `after/combat-1280x720-vi/03-strike-0.png` the same strike shows one `-8`. Fixed by one dedupe path (ADR-055 amendment, `CombatHitNumberTests`).

2. **The HP warning is a colour and a tooltip (fixed).** The HP tube is the red gauge beside the hotbar. Its colour changes at 25 %, and its number is
   only in the hover tooltip (`data-tip`), which a touch player cannot reach. In `before/combat-1280x720-vi/03-strike-0.png` the tube is nearly empty at
   the bottom right of the hotbar, and no number is on screen. Fixed: at 25 % or less the current HP is a small tag over the top of the tube.
   Checked: `after/lowhp-1280x720-vi/lowhp-note.png` (`20`) and `after/lowhp-390x844-vi/lowhp-note.png` (`23`). A first version put the full label above
   the column; at 390 px it ran off the right edge and covered the heart button, so the tag now sits over its own tube (ADR-045 amendment).

3. **The mine elevator picker stays open after a map change (fixed).** In the exploratory play the picker (`Thang máy hầm mỏ`, with `Về nhà` and
   `Cửa hầm`) stayed on top of the beach after a teleport, and after a knock-out it stayed open in the farmhouse. I did not keep that frame, so the
   before evidence is the code: the picker closed only when the player rode the lift (`ElevatorViewModel.OnUpdated`). The after frame is
   `after/death-1280x720-vi/fish-00-beach.png`: the beach with no picker. Fixed (ADR-026 amendment).

4. **A monster's strike has no warning (open; the largest gap).** Ordinary monsters attack with no telegraph. In `before/combat-1280x720-vi/03-strike-0.png`
   the monster is beside the farmer and the `-12` lands on the same frame; nothing shapes, flashes or sounds before it. Only boss slams telegraph
   (`MonstersDecorator.Telegraphs.cs`). A fight against ordinary monsters is therefore a race of hit points, and the player cannot learn the timing of a
   strike. The fix is a server windup: the monster announces a strike and lands it after a delay. That changes the rule and the timing of every monster
   fight, so it needs an ADR and a playtest, and it is not in this change.

5. **The skill keys do not show when they are ready (open).** The `Z X V` keys are dim in the combat frames I looked at, with no cooldown sweep and no timer
   (`before/combat-1280x720-vi/03-strike-0.png`). A dim key could mean cooldown, no MP, or no target; the refusal toast (ADR-056 D8) shows only after a press
   is refused. A cooldown readout on the key is the next change in this area.

6. **The knock-out message is cut off by the combo chip (open; the message the player needs most).** In `before/combat-1280x720-vi/03-strike-2.png` the
   farmer is knocked out and the screen goes dark. The message `Bạn đã kiệt sức và được đưa về Nhà nông trại với l…` is cut off under the combo chip
   `Chuỗi đòn 2x (+5%)`. The text is the only explanation of why the farmer is at the farmhouse, and it overlaps the chip, which breaks checklist item 2
   of the UI standard. Open: move the combo chip, or give the knock-out message its own line. It was not changed here because the toast line is shared.
   Also open: the revival HP is only in the tooltip after a knock-out, and the attack reply comment says "revived at 50 % HP" while the toast says "low HP". I did not confirm the HP on screen after the revival.

7. **The walking hint covers the sea during a cast (open, onboarding).** `Đi lại bằng W A S D hoặc cần điều khiển bên trái.` stays on screen during the
   fishing pass (`before/fish-1280x720-vi/fish-02-bite.png`). It belongs to the onboarding, which is outside this change.

8. **Skill targeting on touch is already in place (not changed).** `X` (Fireball) targets the nearest monster within six tiles on the server path
   (`PlayerActions.Combat.cs`, `TryFindTarget(x, y, 6, ...)`), so a touch player does not need to aim it. `Z` is an area around the farmer and needs no
   target. A press with no monster in reach shows a toast (ADR-056 D8). I did not play it on touch, so the button size and hit area are unchecked.

9. **The reel window is short, and only the bar shows it (open, balance).** The press window is 1.0 s at fishing level 0 (ADR-078 D4). The bar in
   `fish-02-bite.png` runs down with no marker for where the press counts. The early tolerance (300 ms before the bite) and the late tolerance (400 ms
   after the window) are not shown anywhere. Open: mark the window's start and end on the bar, or lengthen the level-0 window.

10. **The result says the same thing twice (open, minor).** The catch card and the banner line both say the catch
    (`before/fish-1280x720-vi/fish-04-result.png`). It is clear but redundant; the card should carry the result and the banner should show the next prompt.

11. **Coins went up on the catch, and I did not check why.** The counter read 500 before the catch and 550 after it (`fish-01-beach-rod.png` against
    `fish-04-result.png`). The fish's bin price is the economy's business (ADR-081), so this may be a sale or a reward I do not know of.
    Open: confirm which it is and say it on the card.

## Reward pacing

One cast gave one fish, 14 to 27 cm in the runs I played (common species), and the counter rose by 50 coins on the catch. A cast costs ten energy (ADR-078 amendment), and the rewards
come in that band. I did not play long enough to judge a session's pace. What I can say: a cast with no bite is the likeliest moment to lose a
player, and the banner says only `Đang chờ cá cắn câu...` during the wait. A line that the wait is normal (for example, the bite delay range) would help.

## What this change does

- **Fixed:** item 1 (ADR-055 amendment, `CombatHitNumberTests`), item 2 (ADR-045 amendment), item 3 (ADR-026 amendment).
- **Not changed:** items 4 to 11. Item 4 is the largest and needs an ADR and a playtest. Item 6 is the most urgent of the rest, because its message is cut off.
- **Wire:** no change, so `NosumProtocol.Version` stays where it is. No `DbBudget` rows change.
