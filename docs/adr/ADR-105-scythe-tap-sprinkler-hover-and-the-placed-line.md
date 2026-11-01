# ADR-105: A scythe tap reaches the server and names the days left; the hover stage counts a sprinkler; a placed piece says so

## Status

Accepted

## Context

The farming round four pass (`docs/design/critique-farming.md`, round-4 addendum; captures and grades in `docs/qa/farming-round4/`) took three
items from the backlog. Each is read from the browser, not from the code alone.

- **The scythe tap on an unripe crop was silent, and so was every scythe tap.** The E key names "Not ready, N days left" (ADR-094 D2). A mouse tap
  reaches the farm through `PlayerActions.DragSow`, which returns before any use unless `IsFarmTool` admits the held item. `IsFarmTool` admitted seeds,
  a hoe and a watering can, not the scythe, so a scythe tap never sent a request. Round three read the silence as a server or reply fault; the cause
  is the client gate. Master (`origin/master` d4fba266) shows no line at 1280x720 and 390x844, in English and Vietnamese.
- **The sprinkler's growth stage in the hover ignored the sprinkler.** The hover names the stage the mouse is over (`PlayerActions.TryGetCropStage`,
  backlog item 4). It called `FarmRules.StageAt` without the sprinkler time, so a crop a sprinkler waters showed one stage behind the server's growth
  (the round-three note). `FarmRules.StageAt` and `DaysUntilMature` already take the time; the client already holds the placed pieces.
- **Placing a piece was silent.** A successful placement, including a sprinkler, gave no line. A refusal did (`Place_Status_*`). ADR-100 D3 made a
  successful farm use say what it did; a placed piece was left out.

The sprinkler watering and the shipping bin were replayed end to end in the browser (see the QA README). Both work on master: the day after a
sprinkler is placed, a can tap on the crop beside it says the soil is already wet, and E facing the bin sells the stack ("Sold for 80 coins.").
Drag-to-sow over a line of three sows the three tiles ("Seeds planted. ×3"). Nothing in those three needed a fix.

## Decision

- **D1 - The scythe's tap is a use.** `ToolRules.PointerWorksTile(kind, tool)` is the set of items a press works a tile with: seeds, a hoe, a watering
  can and a scythe. `PlayerActions.IsFarmTool` calls it, so the scythe's tap reaches the server (`Use`, one tile) and the server's reply names an
  unripe crop's days left through the existing `Farm_Status_NotMatureDays` line (ADR-094 D2). The scythe works the tile of a press once: while the
  pointer is held, a drag over the row does not repeat the use, so a scythe dragged over bare ground does not refuse every tile. The reticle for the
  scythe is red unless the tile holds a withered crop (`TargetWorks`; the scythe cuts those, ADR-018) or a ripe one (already the harvest check).
- **D2 - The hover stage counts a sprinkler beside the tile.** `PlayerActions` takes the placeable channel (an optional last constructor argument,
  wired from `GameLoop.StartAsync`) and passes `SprinklerWateredSinceMs(tileX, tileY)` to `FarmRules.StageAt`, the same rule the server uses
  (`SprinklerRules.WateredSinceMs`). The hover and the server's "days left" now agree on the day the sprinkler waters from.
- **D3 - A placed piece says so.** `GameMessagesViewModel.OnPlaceCompleted` shows `Place_Done` ("Placed." / "Đã đặt xuống.") for `PlaceStatus.Ok`. A
  refusal keeps its own `Place_Status_*` line. The line is generic: the placement event carries the status, not the item, and a sprinkler-specific
  line would need the item in that event.
- **D4 - No other change.** The sprinkler's watering, the bin's sale and the drag-to-sow line of three were checked in the browser and work; no code
  changed for them. Whether the drag is taught on the first-run hint is copy and quest text, an owner decision (`critique-farming.md`, round four).
- **D5 - Wire.** No change. Protocol version stays 32. No opcode, no packet and no enum value changed. Clients built for 32 keep working; the scythe
  tap is a use the server already accepted (`UseItem`).

## Consequences

- A scythe tap on a crop now harvests a ripe one and names an unripe one's days left, from a mouse as from E. Before, a tap with the scythe did nothing
  at all.
- The hover stage on a sprinkler-watered crop matches the server's; the stage no longer lags a day behind the sprinkler.
- A successful placement adds one message-line entry. The message line can hold the placement, a hover stage and a hint at once; the layout question
  stays open (round-three finding 2).
- The client tests cover the rules (`ToolRulesTests.PointerWorksTile_*`, `FarmStageSprinklerTests`, `PlaceDoneKeyTests`); the tap, the drag and the
  hover are browser checks (`tests/browser/farm-round4-capture.cjs`, and the probe in the QA README).

## References

- docs/adr/ADR-094 (the E-key harvest line and the days left), ADR-100 D3 (a successful farm use says so), ADR-018 (the scythe cuts a withered crop),
  ADR-021 D7 (growth by watered days), ADR-071 D3 (drag to sow), ADR-093 D5 (the first-run hint), `docs/design/critique-farming.md`.
