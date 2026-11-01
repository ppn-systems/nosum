# ADR-098: Label placement has one scope in `renderer.js`, and the round-five live check of the stray letter

## Status

Accepted

Amends [ADR-095](ADR-095-world-labels-clear-the-hud-and-touch-controls.md) D6 (the placement function: the renderer keeps no second copy of the fit rules)
and [ADR-096](ADR-096-waypoint-arrow-off-the-hud-and-the-round-three-check.md) D2 (the edge arrow reads its constants from `label-fit.js`). No wire change; no render
command change; no new interop crossing; `NosumProtocol.Version` does not move.

## Context

Two things came out of round 5 of the world-ambience critique ([section 9](../design/critique-world-ambience.md)).

1. **The stray letter (round 3, round 4).** PR #309 changed the placement order (ADR-095 D6) and said the cause was "likely". Round 5 looked for the letter in
   live Chromium frames on the build that has #309 (`docs/qa/world-ambience-round5/after-d6642a4/`): mine floor 35 at two moments, English and Vietnamese, and a
   x4 crop of the view's centre. **No stray letter appeared in any usable frame.** That is absence in three frames, not proof of a fix. A "before" build
   (master before #309) was not captured: the letter depends on which labels happen to overlap at that moment, so one frame each cannot compare them.
   The placement rule is still the only path found that puts two labels on each other, and it is covered by the node reproduction.
2. **A defect found while reading the placement code.** `renderer.js` referenced `LABEL_EDGE` (in `fitArrow`, the quest arrow's placement, ADR-096 D2)
   and `LABEL_GAP` (in the dead `fitLabel` copy) without declaring or importing them. `label-fit.js` exports them, but `renderer.js` imported only
   `hudRect` and `placeLabel` (#309 changed that import). In an ES module an undeclared identifier throws `ReferenceError` when the line runs. `fitArrow`
   runs whenever a quest waypoint sits on the current map and off the view, so the first such frame would throw inside `submitFrame`. The `fitLabel` copy
   was no longer called after #309; it was kept as a second copy of the rules, which is how the constants went out of sync.

   This is shown by reading the code and by the static test below, not by a live frame: no capture in round 5 had a waypoint off the view on its map, so the
   arrow never ran in them. It is a fault in the shipped build.

## Decision

- **D1 — The renderer imports the constants it uses.** `renderer.js` imports `LABEL_EDGE` from `label-fit.js`, the only label constant it still needs.
- **D2 — One copy of the fit rules.** The renderer's `fitLabel`, `clampTo` and the `labelX`/`labelY` names are removed. Placement is `placeLabel` (ADR-095 D6),
  and the arrow keeps its own `fitArrow`, which slides along an edge and does not use the label rules.
- **D3 — A static guard.** `tests/browser/label-fit/renderer-scope.test.mjs` reads `renderer.js` and checks that every `LABEL_*` constant it uses is imported
  or declared, that every name it imports from `label-fit.js` is exported there, that the called helpers are declared, and that the second copy of the fit
  rules is gone. It runs with `node --test` beside the placement tests. It fails on the round-4 `renderer.js` (checked).
- **D4 — The live check is a script, not a claim.** `tests/browser/world-ambience-label-zoom.cjs` teleports through the Development QA hook, saves the view and
  a x4 nearest-neighbour crop of its centre, so a later round can repeat the check on any build.

## Consequences

- A quest arrow that is off the view draws on the shipped build instead of throwing on its first frame. Nothing else in the frame path changes.
- The label placement has one implementation (`label-fit.js`); `renderer.js` is smaller by about 45 lines.
- The stray letter is **not** closed. Round 5 found no letter in three frames and no cause beyond the placement path already fixed in #309. The next check
  is a frame with a label set that has produced it (mine floor 35 with a waypoint at the view's edge), on a before build and an after build taken at the
  same clock and tile.

## Alternatives considered

- **Import `fitLabel` back and keep both copies in step.** Rejected: two copies of one rule is how the constants drifted.
- **Move the arrow's edge fit into `label-fit.js`.** Rejected for now: it slides along the view's edge one pixel at a time and uses no label size; a move
  would change the file's role for a single caller.

## Revisit when

The renderer gains a third placement (a sign, a marker), or the stray letter reappears in a live frame. Then the before and after frames are taken at the
same moment and the cause is measured, not inferred.
