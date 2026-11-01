# ADR-080: The wide use reports the tiles it worked, and wet soil says it is wet

## Status

Accepted

## Context

A hoe or watering can of a higher tier works a line of one, three or five tiles with one swing (docs/adr/ADR-026 D1). The server charges the
energy of each tile that works, and a tile that does not work is skipped (`FarmService.Area.cs`). The reply, however, carried one status and one
action for the whole line, so the client could not tell how much of it was done. The client also predicted no energy for a wide use
(`_queuedCost = tiles == 1 ? cost : 0`), so a five-tile swing spent energy the player never saw leave.

Watering a tile that is wet already (from the can earlier that day, or by rain or a sprinkler, docs/adr/ADR-027 D2) was refused as
`NothingToDo`, whose text is "That does nothing here." The reason was the soil, not the item, and the player was told the wrong thing.

Both changes touch the wire, so the protocol version moves (`NosumProtocol.Version`, now 22) and the clients ship with the server. Neither changes
what a tile becomes, what a tile costs or any balance number.

## Decision

- **D1 - The reply counts the tiles that worked.** `UseItemResult` gains `Worked` (a byte, appended as `[SerializeOrder(2)]`). It is the number of
  tiles whose use returned `FarmStatus.Ok`, excluding a ladder (`FarmAction.Descended`), which is used but charges nothing. A single-tile use that
  worked reports one; a refusal reports zero. `FarmUseOutcome` carries the same count (`IFarmService.cs`).
- **D2 - Energy follows the count.** The client predicts the energy of one tile for every farm use, and when the server answers with a worked
  count, the floating energy label shows the cost of one tile times that count. The server charged exactly that many tiles, so the label is the
  energy spent. A refused line shows no label, as before.
- **D3 - Wet soil has its own refusal.** Watering soil that is wet already is refused as `FarmVerdict.AlreadyWet` and `FarmStatus.AlreadyWet` (both
  appended, value 12 and 24). The outcome is unchanged: the can keeps its water, the player keeps the energy, and nothing is written. The text says
  so ("The soil is already wet today." / "Đất đã đủ nước hôm nay."). `NothingToDo` still answers a use that does nothing for any other reason.
- **D4 - Wire.** Protocol version 22. The change is append-only: no opcode, no existing `[SerializeOrder]` value and no existing enum value moved.

## Consequences

- A five-tile line of an upgraded can or hoe now shows the energy it cost, and a line that worked two tiles of five says so in the number.
- The server runs no extra query: `Worked` is counted from results the loop already has, so `DbBudget` rows do not change.
- Clients built for 21 misread a `UseItemResult` with the new byte and a `FarmStatus` value 24, so the server and the clients ship together.
- No balance number, price, cost or growth rule changed.
- Not done here: the reticle still shows only the tile in front, not the line, and a player still cannot tell a stage of growth from its sprite.
  Those are the next candidates (see `docs/design/critique-farming.md`).

## References

- docs/adr/ADR-021 D7 (growth by watered days), ADR-023 D1 (energy charged with the effect), ADR-026 D1 (wide use), ADR-027 D2 (rain counts as
  watering), ADR-074 D2 (one use per interval).
