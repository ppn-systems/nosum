# QA screenshots: season label, season banner and the Skills page (issue #248)

Browser QA of the season fix (docs/adr/ADR-079 amendment D5) and the fishing skill row (docs/adr/ADR-078 amendment, protocol 23).
Captured by `tests/browser/season-fix-capture.cjs`: a guest signs in with `?qa=1`, `window.nosumQa.season(1)` passes one season, and the
script watches the calendar label and the banner for up to 20 s, then opens the Skills page.

## Files

| File | What it shows |
|:--|:--|
| `before-skills-panel-<size>.png` | Skills page on the baseline build (`08740b5`): four skill rows, no Fishing. |
| `before-results.json` | Baseline run: the label stayed on the old season and no banner appeared at any size. |
| `after-season-banner-<size>.png` | Fixed build: the banner "MÙA MỚI / Mùa … đã đến!" about 0.3 s after the season pass. The calendar line reads the new season. |
| `after-skills-panel-<size>.png` | Fixed build: Skills page with Fishing ("Câu cá") as the fifth row, after Combat. |
| `after-results.json` | Fixed run: label and banner both changed in about 280 to 350 ms at each size; Fishing is row five. |

Sizes: 1280x720, 1920x1080 and 390x844 (touch emulation).

## Reproduction

- Baseline: a worktree at `08740b5`, built with `dotnet build server/Nosum.Host` and
  `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`.
- Fixed: the branch `claude/fix-season-update`, same commands.
- Server: `DOTNET_ENVIRONMENT=Development dotnet artifacts/bin/Nosum.Host/debug/Nosum.Host.dll`, PostgreSQL 16, database `nosum_dev`, role `nosum`.
- Client: the published `wwwroot` served with `python3 -m http.server 5080`. `Server:Url` in its `appsettings.json` points at
  `ws://localhost:57300/ws/`.
- Scratch-only change for the 390x844 captures: `Device:MinimumWidth` set to 360 in the published `appsettings.json`. The committed value
  stays 1024, so a desktop window narrower than 1024 px shows the "widen the window" overlay, as designed. Without the override the 390 px
  capture would show that overlay, not the touch layout.
