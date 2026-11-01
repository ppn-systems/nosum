# Phone inventory header: tabs no longer cover Sort and the hotbar lock

Branch `claude/phone-inventory-header`. Follow-up to the open item in `docs/qa/phone-menu/README.md`.

## Cause

On a phone (≤ 640 px) the inventory's side column (`.inv-side`, the worn-item slots) is a wrapping row. Its paperdoll box is 154 px tall,
but the four worn-item slots are taller than the paperdoll. The row centred them (`align-items: center` on `.equip`), so the top slot
(hat) spilled about 33 px above its box and covered the Sort button and the hotbar lock row above it. The layout audit did not flag it: it
checks scroll, clipping and the window edge, not overlap between children of a single window.

## Fix (phone only, `wasm/Nosum.Wasm.Controls/Inventory/InventoryPanel.razor.css`, inside `max-width: 640px`)

- `.inv-tools` stacks: Sort, then the hotbar lock, with `--nosum-gap` between them.
- `.inv-side .equip` aligns its items to the top, so the worn-item column stays inside its row.

Desktop CSS is unchanged; the rules are in the phone media block only. Measured boxes at 1280×720 are identical before and after.

## Measured (`before-measure.json`, `after-measure.json`)

| Cell | Header before (Sort / lock) | Header after (Sort / lock) | Side column top before → after | Overlap before → after |
|---|---|---|---|---|
| vi 390×844 | 172–200 / 177–196 | 172–200 / 204–223 | 208 → 231 | none in the box check; the hat slot visibly covered Sort |
| en 390×844 | 172–200 / 177–196 | 172–200 / 204–223 | 208 → 231 | as above |
| vi 1280×720 | 174–216 / 184–206 | unchanged | 228 | none |
| en 1280×720 | 174–216 / 184–206 | unchanged | 228 | none |

The box check only compares the side column with the header's bounding boxes, and the overlap sat inside the side column's own spill. The
screenshots are the evidence for the before state.

No inner scroll in any cell (`scrolls: []`), and no page errors.

## Files

- `before-{vi,en}-{1280x720,390x844}.png`, `after-{vi,en}-{1280x720,390x844}.png`: the inventory window in each cell.
- `before-measure.json`, `after-measure.json`: element boxes and scroll check per cell.

Not captured: 1920×1080. The change is inside the phone media query, so the desktop layout is the same as before.

## Reproduce

```
DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host
dotnet run --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080
node tests/browser/inventory-header-capture.cjs docs/qa/phone-inventory after
```

Unit tests: `dotnet test tests/Nosum.UnitTests` → 3381 passed, 0 failed. Wasm build: 0 warnings, 0 errors.
