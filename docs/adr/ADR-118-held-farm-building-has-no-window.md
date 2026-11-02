# ADR-118: A held farm building has no arrange window; its key strip docks in the bottom centre

## Status

Accepted

Amends [ADR-116](ADR-116-window-layout-rules-from-world-pass-10.md) D4 (the arrange panel is a centred window) for one case, a farm
building in hand, and the placement flow of [ADR-015](ADR-015-private-instances-and-placeables.md) D7–D10 (the arrange key strip). No wire change: `NosumProtocol.Version`
(34) and every opcode stay as they are. The owner's centred window for furniture and for rooms is unchanged.

## Context

Placement round 5 ([docs/design/critique-placement.md](../design/critique-placement.md), finding 18) left the building confirmation without a
browser frame, because a carpenter order was needed to reach it. Round 6 played it with the Development grant (`carpenter-build-capture.cjs`)
and found the flow was unreadable:

- A held farm building opened the arrange window. A building is never in the furniture catalogue, so the window said "You have no furniture
  yet. The furnisher sells it." under a title and buttons that do nothing for it.
- The window is centred (ADR-116 D4) and covers the farm tiles around the farmer. The building ghost is drawn there, so the green or red
  preview is hidden exactly when the player must read it. The confirmation ("Press E again to build here") is the only word about the build,
  and it is pressed blind.

Frames: `docs/qa/placement-round6/before/build-desktop-en/01-ghost-on-farm.png` (the window over the ghost) and
`02-first-press-confirm.png` (the empty list and the confirmation).

## Decision

- **D1 - A farm building in hand has no window.** `PlacementActions` reports `SetWindowed(false)` when the held item is a building and the
  map is a farm (not a room, which always shows the window) and no piece is carried. `PlacementViewModel.ShowsWindow` is then false while
  `IsVisible` stays true, and `FurnitureCatalogViewModel.IsVisible` follows `ShowsWindow`.
- **D2 - Its key strip docks in the bottom centre.** The same `PlacementPanel` (the E, R, F and Q caps and the notice line) renders above the
  toast line, in the bottom centre, while no window is open. The hotbar stays drawn, the quest card stays up, and the first-run hint steps aside
  for the strip. The caps are the window's own buttons, so a phone player taps Place there (the touch path, ui-standard section 6).
- **D3 - Nothing else changes.** A held chest, furnace, sprinkler or furniture keeps the window, because each is listed in the catalogue. Rooms
  keep the centred window as ADR-116 D4 decided. The ghost of those pieces can still sit under the window on the farm; that is recorded as an
  owner question in `docs/design/critique-placement.md`, not decided here.
- **D4 - Dead CSS removed.** The rules that docked the furniture panel in the top-left corner (ADR-116 moved it to the centre) can never match,
  because the panel renders in the centre region. They are removed; the rendered layout is unchanged.

## Consequences

- A building press now shows the ghost, its colour and its reason before E, and the confirmation sits on the strip where the touch caps are.
- The bottom centre gains one row (the strip, about 42 px at phone size) above the toast line while a building is held. The room and the
  hotbar stay open.
- Owner decision still open: whether a centred window may cover the ghost on the farm and in rooms (the trade-off ADR-116 D4 accepted).
