# World QA pass — Nosum (branch `claude/qa-world-pass`)

Run on a local Development host (`DOTNET_ENVIRONMENT=Development`, PostgreSQL `nosum_dev`) with the tester client
(`-p:NosumDevTools=true`, ADR-079, `?qa=1`). Browser: Playwright Chromium. Script: `tests/browser/world-pass.cjs`.
Output: `docs/qa/world-pass/<locale>/<viewport>/NN-<step>.png` and `docs/qa/world-pass/audit-<locale>-<viewport>.json`
(279 records over 6 combinations of 1280×720, 1920×1080, 390×844 (touch) in `vi` and `en`).

**Scope, honestly stated.** One session per combination plays the world features in order: running and energy, the
minimap toggle, farming (till, sow, water, sprinkler, growth, harvest, wither), the three Town shops and their merchant
dialogues, the market counter, fishing (QA bite and catch card), combat skills, crafting, mail, friends, skills, the co-op
and profile panels, the season change, NPC dialogue and the level-up banner. About a third of the listed features were
**not** exercised (see "Not covered"). Bin sales, shipping, animals, pets, the market's listing and buy, mail send,
the backpack upgrade and the rename scroll have no scripted path yet.

## How grading works

- **Functional** rows (`pass` / `FAIL`) come from an outcome the page shows: a hotbar count that dropped by one (sow,
  water), the tutorial hint moving on (till), a merchant dialog or shop list (`.npc-box`, `.shop-list`), a season
  banner or the level-up banner, or a minimap toggle. A `FAIL` is a missing outcome or a thrown step.
- **`captured`** means the step ran and the screenshot is in the folder, but no DOM outcome was checked. These need a
  person to read the screenshot against section 7 of `docs/design/ui-standard.md`. **This pass did not do that reading
  for every screen.** Only the automatic layout checks below were measured.
- **Section 7 automatic checks** on the open window: item 2 (overlap with the HUD top-right, top-left and hotbar),
  item 5 (off-screen) and item 7 (inner scroll at 1280×720 and above). No overlap or off-screen fault was recorded in any
  combination. Inner scroll was recorded only on the 1920×1080 crafting and skills pages (below).
- Items 1, 3, 4, 6, 8, 9 and 10 need a person; none are graded here.
- Console and page errors: none recorded in any of the six combinations.

## Results

| Feature | vi 1280×720 | en 1280×720 | vi 1920×1080 | en 1920×1080 | vi 390×844 | en 390×844 |
|---|---|---|---|---|---|---|
| HUD start | captured | captured | captured | captured | captured | captured |
| running + energy | captured | captured | captured | captured | captured | captured |
| minimap toggle (M) | pass | pass | pass | pass | **FAIL** | **FAIL** |
| farm: grant seeds, sprinkler, rod | captured | captured | captured | captured | captured | captured |
| farm: till (hoe, E) | pass | pass | pass | pass | pass | pass |
| farm: sow (seeds, E) | pass | pass | pass | pass | pass | pass |
| farm: water (can, E) | pass | pass | pass | pass | pass | pass |
| farm: sprinkler (place, E) | **FAIL** | captured | **FAIL** | captured | **FAIL** | captured |
| farm: advance 4 days (grow) | captured | captured | captured | captured | captured | captured |
| farm: harvest (E, empty hand) | captured | captured | captured | captured | captured | captured |
| farm: wither (no water, 10 days) | captured | captured | captured | captured | captured | captured |
| merchant: seed shop dialog | pass | pass | pass | pass | pass | pass |
| shop: seed shop buy list | pass | pass | pass | pass | pass | pass |
| shop: seed shop quantity stepper | captured | captured | captured | captured | captured | captured |
| merchant: clothing shop dialog | pass | pass | pass | pass | pass | pass |
| shop: clothing shop buy list | pass | pass | pass | pass | pass | pass |
| shop: clothing shop quantity stepper | captured | captured | captured | captured | captured | captured |
| merchant: furniture shop dialog | pass | pass | pass | pass | pass | pass |
| shop: furniture shop buy list | pass | pass | pass | pass | pass | pass |
| shop: furniture shop quantity stepper | captured | captured | captured | captured | captured | captured |
| market: open | captured | captured | captured | captured | captured | captured |
| fishing: cast (rod, E) | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| fishing: bite (QA) | captured | captured | captured | captured | captured | captured |
| fishing: reel in and catch card | captured | captured | captured | captured | captured | captured |
| skills: cast Z then refused | **FAIL** | **FAIL** | **FAIL** | **FAIL** | **FAIL** | **FAIL** |
| crafting panel | captured | captured | captured | captured | **FAIL** | **FAIL** |
| panel: mail | captured | captured | captured | captured | captured | captured |
| panel: friends | captured | captured | captured | captured | **FAIL** | **FAIL** |
| panel: skills | captured | captured | captured | captured | **FAIL** | **FAIL** |
| panel: crafting | captured | captured | captured | captured | **FAIL** | **FAIL** |
| panel: co-op warehouse (G) | captured | captured | captured | captured | captured | captured |
| panel: profile, rename (N) | captured | captured | captured | captured | captured | captured |
| calendar: season passes (QA) | pass | pass | pass | pass | pass | pass |
| NPC dialogue (board keeper) | pass | pass | pass | pass | pass | pass |
| level-up banner (QA) | pass | pass | pass | pass | pass | pass |
| animals (barn, feed, harvest) | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| pets: buy at carpenter, rehome | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| crafting at stations (chest, furnace) | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| market: list and buy | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| mail: send | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| co-op warehouse deposit | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| backpack upgrade | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| rename scroll | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| shipping bin sale | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| dungeon/mine combat | not exercised | not exercised | not exercised | not exercised | not exercised | not exercised |
| console / page errors | pass | pass | pass | pass | pass | pass |

`pass` = the DOM outcome was seen. `captured` = a screenshot exists; grade it by eye (see above). `FAIL` = see the issues.
`not exercised` = no scripted path in this pass.

Screenshots: `docs/qa/world-pass/<locale>/<viewport>/NN-<step>.png`. Example rows: the till at
`en/1280x720/06-farm-till-hoe-E.png`, the shop stepper at `en/1280x720/18-shop-clothing-shop-quantity-stepper.png`,
the merchant dialog at `vi/1280x720/*-merchant-seed-shop-dialog.png`.

## Issues (ranked by severity)

Severity: **High** blocks or hides a feature or the test coverage of one; **Medium** is a visible or reproducibility
fault; **Low** is cosmetic or a single observation.

The brief asked for the top 20. **This pass has evidence for seven.** The rest of the 20 would be guesses, so they are
not listed. Several checklist items were not graded (see above), so more issues are likely once the screenshots are read.

| # | Sev | Where | Issue | Evidence |
|---|---|---|---|---|
| 1 | High | Combat skills (`Z`) | Pressing `Z` twice shows no result: no cooldown, no toast, no mana change, in all six combinations. It may need a target, which this pass did not set up, so the cause is unconfirmed. | `*-skills-cast-Z-then-refused.png` |
| 2 | High | Fishing | The cast is not exercised: the scripted beach teleport lands on the Town road, not the shore. The bite and catch card were captured, but a cast was never made in this pass. `tests/browser/fishing-qa.cjs` covers it. | `*-fishing-cast-rod-E.png` |
| 3 | Medium | Crafting, Skills at 1920×1080 | Inner scroll on the page body: crafting 777/739 px and skills 776/739 px. Breaks section 7 item 7. Same family as the full-pass findings. | audit `vi/en 1920x1080`, `panel: crafting`, `panel: skills` |
| 4 | Medium | Phone 390×844: Crafting, Friends, Skills | These three are not on the top-right toolbar at 390×844; the script clicked them there and failed. The path is probably the Menu column, so this is a test-path gap, but it also needs checking as a UX point: the phone has no direct way to these panels from the toolbar. | audit `vi/en 390x844` |
| 5 | Medium | QA reproducibility | The server's game clock is shared and only moves forward. A run starts in whatever season the last run left (the first run here began in Winter, and parsnip seeds were correctly refused: "That crop does not grow in this season."). The script now passes seasons until Spring; testers must do the same. | `audit-en-1280x720.json` first run; script `farm` block |
| 6 | Low | Minimap on phone | No minimap card at 390×844 (`.hud-minimap` absent before the toggle). May be intentional; not confirmed. | audit `minimap toggle (M)`, 390×844 |
| 7 | Low | Hotbar tooltip | Once, the tooltip reads "Pickaxe" while the fishing rod slot is selected. Single observation; not reproduced. | `en/1280x720/23-fishing-cast-rod-E.png`, `26-skills-cast-Z-then-refused.png` |

Checked and correct (not bugs): the farm till refusal on non-diggable ground ("You can't till here."), the out-of-season
parsnip refusal, the shop stepper (`x1`, `x5`, `All`, `Less`, `More`, `Buy`, and `Previous`/`Next` on the tailor page),
the season change banner, and the NPC dialog's Buy/Talk/Leave choices (which appear after the line types out).

**Test defects found and fixed in the script** (not game bugs): the Vietnamese sprinkler name (`Vòi tưới`) was not
matched (sprinkler step failed in `vi` at all sizes; regex now fixed, **not re-run**); the minimap read used a selector that
did not match; the English settings window could be left open. The vi sprinkler rows above are therefore stale.

## Not covered in this pass

- Fishing cast and reel-in on a real shore (issue 2).
- Animals (barn, feed, harvest), pets (buy at the carpenter, rehome), crafting at stations (chest, furnace).
- The market's listings (`market: open` is captured only), listing and buying, mail send, co-op warehouse deposit,
  the backpack upgrade, the rename scroll, the shipping bin, and dungeon/mine combat.
- Visual grading of section 7 items 1, 3, 4, 6, 8, 9 and 10 for every `captured` row.
- Touch controls at 390×844: the script drives the game with the keyboard even on the phone profile.

## Reproduce

```
DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host
dotnet run --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080
node tests/browser/world-pass.cjs en,vi 1280x720,1920x1080,390x844
```

The run takes about 20 minutes for all six combinations.
