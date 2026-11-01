# Combat round 7: the 390x844 "monster not beside the farmer" question

Build: `dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true` on `origin/master` (d1a8041, PR #325), Development
server, page `?qa=1`, English. Capture: `docs/qa/combat-round5/capture.cjs telegraph` at 390x844 (`TG_DX`/`TG_DY` as noted).

## Result

No position divergence between the server and the client was found, and no code change was made in this round.

| Run | Layout | Monster in frame | "!" cue in wind-up frames | Hits |
| :-- | :-- | :-- | :-- | :-- |
| `en-390x844-dy1/` | `TG_DX=0 TG_DY=1` (monster one tile above the farmer) | Only its gold aura and name label; the farmer's sprite covers the body | Visible above the farmer's head, frames 04 to 10 (about 1088 to 1513 ms) | 84 to 76 to 68 |
| `en-390x844-dx1/` | `TG_DX=1 TG_DY=0` (monster one tile left of the farmer) | Visible, one tile beside the farmer | Visible above the monster, frames 04 to 10 | 76 to 68 to 60 |

Round 6's English run (`docs/qa/combat-round6/sheet-r6-390-en.png`) shows the farmer on the spore-vent patch with no monster in the frame at all.
That run has no capture of the server's tile, so it cannot be tied to a tile in this round.

## What the frames show

- In the side layout the monster, the "!" cue and the health bar all sit on the monster's tile, one tile left of the farmer. The client draws
  the monster where the snapshot says it is, and the farmer where the teleport put it.
- In the `TG_DY=1` layout the monster is occluded by the farmer's body: `MonstersDecorator` draws the sprite and the aura in the monster's
  sort order, and the farmer, lower on the screen, is drawn over it. The "!" cue is an overlay (ADR-097 D4), so it stays visible. This is a
  geometry effect of the placement, not a position error. It is a readability question for the round-6 capture layout, not a bug.
- The camera follows the farmer before the decorators run (`MapScene`: `_camera.Follow` before `SetView`), so culling uses the same camera
  as the farmer's draw.

## Not established

- Whether the round-6 English run had the farmer and the striking monster on different tiles. The fix would need a server-side log of the
  monster and farmer tiles per tick, which this round did not add.
- Which monster the QA `teleportToMonster` picked in that run. It takes the first living row of the client list, which may be a stale row.

## Next step

Add a per-tick log of the striking monster's tile and the farmer's tile on the server (AI tick and strike resolution), run the English
390x844 capture again, and compare the two tiles with the drawn position. Without that log a divergence cannot be proven or fixed.
