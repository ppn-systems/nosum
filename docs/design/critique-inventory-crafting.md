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

**F10. Standard drift in the button kinds.** `ui-standard.md` §8 names `secondary`; the code has `GameButton` variants `primary`, `danger`,
`plain`, `tab`, `link`, `chip`, and `plain` is used 27 times as the secondary kind. The standard should be corrected to match the code, or
the code renamed; this branch keeps `plain` for the new buttons so the row matches.

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
