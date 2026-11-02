# ADR-120: Every wardrobe look change goes out one at a time, in the order chosen, and shows at once

## Status

Accepted

Amends [ADR-109](ADR-109-wardrobe-appearance-requests-in-order.md), consequence "Equip and unequip still send at once", which no longer holds. No wire
change: `SetAppearance`, `EquipItem` and `UnequipItem` keep their fields and opcodes, and `NosumProtocol.Version` (34) does not move. The server is
unchanged; it still decides every change under its per-player lock. Builds on [ADR-006](ADR-006-layered-character-customization.md) and
[ADR-102](ADR-102-guest-progress-and-sign-out-question.md).

## Context

ADR-109 serialized the appearance requests on the client and left equip and unequip to send at once, because they set only the worn slots and so
could not revert a skin or hair choice. That part is right. Each server handler takes a lock per player, but the client never guaranteed the order in which two
requests sent together are applied. A player who wears a shirt and at once takes it off can therefore have the take-off applied first, find
nothing worn (`LookStatus.NothingWorn`), and then have the wear land last, so the shirt stays on while the last click said "take off". The same race exists between an equip and a skin change. The equip also showed nothing until the server's look came back, so the
preview lagged the click, the same lag ADR-109 removed for appearance.

Found while closing the open items of [docs/design/critique-account-settings.md](../design/critique-account-settings.md) (section 8, the wardrobe's
equip and unequip). **The race was not seen live.** The master build passed the same-tick take-off then wear in all six configurations
(`docs/qa/account-round5/before/report.json`), so the server applied those two in the order sent in practice. This change removes an ordering
guarantee the client did not have; it is a risk closed, not a bug reproduced. What was seen on master is the lag: the wear shows on the
wardrobe at once in 2 of 6 configurations; in the rest it is absent at once and present 150 ms later (`atOnce` in the report).

## Decision

1. **One queue of look changes, in order.** `LookChannel` keeps every change the player chose (`PendingAppearance`, `PendingWear`,
   `PendingTakeOff`) in the order chosen. The oldest is in flight; the next is sent when its answer arrives. A change never overtakes an earlier
   one, whatever its kind.
2. **Appearance choices collapse.** An appearance choice that waits behind another appearance choice replaces it: the request carries all three
   parts, so only the newest one is worth sending. The one in flight is never replaced. (ADR-109 D1, extended to the queue.)
3. **The wardrobe shows the queue.** `WardrobeViewModel.Look` is the server's look with each pending change applied in order
   (`PendingLook.Apply`), so the preview, the worn slots and the swatches change at once. A worn item whose wearing is queued leaves the bag at
   once. Take-off does nothing when the look already shows the slot empty, so a second click does not send a request the server would refuse.
4. **The server decides.** A refused change drops out of the queue when its answer arrives, and the status line names the reason. The server's
   push (`LookChanged`) replaces the projection when it arrives. A lost connection drops the changes not yet sent.

## Consequences

- No wire change, no `NosumProtocol.Version` bump, no server change. The server's per-player lock is still the authority; the client only stops
  sending two changes at once.
- The requests of one player are serial. A player who changes the look many times in a second has them answered one after another, which is the
  pace of the server's answers (as before for appearance).
- Looks are a projection of the queue over the server's look while requests wait; a refused change in the middle of the queue shows its successors
  on top of the server's look until they are answered too. They are then the server's, and the push corrects any guess.
- Tests: `tests/Nosum.UnitTests/Wasm/WardrobeAppearanceTests.cs` (take-off waits for its wear; an appearance choice after a wear is sent after
  it and newer choices replace it; each change applies to the look in turn).
- Played live in all six configurations (`docs/qa/account-round5/after/`): a guest claims a mail gift with a shirt and a hat in the game, wears
  one at once (the wear shows on the Clothes tab before the answer), takes it off and wears the other in one tick, then chooses a skin, wears
  the first again and chooses another skin in one tick. The server ends with the last skin and both items worn; no page errors. The starter kit
  has no clothing, which is why the gift is seeded into the database and then claimed through the game.
- Not played: a refused equip (for example a worn slot that changed under the client) and the bag's count after a refused change.

## References

- `wasm/Nosum.Wasm.Socket/Look/LookChannel.cs` (`Pending`, `SetAppearance`, `Equip`, `Unequip`, `Enqueue`, `SendQueueAsync`).
- `wasm/Nosum.Wasm.Socket/Look/PendingLook.cs` (the three change kinds and `Apply`).
- `wasm/Nosum.Wasm.ViewModels/Looks/WardrobeViewModel.cs` (`Look`, `Wardrobe`, `Wear`, `TakeOff`).
- `server/Nosum.Infrastructure/Looks/LookService.cs` (the per-player lock, unchanged).
- Browser check: `tests/browser/account-round5/capture.js`; frames in `docs/qa/account-round5/`.
