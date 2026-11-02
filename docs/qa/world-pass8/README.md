# World QA pass 8 (branch `claude/qa-pass8`, base `origin/master` d621f08)

Pass 7 opened the panels; pass 8 performs each action and checks it. Every flow runs as a guest on a Development server (`window.nosumQa`,
ADR-079), in Vietnamese and English, at 1280x720 and 390x844. Each step saves a frame and records what it checked. The harness is
`tests/browser/world-pass8.cjs`; the two-player social flow is the existing `tests/browser/social-round3-play.cjs` (run per size and language).

## Result

| Flow | What is performed and checked | 1280x720 vi / en | 390x844 vi / en |
|:--|:--|:--|:--|
| farm: animals | Coop built (blueprint, two days). Carpenter Animals tab: two buys, coin balance falls (20,560 to 18,140). Hay fed at the trough (hay 30 to 6). Day rolls; a hen's egg collected ("You got Egg."). | pass | pass |
| farm: pets | Cat bought (Pets tab, name, confirm). Petted by the action key (already petted today, status shown). Next day: petted again, mood 60 to 70, status "Done.". Forage is the first petting of a day (ADR-092 D2): not seen in a capture. | pass | pass |
| town: backpack | Tailor's counter: "Backpack_Size" 45 slots after the upgrade. | pass | pass |
| town: wardrobe | Hair style 3 ("Bowl cut", "Wearing: Bowl cut"), skin tone. Straw hat picked from the bag and worn in the equipment card (slot "Hat" shows the hat). | pass | pass |
| town: world map | Tab opens it; "You are here" on Town; pages 1 of 3. | pass | pass |
| bin: shipping sale | Parsnips held, facing the bin: "Sold for 200 coins.", coins 500 to 730. The coin pop lives 200 ms and was not captured (see Gaps). | pass | pass |
| bin: furnace | Furnace placed; 5 copper ore and 1 coal smelt (15 real minutes); "Collect bar" collected. | pass | pass |
| account | Profile: "Save progress with an account" opens the link form; sign-out asks "Sign out?" and Keep playing closes it. | pass | pass |
| mines | Entrance elevator opens ("Deepest: floor 0", the entrance and the farmhouse). A monster beside the farmer (ADR-079 D9): six swings, "6x COMBO!" on the HUD. | pass (2 skipped) | pass (2 skipped) |
| market | B lists a parsnip at 1,200 coins; A buys it from the trader; the "Confirm purchase" dialog reads "Buy 1 of Parsnip for 1,200 coins?"; Yes buys; B's sale mail "Parsnip x1 sold. You receive 1,140 coins after the fee." | pass | pass |
| co-op | Founded at the board keeper; six seeds deposited; all six withdrawn; warehouse "The warehouse is empty."; log heading shown with entries. | pass | pass |
| social (two players) | Befriend, friend presence, chat, trade (both confirm, "Trade complete."), visit farm, co-op deposit, co-op invite. Existing script `social-round3-play.cjs`. | pass | pass |

"pass" means every step's check passed and the page raised no error (0 page errors in every run). The harness's own checks are DOM text,
the coin and hotbar counts, and the server's reply. Frames are the evidence for the look. Final runs: `docs/qa/world-pass8/<flow>/<lang>/<size>/`
(`report.json` in each), social in `docs/qa/world-pass8/social/<size>/`.

Not driven (recorded as `skipped` in the report, not as passes): mine floors beyond the entrance (`Deepest: floor 0` on a new account, and the
harness does not locate the stairs), and ore yield (the floor's nodes come from the server and are not in the client's text).

## Fixed in this PR

| # | Issue | Evidence | Fix | Test |
|--:|:--|:--|:--|:--|
| 1 | "Menu" text sat under the bottom icon of the desktop menu column at 1280x720 | Seen in every desktop frame of the pre-fix runs (the bottom-right corner, "Menu" under the icon; those frames were replaced by the final runs). | `GameHud.razor.css`: `.hud-menu-label` is hidden outside the phone media query, as it was meant to be (the phone rule shows it) | `tests/browser/hud-menu-label-check.cjs` (desktop hidden, phone shown; exit 1 otherwise). Passes. |
| 2 | Sticky "Close" tooltip after a window closes or opens under a resting pointer | Pre-fix run, world map after the wardrobe (frame replaced by the final run): the map window opened under the pointer that had just closed the wardrobe, and its Close tip showed with no movement | `nosum-theme.js`: a tip shows only after a real pointer move (`mousemove`), and a press, a click or a key clears it; a tip whose target is hidden in place is removed too | `tests/browser/tooltip-rest-check.cjs` (no tip for a window opened under a resting pointer; the tip shows on a move; Escape clears it). Passes. Note: with real clicks and keys the stuck state did not reproduce on the pass-7 build; the reproduced case is the layout one above. |
| 3 | Stage-0 sprout too small to read | `crop-stage0/seeds-old-new-all-21-rows.png` (old and new, all 21 rows), `crop-stage0/sown-1280x720-en.png` | `tools/art_repack/crops.py`: each seed picture gets a one-pixel dark-green outline (`_outlined_seed`). Parsnip 34 to 83 opaque pixels; the smallest picture 13 to 26. `assets/sprites/crops.png` regenerated: only frame 0 of the 21 rows changed (1,001 pixels). | `CropSeedArtTests` (every seed is green; at least 25 opaque pixels). Passes. |

The art repack also rewrote `assets/sprites/items.png` with bytes that differ from the committed file (not from this change). That file was reverted and is not in this PR (see issue 11).

## Ranked issues (not fixed in this pass)

1. **Furnace batch cannot be finished by the dev clock** (medium, QA and pipeline). `FurnaceService` compares the batch with real server time
   (`_time.GetUtcNow()`, `FurnaceRules.StateOf`), while `DevAdvanceClock` moves only the game calendar. A batch of 30 game minutes is 15 real
   minutes (`DefaultDayLengthMs` 12 h), so the harness waits (16.5 minutes). The furnace window also read "14:59 left" at 3:00 and "14:49 left"
   at 3:07 (the pre-final furnace window text in the bin run's log, `/tmp`-only, not kept); this may be a stale readout, which needs a trace before it is called a bug.
2. **The inventory hint assumes a mouse on a phone** (medium, copy). `Inventory_Hint`: "Click to pick up · right-click to split half ·
   Shift+click to quick-move" (vi: "chuột phải", "Shift+nhấn"). On 390x844 it is shown as-is (`town/vi/390x844/05-wear-hat-from-bag.png`,
   `town/en/390x844/05-wear-hat-from-bag.png`). Needs a touch variant of the string (adding keys is allowed) chosen from the input mode.
3. **The stage-0 sprout is still faint at night** (low-medium, art). In `crop-stage0/sown-1280x720-en.png` (12:20 AM) the sprout reads as a
   small dark-green shape on dark soil. Rows 2, 3, 18 and 19 start from 13 to 16-pixel source pictures and stay small (26 to 32 pixels).
   Next step: a daytime capture and a redraw of those four pictures, not a further outline.
4. **Mail opens with no mail selected** (low, UX). The mailbox list shows "Your listing sold" with "Unread", but the body and any gift show
   only after the entry is pressed; the panel text reads "Mail is deleted after 30 days…" in its place
   (the harness selects the entry and records its body; the frame `market/en/1280x720/05-B-mail-collect-all.png` shows the state after that).
5. **Co-op on a phone: the last upgrade row is cut at the window's bottom edge** (low). "More land" is cut off in `coop/en/390x844/03-coop-deposit-seeds.png`
   and no scroll cue shows. Needs a check of whether the window scrolls.
6. **The quest card takes about a fifth of a phone's view** (low). Top-left, 230 x 150 px at 390 wide (`bin/en/390x844/01-bin-coin-pop-0.png`,
   `mine/en/390x844/02-fight-monster.png`).
7. **After a petting the pet panel still says "You already petted them today."** (low, copy). The notice is the state line; "Done." below it
   is the result, so after a success the two read like a refusal (`farm/en/390x844/13-pet-open-next-day.png`).
8. **The sign-out question's three buttons wrap** (low, layout). "Keep playing" goes to a second row (`account/en/1280x720/03-sign-out-question.png`).
9. **Unused strings** (low, code). `Account_Upgrade_Prompt` and `Account_Upgrade_Action` have no caller (`AccountFormViewModel.UpgradePromptText`
   and `UpgradeText`); Profile uses `Account_SaveProgress_*`. Remove both keys from the two `.resx` pairs with the view model members.
10. **The wardrobe does not list clothes** (low, discoverability). Its text says "Change clothes in your bag", but worn clothes and the bag are in the
    Inventory panel (`EquipmentCard`). Works, but a player looking in the wardrobe finds nothing to pick.
11. **The art repack is not reproducible for `items.png`** (low-medium, pipeline). Running `repack_cc0_art.py` changes `assets/sprites/items.png`
    from the committed bytes (the `--check` run reports the first difference only). Not changed in this pass; the generator or the committed file
    needs a decision.
12. **"Opening hours: not posted yet" on every map card** (low, copy). `town/en/390x844/07-world-map.png`.
13. **The shipping coin pop is not in any frame** (harness gap). The pop lives 200 ms after the bin's answer; bursts of frames started at
    the key press did not catch it. The sale itself is confirmed (coins and the "Sold for 200 coins." toast). A video or a server-side hook is
    needed to grade the pop.

## Gaps (what this pass did not drive)

- Mines: floors beyond the entrance (no reached floor on a new account; stairs not located), ore yield (server-placed nodes).
- Mail: the sale mail was driven; the co-op invite mail and a gift (item) mail were not.
- Co-op: a second member's withdraw and the log of another member were not driven; the founder's deposit, withdraw and log were.
- Anvil (smithing) and the market's own price history were not driven.
- Forage from a pet is the first petting of a game day (ADR-092 D2); the harness pets the cat on the next day, but no forage item was seen.
- Grading: every final frame is saved and checked by its step. I viewed about 25 frames by eye (the paths above, plus the farm, carpenter,
  market confirm, wardrobe, world map, account, co-op, mine and social frames named in the issues). The rest are in `docs/qa/world-pass8/` for
  review; their checks are in the `report.json` files.

## Reproduce

```
# server (Development, PostgreSQL role nosum, database nosum_dev)
DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host
# tester client (ADR-079)
dotnet run --no-build --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080

node tests/browser/world-pass8.cjs farm vi,en 1280x720,390x844
node tests/browser/world-pass8.cjs town  vi,en 1280x720,390x844
node tests/browser/world-pass8.cjs bin   vi,en 1280x720,390x844   # about 17 min per run (furnace)
node tests/browser/world-pass8.cjs account|mine|market|coop vi,en 1280x720,390x844
BASE=http://localhost:5080/ LANGS=vi,en SIZE=1280x720 OUT_DIR=docs/qa/world-pass8/social/1280x720 node tests/browser/social-round3-play.cjs
node tests/browser/tooltip-rest-check.cjs
node tests/browser/hud-menu-label-check.cjs
node tests/browser/world-pass7-crop-probe.cjs http://localhost:5080/?qa=1 docs/qa/world-pass8/crop-stage0 1280 720 en

dotnet build tests/Nosum.Tests.slnx -warnaserror
dotnet test tests/Nosum.Tests.slnx --no-build
node --test tests/browser/modules/wwwroot-modules.test.mjs
```
