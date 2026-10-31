# Browser proof: NPC dialogue and seasonal stock (ADR-080)

Screenshots of every merchant's dialogue box in Town, in Vietnamese and English, and of the carpenter's Buy page. The rule is in
[ADR-080](../../adr/ADR-080-npc-dialogue-lines-and-seasonal-stock.md); the capture is [`tests/browser/npc-dialogue-capture.cjs`](../../../tests/browser/npc-dialogue-capture.cjs).

## What was run

- Host: `DOTNET_ENVIRONMENT=Development` with `NOSUM_CALENDAR_EPOCH` set five and a half hours before the run, so the game clock read about 11:00 in spring,
  PostgreSQL 16 (role `nosum`, database `nosum_dev`).
- Client: `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true` (the ADR-079 hook), served as static files on port 5080.
- Chromium through Playwright, a new guest account per run (a newcomer: no quest finished). The farmer is moved by `window.nosumQa.teleport` onto each
  counter; the action key (E) opens the box; a click on the line finishes the typing; the screenshot is taken; Escape ends the conversation.
- The language is switched on the settings page (H), as a player does. Both languages are in the same run.

## Result

| Merchant | Line (vi) | Buttons (vi) | Line (en) | Buttons (en) |
|:--|:--|:--|:--|:--|
| Seed keeper | Mùi đất ẩm là mùi tôi thích nhất đấy. | Mua · Trò chuyện · Tạm biệt | Damp soil is my favourite smell. | Buy · Talk · Leave |
| Tailor | Thử đồ rồi hãy mua nhé, vừa người thì mặc mới thoải mái. | Mua · Trò chuyện · Tạm biệt | Try things on before you buy. A good fit is what makes it comfortable. | Buy · Talk · Leave |
| Furniture seller | Một chiếc đèn và một chậu cây là căn phòng khác hẳn ngay. | Mua · Trò chuyện · Tạm biệt | One lamp and one plant, and the room looks completely different. | Buy · Talk · Leave |
| Blacksmith | Sáng sớm tôi bắt đầu đập sắt, tiếng búa vang cả xóm. | Mua · Nâng cấp · Trò chuyện · Tạm biệt | I start hammering at dawn, and the whole street hears it. | Buy · Upgrade · Talk · Leave |
| Carpenter | Chào bạn! Bác là Lâm. Muốn nuôi gà hay nuôi bò thì cứ hỏi bác. | Mua · Xây dựng · Trò chuyện · Tạm biệt | Welcome! I'm Lam. Want to keep hens or a cow? Just ask me. | Buy · Build · Talk · Leave |
| Trader | Chào bạn! Tôi là Phát. Chợ này mở cửa cho tất cả mọi người. | Mua · Bán · Trò chuyện · Tạm biệt | Welcome! I'm Phat. This market is open to everybody. | Buy · Sell · Talk · Leave |
| Board keeper | Cả làng góp sức thì mục tiêu nào cũng xong sớm hơn. | Mục tiêu · Trò chuyện · Tạm biệt | When the village pitches in, every goal gets done sooner. | Goals · Talk · Leave |

- The carpenter's Buy page (`carpenter-shop-vi.png`, `carpenter-shop-en.png`) lists the supplies (hay, milk pail) and the coop and barn blueprints. It fits 1280×720 without a scrollbar. The animal section is on the Build action (`Xây dựng` / `Build`), which is not captured in this run.
- Every box shows the merchant's name and role, a line, and one button per action the merchant offers, then Leave.
- Console errors, page errors and failed requests during the run: none (`lines.json`).

Files: `<merchant>-vi.png`, `<merchant>-en.png` and `carpenter-shop-*.png` at 1280×720 (this folder); the same set at 1920×1080 in [`1920x1080/`](1920x1080/).
`lines.json` holds the text and buttons read from each box.

## Not covered

- **390×844 (touch).** The game's device gate (`DeviceGuard`, `wasm/README.md`) replaces the page with a notice on a phone-sized screen, so no merchant can be
  opened there and no capture was taken. The phone check of ui-standard §7 does not apply to this screen until the gate changes.
- **Animal rows.** They are listed only inside a building the player owns (ADR-028), and the Build page carries that section. The QA account owns no building, so
  the animal rows never show here; the Build page itself is not captured in this run.
- **"First visit".** The newcomer line is chosen while no quest is finished (ADR-080 D3). A true first visit would need a visit record, which does not exist.
- **Later lines.** The run shows one line per merchant (the greeting). Talk cycles through the same pool; the order is covered by `NpcGreetingTests`, not by
  screenshot.
