# ADR-099: A fractional pointer no longer stops the frame loop; a farm use that worked says so; a tap's harvest is named like the action key's

## Status

Accepted

## Context

The farming round three pass (`docs/design/critique-farming.md`, round-three addendum; captures in `docs/qa/farming-round3/`) found three things.

- **The frame loop can stop.** At 390 px, with the touch layout and a mouse over the canvas, the browser reported a fractional pointer position
  (`466.138671875`). `renderer.js` passes `e.offsetX` straight to `GameLoop.Frame`, whose `pointerX`/`pointerY` are `int` `[JSExport]` parameters. The
  runtime throws "Value is not an integer", the exception leaves `tick` before its `requestAnimationFrame(tick)` call, and the loop does not run again.
  The page stays on its last frame. The fault was seen in one phone run (`docs/qa/farming-round3/before/`, the first capture of the phone set); it depends
  on where the pointer lands.
- **A successful farm use is silent.** A till, a sowing, a watering and a harvest with the scythe change the tile and the quest card, and say nothing
  (`docs/design/critique-farming.md`, round one, finding 2). The message line carries refusals only.
- **A harvest made with another item in hand is named only for the action key.** The server harvests a ripe crop whatever is held, and a mouse tap
  with a watering can on a ripe crop harvests it too. The E key names the harvest (ADR-094 backlog 3); the tap does not, so the crop leaves the tile,
  the quest moves on, and the line stays empty (`docs/qa/farming-round3/before/en-08-harvest-by-tap-with-can-1280x720.png`).

## Decision

- **D1 - Pointer coordinates are rounded before they reach an `int`.** `GameLoop.Frame` takes `pointerX` and `pointerY` as `double` and passes them through
  `PointerPixel.Of` (`wasm/Nosum.Wasm.ViewModels/Input/PointerPixel.cs`): round half away from zero, a value that is not finite is zero, the result is
  clamped to the `int` range. The rule lives in the view-model project so it is unit-tested (`PointerPixelTests`). Canvas size stays `int` (it comes from
  `clientWidth`, which is always whole).
- **D2 - A single-tile harvest with another item in hand is named, from the action key or a tap.** `FarmHarvestNotice.NamesHarvest` is true for
  `FarmStatus.Ok`, `FarmAction.Harvested`, one tile, and an item in hand that is neither nothing nor the scythe. Its line is
  `Farm_HarvestedWithOtherItem`: "Harvested, even with another item in hand." / "Đã thu hoạch, dù đang cầm đồ khác." The text no longer names the key,
  because a tap made it too. A wide use is counted by its own count (ADR-088 D1), not named one tile at a time.
- **D3 - A farm use that worked gets one short line.** `FarmDoneNotice.KeyOf` maps a successful use to a key: `Farm_Done_Tilled` ("Soil tilled." /
  "Đã xới đất."), `Farm_Done_Planted` ("Seeds planted." / "Đã gieo hạt."), `Farm_Done_Watered` ("Watered." / "Đã tưới nước."), `Farm_Done_Harvested`
  ("Harvested." / "Đã thu hoạch."), or the D2 line. A refusal, a ladder, a hit and a use that did nothing return no key and keep their own behaviour.
  `PlayerActions` raises `FarmUseSucceeded` with the key; the toast machinery (ui-standard section 5) shows it, and repeats collapse to "×N".
- **D4 - Wire.** No change. Protocol version stays 32. The reply, the opcodes and every enum are as on master. The change is client-side: a `double`
  parameter in a JS export, one event, and the new `.resx` keys, which exist in both languages (`FarmDoneNoticeTests.Keys_AreInBothLanguages`).

## Consequences

- A pointer over a scaled or emulated screen no longer stops the game. The frame loop's one interop call per frame is unchanged.
- Every successful farm use now says what it did. Hold-to-repeat watering stacks as "Watered. ×N" rather than a new line each time.
- The message line can show a hover hint (growth stage), an onboarding hint and a toast at once. The hint and the hover line were there before; the
  success line adds the third. This is a layout question for the owner (the round-three addendum lists it).
- Not done here: a scythe tap on an unripe crop shows no line at all, while the E key shows "Not ready, N days left". The cause is not yet found (see
  the addendum).

## References

- docs/adr/ADR-094 (the E-key harvest line, `FarmRules`, protocol 31), ADR-088 D1 (wide-use count), ADR-002 (one interop call per frame), docs/design/ui-standard.md
  section 5 (toasts: 3 s, max two stacked, "+1 Cá chép" as a success example).
