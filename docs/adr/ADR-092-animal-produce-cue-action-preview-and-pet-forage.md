# ADR-092: Produce cue after the day rolls over, the action key's preview, and a pet that brings back forage

## Status

Accepted

## Context

`docs/design/critique-animals-pets.md` (the round-one review of PR #289) left four findings open. Each one costs the player a guess or a
wasted trip:

- **Finding 3.** The feeding of a coop settles lazily: the first touch of a building on a new game day runs the night (ADR-028 D3). A player who
  keeps the coop open across midnight sees the hens without their eggs, and nothing says that eggs are waiting.
- **Finding 4.** The action key feeds, collects or pets, depending on what the farmer holds and faces. The outcome is only a line of text after
  the press, so the player cannot tell in advance what E will do.
- **Finding 8.** The pet does nothing in the world. Its mood has no effect, and petting is reachable only from the menu.
- **Finding 6.** The carpenter's purchase notice sits under the animal offers. At 1280 x 720 the bottom of the tab is out of view, so the notice
  is clipped, and `docs/design/ui-standard.md` section 7 forbids clipping.

The server already sends the produce flag of every animal (`AnimalView.ProduceFlag`, protocol 29), and the room load settles the night. What is
missing is a re-read when the game day changes while the player stays inside, and a visible cue for the flag.

## Decision

- **D1 - The cue is the server's flag, kept current.** The server remains the one source of produce: `AnimalRules.Yields` decides, and the room
  snapshot carries the flag. When the game day changes while a player is inside a building, the client asks for the room again (`LoadRoom`), which
  settles the night on the server and returns the new flags. The engine then draws the gold ready sparkle that marks a ripe crop (ADR-021), eight
  pixels, over the top-right corner of each animal that has produce today. No day hook and no push are added (ADR-028 D3 still holds): a player
  who is not inside sees nothing until they enter, as before.
- **D2 - A pet can bring back forage.** The first petting of a game day may find one forage item: thirty chances in a hundred, from spice berry,
  wild plum or mushroom, each equally likely (`PetRules.ForageChancePercent`, `PetRules.ForageItems`). The roll is a pure function of the player and
  the game day (`PetRules.ForageOf`), so the same petting gives the same answer and a test can check it. The item is added through the inventory
  service with the audit reason `pet_forage`. A full bag keeps the item on the grass, and the petting still counts. Refused pettings (a second one
  that day, or no pet) roll nothing.
- **D3 - The action key says what it will do.** `AnimalRules.TendOf` is the one decision of the action key inside a building: feed at the trough,
  collect produce (with a pail for a cow), or pet the animal in reach. The client calls the same decision every frame to preview it, so the hint and
  the press always agree. The hint is a short word drawn with the engine's labels: "Feed" over the bench when hay is in hand at the trough, and
  "Collect" or "Pet" over the animal in reach. The words are localized (`Animal_Tend_*`, Vietnamese and English).
- **D4 - The carpenter's result sits at the top.** The purchase notice of the animal section, and the pet notice of the pet tab, move above their
  sections, so they are on screen without scrolling at 1280 x 720 (`ui-standard.md` section 7, item 6).
- **D5 - Wire.** Protocol version stays 29. No packet, opcode, field or enum value changed. The forage item reaches the client through the normal
  inventory update, so no client needs a new message.

## Consequences

- A coop's produce is visible on the first frame after midnight once the player is inside, and the sparkle says so. A player who stays inside across
  midnight sees the new day's eggs without leaving the room.
- A pet has a small daily effect, and it is server-authoritative. The expected gain is about 0.3 forage items per petting day, and it costs no
  energy or coins.
- The action key's hint costs one ray test per frame inside a building, the same test the press already made. It allocates nothing.
- The pet's mood still changes nothing (`docs/design/critique-animals-pets.md` finding 8, option 1). This ADR adds the forage; it does not tie mood to
  energy or forage. That is the next candidate.
- The database budget of a petting grows by the inventory write when the item is found, which is in memory and written behind (`InventoryService`).
  The `DbBudget` rows are checked by `DbBudgetTests`.
- Not done here: the sparkle and the hints are not yet checked in a browser at 1280 x 720 and 390 x 844 (no before and after captures in this
  change), and a chicken's payback (finding 7) is still a balance question for `docs/design/economy.md`.

## References

- [ADR-028](ADR-028-farm-buildings-and-animals.md) D3 (lazy overnight feeding), D4 (animals drawn in the room).
- [ADR-021](ADR-021-tools-resource-nodes-ground-items.md) D7 (the gold ready sparkle).
- [ADR-075](ADR-075-emotes-and-pet-companion.md) (the pet, its daily petting).
- [ADR-035](ADR-035-database-round-trip-budget.md) (the database budget rows).
- [ADR-088](ADR-088-wide-use-count-and-the-wet-soil-refusal.md) (the last protocol bump, version 29, unchanged here).
- `docs/design/critique-animals-pets.md` (findings 3, 4, 6 and 8).

## Amendments

### 2026-11-02 - the browser check found three gaps (round three)

The browser check at 1280 x 720 and 390 x 844 (`docs/qa/animals-pets-round3/`, `tests/browser/animals-pets-capture.cjs`) found that the day cue, the
forage and the daily petting did not work as D1 and D2 describe. Each is fixed here; the wire is unchanged (protocol 29).

- **D1 amendment - the day is read on the server's clock.** `AnimalsDecorator` read the game day from the local clock, so a server clock that moved
  on (a QA `advanceClock`) never changed the day in the room: the room was not read again, and the sparkle appeared only after the farmer left and
  came back. The day is now read from `CalendarChannel.ServerNowMs()` (the calendar's estimate of the server clock, docs/adr/ADR-018). A real
  rollover and a clock jump both reload the room in place.
- **D2 amendment - the forage lands on the grass.** `PetService` discarded the result of the inventory add, so a full bag lost the forage. A refused
  add now drops the item at the farmer's feet as a ground item caused by the farmer (`IGroundItems.DropAsync`, docs/adr/ADR-021 D5); the petting still
  counts. `PetWireTests.PetPet_WithAFullBag_LeavesTheForageOnTheGrassBesideThePlayer` checks it over a real session.
- **D2 amendment - the pet is read when its panel opens.** The pet was read once at sign-in, so a petting on an earlier game day kept the Pet button
  off until the next sign-in. Opening the Pet panel reads the pet again (`Home.Show`), so each game day's petting is open.
