# Shop and merchant UX audit (claude/shop-ux-audit)

Before and after screens of every merchant screen the Development QA hook reaches (ADR-079), at 1280×720, 1920×1080 and 390×844.

- `before/` — `origin/master` at `5611626`, built with `-p:NosumDevTools=true`.
- `after/` — this branch, built the same way.
- Each folder has `README.txt` (the file list, the merchant and screen of each frame, skipped steps, page errors).
- Captured in English (`en-US`). Vietnamese was not captured (see the checklist, item 10).

## What changed

| Area | Before | After |
| :-- | :-- | :-- |
| Shop quantity | `-` / `+` and a typed box | Presets **x1 / x5 / All** (tab bar, ui-standard §8); "All" is the most the balance and the stack allow. The `-` / `+` and box stay. |
| Shop detail | None: the row was the only information | A detail strip under the list for the selected row: description, effects (walking speed, running energy saving), the total, and the coins left. Selecting a row is a click on its item. |
| Disabled Buy | Greyed, no reason | The strip says why: "Not enough coins: N more needed." or "Your bag is full." |
| Unaffordable rows | Same look as affordable | Dimmed (the item and price at 50 %); Buy keeps its disabled look. |
| Market sell | Typed quantity, no presets, no confirm | Presets x1 / x5 / All; a listing of **10 or more** waits for a Yes/No confirm. |
| Backpack page | Upgrade button hidden at the largest size; no reason | Button always shown; off with a reason: the largest size, or the coins short. Nothing about size is shown until the page has loaded (a pre-existing flash of "already the largest" is fixed). |
| Panel size | Backpack S; Shop, Market, Wardrobe L; Goals, Co-op M | Backpack M (a form with a price and a button). The others are unchanged. |
| Shop list | 5 offers a page | 4 offers a page, so the detail strip fits at 1280×720. The one-line hint "Pick what to buy" is removed (the strip and the price column say it). |

Server: no wire change, so no protocol bump and no DbBudget row. The server already validates every quantity (`ShopRules.Price`: 1 to the stack, and the wire range; `MarketService`: not more than held). An integration case now covers quantity 100 (above the cap) alongside 0, -5 and a stack overflow.

Rules added in `Nosum.Gameplay`: `ShopRules.MaxAffordable` (unit-tested). The effect lines come from `ItemTooltip.Effects`, shared with the tooltip, so the strip and the tooltip read the same data.

## Merchants and screens captured

Per viewport (25 frames each, identical lists in `before/` and `after/`):

- **Seed keeper**: dialog, buy, buy item detail.
- **Tailor**: dialog, buy, buy item detail, wardrobe, backpack.
- **Furnisher**: dialog, buy, buy item detail.
- **Blacksmith**: dialog, buy, buy item detail, upgrade.
- **Carpenter**: dialog, buy, buy item detail, build.
- **Trader**: dialog, market buy, market sell (a stack picked, so the presets show).
- **Board keeper**: dialog, goals, co-op.

Not captured (listed in `README.txt`): the carpenter's Buildings and Pets tabs, the backpack upgrade click, the market confirm for a large listing, and the disabled-Buy and dimmed states (the capture account had 50,500 coins, so nothing was short). These states are covered by unit tests (`ShopViewModelTests`), not by screenshots.

## 10-point self-check (ui-standard §7)

Checked on the frames listed above. "Sampled" means the frames I opened, not every frame.

1. **Regions.** Yes. Everything is inside the centre panel; no floating card over the world. Sampled.
2. **No overlap.** Yes in the sampled frames at 1280×720 and 390×844. The seed shop at 1280×720 overflowed (the strip was cut off) before the fix; it fits now.
3. **Menu column.** Unchanged in this branch.
4. **Edges.** Unchanged; the panels keep the shared frame.
5. **Panels.** Yes: `GamePanel`, one of S / M / L. The backpack moved S → M.
6. **Font sizes and wrapping.** Yes: the three sizes only. A few descriptions wrap at word boundaries (not word by word); the text is not clipped.
7. **No inner scrollbar at 1280×720.** Yes for the frames checked (the seed shop after the two fixes; the carpenter and market sell fit). At 1920×1080 the frames were not scrolled.
8. **Icons whole and centred.** Yes in the sampled frames.
9. **Spacing.** Yes: no new raw sizes; the only CSS added is a text no-wrap rule and an opacity for dimmed rows.
10. **Vietnamese and English fit.** **Not verified.** The keys are in both languages (the `.resx` parity test passes), but only English was captured. The Vietnamese strings are longer ("Thiếu … xu để mua.", "Túi đồ đã đầy."), so this needs a Vietnamese pass before merge.

## How it was produced

- Server: `dotnet build server/Nosum.Host` (Development), with the test role `nosum_base` (a local sandbox database).
- Client: `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, with the server URL set in the published `appsettings.json` only.
- Capture: `tests/`-independent script in the agent's scratchpad (`capture-merchants.cjs`), Playwright Chromium, guest sign-in, `window.nosumQa` teleport and grants (50,000 coins, parsnips, copper bars).
