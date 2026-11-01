# ADR-100: The feet rule steps the ghost aside, and a craft names what it made

## Status

Accepted

Amends [ADR-015](ADR-015-private-instances-and-placeables.md) (the feet rule is unchanged on the server; the place the ghost shows and E sends is
chosen beside an aimed tile that is under the feet). No wire change: `NosumProtocol.Version` does not move (protocol 32).

## Context

[`docs/design/critique-placement.md`](../design/critique-placement.md) finding 16: a piece that blocks walking may not stand on the placer's feet
(`PlacedSet.CoversFeet`, the same feet box as `MoveValidator.FootBlocked`). The farmer stops in the right 5 px of a tile, the feet box (10 px wide)
reaches into the next tile, and the piece aimed there is refused with "You are standing there." on a tile that looks free. Round 3 changed only the
words ("Step aside to place it."); stepping back one tile did not clear every case, and the player had to guess the way out.

Finding 17: a craft gave no line. The bag count changed and the chest went to the bag; nothing said what was made.

## Decision

- **D1 — The rule stays.** A blocking piece is never placed on a present player's feet. Letting a sliver of overlap through would leave a farmer inside a
  blocked tile, where the walker cannot always leave it. `PlaceableService` validates every placement as before; no check is relaxed.
- **D2 — The ghost steps aside.** When the aimed anchor is refused only because it covers the placer's feet, the client tries the eight anchors beside it
  in a fixed order (`PlacedSet.StepAsideOffsets`: right, left, down, up, then the four corners) and shows the ghost on the first one that passes the same
  checks and leaves the feet clear (`PlacedSet.TryFindClearAnchor`). The ghost is green there, and E places it there. The server receives that anchor
  and validates it as any other; if another present player stands on it, the server refuses with the existing "in the way" sentence.
- **D3 — When no neighbour fits**, the ghost stays where it was aimed and red, with the existing sentence ("You are standing there. Step aside to
  place it.").
- **D4 — A craft says what it made.** A successful craft (`CraftingChannel.Crafted`) shows one line on the message line: the output's name and the total
  made ("Crafted {name} x{n}. It is in your bag." / "Đã chế tạo {name} x{n}. Đã vào túi của bạn."). A refused craft keeps its status line and shows no
  confirmation. The line is raised by `CraftingViewModel` through the optional `GameMessagesViewModel`, the same pattern as the chest page.

## Consequences

- Finding 16 is closed for the aimed-under-feet case: a player who walks into the target tile sees the ghost move beside it and places in one press.
- A ghost that moves is not labelled as moved; the player sees it on the tile beside the aim. Explaining it is a copy decision left open.
- The step-aside search is a fixed eight-tile list and allocates nothing; it runs only while the aimed tile is under the feet.
- The reach of the moved anchor is not checked on the client (the status does not include reach, as before); a moved anchor one tile from the aim is
  within the farming reach of a farmer who could place the aimed one, and the server refuses it otherwise.
- Browser evidence: `docs/qa/placement-round4/` (before and after, Vietnamese and English, 1280×720 and 390×844).
