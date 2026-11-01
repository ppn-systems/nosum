# Critique: NPC dialogue, quests, journal and HUD on a normal day

Status: review, 2026-11-01. Branch `claude/rebuild-npc-hud`. Companion to [`ui-standard.md`](ui-standard.md),
[`research-hud-and-loops.md`](research-hud-and-loops.md) and [ADR-080](../adr/ADR-080-npc-dialogue-lines-and-seasonal-stock.md).
Frames: [`docs/qa/rebuild-npc-hud/before/`](../qa/rebuild-npc-hud/before/) (master) and [`after/`](../qa/rebuild-npc-hud/after/) (this branch).

## Method and limits

- A guest farmer, a new account, one in-game morning (Spring day 1, about 11:00). Played through
  [`tests/browser/rebuild-npc-hud-capture.cjs`](../../tests/browser/rebuild-npc-hud-capture.cjs): the farm HUD, the journal
  (All, Found, Missing), and the seed keeper's greeting and two Talk lines. Vietnamese and English, at 1280×720, 1920×1080 and
  390×844 (an iPhone 14 profile, so the touch controls show).
- Not played: a second day, the mine, combat, a finished quest chain, a second merchant's full talk cycle, and sustained play.
  Findings about repetition over days are reasoned from the line-selection rule (ADR-080 D2), not observed.
- Nothing here was measured with a player. The findings are what the frames and the code show.

## Findings

Each finding has a severity (**high**: the player cannot tell what to do; **med**: a clear cost each session; **low**: polish)
and a status: fixed on this branch, or open with the recommended next step.

### 1. Quest clarity: where to go and what to do is hidden (high, fixed; waypoint fixed in round 2)

- The tracker shows the title, one objective with a count, and the reward: "Till the soil · Till a patch of soil 0/1 · Reward: 10 coins"
  ([`before/hud-farm-vi-1280x720.png`](../qa/rebuild-npc-hud/before/hud-farm-vi-1280x720.png)). It says *what*, not *how*.
- Every quest has a `Quest_<key>_Tip` line in both languages (20 of 20, `Catalogue.resx` and `Catalogue.en.resx`). It says how and
  where: "Select the hoe (key 1) and press E…", "Leave the farm for Town, stand at the seed shop counter…". No screen showed it. The
  tracker put only the longer `Hint` in its tooltip, so a mouse player had to hover and a touch player could not see it at all.
- **Fixed:** the tip now shows under the title while the card is open ([`after/hud-farm-vi-1280x720.png`](../qa/rebuild-npc-hud/after/hud-farm-vi-1280x720.png),
  "Chọn cuốc và nhấn phím dùng để xới một ô đất."). The tip is one sentence, so the card grows by two lines. The tooltip keeps the long hint.
- **Fixed in round 2 (ADR-090 D1–D2):** the waypoint. Eight quests have a place (`QuestWaypoints`, by key): an arrow at the screen edge
  while the place is off screen, and a dot on the minimap. Tilling, watering, cooking and fishing have no fixed tile and show none.

### 2. Dialogue is atmosphere, not guidance (med, partly fixed in round 2)

- The seed keeper's three lines on the first morning: "Damp soil is my favourite smell." → "Stop by before you head to the fields…" →
  "Spring is here, and the beans and radishes just came in." (`before/tour-1280x720.json`, the `greeting-*` and `talk*-*` fields.)
  None names the active quest, and none says what to buy. The seed counter is where the Buy seeds step is done, so this is the
  one place the line could help and does not.
- The lines do react to state already: season, weather, time of day, and progress (Newcomer, Harvested, DeepMiner) through
  `NpcLineContext` (ADR-080 D2–D3). The gap is that *quest progress* is not one of the tags.
- **Repetition (reasoned, not observed):** the line is `FNV-1a(merchant, game day) mod count` among the fitting lines, so a
  merchant says one line all day. Talk moves to the next fitting line. With sixteen lines and four tag groups, a newcomer on
  an untagged moment sees the untagged lines most often. Repetition over a week is the open question; it needs play.
- **Fixed in round 2 (ADR-090 D3):** the greeting of the merchant where the active quest sends the farmer names the quest
  (`Npc_{Id}_Quest_{key}`, six lines in each language). Talk keeps the rotating pool. The progress tag `NpcLineTags` for the
  quest is not added; the greeting is the one place it is needed.

### 3. Jargon on the tailor's buttons (low, fixed in round 2)

- The tailor offers "Wardrobe" / "Tủ đồ" and "Backpack" / "Túi đồ" ([`before/npc-greeting-en-1280x720.png`](../qa/rebuild-npc-hud/before/npc-greeting-en-1280x720.png)).
  A newcomer does not know that the backpack means more bag space. **Recommended:** a Tip-style hint in the button's tooltip, or
  clearer labels in the `.resx` (the "Npc_Action_*" keys). Fixed in round 2 (ADR-090 D6): "Change clothes" and "More bag space".

### 4. Journal: no way to see what is missing (med, fixed)

- The journal is one grid of all 212 items in six pages. On the first visit 8 are found, and the found items are scattered
  through the first pages ([`before/journal-all-vi-1280x720.png`](../qa/rebuild-npc-hud/before/journal-all-vi-1280x720.png)).
  Nothing filters it, so a player who wants to find the missing items pages through silhouettes.
- **Fixed:** three tabs under the summary, All / Found / Missing, in the `tab` button kind of `ui-standard.md` section 8. The
  filter is local (no request); changing the filter returns to the first page
  ([`after/journal-missing-vi-1280x720.png`](../qa/rebuild-npc-hud/after/journal-missing-vi-1280x720.png)).
- **Open (low):** the journal says what is obtained, not where a missing item comes from. That needs the same location data as
  finding 1.

### 5. World labels stack over the player (low, fixed in round 2)

- The name plate "Khách 2708" sits directly over the direction label "↑ Nhà nông trại" on the farm, and "Trạm xe buýt" sits
  beside the player's head ([`before/hud-farm-vi-1280x720.png`](../qa/rebuild-npc-hud/before/hud-farm-vi-1280x720.png)). Both are
  world-anchored (`ui-standard.md` section 4). Fixed in round 2 (ADR-090 D4): a farmer within 40 px of an exit has the name plate
  lifted 16 px above the exit's label. The client does not know the label's size, so this is a fixed band, not a measured stack.

### 6. The HUD on a normal day: density and gaps (low)

- At 1280×720 the top-left card, the calendar bar, coins, mail, settings, the minimap and the menu column of eight all fit with no
  overlap (`ui-standard.md` section 7, items 2 and 3). The bottom row holds the hint, the hotbar and the gauges.
- Gap: nothing shows "where the next quest step is" except the tracker (finding 1). Density is not the problem; the content of
  the one card is.

### 7. Phone: the tracker grew into the menu column (high, found and fixed during review)

- The first version of the tip (the change above) did not wrap at 390×844. The card sized itself to the whole sentence and
  ran under the collapsed menu button (the fixed build's frame is [`after/hud-farm-en-390x844.png`](../qa/rebuild-npc-hud/after/hud-farm-en-390x844.png); before the fix:
  the line cut off at the right edge, and the card overlapped the Menu button).
- **Fixed:** the top-left quest card has a maximum width of 120 `--u` (`GameHud.razor.css`). The tip wraps inside it; the card ends
  well short of the menu column ([`after/hud-farm-en-390x844.png`](../qa/rebuild-npc-hud/after/hud-farm-en-390x844.png), "Select the hoe and press the use
  key to till a patch of soil.").

### 8. Notification noise (low; the hint timing fixed in round 2)

- Toasts merge repeats with a count ("×N", `docs/qa/toast-dedupe`), at most two lines, three seconds each. The onboarding hint
  stays twelve seconds (ADR-033 D3), and the quest completion banner five seconds. In one morning the only lines on the screen
  were the onboarding hint and the tracker. A new hint now waits for the player to be idle for 2 s (ADR-090 D5), so it never comes
  up over the action in progress. The rule to keep a hint until its step is done (`research-hud-and-loops.md` section 7) holds: a
  hint stays on screen until its step changes or it times out.

## What changed on this branch

| Change | Where | Wire | Tests |
| :-- | :-- | :-- | :-- |
| Quest tip under the tracker title | `QuestTrackerViewModel.Tip`, `QuestTracker.razor(.css)` | none (client text from `Catalogue`) | `QuestTrackerViewModelTests`: tip for an active quest, empty when finished, every quest has text in both languages |
| Journal All / Found / Missing tabs | `JournalViewModel.Filter`, `SetFilter`, `FilteredCells`, `JournalPanel.razor(.css)`, `Journal_Filter_*` in `Language(.en).resx` | none (the filter is local) | `JournalViewModelTests` (new): found, missing, all, one page, page reset on a change, same filter keeps the page |
| Quest card maximum width | `GameHud.razor.css` | none | checked by the 390×844 capture |

**Wire:** no packet, opcode or field changed, so `NosumProtocol.Version` is not bumped (the rule is for wire changes, and there
were none). The new strings are text, and the server sends none.

**Scope against the brief:** the brief asked for three to five improvements. Two shipped (findings 1 and 4), plus the layout
fix the phone capture exposed (finding 7). Finding 2 needs authored lines in both languages, and finding 3 is a wording decision
for the owner. Both are recorded above rather than guessed at here. The waypoint (finding 1, open) needs location data that
does not exist yet.

## Acceptance

Captures and the grading against `ui-standard.md` section 7 are in [`docs/qa/rebuild-npc-hud/README.md`](../qa/rebuild-npc-hud/README.md).
