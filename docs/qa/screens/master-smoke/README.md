# Master smoke test: guest sign-in to the farm

Base: `master` at `95b9906`. Run on 2026-10-30 in a container with PostgreSQL 16 (database `nosum_dev`), the host in `DOTNET_ENVIRONMENT=Development`, Chromium at 1280 x 720 with the default (Vietnamese) UI.

## Result

| Check | Published client | `dotnet run` client |
|:--|:--|:--|
| Guest sign-in reaches the farm | Yes, about 1.1 s | No: stalls on "Đang vào thế giới…" |
| Quest tracker, calendar, hotbar, HUD | Rendered (`02`, `03`) | Not drawn |
| Emote button `[B]` and emote wheel | Works (`04`, `05`) | n/a |
| Pet panel (cat or dog adoption form) | Works (`06`) | n/a |
| Fishing rod in the hotbar | **Not captured** | n/a |

The fishing rod is not in the starter kit. It is crafted from 5 wood and 4 fiber (`RecipeCatalog`, recipe 29), and this run did not gather those materials.

## Stall in the `dotnet run` client

Symptom: after guest sign-in the loading screen never closes. Every map fails with `Xml_MissingRoot`, and the page shows "An unhandled error has occurred" (`dev-run-stall-1280x720.png`, `dev-run-stall-console.txt`).

Cause: the wasm project links its sprites, farmer sheets and TMX maps from `assets/` and `art/own/Maps/` into `wwwroot` through `<Content Link="wwwroot\…">` items. A Development build does not copy them into `wwwroot`, and the static-asset manifest still points at the missing files. The dev server answers each one with 200 and an empty body, so the TMX parser gets nothing. `dotnet publish` copies the files, and the published client works.

The host is fine: its log shows the player joined `farm@…`, so the stall is in client asset delivery. The pattern dates from cd9e0ec, so it is not one of the recent merges (#231 to #235). The project file is unchanged in this PR.

## Other findings

- `_content/Nosum.Wasm.Controls/ui/menu.png` returns 404, and the theme falls back to plain sprites (both runs).
- `/favicon.ico` returns 404.
