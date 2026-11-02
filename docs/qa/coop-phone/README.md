# QA: co-op window on a phone (branch `claude/coop-phone`)

The co-op window (`wasm/Nosum.Wasm.Controls/Coop/CoopPanel.razor`) at 390×844 cut its last upgrade row: the upgrade section and the
warehouse, members, objectives and log all stacked in one column, so the window scrolled and the bottom row sat under the edge.

## Change

- **Phone (640 px and under): a tab bar of five tabs** (`Coop_Tab_*`: Members, Stock, Upgrades, Goals, Log), a `tab` button each
  (ui-standard §8). One part shows at a time; every part fits the window, so no row is cut and the page body does not scroll.
- **Desktop (above 640 px): unchanged.** The tab bar is hidden and every part shows, as before. Each part is a wrapper with
  `display: contents`, so the desktop layout is the same box tree as before.
- Labels are short on purpose (one row of five tabs on 390 px); the full section headings still title each part.
- Resources: `Coop_Tab_Members`, `Coop_Tab_Warehouse`, `Coop_Tab_Upgrades`, `Coop_Tab_Objectives`, `Coop_Tab_Log` in `Language.resx`
  (Vietnamese) and `Language.en.resx` (English).

## Captures

Harness: `tests/browser/world-pass8.cjs coop <locales> <viewports>` (Development server, client built with `-p:NosumDevTools=true`,
guest accounts). `coop/<lang>/<size>/` holds eight frames and a `report.json`. On a phone each tab step clicks its tab first and records
`overflow` (the page body's `scrollHeight − clientHeight`, 0 = nothing is cut).

- `coop/vi/390x844/`, `coop/en/390x844/`: after (every step passes, overflow 0 on all six co-op parts).
- `coop/vi/1280x720/`, `coop/en/1280x720/`: after, desktop (all parts shown).
- `before/coop/vi/390x844/`: before the change (the cut upgrade row, see `03-coop-deposit-seeds.png`).

## Not changed

- At 1280×720 the co-op window scrolls (`overflow` about 900 px on the desktop frames, both before and after this change): every part is
  shown at once and the warehouse grid is cut at the bottom. ui-standard §7 item 7 says no inner scroll at 1280×720 for normal content.
  This is a desktop layout question, outside the phone change; it needs its own decision (for example the same tab bar on desktop).
