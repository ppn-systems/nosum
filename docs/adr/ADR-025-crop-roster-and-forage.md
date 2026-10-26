# ADR-025 — Crop roster, multi-harvest crops and forage

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M2. Builds on ADR-018 (seasons) and ADR-021
(growth in watered game days, ground drops).

## Context

Three spring crops cannot carry a two-week season, let alone a year. The early sheets (ADR-007) carried art for many more crops, seeds and forage items.

## Decision

- **D1 — Roster (data only).** Spring: parsnip, radish*, cauliflower, potato, green bean (regrows), kale, garlic,
  strawberry (regrows). Summer: tomato (regrows), blueberry (regrows), melon, hot pepper (regrows), corn (regrows,
  summer+fall), wheat (summer+fall), sunflower. Fall: pumpkin, cranberries (regrows), eggplant (regrows), yam,
  bok choy, grape (regrows). (*radish kept in spring for the existing tutorial.) Days, prices and sprite indices are
  taken from the early data where it exists and recorded in `CropCatalog`; seed prices sit at roughly half the
  single-harvest sale value; regrowing crops cost more.
- **D2 — Regrowing crops.** `CropDefinition.RegrowDays`: after a harvest the plant returns to its last stage minus
  one and needs that many watered days. Scythe and hand harvest behave the same.
- **D3 — Quality** is out of scope for M2 (no star quality); revisit with fertiliser.
- **D4 — Forage.** Each public map has forage spawn tiles (from `Paths` or a data list); at each day rollover a few
  seasonal forage items (spring onion, leek, dandelion, daffodil; summer: grape, spice berry, sweet pea; fall:
  blackberry, hazelnut, wild plum, mushroom; winter: winter root, crystal fruit, snow yam) appear as pick-up nodes
  (ADR-021 node kind `forage`), giving foraging XP (ADR-023). Ids 800–899.
- **D5 — Shops.** The seed shop sells the current season's seeds only (ADR-018 rule); prices move to `ShopCatalog`
  data per season.
- **D6 — Client.** Crop sprites read from the sheet by the indices recorded in `CropCatalog`; no code per crop.

## Consequences

Balancing is data; changes need no ADR. Tests assert every crop has art indices inside the sheet and a season.
