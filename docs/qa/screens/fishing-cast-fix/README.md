# Fishing cast fix: QA screenshots

- Date: 2026-10-31
- Branch: `claude/fix-fishing-cast`, built from the fix commit (the code under test)
- Browser: Chromium via Playwright `chromium.launch()` with no options
- Viewport: 1280 x 720, device scale 1

## Setup

- PostgreSQL 16 on localhost:5432; role `nosum` and database `nosum_dev` created for the development connection string.
- Server: `DOTNET_ENVIRONMENT=Development dotnet run --no-build --project server/Nosum.Host` (WebSocket on port 57300).
- Client: `dotnet run -p:NosumDevTools=true --project wasm/Nosum.Wasm` (dev server on http://localhost:5000), opened as `/?qa=1`.

## Steps

1. Chose "Chơi ngay với tư cách khách" (play as a guest).
2. Rod source: the QA hook, not gameplay. `nosumQa.grant(40, 1, 0)` gave the fishing rod (item 40), which landed in hotbar slot 9. Slot 9 was selected with the `9` key.
3. `nosumQa.teleport('farm', 66, 42)` put the farmer on their own farm, beside the pond (the pond spans tiles x 59–73, y 44–54).
4. Held `ArrowDown` for about 450 ms so the farmer stood at the pond's edge.
5. Clicked the water at (560, 500) for the cast. The banner read "Đang chờ cá cắn câu..." (waiting for a bite) and the bobber sat on the water.
6. `nosumQa.fishingBite()` made the line bite at once. The reel press (`Space`) followed a fraction of a second later, inside the window. A press about 1.5 s after the cast escaped, so the press must be quick.
7. A weed result gives no card; the cast was repeated until a fish came up ("Bạn câu được Cá chép!").

## Screenshots

- `01-cast.png`: line out, banner "Đang chờ cá cắn câu...", bobber at the pond's edge.
- `02-bite.png`: bite cue, banner "Cá đang cắn câu! Nhấn Space hoặc bấm chuột để kéo cần!", the "!" bubble over the farmer. The banner is over the pond, which is the case the fix covers (see the cause below).
- `03-catch.png`: catch card "Bạn câu được cá! Cá chép, Dài 24 cm, Thường gặp" and the banner "Bạn câu được Cá chép!".

Two tutorial hints ("Chọn cuốc…" and "Nhấn E để dùng vật đang cầm…") are drawn in the frames. They are not fishing elements.

## Before and after the fix

Before the fix, the fishing banner's panel took the mouse click over the lower part of the pond (checked with `document.elementFromPoint`: the points at y 540–580 resolved to `.game-panel` inside `.fishing-banner`). A click there never reached the canvas, so no cast was sent. With the fix the same points resolve to the canvas, and a click on that water during the result banner's 4-second linger reached the server (the server answered `TooFar` for a point that was about 4.5 tiles away).

## Not checked

- Other viewport sizes and devicePixelRatio above 1.
- Co-op land and the Beach and Woods ponds; only the farm pond was exercised.
