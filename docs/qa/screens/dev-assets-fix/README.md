# Guest sign-in after the dev-asset fix

Re-run of the master smoke test (`docs/qa/screens/master-smoke/`) on the branch that fixes the linked art, the Controls theme
sheet and the favicon. Chromium at 1280 x 720 with the default (Vietnamese) UI, service workers blocked so a cache cannot hide a
404, the host in `DOTNET_ENVIRONMENT=Development` with PostgreSQL 16 (`nosum_dev`).

| Check | `dotnet run` client | Published client (`dotnet publish -c Release`) |
|:--|:--|:--|
| Guest sign-in reaches the farm | Yes, about 7 s (`01`) | Yes, about 4 s (`02`) |
| HTTP responses of 400 or more | None | None |
| Failed requests | None | None |
| Console and page errors | None | None |
| Theme sprites cut from `ui/menu.png` and the other sheets | 64 of 64 | 64 of 64 |
| `/favicon.ico` | 200, the tomato sprite | 200, the tomato sprite |

Before the fix, the `dotnet run` client stalled on "Đang vào thế giới…" and returned empty bodies for the maps
(`master-smoke/dev-run-stall-*`); `_content/Nosum.Wasm.Controls/ui/menu.png` was 404 in both runs.
