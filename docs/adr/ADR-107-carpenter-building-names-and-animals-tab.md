# ADR-107: Building names by what they house, and the carpenter's animals on their own tab

## Status

Accepted. Amends ADR-028 (the coop and barn names, the animal offers) and ADR-092 D4 (where the carpenter's results sit). Client content and layout
only: no packet, opcode, field, enum value on the wire or database change, and `NosumProtocol.Version` stays 30.

## Context

`docs/design/critique-animals-pets.md` (finding 6) and the round-three grading (`docs/qa/animals-pets-round3/README.md`) left three problems open at the
carpenter's Build page:

- **Names.** Each animal row was labelled with its building's name. The coop was "Chuồng gà" (chicken coop) and the barn "Chuồng bò" (cow barn), so
  in Vietnamese a duck read "Chuồng gà (2/4)" and a goat, sheep or pig read "Chuồng bò (0/4)". The English "Coop" and "Barn" were right for the ducks
  and the goats, but the row still repeated the building's name on every line.
- **Layout.** The Build page put the blueprints, the name field and all six animal rows on one tab. At 1280 x 720 the panel body scrolled: the pig row and
  the barn's last rows were out of view (`docs/qa/animals-round4/before/1280x720-en/barn/carpenter-animals.png`), which `docs/design/ui-standard.md` section 7 item 7 forbids.
- **Blueprint rows.** Once the English coop name was longer ("Poultry coop"), the row's button wrapped onto its own line, out of line with the barn's.

## Decision

- **D1 - A building is named for what it houses.** The coop houses chickens and ducks: "Chuồng gia cầm" (poultry house) and "Poultry coop". The barn houses
  cows, goats, sheep and pigs: "Chuồng gia súc" (livestock barn) and "Barn". The keys (`Building_coop_Name`, `Building_barn_Name`) are unchanged, so every
  caller follows the rename. The carpenter's line in the NPC's greeting follows the same names. Tests: `CarpenterViewModelTests.Offers_NameTheBuildingEachAnimalLivesIn_InBothLanguages`.
- **D2 - The animals have their own tab, and each building is one group.** On the Build page the carpenter's tabs are Buildings, Animals and Pets
  (`CarpenterTab.Animals`, shown only when the page shows animals, so the Buy page keeps its tabs as they were). The Animals tab holds the purchase notice, the
  name field and one group per finished building: the building's name and fullness once, then its species as buy buttons that wrap. A blueprint row
  stacks its name over its cost, so the button stays on the row's first line.
- **D3 - Balance: the chicken pays back in about four seasons (ADR-081 round-4 amendment).** The chicken's fed payback was 9.5 seasons. Egg 15 → 20 and the
  price 800 → 950 bring it to 4.2, with the everyday product still inside the 40 to 60 % band of ADR-081 D4 (58.9 %). Locked by
  `EconomyBalanceTests.Animal_FedPaybackOverASeason_IsWithinFiveSeasons`.
- **D4 - The carpenter's result sits above the section it belongs to.** The purchase notice sits above the Animals section, and a blueprint notice above the
  Buildings rows on the Build page (ADR-092 D4). It is shown only where the page shows animals, as before.

## Measured (browser, `tests/browser/animals-barn-capture.cjs`)

- Before, at 1280 x 720 English, the barn's four rows sat on the Build page's one tab with the blueprints; the pig row was cut
  (`before/1280x720-en/barn/carpenter-animals.png`). After, the Animals tab fits: the body has no scroll (`bodyScrollHeight` = `bodyClientHeight`) and no
  button is cut (`after/1280x720-en/barn/carpenter-animals.png`). The numbers and every frame are in [`docs/qa/animals-round4/`](../qa/animals-round4/README.md).
- The after frames at 390 x 844 and 1920 x 1080 are graded in the same README.

## Consequences

- A player sees "Poultry coop" or "Chuồng gia cầm" over the ducks, and "Barn" or "Chuồng gia súc" over the cows. The names of buildings change everywhere
  they are shown: the carpenter's list, the animal rows, the NPC's greeting.
- The Build page has one more tab. The blueprint and the animals are no longer on one scroll, so a player who wants both moves between two tabs.
- The egg is worth 20 in the bin, so a chicken's eggs now sell for as much as a duck's (`ItemCatalog.Egg`). The market's price of an egg follows the bin.
- The pet's mood is still not used. `docs/adr/ADR-092.md` (round-4 amendment) proposes the numbers; that change is a rule, not data, and it is not applied here.

## References

- [ADR-028](ADR-028-farm-buildings-and-animals.md) (buildings, animals, the carpenter), [ADR-080](ADR-080-npc-dialogue-lines-and-seasonal-stock.md) D5 (the carpenter's Buy and Build pages).
- [ADR-081](ADR-081-economy-balance-targets.md) round-4 amendment (the chicken's egg and price).
- [ADR-092](ADR-092-animal-produce-cue-action-preview-and-pet-forage.md) D4 (the notice above the section).
- `docs/design/ui-standard.md` sections 3 and 7.
