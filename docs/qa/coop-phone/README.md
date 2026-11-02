# QA: co-op window on a phone (branch `claude/coop-phone`)

The co-op window (`wasm/Nosum.Wasm.Controls/Coop/CoopPanel.razor`) at 390×844 cut its last upgrade row: the upgrade section and the
warehouse, members, objectives and log all stacked in one column, so the window scrolled and the bottom row sat under the edge.

## Change

- **Phone (640 px and under): a tab bar of five tabs** (`Coop_Tab_*`: Members, Stock, Upgrades, Goals, Log), a `tab` button each
  (ui-standard §8). One part shows at a time; every part fits the window, so no row is cut and the page body does not scroll.
- **Desktop (above 640 px):** superseded by `docs/qa/coop-desktop/`: the tab bar now shows at every size.
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

- Desktop: superseded. At 1280×720 the co-op window scrolled about 900 px because every part showed at once. The desktop now uses the
  same tab bar and fits every tab; see `docs/qa/coop-desktop/README.md`.
