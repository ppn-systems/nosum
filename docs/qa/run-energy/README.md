# QA captures: running with energy (ADR-085, ADR-084 amendment 2)

Captured against the working tree of branch `claude/run-energy-footwear`, on top of commit `f6f8b2f`
(`fix(server): let running keep the boots, raise the speed cap to 1.8`). The client parts (energy bar, run state, toast) were
the uncommitted changes at capture time and are committed with these files. The server binary was built from the same server
source as `f6f8b2f`.

Environment: the development server (`DOTNET_ENVIRONMENT=Development`, database `nosum_dev`, created for this capture) and the
client built with `-p:NosumDevTools=true`, served on `localhost:5080`, opened at `/?qa=1` as a guest, English (`lang=2`). The
database role is a local, test-only superuser. No file was served through a route: the build's static asset manifest lists
`assets/farmer/boots.png` (HTTP 200).

Each file is the full page at that size. Running shots: the farmer holds Shift and ArrowRight (keyboard, also at 390x844).
Out-of-energy shots: Shift held, the farmer walks left and right, and the frame is taken as the toast appears with the energy bar at 0.

| File | Screen | Shows |
|:--|:--|:--|
| `idle-1280x720.png`, `idle-1920x1080.png`, `idle-390x844.png` | Farm, nothing held | Energy bar full, not running. |
| `running-1280x720.png`, `running-1920x1080.png`, `running-390x844.png` | Farm, Shift + ArrowRight held | Energy bar in the running state (class `running`, tooltip `… · Running`). The fill blinks between two shades every half second, so a still frame shows one of them. |
| `out-of-energy-1280x720.png`, `out-of-energy-1920x1080.png`, `out-of-energy-390x844.png` | Farm, energy at 0, Shift still held | Toast `Out of energy: you stop running.` above the hotbar, and the energy gauge (third, right-hand bar) empty. |

## Checked

- All three out-of-energy captures were viewed: the toast is above the hotbar at each size, and the energy gauge is empty.
- The `running` class and the `Running` tooltip were read from the DOM during the hold at each size (capture run).
- No overlap between the energy gauge, the toast and the hotbar at any size (ui-standard section 1, measured in the capture run).

## Defects seen

- **Right menu column covers the world at the right edge.** At 1280x720 and 1920x1080 the menu column is drawn over the farmer when
  it walks right, and it cuts the farmer's name label. This is the column's existing placement, not part of this change; running
  reaches that edge sooner. Not fixed here.
- **Phone layout.** At 390x844 the energy gauge is small beside the Run and Use controls. Nothing overlaps, but the gauge is
  hard to read at that size. Not changed here.

## Not captured

- The touch Run toggle was not used for the running shot at 390x844; the keyboard path (Shift) was used at all three sizes.
- The out-of-energy toast was captured at all three sizes. The energy was drained by real running with Shift held; no QA hook sets energy, so the drain took as long as the 0.25-per-second rate requires.
