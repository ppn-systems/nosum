# Browser proof: NPC, quest, journal and HUD (before and after)

Captured by [`tests/browser/rebuild-npc-hud-capture.cjs`](../../../tests/browser/rebuild-npc-hud-capture.cjs), one run per
client and size. The review is [`docs/design/critique-npc-hud.md`](../../design/critique-npc-hud.md).

- Host: `DOTNET_ENVIRONMENT=Development`, `NOSUM_CALENDAR_EPOCH` set five and a half hours before the run (about 11:00 in
  spring, day 1), PostgreSQL 16.
- Client: `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, served as static files. `before/` is `master`,
  `after/` is this branch.
- Phone: an iPhone 14 profile at 390×844, so the touch controls show (`Device:BlockTouchDevices` is `false`).
- Each run: a new guest account; the HUD; the journal (All, then Found and Missing where the build has them); the seed keeper's
  greeting and two Talk lines. Vietnamese, then English (the language switched on the settings page, H).
- Console errors and page errors: none in any run (`tour-*.json`, `errors: []`).

## Files

| Folder | Sizes | Content |
| :-- | :-- | :-- |
| [`before/`](before/) | 1280×720, 1920×1080, 390×844 | `hud-farm`, `journal-all`, `npc-greeting`, `npc-talk1`, `npc-talk2` in vi and en; `tour-*.json` |
| [`after/`](after/) | the same, plus `journal-found` and `journal-missing` | the same, from this branch; `tour-*.json` |

Names are `<step>-<lang>-<WxH>.png`.

## What changed between the two

| Frame | Before | After |
| :-- | :-- | :-- |
| `hud-farm-vi-1280x720` | the tracker shows "Xới một ô đất 0/1" and the reward | the tracker also shows the tip "Chọn cuốc và nhấn phím dùng để xới một ô đất." |
| `journal-all-vi-1280x720` | one grid, 212 items, six pages | the same grid under three tabs (Tất cả, Đã có, Chưa có) |
| `journal-found-vi-*` | — | only the eight obtained items |
| `journal-missing-en-390x844` | — | the missing items, English tabs, pager "Page 1 of 6" |
| `hud-farm-en-390x844` | the tracker is one line and the reward | the tip wraps to two lines inside the card (the card's maximum width is 120 `--u`) |

## Grading against `ui-standard.md` section 7

Graded from the frames by the author of the change, not by a second reviewer. A "no" would have been a reason not to merge.

| # | Check | 1280×720 | 1920×1080 | 390×844 |
| :-- | :-- | :-- | :-- | :-- |
| 1 | Every element is in its region (section 2) | yes: tracker in top-left; journal in the centre | yes | yes: tracker under the calendar row |
| 2 | Nothing overlaps (regions, panels, toasts, hotbar, touch) | yes | yes | yes, after the fix (the first version of the tip ran under the Menu button; see the critique, finding 7) |
| 3 | Menu column: one column, at most eight buttons | yes (unchanged) | yes | collapsed to Menu (unchanged) |
| 4 | Edges line up | yes | yes | yes |
| 5 | Panels use `GamePanel` with an S/M/L/XL width | yes: the journal keeps its panel; the tabs fit inside it | yes | yes |
| 6 | Only the three font sizes; no word-by-word wrap; nothing clipped | yes: the tip is body text and wraps at word boundaries | yes | yes: the tip wraps to two lines |
| 7 | No inner scrollbar on desktop | yes | yes | n/a (the journal page fits) |
| 8 | Item icons whole and centred | yes | yes | yes |
| 9 | Spacing in `--u` multiples and gap tokens | yes | yes | yes |
| 10 | Vietnamese and English both fit | yes: vi and en tips both fit | yes | yes: en "Select the hoe and press the use key to till a patch of soil." wraps to two lines |

Known and not fixed: finding 5 in the critique (the name plate and the direction label stack over the player). It is
world-anchored, drawn by the engine, and outside the HUD regions, so it is not a checklist failure of this change.

## Not covered

- A second in-game day, the mine, and a finished quest chain (the first step only).
- A second merchant's full talk cycle; the seed keeper's three lines only.
- Sustained play; the repetition claim in the critique is reasoned from the line rule, not observed.
