# QA screenshots: storm flash and season banner

Browser QA run of the lightning flash (`SkyLayer.DrawLightning`) and the season banner (`SeasonBanner.razor`) on branch
`claude/feel-audio-feedback` (code commit `92fbf4e`). Viewport 1280x720, Chromium through Playwright, default settings
(motion not reduced), UI language Vietnamese, one guest account per run.

## Files

| File | What it shows |
|:--|:--|
| `storm-flash.png` | The game canvas (1280x720, no DOM overlay) captured with the call starting about 100 ms after the client's flash onset. The frame is lighter than the plain one (mean luminance +66), but the flash is a partial white wash over the storm-tinted sky, so it reads in the numbers more than at a glance. Rain streaks visible. |
| `storm-flash-plain.png` | The same canvas about 1.5 s before the client's flash onset of the next strike: plain storm frame, no flash. |
| `season-banner.png` | Full page (1280x720) about 1 s after the banner appeared: the top banner "MÙA MỚI" / "Mùa Hè đã đến!" over the farm, calendar reading "Hè, Ngày 1, 00:00". |

## Commands and epochs

- Build: `dotnet build server/Nosum.Host` (output `artifacts/bin/Nosum.Host/debug/Nosum.Host.dll`); client
  `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true -o <scratch>/wasm-publish`, served with
  `python3 -m http.server 5080` from its `wwwroot`.
- Server: `DOTNET_ENVIRONMENT=Development NOSUM_CALENDAR_EPOCH=<epoch> dotnet artifacts/bin/Nosum.Host/debug/Nosum.Host.dll`,
  PostgreSQL 16, database `nosum_dev` (role `nosum`).
- Run 1 (flash): `NOSUM_CALENDAR_EPOCH=2026-10-14T07:15:07.898Z`, server started 23:45:55 UTC. Chosen by a scratch probe that
  calls `WeatherRules.At` and `LightningRules.StrikeAtMs`: at that epoch the server clock falls on Summer, day 32 of the
  calendar (day 4 of the season), Storm, 4.5 game hours in.
- Run 2 (banner): `NOSUM_CALENDAR_EPOCH=2026-10-15T23:58:45.420Z` (now minus 14 days plus 120 s), server started 23:56:45 UTC.
  The season turns from Spring to Summer at 23:58:45.42 UTC.

## Server time, season and weather

- Run 1: the screenshots were taken between 23:47:52 and 23:56:14 UTC on 2026-10-29 (strike windows at 23:47:54 and
  23:55:48). The calendar read Summer, "Hè Ngày 4", 09:00 to 09:20. Today's weather in the calendar widget was Storm
  ("Hôm nay: Bão"), tomorrow Sun.
- Run 2: the banner appeared at 23:58:46.49 UTC. The calendar read "Xuân" before the turn and "Hè, Ngày 1, 00:00" after it.
  Weather on day 1 of a season is always Sun (`WeatherRules.SunnyDaysAtSeasonStart`).

## Storm flash: measurements

Mean luminance of the canvas PNG (Rec. 601 luma, 0 to 255, full 1280x720 canvas):

| Frame | Mean luminance |
|:--|:--|
| Plain, before the strike (`storm-flash-plain.png`) | 116.88 |
| Flash, call starting 101 ms after the client onset (`storm-flash.png`) | 182.84 |
| Flash, call starting 190 ms after the client onset | 145.10 |
| Earlier attempt at server strike + 100 ms, no onset correction (not kept) | 116.83, plain 116.82 (no flash yet) |

The flash frame is clearly brighter (+66 on the plain frame). A screenshot call takes about 110 to 130 ms, so the exact frame
time inside the call is not known; the +100 ms frame is the call that starts 101 ms after onset.

Per-frame canvas samples (16x16 centre block, read in `requestAnimationFrame`) show the flash shape of
`LightningRules.FlashAlpha`: a bump peaking about 30 ms after onset and a second bump peaking about 170 ms later, each
roughly 110 ms wide, ending in the expected baseline (137).

**Timing finding.** The flash appears late against the server strike time on this single machine. The onset measured
from the canvas was about 862 ms (diagnostic session, two strikes), about 985 ms (second session, one strike) and 911 ms
(the session used for the screenshots), after `LightningRules.StrikeAtMs`. The screenshots were aimed at the measured onset. The cause was not isolated; the
prime candidate is `CalendarChannel.LoadAsync`, which sets the client clock offset once from the calendar reply without
accounting for the time the reply took (`wasm/Nosum.Wasm.Socket/Calendar/CalendarChannel.cs`).

## Season banner: observations

- Title "MÙA MỚI", message "Mùa Hè đã đến!" (the Vietnamese default). The banner appeared 1.07 s after the server turn, within
  the 5-second calendar refresh.
- Screenshot taken 1.0 to 1.1 s after the banner appeared, while it was still on screen.
- The banner text is small at 1280x720: `--u` is 3 px (`nosum-theme.css`), so the title is about 10.5 px and the message
  about 7.5 px. It is legible when zoomed, but small for a headline.
