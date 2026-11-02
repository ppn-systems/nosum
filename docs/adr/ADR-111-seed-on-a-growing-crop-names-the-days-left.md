# ADR-111: A seed on a growing crop names the days left

## Status

Accepted

Amends [ADR-094](ADR-094-scythe-line-days-left-and-one-watering-rule.md) (D2: the days-left reply covers a seed refused by a growing crop, not only a
harvest). No wire change: `UseItemResult`, `FarmStatus` and `NosumProtocol.Version` (34) stay as they are.

## Context

Round five of the farming critique (`docs/design/critique-farming.md`) kept one finding open from round one: a seed in hand tapped on an unripe crop was told
"A crop already grows here.". That is true, but it does not say when the crop is ready, so the player cannot tell whether to wait or to come back later.
ADR-094 D2 already gives the days left for a harvest attempt, and the server already computes them (`FarmRules.DaysUntilMature`). The seed refusal did not use them.

## Decision

- **D1 - A seed refused by a growing crop names the days left.** When a seed is refused because a living crop that is not yet mature holds the tile
  (`FarmVerdict.HasCrop`), `FarmService` replies `FarmStatus.NotMature` with `DaysLeft`, as a harvest attempt on an unripe crop does (ADR-094 D2). The
  client writes "Not ready, N days left" from its existing `.resx` keys, so no text is added.
- **D2 - The rest of the refusals keep their meaning.** A mature crop under a seed is harvested first (unchanged). A seed on a trailing tile of a wide swing
  on a mature crop keeps `HasCrop`. A withered crop is replaced by a seed (unchanged). A pickaxe on a growing crop keeps `HasCrop`: it clears nothing and
  waits for nothing, so "Not ready" would mislead.
- **D3 - A hoe is not changed.** A hoe on a tilled tile answers `AlreadyTilled` before it reaches the crop check, so no hoe reply needs the days left.

## Consequences

- A seed tap on a growing crop reads "Not ready, 4 days left" (vi "Chưa chín, còn 4 ngày") instead of "A crop already grows here.". The player learns
  when to come back.
- The server rule is in `FarmService` (the refusal mapping), not in `FarmRules`: the pure rule still returns `HasCrop`, and the service decides what the
  refusal means on the wire. `FarmWireTests` pins the reply; no unit test changes.
- Captures: `docs/qa/farming-round5/` (1280x720, 1920x1080, 390x844 in vi and en).

## Revisit when

- Planting on a crop that is ripe should name the harvest ("Harvest it first") rather than the generic text; that needs a new status code and a protocol
  bump, so it waits for a design decision.
