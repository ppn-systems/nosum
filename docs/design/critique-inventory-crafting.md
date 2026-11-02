# Critique: inventory, hotbar, backpack, chests and crafting

Status: review of the rebuild-inventory-crafting branch. Covers what a player does with the bag, the hotbar, the chest page and the crafting
page today, the clicks each common task takes, what is missing, and the gaps against `docs/design/ui-standard.md`. Each claim cites the
frame or code it rests on. The improvements made on this branch are listed in section 5; the rest is a backlog.

## 1. Method and limits

- **Browser pass.** Playwright against the dev server and the wasm client (guest login), at 1280×720, 1920×1080 and 390×844 (touch), in
  Vietnamese and English. Captures: `docs/qa/rebuild-inventory-crafting/{before,after}/{vi,en}/{viewport}/`. The language is the player's
  saved setting (`nosum.settings.v1`), not the browser's; the capture sets it.
- **Interaction check.** `tests/browser/inventory-interact-check.cjs` drives a right-click split and a shift-click against the real server
  (results in `docs/qa/rebuild-inventory-crafting/interact-en.txt`).
- **Not verified in a browser:** the chest page (it needs a placed chest; the capture does not reach it), craft-max on a recipe with
  materials (covered by unit tests only), and touch long-press on a real device (a long press opens the context menu, which the slot now
  handles; the headless touch emulation does not produce it). These are marked where they matter.
- A dedicated "Dev QA hook" was not found in the repository; the existing capture scripts pattern was used instead.

## 2. Clicks per common task

"Before" is the code on `master` at the start of the branch; "after" is this branch. A click is one pointer action.

| Task | Before | After | Notes |
| :-- | :-- | :-- | :-- |
| Move a whole stack to an empty slot | 2 (pick, place) | 2 | No drag and drop; a pick is not visibly sticky, so the hand card is the only cue (`before/en/1280x720/01-bag.png`). |
| Move a stack from the hotbar to the bag | 2 | 1 (shift-click) | The chest page already had shift-click; the bag page did not. |
| Split a stack (put half somewhere) | **not possible** | 2 (right-click, place) | The server already applies a partial move (`MoveSlot.Quantity`, `InventoryGrid.Move`); no page sent one. |
| Sort the bag | 1 | 1 | The hotbar lock checkbox sits beside the button (`before/en/1280x720/01-bag.png`). |
| Move a stack into an open chest | 2, or 1 with shift | unchanged | Chest page: shift-click works; the hint line says so. |
| Craft one | 2 (select recipe, Craft) | 2 | |
| Craft five | 3, **only when the ×5 button is shown** | 3 | See finding F3. |
| Craft a number that is not 1 or 5 (say 4 fences) | 5 (select, then four Craft clicks) or give up | 2 (select, craft max) | Craft max is capped by the items and by the server's batch limit of 99. |
| Find a recipe by name | up to 30 hovers | up to 30 hovers | No search or sort; see F6. |
| See what a recipe still needs | select it, read the have/need rows | unchanged | Only one recipe at a time; no "missing ingredients" list. See F7. |

## 3. Findings

Ranked by how often a player meets them.

**F1. The bag cannot split a stack.** The wire and the grid support it (`MoveSlot.Quantity`, `InventoryGrid.Move`); the page never sent a
part. A player who wants four seeds out of eight has to craft or drop the rest. *Fixed on this branch:* right-click picks up half; the
place click sends that part. Verified in the browser (`docs/qa/rebuild-inventory-crafting/interact-en.txt`: "before 8, after 4", "bag slot
shows 4").

**F2. Nothing says how to move, split or quick-move.** The bag page had no hint; a new player learns "click, then click" by trial
(`before/en/1280x720/01-bag.png`). The chest page has one line of hint, the bag had none. *Fixed:* a hint line under the toolbar names the
clicks while nothing is picked, and names the count while a stack is held ("Holding 4: click a target slot to place, click again to put
down").

**F3. The craft batch button depends on the output's tooltip shape (code finding, not reproduced).** `CraftingPanel.razor` shows ×5 only
when the output's tooltip has zero lines or two or more line breaks. A tooltip is name, description, then one line per stat, so an item with
no stat line has one break and hides ×5. The default recipe (chest) showed ×5 in `before/vi/1280x720/02-crafting.png`; I did not check that
recipe's tooltip lines, so the browser pass does not confirm the cause. *Fixed:* ×5 is always shown. The rule was not a documented design decision.

**F4. Craft max does not exist.** The page offers ×1 and ×5 only, though the server accepts up to 99 (`RecipeCatalog.MaxBatch`). A player
who has 49 wood cannot make 24 fences in one action. *Fixed:* "Tối đa xN" / "Craft max xN" computes the most the items and the bag allow,
capped at 99, and sends that count. The server re-checks it (`RecipeRules.Check`).

**F5. The menu column breaks the UI standard.** `docs/design/ui-standard.md` §2 says the column is one column of at most eight buttons. The
DOM (1280×720, guest login) has 15 icon buttons in the top-right: Mail and Settings in the top row, then 13 in two columns of seven and six
(`before/vi/1280x720/00-hud.png`). This branch does not change the layout; it is the largest standard drift I found and needs its own change.

**F6. No recipe search or sort.** The 30 recipes are icons; the name is only on hover (`before/vi/1280x720/02-crafting.png`). On a phone
there is no hover, so a player cannot read the name of a recipe cell before selecting it. Not addressed: it needs a new control.

**F7. No "what am I missing" view.** The materials list shows have/need for the selected recipe (`0 / 50` in the frame). A player who wants
to know which recipes they are close to must select each one. Not addressed: it is a new view, not a fix.

**F8. Touch.** At 390×844 the bag slots are about 36 px, above the 14 `--u` touch minimum (`before/en/390x844/01-bag.png`). Tap-to-pick and
tap-to-place work. Drag and drop does not exist (and is not added here). Right-click and shift-click have no touch gesture except the long
press that opens the context menu, which the slot now uses for a split. That path is unverified on a device.

**F9. Tooltips.** The item tooltip is name, description, stats, quantity: clear on hover, and the hand card repeats it in full
(`before/en/1280x720/01-bag.png`). Locked recipes say what unlocks them in the tooltip and, once selected, under the title (`Craft_Unlock`). Both are hover-only, so the touch gap in F8
applies to them too.

**F10. Standard drift in the button kinds (closed on master).** `ui-standard.md` §8 names `secondary`; the code has `GameButton` variants `primary`, `danger`,
`plain`, `tab`, `link`, `chip`, and `plain` is used 27 times as the secondary kind. The standard should be corrected to match the code, or
the code renamed; this branch keeps `plain` for the new buttons so the row matches. *Round three check:* master now uses `primary`, `secondary`, `danger`, `tab` and `icon` only (`Variant=` in `wasm/`, 77 uses), and `ui-standard.md` section 8 lists the same five. The drift is gone; no change was needed.

## 4. Touch and small screens (summary)

| Check | Result |
| :-- | :-- |
| Bag fits 390×844 with the hint line | Yes. The English hint wraps to two lines (`after/en/390x844/01-bag.png`); it breaks at a word boundary, not word by word. |
| Crafting row of three buttons fits 390 px | Yes: Craft, Craft x5 and Craft max x0 on one row (`after/en/390x844/02-crafting.png`). |
| Slot size vs 14 `--u` | Yes (about 18 `--u`). |
| Hover-only information | Recipe names and locked-recipe text (F9). |

## 5. What this branch changes (no new system, no wire change)

1. **Split stack** (F1): right-click picks up half; the target click sends the part. Client validates (no request to an occupied slot of
   another item, which the server refuses); server validates the move.
2. **Shift-click quick move** in the bag (F2 and §2): the stack goes to the other section, into a same-item slot with room first, then an
   empty one; when neither exists the page says the bag is full.
3. **Craft max** (F4): the largest count the items and the bag allow, capped at the server's batch limit.
4. **Hint line** on the bag page (F2) and an always-visible ×5 (F3).

The wire is unchanged: `MoveSlot` and `Craft` already carry what these need, so `NosumProtocol.Version` is not bumped.

## 6. Backlog (not in this branch)

- Fix the menu column to the standard (F5). It is a layout change across the whole HUD.
- Recipe search or sort, and a "missing ingredients" view (F6, F7).
- Long-press and touch affordances tested on a device (F8).
- Correct `ui-standard.md` §8 to the real variant names (F10).

## 7. Round three (ADR-114)

- F10 is closed: the button kinds on master match `ui-standard.md` section 8 (see F10 above). No code change.
- The rest of the backlog is unchanged this round. F5 (the menu column) is a whole-HUD layout change, and F6 and F7 (recipe search and a missing-ingredients
  view) are new features; the brief for this round excluded both.

## 8. Round four (ADR-123, and the ADR-116 open item)

Scope: the "In hand" card that ADR-116 left clipped at 1920 × 1080, the furnace (not reviewed in this document before), and the held-tool refusal the bag's
swings produce. No new feature: F5, F6 and F7 stay in the backlog.

- **"In hand" at 1920 (fixed).** Measured on master with `tests/browser/inventory-hand-capture.cjs`: every hotbar item held in turn, three sizes, vi and
  en. At 1920 × 1080 four frames clip the description under the window's scroll fade: the Axe and the Pickaxe (en), the watering can and the axe (vi).
  The window is 900 px tall there, and `.menu-page-body` scrolls internally by up to 11 px. `ui-standard.md` section 3 allows no inner scroll at a desktop
  size. The cause is in `InventoryPanel.razor.css`: `.hand-stats` was a flex item at its full text width, so it could not share the name's line and wrapped
  under it. The CSS comment above it already says "the name and stats on one line", so the wrap was a bug against the existing design. Fix: the stats take
  the width the name leaves (`flex: 1 1 0`). After the change no held item clips at any size or language, and the 1280 × 720 strip is one line shorter
  (Hoe: strip bottom 639 → 617 px). Frames: [docs/qa/inventory-round4/](../qa/inventory-round4/README.md). `ui-standard.md` is unchanged: the fix
  meets the rule it already states, so it needed no new design decision.
- **The remaining limit (proposal, not applied).** The window's height is capped by the centre region, which ends at 1060 px on a 1080 px screen. A longer
  description could clip there again. Each fix changes a layout rule that is the owner's call: (a) let the XL window use the band below the hotbar; (b) make the
  bag's slots smaller at 1920 (16 u now); (c) move "In hand" beside the paperdoll column, which has the 1920 width but would change the window's
  structure. Recommendation: test (c) as a mock-up before a decision; it is the only option that adds no height.
- **Furnace refusal (fixed).** `Furnace_Status_NotEnough` said "5 ore and 1 coal" as literal text in both languages. It now takes `FurnaceRules.OreCost` and
  `CoalCost` (`Language.resx`, `Language.en.resx`; `CraftingTextTests.Language_FurnaceNotEnoughTakesItsCostsFromTheRules`), so the words cannot drift from
  the charge.
- **Furnace time (fixed in the comment; wording proposed).** `FurnaceRules` described a game minute as "a thirtieth of a real minute". At the default day
  (12 real hours and 1440 game minutes) it is half a real minute, so a batch of 30 game minutes is 15 real minutes, which is what the countdown shows. The
  comment is corrected; no value changed. The idle line still says a batch "smelts for 30 game minutes" next to a countdown in real time (`12:05`), so a
  player can read 30 minutes and wait 15. Proposal, not applied: state the real minutes (15) on the idle line, or give both. It is a presentation choice for the
  owner.
- **Touch and the split (open, not verified).** The touch hint reads "Tap to pick up · tap another slot to move it" and does not name the half-stack
  long press. The long press opens the slot's context menu, which the slot treats as a right click (`ItemSlotButton.razor`). Whether it splits on a real
  device is not verified here: the headless touch emulation does not produce the long press. Left as it is until a device check.
- **Held-tool refusal (fixed in gathering).** A tool too weak for a node names the tool in hand and the blacksmith: "Your Axe is too weak for that. Upgrade it
  at the blacksmith." (ADR-123, [critique-gathering section 7e](critique-gathering.md)). Frames: [docs/qa/inventory-round4/](../qa/inventory-round4/README.md).
- **Craft max cost (noted, no change).** `CraftingViewModel.MaxCraftable` steps down through up to 99 `RecipeRules.CanCraft` checks each time the page reads it
  (the label, the enabled state, the click). It runs on a redraw, not the frame, and the page is short, so it is not a feel problem.
- **Backlog (unchanged).** F5 (the menu column, a HUD change), F6 and F7 (recipe search, a missing-ingredients view, new features), F8 (the touch device check
  of the split and the long press), F9 (hover-only text on touch).
