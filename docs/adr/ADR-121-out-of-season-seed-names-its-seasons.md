# ADR-121: An out-of-season seed names the seasons its crop grows in

## Status

Accepted

Amends nothing. Uses the crop seasons of [ADR-018](ADR-018-seasons-and-server-calendar.md) and the wording of the shop's season note (ADR-024:
`Shop_Block_NotInSeason`). No wire change: `UseItemResult`, `FarmStatus` and `NosumProtocol.Version` (34) stay as they are.

## Context

Round six of the farming critique (`docs/design/critique-farming.md`, round-6 addendum) looked at the seasons feedback. A seed tapped on a tilled tile in a
season its crop does not grow in is refused with `FarmStatus.OutOfSeason`, and the line reads "That crop does not grow in this season." The player learns
that the crop is wrong for the season, but not which season it does grow in, so the only way to find out is to try the seeds one by one or read a shop row.
The shop already greys a row and names the seasons ("Plants in Spring, Fall, Winter"). The farm refusal did not.

The client already holds the data: `CropDefinition.Seasons` in `Nosum.Gameplay`, which the client references, is the same data the shop reads.

## Decision

- **D1 - The out-of-season line names the seasons.** When a use is refused as `OutOfSeason` and the item in hand is a seed with a known crop, the message
  line reads "That crop only grows in {seasons}." (vi "Cây này chỉ trồng được vào {seasons}."), the season names in calendar order and joined with ", ".
  The seasons come from `FarmSeasonNotice.SeasonsOf` (`wasm/Nosum.Wasm.ViewModels/Feedback/`), which reads `CropCatalog` and the seed's `CropId`. The
  server's status and the wire do not change. When the item in hand is not a seed, the plain line stands.
- **D2 - The seed is the one in the selected slot when the reply arrives.** The reply carries no item, so `GameMessagesViewModel` reads
  `InventoryViewModel.Held`. A reply takes about one round trip, and the player rarely changes slot in that time. A slot change inside it can name the
  seasons of the other seed (when both are seeds), so this is a known limit; a use that carries the item it fired is the fix if it is ever seen.
- **D3 - Refusals keep their meaning and their codes.** `OutOfSeason` stays the status. A seed refused by a crop that is still growing or ripe keeps
  `HasCrop` or `NotMature` (ADR-111). No new status code is added.

## Consequences

- The player reads "That crop only grows in Spring, Fall, Winter." for a parsnip seed in Summer, and can choose a seed that grows now.
- The client depends on `CropCatalog` and `ItemCatalog` as the shop already does; no new reference.
- A unit test pins the season list (`FarmSeasonNoticeTests`: order, one season, not a seed). The message line itself is captured in the browser
  (`tests/browser/farm-round6-capture.cjs`, `docs/qa/farming-round6/`, 1280x720, 1920x1080, 390x844 in vi and en).

## Revisit when

- A seed tapped on a ripe crop still answers "A crop already grows here." (`HasCrop`). Naming the harvest there needs a new status code and a protocol
  bump, or a server decision that a seed tap harvests the crop. That is the open question in ADR-111's "revisit when" list.
- A reply that fires from a seed the player no longer holds becomes visible in play: carry the fired item on the use (D2).
