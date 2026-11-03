# QA: animals and pets, round six (the bench's ration count, the refused name under its field)

Critique: [`docs/design/critique-animals-pets.md`](../../design/critique-animals-pets.md), round-six findings 2 (count) and 10 (name refusal).
No decision record: no rule or wire change.

- Frames, per viewport (1280x720, 1920x1080, 390x844) and language (en, vi), in `after/<viewport>-<lang>/pets/`:
  - `carpenter-name-refused.png`: the Animals tab after a purchase with the name field empty; the refusal sits under the field.
  - `coop-fed.png` and `coop-hint-*.png`: the coop with the bench fed; the ration count (24 after a press of 30 hay) shows beside the hay.
- Reports: `report-*.json` beside each run. No step failed in any of the six runs.
- Script: `tests/browser/animals-pets-capture.cjs` (it gained the refused-name step in this round).
- Server: Development, PostgreSQL 16; client: `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, served statically.
