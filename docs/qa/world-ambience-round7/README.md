# World ambience, round 7

Evidence for round 7 of [`docs/design/critique-world-ambience.md`](../../design/critique-world-ambience.md) (section 11). Change: the sampled minimap
cell rule in `engine/Nosum.Rendering/MinimapLayer.cs` (`ColourOfBlock`), tested in `tests/Nosum.UnitTests/Engine/MinimapLayerTests.cs`.

## Frames

- `before/` is master (the sampled minimap read one corner tile per cell). `after/` is this branch's revised rule (water if any tile is water; walls and
  tilled ground when at least half the block has them). Scenario `noon-spring-sun`, English and Vietnamese, 1280 × 720, 1920 × 1080 and 390 × 844.
- `minimap-before-after-4x.png`: the minimap card only, English at 1280 × 720, before (left) and after (right), each at 4× nearest-neighbour. Rows: farm,
  town, beach, forest, mine1. The phone (390 px) hides the minimap by default, so its frames show no minimap change.
- `report-noon-spring-sun-en+vi.json`: one entry per frame with the HUD calendar text and the audio starts.

## What changed on the sampled maps (English, 1280 × 720 minimap crop)

| Map | What the before frame showed | What the after frame shows |
|:--|:--|:--|
| Farm (80 × 65, sampled 2×) | The pond was a single blue dot, and only where its tile sat on the corner | The pond is a clear blue block |
| Town (130 × 110, sampled 4×) | Walls and buildings came and went with the corner tile | Walls read as clusters of buildings |
| Beach (104 × 50, sampled 3×) | The river and the shore were there; dark speckle (rock blocks) was spread over the grass | The river and the shore are unchanged; the speckle is gone (no block is half rock) |
| Forest (120 × 120, sampled 4×) | Scattered trees showed as a dense speckle | **Scattered trees are gone**: no 4 × 4 block is half trees, so the forest reads as open grass |
| Mine floor 1 (48 × 48, sampled 2×) | The floor's walls and open ground | No change inside the minimap box. The grey shapes at the edges of the frame are world tiles, not minimap cells |

## Limits

- One calendar state (noon spring sun) and English only in the strip. The minimap is not time-of-day dependent, so the other states are not needed here.
- A listening or hand test of the minimap was not possible; the readability call is from the frames.
- The after frames are the revised rule. An earlier build (any wall wins) was captured and replaced; its frames are in git history, not here.
