# World QA pass 3 — Nosum (branch `claude/qa-world-pass3`)

Run on a local Development host (`DOTNET_ENVIRONMENT=Development`, PostgreSQL `nosum_dev`) with the tester client
(`-p:NosumDevTools=true`, `?qa=1`, ADR-079). Script: `tests/browser/world-pass.cjs` (pass 3 is the same script, extended).
Output: `docs/qa/world-pass3/<locale>/<viewport>/NN-<step>.png` and `docs/qa/world-pass3/audit-<locale>-<viewport>.json`.

**Read this first.** This pass has three kinds of evidence, and the README keeps them apart:

1. **DOM outcomes (`pass` / `FAIL`)** were checked by the script in all six combinations: the toast text, the cooldown sweep, a
   hotbar count, a status line, a merchant dialog.
2. **Visual grading against section 7 of `docs/design/ui-standard.md`** was done by eye for **four frames only** (listed below).
   The other `captured` rows are screenshots that exist and are **not graded**. This pass did not read every frame; a person must.
3. **`not exercised`** rows have no scripted path yet. Their features were not tested in this pass.

## What changed in the harness and the game

| Change | Where | Why |
|---|---|---|
| Spring reset: `nosumQa.springReset()` = `DevSeason` with 0 passes to the next Spring | `DevClockService.AdvanceSeasons`, `DevTools.ToSpring`, `QaEntry` | A run starts in whatever season the last run left (pass 2, issue 5). |
| Teleport next to a monster: `nosumQa.teleportToMonster('woods')` | `CombatChannel.TryGetLivingMonsterTile`, `DevTools.TeleportToMonster` | Combat keys need a monster in reach. |
| Beach shore at `(39, 43)` (was `(39, 2)`, the Town road) | `world-pass.cjs` `TILE.beach` | Read from `Beach.tmx`: the sea starts at `y = 44`. |
| Fishing cast by a **mouse click** on the sea | `world-pass.cjs` | A rod casts only by click (ADR-078 D2). Pass 2 used E, which does nothing with a rod. |
| **Z, X, V no longer silent**: a refused press names its reason | `SkillPressRefusal.KeyFor`, `PlayerActions.TryCastSkillFromEdges`, `GameLoop.SkillPressRefused`, `Combat_Refused_NoTarget` (vi, en) | The pass 2 High bug (issue 1 below). |
| Unit tests for the refusal rules and the Spring pass | `tests/Nosum.UnitTests/Wasm/SkillPressRefusalTests.cs`, `DayClockServiceTests.cs` | Pure decisions, no browser needed. |

**Root cause of the High bug (skill Z gives nothing).** Z is Whirlwind (`SkillCatalog`), which needs a live monster within
2 tiles. With none in reach, `PlayerActions.CanCastSkill` returned `false` and the press was dropped before it was sent,
so the server's own refusal toast (`SkillRefusalText`, which already exists) never fired. It was a **target** problem,
and the game was silent about it. The server was not at fault. The fix names the reason on the client, and the server
still decides every outcome.

## Verification

- `dotnet build server/Nosum.Host`: 0 warnings, 0 errors.
- `dotnet build wasm/Nosum.Wasm` (production): 0 warnings, 0 errors. The built `Nosum.Wasm.dll` has no `nosumQa` string.
- `dotnet build wasm/Nosum.Wasm -p:NosumDevTools=true`: 0 warnings, 0 errors.
- `dotnet test tests/Nosum.UnitTests`: 3384 passed, 0 failed.
- `dotnet test tests/Nosum.IntegrationTests --filter DevQa`: 15 passed (the Production host still refuses every tester opcode).
- Browser matrix: 6 combinations, 305 records on the first full run; the four combinations with the harness fixes were rerun.

## Results

Statuses are from the audit files. `captured` means the screenshot exists and is **not graded** (see the top of this page).

| Feature | en 1280×720 | en 1920×1080 | en 390×844 | vi 1280×720 | vi 1920×1080 | vi 390×844 |
|---|---|---|---|---|---|---|
| settings: English | pass | pass | pass | - | - | - |
| HUD start | captured | captured | captured | captured | captured | captured |
| running + energy | captured | captured | captured | captured | captured | captured |
| minimap toggle (M) | pass | pass | n/a | pass | pass | n/a |
| farm: grant seeds, sprinkler, rod | captured | captured | captured | captured | captured | captured |
| farm: till (hoe, E) | pass | pass | pass | pass | pass | pass |
| farm: sow (seeds, E) | pass | pass | **FAIL** | pass | pass | pass |
| farm: water (can, E) | pass | pass | pass | pass | pass | pass |
| farm: sprinkler (place, E) | captured | captured | captured | captured | captured | captured |
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
| fishing: cast at the shore (rod, face sea, E) | pass | pass | pass | pass | pass | pass |
| fishing: bite (QA) | captured | captured | captured | captured | captured | captured |
| fishing: reel in and catch card | captured | captured | captured | captured | captured | captured |
| combat: Z with no monster in reach (toast) | pass | pass | pass | pass | pass | pass |
| combat: teleport next to a monster (woods) | captured | captured | captured | captured | captured | captured |
| combat: Z with a monster in reach (cooldown sweep) | pass | pass | pass | pass | pass | pass |
| combat: X fireball (monster in reach) | captured | captured | captured | captured | captured | captured |
| combat: V heal at full health (toast) | pass | pass | pass | pass | pass | pass |
| crafting panel | captured | captured | **FAIL** | captured | captured | **FAIL** |
| panel: mail | captured | captured | captured | captured | captured | captured |
| panel: friends | captured | captured | **FAIL** | captured | captured | **FAIL** |
| panel: skills | captured | captured | **FAIL** | captured | captured | **FAIL** |
| panel: crafting | captured | captured | **FAIL** | captured | captured | **FAIL** |
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
| menu (phone): crafting, friends, skills in the game menu | - | - | **FAIL** | - | - | **FAIL** |

Notes on the table:
- `farm: sow` **FAIL** at en 390×844 is open. The real-time shared clock turned the season during the run ("That crop does not
  grow in this season."). The script then passed to the next Spring and sowed again, and the refusal came back. Not resolved.
- `minimap toggle (M)` is `n/a` on the phone: the phone HUD keeps one map button in the menu column, not the M card
  (`GameHud.razor.css`, max-width 640 px or a coarse pointer). It is not a fault.
- `menu (phone)` and the `panel:` rows at 390×844 are the phone reachability result (issue 1).
- `farm: water` passes in every combination now. Pass 3's first run recorded a refusal ("That does nothing here.") while it
  rained; the step is now recorded as captured when the count does not drop, with the reason in the notes.

## Graded frames (section 7, by eye)

| Frame | Checklist | Grade |
|---|---|---|
| `en/1280x720/23-fishing-cast-at-the-shore-rod-face-sea-E.png` (fishing: cast) | 1 region: the status line sits in the bottom toast stack ✓ · 2 no overlap ✓ · 4 edges ✓ · 6 text fits ✓ · 9 spacing ✓ · 10 vi not viewed | **pass for en; vi not graded** |
| `en/1280x720/26-combat-Z-with-no-monster-in-reach-toast.png` (refusal toast) | 1 toast stack ✓ · 2 the earlier "The fish got away!" toast and the new one stack with no overlap ✓ · 6 text fits on one line ✓ · 10 vi not viewed (vi text checked in the audit only) | **pass for en; vi not graded** |
| `en/1280x720/27-combat-teleport-next-to-a-monster-woods.png` (monster beside the farmer) | the frame does not show a monster clearly; the cooldown after Z proves one was in reach (row `Z with a monster in reach`) | **not verified visually** |
| `en/390x844/31-menu-phone-crafting-friends-skills-in-th.png` (phone Menu) | 1 the menu window is centred ✓ · 3 tiles one size ✓ · 5 GamePanel title and close ✓ · 6 "Profile" has an `N` hotkey shown on a touch screen (low) · 7 no inner scroll ✓ · the **content** is wrong: 3 of the 6 column panels are missing (issue 1) | **fails on content** |

## Top issues (ranked)

The brief asked for twenty. **This pass has evidence for eight.** The rest would be guesses, so they are not listed.

| # | Sev | Where | Issue | Status | Evidence |
|---|---|---|---|---|---|
| 1 | High | 390×844 (touch, ≤640 px) | Crafting, Friends, Skills, Inventory, World Map and Journal are not reachable at all on a phone. The column items are `display:none` on narrow and coarse screens (`GameHud.razor.css`), and `ToolbarViewModel.MenuItems` leaves out the column kinds, so the Menu shows only Visitors, Pet, Profile. The Menu button does not reach them. | Open. The fix changes the Menu on desktop too (a menu-layout decision, `ui-standard.md` sections 2 and 6). | `en/390x844/31-menu-phone…png`; FAIL rows at 390×844 |
| 2 | High | Combat Z, X, V | Z with no monster in reach was silent. The client dropped the press before the server could answer. | **Fixed in this PR.** Toast names the reason. | `en/1280x720/26-…toast.png`, rows `combat: Z with no monster` pass in six combinations |
| 3 | Medium | Farm, season | The real-time shared clock can turn the season during a run; a late-Spring run gets "That crop does not grow in this season." | Partly mitigated (the retry), not resolved at en 390×844. | `en/390x844` sow FAIL; audit notes |
| 4 | Medium | Fishing, reel-in | The QA bite reeled in 1.2 s later was missed: "The fish got away!" and no catch card in en 1280×720 and vi 1920×1080. The catch path is not verified by a pass. | Open (harness timing or the reel window). Not checked in the other four combinations. | audit `fishing: reel in and catch card`, en 1280 and vi 1920 |
| 5 | Medium | Combat, monster teleport | The woods frame does not show the monster clearly, so "beside a monster" is not verified by eye. The cooldown proves Z reached one. | Open: needs a frame with the monster in view. | `en/1280x720/27-…png`; row `Z with a monster in reach` |
| 6 | Low | Phone Menu | The Profile tile shows its `N` keyboard hotkey on a touch screen. | Open. | `en/390x844/31-…png` |
| 7 | Low | Farm, watering can | The can is a tool: its hotbar number is charges, not items, so a "count -1" check is wrong for it. The first run's watering FAIL was rain (the soil was already wet). | Harness fixed (captured when the count does not drop, with the reason). | `en/1280x720/08-farm-water-can-E.png`, audit notes |
| 8 | Low | Fishing, cast | The cast needs a click on water; the action key does nothing with a rod (ADR-078 D2). A QA script that presses E cannot cast. | Documented behaviour, not a bug; recorded so testers do not hit it. | row `fishing: cast` |

Carried from pass 2 and **not re-verified in this pass** (the panels were captured, not graded): the 1920×1080 inner scroll on
the crafting and skills pages (pass 2, issue 3).

## Not exercised in pass 3

These have no scripted path in this pass and no evidence either way: animals (barn, feed, harvest), pets (buy at the
carpenter, rehome), crafting at stations (chest, furnace), the market's list and buy, mail send, the co-op warehouse
deposit, the backpack upgrade, the rename scroll (only the profile window was captured), the shipping bin sale, and
dungeon or mine combat. Writing those paths is the next pass's work.

## Reproduce

```
DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host
dotnet run --no-build --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080
node tests/browser/world-pass.cjs en,vi 1280x720,1920x1080,390x844
```

The shared clock is real time. A run that starts late in a season can see the season turn; the spring reset helps but
does not stop it.
