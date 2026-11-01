# ADR-091: Hazard labels do not stack on one another

## Status

Accepted (2026-11-01). Amends ADR-061 (the hazard labels) and ADR-063 (the shrine labels). No wire change; no render command change; no new
allocation (`NosumProtocol.Version` stays as it is). Evidence: [docs/qa/world-ambience-round2/](../qa/world-ambience-round2/README.md), frame
`fall-noon-sun-mine35.png` (before and after).

## Context

The world-ambience round 2 capture walked the mine floors that round 1 skipped. On floor 35 (Ruins) the magma fields place a fissure every few
tiles, and each fissure drew its own "Magma fissure" overhead label. Where they were close, the same words sat on top of one another (the view
read "SCMAGMA FISSURE" in the bottom-left corner), and the labels hid the ground they described. The same happens with spike traps and spore vents.

## Decision

- **D1 — One label per cluster of the same text.** A hazard's label is placed only when no label of the same text is already placed this frame
  within 96 pixels across and 16 pixels down (`HazardLabelRules.Clashes`, `wasm/Nosum.Wasm.ViewModels/Labels/`). Labels of different text are
  never thinned: a spike trap beside a fissure still shows both.
- **D2 — Only visible hazards count.** The loop already skips hazards outside the view (`IsVisible`), so the rule works on what the player sees.
- **D3 — Fixed storage.** The labels placed this frame are kept in a 64-entry array on the decorator (`_placedHazardLabels`), reset at the start of
  `DrawHazardsAndShrines`. Labels past 64 are placed as before.

## Consequences

- A dense field shows one label per about 96 × 16 pixels rather than one per fissure. The player still learns what the hazard is; they do not get a
  label on each tile.
- The first hazard in the list keeps its label; which one that is depends on the order of the server's hazard list, which is stable per map.
- The rule is pure and tested (`HazardLabelRulesTests`).

## Revisit when

A design pass wants one label per field (the centre of a cluster) rather than the first one; or the labels get a camera-aware placement.
