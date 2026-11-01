# Full QA pass — Nosum (branch `claude/qa-full-pass`)

Run on a local Development host (`DOTNET_ENVIRONMENT=Development`, PostgreSQL `nosum_dev`) with the tester client
(`-p:NosumDevTools=true`, ADR-079). Browser: Playwright Chromium. Script: `tests/browser/full-pass.cjs`.

**Scope, honestly stated.** This pass covers sign-in as a guest, the HUD, movement (screenshot only), every top-right
panel and the Settings language switch, at 1280×720, 1920×1080 and 390×844 (touch), in Vietnamese and English. It does
**not** yet exercise the world features (farming, sprinklers, animals, fishing, combat, crafting actions, merchants and
shops, the market, NPC dialogue, quests beyond the tracker, mail send/receive, co-op and friends actions, the calendar and
season change, remembered sign-in, the minimap toggle). Those are listed under "Not covered" below and are the next step.

## How grading works

Section 7 of `docs/design/ui-standard.md` has ten items. The script checks two of them automatically per panel:

- **Item 2 (overlap)**: the open window's box against the HUD top-right, top-left and hotbar regions.
- **Item 7 (no inner scrollbar at 1280×720 and above)**: any inner box whose content is taller than its box.
- **Item 5 / off-screen**: the window box must stay inside the viewport.

Items 1, 3, 4, 6, 8, 9 and 10 need a person (or a visual review) and are only spot-checked in this pass from the screenshots
named below. The table reports what the script measured; a `pass` means "no automatic fault", not "the checklist is
complete".

Console errors and page errors are recorded per run; none were recorded in any run.

## Results

`FAIL: inner scroll a/b` means the menu body has `a` px of content in a `b` px box. `n/a` means the button does not open a window (Emotes shows a bubble over the farmer, verified in `vi/1280x720/16-Biểu-cảm-B.png`).

| Feature | vi 1280×720 | en 1280×720 | vi 1920×1080 | en 1920×1080 | vi 390×844 | en 390×844 |
|---|---|---|---|---|---|---|
| HUD | captured | captured | captured | captured | captured | captured |
| movement (WASD) | captured | captured | captured | captured | captured | captured |
| panel: Thư | pass | – | pass | – | pass | – |
| panel: Cài đặt | pass | – | pass | – | pass | – |
| panel: Túi đồ [I] | FAIL: inner scroll 422/416 | – | FAIL: inner scroll 696/686 | – | FAIL: inner scroll 402/398 | – |
| panel: Tủ đồ [C] | pass | – | pass | – | pass | – |
| panel: Chế tạo | FAIL: inner scroll 473/460 | – | FAIL: inner scroll 770/739 | – | pass | – |
| panel: Giao dịch [T] | pass | – | pass | – | pass | – |
| panel: Hợp tác xã [G] | pass | – | pass | – | pass | – |
| panel: Khách | pass | – | pass | – | pass | – |
| panel: Bạn bè | pass | – | pass | – | pass | – |
| panel: Kỹ năng | FAIL: inner scroll 2769/471 | – | FAIL: inner scroll 4298/755 | – | FAIL: inner scroll 1555/658 | – |
| panel: Thú cưng | pass | – | pass | – | pass | – |
| panel: Hồ sơ [N] | pass | – | pass | – | pass | – |
| panel: Bản đồ thế giới [Tab] | FAIL: inner scroll 869/471 | – | FAIL: inner scroll 1375/755 | – | pass | – |
| panel: Sổ sưu tập [J] | FAIL: inner scroll 1588/471 | – | FAIL: inner scroll 2643/755 | – | FAIL: inner scroll 1215/658 | – |
| panel: Biểu cảm [B] | n/a | – | n/a | – | n/a | – |
| console / page errors | pass | pass | pass | pass | pass | pass |
| panel: Mail | – | pass | – | pass | – | pass |
| panel: Settings | – | pass | – | pass | – | pass |
| panel: Inventory [I] | – | FAIL: inner scroll 422/416 | – | FAIL: inner scroll 696/686 | – | FAIL: inner scroll 402/398 |
| panel: Wardrobe [C] | – | pass | – | pass | – | pass |
| panel: Crafting | – | FAIL: inner scroll 473/460 | – | FAIL: inner scroll 770/739 | – | pass |
| panel: Trade [T] | – | pass | – | pass | – | pass |
| panel: Co-op [G] | – | pass | – | pass | – | pass |
| panel: Visitors | – | pass | – | pass | – | pass |
| panel: Friends | – | pass | – | pass | – | pass |
| panel: Skills | – | FAIL: inner scroll 2699/471 | – | FAIL: inner scroll 4152/755 | – | FAIL: inner scroll 1492/658 |
| panel: Pet | – | pass | – | pass | – | pass |
| panel: Profile [N] | – | pass | – | pass | – | pass |
| panel: World map [Tab] | – | FAIL: inner scroll 869/471 | – | FAIL: inner scroll 1375/755 | – | pass |
| panel: Journal [J] | – | FAIL: inner scroll 1588/471 | – | FAIL: inner scroll 2643/755 | – | FAIL: inner scroll 1215/658 |
| panel: Emotes [B] | – | n/a | – | n/a | – | n/a |

Screenshots: `docs/qa/full-pass/<locale>/<viewport>/NN-<panel>.png` (`vi` = Tiếng Việt, `en` = English). Every panel has one
file per cell of the table; `00-hud.png` is the HUD and `01-moved.png` the position after a 800 ms walk. Raw results:
`docs/qa/full-pass/audit-<locale>-<viewport>.json`.

Spot checks by eye (screenshots named):

| Checklist item | Result | Evidence |
|---|---|---|
| 6 — text not cut off | **FAIL** on the Skills page at 1280×720: the last description line is cut mid-line at the bottom of the box | `vi/1280x720/11-Kỹ-năng.png` |
| 2 — nothing overlaps | **FAIL** at 1280×720: the world label "→ Trạm xe buýt" sits under the top-right button column | `vi/1280x720/11-Kỹ-năng.png`, `vi/1280x720/16-Biểu-cảm-B.png` |
| 2 — nothing overlaps | **FAIL** at 1280×720: world label "→ Trạm xa" runs into the right screen edge | `vi/1280x720/00-hud.png` |
| 10 — EN fits | pass on the sampled screens (Crafting, Journal, HUD at 390 and 1920) | `en/390x844/06-Crafting.png`, `en/390x844/15-Journal-J.png`, `en/1920x1080/00-hud.png` |
| 4, 5, 8 — edges, panel sizes, icons | pass on the sampled screens; not measured | `en/390x844/06-Crafting.png` |

## Bugs (ranked by severity)

Severity: **High** blocks or hides a feature; **Medium** is a visible layout fault; **Low** is cosmetic or a test gap.

| # | Sev | Where | Bug |
|---|---|---|---|
| 1 | High | Skills (`Kỹ năng`), 1280×720 and up | Content is 2769 px tall in a 471 px inner scroll at 1280×720 (4298/755 at 1920×1080). The skill description text is cut mid-line at the bottom. |
| 2 | High | Journal (`Sổ sưu tập [J]`), 1280×720 and up | Inner scroll 1588/471 at 1280×720 and 2643/755 at 1920×1080; the discovery grid needs its own scrollbar on desktop. |
| 3 | Medium | World map `[Tab]` | Inner scroll 869/471 at 1280×720 and 1375/755 at 1920×1080. |
| 4 | Medium | Inventory `[I]` | Small inner scroll (422/416, 696/686, and 402/398 on phone) — a scrollbar appears where the content should fit. |
| 5 | Medium | Crafting | Small inner scroll (473/460 at 1280×720, 770/739 at 1920×1080). |
| 6 | Medium | Language selection | The browser locale (`en-US`) does not change the UI language; a new guest gets Vietnamese. The switch is only in Settings → General. |
| 7 | Medium | World labels, 1280×720 | "→ Trạm xe buýt" is drawn under the top-right button column (item 2, overlap). |
| 8 | Low | World labels, 1280×720 | "→ Trạm xa" is clipped at the right screen edge. |
| 9 | Low | Inventory, 390×844 | Overflows by 4 px (402/398). |
| 10 | Low | Test gap | Movement is screenshot-only; the client has no position read-back, so "moves" is not asserted. |
| 11 | Low | Test gap | Farming, animals, fishing, combat, merchants, market, NPC dialogue, mail, co-op, calendar, minimap toggle and remembered sign-in are not exercised yet (see below). |

## Not covered in this pass

- World play: till, sow, water, sprinkler, decay, harvest; animals; fishing; combat and skills gains; crafting actions.
- Shops, merchants and the market; NPC dialogue; quest completion; mail send/receive; co-op and friend actions.
- Calendar and season change (the tester `DevSeason` hook from ADR-079 can drive this; it is the next step).
- Sign-in with a remembered session and "continue"; the minimap toggle; touch controls (the phone run only opens panels).
- Running (energy), wardrobe and boots actions beyond the panel opening.

## Reproduce

```
DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host
dotnet run --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080
node tests/browser/full-pass.cjs vi,en
```
