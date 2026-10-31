# QA captures: running with energy (ADR-085)

Captured on commit **5611626** (`git rev-parse --short HEAD` when the capture started), with uncommitted working-tree changes
on top (the client parts: `EnergyBar`, `ProgressViewModel`, `RunState`, `MapScene`, `GameLoop`, `ClientWiring`). While the capture
ran, HEAD moved to **f6f8b2f** (`fix(server): let running keep the boots, raise the speed cap to 1.8`, committed 16:13 on this branch). The server was started from binaries built at 15:51, before that commit, and was not rebuilt.

Environment: the development server (`DOTNET_ENVIRONMENT=Development`, database `nosum_dev`, created for this capture) and the
client built with `-p:NosumDevTools=true` (Debug), served on `localhost:5080`, opened at `/?qa=1` as a guest, English (`lang=2`).
Database role: the local test role (superuser, test-only). No file was served through a route: the build's static asset manifest
lists `assets/farmer/boots.png` (HTTP 200).

Each file is the full page at that size. Running shots: Shift and ArrowRight held (keyboard, also at 390x844 with touch emulation),
frame taken about 0.7 s into the hold. Out-of-energy shots: Shift held, the farmer walking left and right every 1.5 s, the frame taken
as the toast appears with the bar at 0.

| File | Screen | Shows |
|:--|:--|:--|
| `idle-1280x720.png`, `idle-1920x1080.png`, `idle-390x844.png` | Farm, nothing held | Energy bar at 270 / 270, not running. |
| `running-1280x720.png`, `running-1920x1080.png`, `running-390x844.png` | Farm, Shift + ArrowRight held | Energy bar has class `running`; tooltip `Energy 270 / 270 · Running`. |
| `out-of-energy-1280x720.png`, `out-of-energy-1920x1080.png`, `out-of-energy-390x844.png` | Farm, energy 0 / 270, Shift still held | Toast `Out of energy: you stop running.` above the hotbar; energy bar empty. |

## Results

| Size | `running` class | `Running` in tooltip | Out-of-energy toast |
|:--|:--|:--|:--|
| 1280x720 | yes | yes | yes, energy 0 / 270 (captured 16:43) |
| 1920x1080 | yes | yes | yes, energy 0 / 270 (captured 16:37) |
| 390x844 (touch emulation) | yes | yes | yes, energy 0 / 270 (captured 16:36) |

The out-of-energy runs each drained 270 units from full at the 0.25 per second rate (about 18 minutes of continuous running).
All three frames were viewed by eye after capture: the toast is above the hotbar and the energy gauge is empty at each size.

The two blink shades were compared on the 1920x1080 running pair (taken 0.5 s apart): the fill's average colour differs
(about (76,87,48) vs (65,70,38)), so the blink runs. The first frame is kept.

## Layout check (ui-standard: nothing overlaps)

Measured rectangles (CSS px):

- 1280x720: energy bar x 1000-1030, y 606-702; hotbar x 250-922, y 600-708. No overlap. Toast sits above the hotbar (y about 548-590).
- 1920x1080: energy bar x 1560-1610, y 890-1050; hotbar x 310-1430, y 880-1060. No overlap.
- 390x844: energy bar x 355-375, y 744-808; hotbar x 15-303, y 716-836. No overlap. The touch Run and Use buttons sit above
  the hotbar and do not overlap the energy bar or the toast.

## Defects seen

- **Right menu column covers the world at the right edge.** At 1280x720 and 1920x1080 the menu column is drawn over the farmer when
  it walks to the right, and it cuts the farmer's name label (`Guest ...`). This is the column's existing placement, not part of the
  run change, but running reaches that edge sooner. Not fixed (capture only).
- No overlap between the energy bar, the toast and the hotbar was found at any size.

## Limits

- The touch Run toggle was not used at 390x844; the keyboard Shift path was used at all three sizes.
