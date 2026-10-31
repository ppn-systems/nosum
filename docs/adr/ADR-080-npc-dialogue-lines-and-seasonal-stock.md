# ADR-080: NPC dialogue lines and seasonal stock

**Status:** Proposed

Amends [ADR-043](ADR-043-npc-merchants-and-the-menu-split.md) (the merchants' dialog and greeting, D3 and D7) and [ADR-028](ADR-028-farm-buildings-and-animals.md) (the carpenter's page).

## Context

Each merchant had one greeting, two alternating talk lines and a season or weather variant of the greeting. The dialog box offered its actions, but the lines
were thin and the shop stock only rotated for seeds (ADR-018) and seasonal furniture (ADR-073). The carpenter's buildings and animals were on the Build
action only; the Buy page showed hay and milk pails. This ADR upgrades that content with the mechanisms that already exist. It adds no screen, no packet and no
persisted field.

## Decision

- **D1 — Stock rotates by season at the counter.** A `ShopOffer` may name the seasons it is in stock (`Sold`, a `SeasonSet`); no name means every season.
  `ShopRules.IsInSeason(shopId, itemId, calendar, now)` checks the counter's rotation first, then the item's own rule (seeds, seasonal furniture). The
  server's purchase (`ShopService`) and the shop page both ask it, so the page never offers what the server refuses. The straw hat is in stock in spring and
  summer, the cap in fall and winter. Tools, ore, hay, milk pails, shirts, trousers and furniture stay year-round.
- **D2 — Sixteen lines per merchant, chosen by the moment.** Each merchant has sixteen lines, `Npc_{Id}_Line_{nn}` in both `Language.resx` and
  `Language.en.resx`. A line may be tagged with the moments it fits, in four groups: season; weather (clear, wet for rain and storm, snow); time of day
  (morning from five, day from eleven, evening from five in the afternoon through the night); progress. A line fits when every group it is tagged in has the
  moment's value. Untagged lines fit any moment, so every merchant always has something to say. Among the fitting lines, the line is the one at
  `FNV-1a(merchant id, game day) mod count`, and each Talk moves on by one place. The same day and moment always give the same line; there is no random source.
  This replaces the `Greeting`, `Greeting_*` and `Talk1`/`Talk2` keys and extends the greeting mechanism of issue #130.
- **D3 — Progress from what the client already holds.** *Newcomer*: no quest is finished (`QuestChannel`). *Harvested*: the first harvest quest is finished
  (`QuestCatalog.FirstHarvest`). *DeepMiner*: the deepest mine floor is ten or more (`MineChannel.DeepestFloor`). `MineChannel` now reads the mine state
  when the player joins. "First visit" is approximated by *Newcomer*: the server keeps no record of a visit to a merchant, and adding one would be a new
  persisted system.
- **D4 — The box offers what the merchant supports, and nothing more.** The dialog box already shows one button for each action in the merchant's
  `NpcActions` (Buy, Sell, Build, Upgrade, Goals, Talk) before Leave. No action is added, and the box keeps the `GamePanel` frame of `docs/design/ui-standard.md`.
- **D5 — The carpenter's Buy page sells the buildings and animals.** The carpenter's shop page shows the building orders and the animal sections under the
  supplies (`CarpenterSections`, ADR-028). The Build action still opens the same sections on their own. The blacksmith's Buy page is unchanged.
- **D6 — Text is never on the wire.** Lines are keys; the server sends nothing new. `NosumProtocol.Version` does not change.

## Alternatives considered

- **A random line on each visit.** Rejected: a merchant would change the line as the player reopens the box, and the choice could not be tested.
- **The line chosen on the server.** Rejected: the text is client-only, and a server choice would put display text or a line number on the wire.
- **A visit log for a true "first visit".** Rejected for now: a new persisted record needs its own decision.
- **Season on the item instead of on the offer.** Rejected: one item may be stocked all year at one counter and only in one season at another.

## Consequences

- 112 lines in each language. Vietnamese is the default; the English lines follow the same index, so the two languages say the same thing at the same place.
- Shop stock changes with the season for the clothing counter, in addition to seeds and furniture.
- The client reads the mine state on join; one request more per join.
- Tests: `NpcGreetingTests` (pool size, text in both languages, the fit rule, determinism, the conversation order), `NpcDialogViewModelTests` (progress reaches
  the choice), `ShopRulesTests` (rotation, every counter has stock in every season), `ShopServiceTests` and `SocialWireTests` buy year-round items.

## Revisit when

- The server keeps a visit record, so *Newcomer* can mean a true first visit.
- A second counter needs a rotation that is not by season.
- A merchant needs more than sixteen lines, or the lines are re-written in a new language.
