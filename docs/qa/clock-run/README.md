# QA capture: HUD clock, minimap toggle, running

Browser QA of the uncommitted change on branch `claude/fix-clock-minimap-run`: the HUD clock now runs, the minimap can be hidden
in Settings, and holding Shift runs. Viewport 1280x720, desktop Chromium through Playwright, locale `vi-VN` (Vietnamese UI, the
default), one new guest account, default settings.

## Files

| File | What it shows |
|:--|:--|
| `clock-before.png` | Top-right calendar at the first reading (`04:46`). |
| `clock-after.png` | The same calendar 36.0 s later (`04:47`). |
| `minimap-shown.png` | The minimap card in the top-right corner, before the toggle. Same frame as `clock-before.png`. |
| `minimap-hidden.png` | After Settings > Display > "Hide the minimap in the corner" is checked and Settings is closed. |
| `running.png` | A frame 1 s into a Shift + ArrowRight run (see below). The pose was not checked visually. |

## Commands and setup

1. PostgreSQL 16 is local. The role `nosum` did not accept the password in `server/README.md` (`dev_password_not_for_production`), so
   it was left as it is. A separate role and database were created for this run with `sudo -u postgres psql`: role `nosum_qa` (login)
   and database `nosum_qa` owned by `nosum_qa`. The database `nosum_dev` and `nosum_test` were not used.
2. `dotnet build server/Nosum.Host` (succeeded).
3. `dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true -m:2 -o <scratch>/wasm-publish`.
4. Host: `DOTNET_ENVIRONMENT=Development NOSUM_CALENDAR_EPOCH=<UTC time two hours before the start> ConnectionStrings__Default=Host=localhost;Port=5432;Database=nosum_qa;Username=nosum_qa;Password=<QA role password> dotnet artifacts/bin/Nosum.Host/debug/Nosum.Host.dll`.
   `/readyz` answered `ready`; WebSocket on port 57300.
5. Client: `python3 -m http.server 5080` in `<scratch>/wasm-publish/wwwroot`.
6. Capture: a Playwright script (kept in the scratchpad, not committed) opened `http://localhost:5080/?qa=1`, clicked the guest button,
   and read `.calendar-time` and the minimap elements from the DOM. The farmer's minimap dot was read from the 2D game canvas on each
   animation frame. The script then pressed H, clicked the Display tab (`Hiển thị`), and clicked the toggle `Ẩn bản đồ nhỏ (minimap) ở góc phải`.
   Running: ArrowRight held 3 s (walk), Shift + ArrowRight held 3 s (run, screenshot 1 s in), then the same to the left.

## Observed

**Clock.** `.calendar-time` read `04:46` at the first reading and `04:47` after 36.0 s. An earlier complete run read `04:37`
then `04:38`, also 36.0 s apart. The clock advances.

**Minimap.** With the minimap shown: one `button.hud-minimap` card, no `.hud-minimap-button`. The toggle was unchecked, then checked.
With it hidden: no `button.hud-minimap` card, and one `.hud-minimap-button` (the collapsed map button of `HudMinimap.razor`).
In `minimap-hidden.png` the card is gone and the right-hand icon column starts higher, where the card was. The collapsed button's
pixel bounds were not measured.

**Running.** Rates are read from the farmer's dot on the minimap, in minimap canvas pixels per second (least-squares slope over
each 3 s hold). They are not world pixels or tiles.

| Segment | Rate (minimap px/s) | Ratio to walk in the same direction |
|:--|--:|--:|
| Walk right (ArrowRight) | +7.51 | 1.00 |
| Run right (Shift + ArrowRight) | +11.28 | 1.50 |
| Walk left (ArrowLeft) | -7.51 | 1.00 |
| Run left (Shift + ArrowLeft) | -10.73 | 1.43 |

The client-side farmer position is what the dot shows, not a server-reported position; the page does not expose positions.
The energy cost of running was not measured.

## Problems

- The `nosum` password in `server/README.md` did not work, so the `nosum_qa` role was created instead.
- Three earlier capture attempts did not finish: the first was killed by a 600 s timeout, the second stalled in the wait for the
  game canvas and was stopped, and the third failed at the toggle because the Display tab was not opened first. The five images here, and the numbers above, come from the last run, which
  finished cleanly and reported no console or page errors.
- `minimap-shown.png` and `clock-before.png` are the same frame, taken back to back before any input.
