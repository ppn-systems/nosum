# QA captures: boots and the speed tonic (ADR-084)

Captured on commit 885a33ca (wardrobe Feet slot fix) with the development server (`DOTNET_ENVIRONMENT=Development`,
database `nosum_dev`) and the client built with `-p:NosumDevTools=true`, opened at `/?qa=1` as a guest, English (`lang=2`).
Both viewports: 1280x720 and 390x844 (phone, touch emulation). Each file is the full page at that size.

Boots worn: **Winged boots (411)**, worn through the UI (bag stack picked up, then the Feet slot clicked). Boots and tonic
were granted with `nosumQa.grant`.

| File | Screen | Shows |
|:--|:--|:--|
| `paperdoll-1280x720.png`, `paperdoll-390x844.png` | Inventory page (the wardrobe's worn list) | The Feet row (fourth worn slot, bottom left of the equipment column) holds Winged boots; the bag holds the other boots and the tonic. The paperdoll figure is at the side of the equipment column. |
| `farmer-1280x720.png`, `farmer-390x844.png` | Farm world, inventory closed | The farmer standing on the farm with the HUD. Boots are not distinguishable at native size (night tint); see defects. |
| `tooltip-1280x720.png`, `tooltip-390x844.png` | Winged boots tooltip, hovering the worn Feet slot | Name, description, `Walking speed: +20%`, sell price, `Click to take off`. |
| `tooltip-leather-<WxH>.png` | Leather boots (408) in the bag | Name, description, `Walking speed: +5%`. |
| `tooltip-traveler-<WxH>.png` | Traveler boots (409) in the bag | Name, description, `Walking speed: +10%`. |
| `tooltip-swift-<WxH>.png` | Swift boots (410) in the bag | Name, description, `Walking speed: +15%`. |
| `tooltip-tonic-<WxH>.png` | Speed tonic (715, quantity 2) in the bag | Name, description (three game hours, +25%, shares the Swiftness slot), sell price, quantity. |

The extra tooltip files (leather, traveler, swift, tonic) are named by item rather than by the bare pattern, because one
tooltip per viewport cannot show five items.

## Defects seen in these captures

- **Phone tooltips overlap.** At 390x844 every item tooltip (boots and tonic) has its lines drawn over each other and runs
  past the box (for example `Walking speed: +20%` collides with the description). The 1280x720 tooltips are clean.
- **Duplicated speed line.** The description already says `+20% walking speed.` and the stat line says `Walking speed: +20%`.
- **Farmer boots not verifiable.** The farmer at native size is too small and night-tinted to confirm the boots layer or its tier.

## Environment notes

- The client build on this Linux machine does not ship `assets/farmer/boots.png` (the farmer list in
  `wasm/Nosum.Wasm/Nosum.Wasm.csproj` omits it), so the game refused to start with `HTTP 404`. The capture served that one
  file from `assets/farmer/boots.png` through the browser route. No other asset was substituted.
