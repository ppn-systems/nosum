# QA: animals and pets, round four (carpenter names, layout, chicken payback)

Decision records: [ADR-107](../../adr/ADR-107-carpenter-building-names-and-animals-tab.md) (names, the Animals tab),
[ADR-081](../../adr/ADR-081-economy-balance-targets.md) round-4 amendment (the chicken's egg and price, the five-season limit) and
[ADR-092](../../adr/ADR-092-animal-produce-cue-action-preview-and-pet-forage.md) round-4 amendment (the pet's mood, proposed and not applied).
Critique: [`docs/design/critique-animals-pets.md`](../../design/critique-animals-pets.md) findings 6, 7 and 8.

- Script: [`tests/browser/animals-barn-capture.cjs`](../../../tests/browser/animals-barn-capture.cjs). One guest per run; `BUILDINGS=coop` or `barn`,
  `LANG_CODE=en|vi`, `VIEWPORT=WxH`. Each run writes `carpenter-buildings.png`, `carpenter-animals.png` (the Animals tab; on the before build the
  Build page has no tab, so this frame is the whole page) and `carpenter-animals-after.png` (after buying a cow and a goat), plus `report-*.json`.
- Builds: client `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, served statically. Before = `origin/master` at `6a2c64e`; after =
  this branch. Server: `DOTNET_ENVIRONMENT=Development`, one server build per side; each run starts a fresh server, so the clock starts at spring day 14 and
  the first farm tiles are the same in every run.
- Frames: `before/<viewport>-<lang>/<coop|barn>/` and `after/<viewport>-<lang>/<coop|barn>/`. Viewports 1280x720, 1920x1080 and 390x844; languages en and vi.
  Twelve runs per side, 24 in all.

## What changed

| # | Item | Before | After |
|:--|:--|:--|:--|
| 1 | The duck row | "Chuồng gà (2/4)" (the chicken coop's name on every row) | "Chuồng gia cầm (2/4)": the poultry house, over the chicken and the duck |
| 1 | The barn's rows | "Chuồng bò (0/4)" over the goat, sheep and pig | "Chuồng gia súc (0/4)": the livestock barn, over the cow, goat, sheep and pig |
| 1 | English | "Coop" over the duck; "Barn" repeated on every row | "Poultry coop" over the chicken and duck; "Barn" once over the four |
| 2 | Layout at 1280 x 720 | All on one tab: the pig row cut below the panel | An Animals tab: name field, one group per building; nothing cut or scrolled |
| 2 | Blueprint rows | With the longer English name, "Buy blueprint" wrapped onto its own line | Name stacked over its cost; the button stays on the first line |
| 3 | Chicken | Egg 15, price 800: fed payback **9.5 seasons** | Egg 20, price 950: fed payback **4.2 seasons** |
| 3 | Every animal | Not tested | Fed payback within five seasons, locked by `EconomyBalanceTests.Animal_FedPaybackOverASeason_IsWithinFiveSeasons` |
| 3 | Pet mood | No effect | No effect; the proposal with numbers is in ADR-092 (energy `3 + mood / 25`), for the owner |

## Layout, measured

From the `report-*.json` files: `bodyScrollHeight` against `bodyClientHeight` on the carpenter's panel body, and the buttons whose bounds fall
outside the panel frame (`buttonsCut`).

| Viewport | Before, barn (Build page, one tab) | After, barn (Animals tab) | After, coop (Animals tab) |
|:--|:--|:--|:--|
| 1280 x 720 en | 6 buttons; pig row cut; 518 px body | 4 buttons; no scroll; none cut | 2 buttons; none cut |
| 1280 x 720 vi | same; pig row cut | 4 buttons; none cut | 2 buttons; none cut |
| 1920 x 1080 en | sheep and pig rows cut | 4 buttons; none cut | 2 buttons; none cut |
| 1920 x 1080 vi | sheep and pig rows cut | 4 buttons; none cut | 2 buttons; none cut |
| 390 x 844 en | 6 buttons; none cut | 4 buttons, two to a row; none cut | 2 buttons; none cut |
| 390 x 844 vi | 6 buttons; none cut | 4 buttons; none cut | 2 buttons; none cut |

The 1280 x 720 "after" barn body is 314 px against a 314 px client, so nothing scrolls. Where the table says "two to a row", that is from the frame. The panel body had `overflow: visible`, so the "before" cut
is measured against the panel frame, not against a scroll bar.

## Grading (docs/design/ui-standard.md section 7)

Frames viewed: after 1280 x 720 en and vi (barn), after 1280 x 720 vi (coop), after 1280 x 720 en (blueprints), after 390 x 844 en (barn),
after 1920 x 1080 en (barn), before 1280 x 720 vi (barn). The other frames were checked by their `report-*.json` (buttons, cut, scroll).

| # | Item | Result |
|:--|:--|:--|
| 1 | In its region | Yes: the carpenter is a centre `GamePanel` |
| 2 | Nothing overlaps | Yes in the frames viewed: the panel stays clear of the HUD and the hotbar |
| 3 | Menu column | Not part of this change |
| 4 | Edges line up | Yes: the group buttons share their left edge; the blueprint buttons share the right edge |
| 5 | `GamePanel`, S/M/L | Yes: the panel and title bar are unchanged |
| 6 | Text, three sizes, no clipping | Yes: the name stacks over its cost (no word-wrap); no button text is cut |
| 7 | No inner scrollbar at 1280 x 720 and above | **Yes** (before: the barn rows overflowed at 1280 x 720 and 1920 x 1080) |
| 8 | Icons | Not part of this change |
| 9 | Spacing in `--u` | Yes: the group and button gaps are `--u` multiples |
| 10 | Vietnamese and English both fit | Yes: "Chuồng gia súc (0/4)" and "Mua Heo · 2.000 xu" fit; "Poultry coop" and "Buy Sheep · 1,600 coins" fit |

Grade, on the frames viewed and the measurements above: the carpenter's animal section passes items 1, 2, 4, 5, 6, 7, 9 and 10. Item 7 failed before (the
barn's rows overflowed at 1280 x 720 and 1920 x 1080) and passes after at all three sizes. Items 3 and 8 do not apply to this change.

## The economy, from the simulation

The report is [`economy/economy-report-after.txt`](economy/economy-report-after.txt) (`NOSUM_ECONOMY_REPORT`, `EconomyReportTests`, now with the
`fed_payback_seasons` column). The "before" chicken row is computed from the same formula with the old values (price 800, egg 15).

| Animal | Price | Season product | Fed season net (hay 12 a night) | Fed payback, seasons (before → after) |
|:--|--:|--:|--:|--:|
| Chicken | 800 → **950** | 420 → **560** | 84 → **224** | 9.5 → **4.24** |
| Duck | 1,100 | 560 | 224 | 4.91 |
| Cow | 1,500 | 756 | 420 | 3.57 |
| Goat | 1,800 | 896 | 560 | 3.21 |
| Sheep | 1,600 | 720 | 384 | 4.17 |
| Pig | 2,000 | 980 | 644 | 3.11 |

Herd: 8,800 → 8,950 coins of animals; 156 → 160.7 coins a game day. Share of price the everyday product pays in a season: chicken 52.5 → 58.9 %,
inside the 40 to 60 % band of ADR-081 D4.

The duck is now the slowest animal (4.9 seasons). It is inside the limit; whether the owner wants it lower is an open question in ADR-081.

## Not covered

- **Pet mood.** Not changed in code. The numbers are proposed in ADR-092; the mood still has no effect, and a data retune cannot give it one.
- **Placement is fragile in this environment.** The coop's tile was clear in every run. The barn's first candidate tiles were blocked by trees or weeds
  in some server instances (the trees differ from one start to the next), so the script tries a list of tiles and reads the hotbar's slot 9 to see
  whether a blueprint left the hand. A run whose `placedAt` is missing or whose buttons list is empty did not place its building; none of the 24 runs
  in the final matrix did that.
- **One building per guest.** A second building placed in the same guest was refused in this flow, so each capture has one building. The coop frames
  show the chicken and duck rows; the barn frames show the cow, goat, sheep and pig rows.
- **Server.** The "before" side ran a server built from `origin/master`, the "after" side one built from this branch. The only server difference is the
  chicken's price, and no chicken is bought in these captures.
- **Vietnamese pet and tab labels** are checked in the frames but not graded line by line; the 1920 x 1080 and 390 x 844 frames were checked by
  their buttons list, not by eye, except for the English 1920 x 1080 barn and the 390 x 844 English barn.
