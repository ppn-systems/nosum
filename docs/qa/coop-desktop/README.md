# QA: co-op window on desktop (branch `claude/coop-desktop`)

Follows `docs/qa/coop-phone/`. On desktop the co-op window showed every part at once and scrolled about 900 px at 1280×720
(ui-standard §7 item 7 says no inner scroll). Coordinator decision: the phone's tab bar is used on desktop too, one part per tab.

## Change

- **Tab bar at every size.** `wasm/Nosum.Wasm.Controls/Coop/CoopPanel.razor`: the five tabs (Members, Stock, Upgrades, Goals, Log) are
  `tab` buttons (ui-standard §8) above one part. The tab bar and the part rule are no longer phone-only (`CoopPanel.razor.css`).
- **Panel size stays L (220 u).** The limit is the height, not the width: the window cap is the viewport height minus the gutter and the
  HUD top row (`TradeDialog.razor.css`), so XL would not add a line. L is what the co-op already used.
- **Last tab remembered for the page session.** `CoopViewModel.Part` (a singleton, `wasm/Nosum.Wasm.ViewModels/Coop/CoopPart.cs`) keeps the
  tab; a reopened window shows it again. A page reload starts on Members.
- **Upgrades fit.** Each track's name, level and "Buy next level" share one line (the button wraps on a phone). The summary is one
  line; the next level's prices run on one line, each price kept whole. Before, every track took seven lines.
- **Warehouse hint.** The deposit hint is shorter (one line in both languages, `Coop_DepositHint`). On desktop it is the Deposit button's
  tooltip (`Title`) instead of a visible line, because the 9-column bag grid is the tallest part and the line pushed the Stock tab past
  the window at 1920×1080 by 21 to 51 px. On a phone the line stays visible.
- **Warehouse log pages ten lines.** The server keeps the latest 20 lines (`CoopRules.RecentLogLines`). Twenty lines are about 125 px
  taller than the window at 1280×720, so the Log tab uses the shared `Pager` (Previous / Next, ten lines a page). The counts in Members
  still cover all 20.
- **Objectives spacing.** `.objective-row` gap was `4px`, not a `--u` multiple (ui-standard §7 item 9); now `calc(var(--u) * 2)`.
- Resources: `Coop_DepositHint` shortened in `Language.resx` and `Language.en.resx`. No key added.
- `docs/design/ui-standard.md` §3 gets the rule for a window with several parts.

## Measurements

Scroll overflow of the co-op's page body (`scrollHeight − clientHeight` px, from `world-pass8.cjs coop`). 0 means no inner scroll
(`clientHeight == scrollHeight`).

| Tab (step) | 1280×720 vi before | 1280×720 vi after | 1280×720 en before | 1280×720 en after | 1920×1080 vi before | 1920×1080 vi after | 1920×1080 en before | 1920×1080 en after | 390×844 before | 390×844 after |
|:--|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| Members | 886 | 0 | 908 | 0 | 1433 | 0 | 1466 | 0 | 0 | 0 |
| Stock (deposit) | 946 | 0 | 968 | 0 | 1533 | 0 | 1566 | 0 | 0 | 0 |
| Stock (withdraw) | 942 | 0 | 964 | 0 | 1519 | 0 | 1552 | 0 | 0 | 0 |
| Upgrades | 886 | 0 | 908 | 0 | 1433 | 0 | 1466 | 0 | 0 | 0 |
| Goals (objectives) | 886 | 0 | 908 | 0 | 1433 | 0 | 1466 | 0 | 0 | 0 |
| Log (two lines) | 942 | 0 | 964 | 0 | 1519 | 0 | 1552 | 0 | 0 | 0 |
| Log, page 1 (ten lines) | n/a | 0 | n/a | 0 | n/a | 0 | n/a | 0 | n/a | 0 |
| Log, page 2 (ten lines) | n/a | 0 | n/a | 0 | n/a | 0 | n/a | 0 | n/a | 0 |
| Log, reopened (remembered tab) | n/a | 0 | n/a | 0 | n/a | 0 | n/a | 0 | n/a | 0 |

"before" = `origin/master` at `f14d913` (#345), every part shown at once on desktop (the baseline's own `world-pass8.cjs` plus a 1920
viewport; `before/`). "after" = this branch, 60 checks (10 steps × 2 languages × 3 sizes, `harness-after.log`). Every step passed,
no page errors. The "before" harness has no paged log and no reopen step, so those rows are n/a there. The full 20-line log was measured on this
branch before paging was added: 125 px over at 1280×720 (en), a one-off check. With paging it is 0 (table above).

Window body heights at the first step (members, `clientHeight`): 212 px at 1280×720, 346 px at 1920×1080, 174 px at 390×844.
Phone numbers are unchanged from #345.

## Grades (ui-standard §7 checklist, co-op window, L panel)

Grades: **Pass** (checked in the captures and the measurements), **Pass (note)** (passes, with a point to know), **n/a** (the window does not touch the item).

| # | Item | 1280×720 vi/en | 1920×1080 vi/en | 390×844 vi/en | Note |
|:--|:--|:--|:--|:--|:--|
| 1 | Region (center modal) | Pass | Pass | Pass | Center window; nothing new floats over the world. |
| 2 | Nothing overlaps | Pass | Pass | Pass | Captures checked for every tab. |
| 3 | Menu column (≤8 buttons) | n/a | n/a | n/a | Not touched. |
| 4 | Edges line up | Pass | Pass | Pass | Tab bar centred; section rules span the inner width. |
| 5 | `GamePanel`, S/M/L/XL | Pass | Pass | Pass | Size L, shared title bar and close. |
| 6 | Three font sizes, no word-by-word wrap | Pass (note) | Pass | Pass (note) | A price line breaks between prices (each price stays whole), a long summary wraps by words in English at 1280×720 ("…for tilling and / building."). |
| 7 | No inner scroll at 1280×720 and above | Pass | Pass | Pass | 0 on every tab and log page (table above). |
| 8 | Item icons whole and centred | Pass | Pass | Pass | Stock slots at all three sizes. |
| 9 | `--u` spacing and gap tokens | Pass | Pass | Pass | One fix: the objective row's `4px` gap (now 2 u). |
| 10 | Vietnamese and English both fit | Pass | Pass | Pass | Both locales measured; vi prices and summaries are the longer lines and fit. |

Behaviour checks: the tab bar switches the part (all six runs); the last tab opened is shown again on reopen (`coop-reopen-remembers`,
all six runs); the Log pager moves from page 1 to page 2 (`Page 2 of 2` / `Trang 2/2`).

**Overall: Pass.** No item fails. The notes are the wrap points in item 6 and the two decisions below.

## Decisions to review

1. The desktop deposit hint is a tooltip, not a line (see Change). The text is still in the game; a player reads it on hover. Hover does
   not exist on a touch screen, so the phone keeps the line.
2. The log shows ten lines a page with a pager, not all twenty. The alternative was a scroll inside the part, which §7 forbids.
3. The Buy button moved from a full-width row to the track's title line.

## Captures

- `coop/<lang>/<size>/NN-<step>.png`: one frame per step. `<lang>` is `vi` or `en`; `<size>` is `1280x720`, `1920x1080` or `390x844`.
  Frames: 01 board open, 02 found a co-op, 03 members, 04 upgrades, 05 objectives (goals), 06 deposit, 07 withdraw, 08 log page 1
  (`08-coop-log-full.png` shows page 2 after the Next click), 09 log, 10 reopened (Log tab remembered).
  Each run has a `report.json` with the step values.
- `before/coop/<lang>/<size>/`: the same flow on `origin/master` (the before state). Its tabs are hidden on desktop; the frames show all
  parts at once.
- `harness-after.log`, `harness-before.log`: the console output of both runs.

## Not verified here

- Members with eight or more people, and a warehouse with a full bag of different items at 1920×1080 beyond the deposit frame. The
  members list grows one row a person; the window scrolls only on those counts (not measured).
- Browsers other than Chromium (Playwright's Chromium only).
- The server here is the Development server on PostgreSQL (`nosum_dev`) with the QA hooks (`-p:NosumDevTools=true`), as in #345.

## Reproduce

```
dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true -o <scratch>/wasm-after
(cd <scratch>/wasm-after/wwwroot && python3 -m http.server 5080)
DOTNET_ENVIRONMENT=Development ConnectionStrings__Default="Host=localhost;Port=5432;Database=nosum_dev;Username=postgres" \
  dotnet <built Nosum.Host.dll>
NOSUM_QA_URL="http://localhost:5080/?qa=1" NOSUM_QA_OUT=docs/qa/coop-desktop \
  node tests/browser/world-pass8.cjs coop vi,en 1280x720,1920x1080,390x844
```

The before run is the same harness from `origin/master`, with a 1920×1080 viewport added to its `VIEWPORTS`, on a client built from
`origin/master`.
