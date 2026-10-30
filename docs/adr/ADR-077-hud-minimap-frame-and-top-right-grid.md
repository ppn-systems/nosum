# ADR-077: HUD minimap frame and the top-right HUD grid

## Status

Accepted

## Context

The minimap sat in the bottom-right corner, beside the hotbar, so it read as part of the hotbar row and covered the world (ADR-072 D1). The
top-right column of system buttons changed shape with the screen: one column of 16 u buttons on a tall screen, two of 14 u on a short one. Nothing
could align to a column that moved, and the minimap had no frame of its own.

## Decision

- **D1 — The minimap has a HUD frame, and the engine draws the map inside it.** `HudMinimap` (`wasm/Nosum.Wasm.Controls/Layout`) is a button with
  the panels' 9-slice wood frame (`--nosum-img-panel`, 60 u by 44 u). It sits in the top-right corner, under the calendar bar and left of the system
  column. `MapScene.Minimap.cs` draws the map at the same place in the frame, from the same UI-pixel numbers. A click opens the world map.
- **D2 — The system column is a fixed two-column grid.** Its buttons are 16 u (14 u on a narrow or touch screen), so its width and its place are
  fixed in UI pixels and the minimap frame can sit beside it at a known offset.
- **D3 — The view model says whether the frame is drawn.** `MinimapViewModel` holds `Shown` (the M key, and off on a narrow canvas) and `Covered` (a
  panel is open over the corner). `Drawn` is the two together. The game loop sets `Shown`, the HUD sets `Covered`, and each raises `Changed` only on
  a change. Neither reads the layout or calls JS.
- **D4 — A narrow screen collapses the minimap to the world map button.** Under 640 px the frame and the map are not drawn; the world map button in
  the system column opens the same map.
- **D5 — A large map is sampled, not clipped.** `MinimapLayer` keeps one cell per tile for a map that fits its 50 u by 34 u ground, and otherwise
  takes one cell per several tiles (`Stride`), so the box never leaves the frame.

## Consequences

- The minimap placement of ADR-072 D1 is superseded by this record. The frame's numbers live in two files (`HudMinimap.razor.css` and
  `MapScene.Minimap.cs`); change both together.
- The minimap costs no new interop crossing: the frame is ordinary markup, updated only when `Shown` or `Covered` changes.
- Windows that cover the corner hide the minimap, as they already hide the player bar.
