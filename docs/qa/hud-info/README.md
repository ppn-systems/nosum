# HUD info upgrades: before and after (backlog items 1, 3, 4, 7)

Source: [`docs/design/research-hud-and-loops.md`](../../design/research-hud-and-loops.md), section "Prioritised backlog", items 1, 3, 4 and 7.
Rules: [`docs/design/ui-standard.md`](../../design/ui-standard.md).

- **Before** = `origin/master` at `fd9cba9`. **After** = this branch, `claude/hud-info-upgrades`.
- Captured by [`capture-hud-info.cjs`](capture-hud-info.cjs) against a local server (`server/Nosum.Host`, PostgreSQL 16) and a
  client published with `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, served as static files.
- Layout: `before/` and `after/`, then `vi/` and `en/`, then `1280x720/`, `1920x1080/`, `390x844/`. Each language folder has a
  `capture-report.json` that lists what was captured and what was not.

## What changed

| Item | Change | Files |
|:--|:--|:--|
| 1. Toast queue | Up to two toasts stacked, newest last; each removed after 3 s and fades over its last fifth. A third pushes out the oldest. Stays in the bottom-centre region above the hotbar. | `GameMessagesViewModel.cs`, `GameMessage.razor`, `GameHud.razor.css` |
| 3. Energy tooltip | Tooltip adds "About N tool uses left" for the tool in hand: energy divided by `EnergyRules.Cost` at the skill's level, the same cost the server charges. No line for the scythe or no tool. | `ProgressViewModel.cs`, `InventoryViewModel.cs`, `EnergyBar.razor`, `Hotbar.razor` |
| 4. Shop | Out-of-season seeds stay listed, greyed, with the reason in the price cell ("Plants in Spring"). They cannot be selected or bought; `Buy` refuses them before a request. The confirm box already showed the total and the balance after. | `ShopViewModel.cs`, `ShopPanel.razor` |
| 7. Today card | A click or tap on the calendar bar opens a "Today" window in the centre: season and day, weather today and tomorrow, crops in season. | `CalendarClock.razor`, `TodayPanel.razor`, `CalendarViewModel.cs`, `HudTopRightBar.razor`, `GameMenuWindow.razor`, `ToolbarViewModel.cs` |

All visible text is in `Language.resx` (vi) and `Language.en.resx` (en). The wire and the server are unchanged: no opcode, no packet,
no `NosumProtocol.Version` bump. The client estimate and the greyed rows do not decide anything; the server still refuses.

## Captures

| Screen | Before | After | Note |
|:--|:--|:--|:--|
| Idle HUD | yes | yes | baseline |
| Toast, one refusal | yes | yes | |
| Toast, two refusals stacked | no (master shows one) | yes | master replaces the first message |
| Energy tooltip, hoe in hand | yes | yes | after: "About 90 tool uses left" at 270 energy |
| Today card | no (no card on master) | yes | |
| Shop, out-of-season seeds | **not captured** | **not captured** | see below |

**Shop screens are not captured.** The shop opens from a merchant on the map. `capture-hud-info.cjs` does not walk there. The
behaviour is covered by unit tests instead: `tests/Nosum.UnitTests/Wasm/ShopSeasonRowsTests.cs` (greyed row, reason text,
no buy request) and the existing `ShopViewModelTests` and `ShopPagingTests`. A manual shop capture at the three sizes is still owed.

## Self-check against the 10-point checklist (ui-standard section 7)

Checked on the after captures in English and Vietnamese at 1280x720, 1920x1080 and 390x844.

1. **Regions.** Toast line sits in bottom-centre above the hotbar; the Today card is a centre window; the energy tooltip is a tooltip. No new floating card over the world. **Yes.**
2. **No overlap.** Two toasts stack above the hotbar without touching it (1280x720 checked). The Today window is the only centre item. **Yes**, for the captured states. The shop row is not captured.
3. **Menu column.** Unchanged: still eight or fewer buttons, no new column button. **Yes.**
4. **Edges.** The calendar bar, minimap and menu column still share their right edges; the Today card is a centred window. **Yes.**
5. **Panels.** The Today card is a `TradeDialog` through `GameMenuWindow` with the shared title bar and close button, size M. **Yes.**
6. **Fonts and wrapping.** Only the existing three font sizes are used; no inline sizes added. The crop list wraps at phrase boundaries, not word by word, in the 160 u panel. **Yes**, checked in English.
7. **No inner scrollbar** at 1280x720 and up for the Today card. **Yes.**
8. **Icons whole and centred.** Weather icons are the existing `WeatherIcon` sprites. **Yes.**
9. **Spacing.** New CSS adds only a cursor and a keyframe; no new margins or padding. **Yes.**
10. **Vietnamese and English fit.** Checked both languages on the captured screens. The Vietnamese crop line is longer and fits the panel. **Yes**, for the captured screens. The Vietnamese shop reason has not been checked on a screenshot.

## Known gaps

- The shop screens (item 4) need a manual capture at the three sizes, in both languages.
- `GameHud.razor` lists the nine regions. The bottom-left slot holds the chat line here, while `ui-standard.md` section 2 names the touch
  joystick for it. The touch controls are mounted by the page, outside the grid. This drift is not changed by this branch.
