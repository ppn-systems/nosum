# QA: animals and pets, round five (petting energy, name under the species)

Critique: [`docs/design/critique-animals-pets.md`](../../design/critique-animals-pets.md), round-five findings 9, 10 and the name order of finding 6.
No decision record: no rule changed, and the wire is the same.

- Frames, per viewport (1280x720, 1920x1080, 390x844) and language (en, vi):
  - `after/<viewport>-<lang>/coop/carpenter-animals.png`: the carpenter's Animals tab, the poultry coop with the name field under the species buttons.
    Before (round four, the name field above the species): `docs/qa/animals-round4/after/<viewport>-<lang>/coop/carpenter-animals.png`.
  - `after/<viewport>-<lang>/pets/pet-day-1.png`: the pet panel after the first petting of the day, with the line "Petted. Energy +5." (vi: "Đã vuốt ve. Sức lực +5.").
    Before: the same panel said "Done.".
- Reports: `report-*.json` beside each frame. `buttonsCut` is empty in all six coop runs. The coop run's `buy-cow` and `buy-goat` steps fail by design:
  those animals are sold only in the barn, and the script does not know which building it is on. The pet runs have no failed steps.
- Scripts: `tests/browser/animals-barn-capture.cjs` (`BUILDINGS=coop`) and `tests/browser/animals-pets-capture.cjs`. The pet script now opens the
  carpenter's Animals tab before it types the names of the two chickens; the Buildings tab has no input, so the script had been failing there since ADR-107.
- Builds: client `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, served statically; server `DOTNET_ENVIRONMENT=Development`.
  One server served all twelve runs, each on a fresh guest; the clock was shared, so later runs start on later days. The coop was placed and
  built in every run (no placement step failed).
