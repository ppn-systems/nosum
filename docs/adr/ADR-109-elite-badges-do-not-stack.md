# ADR-109: Elite title badges of the same text are placed once per cluster

## Status

Accepted

Amends [ADR-058](ADR-058-elite-monster-modifiers-and-affixes.md) (the elite title badge) with the placement rule of [ADR-091](ADR-091-hazard-label-clustering.md).
No wire change; no render command change; no new allocation (`NosumProtocol.Version` stays as it is).

## Context

The world-ambience critique (round 5, section 9) saw two Vampiric Elite monsters of one pack draw their "HUYẾT THỆ" badge twice in Vietnamese, one over the
other. Each elite drew its badge at a fixed offset above its own sprite with no check against the others, so two elites one tile apart put the same words
16 pixels apart on one row. That is the stacking ADR-091 fixed for hazard labels, in a different place.

The round 5 note recorded the case as "two monsters, two labels; not a fault in placement, and not changed". Round 6 changes it: the words are the same and
the player learns the same thing from one badge as from two, so the second copy is only clutter. A pack with different modifiers still shows each badge.

## Decision

- **D1 — One badge per cluster of the same text.** An elite's title badge is drawn only when no badge of the same text is already drawn this frame within
  96 pixels across and 16 pixels down (`HazardLabelRules.Clashes`, the rule ADR-091 set). Badges of different text are never thinned.
- **D2 — One shared rule, one class.** The fixed-array bookkeeping that ADR-091 kept on the hazard decorator moves to `PlacedLabelSet`
  (`wasm/Nosum.Wasm.ViewModels/Labels/PlacedLabelSet.cs`). Hazards and elite badges each hold one instance, reset at the start of their draw.
- **D3 — Only visible elites count.** The monster loop already skips monsters outside the view, so the rule works on what the player sees.
- **D4 — The rest of the pack keeps its sprite, flash, strike cue and health bar.** Only the title badge is thinned.

## Consequences

- In a dense elite pack the player sees one title badge per cluster. Which elite keeps its badge is the first in the server's monster list, which is
  not stable from frame to frame as monsters move. A player who wants to know which elite is Vampiric reads the one shown; the other elites of that
  modifier are the same kind.
- The rule is pure and tested (`PlacedLabelSetTests`, `HazardLabelRulesTests`). Hazard behaviour is unchanged: the same text, the same anchors, the
  same 64-entry limit.

## Revisit when

- A design pass wants each elite to show its badge and the cluster to show a count instead (a new label, so a design call).
- Elite packs are common enough that the first-in-list choice reads as unfair; then pick the badge nearest the player.
