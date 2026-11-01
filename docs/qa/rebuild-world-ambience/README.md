# Captures: rebuild-world-ambience

Before and after frames for [`docs/design/critique-world-ambience.md`](../../design/critique-world-ambience.md).
Same scenarios, same maps, same calendar times on both builds, so each pair differs only by the change.

## Layout

| Folder | What |
|:--|:--|
| `before/en/1280x720`, `before/en/390x844` | master at 384cb1d, English: six calendar states × five maps |
| `after/en/...` | this branch, English, same scenarios |
| `before/vi/...`, `after/vi/...` | Vietnamese: three states (noon sun, night rain, summer storm) × five maps |
| `report-<state>-<locale>.json` | per frame: the HUD calendar text the page showed, the audio buffers started during the visit (duration, looped or once), and any page errors (none recorded) |
| `minimap-town-before-after-4x.png` | the town minimap, zoomed 4× (left before, right after) |

File names: `<state>-<map>.png`. Maps: `farm`, `town`, `beach`, `forest`, `mine1` (mine floor 1).

## States

| State | Calendar text in the reports | Epoch (`NOSUM_CALENDAR_EPOCH`) |
|:--|:--|:--|
| `noon-spring-sun` | Spring Day 1 12:00 PM | `2026-11-01T10:43:39.000Z` |
| `noon-spring-rain` | Spring Day 5 12:09 PM | `2026-10-30T10:40:40.000Z` |
| `dusk-spring-sun` | Spring Day 1 7:12 PM | `2026-11-01T07:10:40.000Z` |
| `night-spring-rain` | Spring Day 6 10:15 PM | `2026-10-29T17:40:40.000Z` |
| `storm-summer-storm` | Summer Day 24 3:18 PM | `2026-10-06T21:10:40.000Z` |
| `snow-winter-snow` | Winter Day 5 12:21 PM | `2026-09-18T10:40:40.000Z` |

The epochs are relative to the moment of capture: the game clock is `(now - epoch)` on a 12-hour day, and the weather is a
pure function of the epoch and the day (`WeatherRules.ForDay`). To reproduce a state later, pick an epoch with the same minute of day
and the same weather for that day again. Fall is not in this set.

## Reproduce

Postgres on `localhost:5432` (database `nosum`), the Development server (`server/Nosum.Host`), and the client
built with `-p:NosumDevTools=true` on `:5080`. Then, per state:

```sh
EPOCH=<epoch above> SCENARIO=<state> OUT_DIR=docs/qa/rebuild-world-ambience/after LOCALES=en \
  bash tests/browser/world-ambience-run.sh
```

`LOCALES=vi` for the Vietnamese set. The script restarts the server with the epoch, then runs
`tests/browser/world-ambience-capture.cjs`.

## Grading

See section 4 of the critique: the HUD layout and the minimap card are unchanged between before and after; the
changes are the tint at night in rain, the storm's streaks, and the merchant dot on the minimap. Listening was not
possible in a headless browser; the audio columns are event logs, and the loudness numbers are measured from the files.
