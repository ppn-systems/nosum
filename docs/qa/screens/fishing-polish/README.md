# Fishing polish: QA screenshots

- Date: 2026-10-30
- Branch: `claude/feat-fishing-polish` (commit `784beaa` at the time of the run)
- Browser: Chromium 153.0.8010.12 via Playwright `chromium.launch()` with no options
- Viewport: 1280 x 720

## Setup

- PostgreSQL 16 on localhost:5432. The role `nosum` and the database `nosum_dev` did not exist and were created (`CREATE ROLE nosum LOGIN ...`, `CREATE DATABASE nosum_dev OWNER nosum`), using the development connection string from `NosumDatabase.DevelopmentConnectionString`.
- Server: `DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host` (WebSocket on port 57300). The host applied the EF migrations at start-up; `__EFMigrationsHistory` holds 30 rows.
- Client: `dotnet run -p:NosumDevTools=true --project wasm/Nosum.Wasm` (dev server on http://localhost:5000).

## Steps

1. Opened the start screen and chose "Chơi ngay với tư cách khách" (play as a guest).
2. Rod source: the project's dev tool, not gameplay. `DevTools.Grant(500, 5, 0)` (5 wood) and `DevTools.Grant(502, 4, 0)` (4 fiber), called from the page through `getAssemblyExports`. Then crafted "Cần câu" (fishing rod, `RecipeCatalog`: 5 wood + 4 fiber) in the Chế tạo panel. The rod was placed in hotbar slot 9 and selected with the `9` key.
3. Walked south from the farm spawn with the arrow keys in 1-second holds, clicking water tiles just below the farmer until a cast was accepted (the server answered "Đang chờ cá cắn câu...").
4. Waited for the bite (the banner showed "Cá đang cắn câu!"), took the bite screenshot, then pressed Space to reel.
5. Repeated the catch when a cast returned only weeds ("Lưỡi câu chỉ kéo lên được rong rêu"); weeds give no card.
6. Reduced motion: Settings, "Hiển thị" tab, "Giảm chuyển động" switched on. `document.documentElement` then had `data-motion="reduced"`. A fresh cast and catch was taken under that setting.

## Screenshots

- `01-cast.png`: line out from the farmer to the water, banner "Đang chờ cá cắn câu...". The bobber is small at the water's edge.
- `02-bite.png`: the "!" bubble over the farmer, the green reel gauge beside the farmer, and the banner "Cá đang cắn câu! Nhấn Space hoặc bấm chuột để kéo cần!".
- `03-reel.png`: reeling, banner "Đang kéo cần...", no bubble and no gauge. Taken from a separate run, so the farmer and the pond are in a different place than in 01 and 02.
- `04-catch.png`: the catch card with the fish icon, "Cá chép", "Dài 16 cm" and "Thường gặp" (common), plus the banner "Bạn câu được Cá chép!". The size and name in the card were read from the DOM text at capture time.
- `05-reduced-motion.png`: with reduced motion on, the catch card for "Cá chép, Dài 11 cm, Thường gặp". A hoe hint tooltip ("Chọn cuốc và nhấn phím dùng để xới một ô đất") overlaps the card; this is not a fishing element.
- `05b-reduced-motion-bobber.png`: with reduced motion on, the waiting stage with the line and the bobber. The bobber is not visible at the card stage, so 05 and 05b are separate frames.

## Console errors

- The browser console showed no `console.error` or uncaught `pageerror` events during the runs.
- The server log has two `console.error` entries at start-up: "Google sign-in is off" and "Microsoft sign-in is off" (client IDs not set). These are server-side and not related to fishing.
- No fishing-related error was seen.

## Not checked

- The rarity colour of the card: only the common rarity ("Thường gặp") was seen, so the colour for other rarities was not checked.
- The bite delay was not timed precisely; the bites seen came a few seconds after the cast.
