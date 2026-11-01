# ADR-095: World labels clear the HUD, the bottom band and the touch controls

## Status

Proposed (2026-11-01); amended in round 4 (D6). Amends ADR-063 (localized world labels) and ADR-091 (hazard label clustering). Extends the HUD placement that PR #274
added to `renderer.js`. No wire change; no render command change; no new interop crossing (`NosumProtocol.Version` stays as it is). Evidence:
[docs/qa/world-ambience-round3/](../qa/world-ambience-round3/) (before and after, 1280 × 720 and 390 × 844, English and Vietnamese).

## Context

Round 2 of the world-ambience critique found two label faults on the bottom edge, and a third at the farm's north exit:

- On mine floor 15 the trap and shrine labels sat in the strip under the hotbar, at the very bottom of the view. Two of them ("VITAL" and
  "SPIKE TRAP") touched on the same line and read as one word.
- The bottom-centre HUD region (the toast line, the hotbar and its gauges) is 172 px tall at 1280 × 720 and ends 12 px above the screen edge.
  A label in the strip under it was inside no rectangle, so nothing moved it.
- The touch controls (the stick, the use, run and chat buttons) are outside the HUD grid, so placement did not know about them.
- A label that only touched another (the "↑ Backwoods" exit label over the "Guest" name tag at the farm's north exit) was not stacked, because the
  overlap test was strict.

## Decision

- **D1 — The bottom band is part of the HUD.** A HUD rectangle whose bottom is within 40 px of the canvas bottom (`BOTTOM_BAND`) is taken down to
  the edge (`hudRect`, `label-fit.js`). A label cannot sit in the gutter under the hotbar. It moves beside the rectangle it hit, then below, then
  above it, then above the band (the top of the rectangles that reach the edge), and only then stays beside the rectangle in a crowded corner.
- **D2 — Labels keep a gap from the HUD.** The placement test grows each HUD rectangle by `LABEL_GAP` (2 px). A label that only touches a frame is
  moved, as a label that overlaps one already was.
- **D3 — The touch controls are HUD.** The stick and the buttons (`.touch-stick`, `.touch-use`, `.touch-run`, `.touch-chat`) join the HUD selector,
  so labels avoid them on a phone. The DOM read stays at most every 250 ms and the list stays under 16 rectangles (a hidden control has no size and
  is skipped).
- **D4 — Stacked labels keep a gap.** A label is moved off a placed one when the two come within `STACK_GAP` (2 px), not only when they overlap:
  above it when the screen has room there, otherwise below it. A label at the top edge cannot be lifted past the canvas (the clamp would put it back
  on top of the other), which is why the "below" case exists. A step is capped at 32 moves.
- **D5 — The rules are pure and tested.** The rules live in `engine/Nosum.Rendering.Web/wwwroot/label-fit.js` (no imports, no DOM, no allocation;
  results go to a module-level `Int32Array`). `tests/browser/label-fit/label-fit.test.mjs` runs them with `node --test`, which needs no package.

## Consequences

- Labels in the bottom corners move beside the hotbar, not under it. On a dense bottom row a label may end up above the bar instead.
- A label next to the name tag of a player at an exit lifts a little higher, so the two read as a column.
- Per label, the cost is the same loop over at most 16 rectangles as before. No frame allocates, and there is still one `submitFrame` crossing.
- The JS test is not part of `dotnet test`: the placement runs in the page, and moving it to C# would need the HUD rectangles and label sizes
  to cross into .NET every frame (ADR-002).

## Alternatives considered

- **Move the labels into C#.** Rejected: the label sizes come from the canvas text measure and the HUD rectangles from the DOM, both in the page.
- **Hide labels in the bottom band.** Rejected: the shrine and trap names are the only cue for them on a dungeon floor.
- **Raise the whole world view above the hotbar.** Rejected: it changes the camera for every map to fix a few labels (ADR-077 keeps the measured inset).

## Revisit when

A design pass wants labels inside the hotbar's own frame, or the touch layout changes its regions (the rules read them from the DOM, so only the
selector and `BOTTOM_BAND` need to change).

## Amendment (round 4): one placement per label (D6)

**Context.** The round-3 capture left a stray letter before some labels on mine floor 35 ("SFLAME AURA", "SVITALITY SHRINE"). Round 4 found a placement fault that
fits that picture (the frame itself was not re-measured pixel by pixel). In `renderer.js`: a label was stacked off the labels already drawn (D4) and then fitted off the HUD (D1–D3), and the HUD fit does not
look at the placed labels. A label moved beside a HUD rectangle could land on a label drawn earlier, so the two printed one over the other, and the
edge of the one underneath showed. This is the likely cause, not a proven one: it is the only placement path found that puts two labels on each other. A node reproduction with a label at (560, 200) beside a HUD rectangle (560–600, 180–230) and a placed label at
(470, 200, 80 × 14) gives a fit at (478, 200), which overlaps the placed label (`overlapsPlaced` true).

**Decision.**

- **D6 — One placement function.** `placeLabel` (`label-fit.js`) stacks the label, fits it off the HUD, and, when the fit landed on a placed label,
  stacks it again from the fit result. The stacked spot is used when it is clear of the placed labels and on the canvas; otherwise the fit result
  stands. A label on the HUD edge is preferred to two labels on each other. `renderer.js` calls `placeLabel` once per label; it no longer calls
  `stackLabel` and `fitLabel` directly. The rule stays pure, allocation-free (results in the caller's `Int32Array`) and in the browser test
  (`tests/browser/label-fit/label-fit.test.mjs`: three new cases, including the reproduction above).

**Consequences.** Same interop and allocation as before (one `drawImage` per label, no new crossing). In the case above the label moves up beside the
HUD rectangle (478, 184), clear of both. On a crowded HUD edge the fallback can still leave a label on the HUD, as before.

**Evidence and limits.** The round-4 frame `docs/qa/world-ambience-round4/` shows mine floor 35 without the stray letter after the change. The
before and after frames are not the same moment (the clock and the player's tile differ between runs), so the frame is supporting evidence only;
the reproduction is the proof.
