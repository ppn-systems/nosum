# Phone menu: every column panel has a door (branch `claude/phone-menu-doors`)

On a 390 px screen the HUD menu column collapses into its Menu button. `GameMenuWindow` used to list only the panels outside the
column, so Inventory, Crafting, Trade, Friends, Skills, World map and Journal had no door on a phone (the QA pass-1 open item). Per
`docs/design/ui-standard.md` section 6, the Menu window now gives a tile for every column panel while the column is collapsed. On a
desktop it keeps only the extras, as before.

## What changed

- `ToolbarViewModel.ColumnCollapsed`: when set, `MenuItems` includes the column panels. Merchants' panels and Mail and Settings stay out.
- `page-guard.js` reads the same media query as `GameHud.razor.css` (`(max-width: 640px), (pointer: coarse)`) at start-up and on each
  crossing, and `ClientWiring` feeds it to the view model. A window that is resized across the width updates the menu live.
- Phone menu: 10 tiles in the existing tile grid (`GameMenuTiles`), which fits 390×844 without scroll.
- The hotbar by tap: a tap selects its slot (checked in the script). The inventory opens from the Menu window's Inventory tile.
- `tests/browser/full-pass.cjs`: phone cells open every panel through the Menu. The n/a path is gone; a missing tile is a FAIL.

## Results

| Cell | Checks | FAIL before | FAIL after |
|---|---|---|---|
| vi 1280×720 | 14 | 0 | 0 |
| en 1280×720 | 15 | 0 | 0 |
| vi 390×844 | 16 | 7 n/a (column panels "not visible") | 0 |
| en 390×844 | 17 | 7 n/a (column panels "not visible") | 0 |

Console and page errors: none in any cell. Layout audit: every window passes (no scroll, clipping, overlap or off-screen box).

The Menu window on 390×844 (vi, `vi/390x844/12-Menu.png`): 10 tiles, 329 px tall, no scroll.

## Open items

- **Inventory on a phone, top-left tabs.** Resolved in `docs/qa/phone-inventory/`: the worn-item column spilled up over the Sort button and
  the hotbar lock on 390×844. The header now stacks and the column starts at its row's top edge (phone only).

## Reproduce

```
DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host
dotnet run --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080
NOSUM_QA_OUT=docs/qa/phone-menu node tests/browser/full-pass.cjs vi,en 1280x720,390x844
```

Unit tests: `dotnet test tests/Nosum.UnitTests` → 3377 passed, 0 failed.
