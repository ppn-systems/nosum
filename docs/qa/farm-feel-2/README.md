# Farm feel, pass 2 — captures

Captured at 1280x720 against a Development server and the tester build of the client (`-p:NosumDevTools=true`, served statically), by
`tests/browser/farm-feel-capture.cjs`; the skill-button crops were scaled by `tests/browser/farm-feel-zoom.cjs`. `manifest.json` lists every frame.

## Facing (docs/adr/ADR-074 D7)

- `facing-1-before-faces-left.png`: after walking left, the hoe on the left shoulder, the farmer facing left. Native crop `facing-1-before-crop.png`, scaled `facing-1-before-crop-zoom.png`.
- `facing-2-swing-montage.png`: four crops across the swing after the mouse goes down on the tile to the right. At 60 ms the hoe is raised on the right and at 150 ms the
  flecks land on the tile to the right: the body turned to the tile it works before the swing.
- `facing-2-drag-turns-right-mid-swing.png` and `facing-3-after-till.png`: the full frames at the same moments.

## Skill cooldown overlay (docs/adr/ADR-056 D5)

- `cooldown-1-overlay-just-cast.png`: the Fireball (X) was admitted; its hotbar button is veiled and disabled. The Z and V buttons are ready.
  Crops of the buttons at native size and scaled: `cooldown-1-skill-buttons-crop.png`, `cooldown-1-skill-buttons-crop-zoom.png`.
- `cooldown-2-overlay-draining.png`: 1.5 s later the sweep has drained from the top. Crops: `cooldown-2-skill-buttons-crop-draining.png`, `cooldown-2-skill-buttons-crop-zoom-draining.png`.
- `cooldown-3-press-while-cooling-is-refused.png`: X pressed while it cools starts no swing and no skill effect.
- `cooldown-1-overlay-1920x1080.png`, `cooldown-1-overlay-1024x768.png`: the same cast at the other two sizes. The sweep is captured at the moment of the
  frame and may already have drained at these sizes; the 1280x720 crops are the evidence for the sweep itself.

## Not captured here

- The decay rules (a crop withering after two dry days, soil reverting after three) are checked by `FarmDryDayRulesTests` and `FarmWireTests.Decay_*`,
  not by screenshots.
