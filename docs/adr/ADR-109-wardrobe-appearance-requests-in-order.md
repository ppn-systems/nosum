# ADR-109: Wardrobe appearance requests go one at a time, and the wardrobe shows the chosen look at once

## Status

Accepted. Amended by [ADR-120](ADR-120-wardrobe-look-changes-in-order.md): equip and unequip are now queued in order with the appearance requests.

No wire change: `SetAppearance` keeps its fields and opcode, and `NosumProtocol.Version` does not move. The server is unchanged. This
is client-side sequencing of the requests the wardrobe already sends. Builds on [ADR-006](ADR-006-layered-character-customization.md)
(the look is layered ids) and [ADR-102](ADR-102-guest-progress-and-sign-out-question.md) (hair-style names); the server still decides.

## Context

The wardrobe's three choices (skin, hair style, hair dye) each send the whole appearance, because `SetAppearance` carries all three
ids. Before this change, every choice built that triple from the last look the server had sent. Two choices made before the first
answer arrived therefore sent the first choice's old part again: choose a skin, then a hair style within a few milliseconds, and the
second request reverts the skin. The server applies each request as it arrives, so the outcome depended on the order of the answers.

The same window made the wardrobe feel late: a chosen swatch or style did not change on screen until the server answered.

Found while self-critiquing the account and settings round ([docs/design/critique-account-settings.md](../design/critique-account-settings.md),
section 3); it was not seen in the captures because a local round trip is faster than two clicks.

## Decision

1. **One appearance request in flight.** `LookChannel.SetAppearance` keeps the newest wish. A request is sent when the previous answer
   arrives, and it carries the newest wish at that moment. Wishes made while a request is in flight collapse into the newest one, which
   is sent when that answer arrives; a burst of clicks is one request in flight and at most one more after it.
2. **The pending choice is visible.** `LookChannel.TryGetPendingAppearance` returns the wish until the last answer. `WardrobeViewModel.Look`
   shows the server's look with the pending appearance laid over it, so the preview and the selected swatch and style change at once.
   Setters are unchanged: they build from `Look`, which already includes the pending choice.
3. **The server decides.** A refused answer ends the pending choice and the look the server sent shows again; the status line names the
   reason (as before). A lost connection drops the unsent wishes and shows the server's look.

## Consequences

- Two quick choices keep both: the skin stays when the hair style is chosen next (`WardrobeAppearanceTests`, the fake socket's held answers).
- A choice shows at once, so the wardrobe no longer lags a round trip behind the click.
- The requests of one player are serial. A player who changes the look many times in a second sends them one after another, each answered
  before the next; this is already the pace of the server's answers.
- Equip and unequip still send at once; they set only the worn slots, so they do not revert an appearance and are not sequenced here. (Superseded by [ADR-120](ADR-120-wardrobe-look-changes-in-order.md): a take-off could overtake the wear before it.)

## References

- `wasm/Nosum.Wasm.Socket/Look/LookChannel.cs` (`SetAppearance`, `TryGetPendingAppearance`, `SendAppearanceAsync`).
- `wasm/Nosum.Wasm.ViewModels/Looks/WardrobeViewModel.cs` (`Look`).
- Tests: `tests/Nosum.UnitTests/Wasm/WardrobeAppearanceTests.cs`; `tests/Nosum.UnitTests/Wasm/FakeGameSocket.cs` (`HoldReplies`, `ReleaseOldestHeld`).
- Browser check: `tests/browser/account-round4/capture.js` (the quick-choice step), frames in `docs/qa/account-round4/`.
